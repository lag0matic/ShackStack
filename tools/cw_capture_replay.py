from __future__ import annotations

import argparse
import json
import sys
import wave
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
from scipy.signal._filter_design import butter
from scipy.signal._signaltools import hilbert, sosfilt

REPO_ROOT = Path(__file__).resolve().parents[1]
PY_WORKERS = REPO_ROOT / "src" / "ShackStack.DecoderWorkers.Python"
sys.path.insert(0, str(PY_WORKERS))

from modes.cw_engine_fldigi import CwFldigiDecoder


@dataclass(frozen=True)
class DecodeVariant:
    wpm: int
    bandwidth_hz: int
    matched_filter: bool
    squelch: str
    tracking_range_wpm: int
    spacing: str = "Normal"

    @property
    def label(self) -> str:
        mf = "mf" if self.matched_filter else "bw"
        return f"wpm{self.wpm}-{mf}{self.bandwidth_hz}-sq{self.squelch}-tr{self.tracking_range_wpm}-sp{self.spacing}"


def load_manifest(session: Path) -> list[dict[str, Any]]:
    samples: list[dict[str, Any]] = []
    with (session / "manifest.jsonl").open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            payload = json.loads(line)
            if payload.get("type") == "cw-skimmer-sample":
                samples.append(payload)
    return samples


def load_audio(session: Path, sample: dict[str, Any]) -> np.ndarray:
    path = session / str(sample["audioFile"]).replace("/", "\\")
    raw = np.fromfile(path, dtype="<f4")
    channels = int(sample.get("channels", 1))
    if channels > 1 and raw.size >= channels:
        usable = (raw.size // channels) * channels
        return raw[:usable].reshape(-1, channels).mean(axis=1).astype(np.float32, copy=False)
    return raw.astype(np.float32, copy=False)


def lane_isolate(
    audio: np.ndarray,
    *,
    sample_rate: int,
    tone_hz: float,
    target_hz: float = 700.0,
    bandwidth_hz: int = 260,
) -> np.ndarray:
    if audio.size == 0:
        return audio

    audio64 = audio.astype(np.float64, copy=False)
    t = np.arange(audio64.size, dtype=np.float64) / float(sample_rate)
    analytic = hilbert(audio64)
    shifted = np.real(analytic * np.exp(2j * np.pi * (target_hz - tone_hz) * t))
    half_width = max(35.0, bandwidth_hz / 2.0)
    low = max(20.0, target_hz - half_width) / (sample_rate / 2.0)
    high = min(sample_rate / 2.0 - 20.0, target_hz + half_width) / (sample_rate / 2.0)
    sos = butter(4, [low, high], btype="bandpass", output="sos")
    filtered = sosfilt(sos, shifted)
    return filtered.astype(np.float32, copy=False)


def decode_audio(audio: np.ndarray, *, sample_rate: int, tone_hz: float, variant: DecodeVariant) -> tuple[str, Any]:
    decoded: list[str] = []
    decoder = CwFldigiDecoder(
        sample_rate=sample_rate,
        tone_hz=tone_hz,
        text_callback=lambda event: decoded.append(str(getattr(event, "text", ""))),
        initial_wpm=variant.wpm,
        bandwidth_hz=variant.bandwidth_hz,
        matched_filter_enabled=variant.matched_filter,
        tracking_enabled=True,
        tracking_range_wpm=variant.tracking_range_wpm,
        lower_wpm_limit=5,
        upper_wpm_limit=60,
        auto_tone_search_enabled=False,
        afc_enabled=False,
        squelch=variant.squelch,
        spacing=variant.spacing,
        noise_character="Suppress",
    )
    decoder.start()
    for offset in range(0, audio.size, 2048):
        decoder.push_samples(audio[offset : offset + 2048])
    decoder.stop()
    return "".join(decoded).strip(), decoder.stats


def score_text(text: str, hints: list[str]) -> int:
    upper = text.upper()
    score = 0
    for hint in hints:
        if hint and hint.upper() in upper:
            score += 10 + min(10, len(hint))
    score += sum(ch.isdigit() for ch in upper)
    score += upper.count("CQ") * 5
    score += upper.count("TU") * 3
    score += upper.count("73") * 3
    return score


def default_variants() -> list[DecodeVariant]:
    return [
        DecodeVariant(15, 220, True, "Low", 14, "Tight"),
        DecodeVariant(15, 220, True, "Low", 14),
        DecodeVariant(15, 220, True, "Low", 14, "Loose"),
        DecodeVariant(15, 220, True, "Low", 18, "Slow"),
        DecodeVariant(18, 180, True, "Low", 8),
        DecodeVariant(20, 220, True, "Low", 14, "Tight"),
        DecodeVariant(20, 220, True, "Low", 8),
        DecodeVariant(20, 220, True, "Low", 14),
        DecodeVariant(20, 220, True, "Low", 14, "Loose"),
        DecodeVariant(20, 220, True, "Low", 18, "Slow"),
        DecodeVariant(24, 260, True, "Low", 10),
        DecodeVariant(28, 320, True, "Low", 12),
        DecodeVariant(20, 180, False, "Low", 8),
        DecodeVariant(24, 260, False, "Low", 10),
        DecodeVariant(20, 220, True, "Off", 8),
        DecodeVariant(24, 260, True, "Medium", 10),
    ]


def select_samples(samples: list[dict[str, Any]], args: argparse.Namespace) -> list[dict[str, Any]]:
    selected = samples
    if args.lane:
        lanes = set(args.lane)
        selected = [item for item in selected if int(item.get("laneId", 0)) in lanes]
    if args.min_confidence is not None:
        selected = [item for item in selected if float(item.get("confidence", 0.0)) >= args.min_confidence]
    selected.sort(
        key=lambda item: (
            -len(str(item.get("laneTranscript", ""))),
            -float(item.get("confidence", 0.0)),
            str(item.get("capturedUtc", "")),
        )
    )
    return selected[: args.top]


def write_wav(path: Path, audio: np.ndarray, sample_rate: int) -> None:
    normalized = audio
    peak = float(np.max(np.abs(normalized))) if normalized.size else 0.0
    if peak > 0.98:
        normalized = normalized / peak * 0.98
    pcm = (np.clip(normalized, -1.0, 1.0) * 32767.0).astype("<i2")
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(sample_rate)
        handle.writeframes(pcm.tobytes())


def main() -> int:
    parser = argparse.ArgumentParser(description="Replay saved CW skimmer captures through decoder variants.")
    parser.add_argument("session", type=Path)
    parser.add_argument("--lane", type=int, action="append")
    parser.add_argument("--top", type=int, default=8)
    parser.add_argument("--min-confidence", type=float, default=0.5)
    parser.add_argument("--hint", action="append", default=[])
    parser.add_argument("--isolate", action="store_true", help="Frequency-shift lane to 700 Hz before decoding.")
    parser.add_argument("--export-isolated", action="store_true")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    samples = select_samples(load_manifest(args.session), args)
    variants = default_variants()
    out_root = args.out or (args.session / "replay")

    for sample in samples:
        sample_rate = int(sample.get("sampleRate", 48000))
        tone_hz = float(sample.get("audioToneHz", 700.0))
        audio = load_audio(args.session, sample)
        decode_tone = tone_hz
        replay_audio = audio
        if args.isolate:
            replay_audio = lane_isolate(audio, sample_rate=sample_rate, tone_hz=tone_hz)
            decode_tone = 700.0
            if args.export_isolated:
                write_wav(out_root / f"{sample['id']}-{tone_hz:0.0f}Hz-isolated.wav", replay_audio, sample_rate)

        print()
        print(f"{sample['id']} lane={sample.get('laneId')} tone={tone_hz:0.1f}Hz conf={float(sample.get('confidence', 0.0)):0.2f}")
        print(f"manifest: {str(sample.get('laneTranscript', ''))[:160]!r}")
        best: tuple[int, str, str] | None = None
        for variant in variants:
            text, stats = decode_audio(replay_audio, sample_rate=sample_rate, tone_hz=decode_tone, variant=variant)
            hints = args.hint or str(sample.get("laneTranscript", "")).split()[:6]
            score = score_text(text, hints)
            if best is None or score > best[0]:
                best = (score, variant.label, text)
            print(f"  {variant.label:<28} score={score:>3} stats={stats.estimated_wpm:0.1f}wpm/{stats.confidence:0.2f} :: {text[:140]!r}")
        if best:
            print(f"  best {best[1]} score={best[0]} :: {best[2][:160]!r}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
