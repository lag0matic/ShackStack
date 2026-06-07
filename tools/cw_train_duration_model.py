from __future__ import annotations

import argparse
import json
import math
import random
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
import torch
from torch import nn
from torch.nn import functional as F

from cw_synthetic_dataset import add_awgn, add_impulses, random_message, shaped_tone


LABELS = ["gap", "dit", "dah", "bad_lane"]


def utc_stamp() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def append_segment(
    key: list[float],
    labels: list[int],
    value: float,
    label: int,
    units: float,
    dit_samples: int,
    rng: random.Random,
    jitter: float,
) -> None:
    count = max(1, int(round(units * dit_samples * max(0.15, 1.0 + rng.gauss(0.0, jitter)))))
    key.extend([value] * count)
    labels.extend([label] * count)


def morse_key_with_labels(text: str, sample_rate: int, wpm: float, rng: random.Random, jitter: float) -> tuple[np.ndarray, np.ndarray]:
    from cw_synthetic_dataset import MORSE

    dit_samples = max(1, int(round((1.2 / max(5.0, wpm)) * sample_rate)))
    key: list[float] = []
    labels: list[int] = []
    words = text.upper().split(" ")
    for word_index, word in enumerate(words):
        for char_index, char in enumerate(word):
            code = MORSE.get(char)
            if not code:
                continue
            for elem_index, elem in enumerate(code):
                append_segment(key, labels, 1.0, 1 if elem == "." else 2, 1.0 if elem == "." else 3.0, dit_samples, rng, jitter)
                if elem_index != len(code) - 1:
                    append_segment(key, labels, 0.0, 0, 1.0, dit_samples, rng, jitter)
            if char_index != len(word) - 1:
                append_segment(key, labels, 0.0, 0, 3.0, dit_samples, rng, jitter)
        if word_index != len(words) - 1:
            append_segment(key, labels, 0.0, 0, 7.0, dit_samples, rng, jitter)

    if not key:
        return np.zeros(sample_rate, dtype=np.float32), np.zeros(sample_rate, dtype=np.int64)
    return np.asarray(key, dtype=np.float32), np.asarray(labels, dtype=np.int64)


def frame_labels(sample_labels: np.ndarray, sample_rate: int, frame_hz: int, frames: int) -> np.ndarray:
    hop = max(1, int(round(sample_rate / frame_hz)))
    output = np.zeros(frames, dtype=np.int64)
    for index in range(frames):
        start = index * hop
        end = min(sample_labels.size, start + hop)
        if start >= sample_labels.size:
            break
        counts = np.bincount(sample_labels[start:end], minlength=len(LABELS))
        output[index] = int(np.argmax(counts))
    return output


def envelope_features(audio: np.ndarray, frame_hz: int, frames: int) -> np.ndarray:
    hop = max(1, int(round(audio.size / frames)))
    env = np.abs(audio.astype(np.float32))
    rms = np.zeros(frames, dtype=np.float32)
    peak = np.zeros(frames, dtype=np.float32)
    zcr = np.zeros(frames, dtype=np.float32)
    for index in range(frames):
        start = index * hop
        end = min(audio.size, start + hop)
        block = env[start:end]
        raw = audio[start:end]
        if block.size:
            rms[index] = float(np.sqrt(np.mean(block * block)))
            peak[index] = float(np.max(block))
            signs = np.signbit(raw)
            zcr[index] = float(np.mean(signs[1:] != signs[:-1])) if raw.size > 1 else 0.0

    def normalize_feature(values: np.ndarray) -> np.ndarray:
        scale = float(np.percentile(values, 98.0) + 1e-6)
        values = np.clip(values / scale, 0.0, 3.0)
        return values.astype(np.float32)

    rms = normalize_feature(rms)
    peak = normalize_feature(peak)
    delta = np.concatenate([[0.0], np.diff(rms)]).astype(np.float32)
    slow = np.convolve(rms, np.ones(max(1, frame_hz // 10), dtype=np.float32) / max(1, frame_hz // 10), mode="same")
    return np.stack([rms, peak, delta, slow.astype(np.float32), zcr], axis=0)


def fixed_length(audio: np.ndarray, labels: np.ndarray, size: int, rng: random.Random) -> tuple[np.ndarray, np.ndarray]:
    if audio.size > size:
        start = rng.randrange(0, audio.size - size + 1)
        return audio[start : start + size], labels[start : start + size]
    if audio.size < size:
        pad = size - audio.size
        left = rng.randrange(0, pad + 1)
        right = pad - left
        audio = np.pad(audio, (left, right))
        labels = np.pad(labels, (left, right))
    return audio, labels


def make_positive(sample_rate: int, seconds: float, frame_hz: int, rng: random.Random) -> tuple[np.ndarray, np.ndarray, dict[str, object]]:
    text = random_message(rng)
    wpm = rng.uniform(10.0, 38.0)
    tone_hz = rng.uniform(350.0, 2800.0)
    key, sample_labels = morse_key_with_labels(text, sample_rate, wpm, rng, jitter=rng.uniform(0.02, 0.22))
    lead = rng.randint(int(0.05 * sample_rate), int(1.0 * sample_rate))
    tail = rng.randint(int(0.05 * sample_rate), int(1.0 * sample_rate))
    key = np.pad(key, (lead, tail))
    sample_labels = np.pad(sample_labels, (lead, tail))
    audio = shaped_tone(key, sample_rate, tone_hz, rng.uniform(0.05, 0.24), rng, rng.uniform(-10.0, 10.0), rng.uniform(0.0, 0.55))

    if rng.random() < 0.45:
        other_key, _ = morse_key_with_labels(random_message(rng), sample_rate, rng.uniform(10.0, 34.0), rng, jitter=rng.uniform(0.02, 0.2))
        other_key, _other_labels = fixed_length(other_key, np.zeros_like(other_key, dtype=np.int64), audio.size, rng)
        offset = rng.choice([-1.0, 1.0]) * rng.uniform(70.0, 620.0)
        audio += shaped_tone(other_key, sample_rate, max(120.0, min(3300.0, tone_hz + offset)), rng.uniform(0.01, 0.12), rng, rng.uniform(-8.0, 8.0), rng.uniform(0.0, 0.5))

    audio = add_awgn(audio, rng, rng.uniform(-8.0, 22.0))
    add_impulses(audio, sample_rate, rng)
    audio, sample_labels = fixed_length(audio, sample_labels, int(seconds * sample_rate), rng)
    frames = int(seconds * frame_hz)
    return envelope_features(audio, frame_hz, frames), frame_labels(sample_labels, sample_rate, frame_hz, frames), {
        "kind": "cw",
        "text": text,
        "wpm": round(wpm, 2),
        "toneHz": round(tone_hz, 2),
    }


def make_negative(sample_rate: int, seconds: float, frame_hz: int, rng: random.Random) -> tuple[np.ndarray, np.ndarray, dict[str, object]]:
    size = int(seconds * sample_rate)
    t = np.arange(size, dtype=np.float64) / float(sample_rate)
    tone_hz = rng.uniform(250.0, 3000.0)
    kind = rng.choice(["solid_carrier", "rfi_spike", "noise"])
    audio = np.zeros(size, dtype=np.float32)
    if kind == "solid_carrier":
        phase = 2.0 * np.pi * np.cumsum((tone_hz + np.linspace(0.0, rng.uniform(-3.0, 3.0), size)) / sample_rate)
        audio = (rng.uniform(0.03, 0.25) * np.sin(phase)).astype(np.float32)
    elif kind == "rfi_spike":
        for harmonic in range(1, rng.randint(2, 5)):
            audio += (rng.uniform(0.025, 0.12) / harmonic * np.sin(2.0 * np.pi * tone_hz * harmonic * t + rng.random() * 6.28)).astype(np.float32)
        audio *= (0.8 + 0.2 * np.sin(2.0 * np.pi * rng.uniform(15.0, 90.0) * t)).astype(np.float32)

    audio = add_awgn(audio, rng, rng.uniform(-10.0, 12.0), signal_rms=max(0.02, float(np.sqrt(np.mean(audio * audio)) + 1e-8)))
    add_impulses(audio, sample_rate, rng)
    frames = int(seconds * frame_hz)
    return envelope_features(audio, frame_hz, frames), np.full(frames, 3, dtype=np.int64), {
        "kind": kind,
        "toneHz": round(tone_hz, 2),
    }


def make_batch(batch_size: int, sample_rate: int, seconds: float, frame_hz: int, rng: random.Random) -> tuple[torch.Tensor, torch.Tensor]:
    xs: list[np.ndarray] = []
    ys: list[np.ndarray] = []
    for _ in range(batch_size):
        if rng.random() < 0.72:
            x, y, _ = make_positive(sample_rate, seconds, frame_hz, rng)
        else:
            x, y, _ = make_negative(sample_rate, seconds, frame_hz, rng)
        xs.append(x)
        ys.append(y)
    return torch.from_numpy(np.stack(xs)), torch.from_numpy(np.stack(ys))


def load_real_manifest(path: Path, split: str) -> list[dict[str, Any]]:
    if not path:
        return []
    rows: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            payload = json.loads(line)
            if payload.get("type") != "cw-real-curated-sample":
                continue
            if str(payload.get("split", "train")) != split:
                continue
            label = str(payload.get("label", ""))
            if label not in {"positive_keyed_cw", "weak_positive_keyed_cw", "contaminated_keyed_cw", "hard_negative_psu", "hard_negative_rfi"}:
                continue
            payload["_manifestRoot"] = str(path.parent)
            rows.append(payload)
    return rows


def real_bad_lane_target(label: str) -> float:
    if label in {"hard_negative_psu", "hard_negative_rfi"}:
        return 1.0
    if label == "contaminated_keyed_cw":
        return 0.15
    return 0.0


def load_real_audio_features(row: dict[str, Any], frame_hz: int, frames: int) -> np.ndarray:
    audio_path = Path(str(row["audioFile"]))
    if not audio_path.is_absolute():
        audio_path = Path(str(row.get("_manifestRoot", "."))) / audio_path
    audio = np.fromfile(audio_path, dtype="<f4")
    audio = np.nan_to_num(audio.astype(np.float32, copy=False))
    target_samples = int(round(float(row.get("sampleRate", 12000)) * float(row.get("durationSeconds", 8.0))))
    if audio.size > target_samples:
        audio = audio[:target_samples]
    elif audio.size < target_samples:
        audio = np.pad(audio, (0, target_samples - audio.size))
    return envelope_features(audio, frame_hz, frames)


def make_real_batch(
    rows: list[dict[str, Any]],
    batch_size: int,
    frame_hz: int,
    frames: int,
    rng: random.Random,
) -> tuple[torch.Tensor, torch.Tensor] | None:
    if not rows or batch_size <= 0:
        return None
    xs: list[np.ndarray] = []
    ys: list[float] = []
    for _ in range(batch_size):
        row = rows[rng.randrange(len(rows))]
        try:
            xs.append(load_real_audio_features(row, frame_hz, frames))
            ys.append(real_bad_lane_target(str(row.get("label", ""))))
        except Exception:
            continue
    if not xs:
        return None
    return torch.from_numpy(np.stack(xs)), torch.tensor(ys, dtype=torch.float32)


class DurationCnn(nn.Module):
    def __init__(self, channels: int, classes: int) -> None:
        super().__init__()
        self.net = nn.Sequential(
            nn.Conv1d(channels, 48, kernel_size=7, padding=3),
            nn.BatchNorm1d(48),
            nn.SiLU(),
            nn.Conv1d(48, 64, kernel_size=7, padding=6, dilation=2),
            nn.BatchNorm1d(64),
            nn.SiLU(),
            nn.Conv1d(64, 96, kernel_size=7, padding=12, dilation=4),
            nn.BatchNorm1d(96),
            nn.SiLU(),
            nn.Conv1d(96, 96, kernel_size=7, padding=24, dilation=8),
            nn.BatchNorm1d(96),
            nn.SiLU(),
            nn.Conv1d(96, classes, kernel_size=1),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.net(x)


def save_checkpoint(path: Path, model: nn.Module, optimizer: torch.optim.Optimizer, args: argparse.Namespace, step: int, loss: float, accuracy: float) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    torch.save(
        {
            "type": "cw-duration-model",
            "createdUtc": datetime.now(timezone.utc).isoformat(),
            "step": step,
            "loss": loss,
            "accuracy": accuracy,
            "labels": LABELS,
            "config": {
                "sampleRate": args.sample_rate,
                "seconds": args.seconds,
                "frameHz": args.frame_hz,
                "inputChannels": 5,
            },
            "modelState": model.state_dict(),
            "optimizerState": optimizer.state_dict(),
        },
        path,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Train the first duration-aware CW neural timing model.")
    parser.add_argument("--out", type=Path, default=None)
    parser.add_argument("--steps", type=int, default=2000)
    parser.add_argument("--batch-size", type=int, default=96)
    parser.add_argument("--sample-rate", type=int, default=12000)
    parser.add_argument("--seconds", type=float, default=8.0)
    parser.add_argument("--frame-hz", type=int, default=100)
    parser.add_argument("--lr", type=float, default=0.0015)
    parser.add_argument("--seed", type=int, default=20260530)
    parser.add_argument("--save-every", type=int, default=250)
    parser.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    parser.add_argument("--real-manifest", type=Path)
    parser.add_argument("--real-batch-size", type=int, default=0)
    parser.add_argument("--real-loss-weight", type=float, default=0.35)
    args = parser.parse_args()

    out_dir = args.out or (Path.home() / "ShackStack" / "cw-training" / f"duration-{utc_stamp()}")
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "run_config.json").write_text(json.dumps(vars(args) | {"out": str(out_dir)}, indent=2, default=str), encoding="utf-8")

    rng = random.Random(args.seed)
    device = torch.device(args.device)
    model = DurationCnn(channels=5, classes=len(LABELS)).to(device)
    optimizer = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=0.0005)
    class_weights = torch.tensor([0.55, 1.6, 1.25, 0.9], dtype=torch.float32, device=device)
    real_rows = load_real_manifest(args.real_manifest, "train") if args.real_manifest else []

    print(f"device={device}")
    if device.type == "cuda":
        print(f"gpu={torch.cuda.get_device_name(0)}")
    print(f"out={out_dir}")
    if args.real_manifest:
        print(f"real_manifest={args.real_manifest} train_rows={len(real_rows)} real_batch_size={args.real_batch_size}")

    best_acc = 0.0
    for step in range(1, args.steps + 1):
        model.train()
        x, y = make_batch(args.batch_size, args.sample_rate, args.seconds, args.frame_hz, rng)
        x = x.to(device, non_blocking=True)
        y = y.to(device, non_blocking=True)
        logits = model(x)
        loss = F.cross_entropy(logits, y, weight=class_weights)
        real_loss_value = 0.0
        if real_rows and args.real_batch_size > 0 and args.real_loss_weight > 0.0:
            real_batch = make_real_batch(real_rows, args.real_batch_size, args.frame_hz, int(args.seconds * args.frame_hz), rng)
            if real_batch is not None:
                real_x, real_target = real_batch
                real_x = real_x.to(device, non_blocking=True)
                real_target = real_target.to(device, non_blocking=True)
                real_logits = model(real_x)
                bad_lane_probability = torch.softmax(real_logits, dim=1)[:, 3, :].mean(dim=1)
                real_loss = F.binary_cross_entropy(bad_lane_probability, real_target)
                real_loss_value = float(real_loss.detach().cpu())
                loss = loss + (args.real_loss_weight * real_loss)
        optimizer.zero_grad(set_to_none=True)
        loss.backward()
        torch.nn.utils.clip_grad_norm_(model.parameters(), 2.0)
        optimizer.step()

        if step == 1 or step % 25 == 0 or step == args.steps:
            with torch.no_grad():
                pred = torch.argmax(logits, dim=1)
                acc = float((pred == y).float().mean().detach().cpu())
                loss_value = float(loss.detach().cpu())
            real_suffix = f" real_bce={real_loss_value:0.4f}" if real_loss_value else ""
            print(f"step={step:>6} loss={loss_value:0.4f} frame_acc={acc:0.4f}{real_suffix}")
            if acc > best_acc:
                best_acc = acc
                save_checkpoint(out_dir / "best.pt", model, optimizer, args, step, loss_value, acc)

        if step % args.save_every == 0 or step == args.steps:
            save_checkpoint(out_dir / f"checkpoint-{step:06d}.pt", model, optimizer, args, step, float(loss.detach().cpu()), best_acc)

    save_checkpoint(out_dir / "final.pt", model, optimizer, args, args.steps, float(loss.detach().cpu()), best_acc)
    print(f"saved final model: {out_dir / 'final.pt'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
