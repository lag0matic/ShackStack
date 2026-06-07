from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any


def load_manifest(session: Path) -> tuple[dict[str, Any] | None, list[dict[str, Any]]]:
    header: dict[str, Any] | None = None
    samples: list[dict[str, Any]] = []
    manifest = session / "manifest.jsonl"
    if not manifest.exists():
        return None, []
    with manifest.open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            payload = json.loads(line)
            if payload.get("type") == "session":
                header = payload
            elif payload.get("type") == "cw-skimmer-sample":
                samples.append(payload)
    return header, samples


def callish_tokens(samples: list[dict[str, Any]], limit: int = 16) -> list[str]:
    counts: Counter[str] = Counter()
    for sample in samples:
        for token in str(sample.get("laneTranscript", "")).replace("?", " ").split():
            token = token.strip(".,;:/\\|")
            if any(ch.isdigit() for ch in token) and any(ch.isalpha() for ch in token):
                counts[token] += 1
    return [token for token, _ in counts.most_common(limit)]


def lane_summary(samples: list[dict[str, Any]], limit: int = 8) -> list[dict[str, Any]]:
    grouped: dict[int, list[dict[str, Any]]] = defaultdict(list)
    for sample in samples:
        grouped[int(sample.get("laneId", 0))].append(sample)

    lanes = []
    for lane_id, lane_samples in grouped.items():
        tones = [float(item.get("audioToneHz", 0.0)) for item in lane_samples]
        confidence = [float(item.get("confidence", 0.0)) for item in lane_samples]
        transcript = max((str(item.get("laneTranscript", "")) for item in lane_samples), key=len, default="")
        lanes.append(
            {
                "laneId": lane_id,
                "samples": len(lane_samples),
                "avgToneHz": sum(tones) / max(1, len(tones)),
                "avgConfidence": sum(confidence) / max(1, len(confidence)),
                "longestTranscript": transcript[:220],
            }
        )

    lanes.sort(key=lambda lane: (-int(lane["samples"]), -float(lane["avgConfidence"])))
    return lanes[:limit]


def main() -> int:
    parser = argparse.ArgumentParser(description="Create a JSON dataset index for CW skimmer capture sessions.")
    parser.add_argument("root", type=Path)
    parser.add_argument("--note", action="append", default=[], help="session-name=note")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    notes = {}
    for item in args.note:
        if "=" in item:
            key, value = item.split("=", 1)
            notes[key] = value

    sessions = []
    for session in sorted(path for path in args.root.iterdir() if path.is_dir()):
        header, samples = load_manifest(session)
        if not samples:
            continue
        sessions.append(
            {
                "session": session.name,
                "path": str(session),
                "createdUtc": header.get("createdUtc") if header else None,
                "sampleCount": len(samples),
                "audioFileCount": len(list((session / "audio").glob("*.f32"))),
                "wavFileCount": len(list((session / "wav").glob("*.wav"))),
                "radioFrequencyHz": samples[-1].get("radioFrequencyHz"),
                "radioMode": samples[-1].get("radioMode"),
                "firstCaptureUtc": min(str(item.get("capturedUtc", "")) for item in samples),
                "lastCaptureUtc": max(str(item.get("capturedUtc", "")) for item in samples),
                "note": notes.get(session.name, ""),
                "callishTokens": callish_tokens(samples),
                "topLanes": lane_summary(samples),
            }
        )

    output = {
        "type": "cw-skimmer-capture-index",
        "root": str(args.root),
        "sessionCount": len(sessions),
        "totalSamples": sum(int(session["sampleCount"]) for session in sessions),
        "sessions": sessions,
    }

    out = args.out or (args.root / "dataset-index.json")
    out.write_text(json.dumps(output, indent=2), encoding="utf-8")
    print(f"wrote {out}")
    print(f"sessions={output['sessionCount']} samples={output['totalSamples']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
