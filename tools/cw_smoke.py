from __future__ import annotations

import math
import sys
from pathlib import Path

import numpy as np

REPO_ROOT = Path(__file__).resolve().parents[1]
PY_WORKERS = REPO_ROOT / "src" / "ShackStack.DecoderWorkers.Python"
sys.path.insert(0, str(PY_WORKERS))

from modes.cw_engine import MORSE_ENCODE
from modes.cw_engine_fldigi import CwFldigiDecoder


def generate_cw(text: str, *, sample_rate: int = 48000, tone_hz: int = 700, wpm: int = 20) -> np.ndarray:
    dit = 1.2 / float(wpm)
    samples: list[np.ndarray] = []
    cursor = 0

    def silence(seconds: float) -> None:
        nonlocal cursor
        count = int(round(seconds * sample_rate))
        samples.append(np.zeros(count, dtype=np.float32))
        cursor += count

    def tone(seconds: float) -> None:
        nonlocal cursor
        count = int(round(seconds * sample_rate))
        t = (np.arange(count, dtype=np.float64) + cursor) / float(sample_rate)
        samples.append((0.7 * np.sin(2.0 * math.pi * tone_hz * t)).astype(np.float32))
        cursor += count

    silence(1.0)
    for word_index, word in enumerate(text.upper().split()):
        if word_index:
            silence(7.0 * dit)
        for char_index, char in enumerate(word):
            code = MORSE_ENCODE.get(char)
            if not code:
                continue
            for element_index, element in enumerate(code):
                tone((3.0 if element == "-" else 1.0) * dit)
                if element_index != len(code) - 1:
                    silence(dit)
            if char_index != len(word) - 1:
                silence(3.0 * dit)
    silence(1.0)
    return np.concatenate(samples) if samples else np.array([], dtype=np.float32)


def run_case(text: str, *, tone_hz: int = 700, wpm: int = 20) -> str:
    decoded: list[str] = []
    decoder = CwFldigiDecoder(
        sample_rate=48000,
        tone_hz=float(tone_hz),
        text_callback=lambda event: decoded.append(event.text),
        initial_wpm=wpm,
    )
    decoder.start()
    audio = generate_cw(text, tone_hz=tone_hz, wpm=wpm)
    for offset in range(0, len(audio), 2048):
        decoder.push_samples(audio[offset : offset + 2048])
    decoder.stop()
    return "".join(decoded).strip()


def main() -> int:
    cases = [
        "CQ TEST 73",
        "W8STR DE KE9CRR",
        "599 TU",
    ]
    failed = False
    for case in cases:
        decoded = run_case(case)
        ok = decoded == case
        failed = failed or not ok
        print(f"{'PASS' if ok else 'FAIL'}: {case!r} -> {decoded!r}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
