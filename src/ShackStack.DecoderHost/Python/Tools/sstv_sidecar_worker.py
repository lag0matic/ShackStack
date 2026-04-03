from __future__ import annotations

import base64
import json
import math
import sys
import threading
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image
from scipy.signal import hilbert, resample_poly


WORKING_SAMPLE_RATE = 12_000
VIS_FRAME_MS = 10
VIS_FRAME_SAMPLES = int(WORKING_SAMPLE_RATE * VIS_FRAME_MS / 1000)
FREQ_VIS_BIT1 = 1100.0
FREQ_SYNC = 1200.0
FREQ_VIS_BIT0 = 1300.0
FREQ_BLACK = 1500.0
FREQ_VIS_START = 1900.0
FREQ_WHITE = 2300.0

SAVE_ROOT = Path.home() / "ShackStack" / "sstv"
SAVE_ROOT.mkdir(parents=True, exist_ok=True)
AUTO_MODE_LABEL = "Auto Detect"


@dataclass(frozen=True)
class SstvModeProfile:
    name: str
    vis_code: int
    width: int
    height: int
    sync_ms: float
    scan_ms: float
    gap_ms: float
    color_seq: tuple[str, ...]
    supported_decode: bool
    family: str = "rgb"
    aux_scan_ms: float = 0.0
    porch_ms: float = 0.0
    sync_porch_ms: float = 0.0
    pixel_ms: float = 0.0

    @property
    def line_ms(self) -> float:
        if self.family == "robot36":
            return self.sync_ms + self.sync_porch_ms + self.scan_ms + self.gap_ms + self.porch_ms + self.aux_scan_ms
        if self.family == "pd":
            pair_ms = self.sync_ms + self.porch_ms + (self.pixel_ms * self.width * 4)
            return pair_ms / 2.0
        return self.sync_ms + (self.gap_ms * (1 + len(self.color_seq))) + (self.scan_ms * len(self.color_seq))


MODE_PROFILES: dict[int, SstvModeProfile] = {
    0x2C: SstvModeProfile("Martin M1", 0x2C, 320, 256, 4.862, 146.432, 0.572, ("g", "b", "r"), True, "martin"),
    0x28: SstvModeProfile("Martin M2", 0x28, 160, 256, 4.862, 73.216, 0.572, ("g", "b", "r"), True, "martin"),
    0x3C: SstvModeProfile("Scottie 1", 0x3C, 320, 256, 9.0, 136.74, 1.5, ("g", "b", "r"), True, "scottie"),
    0x38: SstvModeProfile("Scottie 2", 0x38, 160, 256, 9.0, 86.564, 1.5, ("g", "b", "r"), True, "scottie"),
    0x08: SstvModeProfile("Robot 36", 0x08, 320, 240, 9.0, 88.0, 4.5, ("y", "uv"), True, "robot36", aux_scan_ms=44.0, porch_ms=1.5, sync_porch_ms=3.0),
    0x5F: SstvModeProfile("PD 120", 0x5F, 640, 496, 20.0, 0.0, 0.0, ("pd",), True, "pd", porch_ms=2.08, pixel_ms=0.19),
}


def emit(payload: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(payload, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def tone_power(block: np.ndarray, sample_rate: int, freq_hz: float) -> float:
    if block.size == 0:
        return 0.0
    window = np.hanning(block.size).astype(np.float32)
    weighted = block.astype(np.float32, copy=False) * window
    n = np.arange(weighted.size, dtype=np.float32)
    osc = np.exp(-2j * np.pi * freq_hz * n / sample_rate)
    value = np.dot(weighted.astype(np.complex64), osc.astype(np.complex64))
    return float(np.abs(value))


def dominant_tone_hz(block: np.ndarray, sample_rate: int, min_hz: float = 1000.0, max_hz: float = 2400.0) -> tuple[float, float]:
    if block.size == 0:
        return 0.0, 0.0
    window = np.hanning(block.size).astype(np.float32)
    weighted = block.astype(np.float32, copy=False) * window
    fft = np.fft.rfft(weighted)
    freqs = np.fft.rfftfreq(weighted.size, 1.0 / sample_rate)
    mask = (freqs >= min_hz) & (freqs <= max_hz)
    if not np.any(mask):
        return 0.0, 0.0
    band = np.abs(fft[mask])
    idx = int(np.argmax(band))
    dom_freq = float(freqs[mask][idx])
    dom_power = float(band[idx])
    return dom_freq, dom_power


def classify_vis_frame(block: np.ndarray) -> str | None:
    candidates = {
        "1100": tone_power(block, WORKING_SAMPLE_RATE, FREQ_VIS_BIT1),
        "1200": tone_power(block, WORKING_SAMPLE_RATE, FREQ_SYNC),
        "1300": tone_power(block, WORKING_SAMPLE_RATE, FREQ_VIS_BIT0),
        "1900": tone_power(block, WORKING_SAMPLE_RATE, FREQ_VIS_START),
    }
    label, power = max(candidates.items(), key=lambda item: item[1])
    total = sum(candidates.values())
    if power <= 0.0 or total <= 0.0:
        return None
    if (power / total) < 0.42:
        return None
    return label


def freq_to_luma(freqs: np.ndarray) -> np.ndarray:
    if freqs.size == 0:
        return np.zeros(0, dtype=np.uint8)
    values = np.clip((freqs - FREQ_BLACK) / (FREQ_WHITE - FREQ_BLACK), 0.0, 1.0)
    return np.round(values * 255.0).astype(np.uint8)


class MartinM1Session:
    def __init__(self, profile: SstvModeProfile, start_sample: int) -> None:
        self.profile = profile
        self.start_sample = start_sample
        self.image = Image.new("RGB", (profile.width, profile.height), (0, 0, 0))
        self._raw_rows: list[np.ndarray | None] = [None] * profile.height
        self.line_index = 0
        self.last_saved_line = -1
        self._next_line_start = start_sample
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        self.image_path = SAVE_ROOT / f"sstv_{timestamp}_{profile.name.lower().replace(' ', '_')}.png"
        self.completed = False
        self.manual_slant = 0
        self.manual_offset = 0

    def refine_line_start(self, samples: np.ndarray, expected_start: int) -> int:
        sync_samples = max(8, int(round(self.profile.sync_ms * WORKING_SAMPLE_RATE / 1000.0)))
        sync_anchor_offset = self._sync_anchor_offset(sync_samples)
        best_offset = 0
        best_score = -1.0
        for offset in range(-48, 49, 6):
            candidate = expected_start + offset
            sync_start = candidate + sync_anchor_offset
            if sync_start < 0 or sync_start + sync_samples > samples.size:
                continue
            block = samples[sync_start:sync_start + sync_samples]
            score = tone_power(block, WORKING_SAMPLE_RATE, FREQ_SYNC)
            if score > best_score:
                best_score = score
                best_offset = offset
        return max(0, expected_start + best_offset)

    def decode_available_lines(self, samples: np.ndarray) -> tuple[int, str | None]:
        if self.profile.family == "robot36":
            return self._decode_robot36_lines(samples)
        if self.profile.family == "pd":
            return self._decode_pd_lines(samples)

        updated = 0
        status: str | None = None
        line_samples = self._line_samples()
        sync_samples = int(round(self.profile.sync_ms * WORKING_SAMPLE_RATE / 1000.0))
        gap_samples = max(1, int(round(self.profile.gap_ms * WORKING_SAMPLE_RATE / 1000.0)))
        scan_samples = int(round(self.profile.scan_ms * WORKING_SAMPLE_RATE / 1000.0))

        while self.line_index < self.profile.height:
            expected_start = self._next_line_start
            expected_end = expected_start + line_samples
            if expected_end > samples.size:
                break

            line_start = self.refine_line_start(samples, expected_start)
            if line_start + line_samples > samples.size:
                break

            line_error = line_start - expected_start

            channels: dict[str, np.ndarray] = {}
            for color, start_pos in self._channel_layout(line_start, sync_samples, gap_samples, scan_samples):
                segment = samples[start_pos:start_pos + scan_samples]
                channels[color] = self._decode_channel(segment)

            self._write_line(channels)
            self.line_index += 1
            drift_correction = int(round(line_error * 0.35))
            drift_correction = max(-24, min(24, drift_correction))
            self._next_line_start = line_start + line_samples + drift_correction
            updated += 1

            if self.line_index == self.profile.height:
                self.image.save(self.image_path)
                self.completed = True
                status = f"Image complete: {self.image_path.name}"
            elif (self.line_index - self.last_saved_line) >= 8:
                self.image.save(self.image_path)
                self.last_saved_line = self.line_index
                status = f"Decoded {self.line_index}/{self.profile.height} lines"

        return updated, status

    def _line_samples(self) -> int:
        if self.profile.family == "scottie":
            total_ms = (self.profile.scan_ms * 3.0) + (self.profile.gap_ms * 3.0) + self.profile.sync_ms
            return int(round(total_ms * WORKING_SAMPLE_RATE / 1000.0))
        if self.profile.family == "robot36":
            total_ms = self.profile.sync_ms + self.profile.sync_porch_ms + self.profile.scan_ms + self.profile.gap_ms + self.profile.porch_ms + self.profile.aux_scan_ms
            return int(round(total_ms * WORKING_SAMPLE_RATE / 1000.0))
        if self.profile.family == "pd":
            total_ms = self.profile.sync_ms + self.profile.porch_ms + (self.profile.pixel_ms * self.profile.width * 4.0)
            return int(round(total_ms * WORKING_SAMPLE_RATE / 1000.0))
        return int(round(self.profile.line_ms * WORKING_SAMPLE_RATE / 1000.0))

    def _sync_anchor_offset(self, sync_samples: int) -> int:
        if self.profile.family == "scottie":
            gap_samples = max(1, int(round(self.profile.gap_ms * WORKING_SAMPLE_RATE / 1000.0)))
            scan_samples = int(round(self.profile.scan_ms * WORKING_SAMPLE_RATE / 1000.0))
            return (scan_samples * 2) + (gap_samples * 2)
        return 0

    def _channel_layout(self, line_start: int, sync_samples: int, gap_samples: int, scan_samples: int) -> list[tuple[str, int]]:
        if self.profile.family == "scottie":
            g_start = line_start
            b_start = g_start + scan_samples + gap_samples
            r_start = b_start + scan_samples + gap_samples + sync_samples + gap_samples
            return [("g", g_start), ("b", b_start), ("r", r_start)]

        first_start = line_start + sync_samples + gap_samples
        second_start = first_start + scan_samples + gap_samples
        third_start = second_start + scan_samples + gap_samples
        return [("g", first_start), ("b", second_start), ("r", third_start)]

    def _decode_channel(self, segment: np.ndarray) -> np.ndarray:
        if segment.size < 8:
            return np.zeros(self.profile.width, dtype=np.uint8)
        analytic = hilbert(segment.astype(np.float32, copy=False))
        phase = np.unwrap(np.angle(analytic))
        inst_freq = np.diff(phase) * WORKING_SAMPLE_RATE / (2 * np.pi)
        if inst_freq.size == 0:
            return np.zeros(self.profile.width, dtype=np.uint8)
        inst_freq = np.clip(inst_freq, 1000.0, 2500.0)
        kernel = np.array([0.08, 0.24, 0.36, 0.24, 0.08], dtype=np.float32)
        inst_freq = np.convolve(inst_freq, kernel, mode="same")
        pixels = np.zeros(self.profile.width, dtype=np.uint8)
        for idx in range(self.profile.width):
            start = int(round(idx * inst_freq.size / self.profile.width))
            end = int(round((idx + 1) * inst_freq.size / self.profile.width))
            if end <= start:
                end = min(inst_freq.size, start + 1)
            pixels[idx] = freq_to_luma(inst_freq[start:end].mean(keepdims=True))[0]
        pixels = self._smooth_pixels(pixels)
        pixels = self._destripe_pixels(pixels)
        return pixels

    def _decode_robot36_lines(self, samples: np.ndarray) -> tuple[int, str | None]:
        updated = 0
        status: str | None = None
        line_samples = self._line_samples()
        sync_samples = max(8, int(round(self.profile.sync_ms * WORKING_SAMPLE_RATE / 1000.0)))
        porch_samples = max(1, int(round(self.profile.sync_porch_ms * WORKING_SAMPLE_RATE / 1000.0)))
        y_scan_samples = int(round(self.profile.scan_ms * WORKING_SAMPLE_RATE / 1000.0))
        gap_samples = max(1, int(round(self.profile.gap_ms * WORKING_SAMPLE_RATE / 1000.0)))
        chroma_porch_samples = max(1, int(round(self.profile.porch_ms * WORKING_SAMPLE_RATE / 1000.0)))
        chroma_scan_samples = int(round(self.profile.aux_scan_ms * WORKING_SAMPLE_RATE / 1000.0))
        cb_hold = np.full(self.profile.width, 128, dtype=np.uint8)
        cr_hold = np.full(self.profile.width, 128, dtype=np.uint8)

        while self.line_index < self.profile.height:
            expected_start = self._next_line_start
            expected_end = expected_start + line_samples
            if expected_end > samples.size:
                break

            line_start = self.refine_line_start(samples, expected_start)
            if line_start + line_samples > samples.size:
                break

            line_error = line_start - expected_start
            y_start = line_start + sync_samples + porch_samples
            c_start = y_start + y_scan_samples + gap_samples + chroma_porch_samples

            y_segment = samples[y_start:y_start + y_scan_samples]
            c_segment = samples[c_start:c_start + chroma_scan_samples]
            y_pixels = self._decode_channel(y_segment)
            c_pixels = self._decode_channel(c_segment)

            if (self.line_index % 2) == 0:
                cr_hold = c_pixels
            else:
                cb_hold = c_pixels

            rgb_row = self._ycbcr_row_to_rgb(y_pixels, cb_hold, cr_hold)
            self._write_rgb_row(self.line_index, rgb_row)
            self.line_index += 1
            drift_correction = int(round(line_error * 0.35))
            drift_correction = max(-24, min(24, drift_correction))
            self._next_line_start = line_start + line_samples + drift_correction
            updated += 1

            if self.line_index == self.profile.height:
                self.image.save(self.image_path)
                self.completed = True
                status = f"Image complete: {self.image_path.name}"
            elif (self.line_index - self.last_saved_line) >= 8:
                self.image.save(self.image_path)
                self.last_saved_line = self.line_index
                status = f"Decoded {self.line_index}/{self.profile.height} lines"

        return updated, status

    def _decode_pd_lines(self, samples: np.ndarray) -> tuple[int, str | None]:
        updated = 0
        status: str | None = None
        pair_samples = self._line_samples()
        sync_samples = max(8, int(round(self.profile.sync_ms * WORKING_SAMPLE_RATE / 1000.0)))
        porch_samples = max(1, int(round(self.profile.porch_ms * WORKING_SAMPLE_RATE / 1000.0)))
        segment_samples = int(round(self.profile.pixel_ms * self.profile.width * WORKING_SAMPLE_RATE / 1000.0))

        while self.line_index < self.profile.height - 1:
            expected_start = self._next_line_start
            expected_end = expected_start + pair_samples
            if expected_end > samples.size:
                break

            line_start = self.refine_line_start(samples, expected_start)
            if line_start + pair_samples > samples.size:
                break

            line_error = line_start - expected_start
            y0_start = line_start + sync_samples + porch_samples
            cr_start = y0_start + segment_samples
            cb_start = cr_start + segment_samples
            y1_start = cb_start + segment_samples

            y0 = self._decode_channel(samples[y0_start:y0_start + segment_samples])
            cr = self._decode_channel(samples[cr_start:cr_start + segment_samples])
            cb = self._decode_channel(samples[cb_start:cb_start + segment_samples])
            y1 = self._decode_channel(samples[y1_start:y1_start + segment_samples])

            self._write_rgb_row(self.line_index, self._ycbcr_row_to_rgb(y0, cb, cr))
            self._write_rgb_row(self.line_index + 1, self._ycbcr_row_to_rgb(y1, cb, cr))
            self.line_index += 2
            drift_correction = int(round(line_error * 0.35))
            drift_correction = max(-24, min(24, drift_correction))
            self._next_line_start = line_start + pair_samples + drift_correction
            updated += 2

            if self.line_index >= self.profile.height:
                self.image.save(self.image_path)
                self.completed = True
                status = f"Image complete: {self.image_path.name}"
            elif (self.line_index - self.last_saved_line) >= 8:
                self.image.save(self.image_path)
                self.last_saved_line = self.line_index
                status = f"Decoded {self.line_index}/{self.profile.height} lines"

        return updated, status

    @staticmethod
    def _smooth_pixels(pixels: np.ndarray) -> np.ndarray:
        if pixels.size < 5:
            return pixels

        padded = np.pad(pixels.astype(np.float32), (2, 2), mode="edge")
        kernel = np.array([0.1, 0.2, 0.4, 0.2, 0.1], dtype=np.float32)
        smoothed = np.convolve(padded, kernel, mode="valid")
        return np.clip(np.round(smoothed), 0, 255).astype(np.uint8)

    @staticmethod
    def _destripe_pixels(pixels: np.ndarray) -> np.ndarray:
        if pixels.size < 3:
            return pixels

        corrected = pixels.copy()
        for idx in range(1, pixels.size - 1):
            left = int(corrected[idx - 1])
            center = int(corrected[idx])
            right = int(corrected[idx + 1])
            if abs(center - left) > 90 and abs(center - right) > 90 and abs(left - right) < 45:
                corrected[idx] = np.uint8((left + right) // 2)
        return corrected

    def _write_line(self, channels: dict[str, np.ndarray]) -> None:
        if self.line_index >= self.profile.height:
            return
        row = np.zeros((self.profile.width, 3), dtype=np.uint8)
        row[:, 0] = channels.get("r", np.zeros(self.profile.width, dtype=np.uint8))
        row[:, 1] = channels.get("g", np.zeros(self.profile.width, dtype=np.uint8))
        row[:, 2] = channels.get("b", np.zeros(self.profile.width, dtype=np.uint8))
        self._write_rgb_row(self.line_index, row)

    def _write_rgb_row(self, line_index: int, row: np.ndarray) -> None:
        if line_index >= self.profile.height:
            return
        self._raw_rows[line_index] = row.copy()
        adjusted = self._apply_alignment(row, line_index)
        self.image.paste(Image.fromarray(adjusted.reshape(1, self.profile.width, 3), mode="RGB"), (0, line_index))

    def set_manual_alignment(self, manual_slant: int, manual_offset: int) -> None:
        self.manual_slant = int(np.clip(manual_slant, -200, 200))
        self.manual_offset = int(np.clip(manual_offset, -400, 400))
        self._rebuild_image()

    def _apply_alignment(self, row: np.ndarray, line_index: int) -> np.ndarray:
        shift = self.manual_offset
        if self.manual_slant != 0 and line_index > 0:
            shift += int(round(line_index * (self.manual_slant / 1000.0)))
        if shift == 0:
            return row
        return np.roll(row, shift, axis=0)

    def _rebuild_image(self) -> None:
        rebuilt = Image.new("RGB", (self.profile.width, self.profile.height), (0, 0, 0))
        for line_index, row in enumerate(self._raw_rows):
            if row is None:
                continue
            adjusted = self._apply_alignment(row, line_index)
            rebuilt.paste(Image.fromarray(adjusted.reshape(1, self.profile.width, 3), mode="RGB"), (0, line_index))
        self.image = rebuilt
        self.image.save(self.image_path)

    @staticmethod
    def _ycbcr_row_to_rgb(y: np.ndarray, cb: np.ndarray, cr: np.ndarray) -> np.ndarray:
        y_f = y.astype(np.float32)
        cb_f = cb.astype(np.float32) - 128.0
        cr_f = cr.astype(np.float32) - 128.0
        r = np.clip(y_f + (1.402 * cr_f), 0, 255)
        g = np.clip(y_f - (0.344136 * cb_f) - (0.714136 * cr_f), 0, 255)
        b = np.clip(y_f + (1.772 * cb_f), 0, 255)
        return np.stack(
            [
                np.round(r).astype(np.uint8),
                np.round(g).astype(np.uint8),
                np.round(b).astype(np.uint8),
            ],
            axis=1)


class SstvReceiver:
    def __init__(self) -> None:
        self._lock = threading.RLock()
        self._running = False
        self._configured_mode = AUTO_MODE_LABEL
        self._configured_frequency = "14.230 MHz USB"
        self._manual_slant = 0
        self._manual_offset = 0
        self._detected_mode = AUTO_MODE_LABEL
        self._signal_level_percent = 0
        self._status = "Python SSTV worker ready"
        self._samples = np.zeros(0, dtype=np.float32)
        self._vis_frame_cursor = 0
        self._vis_labels: list[str | None] = []
        self._last_vis_detection_frame = -10_000
        self._session: MartinM1Session | None = None
        self._latest_image_path: str | None = None
        self._fallback_attempted = False
        self._configured_session_started = False
        self._last_force_probe_sample = 0

    def telemetry_payload(self) -> dict[str, Any]:
        with self._lock:
            return {
                "type": "telemetry",
                "isRunning": bool(self._running),
                "status": self._status,
                "activeWorker": "Python SSTV receiver",
                "signalLevelPercent": int(self._signal_level_percent),
                "detectedMode": self._detected_mode,
            }

    def emit_telemetry(self, status: str | None = None) -> None:
        if status is not None:
            with self._lock:
                self._status = status
        emit(self.telemetry_payload())

    def emit_image(self, status: str, image_path: str | None = None) -> None:
        with self._lock:
            self._latest_image_path = image_path
        emit({"type": "image", "status": status, "imagePath": image_path})

    def configure(self, message: dict[str, Any]) -> None:
        with self._lock:
            self._configured_mode = str(message.get("mode", self._configured_mode))
            self._configured_frequency = str(message.get("frequencyLabel", self._configured_frequency))
            self._manual_slant = int(message.get("manualSlant", self._manual_slant))
            self._manual_offset = int(message.get("manualOffset", self._manual_offset))
            self._detected_mode = self._configured_mode
        self.emit_telemetry(f"Configured for {self._configured_mode} on {self._configured_frequency}")

    def set_manual_alignment(self, manual_slant: int, manual_offset: int) -> None:
        with self._lock:
            self._manual_slant = int(np.clip(manual_slant, -200, 200))
            self._manual_offset = int(np.clip(manual_offset, -400, 400))
            session = self._session
        if session is not None:
            session.set_manual_alignment(self._manual_slant, self._manual_offset)
            self.emit_image(f"Adjusted preview: {Path(session.image_path).name}", str(session.image_path))
        self.emit_telemetry()

    def start(self) -> None:
        with self._lock:
            self._running = True
            self._status = "Listening for SSTV VIS"
        self.emit_telemetry()
        self.emit_image(self._latest_image_path and f"Last image: {Path(self._latest_image_path).name}" or "Listening for VIS / sync tones", self._latest_image_path)

    def stop(self) -> None:
        with self._lock:
            self._running = False
            self._status = "SSTV receiver stopped"
        self.emit_telemetry()

    def reset(self) -> None:
        with self._lock:
            self._signal_level_percent = 0
            self._status = "SSTV session reset"
            self._samples = np.zeros(0, dtype=np.float32)
            self._vis_frame_cursor = 0
            self._vis_labels = []
            self._last_vis_detection_frame = -10_000
            self._session = None
            self._latest_image_path = None
            self._detected_mode = self._configured_mode
            self._fallback_attempted = False
            self._configured_session_started = False
            self._last_force_probe_sample = 0
        self.emit_telemetry()
        self.emit_image("No image captured yet", None)

    def handle_audio(self, message: dict[str, Any]) -> None:
        with self._lock:
            if not self._running:
                return

        samples_b64 = message.get("samples")
        if not isinstance(samples_b64, str) or not samples_b64:
            return

        raw = base64.b64decode(samples_b64)
        samples = np.frombuffer(raw, dtype="<f4")
        channels = int(message.get("channels", 1))
        sample_rate = int(message.get("sampleRate", 48_000))
        if channels <= 0 or sample_rate <= 0:
            self.emit_telemetry("Invalid SSTV audio format from host")
            return

        if channels > 1 and samples.size >= channels:
            usable = (samples.size // channels) * channels
            if usable == 0:
                return
            samples = samples[:usable].reshape(-1, channels).mean(axis=1)

        if samples.size == 0:
            return

        if sample_rate != WORKING_SAMPLE_RATE:
            gcd = math.gcd(sample_rate, WORKING_SAMPLE_RATE)
            up = WORKING_SAMPLE_RATE // gcd
            down = sample_rate // gcd
            samples = resample_poly(samples.astype(np.float32, copy=False), up, down).astype(np.float32, copy=False)
        else:
            samples = samples.astype(np.float32, copy=False)

        rms = float(np.sqrt(np.mean(np.square(samples))))
        level = int(max(0, min(100, round(rms * 400.0))))
        with self._lock:
            self._signal_level_percent = int(round((self._signal_level_percent * 0.82) + (level * 0.18)))
            self._samples = np.concatenate((self._samples, samples))

        self._process_vis_frames()
        self._maybe_force_start_auto()
        self._maybe_force_start_from_config()
        self._decode_session_lines()

        if self._session is None:
            dom_freq, _ = dominant_tone_hz(samples, WORKING_SAMPLE_RATE)
            if dom_freq > 0:
                self.emit_telemetry(f"Monitoring SSTV audio ({dom_freq:.0f} Hz)")
            else:
                self.emit_telemetry("Monitoring SSTV audio")

    def _process_vis_frames(self) -> None:
        while self._vis_frame_cursor + VIS_FRAME_SAMPLES <= self._samples.size:
            frame = self._samples[self._vis_frame_cursor:self._vis_frame_cursor + VIS_FRAME_SAMPLES]
            self._vis_labels.append(classify_vis_frame(frame))
            self._vis_frame_cursor += VIS_FRAME_SAMPLES
            self._maybe_detect_vis()

    def _maybe_detect_vis(self) -> None:
        labels = self._vis_labels
        frame_count = len(labels)
        if frame_count < 70:
            return

        search_start = max(self._last_vis_detection_frame + 1, frame_count - 140)
        for break_idx in range(search_start, frame_count - 55):
            if labels[break_idx] != "1200":
                continue
            if break_idx < 20 or labels[break_idx - 20:break_idx].count("1900") < 18:
                continue

            second_leader_start = break_idx + 1
            second_leader_end = second_leader_start
            while second_leader_end < frame_count and labels[second_leader_end] == "1900":
                second_leader_end += 1

            second_leader_len = second_leader_end - second_leader_start
            if second_leader_len < 18:
                continue

            for bit_offset in range(3):
                groups_start = second_leader_end + bit_offset
                groups_end = groups_start + 30
                if groups_end > frame_count:
                    continue

                groups = [labels[groups_start + (i * 3):groups_start + ((i + 1) * 3)] for i in range(10)]
                if any(len(group) < 3 for group in groups):
                    continue

                normalized_groups: list[str] = []
                valid = True
                for group in groups:
                    valid_labels = [label for label in group if label is not None]
                    if not valid_labels:
                        valid = False
                        break
                    normalized_groups.append(max(set(valid_labels), key=valid_labels.count))
                if not valid:
                    continue

                if normalized_groups[0] != "1200":
                    continue

                data_bits: list[int] = []
                for label in normalized_groups[1:8]:
                    if label not in ("1100", "1300"):
                        data_bits = []
                        break
                    data_bits.append(1 if label == "1100" else 0)
                if len(data_bits) != 7:
                    continue

                parity_label = normalized_groups[8]
                stop_label = normalized_groups[9]
                if parity_label not in ("1100", "1300") or stop_label != "1200":
                    continue

                parity_bit = 1 if parity_label == "1100" else 0
                if ((sum(data_bits) + parity_bit) % 2) != 0:
                    continue

                vis_code = 0
                for bit_index, bit in enumerate(data_bits):
                    vis_code |= (bit << bit_index)

                profile = MODE_PROFILES.get(vis_code)
                mode_name = profile.name if profile else f"VIS 0x{vis_code:02X}"
                self._detected_mode = mode_name
                self._last_vis_detection_frame = break_idx

                if profile is None:
                    self.emit_telemetry(f"Detected unsupported SSTV mode {mode_name}")
                    return

                image_start_sample = groups_end * VIS_FRAME_SAMPLES
                if profile.supported_decode:
                    self._session = MartinM1Session(profile, image_start_sample)
                    self._session.set_manual_alignment(self._manual_slant, self._manual_offset)
                    self._configured_session_started = True
                    self._latest_image_path = str(self._session.image_path)
                    self.emit_image(f"{profile.name} detected - receiving image", self._latest_image_path)
                    self.emit_telemetry(f"Detected {profile.name} VIS - decoding image")
                else:
                    self.emit_image(f"{profile.name} detected - decode support pending", None)
                    self.emit_telemetry(f"Detected {profile.name} VIS - decode support pending")
                return

    def _decode_session_lines(self) -> None:
        if self._session is None:
            return

        updated, status = self._session.decode_available_lines(self._samples)
        if status:
            image_path = str(self._session.image_path)
            self._latest_image_path = image_path
            self.emit_image(status, image_path)
            self.emit_telemetry(status)
        elif updated > 0:
            self.emit_telemetry(f"Decoded {self._session.line_index}/{self._session.profile.height} lines")

    def _maybe_force_start_auto(self) -> None:
        if self._session is not None:
            return

        if self._configured_mode != AUTO_MODE_LABEL:
            return

        min_probe_stride = WORKING_SAMPLE_RATE * 2
        if self._samples.size < (WORKING_SAMPLE_RATE * 4):
            return
        if (self._samples.size - self._last_force_probe_sample) < min_probe_stride:
            return

        candidates = [profile for profile in MODE_PROFILES.values() if profile.supported_decode]
        best_profile: SstvModeProfile | None = None
        best_start: int | None = None
        best_score = 0.0

        for profile in candidates:
            result = self._find_best_sync_start(profile)
            if result is None:
                continue

            start_sample, score = result
            if score > best_score:
                best_profile = profile
                best_start = start_sample
                best_score = score

        self._last_force_probe_sample = self._samples.size
        if best_profile is None or best_start is None or best_score <= 0.0:
            return

        self._start_configured_session(best_start, best_profile, f"Auto-detected {best_profile.name} from line sync")

    def _maybe_force_start_from_config(self) -> None:
        if self._session is not None:
            return

        if self._configured_mode == AUTO_MODE_LABEL:
            return

        min_probe_stride = WORKING_SAMPLE_RATE * 2
        if self._samples.size < (WORKING_SAMPLE_RATE * 3):
            return
        if (self._samples.size - self._last_force_probe_sample) < min_probe_stride:
            return

        profile = next((p for p in MODE_PROFILES.values() if p.name == self._configured_mode and p.supported_decode), None)
        if profile is None:
            return

        result = self._find_best_sync_start(profile)
        self._last_force_probe_sample = self._samples.size
        if result is None:
            if profile.family == "scottie":
                active_indices = np.flatnonzero(np.abs(self._samples) > 0.02)
                if active_indices.size > 0:
                    self._start_configured_session(int(active_indices[0]), profile, "Scottie forced start from configured mode")
            return

        best_start, _ = result
        self._start_configured_session(best_start, profile, f"{profile.name} forced start from line sync")

    def _find_best_sync_start(self, profile: SstvModeProfile) -> tuple[int, float] | None:
        line_samples = int(round(profile.line_ms * WORKING_SAMPLE_RATE / 1000.0))
        if profile.family == "scottie":
            line_samples = int(round(((profile.scan_ms * 3.0) + (profile.gap_ms * 3.0) + profile.sync_ms) * WORKING_SAMPLE_RATE / 1000.0))
        sync_samples = max(8, int(round(profile.sync_ms * WORKING_SAMPLE_RATE / 1000.0)))
        if profile.family == "scottie":
            gap_samples = max(1, int(round(profile.gap_ms * WORKING_SAMPLE_RATE / 1000.0)))
            scan_samples = int(round(profile.scan_ms * WORKING_SAMPLE_RATE / 1000.0))
            sync_anchor_offset = (scan_samples * 2) + (gap_samples * 2)
        else:
            sync_anchor_offset = 0
        if self._samples.size < line_samples * 12:
            return None

        active_indices = np.flatnonzero(np.abs(self._samples) > 0.02)
        if active_indices.size == 0:
            return None
        search_origin = int(active_indices[0])
        search_limit = min(self._samples.size - sync_samples - 1, search_origin + (line_samples * 2))
        if search_limit <= search_origin:
            return None

        best_start = None
        best_score = -1.0
        for candidate in range(search_origin, search_limit, 8):
            score = 0.0
            used = 0
            for line_index in range(10):
                pos = candidate + (line_index * line_samples)
                sync_pos = pos + sync_anchor_offset
                if sync_pos + sync_samples > self._samples.size:
                    break
                block = self._samples[sync_pos:sync_pos + sync_samples]
                score += tone_power(block, WORKING_SAMPLE_RATE, FREQ_SYNC)
                used += 1
            if used < 5:
                continue
            normalized = score / used
            if normalized > best_score:
                best_score = normalized
                best_start = candidate

        if best_start is None or best_score <= 0.0:
            return None

        return best_start, best_score

    def _start_configured_session(self, start_sample: int, profile: SstvModeProfile, status: str) -> None:
        if self._configured_session_started:
            return
        self._session = MartinM1Session(profile, start_sample)
        self._session.set_manual_alignment(self._manual_slant, self._manual_offset)
        self._configured_session_started = True
        self._latest_image_path = str(self._session.image_path)
        self._detected_mode = profile.name
        self.emit_image(f"{profile.name} forced start - receiving image", self._latest_image_path)
        self.emit_telemetry(status)


_receiver = SstvReceiver()


def main() -> int:
    _receiver.emit_telemetry("Python SSTV worker ready")
    _receiver.emit_image("No image captured yet", None)

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            message = json.loads(line)
        except Exception as ex:
            emit({
                "type": "telemetry",
                "isRunning": False,
                "status": f"Protocol error: {ex}",
                "activeWorker": "Python SSTV receiver",
                "signalLevelPercent": 0,
                "detectedMode": "Unknown",
            })
            continue

        msg_type = message.get("type")
        try:
            if msg_type == "configure":
                _receiver.configure(message)
            elif msg_type == "start":
                _receiver.start()
            elif msg_type == "stop":
                _receiver.stop()
            elif msg_type == "reset":
                _receiver.reset()
            elif msg_type == "manual_alignment":
                _receiver.set_manual_alignment(
                    int(message.get("manualSlant", 0)),
                    int(message.get("manualOffset", 0)),
                )
            elif msg_type == "audio":
                _receiver.handle_audio(message)
            elif msg_type == "shutdown":
                _receiver.stop()
                break
        except Exception as ex:
            payload = _receiver.telemetry_payload()
            payload["status"] = f"Worker error: {ex}"
            emit(payload)

    _receiver.stop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
