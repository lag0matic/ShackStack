from __future__ import annotations

import base64
import importlib.util
import json
import struct
import sys
import wave
from pathlib import Path


ROOT = Path(r"C:\Users\lag0m\Documents\ShackStack.Avalonia")
HARNESS_ROOT = ROOT / ".tmp-sstv-harness"
WAV_PATH = HARNESS_ROOT / "martin1_loopback.wav"
SOURCE_PATH = HARNESS_ROOT / "martin1_source.bmp"
PYTHON_WORKER_PATH = ROOT / "src" / "ShackStack.DecoderHost" / "Python" / "Tools" / "sstv_sidecar_worker.py"


def load_worker_module():
    module_name = "sstv_sidecar_worker"
    spec = importlib.util.spec_from_file_location(module_name, PYTHON_WORKER_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load python SSTV worker.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module


def read_wav_as_float32(path: Path) -> tuple[list[float], int]:
    with wave.open(str(path), "rb") as handle:
        channels = handle.getnchannels()
        sample_width = handle.getsampwidth()
        sample_rate = handle.getframerate()
        frames = handle.getnframes()
        data = handle.readframes(frames)

    if sample_width != 2 or channels != 1:
        raise RuntimeError("Expected mono 16-bit WAV from harness.")

    ints = struct.unpack("<" + ("h" * (len(data) // 2)), data)
    floats = [sample / 32768.0 for sample in ints]
    return floats, sample_rate


def image_metrics(source_path: Path, decoded_path: Path) -> dict[str, float]:
    from PIL import Image
    import numpy as np

    src = np.asarray(Image.open(source_path).convert("RGB"), dtype=np.float32)
    dec = np.asarray(Image.open(decoded_path).convert("RGB"), dtype=np.float32)
    if src.shape != dec.shape:
        raise RuntimeError(f"Source and decoded images differ in size: {src.shape} vs {dec.shape}.")

    mae = float(np.mean(np.abs(src - dec)))
    metrics = {"mae": mae}
    for idx, name in enumerate(("r", "g", "b")):
        a = src[:, :, idx].reshape(-1)
        b = dec[:, :, idx].reshape(-1)
        corr = float(0.0 if a.std() == 0.0 or b.std() == 0.0 else np.corrcoef(a, b)[0, 1])
        metrics[f"{name}_corr"] = corr
    return metrics


def main() -> int:
    module = load_worker_module()
    captured = []
    module.emit = lambda payload: captured.append(payload)
    receiver = module.SstvReceiver()
    receiver.configure({"mode": "Auto Detect", "frequencyLabel": "14.230 MHz USB", "manualSlant": 0, "manualOffset": 0})
    receiver.start()

    floats, sample_rate = read_wav_as_float32(WAV_PATH)
    chunk_size = 2048
    for offset in range(0, len(floats), chunk_size):
        chunk = floats[offset:offset + chunk_size]
        raw = struct.pack("<" + ("f" * len(chunk)), *chunk)
        receiver.handle_audio({
            "samples": base64.b64encode(raw).decode("ascii"),
            "channels": 1,
            "sampleRate": sample_rate,
        })

    latest = Path(receiver._latest_image_path) if receiver._latest_image_path else None
    if latest is None or not latest.exists():
        print(json.dumps({
            "decoded_image": None,
            "status": receiver._status,
            "mode": receiver._detected_mode,
            "events": len(captured),
        }, indent=2))
        return 1

    copied = HARNESS_ROOT / "martin1_python_decoded.png"
    copied.write_bytes(latest.read_bytes())
    try:
        metrics = image_metrics(SOURCE_PATH, copied)
    except Exception as ex:
        print(json.dumps({
            "decoded_image": str(copied),
            "status": receiver._status,
            "mode": receiver._detected_mode,
            "events": len(captured),
            "error": str(ex),
        }, indent=2))
        return 1
    print(json.dumps({
        "decoded_image": str(copied),
        "status": receiver._status,
        "mode": receiver._detected_mode,
        "metrics": metrics,
        "events": len(captured),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
