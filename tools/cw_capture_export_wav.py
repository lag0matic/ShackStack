from __future__ import annotations

import argparse
import json
import wave
from pathlib import Path
from typing import Any

import numpy as np


def load_samples(manifest_path: Path) -> list[dict[str, Any]]:
    samples: list[dict[str, Any]] = []
    with manifest_path.open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            payload = json.loads(line)
            if payload.get("type") == "cw-skimmer-sample":
                samples.append(payload)
    return samples


def write_wav(path: Path, samples: np.ndarray, sample_rate: int, channels: int) -> None:
    clipped = np.clip(samples, -1.0, 1.0)
    pcm = (clipped * 32767.0).astype("<i2")
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(channels)
        handle.setsampwidth(2)
        handle.setframerate(sample_rate)
        handle.writeframes(pcm.tobytes())


def main() -> int:
    parser = argparse.ArgumentParser(description="Export ShackStack CW skimmer .f32 captures to WAV.")
    parser.add_argument("session", type=Path)
    parser.add_argument("--lane", type=int, action="append", help="Export only these lane IDs. Can be repeated.")
    parser.add_argument("--top", type=int, default=12, help="Maximum number of samples to export.")
    parser.add_argument("--min-confidence", type=float, default=0.5)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    session = args.session
    manifest_path = session / "manifest.jsonl"
    samples = load_samples(manifest_path)
    lanes = set(args.lane or [])
    selected = [
        item
        for item in samples
        if (not lanes or int(item.get("laneId", 0)) in lanes)
        and float(item.get("confidence", 0.0)) >= args.min_confidence
    ]
    selected.sort(key=lambda item: (-len(str(item.get("laneTranscript", ""))), -float(item.get("confidence", 0.0))))
    selected = selected[: args.top]

    output_root = args.out or (session / "wav")
    for item in selected:
        source = session / str(item["audioFile"]).replace("/", "\\")
        raw = np.fromfile(source, dtype="<f4")
        sample_rate = int(item.get("sampleRate", 48000))
        channels = int(item.get("channels", 1))
        name = f"{item['id']}-{float(item.get('audioToneHz', 0.0)):0.0f}Hz.wav"
        target = output_root / name
        write_wav(target, raw, sample_rate, channels)
        print(f"{target} :: lane {item.get('laneId')} {item.get('audioToneHz'):0.1f} Hz :: {str(item.get('laneTranscript', ''))[:100]}")

    print(f"exported {len(selected)} wav file(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
