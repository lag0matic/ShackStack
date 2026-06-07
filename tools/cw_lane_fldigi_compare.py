from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np

REPO_ROOT = Path(__file__).resolve().parents[1]
PY_WORKERS = REPO_ROOT / "src" / "ShackStack.DecoderWorkers.Python"
sys.path.insert(0, str(PY_WORKERS))

from cw_skimmer_file_harness import decode_m4a, run_skimmer, summarize_skimmer_payloads
from modes.cw_engine_fldigi import CwFldigiDecoder


def decode_lane(audio: np.ndarray, tone_hz: float, *, wpm: int, bandwidth_hz: int) -> str:
    decoded: list[str] = []
    decoder = CwFldigiDecoder(
        sample_rate=48000,
        tone_hz=float(tone_hz),
        text_callback=lambda event: decoded.append(str(getattr(event, "text", ""))),
        initial_wpm=wpm,
        bandwidth_hz=bandwidth_hz,
        matched_filter_enabled=True,
        tracking_enabled=True,
        tracking_range_wpm=8,
        lower_wpm_limit=5,
        upper_wpm_limit=60,
        auto_tone_search_enabled=False,
        afc_enabled=False,
        squelch="Low",
        noise_character="Suppress",
    )
    decoder.start()
    for offset in range(0, len(audio), 2048):
        decoder.push_samples(audio[offset : offset + 2048])
    decoder.stop()
    return "".join(decoded).strip()


def select_skimmer_lanes(audio: np.ndarray, args: argparse.Namespace) -> list[dict[str, object]]:
    payloads = run_skimmer(
        audio,
        low=args.low,
        high=args.high,
        spacing=args.spacing,
        lanes=args.lanes,
        chunk_size=args.chunk,
    )
    summary = summarize_skimmer_payloads(payloads)
    selected: list[dict[str, object]] = []
    seen_tones: list[float] = []

    for lane in summary["laneSummaries"]:
        tone = float(lane["avgToneHz"])
        confidence = float(lane["confidence"])
        candidate_count = int(lane["candidateCount"])
        transcript = str(lane["transcript"])

        if confidence < args.min_confidence:
            continue
        if candidate_count < args.min_candidates:
            continue
        if any(abs(tone - seen) < args.dedupe_hz for seen in seen_tones):
            continue

        selected.append(lane)
        seen_tones.append(tone)
        if len(selected) >= args.top:
            break

    print(
        "skimmer",
        f"telemetry={len(summary['telemetry'])}",
        f"candidates={len(summary['candidates'])}",
        f"lanes={len(summary['laneSummaries'])}",
        f"selected={len(selected)}",
    )
    for lane in selected:
        print(
            f"  lane {int(lane['laneId']):>2} {float(lane['avgToneHz']):7.1f} Hz "
            f"conf={float(lane['confidence']):0.2f} events={int(lane['candidateCount'])} "
            f"skimmer={str(lane['transcript'])[:80]!r}"
        )
    return selected


def main() -> int:
    parser = argparse.ArgumentParser(description="Compare existing fldigi CW decoder against skimmer lane tones.")
    parser.add_argument("audio_file", type=Path)
    parser.add_argument("tones", nargs="*", type=float)
    parser.add_argument("--wpm", type=int, default=20)
    parser.add_argument("--bandwidth", type=int, default=220)
    parser.add_argument("--low", type=int, default=450)
    parser.add_argument("--high", type=int, default=2800)
    parser.add_argument("--spacing", type=int, default=45)
    parser.add_argument("--lanes", type=int, default=8)
    parser.add_argument("--chunk", type=int, default=2048)
    parser.add_argument("--top", type=int, default=8)
    parser.add_argument("--min-confidence", type=float, default=0.08)
    parser.add_argument("--min-candidates", type=int, default=3)
    parser.add_argument("--dedupe-hz", type=float, default=90.0)
    args = parser.parse_args()

    audio = decode_m4a(args.audio_file)
    print(f"decoded {len(audio)} samples ({len(audio) / 48000.0:0.1f}s)")

    lane_sources: list[dict[str, object]]
    if args.tones:
        lane_sources = [
            {"laneId": index + 1, "avgToneHz": tone, "transcript": "", "confidence": 0.0, "candidateCount": 0}
            for index, tone in enumerate(args.tones)
        ]
    else:
        lane_sources = select_skimmer_lanes(audio, args)

    for lane in lane_sources:
        tone = float(lane["avgToneHz"])
        text = decode_lane(audio, tone, wpm=args.wpm, bandwidth_hz=args.bandwidth)
        print(
            f"{tone:7.1f} Hz fldigi={text[:180]!r} "
            f"skimmer={str(lane.get('transcript', ''))[:80]!r}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
