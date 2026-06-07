from __future__ import annotations

import argparse
import csv
import json
import sys
from collections import Counter
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from cw_capture_replay import default_variants, decode_audio, load_audio, load_manifest, lane_isolate, score_text


def callish_hints(sample: dict[str, Any], limit: int = 8) -> list[str]:
    counts: Counter[str] = Counter()
    for token in str(sample.get("laneTranscript", "")).replace("?", " ").split():
        token = token.strip(".,;:/\\|")
        if any(ch.isdigit() for ch in token) and any(ch.isalpha() for ch in token):
            counts[token] += 1
    fixed = ["CQ", "SST", "TU", "73"]
    return [token for token, _ in counts.most_common(limit)] + fixed


def select_samples(session: Path, top: int, min_confidence: float) -> list[dict[str, Any]]:
    samples = [
        item
        for item in load_manifest(session)
        if int(item.get("laneId", 0)) > 0 and float(item.get("confidence", 0.0)) >= min_confidence
    ]
    samples.sort(key=lambda item: (-len(str(item.get("laneTranscript", ""))), -float(item.get("confidence", 0.0))))
    return samples[:top]


def main() -> int:
    parser = argparse.ArgumentParser(description="Benchmark fldigi CW decoder variants against capture sessions.")
    parser.add_argument("sessions", nargs="+", type=Path)
    parser.add_argument("--top", type=int, default=10)
    parser.add_argument("--min-confidence", type=float, default=0.55)
    parser.add_argument("--isolate", action="store_true")
    parser.add_argument("--out", type=Path, default=Path("cw-capture-benchmark.csv"))
    args = parser.parse_args()

    variants = default_variants()
    rows: list[dict[str, Any]] = []
    totals: Counter[str] = Counter()

    for session in args.sessions:
        for sample in select_samples(session, args.top, args.min_confidence):
            sample_rate = int(sample.get("sampleRate", 48000))
            tone_hz = float(sample.get("audioToneHz", 700.0))
            audio = load_audio(session, sample)
            decode_tone = tone_hz
            if args.isolate:
                audio = lane_isolate(audio, sample_rate=sample_rate, tone_hz=tone_hz)
                decode_tone = 700.0
            hints = callish_hints(sample)
            best_label = ""
            best_score = -1
            best_text = ""
            for variant in variants:
                text, stats = decode_audio(audio, sample_rate=sample_rate, tone_hz=decode_tone, variant=variant)
                score = score_text(text, hints)
                totals[variant.label] += score
                if score > best_score:
                    best_label = variant.label
                    best_score = score
                    best_text = text
                rows.append(
                    {
                        "session": session.name,
                        "sampleId": sample.get("id"),
                        "laneId": sample.get("laneId"),
                        "toneHz": f"{tone_hz:0.2f}",
                        "confidence": f"{float(sample.get('confidence', 0.0)):0.3f}",
                        "variant": variant.label,
                        "score": score,
                        "estimatedWpm": f"{stats.estimated_wpm:0.2f}",
                        "decoderConfidence": f"{stats.confidence:0.3f}",
                        "text": text,
                        "manifestTranscript": str(sample.get("laneTranscript", "")),
                        "hints": " ".join(hints),
                    }
                )
            print(f"{session.name} {sample.get('id')} lane {sample.get('laneId')} {tone_hz:0.1f} Hz best={best_label} score={best_score} :: {best_text[:100]!r}")

    args.out.parent.mkdir(parents=True, exist_ok=True) if args.out.parent != Path(".") else None
    with args.out.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()) if rows else [])
        if rows:
            writer.writeheader()
            writer.writerows(rows)

    print()
    print("variant totals:")
    for label, score in totals.most_common():
        print(f"  {label}: {score}")
    print(f"wrote {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
