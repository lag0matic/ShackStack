from __future__ import annotations

import base64
import json
import sys
import threading
from dataclasses import asdict
from datetime import datetime
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image
from scipy.signal import resample_poly

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from modes.wefax_engine_proto import WefaxDecoderPrototype

TARGET_SR = 48_000
SAVE_ROOT = Path.home() / "ShackStack" / "wefax"
SAVE_ROOT.mkdir(parents=True, exist_ok=True)


def emit(payload: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(payload, separators=(",", ":")) + "\n")
    sys.stdout.flush()


class WefaxWorker:
    def __init__(self) -> None:
        self._decoder: WefaxDecoderPrototype | None = None
        self._mode_label = "IOC 576 / 120 LPM"
        self._ioc = 576
        self._lpm = 120
        self._frequency_label = "NOAA Atlantic 12750.0 kHz USB-D"
        self._manual_slant = 0
        self._manual_offset = 0
        self._running = False
        self._line_count = 0
        self._rows: list[np.ndarray] = []
        self._active_image_path: Path | None = None
        self._last_status = "WeFAX worker idle"
        self._last_offset = 0
        self._last_start_conf = 0.0
        self._last_stop_conf = 0.0
        self._lock = threading.Lock()
        self._init_decoder()

    def _init_decoder(self) -> None:
        if self._decoder is not None:
            try:
                self._decoder.stop()
            except Exception:
                pass

        self._decoder = WefaxDecoderPrototype(
            sample_rate=TARGET_SR,
            lpm=self._lpm,
            ioc=self._ioc,
            image_callback=self._on_image_complete,
            line_callback=self._on_line,
            status_callback=self._on_status,
            telemetry_callback=self._on_telemetry,
        )

    def configure(self, payload: dict[str, Any]) -> None:
        self._mode_label = str(payload.get("modeLabel") or self._mode_label)
        self._ioc = int(payload.get("ioc") or self._ioc)
        self._lpm = int(payload.get("lpm") or self._lpm)
        self._frequency_label = str(payload.get("frequencyLabel") or self._frequency_label)
        self._manual_slant = int(payload.get("manualSlant") or self._manual_slant)
        self._manual_offset = int(payload.get("manualOffset") or self._manual_offset)
        self._line_count = 0
        self._rows = []
        self._active_image_path = None
        self._init_decoder()
        self._emit_telemetry("Configured WeFAX receiver")
        self._emit_image("No WeFAX image captured yet", None)

    def set_manual_slant(self, manual_slant: int) -> None:
        self._manual_slant = int(np.clip(manual_slant, -200, 200))
        self._rerender_preview()
        self._emit_telemetry(self._last_status)

    def set_manual_offset(self, manual_offset: int) -> None:
        self._manual_offset = int(np.clip(manual_offset, -1200, 1200))
        self._rerender_preview()
        self._emit_telemetry(self._last_status)

    def start(self, force_now: bool = False) -> None:
        if self._decoder is None:
            self._init_decoder()
        assert self._decoder is not None

        self._line_count = 0
        self._rows = []
        self._active_image_path = SAVE_ROOT / f"wefax_{datetime.now():%Y%m%d_%H%M%S}_{self._ioc}_{self._lpm}.png"
        self._decoder.reset()
        self._decoder.start()
        if force_now:
            self._decoder.force_start()
            self._last_status = "Forced WeFAX image start"
        self._running = True
        self._emit_telemetry("Receiving WeFAX" if not force_now else "Receiving WeFAX (Start Now)")

    def stop(self) -> None:
        if self._decoder is not None:
            self._decoder.stop()
        self._running = False
        self._emit_telemetry("WeFAX receiver stopped")

    def reset(self) -> None:
        if self._decoder is not None:
            self._decoder.reset()
        self._running = False
        self._line_count = 0
        self._rows = []
        self._active_image_path = None
        self._last_status = "WeFAX receiver reset"
        self._emit_telemetry(self._last_status)
        self._emit_image("No WeFAX image captured yet", None)

    def shutdown(self) -> None:
        self.stop()

    def push_audio(self, sample_rate: int, channels: int, encoded_samples: str) -> None:
        if not self._running or self._decoder is None:
            return
        if sample_rate <= 0 or channels <= 0:
            self._emit_telemetry("Invalid audio format from host")
            return

        raw = base64.b64decode(encoded_samples.encode("ascii"))
        samples = np.frombuffer(raw, dtype=np.float32)
        if channels > 1 and samples.size >= channels:
            usable = (samples.size // channels) * channels
            if usable == 0:
                return
            samples = samples[:usable].reshape(-1, channels)[:, 0]
        elif channels > 1:
            return
        if sample_rate != TARGET_SR:
            samples = resample_poly(samples, TARGET_SR, sample_rate).astype(np.float32, copy=False)
        self._decoder.push_samples(samples)

    def _on_status(self, state: str) -> None:
        mapping = {
            "idle": "WeFAX idle",
            "wait_start": "Waiting for WeFAX start tones",
            "phasing": "Aligning phasing and auto slant",
            "image": "Receiving WeFAX image",
            "stop": "WeFAX image complete",
        }
        self._last_status = mapping.get(state, state)
        self._emit_telemetry(self._last_status)

    def _on_telemetry(self, telemetry: Any) -> None:
        self._last_offset = int(getattr(telemetry, "aligned_offset", 0))
        self._last_start_conf = float(getattr(telemetry, "start_confidence", 0.0))
        self._last_stop_conf = float(getattr(telemetry, "stop_confidence", 0.0))
        self._emit_telemetry(self._last_status)

    def _on_line(self, line: np.ndarray) -> None:
        with self._lock:
            self._rows.append(np.asarray(line, dtype=np.uint8))
            self._line_count += 1
            if self._active_image_path is None:
                self._active_image_path = SAVE_ROOT / f"wefax_{datetime.now():%Y%m%d_%H%M%S}_{self._ioc}_{self._lpm}.png"
            if self._line_count == 1 or (self._line_count % 8) == 0:
                self._save_preview_image()
        self._emit_telemetry(self._last_status)

    def _on_image_complete(self, image: Image.Image) -> None:
        if self._active_image_path is None:
            self._active_image_path = SAVE_ROOT / f"wefax_{datetime.now():%Y%m%d_%H%M%S}_{self._ioc}_{self._lpm}.png"
        self._save_preview_image()
        self._emit_image(f"WeFAX image complete: {self._active_image_path.name}", str(self._active_image_path))

    def _save_preview_image(self) -> None:
        if not self._rows or self._active_image_path is None:
            return
        arr = self._render_rows()
        Image.fromarray(arr, mode="L").save(self._active_image_path)
        self._emit_image(f"Decoded {self._line_count} lines", str(self._active_image_path))

    def _rerender_preview(self) -> None:
        if not self._rows or self._active_image_path is None:
            return
        self._save_preview_image()

    def _render_rows(self) -> np.ndarray:
        rendered: list[np.ndarray] = []
        for index, row in enumerate(self._rows):
            shift = self._manual_offset
            if self._manual_slant != 0 and index > 0:
                shift += int(round(index * (self._manual_slant / 1000.0)))
            rendered.append(np.roll(row, shift) if shift else row)
        return np.vstack(rendered)

    def _emit_telemetry(self, status: str) -> None:
        emit({
            "type": "telemetry",
            "isRunning": self._running,
            "status": status,
            "activeWorker": "Python WeFAX sidecar",
            "linesReceived": self._line_count,
            "alignedOffset": self._last_offset,
            "startConfidence": round(self._last_start_conf, 3),
            "stopConfidence": round(self._last_stop_conf, 3),
            "modeLabel": self._mode_label,
            "frequencyLabel": self._frequency_label,
        })

    def _emit_image(self, status: str, image_path: str | None) -> None:
        emit({
            "type": "image",
            "status": status,
            "imagePath": image_path,
        })


def main() -> None:
    worker = WefaxWorker()
    worker._emit_telemetry("Python WeFAX worker ready")
    worker._emit_image("No WeFAX image captured yet", None)

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            payload = json.loads(line)
            msg_type = payload.get("type")
            if msg_type == "configure":
                worker.configure(payload)
            elif msg_type == "start":
                worker.start(force_now=False)
            elif msg_type == "start_now":
                worker.start(force_now=True)
            elif msg_type == "stop":
                worker.stop()
            elif msg_type == "reset":
                worker.reset()
            elif msg_type == "manual_slant":
                worker.set_manual_slant(int(payload.get("manualSlant") or 0))
            elif msg_type == "manual_offset":
                worker.set_manual_offset(int(payload.get("manualOffset") or 0))
            elif msg_type == "audio":
                worker.push_audio(
                    int(payload.get("sampleRate") or TARGET_SR),
                    int(payload.get("channels") or 1),
                    str(payload.get("samples") or ""),
                )
            elif msg_type == "shutdown":
                worker.shutdown()
                break
        except Exception as ex:
            worker._emit_telemetry(f"WeFAX worker error: {ex}")


if __name__ == "__main__":
    main()
