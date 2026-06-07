from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import numpy as np

from cw_train_presence_baseline import extract_features, load_audio, sigmoid


def load_rows(session: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    with (session / "manifest.jsonl").open("r", encoding="utf-8") as handle:
        for line in handle:
            payload = json.loads(line)
            if payload.get("type") == "cw-skimmer-sample":
                rows.append(payload)
    return rows


def load_model(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def score_row(session: Path, model: dict[str, Any], row: dict[str, Any]) -> tuple[float, np.ndarray]:
    audio = load_audio(session, row)
    sample_rate = int(row.get("sampleRate") or row.get("sessionSampleRate") or 48000)
    features = extract_features(audio, sample_rate, float(row.get("audioToneHz") or 0.0))
    mean = np.asarray(model["mean"], dtype=np.float64)
    std = np.asarray(model["std"], dtype=np.float64)
    weights = np.asarray(model["weights"], dtype=np.float64)
    bias = float(model["bias"])
    prob = float(sigmoid(((features - mean) / std) @ weights + bias))
    return prob, features


def main() -> int:
    parser = argparse.ArgumentParser(description="Score recorded CW skimmer captures with the CW presence baseline.")
    parser.add_argument("model", type=Path)
    parser.add_argument("session", type=Path)
    parser.add_argument("--top", type=int, default=20)
    parser.add_argument("--out", type=Path, default=None)
    args = parser.parse_args()

    model = load_model(args.model)
    rows = load_rows(args.session)
    scored: list[dict[str, Any]] = []
    for row in rows:
        prob, _ = score_row(args.session, model, row)
        scored.append(
            {
                "id": row.get("id"),
                "laneId": row.get("laneId"),
                "audioToneHz": row.get("audioToneHz"),
                "confidence": row.get("confidence"),
                "presenceScore": round(prob, 4),
                "firstPassText": row.get("firstPassText", ""),
                "laneTranscript": row.get("laneTranscript", ""),
                "scoutText": row.get("scoutText", ""),
            }
        )

    if args.out:
        args.out.write_text(json.dumps(scored, indent=2), encoding="utf-8")

    print(f"session: {args.session.name}")
    print(f"samples: {len(scored)}")
    if scored:
        scores = np.asarray([float(row["presenceScore"]) for row in scored], dtype=np.float64)
        print(f"score min/median/max: {scores.min():0.3f} / {np.median(scores):0.3f} / {scores.max():0.3f}")
    print()
    print("lowest scored lanes:")
    for row in sorted(scored, key=lambda item: float(item["presenceScore"]))[: args.top]:
        print(
            f"  {row['presenceScore']:0.3f} lane {row['laneId']} {float(row.get('audioToneHz') or 0):7.1f} Hz "
            f"conf={float(row.get('confidence') or 0):0.2f} :: {str(row.get('laneTranscript') or '')[:80]}"
        )
    print()
    print("highest scored lanes:")
    for row in sorted(scored, key=lambda item: float(item["presenceScore"]), reverse=True)[: args.top]:
        print(
            f"  {row['presenceScore']:0.3f} lane {row['laneId']} {float(row.get('audioToneHz') or 0):7.1f} Hz "
            f"conf={float(row.get('confidence') or 0):0.2f} :: {str(row.get('laneTranscript') or '')[:80]}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
