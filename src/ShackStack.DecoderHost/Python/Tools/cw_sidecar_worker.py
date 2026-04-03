from __future__ import annotations

import base64
import json
import os
import sys
import threading
from typing import Any

import numpy as np

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from modes.cw_engine_hybrid import CwHybridDecoder
from modes.cw_engine_adaptive import CwAdaptiveDecoder
from modes.cw_engine_minimal import CwMinimalDecoder
from modes.morse_decoder_ext import MorseConfig as ExternalMorseConfig
from modes.morse_decoder_ext import MorseDecoder as ExternalMorseDecoder


_lock = threading.RLock()
_decoder = None
_running = False
_config = {"pitchHz": 700, "wpm": 20, "profile": "Adaptive"}
_last_confidence = 0.0
_estimated_pitch = 700
_estimated_wpm = 20


def emit(payload: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(payload, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def worker_name_for(profile: str) -> str:
    return {
        "Minimal": "Python minimal",
        "Adaptive": "Python adaptive",
        "Hybrid": "Python hybrid",
        "External": "Python external",
    }.get(profile, "Python adaptive")


class ExternalDecoderAdapter:
    def __init__(
        self,
        *,
        sample_rate: int,
        tone_hz: float,
        text_callback,
        initial_wpm: int,
    ):
        self._cfg = ExternalMorseConfig(
            sample_rate=sample_rate,
            tone_freq=tone_hz,
            wpm_initial=float(initial_wpm),
        )
        self._decoder = ExternalMorseDecoder(self._cfg)
        self._text_callback = text_callback
        self._decoder.on_result = self._on_result
        self._decoder._wire_callbacks()

    def _on_result(self, result):
        if self._text_callback is None:
            return
        self._text_callback(type("ExternalEvent", (), {
            "text": getattr(result, "character", ""),
            "confidence": float(getattr(result, "confidence", 0.0)),
        })())

    def start(self):
        return None

    def stop(self):
        self._decoder.flush()

    def reset(self):
        self._decoder.reset()

    def push_samples(self, samples: np.ndarray):
        self._decoder.feed(np.asarray(samples, dtype=np.float32))

    @property
    def stats(self):
        return type("ExternalStats", (), {
            "tracked_tone_hz": float(self._decoder.config.tone_freq),
            "estimated_wpm": float(self._decoder.current_wpm),
        })()


def emit_telemetry(status: str | None = None) -> None:
    with _lock:
        payload = {
            "type": "telemetry",
            "isRunning": bool(_running),
            "status": status or ("Running" if _running else "Stopped"),
            "activeWorker": worker_name_for(str(_config.get("profile", "Adaptive"))),
            "confidence": float(_last_confidence),
            "estimatedPitchHz": int(_estimated_pitch),
            "estimatedWpm": int(_estimated_wpm),
        }
    emit(payload)


def on_decode(event: Any) -> None:
    global _last_confidence
    text = getattr(event, "text", "")
    confidence = float(getattr(event, "confidence", 0.0))
    _last_confidence = confidence
    if text:
        emit({"type": "decode", "text": text, "confidence": confidence})
    emit_telemetry("Receiving")


def on_pitch(hz: float) -> None:
    global _estimated_pitch
    _estimated_pitch = int(round(float(hz)))


def on_wpm(wpm: int) -> None:
    global _estimated_wpm
    _estimated_wpm = int(wpm)


def build_decoder() -> None:
    global _decoder, _estimated_pitch, _estimated_wpm, _last_confidence

    with _lock:
        if _decoder is not None:
            try:
                _decoder.stop()
            except Exception:
                pass
            _decoder = None

        pitch = int(_config.get("pitchHz", 700))
        wpm = int(_config.get("wpm", 20))
        profile = str(_config.get("profile", "Adaptive"))

        if profile == "Minimal":
            _decoder = CwMinimalDecoder(
                sample_rate=48000,
                tone_hz=float(pitch),
                text_callback=on_decode,
                initial_wpm=wpm,
                use_goertzel=True,
            )
        elif profile == "Hybrid":
            _decoder = CwHybridDecoder(
                sample_rate=48000,
                tone_hz=float(pitch),
                text_callback=on_decode,
                initial_wpm=wpm,
            )
        elif profile == "External":
            _decoder = ExternalDecoderAdapter(
                sample_rate=8000,
                tone_hz=float(pitch),
                text_callback=on_decode,
                initial_wpm=wpm,
            )
        else:
            _decoder = CwAdaptiveDecoder(
                sample_rate=48000,
                tone_hz=float(pitch),
                text_callback=on_decode,
                initial_wpm=wpm,
            )
            if hasattr(_decoder, "set_afc_callback"):
                _decoder.set_afc_callback(on_pitch)
            if hasattr(_decoder, "set_wpm_callback"):
                _decoder.set_wpm_callback(on_wpm)

        if hasattr(_decoder, "set_initial_wpm"):
            _decoder.set_initial_wpm(wpm)
        if hasattr(_decoder, "tone_hz"):
            try:
                _decoder.tone_hz = float(pitch)
            except Exception:
                pass

        _estimated_pitch = pitch
        _estimated_wpm = wpm
        _last_confidence = 0.0


def handle_configure(message: dict[str, Any]) -> None:
    with _lock:
        _config["pitchHz"] = int(message.get("pitchHz", _config["pitchHz"]))
        _config["wpm"] = int(message.get("wpm", _config["wpm"]))
        _config["profile"] = str(message.get("profile", _config["profile"]))

    build_decoder()
    emit_telemetry(f"Configured for {_config['pitchHz']} Hz / {_config['wpm']} WPM")


def handle_start() -> None:
    global _running
    with _lock:
        if _decoder is None:
            build_decoder()
        if _decoder is not None and not _running:
            _decoder.start()
            _running = True
    emit_telemetry("Decoder running")


def handle_stop() -> None:
    global _running
    with _lock:
        if _decoder is not None and _running:
            _decoder.stop()
        _running = False
    emit_telemetry("Decoder stopped")


def handle_reset() -> None:
    with _lock:
        if _decoder is not None:
            _decoder.reset()
    emit_telemetry("Decoder reset")


def handle_audio(message: dict[str, Any]) -> None:
    with _lock:
        if not _running or _decoder is None:
            return

    samples_b64 = message.get("samples")
    if not isinstance(samples_b64, str) or not samples_b64:
        return

    raw = base64.b64decode(samples_b64)
    samples = np.frombuffer(raw, dtype="<f4")
    channels = int(message.get("channels", 1))

    if channels > 1 and len(samples) >= channels:
        usable = (len(samples) // channels) * channels
        if usable == 0:
            return
        samples = samples[:usable].reshape(-1, channels).mean(axis=1)

    if samples.size == 0:
        return

    profile = str(_config.get("profile", "Adaptive"))
    if profile == "External":
        source_rate = int(message.get("sampleRate", 48000))
        samples = downsample_for_external(samples.astype(np.float32, copy=False), source_rate)
        if samples.size == 0:
            return

    _decoder.push_samples(samples.astype(np.float32, copy=False))

    if hasattr(_decoder, "stats"):
        try:
            stats = _decoder.stats
            globals()["_estimated_pitch"] = int(round(float(getattr(stats, "tracked_tone_hz", _estimated_pitch))))
            globals()["_estimated_wpm"] = int(round(float(getattr(stats, "estimated_wpm", _estimated_wpm))))
        except Exception:
            pass


def downsample_for_external(samples: np.ndarray, sample_rate: int) -> np.ndarray:
    target_rate = 8000
    if sample_rate <= 0 or samples.size == 0:
        return samples.astype(np.float32, copy=False)
    if sample_rate == target_rate:
        return samples.astype(np.float32, copy=False)
    if sample_rate % target_rate == 0:
        factor = sample_rate // target_rate
        usable = (len(samples) // factor) * factor
        if usable <= 0:
            return np.array([], dtype=np.float32)
        return samples[:usable].reshape(-1, factor).mean(axis=1).astype(np.float32)

    x_old = np.linspace(0.0, 1.0, num=len(samples), endpoint=False)
    new_len = max(1, int(round(len(samples) * target_rate / sample_rate)))
    x_new = np.linspace(0.0, 1.0, num=new_len, endpoint=False)
    return np.interp(x_new, x_old, samples).astype(np.float32)


def main() -> int:
    emit_telemetry("Python CW worker ready")

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            message = json.loads(line)
        except Exception as ex:
            emit({"type": "telemetry", "isRunning": False, "status": f"Protocol error: {ex}", "activeWorker": "Python CW worker", "confidence": 0.0, "estimatedPitchHz": int(_estimated_pitch), "estimatedWpm": int(_estimated_wpm)})
            continue

        msg_type = message.get("type")
        try:
            if msg_type == "configure":
                handle_configure(message)
            elif msg_type == "start":
                handle_start()
            elif msg_type == "stop":
                handle_stop()
            elif msg_type == "reset":
                handle_reset()
            elif msg_type == "audio":
                handle_audio(message)
            elif msg_type == "shutdown":
                handle_stop()
                break
        except Exception as ex:
            emit({"type": "telemetry", "isRunning": bool(_running), "status": f"Worker error: {ex}", "activeWorker": worker_name_for(str(_config.get('profile', 'Adaptive'))), "confidence": float(_last_confidence), "estimatedPitchHz": int(_estimated_pitch), "estimatedWpm": int(_estimated_wpm)})

    handle_stop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
