from __future__ import annotations

import math
from collections import deque
from dataclasses import dataclass
from typing import Callable, Optional

import numpy as np

from modes.cw_engine import CwDecodeEvent
from modes.morse_decoder_ext.assembler import Assembler
from modes.morse_decoder_ext.config import MorseConfig
from modes.morse_decoder_ext.dsp import KeyEvent, KeyState
from modes.morse_decoder_ext.timing import TimingEngine


@dataclass
class HybridStats:
    tone_hz: float
    tracked_tone_hz: float
    tone_confidence: float
    estimated_wpm: float
    dit_ms: float
    noise_floor: float
    peak_level: float
    key_state: str


class _HybridEventAdapter:
    def __init__(self, callback):
        self._callback = callback

    def __call__(self, result) -> None:
        if self._callback is None:
            return
        character = getattr(result, "character", "")
        if not character:
            return
        self._callback(
            CwDecodeEvent(
                text=character,
                confidence=float(getattr(result, "confidence", 0.0)),
                pattern=str(getattr(result, "code", "")),
            )
        )


class _HybridFrontEnd:
    def __init__(self, cfg: MorseConfig):
        self._cfg = cfg
        self._sr = cfg.sample_rate
        self._block_ms = 8.0
        self._block_size = max(8, int(round(self._sr * self._block_ms / 1000.0)))
        self._tone_search_offsets = np.array([-48.0, -24.0, -12.0, 0.0, 12.0, 24.0, 48.0], dtype=np.float64)
        self._tracked_tone_hz = float(cfg.tone_freq)
        self._tone_conf = 0.0
        self.reset()

    def reset(self) -> None:
        self._sample_buf = np.array([], dtype=np.float32)
        self._key_down = False
        self._on_blocks = 0
        self._off_blocks = 0
        self._low_run_blocks = 0
        self._peak = 0.10
        self._floor = 0.0
        self._threshold_hi = 0.055
        self._threshold_lo = 0.035
        self._stats_key_state = "KEY_UP"
        self._last_block_level = 0.0
        self._tone_lock_score = 0.0
        self._level_history = deque(maxlen=96)

    def process(self, pcm: np.ndarray):
        pcm = np.asarray(pcm, dtype=np.float32)
        self._sample_buf = np.concatenate([self._sample_buf, pcm])
        events = []
        while len(self._sample_buf) >= self._block_size:
            block = self._sample_buf[: self._block_size]
            self._sample_buf = self._sample_buf[self._block_size :]
            level = self._measure_block(block)
            self._update_levels(level)
            events.extend(self._step_detector(level))
        return events

    def flush(self):
        events = []
        if len(self._sample_buf):
            pad = np.zeros(self._block_size - len(self._sample_buf), dtype=np.float32)
            block = np.concatenate([self._sample_buf, pad])
            level = self._measure_block(block)
            self._update_levels(level)
            events.extend(self._step_detector(level))
            self._sample_buf = np.array([], dtype=np.float32)
        if self._key_down and self._on_blocks:
            events.append(self._emit(KeyState.KEY_DOWN, self._on_blocks * self._block_size))
            self._key_down = False
            self._on_blocks = 0
            self._low_run_blocks = 0
            self._stats_key_state = "KEY_UP"
        return events

    def _measure_block(self, block: np.ndarray) -> float:
        candidates: list[tuple[float, float]] = []
        candidate_hz = set()
        for base in (float(self._cfg.tone_freq), float(self._tracked_tone_hz)):
            for offset in self._tone_search_offsets:
                tone = round(base + float(offset), 1)
                if 300.0 <= tone <= 1100.0:
                    candidate_hz.add(tone)

        if not candidate_hz:
            self._tone_conf = 0.0
            return 0.0

        for tone in sorted(candidate_hz):
            level = self._goertzel_power(block, tone)
            drift_penalty = abs(tone - self._tracked_tone_hz) * 0.010
            hint_penalty = abs(tone - float(self._cfg.tone_freq)) * 0.006
            stay_bonus = 0.18 if abs(tone - self._tracked_tone_hz) <= 6.0 else 0.0
            candidates.append((tone, level - drift_penalty - hint_penalty + stay_bonus))

        scored = np.asarray([score for _, score in candidates], dtype=np.float64)
        raw_levels = np.asarray([self._goertzel_power(block, tone) for tone, _ in candidates], dtype=np.float64)
        if raw_levels.size == 0 or float(np.sum(raw_levels)) <= 1e-12:
            self._tone_conf = 0.0
            self._tone_lock_score *= 0.9
            return 0.0

        best_idx = int(np.argmax(scored))
        best_tone = float(candidates[best_idx][0])
        best_level = float(raw_levels[best_idx])
        sorted_levels = np.sort(raw_levels)
        second_level = float(sorted_levels[-2]) if len(sorted_levels) >= 2 else 0.0
        total_level = float(np.sum(raw_levels))
        concentration = best_level / max(total_level, 1e-12)
        isolation = best_level / max(second_level, 1e-12)
        confidence = np.clip((0.55 * concentration) + (0.25 * min(1.0, isolation / 2.4)) + (0.20 if abs(best_tone - self._tracked_tone_hz) <= 12.0 else 0.0), 0.0, 1.0)
        self._tone_conf = float(confidence)

        if confidence >= 0.42:
            blend = 0.78 if abs(best_tone - self._tracked_tone_hz) <= 12.0 else 0.62
            self._tracked_tone_hz = (blend * self._tracked_tone_hz) + ((1.0 - blend) * best_tone)
            self._tone_lock_score = min(1.0, self._tone_lock_score + 0.10 + confidence * 0.08)
        else:
            self._tone_lock_score = max(0.0, self._tone_lock_score - 0.10)

        support = best_level + (0.45 * second_level)
        return float(support * max(0.35, confidence))

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

    def _update_levels(self, level: float) -> None:
        self._last_block_level = float(level)
        self._level_history.append(float(level))
        if level > self._peak:
            self._peak = 0.94 * self._peak + 0.06 * level
        else:
            self._peak = 0.999 * self._peak + 0.001 * level
        if len(self._level_history) >= 12:
            noise_est = float(np.percentile(np.asarray(self._level_history, dtype=np.float64), 30))
            if self._floor <= 0.0:
                self._floor = noise_est
            elif noise_est < self._floor:
                self._floor = 0.92 * self._floor + 0.08 * noise_est
            else:
                self._floor = 0.995 * self._floor + 0.005 * noise_est
        elif level < self._floor:
            self._floor = 0.96 * self._floor + 0.04 * level
        else:
            self._floor = 0.999 * self._floor + 0.001 * level
        span = max(0.01, self._peak - self._floor)
        conf_scale = max(0.0, min(1.0, self._tone_conf))
        lock_bonus = max(0.0, min(1.0, self._tone_lock_score))
        noise_ratio = self._floor / max(self._peak, 1e-6)
        hi_ratio = 0.55 - (0.12 * lock_bonus) + (0.10 * (1.0 - conf_scale)) + (0.08 * noise_ratio)
        lo_ratio = 0.34 - (0.08 * lock_bonus) + (0.06 * (1.0 - conf_scale)) + (0.05 * noise_ratio)
        self._threshold_hi = self._floor + span * float(np.clip(hi_ratio, 0.36, 0.72))
        self._threshold_lo = self._floor + span * float(np.clip(lo_ratio, 0.24, 0.56))

    def _step_detector(self, level: float):
        events = []
        if not self._key_down:
            if level >= self._threshold_hi:
                if self._off_blocks > 0:
                    events.append(self._emit(KeyState.KEY_UP, self._off_blocks * self._block_size))
                self._key_down = True
                self._off_blocks = 0
                self._on_blocks = 1
                self._stats_key_state = "KEY_DOWN"
                return events
            self._off_blocks += 1
            return events

        if level >= self._threshold_lo:
            self._low_run_blocks = 0
            self._on_blocks += 1
            return events

        self._low_run_blocks += 1
        mark_samples = self._on_blocks * self._block_size
        min_real_mark = max(1, int(0.60 * (1.2 / max(self._cfg.wpm_initial, 5)) * self._sr))
        dot_dash_split = (1.2 / max(self._cfg.wpm_initial, 5)) * self._sr * 2.0
        end_run_blocks = 3 if mark_samples < dot_dash_split else 4
        if mark_samples < min_real_mark:
            return events
        if self._low_run_blocks < end_run_blocks:
            return events
        events.append(self._emit(KeyState.KEY_DOWN, mark_samples))
        self._key_down = False
        self._on_blocks = 0
        self._off_blocks = self._low_run_blocks
        self._low_run_blocks = 0
        self._stats_key_state = "KEY_UP"
        return events

    @property
    def noise_floor(self) -> float:
        return float(self._floor)

    @property
    def peak_level(self) -> float:
        return float(self._peak)

    @property
    def tracked_tone_hz(self) -> float:
        return float(self._tracked_tone_hz)

    @property
    def tone_confidence(self) -> float:
        return float(self._tone_conf)

    @property
    def key_state(self) -> str:
        return self._stats_key_state

    def _emit(self, key_state: KeyState, sample_count: int) -> KeyEvent:
        duration = float(sample_count) / float(self._sr)
        return KeyEvent(state=key_state, duration=duration, mean_level=self._last_block_level)


class CwHybridDecoder:
    def __init__(
        self,
        sample_rate: int = 48000,
        tone_hz: float = 700.0,
        text_callback: Optional[Callable[[CwDecodeEvent], None]] = None,
        initial_wpm: int = 18,
    ):
        self._cfg = MorseConfig(
            sample_rate=int(sample_rate),
            tone_freq=float(tone_hz),
            tone_bw=120.0,
            wpm_initial=float(initial_wpm),
            max_decode_errors=1,
        )
        self._frontend = _HybridFrontEnd(self._cfg)
        self._timing = TimingEngine(self._cfg)
        self._assembler = Assembler(self._cfg, wpm_source=self._timing)
        self._callback = text_callback
        self._adapter = _HybridEventAdapter(text_callback)
        self._assembler.on_result = self._adapter
        self._running = False

    def start(self):
        self._running = True

    def stop(self):
        self._running = False
        for ev in self._frontend.flush():
            for el in self._timing.feed(ev):
                self._assembler.feed(el)
        self._assembler.flush()

    def reset(self):
        self._frontend.reset()
        self._timing.reset()
        self._assembler = Assembler(self._cfg, wpm_source=self._timing)
        self._adapter = _HybridEventAdapter(self._callback)
        self._assembler.on_result = self._adapter

    def push_samples(self, samples: np.ndarray):
        if not self._running:
            return
        events = self._frontend.process(samples)
        for ev in events:
            for el in self._timing.feed(ev):
                self._assembler.feed(el)

    @property
    def stats(self) -> HybridStats:
        return HybridStats(
            tone_hz=float(self._cfg.tone_freq),
            tracked_tone_hz=self._frontend.tracked_tone_hz,
            tone_confidence=self._frontend.tone_confidence,
            estimated_wpm=float(self._timing.current_wpm),
            dit_ms=float(self._timing.dit_duration * 1000.0),
            noise_floor=self._frontend.noise_floor,
            peak_level=self._frontend.peak_level,
            key_state=self._frontend.key_state,
        )
