from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

import numpy as np
from scipy.signal import butter, hilbert, sosfilt


FEATURE_NAMES = [
    "rms",
    "crest",
    "spectral_flatness",
    "peak_prominence_db",
    "peak_hz",
    "envelope_cv",
    "envelope_duty",
    "envelope_transition_rate",
    "envelope_entropy",
    "longest_on_fraction",
]


def load_manifest(dataset: Path) -> list[dict[str, Any]]:
    manifest = dataset / "manifest.jsonl"
    rows: list[dict[str, Any]] = []
    with manifest.open("r", encoding="utf-8") as handle:
        for line in handle:
            payload = json.loads(line)
            if payload.get("type") in {"cw-synthetic-sample", "cw-skimmer-sample"}:
                rows.append(payload)
    return rows


def load_audio(dataset: Path, row: dict[str, Any]) -> np.ndarray:
    audio_path = dataset / str(row["audioFile"]).replace("/", "\\")
    audio = np.fromfile(audio_path, dtype="<f4")
    channels = int(row.get("channels") or 1)
    if channels > 1 and audio.size >= channels:
        audio = audio.reshape((-1, channels)).mean(axis=1)
    if audio.size > 48000 * 10:
        audio = audio[-48000 * 10 :]
    return np.nan_to_num(audio.astype(np.float32, copy=False))


def safe_entropy(values: np.ndarray, bins: int = 16) -> float:
    hist, _ = np.histogram(values, bins=bins, range=(0.0, 1.0))
    total = float(np.sum(hist))
    if total <= 0.0:
        return 0.0
    p = hist.astype(np.float64) / total
    p = p[p > 0.0]
    return float(-np.sum(p * np.log2(p)) / math.log2(bins))


def longest_run_fraction(mask: np.ndarray) -> float:
    best = 0
    current = 0
    for value in mask:
        if value:
            current += 1
            best = max(best, current)
        else:
            current = 0
    return float(best / max(1, mask.size))


def extract_features(audio: np.ndarray, sample_rate: int, tone_hz: float | None = None) -> np.ndarray:
    if audio.size < sample_rate // 2:
        audio = np.pad(audio, (0, sample_rate // 2 - audio.size))
    audio = audio - float(np.mean(audio))
    rms = float(np.sqrt(np.mean(audio * audio)) + 1e-9)
    peak = float(np.max(np.abs(audio)) + 1e-9)

    nfft = 1 << int(np.ceil(np.log2(min(audio.size, sample_rate * 4))))
    segment = audio[:nfft]
    if segment.size < nfft:
        segment = np.pad(segment, (0, nfft - segment.size))
    segment = segment * np.hanning(nfft).astype(np.float32)
    spectrum = np.abs(np.fft.rfft(segment)) + 1e-12
    freqs = np.fft.rfftfreq(nfft, 1.0 / sample_rate)
    band = (freqs >= 180.0) & (freqs <= min(3400.0, sample_rate / 2.0 - 100.0))
    band_spectrum = spectrum[band]
    band_freqs = freqs[band]
    peak_index = int(np.argmax(band_spectrum)) if band_spectrum.size else 0
    detected_peak_hz = float(band_freqs[peak_index]) if band_freqs.size else 0.0
    peak_hz = float(tone_hz) if tone_hz and tone_hz > 0.0 else detected_peak_hz
    median_power = float(np.median(band_spectrum) + 1e-12)
    if band_spectrum.size:
        local = band_spectrum[max(0, peak_index - 3) : min(band_spectrum.size, peak_index + 4)]
        if tone_hz and tone_hz > 0.0 and band_freqs.size:
            tone_index = int(np.argmin(np.abs(band_freqs - tone_hz)))
            local = band_spectrum[max(0, tone_index - 3) : min(band_spectrum.size, tone_index + 4)]
        peak_prominence_db = float(20.0 * np.log10(float(np.max(local)) / median_power))
    else:
        peak_prominence_db = 0.0
    spectral_flatness = float(np.exp(np.mean(np.log(band_spectrum))) / (np.mean(band_spectrum) + 1e-12)) if band_spectrum.size else 1.0

    low = max(80.0, peak_hz - 180.0)
    high = min(sample_rate / 2.0 - 100.0, peak_hz + 180.0)
    if high <= low + 20.0:
        env = np.abs(audio)
    else:
        sos = butter(4, [low, high], btype="bandpass", fs=sample_rate, output="sos")
        filtered = sosfilt(sos, audio).astype(np.float32)
        env = np.abs(hilbert(filtered))

    smooth_len = max(1, int(sample_rate * 0.012))
    if smooth_len > 1:
        kernel = np.ones(smooth_len, dtype=np.float32) / smooth_len
        env = np.convolve(env, kernel, mode="same")
    env = env / (float(np.percentile(env, 98.0)) + 1e-9)
    env = np.clip(env, 0.0, 1.0)
    threshold = max(0.18, min(0.58, float(np.median(env) + 0.75 * np.std(env))))
    key_mask = env > threshold
    transitions = np.count_nonzero(key_mask[1:] != key_mask[:-1])
    duration = audio.size / float(sample_rate)

    return np.asarray(
        [
            rms,
            peak / rms,
            spectral_flatness,
            peak_prominence_db,
            peak_hz,
            float(np.std(env) / (np.mean(env) + 1e-9)),
            float(np.mean(key_mask)),
            float(transitions / max(0.1, duration)),
            safe_entropy(env),
            longest_run_fraction(key_mask),
        ],
        dtype=np.float64,
    )


def sigmoid(x: np.ndarray) -> np.ndarray:
    return 1.0 / (1.0 + np.exp(-np.clip(x, -50.0, 50.0)))


def fit_logistic(x: np.ndarray, y: np.ndarray, epochs: int, lr: float) -> tuple[np.ndarray, float]:
    weights = np.zeros(x.shape[1], dtype=np.float64)
    bias = 0.0
    for _ in range(epochs):
        pred = sigmoid(x @ weights + bias)
        error = pred - y
        weights -= lr * ((x.T @ error) / y.size + 0.002 * weights)
        bias -= lr * float(np.mean(error))
    return weights, bias


def metrics(prob: np.ndarray, y: np.ndarray) -> dict[str, float]:
    pred = prob >= 0.5
    truth = y >= 0.5
    tp = int(np.sum(pred & truth))
    tn = int(np.sum(~pred & ~truth))
    fp = int(np.sum(pred & ~truth))
    fn = int(np.sum(~pred & truth))
    return {
        "accuracy": float((tp + tn) / max(1, y.size)),
        "precision": float(tp / max(1, tp + fp)),
        "recall": float(tp / max(1, tp + fn)),
        "falsePositiveRate": float(fp / max(1, fp + tn)),
        "tp": tp,
        "tn": tn,
        "fp": fp,
        "fn": fn,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Train a tiny CW-keyed-vs-RFI baseline model.")
    parser.add_argument("dataset", type=Path)
    parser.add_argument("--out", type=Path, default=None)
    parser.add_argument("--epochs", type=int, default=650)
    parser.add_argument("--lr", type=float, default=0.09)
    parser.add_argument("--seed", type=int, default=20260530)
    args = parser.parse_args()

    rows = load_manifest(args.dataset)
    if not rows:
        print(f"no sample rows found in {args.dataset}")
        return 1

    features = []
    labels = []
    ids = []
    for row in rows:
        audio = load_audio(args.dataset, row)
        sample_rate = int(row.get("sampleRate") or row.get("sessionSampleRate") or 48000)
        tone_hz = row.get("toneHz") or row.get("audioToneHz")
        features.append(extract_features(audio, sample_rate, float(tone_hz or 0.0)))
        labels.append(1.0 if bool(row.get("isCw")) else 0.0)
        ids.append(str(row.get("id", "")))

    x = np.vstack(features)
    y = np.asarray(labels, dtype=np.float64)
    rng = np.random.default_rng(args.seed)
    order = rng.permutation(y.size)
    split = max(1, int(0.8 * y.size))
    train_idx = order[:split]
    test_idx = order[split:] if split < y.size else order[:]

    mean = np.mean(x[train_idx], axis=0)
    std = np.std(x[train_idx], axis=0) + 1e-9
    xz = (x - mean) / std
    weights, bias = fit_logistic(xz[train_idx], y[train_idx], args.epochs, args.lr)
    train_prob = sigmoid(xz[train_idx] @ weights + bias)
    test_prob = sigmoid(xz[test_idx] @ weights + bias)

    output = {
        "type": "cw-presence-baseline",
        "sourceDataset": str(args.dataset),
        "featureNames": FEATURE_NAMES,
        "mean": mean.tolist(),
        "std": std.tolist(),
        "weights": weights.tolist(),
        "bias": bias,
        "threshold": 0.5,
        "trainMetrics": metrics(train_prob, y[train_idx]),
        "testMetrics": metrics(test_prob, y[test_idx]),
    }

    out = args.out or (args.dataset / "cw_presence_baseline.json")
    out.write_text(json.dumps(output, indent=2), encoding="utf-8")
    print(f"wrote {out}")
    print(f"train {output['trainMetrics']}")
    print(f"test  {output['testMetrics']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
