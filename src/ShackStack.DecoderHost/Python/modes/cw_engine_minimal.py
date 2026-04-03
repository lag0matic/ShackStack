from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np

from modes.cw_engine import CwDecodeEvent, MORSE_DECODE


@dataclass
class MinimalCwStats:
    tone_hz: float
    tracked_tone_hz: float
    wpm: int
    estimated_wpm: float
    estimated_dit_ms: float
    dit_ms: float
    dah_ms: float
    char_gap_ms: float
    word_gap_ms: float
    threshold_hi: float
    threshold_lo: float
    peak: float
    floor: float
    marks_seen: int
    gaps_seen: int


class CwMinimalDecoder:
    """
    Deliberately small CW decoder for clean, machine-generated synthetic audio.

    Design goals:
    - fixed tone
    - fixed timing from the configured WPM
    - no dictionary or phrase repair
    - no adaptive word reconstruction
    - only minimal local tone search around the requested pitch
    """

    def __init__(
        self,
        sample_rate: int = 48000,
        tone_hz: float = 700.0,
        text_callback=None,
        initial_wpm: int = 18,
        use_goertzel: bool = True,
    ):
        self._sr = int(sample_rate)
        self._tone_hz = float(tone_hz)
        self._callback = text_callback
        self._use_goertzel = bool(use_goertzel)
        self._running = False
        self._block_ms = 8.0
        self._block_size = max(8, int(round(self._sr * self._block_ms / 1000.0)))
        self._tone_search_offsets = np.array([-20.0, -10.0, 0.0, 10.0, 20.0], dtype=np.float64)
        self.set_initial_wpm(initial_wpm)
        self.reset()

    def start(self):
        self._running = True

    def stop(self):
        self._running = False
        self._flush_pending()

    def reset(self):
        self._sample_index = 0
        self._sample_buf = np.array([], dtype=np.float32)
        self._key_down = False
        self._on_blocks = 0
        self._off_blocks = 0
        self._low_run_blocks = 0
        self._current = ""
        self._tracked_tone_hz = self._tone_hz
        self._peak = 0.10
        self._floor = 0.0
        self._threshold_hi = 0.055
        self._threshold_lo = 0.035
        self._marks_seen = 0
        self._gaps_seen = 0
        self._recent_mark_samples: list[int] = []

    def set_initial_wpm(self, value: int):
        self._wpm = max(5, min(60, int(value)))
        self._dit_samples = max(1, int(round(self._sr * 1.2 / self._wpm)))
        self._dot_dash_split = self._dit_samples * 2.0
        self._char_gap_threshold = self._dit_samples * 1.5
        self._word_gap_threshold = self._dit_samples * 3.25

    @property
    def stats(self) -> MinimalCwStats:
        return MinimalCwStats(
            tone_hz=self._tone_hz,
            tracked_tone_hz=float(self._tracked_tone_hz),
            wpm=self._wpm,
            estimated_wpm=float(self._estimate_wpm()),
            estimated_dit_ms=float(self._estimate_dit_ms()),
            dit_ms=1000.0 * self._dit_samples / self._sr,
            dah_ms=3000.0 * self._dit_samples / self._sr,
            char_gap_ms=1000.0 * self._char_gap_threshold / self._sr,
            word_gap_ms=1000.0 * self._word_gap_threshold / self._sr,
            threshold_hi=float(self._threshold_hi),
            threshold_lo=float(self._threshold_lo),
            peak=float(self._peak),
            floor=float(self._floor),
            marks_seen=self._marks_seen,
            gaps_seen=self._gaps_seen,
        )

    def push_samples(self, samples: np.ndarray):
        if not self._running:
            return
        if samples is None or len(samples) == 0:
            return
        data = np.asarray(samples, dtype=np.float32)
        self._sample_buf = np.concatenate([self._sample_buf, data])
        while len(self._sample_buf) >= self._block_size:
            block = self._sample_buf[:self._block_size]
            self._sample_buf = self._sample_buf[self._block_size:]
            level = self._measure_block(block)
            self._update_levels(level)
            self._step_detector(level)
            self._sample_index += self._block_size

    def _measure_block(self, block: np.ndarray) -> float:
        if not self._use_goertzel:
            self._tracked_tone_hz = self._tone_hz
            return float(np.mean(np.abs(block.astype(np.float64))))
        best_level = 0.0
        best_tone = self._tone_hz
        total_level = 0.0
        levels = []
        for offset in self._tone_search_offsets:
            tone = self._tone_hz + float(offset)
            level = self._goertzel_power(block, tone)
            levels.append(level)
            total_level += level
            if level > best_level:
                best_level = level
                best_tone = tone
        self._tracked_tone_hz = best_tone
        if total_level <= 1e-12:
            return 0.0
        # Keep the detector selective, but let nearby in-band energy support
        # the level estimate so a real signal survives modest front-end
        # shaping at 1200 Hz without collapsing to a single brittle bin.
        concentration = best_level / total_level
        support = np.partition(np.asarray(levels, dtype=np.float64), -2)[-2:].sum()
        return float((0.65 * best_level) + (0.35 * support * concentration))

    def _goertzel_power(self, block: np.ndarray, tone_hz: float) -> float:
        omega = 2.0 * math.pi * float(tone_hz) / float(self._sr)
        coeff = 2.0 * math.cos(omega)
        s_prev = 0.0
        s_prev2 = 0.0
        for sample in block.astype(np.float64, copy=False):
            s = float(sample) + coeff * s_prev - s_prev2
            s_prev2 = s_prev
            s_prev = s
        power = s_prev2 * s_prev2 + s_prev * s_prev - coeff * s_prev * s_prev2
        return max(0.0, float(power)) / max(1, len(block))

    def _update_levels(self, level: float):
        if level > self._peak:
            self._peak = 0.94 * self._peak + 0.06 * level
        else:
            self._peak = 0.999 * self._peak + 0.001 * level
        if level < self._floor:
            self._floor = 0.96 * self._floor + 0.04 * level
        else:
            self._floor = 0.999 * self._floor + 0.001 * level
        span = max(0.01, self._peak - self._floor)
        self._threshold_hi = self._floor + span * 0.52
        self._threshold_lo = self._floor + span * 0.34

    def _step_detector(self, level: float):
        if self._key_down:
            if level >= self._threshold_lo:
                self._low_run_blocks = 0
                self._on_blocks += 1
                return
            self._low_run_blocks += 1
            mark_samples = self._on_blocks * self._block_size
            min_real_mark = max(1, int(0.60 * self._dit_samples))
            end_run_blocks = 3 if mark_samples < self._dot_dash_split else 4
            if mark_samples < min_real_mark:
                return
            if self._low_run_blocks < end_run_blocks:
                return
            self._finish_mark(mark_samples)
            self._key_down = False
            self._on_blocks = 0
            self._off_blocks = self._low_run_blocks
            self._low_run_blocks = 0
            return

        if level >= self._threshold_hi:
            if self._off_blocks > 0:
                self._finish_gap(self._off_blocks * self._block_size)
            self._key_down = True
            self._off_blocks = 0
            self._on_blocks = 1
            return

        self._off_blocks += 1

    def _finish_mark(self, samples: int):
        if samples < max(1, int(0.35 * self._dit_samples)):
            return
        self._marks_seen += 1
        self._recent_mark_samples.append(int(samples))
        if len(self._recent_mark_samples) > 64:
            self._recent_mark_samples = self._recent_mark_samples[-64:]
        self._current += "." if samples < self._dot_dash_split else "-"

    def _estimate_dit_ms(self) -> float:
        if not self._recent_mark_samples:
            return 1000.0 * self._dit_samples / self._sr
        arr = np.asarray(self._recent_mark_samples, dtype=np.float64)
        candidate = float(np.percentile(arr, 35))
        candidate = max(float(self._block_size), candidate)
        return 1000.0 * candidate / self._sr

    def _estimate_wpm(self) -> float:
        dit_ms = self._estimate_dit_ms()
        if dit_ms <= 1e-6:
            return float(self._wpm)
        return max(5.0, min(60.0, 1200.0 / dit_ms))

    def _finish_gap(self, samples: int):
        self._gaps_seen += 1
        if not self._current:
            return
        if samples >= self._word_gap_threshold:
            self._flush_char()
            self._emit(" ", is_space=True)
            return
        if samples >= self._char_gap_threshold:
            self._flush_char()

    def _flush_char(self):
        if not self._current:
            return
        ch = MORSE_DECODE.get(self._current, "?")
        self._emit(ch, pattern=self._current)
        self._current = ""

    def _flush_pending(self):
        if len(self._sample_buf):
            pad = np.zeros(self._block_size - len(self._sample_buf), dtype=np.float32)
            block = np.concatenate([self._sample_buf, pad])
            level = self._measure_block(block)
            self._update_levels(level)
            self._step_detector(level)
            self._sample_buf = np.array([], dtype=np.float32)
        if self._key_down and self._on_blocks:
            self._finish_mark(self._on_blocks * self._block_size)
            self._key_down = False
            self._on_blocks = 0
            self._low_run_blocks = 0
        if self._current:
            self._flush_char()

    def _emit(self, text: str, pattern: str = "", is_space: bool = False):
        if not text or self._callback is None:
            return
        self._callback(
            CwDecodeEvent(
                text=text,
                confidence=1.0 if text != "?" else 0.0,
                pattern=pattern,
                recovered=False,
                is_space=is_space,
            )
        )
