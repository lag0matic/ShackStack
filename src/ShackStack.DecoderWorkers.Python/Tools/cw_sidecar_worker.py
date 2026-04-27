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

from modes.cw_engine_fldigi import CwFldigiDecoder


_lock = threading.RLock()
_decoder = None
_running = False
_config = {
    "pitchHz": 700,
    "wpm": 20,
    "profile": "Fldigi",
    "bandwidthHz": 220,
    "matchedFilterEnabled": True,
    "trackingEnabled": True,
    "trackingRangeWpm": 8,
    "lowerWpmLimit": 5,
    "upperWpmLimit": 60,
    "attack": "Normal",
    "decay": "Slow",
    "noiseCharacter": "Suppress",
    "autoToneSearchEnabled": True,
    "afcEnabled": True,
    "toneSearchSpanHz": 250,
    "squelch": "Off",
    "spacing": "Normal",
}
_last_confidence = 0.0
_estimated_pitch = 700
_estimated_wpm = 20


def emit(payload: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(payload, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def worker_name_for(profile: str) -> str:
    return "fldigi CW port"


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
        _config["profile"] = "Fldigi"
        _decoder = CwFldigiDecoder(
            sample_rate=48000,
            tone_hz=float(pitch),
            text_callback=on_decode,
            initial_wpm=wpm,
            bandwidth_hz=int(_config.get("bandwidthHz", 220)),
            matched_filter_enabled=bool(_config.get("matchedFilterEnabled", True)),
            tracking_enabled=bool(_config.get("trackingEnabled", True)),
            tracking_range_wpm=int(_config.get("trackingRangeWpm", 8)),
            lower_wpm_limit=int(_config.get("lowerWpmLimit", 5)),
            upper_wpm_limit=int(_config.get("upperWpmLimit", 60)),
            attack=str(_config.get("attack", "Normal")),
            decay=str(_config.get("decay", "Slow")),
            noise_character=str(_config.get("noiseCharacter", "Suppress")),
            auto_tone_search_enabled=bool(_config.get("autoToneSearchEnabled", True)),
            afc_enabled=bool(_config.get("afcEnabled", True)),
            tone_search_span_hz=int(_config.get("toneSearchSpanHz", 250)),
            squelch=str(_config.get("squelch", "Off")),
            spacing=str(_config.get("spacing", "Normal")),
        )
        _decoder.set_afc_callback(on_pitch)
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
        _config["profile"] = "Fldigi"
        _config["bandwidthHz"] = int(message.get("bandwidthHz", _config["bandwidthHz"]))
        _config["matchedFilterEnabled"] = bool(message.get("matchedFilterEnabled", _config["matchedFilterEnabled"]))
        _config["trackingEnabled"] = bool(message.get("trackingEnabled", _config["trackingEnabled"]))
        _config["trackingRangeWpm"] = int(message.get("trackingRangeWpm", _config["trackingRangeWpm"]))
        _config["lowerWpmLimit"] = int(message.get("lowerWpmLimit", _config["lowerWpmLimit"]))
        _config["upperWpmLimit"] = int(message.get("upperWpmLimit", _config["upperWpmLimit"]))
        _config["attack"] = str(message.get("attack", _config["attack"]))
        _config["decay"] = str(message.get("decay", _config["decay"]))
        _config["noiseCharacter"] = str(message.get("noiseCharacter", _config["noiseCharacter"]))
        _config["autoToneSearchEnabled"] = bool(message.get("autoToneSearchEnabled", _config["autoToneSearchEnabled"]))
        _config["afcEnabled"] = bool(message.get("afcEnabled", _config["afcEnabled"]))
        _config["toneSearchSpanHz"] = int(message.get("toneSearchSpanHz", _config["toneSearchSpanHz"]))
        _config["squelch"] = str(message.get("squelch", _config["squelch"]))
        _config["spacing"] = str(message.get("spacing", _config["spacing"]))

    build_decoder()
    emit_telemetry(f"Configured for {_config['pitchHz']} Hz / {_config['wpm']} WPM / {_config['bandwidthHz']} Hz BW")


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

    _decoder.push_samples(samples.astype(np.float32, copy=False))

    if hasattr(_decoder, "stats"):
        try:
            stats = _decoder.stats
            globals()["_estimated_pitch"] = int(round(float(getattr(stats, "tracked_tone_hz", _estimated_pitch))))
            globals()["_estimated_wpm"] = int(round(float(getattr(stats, "estimated_wpm", _estimated_wpm))))
        except Exception:
            pass

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
