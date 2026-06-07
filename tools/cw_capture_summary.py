from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

import numpy as np


def load_manifest(path: Path) -> tuple[dict[str, Any] | None, list[dict[str, Any]]]:
    header: dict[str, Any] | None = None
    samples: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            payload = json.loads(line)
            if payload.get("type") == "session":
                header = payload
            elif payload.get("type") == "cw-skimmer-sample":
                samples.append(payload)
    return header, samples


def inspect_audio(session: Path, sample: dict[str, Any]) -> dict[str, float]:
    audio_path = session / str(sample["audioFile"]).replace("/", "\\")
    data = np.fromfile(audio_path, dtype="<f4")
    if data.size == 0:
        return {"samples": 0, "rms": 0.0, "peak": 0.0}
    return {
        "samples": float(data.size),
        "rms": float(np.sqrt(np.mean(data * data))),
        "peak": float(np.max(np.abs(data))),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Summarize a ShackStack CW skimmer capture session.")
    parser.add_argument("session", type=Path)
    parser.add_argument("--top", type=int, default=12)
    parser.add_argument("--inspect-audio", action="store_true")
    args = parser.parse_args()

    session = args.session
    manifest_path = session / "manifest.jsonl"
    if not manifest_path.exists():
        print(f"manifest not found: {manifest_path}")
        return 1

    header, samples = load_manifest(manifest_path)
    if not samples:
        print("no cw-skimmer-sample entries found")
        return 1

    first = min(str(item["capturedUtc"]) for item in samples)
    last = max(str(item["capturedUtc"]) for item in samples)
    audio_files = list((session / "audio").glob("*.f32"))
    by_lane: dict[int, list[dict[str, Any]]] = defaultdict(list)
    by_tone_bucket: Counter[int] = Counter()
    callish: Counter[str] = Counter()

    for sample in samples:
        lane_id = int(sample.get("laneId", 0))
        by_lane[lane_id].append(sample)
        tone = float(sample.get("audioToneHz", 0.0))
        by_tone_bucket[int(round(tone / 25.0) * 25)] += 1
        for token in str(sample.get("laneTranscript", "")).replace("?", " ").split():
            if any(ch.isdigit() for ch in token) and any(ch.isalpha() for ch in token):
                callish[token.strip(".,;:/\\|")] += 1

    print(f"session: {session}")
    if header:
        print(f"created: {header.get('createdUtc')}")
    print(f"samples: {len(samples)} manifest entries, {len(audio_files)} audio files")
    print(f"time: {first} .. {last}")
    print(f"radio: {samples[-1].get('radioFrequencyHz')} Hz {samples[-1].get('radioMode')}")
    print()

    print("top lanes:")
    lane_rows = []
    for lane_id, lane_samples in by_lane.items():
        tones = [float(item.get("audioToneHz", 0.0)) for item in lane_samples]
        confidence = [float(item.get("confidence", 0.0)) for item in lane_samples]
        snr_text = [str(item.get("scoutText", "")) for item in lane_samples]
        transcript = max((str(item.get("laneTranscript", "")) for item in lane_samples), key=len, default="")
        lane_rows.append(
            (
                len(lane_samples),
                lane_id,
                sum(tones) / len(tones),
                sum(confidence) / len(confidence),
                transcript,
                snr_text[-1],
            )
        )

    for count, lane_id, tone, confidence, transcript, scout in sorted(lane_rows, reverse=True)[: args.top]:
        print(f"  lane {lane_id:>3} {tone:7.1f} Hz n={count:>3} conf={confidence:0.2f} {scout} :: {transcript[:110]}")
    print()

    print("tone buckets:")
    for tone, count in by_tone_bucket.most_common(args.top):
        print(f"  {tone:>5} Hz: {count}")
    print()

    print("call-like tokens:")
    for token, count in callish.most_common(args.top):
        print(f"  {token}: {count}")

    if args.inspect_audio:
        print()
        print("audio sanity:")
        for sample in samples[:2] + samples[-2:]:
            stats = inspect_audio(session, sample)
            print(
                f"  {sample['id']}: samples={int(stats['samples'])} "
                f"rms={stats['rms']:0.4f} peak={stats['peak']:0.4f} "
                f"duration={sample.get('durationSeconds')}"
            )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
