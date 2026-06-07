from __future__ import annotations

import argparse
import json
import random
from pathlib import Path
from typing import Any

import numpy as np
import torch
from scipy.signal import butter, resample_poly, sosfilt

from cw_train_duration_model import DurationCnn, LABELS, envelope_features, make_batch


def load_model(path: Path, device: torch.device) -> tuple[DurationCnn, dict[str, Any]]:
    checkpoint = torch.load(path, map_location=device)
    config = checkpoint["config"]
    model = DurationCnn(channels=int(config["inputChannels"]), classes=len(checkpoint["labels"]))
    model.load_state_dict(checkpoint["modelState"])
    model.to(device)
    model.eval()
    return model, checkpoint


def synthetic_eval(
    model: DurationCnn,
    checkpoint: dict[str, Any],
    device: torch.device,
    batches: int,
    batch_size: int,
    seed: int,
) -> dict[str, Any]:
    config = checkpoint["config"]
    rng = random.Random(seed)
    confusion = np.zeros((len(LABELS), len(LABELS)), dtype=np.int64)
    losses: list[float] = []
    with torch.no_grad():
        for _ in range(batches):
            x, y = make_batch(batch_size, int(config["sampleRate"]), float(config["seconds"]), int(config["frameHz"]), rng)
            x = x.to(device)
            y = y.to(device)
            logits = model(x)
            loss = torch.nn.functional.cross_entropy(logits, y)
            pred = torch.argmax(logits, dim=1)
            losses.append(float(loss.detach().cpu()))
            truth = y.detach().cpu().numpy().reshape(-1)
            got = pred.detach().cpu().numpy().reshape(-1)
            for t, p in zip(truth, got):
                confusion[int(t), int(p)] += 1

    per_class = {}
    for index, label in enumerate(LABELS):
        total = int(confusion[index, :].sum())
        per_class[label] = float(confusion[index, index] / total) if total else 0.0

    return {
        "loss": float(np.mean(losses)) if losses else 0.0,
        "accuracy": float(np.trace(confusion) / max(1, int(confusion.sum()))),
        "perClassRecall": per_class,
        "confusion": confusion.tolist(),
    }


def load_manifest_rows(session: Path) -> list[dict[str, Any]]:
    manifest = session / "manifest.jsonl"
    rows: list[dict[str, Any]] = []
    if not manifest.exists():
        return rows
    with manifest.open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            payload = json.loads(line)
            if payload.get("type") == "cw-skimmer-sample":
                rows.append(payload)
    return rows


def load_real_audio(session: Path, row: dict[str, Any]) -> np.ndarray:
    path = session / str(row["audioFile"]).replace("/", "\\")
    audio = np.fromfile(path, dtype="<f4")
    channels = int(row.get("channels") or row.get("sessionChannels") or 1)
    if channels > 1 and audio.size >= channels:
        audio = audio.reshape((-1, channels)).mean(axis=1)
    return np.nan_to_num(audio.astype(np.float32, copy=False))


def bandpass_lane(audio: np.ndarray, sample_rate: int, tone_hz: float) -> np.ndarray:
    if tone_hz <= 0.0 or audio.size < 32:
        return audio
    low = max(40.0, tone_hz - 180.0)
    high = min(sample_rate / 2.0 - 100.0, tone_hz + 180.0)
    if high <= low + 40.0:
        return audio
    sos = butter(4, [low, high], btype="bandpass", fs=sample_rate, output="sos")
    return sosfilt(sos, audio).astype(np.float32)


def prepare_real_features(audio: np.ndarray, source_rate: int, config: dict[str, Any], tone_hz: float) -> torch.Tensor:
    target_rate = int(config["sampleRate"])
    seconds = float(config["seconds"])
    frame_hz = int(config["frameHz"])
    audio = bandpass_lane(audio, source_rate, tone_hz)
    if source_rate != target_rate and audio.size:
        gcd = int(np.gcd(source_rate, target_rate))
        audio = resample_poly(audio, target_rate // gcd, source_rate // gcd).astype(np.float32)
    target_samples = int(target_rate * seconds)
    if audio.size > target_samples:
        start = max(0, (audio.size - target_samples) // 2)
        audio = audio[start : start + target_samples]
    elif audio.size < target_samples:
        audio = np.pad(audio, (0, target_samples - audio.size))
    frames = int(seconds * frame_hz)
    features = envelope_features(audio, frame_hz, frames)
    return torch.from_numpy(features).unsqueeze(0)


def real_score(model: DurationCnn, checkpoint: dict[str, Any], device: torch.device, session: Path, limit: int) -> list[dict[str, Any]]:
    config = checkpoint["config"]
    rows = load_manifest_rows(session)
    scored: list[dict[str, Any]] = []
    with torch.no_grad():
        for row in rows[:limit if limit > 0 else None]:
            audio = load_real_audio(session, row)
            sample_rate = int(row.get("sampleRate") or row.get("sessionSampleRate") or 48000)
            x = prepare_real_features(audio, sample_rate, config, float(row.get("audioToneHz") or 0.0)).to(device)
            probs = torch.softmax(model(x), dim=1)[0].detach().cpu().numpy()
            fractions = probs.mean(axis=1)
            scored.append(
                {
                    "id": row.get("id"),
                    "laneId": row.get("laneId"),
                    "audioToneHz": row.get("audioToneHz"),
                    "confidence": row.get("confidence"),
                    "laneTranscript": row.get("laneTranscript", ""),
                    "gap": float(fractions[0]),
                    "dit": float(fractions[1]),
                    "dah": float(fractions[2]),
                    "bad_lane": float(fractions[3]),
                }
            )
    return scored


def summarize_real(scored: list[dict[str, Any]]) -> dict[str, Any]:
    if not scored:
        return {"count": 0}
    keys = ["gap", "dit", "dah", "bad_lane"]
    values = {key: np.asarray([float(row[key]) for row in scored], dtype=np.float64) for key in keys}
    return {
        "count": len(scored),
        **{f"{key}_median": float(np.median(value)) for key, value in values.items()},
        **{f"{key}_max": float(np.max(value)) for key, value in values.items()},
        "lowestBadLane": sorted(scored, key=lambda row: float(row["bad_lane"]))[:8],
        "highestBadLane": sorted(scored, key=lambda row: float(row["bad_lane"]), reverse=True)[:8],
    }


def print_real_summary(session: Path, summary: dict[str, Any]) -> None:
    print(f"real session: {session.name}")
    print(
        "  count={count} bad_lane median/max={bad_lane_median:0.3f}/{bad_lane_max:0.3f} "
        "dit median={dit_median:0.3f} dah median={dah_median:0.3f}".format(**summary)
    )
    print("  lowest bad_lane:")
    for row in summary["lowestBadLane"]:
        print(
            f"    {row['bad_lane']:0.3f} lane {row['laneId']} {float(row.get('audioToneHz') or 0):7.1f} Hz "
            f"conf={float(row.get('confidence') or 0):0.2f} :: {str(row.get('laneTranscript') or '')[:80]}"
        )
    print("  highest bad_lane:")
    for row in summary["highestBadLane"]:
        print(
            f"    {row['bad_lane']:0.3f} lane {row['laneId']} {float(row.get('audioToneHz') or 0):7.1f} Hz "
            f"conf={float(row.get('confidence') or 0):0.2f} :: {str(row.get('laneTranscript') or '')[:80]}"
        )


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate a CW duration model on synthetic validation and real captures.")
    parser.add_argument("model", type=Path)
    parser.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    parser.add_argument("--batches", type=int, default=24)
    parser.add_argument("--batch-size", type=int, default=96)
    parser.add_argument("--seed", type=int, default=20260531)
    parser.add_argument("--real-session", action="append", type=Path, default=[])
    parser.add_argument("--real-limit", type=int, default=80)
    parser.add_argument("--json-out", type=Path)
    args = parser.parse_args()

    device = torch.device(args.device)
    model, checkpoint = load_model(args.model, device)
    print(f"model={args.model}")
    print(f"checkpoint step={checkpoint.get('step')} loss={checkpoint.get('loss')} acc={checkpoint.get('accuracy')}")
    print(f"device={device}")

    result: dict[str, Any] = {
        "model": str(args.model),
        "checkpointStep": checkpoint.get("step"),
        "checkpointLoss": checkpoint.get("loss"),
        "checkpointAccuracy": checkpoint.get("accuracy"),
        "synthetic": synthetic_eval(model, checkpoint, device, args.batches, args.batch_size, args.seed),
        "real": {},
    }
    print(f"synthetic holdout loss={result['synthetic']['loss']:0.4f} acc={result['synthetic']['accuracy']:0.4f}")
    print(f"synthetic per-class recall={result['synthetic']['perClassRecall']}")

    for session in args.real_session:
        scored = real_score(model, checkpoint, device, session, args.real_limit)
        summary = summarize_real(scored)
        result["real"][str(session)] = summary
        print_real_summary(session, summary)

    if args.json_out:
        args.json_out.write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(f"wrote {args.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
