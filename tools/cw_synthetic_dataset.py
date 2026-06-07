from __future__ import annotations

import argparse
import json
import math
import random
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

import numpy as np


MORSE = {
    "A": ".-", "B": "-...", "C": "-.-.", "D": "-..", "E": ".", "F": "..-.",
    "G": "--.", "H": "....", "I": "..", "J": ".---", "K": "-.-", "L": ".-..",
    "M": "--", "N": "-.", "O": "---", "P": ".--.", "Q": "--.-", "R": ".-.",
    "S": "...", "T": "-", "U": "..-", "V": "...-", "W": ".--", "X": "-..-",
    "Y": "-.--", "Z": "--..", "1": ".----", "2": "..---", "3": "...--",
    "4": "....-", "5": ".....", "6": "-....", "7": "--...", "8": "---..",
    "9": "----.", "0": "-----", "/": "-..-.", "=": "-...-", "+": ".-.-.",
    "-": "-....-", "?": "..--..", ".": ".-.-.-",
}

CALL_PREFIXES = [
    "K", "N", "W", "AA", "AB", "AC", "AD", "AE", "AF", "VA", "VE", "XE",
    "PY", "LU", "DL", "F", "G", "I", "JA", "VK", "ZS", "TF", "NP", "KP",
]
CALL_SUFFIXES = ["BP", "KS", "TX", "XZ", "TD", "MM", "AW", "RCR", "XF", "BP", "MN", "MD"]
QTH = ["FL", "IN", "OH", "TX", "CA", "NY", "NC", "AZ", "CO", "ON", "DX"]


def utc_stamp() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def random_call(rng: random.Random) -> str:
    prefix = rng.choice(CALL_PREFIXES)
    digit = str(rng.randint(0, 9))
    suffix = rng.choice(CALL_SUFFIXES)
    if rng.random() < 0.2:
        suffix = "".join(rng.choice("ABCDEFGHIJKLMNOPQRSTUVWXYZ") for _ in range(rng.randint(2, 3)))
    return f"{prefix}{digit}{suffix}"


def random_message(rng: random.Random) -> str:
    my_call = random_call(rng)
    their_call = random_call(rng)
    templates = [
        f"CQ TEST {my_call} {my_call}",
        f"{their_call} 5NN {rng.randint(1, 999):03d}",
        f"{their_call} TU {my_call}",
        f"CQ SST {my_call} {rng.choice(QTH)}",
        f"{their_call} {rng.choice(['BOB', 'KEN', 'AL', 'SAM'])} {rng.choice(QTH)}",
        f"{their_call} DE {my_call} 73",
    ]
    return rng.choice(templates)


def morse_key(text: str, sample_rate: int, wpm: float, rng: random.Random, jitter: float) -> np.ndarray:
    dit_seconds = 1.2 / max(5.0, wpm)
    state: list[float] = []

    def append(value: float, units: float) -> None:
        local_units = max(0.15, units * (1.0 + rng.gauss(0.0, jitter)))
        count = max(1, int(round(local_units * dit_seconds * sample_rate)))
        state.extend([value] * count)

    words = text.upper().split(" ")
    for word_index, word in enumerate(words):
        for char_index, char in enumerate(word):
            code = MORSE.get(char)
            if not code:
                continue
            for elem_index, elem in enumerate(code):
                append(1.0, 1.0 if elem == "." else 3.0)
                if elem_index != len(code) - 1:
                    append(0.0, 1.0)
            if char_index != len(word) - 1:
                append(0.0, 3.0)
        if word_index != len(words) - 1:
            append(0.0, 7.0)

    if not state:
        return np.zeros(sample_rate, dtype=np.float32)
    return np.asarray(state, dtype=np.float32)


def shaped_tone(
    key: np.ndarray,
    sample_rate: int,
    tone_hz: float,
    amp: float,
    rng: random.Random,
    drift_hz: float,
    qsb_depth: float,
) -> np.ndarray:
    ramp = max(8, int(sample_rate * rng.uniform(0.003, 0.008)))
    window = np.ones(ramp * 2, dtype=np.float32)
    window[:ramp] = np.linspace(0.0, 1.0, ramp, dtype=np.float32)
    window[ramp:] = np.linspace(1.0, 0.0, ramp, dtype=np.float32)
    envelope = np.convolve(key, window, mode="same") / max(1.0, float(ramp))
    envelope = np.clip(envelope, 0.0, 1.0)

    t = np.arange(envelope.size, dtype=np.float64) / float(sample_rate)
    drift = np.linspace(0.0, drift_hz, envelope.size, dtype=np.float64)
    phase = 2.0 * np.pi * np.cumsum((tone_hz + drift) / sample_rate)
    qsb_rate = rng.uniform(0.08, 0.45)
    qsb = 1.0 - qsb_depth * (0.5 + 0.5 * np.sin(2.0 * np.pi * qsb_rate * t + rng.random() * 6.28))
    return (amp * envelope * qsb * np.sin(phase)).astype(np.float32)


def add_awgn(audio: np.ndarray, rng: random.Random, snr_db: float, signal_rms: float | None = None) -> np.ndarray:
    rms = signal_rms if signal_rms is not None else float(np.sqrt(np.mean(audio * audio)) + 1e-8)
    noise_rms = rms / (10.0 ** (snr_db / 20.0))
    noise_rng = np.random.default_rng(rng.randrange(2**31))
    return audio + (noise_rms * noise_rng.standard_normal(audio.size)).astype(np.float32)


def add_impulses(audio: np.ndarray, sample_rate: int, rng: random.Random) -> None:
    count = rng.randint(0, 6)
    for _ in range(count):
        start = rng.randrange(0, max(1, audio.size - 1))
        width = rng.randint(8, max(10, int(sample_rate * 0.012)))
        end = min(audio.size, start + width)
        audio[start:end] += np.hanning(end - start).astype(np.float32) * rng.uniform(-0.18, 0.18)


def normalize(audio: np.ndarray) -> np.ndarray:
    peak = float(np.max(np.abs(audio))) if audio.size else 0.0
    if peak > 0.95:
        audio = audio * (0.95 / peak)
    return audio.astype("<f4", copy=False)


def make_cw_positive(sample_rate: int, rng: random.Random) -> tuple[np.ndarray, dict[str, object]]:
    text = random_message(rng)
    wpm = rng.uniform(12.0, 36.0)
    tone_hz = rng.uniform(350.0, 2800.0)
    key = morse_key(text, sample_rate, wpm, rng, jitter=rng.uniform(0.02, 0.18))
    lead = np.zeros(rng.randint(int(0.2 * sample_rate), int(1.2 * sample_rate)), dtype=np.float32)
    tail = np.zeros(rng.randint(int(0.2 * sample_rate), int(1.4 * sample_rate)), dtype=np.float32)
    key = np.concatenate([lead, key, tail])
    audio = shaped_tone(key, sample_rate, tone_hz, rng.uniform(0.06, 0.22), rng, rng.uniform(-8.0, 8.0), rng.uniform(0.0, 0.45))

    interferers = 0
    if rng.random() < 0.38:
        interferers = rng.randint(1, 2)
        for _ in range(interferers):
            other = random_message(rng)
            other_key = morse_key(other, sample_rate, rng.uniform(10.0, 34.0), rng, jitter=rng.uniform(0.03, 0.22))
            if other_key.size < audio.size:
                other_key = np.pad(other_key, (0, audio.size - other_key.size))
            else:
                other_key = other_key[: audio.size]
            offset = rng.choice([-1.0, 1.0]) * rng.uniform(55.0, 520.0)
            audio += shaped_tone(other_key, sample_rate, max(120.0, min(3300.0, tone_hz + offset)), rng.uniform(0.015, 0.12), rng, rng.uniform(-5.0, 5.0), rng.uniform(0.0, 0.5))

    snr_db = rng.uniform(-4.0, 20.0)
    audio = add_awgn(audio, rng, snr_db)
    add_impulses(audio, sample_rate, rng)
    return normalize(audio), {
        "label": "cw_keyed",
        "isCw": True,
        "text": text,
        "toneHz": round(tone_hz, 2),
        "wpm": round(wpm, 2),
        "snrDb": round(snr_db, 2),
        "interferers": interferers,
    }


def make_negative(sample_rate: int, rng: random.Random, label: str) -> tuple[np.ndarray, dict[str, object]]:
    duration = rng.uniform(5.0, 10.0)
    size = int(duration * sample_rate)
    t = np.arange(size, dtype=np.float64) / float(sample_rate)
    audio = np.zeros(size, dtype=np.float32)
    tone_hz = rng.uniform(250.0, 3000.0)

    if label == "solid_carrier":
        amp = rng.uniform(0.03, 0.25)
        drift = np.linspace(0.0, rng.uniform(-2.0, 2.0), size, dtype=np.float64)
        phase = 2.0 * np.pi * np.cumsum((tone_hz + drift) / sample_rate)
        audio = (amp * np.sin(phase)).astype(np.float32)
        audio *= (1.0 + 0.08 * np.sin(2.0 * np.pi * rng.uniform(0.03, 0.3) * t)).astype(np.float32)
    elif label == "rfi_spike":
        amp = rng.uniform(0.04, 0.2)
        for harmonic in range(1, rng.randint(2, 5)):
            audio += (amp / harmonic * np.sin(2.0 * np.pi * (tone_hz * harmonic) * t + rng.random() * 6.28)).astype(np.float32)
        if rng.random() < 0.45:
            gate_rate = rng.uniform(20.0, 90.0)
            audio *= (0.75 + 0.25 * np.sign(np.sin(2.0 * np.pi * gate_rate * t))).astype(np.float32)
    else:
        audio = np.zeros(size, dtype=np.float32)

    audio = add_awgn(audio, rng, rng.uniform(-8.0, 10.0), signal_rms=max(0.02, float(np.sqrt(np.mean(audio * audio)) + 1e-8)))
    add_impulses(audio, sample_rate, rng)
    return normalize(audio), {
        "label": label,
        "isCw": False,
        "text": "",
        "toneHz": round(tone_hz, 2),
        "wpm": None,
        "snrDb": None,
        "interferers": 0,
    }


def write_manifest(path: Path, rows: Iterable[dict[str, object]]) -> None:
    with path.open("w", encoding="utf-8") as handle:
        for row in rows:
            handle.write(json.dumps(row, sort_keys=True) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate dirty synthetic CW/RFI training examples.")
    parser.add_argument("--out", type=Path, default=None)
    parser.add_argument("--count", type=int, default=240)
    parser.add_argument("--sample-rate", type=int, default=48000)
    parser.add_argument("--seed", type=int, default=20260530)
    args = parser.parse_args()

    rng = random.Random(args.seed)
    out = args.out or (Path.home() / "ShackStack" / "cw-training" / f"synthetic-{utc_stamp()}")
    audio_dir = out / "audio"
    audio_dir.mkdir(parents=True, exist_ok=True)

    rows: list[dict[str, object]] = []
    for index in range(args.count):
        roll = rng.random()
        if roll < 0.62:
            audio, meta = make_cw_positive(args.sample_rate, rng)
        elif roll < 0.82:
            audio, meta = make_negative(args.sample_rate, rng, "solid_carrier")
        elif roll < 0.94:
            audio, meta = make_negative(args.sample_rate, rng, "rfi_spike")
        else:
            audio, meta = make_negative(args.sample_rate, rng, "noise")

        sample_id = f"syn-{index:06d}"
        file_name = f"{sample_id}.f32"
        (audio_dir / file_name).write_bytes(audio.tobytes())
        rows.append({
            "type": "cw-synthetic-sample",
            "id": sample_id,
            "audioFile": f"audio/{file_name}",
            "audioFormat": "float32-le",
            "sampleRate": args.sample_rate,
            "channels": 1,
            "durationSeconds": round(audio.size / args.sample_rate, 4),
            **meta,
        })

    header = {
        "type": "cw-synthetic-dataset",
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "sampleCount": len(rows),
        "sampleRate": args.sample_rate,
        "seed": args.seed,
    }
    write_manifest(out / "manifest.jsonl", [header, *rows])
    print(f"wrote {out}")
    print(f"samples={len(rows)} positives={sum(1 for row in rows if row['isCw'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
