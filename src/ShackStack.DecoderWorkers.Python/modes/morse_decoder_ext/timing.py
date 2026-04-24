"""
timing.py — Adaptive timing engine with bimodal classification and mark splitting.

DESIGN
──────
The frame-based DSP pipeline introduces a systematic bias:
  • Marks appear ~30 ms longer than nominal
  • Gaps appear ~30 ms shorter than nominal

Three strategies address this:

1. SINGLE-MARK BOOTSTRAP
   After the very first mark, dit_s is immediately hard-set from that
   observation (if dah, divide by 3; if dit, use directly). This gives
   correct WPM from character 1, before the EMA can adapt.

2. BIMODAL MARK CLASSIFIER
   K-means k=2 on mark history. Boundary floats between 1.5× and 2.5× dit_s.

3. BIMODAL GAP CLASSIFIER
   Independent k-means on gap history. Intra/char boundary learned from data,
   not derived from mark timing. Fallback to ratio-based classifier until
   MIN_GAP_SAMPLES are observed.

4. MARK SPLITTER
   When mark > 2.5× nominal dah, try all (n_dits, n_dahs) combos ≤ 5 elements.
   Pick minimum error; tie-break by fewest elements.
"""

from __future__ import annotations

import math
from collections import deque
from dataclasses import dataclass
from enum import Enum, auto
from typing import List, Optional

import numpy as np

from .config import MorseConfig
from .dsp import KeyEvent, KeyState


class ElementType(Enum):
    DIT       = "."
    DAH       = "-"
    INTRA_GAP = "INTRA"
    CHAR_GAP  = "CHAR"
    WORD_GAP  = "WORD"


@dataclass
class MorseElement:
    kind:       ElementType
    duration_s: float
    confidence: float = 1.0


class TimingEngine:
    """
    Adaptive timing engine — converts KeyEvents to MorseElements.
    Feed KeyEvents one at a time; receive zero or more MorseElements.
    """

    _MIN_GAP_SAMPLES  = 4
    _MIN_MARK_SAMPLES = 4
    _SPLIT_THRESHOLD  = 2.5   # × nominal dah before splitting

    def __init__(self, cfg: MorseConfig):
        self._cfg = cfg
        self._dit_s: float = 1.2 / cfg.wpm_initial
        self._marks: deque = deque(maxlen=80)
        self._gaps:  deque = deque(maxlen=80)
        self._dit_dah_boundary:    float           = self._dit_s * 2.0
        self._intra_char_boundary: Optional[float] = None
        self._char_word_boundary:  Optional[float] = None
        self._bootstrapped: bool = False  # True after first mark

    # ── Public ────────────────────────────────────────────────────────────

    def feed(self, event: KeyEvent) -> List[MorseElement]:
        if event.state == KeyState.KEY_DOWN:
            return self._classify_mark(event.duration)
        else:
            return [self._classify_space(event.duration)]

    @property
    def current_wpm(self) -> float:
        return 1.2 / max(self._dit_s, 0.001)

    @property
    def dit_duration(self) -> float:
        return self._dit_s

    def reset(self) -> None:
        self._marks.clear()
        self._gaps.clear()
        self._dit_s               = 1.2 / self._cfg.wpm_initial
        self._dit_dah_boundary    = self._dit_s * 2.0
        self._intra_char_boundary = None
        self._char_word_boundary  = None
        self._bootstrapped        = False

    # ── Mark classification ───────────────────────────────────────────────

    def _classify_mark(self, dur: float) -> List[MorseElement]:
        # Check for merged mark before updating history
        split_threshold = self._dit_s * 3.0 * self._SPLIT_THRESHOLD
        if dur > split_threshold and self._bootstrapped:
            return self._split_mark(dur)

        self._marks.append(dur)
        self._update_mark_timing()

        if dur <= self._dit_dah_boundary:
            conf = self._duration_confidence(dur, self._dit_s, 0.55)
            return [MorseElement(ElementType.DIT, dur, conf)]
        else:
            conf = self._duration_confidence(dur, self._dit_s * 3.0, 0.60)
            return [MorseElement(ElementType.DAH, dur, conf)]

    def _split_mark(self, dur: float) -> List[MorseElement]:
        """Decompose a merged mark into its constituent dits and dahs."""
        dit = self._dit_s
        dah = dit * 3.0
        best_err = float("inf")
        best_seq: List[ElementType] = [ElementType.DAH]

        for n_d in range(0, 6):
            for n_h in range(0, 6):
                total = n_d + n_h
                if total == 0 or total > 5:
                    continue
                expected = n_d * dit + n_h * dah + (total - 1) * dit
                err = abs(dur - expected)
                if err < best_err or (err == best_err and total < len(best_seq)):
                    best_err = err
                    best_seq = [ElementType.DIT] * n_d + [ElementType.DAH] * n_h

        conf = max(0.1, 1.0 - best_err / max(dur, 1e-6))
        per_elem_dur = dur / max(1, len(best_seq))
        elements = [MorseElement(k, per_elem_dur, conf) for k in best_seq]

        for el in elements:
            self._marks.append(el.duration_s)
        self._update_mark_timing()
        return elements

    def _update_mark_timing(self) -> None:
        marks = np.array(self._marks)
        n = len(marks)

        if n == 0:
            return

        if n == 1:
            # Single-mark hard bootstrap — immediately set dit_s from first observation
            m = float(marks[0])
            if m <= self._dit_dah_boundary:
                dit_est = m           # it's a dit
            else:
                dit_est = m / 3.0    # it's a dah → back-estimate dit
            dit_min = 1.2 / self._cfg.wpm_max
            dit_max = 1.2 / self._cfg.wpm_min
            self._dit_s = float(np.clip(dit_est, dit_min, dit_max))
            self._dit_dah_boundary = self._dit_s * 2.0
            self._bootstrapped = True
            return

        if n >= self._MIN_MARK_SAMPLES:
            dit_est = self._bimodal_lower(marks)
        else:
            dit_est = float(np.percentile(marks, 33))

        dit_min = 1.2 / self._cfg.wpm_max
        dit_max = 1.2 / self._cfg.wpm_min
        dit_est = float(np.clip(dit_est, dit_min, dit_max))

        alpha = self._cfg.wpm_adapt_weight
        self._dit_s = (1.0 - alpha) * self._dit_s + alpha * dit_est

        if n >= self._MIN_MARK_SAMPLES:
            valley = self._bimodal_valley(marks)
            boundary = valley if valley is not None else self._dit_s * 2.0
        else:
            boundary = self._dit_s * 2.0

        self._dit_dah_boundary = float(np.clip(
            boundary, self._dit_s * 1.5, self._dit_s * 2.5))

    # ── Gap classification ────────────────────────────────────────────────

    def _classify_space(self, dur: float) -> MorseElement:
        if dur < 10.0:
            self._gaps.append(dur)
            self._update_gap_boundaries()

        intra_b = self._intra_char_boundary
        char_b  = self._char_word_boundary

        if intra_b is not None and char_b is not None:
            # Bimodal classifier active
            if dur <= intra_b:
                conf = self._duration_confidence(dur, self._dit_s, 0.70)
                return MorseElement(ElementType.INTRA_GAP, dur, conf)
            elif dur <= char_b:
                conf = self._duration_confidence(dur, self._dit_s * 3.0, 0.70)
                return MorseElement(ElementType.CHAR_GAP, dur, conf)
            else:
                conf = self._duration_confidence(dur, self._dit_s * 7.0, 0.85)
                return MorseElement(ElementType.WORD_GAP, dur, conf)
        else:
            # Fallback ratio-based (before enough gap data)
            dit = self._dit_s
            if dur <= dit * self._cfg.intra_char_max:
                return MorseElement(ElementType.INTRA_GAP, dur,
                    self._duration_confidence(dur, dit, 0.65))
            elif dur <= dit * self._cfg.inter_char_max:
                return MorseElement(ElementType.CHAR_GAP, dur,
                    self._duration_confidence(dur, dit * 3.0, 0.70))
            else:
                return MorseElement(ElementType.WORD_GAP, dur,
                    self._duration_confidence(dur, dit * 7.0, 0.85))

    def _update_gap_boundaries(self) -> None:
        gaps = np.array(self._gaps)
        if len(gaps) < self._MIN_GAP_SAMPLES:
            return
        boundary = self._bimodal_valley(gaps)
        if boundary is None:
            return
        self._intra_char_boundary = boundary
        long_gaps = gaps[gaps > boundary]
        if len(long_gaps) < 2:
            self._char_word_boundary = boundary * 4.0
            return
        p75 = float(np.percentile(long_gaps, 75))
        self._char_word_boundary = max(p75 * 1.5, boundary * 2.5)

    # ── Bimodal helpers ───────────────────────────────────────────────────

    @staticmethod
    def _bimodal_lower(data: np.ndarray) -> float:
        c_low  = float(np.percentile(data, 25))
        c_high = float(np.percentile(data, 75))
        for _ in range(25):
            lo = np.abs(data - c_low) <= np.abs(data - c_high)
            hi = ~lo
            new_lo  = float(np.mean(data[lo])) if lo.any()  else c_low
            new_hi  = float(np.mean(data[hi])) if hi.any() else c_high
            if abs(new_lo - c_low) < 1e-6 and abs(new_hi - c_high) < 1e-6:
                break
            c_low, c_high = new_lo, new_hi
        lo = np.abs(data - c_low) <= np.abs(data - c_high)
        return float(np.mean(data[lo])) if lo.any() else c_low

    @staticmethod
    def _bimodal_valley(data: np.ndarray) -> Optional[float]:
        mn, mx = data.min(), data.max()
        if mn <= 0 or mx / mn < 1.5:
            return None
        bins = np.linspace(mn, mx, min(len(data) + 1, 30))
        hist, edges = np.histogram(data, bins=bins)
        lo = len(hist) // 5
        hi = 4 * len(hist) // 5
        if lo >= hi:
            return None
        valley_idx = int(np.argmin(hist[lo:hi])) + lo
        return float((edges[valley_idx] + edges[valley_idx + 1]) / 2)

    @staticmethod
    def _duration_confidence(observed: float, nominal: float, tol: float) -> float:
        if nominal <= 0:
            return 0.5
        return float(max(0.1, 1.0 - abs(observed - nominal) / nominal / tol))
