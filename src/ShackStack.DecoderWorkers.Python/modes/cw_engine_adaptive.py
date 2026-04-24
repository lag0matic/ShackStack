from __future__ import annotations

import logging
import re
import threading
from collections import deque
from dataclasses import dataclass

import numpy as np
from scipy.signal import butter, sosfilt, sosfilt_zi

from modes.cw_engine import MORSE_DECODE, MORSE_PREFIXES, CwDecodeEvent

log = logging.getLogger(__name__)


@dataclass
class _BeamState:
    text: str
    chars: list
    current: str
    current_conf: list
    score: float


class CwAdaptiveDecoder:
    """
    Fresh adaptive CW decoder path inspired by "fist tracking" decoders.

    Goals:
    - prefer silence over gibberish
    - adapt dot / dash / gap timing from recent valid events
    - reject weak single-letter junk aggressively
    """

    def __init__(
        self,
        sample_rate: int = 48000,
        tone_hz: float = 700.0,
        text_callback=None,
        initial_wpm: int = 18,
    ):
        self._sr = int(sample_rate)
        self._tone_hz = float(tone_hz)
        self._tune_hint_hz = float(tone_hz)
        self._initial_wpm = int(initial_wpm)
        self._search_low_hz = 500.0
        self._search_high_hz = 1000.0
        self._assist_window_hz = 120.0
        self._fallback_window_hz = 180.0
        self._decode_bw_hz = 55.0
        self._acquire_bw_hz = 180.0
        self._tone_isolation = 1.0
        self._tone_locked = False
        self._tone_lock_conf = 0
        self._callback = text_callback
        self._running = False
        self._thread = None
        self._queue = deque(maxlen=4000)
        self._lock = threading.Lock()

        self._afc_callback = None
        self._wpm_callback = None
        self._afc_enabled = True
        self._word_space_mult = 4.0
        self._last_reported_tone_hz = float(tone_hz)

        self._build_filters()
        self.reset()

    def _build_filters(self):
        nyq = self._sr / 2.0
        if getattr(self, "_tone_locked", False):
            bw = 38.0 if self._tone_lock_conf >= 4 and self._tone_isolation >= 2.8 else self._decode_bw_hz
        else:
            bw = 140.0 if self._tone_lock_conf >= 2 else self._acquire_bw_hz
        lo = max(50.0, self._tone_hz - bw) / nyq
        hi = min(self._tone_hz + bw, nyq * 0.99) / nyq
        self._bp_sos = butter(4, [lo, hi], btype="band", output="sos")
        self._lp_sos = butter(4, min(0.99, 100.0 / nyq), btype="low", output="sos")
        self._bp_state = None

    def start(self):
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(target=self._run, daemon=True, name="cw-adaptive")
        self._thread.start()

    def stop(self):
        self._running = False
        if self._thread:
            self._thread.join(timeout=2.0)
        self._flush_character(force=True)
        self._flush_pending_word()

    def reset(self):
        with self._lock:
            self._queue.clear()
        self._bp_state = None
        self._i_lp_state = None
        self._q_lp_state = None
        self._mix_phase = 0.0
        self._key_down = False
        self._pending_on_ms = 0.0
        self._pending_off_ms = 0.0
        self._on_ms = 0.0
        self._off_ms = 0.0
        dit_ms = float(np.clip(1200.0 / max(self._initial_wpm, 5), 24.0, 240.0))
        self._dit_ms = dit_ms
        self._dah_ms = dit_ms * 3.0
        self._gap_ms = dit_ms
        self._char_gap_ms = 140.0
        self._peak = 0.20
        self._floor = 0.01
        self._env = 0.0
        self._env_val = 0.0
        self._slow_env_val = 0.0
        self._mark_peak = 0.0
        self._recent_mark_strength = deque(maxlen=48)
        self._threshold_hi = 0.10
        self._threshold_lo = 0.06
        self._tone_locked = False
        self._tone_lock_conf = 0
        self._tone_isolation = 1.0
        self._tone_candidate_hz = self._tone_hz
        self._acquire_buf = np.array([], dtype=np.float32)
        self._recent_mark_ms = deque(maxlen=48)
        self._recent_gap_ms = deque(maxlen=48)
        self._current_pattern = ""
        self._current_conf = []
        self._recent_tokens = deque(maxlen=10)
        self._repeat_token = ""
        self._repeat_count = 0
        self._pending_word = ""
        self._pending_word_conf = []
        self._word_tokens = []
        self._space_emitted_this_gap = False
        self._since_emit_ms = 0.0
        self._afc_buf = np.array([], dtype=np.float32)
        self._dbg_marks_seen = 0
        self._dbg_marks_accepted = 0
        self._dbg_mark_reject_too_short = 0
        self._dbg_mark_reject_too_long = 0
        self._dbg_mark_reject_too_weak = 0
        self._dbg_spaces_seen = 0
        self._dbg_spaces_ignored_short = 0
        self._dbg_char_flushes = 0
        self._dbg_word_flushes = 0
        self._dbg_idle_flushes = 0
        self._dbg_token_merge_attempts = 0
        self._dbg_token_merges = 0

    def set_initial_wpm(self, wpm: int) -> None:
        self._initial_wpm = int(np.clip(wpm, 5, 60))
        dit_ms = float(np.clip(1200.0 / max(self._initial_wpm, 5), 24.0, 240.0))
        self._dit_ms = dit_ms
        self._dah_ms = dit_ms * 3.0
        self._gap_ms = dit_ms

    def push_samples(self, samples: np.ndarray):
        with self._lock:
            self._queue.append(np.asarray(samples, dtype=np.float32))

    def set_afc_callback(self, fn):
        self._afc_callback = fn
        self._report_tone(self._tone_hz, force=True)

    def set_wpm_callback(self, fn):
        self._wpm_callback = fn

    @property
    def tone_hz(self):
        return self._tone_hz

    @tone_hz.setter
    def tone_hz(self, value: float):
        self._tone_hz = float(value)
        self._tune_hint_hz = float(value)
        self._tone_candidate_hz = self._tone_hz
        self._tone_locked = False
        self._tone_lock_conf = 0
        self._build_filters()
        self._report_tone(self._tone_hz, force=True)

    def _report_tone(self, hz: float, *, force: bool = False):
        if self._afc_callback is None:
            return
        hz = float(hz)
        if not force and abs(hz - self._last_reported_tone_hz) < 1.0:
            return
        self._last_reported_tone_hz = hz
        try:
            self._afc_callback(hz)
        except Exception:
            log.exception("Adaptive CW AFC callback failed")

    @property
    def afc_enabled(self):
        return self._afc_enabled

    @afc_enabled.setter
    def afc_enabled(self, value: bool):
        self._afc_enabled = bool(value)

    @property
    def word_space_mult(self):
        return self._word_space_mult

    @word_space_mult.setter
    def word_space_mult(self, value: float):
        self._word_space_mult = float(value)

    def _run(self):
        while self._running:
            chunk = None
            with self._lock:
                if self._queue:
                    chunk = self._queue.popleft()
            if chunk is None:
                threading.Event().wait(0.005)
                continue
            self._process(chunk)

    def _process(self, samples: np.ndarray):
        if samples.size == 0:
            return

        self._coarse_acquire(samples)

        if self._bp_state is None:
            self._bp_state = sosfilt_zi(self._bp_sos) * float(samples[0])
        band, self._bp_state = sosfilt(self._bp_sos, samples, zi=self._bp_state)

        if self._i_lp_state is None or self._q_lp_state is None:
            self._i_lp_state = np.zeros((self._lp_sos.shape[0], 2), dtype=np.float32)
            self._q_lp_state = np.zeros((self._lp_sos.shape[0], 2), dtype=np.float32)

        phase_step = 2.0 * np.pi * self._tone_hz / self._sr
        phases = self._mix_phase + phase_step * np.arange(len(band), dtype=np.float32)
        osc_i = np.cos(phases).astype(np.float32)
        osc_q = (-np.sin(phases)).astype(np.float32)
        mixed_i = band * osc_i
        mixed_q = band * osc_q
        bb_i, self._i_lp_state = sosfilt(self._lp_sos, mixed_i, zi=self._i_lp_state)
        bb_q, self._q_lp_state = sosfilt(self._lp_sos, mixed_q, zi=self._q_lp_state)
        self._mix_phase = float((self._mix_phase + phase_step * len(band)) % (2.0 * np.pi))

        raw_env = np.sqrt(bb_i * bb_i + bb_q * bb_q)
        if raw_env.size >= 9:
            med = np.median(raw_env)
            spike_limit = max(med * 8.0, np.percentile(raw_env, 97) * 1.2, 1e-5)
            raw_env = np.minimum(raw_env, spike_limit)
        alpha_attack = 1.0 - np.exp(-1.0 / (self._sr * 0.0007))
        alpha_release = 1.0 - np.exp(-1.0 / (self._sr * 0.0060))
        env = np.empty_like(raw_env)
        env_val = self._env_val
        for i, sample in enumerate(raw_env):
            if sample > env_val:
                env_val += alpha_attack * (sample - env_val)
            else:
                env_val += alpha_release * (sample - env_val)
            env[i] = env_val
        self._env_val = env_val

        slow_attack = 1.0 - np.exp(-1.0 / (self._sr * 0.0022))
        slow_release = 1.0 - np.exp(-1.0 / (self._sr * 0.0100))
        clean_env = np.empty_like(env)
        slow_env_val = self._slow_env_val
        for i, sample in enumerate(env):
            if sample > slow_env_val:
                slow_env_val += slow_attack * (sample - slow_env_val)
            else:
                slow_env_val += slow_release * (sample - slow_env_val)
            clean_env[i] = 0.85 * sample + 0.15 * slow_env_val
        self._slow_env_val = slow_env_val

        block_level = float(np.mean(clean_env))
        self._env = block_level
        block_max = float(np.max(clean_env))
        if block_level <= self._threshold_lo:
            self._floor = 0.992 * self._floor + 0.008 * block_level
        else:
            self._floor = 0.998 * self._floor + 0.002 * min(block_level, self._threshold_lo)

        if block_max > self._peak:
            self._peak = 0.82 * self._peak + 0.18 * block_max
        else:
            self._peak = 0.996 * self._peak + 0.004 * block_max

        span = max(0.02, self._peak - self._floor)
        self._threshold_hi = self._floor + 0.30 * span
        self._threshold_lo = self._floor + 0.16 * span

        if self._afc_enabled and self._afc_callback is not None:
            self._do_afc(samples, block_level)

        sub = 64
        for i in range(0, len(clean_env), sub):
            block = clean_env[i:i + sub]
            if block.size == 0:
                continue
            block_mean = float(np.mean(block))
            present = block_mean >= (self._threshold_lo if self._key_down else self._threshold_hi)
            block_ms = 1000.0 * block.size / self._sr
            on_confirm_ms = max(6.0, self._dit_ms * 0.11)
            off_confirm_ms = max(10.0, self._dit_ms * 0.18)

            if present:
                self._pending_on_ms += block_ms
                self._pending_off_ms = 0.0
                if not self._key_down and self._pending_on_ms >= on_confirm_ms:
                    start_ms = self._pending_on_ms
                    self._key_down = True
                    self._pending_on_ms = 0.0
                    self._mark_peak = block_mean
                    self._space_emitted_this_gap = False
                    self._handle_space(max(0.0, self._off_ms))
                    self._off_ms = 0.0
                    self._on_ms = start_ms
                elif self._key_down:
                    self._on_ms += block_ms
                    self._mark_peak = max(self._mark_peak, block_mean)
            else:
                self._pending_off_ms += block_ms
                self._pending_on_ms = 0.0
                if self._key_down:
                    self._off_ms += block_ms
                    if self._pending_off_ms >= off_confirm_ms:
                        trailing_ms = self._pending_off_ms
                        self._key_down = False
                        self._pending_off_ms = 0.0
                        self._handle_mark(max(0.0, self._on_ms - trailing_ms), self._mark_peak)
                        self._mark_peak = 0.0
                        self._on_ms = 0.0
                else:
                    self._off_ms += block_ms
                    self._since_emit_ms += block_ms

                    if self._current_pattern or self._pending_word or self._word_tokens:
                        word_gap = max(self._gap_ms * self._word_space_mult * 1.8,
                                       self._dit_ms * self._word_space_mult * 1.8)
                        elem_count = sum(1 for kind, _ in self._word_tokens if kind == "elem")
                        soft_word_gap = max(
                            self._char_gap_ms * 1.55,
                            self._gap_ms * 2.7,
                            self._dit_ms * 2.8,
                            170.0,
                        )
                        if (not self._space_emitted_this_gap) and elem_count >= 6 and self._off_ms >= soft_word_gap:
                            self._dbg_word_flushes += 1
                            self._space_emitted_this_gap = True
                            self._emit_space()
                        if (not self._space_emitted_this_gap) and self._off_ms >= word_gap:
                            self._dbg_word_flushes += 1
                            self._space_emitted_this_gap = True
                            self._emit_space()

                    if self._off_ms > max(1400.0, self._dit_ms * 12.0):
                        self._dbg_idle_flushes += 1
                        self._flush_character(force=True)
                        self._flush_pending_word()
                        self._afc_buf = np.array([], dtype=np.float32)
                        if self._tone_locked and self._since_emit_ms > max(2200.0, self._dit_ms * 20.0):
                            self._tone_locked = False
                            self._tone_lock_conf = 0
                            self._build_filters()

    def _coarse_acquire(self, samples: np.ndarray):
        self._acquire_buf = np.concatenate([self._acquire_buf, samples.astype(np.float32)])
        if self._acquire_buf.size < 4096:
            return

        buf = self._acquire_buf[-4096:]
        window = np.hanning(buf.size).astype(np.float32)
        spec = np.fft.rfft(buf * window)
        power = np.abs(spec)
        freqs = np.fft.rfftfreq(buf.size, d=1.0 / self._sr)
        mask = (freqs >= self._search_low_hz) & (freqs <= self._search_high_hz)
        if not np.any(mask):
            return

        region_power = power[mask]
        region_freqs = freqs[mask]
        noise = float(np.median(region_power))
        if noise <= 0.0:
            return

        candidates = np.flatnonzero(region_power >= noise * 3.0)
        if candidates.size == 0:
            return

        hint_hz = self._tone_hz if self._tone_locked else self._tune_hint_hz
        assist_candidates = [idx for idx in candidates if abs(float(region_freqs[idx]) - hint_hz) <= self._assist_window_hz]
        if assist_candidates:
            search_candidates = assist_candidates
        else:
            fallback_candidates = [
                idx for idx in candidates
                if abs(float(region_freqs[idx]) - hint_hz) <= self._fallback_window_hz
                and float(region_power[idx]) >= noise * 6.5
            ]
            search_candidates = fallback_candidates
        if not search_candidates:
            return

        best_score = None
        peak_hz = None
        peak_isolation = 1.0
        for idx in search_candidates:
            hz = float(region_freqs[idx])
            mag = float(region_power[idx])
            near_hint = abs(hz - hint_hz)
            near_candidate = abs(hz - self._tone_candidate_hz)
            flank_mask = (np.abs(region_freqs - hz) <= 35.0) & (np.abs(region_freqs - hz) >= 6.0)
            local_noise = float(np.median(region_power[flank_mask])) if np.any(flank_mask) else noise
            local_noise = max(local_noise, noise, 1e-9)
            isolation = mag / local_noise
            rival_mask = (np.abs(region_freqs - hz) <= 90.0) & (np.abs(region_freqs - hz) >= 18.0)
            rival_mag = float(np.max(region_power[rival_mask])) if np.any(rival_mask) else local_noise
            separation = mag / max(rival_mag, local_noise, 1e-9)
            peak_mask = np.abs(region_freqs - hz) <= 10.0
            if np.any(peak_mask):
                peak_freqs = region_freqs[peak_mask]
                peak_power = region_power[peak_mask]
                weighted_hz = float(np.sum(peak_freqs * peak_power) / max(np.sum(peak_power), 1e-9))
            else:
                weighted_hz = hz
            score = (
                mag
                + noise * 0.35 * min(isolation, 12.0)
                + noise * 0.55 * min(separation, 8.0)
                - noise * 0.028 * near_hint
                - noise * 0.014 * near_candidate
            )
            if best_score is None or score > best_score:
                best_score = score
                peak_hz = weighted_hz
                peak_isolation = max(1.0, min(isolation, separation * 1.6))

        if peak_hz is None:
            return

        if abs(peak_hz - self._tone_candidate_hz) <= 14.0:
            self._tone_candidate_hz = 0.7 * self._tone_candidate_hz + 0.3 * peak_hz
            self._tone_lock_conf = min(8, self._tone_lock_conf + 1)
        else:
            blended = 0.93 * hint_hz + 0.07 * peak_hz
            self._tone_candidate_hz = float(np.clip(blended, self._search_low_hz, self._search_high_hz))
            self._tone_lock_conf = max(1, self._tone_lock_conf - 1)
        self._tone_isolation = 0.78 * self._tone_isolation + 0.22 * float(np.clip(peak_isolation, 1.0, 8.0))

        report_hz = self._tone_candidate_hz if not self._tone_locked else self._tone_hz
        self._report_tone(report_hz)

        if not self._tone_locked and self._tone_lock_conf >= 3:
            self._set_decode_tone(self._tone_candidate_hz, locked=True)
        elif not self._tone_locked and abs(self._tone_hz - self._tone_candidate_hz) >= 12.0:
            self._set_decode_tone(self._tone_candidate_hz, locked=False)

        self._acquire_buf = self._acquire_buf[-4096:]

    def _set_decode_tone(self, hz: float, *, locked: bool):
        hz = float(np.clip(hz, self._search_low_hz, self._search_high_hz))
        if abs(hz - self._tone_hz) < 0.5 and self._tone_locked == locked:
            return
        self._tone_hz = hz
        self._tone_candidate_hz = hz
        self._tone_locked = bool(locked)
        self._build_filters()
        self._report_tone(self._tone_hz, force=True)

    def _do_afc(self, samples: np.ndarray, level: float):
        if not self._tone_locked:
            return
        if level < self._threshold_hi or samples.size < 256:
            return
        self._afc_buf = np.concatenate([self._afc_buf, samples])
        if self._afc_buf.size < 4096:
            return
        buf = self._afc_buf[-4096:]
        window = np.hanning(len(buf)).astype(np.float32)
        spec = np.fft.rfft(buf * window)
        power = np.abs(spec)
        freqs = np.fft.rfftfreq(len(buf), d=1.0 / self._sr)
        track_window = 90.0 if self._tone_isolation >= 2.5 else 140.0
        lo = max(200.0, self._tone_hz - track_window)
        hi = min((self._sr / 2.0) - 1.0, self._tone_hz + track_window)
        mask = (freqs >= lo) & (freqs <= hi)
        if not np.any(mask):
            return
        local_freqs = freqs[mask]
        local_power = power[mask]
        peak_idx = int(np.argmax(local_power))
        peak_hz = float(local_freqs[peak_idx])
        centroid_mask = np.abs(local_freqs - peak_hz) <= 8.0
        if np.any(centroid_mask):
            peak_hz = float(
                np.sum(local_freqs[centroid_mask] * local_power[centroid_mask]) /
                max(np.sum(local_power[centroid_mask]), 1e-9)
            )
        delta = float(np.clip(
            peak_hz - self._tone_hz,
            -6.0 if self._tone_locked else -18.0,
            6.0 if self._tone_locked else 18.0,
        ))
        new_hz = self._tone_hz + (0.03 if self._tone_locked else 0.05) * delta
        self._set_decode_tone(new_hz, locked=self._tone_locked or self._tone_lock_conf >= 3)

    def _handle_mark(self, ms: float, peak_level: float):
        self._dbg_marks_seen += 1
        min_mark = max(30.0, self._dit_ms * 0.50)
        if ms < min_mark:
            self._dbg_mark_reject_too_short += 1
            return
        if ms > 700.0:
            self._dbg_mark_reject_too_long += 1
            return
        span = max(self._peak - self._floor, 1e-6)
        strength = (peak_level - self._floor) / span
        self._recent_mark_strength.append(float(strength))
        in_word = len(self._pending_word) >= 2 or len(self._current_pattern) >= 1
        strength_floor = 0.28 if self._tone_locked else 0.38
        if in_word:
            strength_floor -= 0.04
        if strength < strength_floor:
            self._dbg_mark_reject_too_weak += 1
            return
        self._dbg_marks_accepted += 1
        self._recent_mark_ms.append(ms)
        self._word_tokens.append(("elem", float(ms)))
        self._update_timing_models()

        split = self._dit_ms * 1.95
        if ms <= split:
            self._current_pattern += "."
            conf = 1.0 - min(1.0, abs(ms - self._dit_ms) / max(self._dit_ms * 0.85, 15.0))
        else:
            self._current_pattern += "-"
            conf = 1.0 - min(1.0, abs(ms - self._dah_ms) / max(self._dah_ms * 0.70, 20.0))
        conf *= float(np.clip(strength, 0.0, 1.2))
        self._current_conf.append(float(np.clip(conf, 0.0, 1.0)))

    def _handle_space(self, ms: float):
        self._dbg_spaces_seen += 1
        min_gap = max(24.0, self._dit_ms * 0.50)
        if ms < min_gap:
            self._dbg_spaces_ignored_short += 1
            return
        if self._current_pattern:
            char_gap_hint = max(self._char_gap_ms * 0.82, self._gap_ms * 1.42)
            base_char_gap = max(char_gap_hint, self._dit_ms * 1.52)
            if len(self._current_pattern) <= 2:
                long_pattern_bonus = self._dit_ms * 0.30
            elif len(self._current_pattern) == 3:
                long_pattern_bonus = self._dit_ms * 0.12
            else:
                long_pattern_bonus = 0.0
            char_gap = base_char_gap + long_pattern_bonus
            if ms >= char_gap:
                self._dbg_char_flushes += 1
                self._flush_character()
        if self._word_tokens and self._word_tokens[-1][0] == "gap":
            self._word_tokens[-1] = ("gap", max(self._word_tokens[-1][1], float(ms)))
        else:
            self._word_tokens.append(("gap", float(ms)))
        self._recent_gap_ms.append(ms)
        self._update_timing_models()

    def _update_timing_models(self):
        if len(self._recent_mark_ms) >= 6:
            marks = np.array(self._recent_mark_ms, dtype=np.float32)
            marks = np.sort(marks)
            split_idx = max(1, int(len(marks) * 0.52))
            short = marks[:split_idx]
            long = marks[split_idx:]
            if short.size:
                dit_target = float(np.clip(np.median(short), 48.0, 220.0))
                self._dit_ms = 0.80 * self._dit_ms + 0.20 * dit_target
            long_candidates = marks[marks >= max(self._dit_ms * 1.60, np.percentile(marks, 60))]
            if long_candidates.size >= 2:
                dah_target = float(np.percentile(long_candidates, 70))
                self._dah_ms = 0.76 * self._dah_ms + 0.24 * float(
                    np.clip(dah_target, self._dit_ms * 2.5, 660.0)
                )
            elif long.size:
                long_med = float(np.median(long))
                if long_med > self._dit_ms * 1.55:
                    self._dah_ms = 0.82 * self._dah_ms + 0.18 * float(
                        np.clip(long_med, self._dit_ms * 2.4, 660.0)
                    )
                else:
                    self._dah_ms = max(self._dah_ms, self._dit_ms * 3.0)
            else:
                self._dah_ms = max(self._dah_ms, self._dit_ms * 3.0)

        if len(self._recent_gap_ms) >= 4:
            low = max(24.0, self._dit_ms * 0.60)
            high = min(350.0, self._dit_ms * 3.4)
            gaps = np.array([g for g in self._recent_gap_ms if low <= g <= high], dtype=np.float32)
            if gaps.size:
                intra = gaps[gaps <= max(self._dit_ms * 1.45, 120.0)]
                if intra.size:
                    self._gap_ms = float(np.clip(np.median(intra), 45.0, 180.0))
                charish = gaps[gaps >= max(self._dit_ms * 1.55, self._gap_ms * 1.35)]
                if charish.size:
                    target = float(np.clip(np.median(charish), self._gap_ms * 1.45, 320.0))
                    self._char_gap_ms = 0.82 * self._char_gap_ms + 0.18 * target

        if self._wpm_callback and self._dit_ms > 1.0:
            wpm = int(np.clip(1200.0 / self._dit_ms, 5.0, 45.0))
            self._wpm_callback(wpm)

    def _flush_character(self, force: bool = False):
        if not self._current_pattern:
            return
        pattern = self._current_pattern
        conf = float(np.mean(self._current_conf)) if self._current_conf else 0.5
        self._current_pattern = ""
        self._current_conf = []

        ch = MORSE_DECODE.get(pattern)
        recovered = False
        if not ch:
            for idx in range(len(pattern) - 1, 0, -1):
                prefix = pattern[:idx]
                suffix = pattern[idx:]
                if prefix in MORSE_DECODE and suffix in MORSE_PREFIXES:
                    ch = MORSE_DECODE[prefix]
                    conf *= 0.76
                    recovered = True
                    break
        if not ch:
            return

        if self._recent_tokens and self._recent_tokens[-1] == " ":
            prev = self._recent_tokens[-2] if len(self._recent_tokens) >= 2 else ""
            if isinstance(prev, str) and prev and prev[-1] == ch:
                return
        if not ch.isalnum():
            return

        # aggressively suppress ambiguous junk
        if len(pattern) == 1 and conf < (0.46 if force else 0.60):
            return
        if len(pattern) == 2 and conf < (0.36 if force else 0.42):
            return
        if ch in {"E", "T", "I", "M"} and conf < (0.48 if force else 0.58):
            return
        if len(pattern) <= 2 and len(self._pending_word) >= 2 and conf < 0.70:
            return
        if ch.isdigit() and conf < (0.72 if force else 0.82):
            return

        token = ch
        if token == self._repeat_token:
            self._repeat_count += 1
        else:
            self._repeat_token = token
            self._repeat_count = 1
        if self._repeat_count >= 5 and conf < 0.92:
            return

        event = CwDecodeEvent(text=ch, confidence=conf, pattern=pattern, recovered=recovered, is_space=False)
        self._emit_event(event)

    def _emit_space(self):
        self._repeat_token = ""
        self._repeat_count = 0
        event = CwDecodeEvent(text=" ", confidence=0.95, pattern="", recovered=False, is_space=True)
        self._emit_event(event)

    def _emit_event(self, event: CwDecodeEvent):
        if not self._callback or not event.text:
            return
        if event.is_space:
            self._flush_pending_word()
            if self._recent_tokens and self._recent_tokens[-1] == " ":
                return
            self._recent_tokens.append(" ")
            try:
                self._callback(event)
            except Exception:
                log.exception("Adaptive CW callback failed")
            return

        if event.text.isalnum():
            self._pending_word += event.text
            self._pending_word_conf.append(float(getattr(event, "confidence", 0.5)))
            if len(self._pending_word) >= 18:
                self._flush_pending_word()
            return

        self._flush_pending_word()
        self._recent_tokens.append(event.text)
        try:
            self._callback(event)
        except Exception:
            log.exception("Adaptive CW callback failed")

    def _flush_pending_word(self):
        if not self._pending_word and not self._word_tokens:
            return
        text = self._pending_word
        pending_conf = float(np.mean(self._pending_word_conf)) if self._pending_word_conf else 0.6
        decoded = self._decode_word_tokens(self._word_tokens)
        decoded_conf = None
        used_greedy = False
        greedy = []
        if not decoded:
            greedy = self._greedy_decode_word_tokens(self._word_tokens)
            decoded = greedy
            used_greedy = bool(decoded)
        if decoded:
            decoded_conf = float(np.mean([conf for _, conf, *_ in decoded]))
            alt = "".join(ch for ch, *_ in decoded)
            if len(alt) >= 2 and alt[-1] == alt[-2] and alt[-1].isalnum():
                alt = alt[:-1]
            if used_greedy and not self._is_plausible_fallback(alt):
                alt = self._greedy_letter_skeleton(greedy, text)
            segmented_alt = self._segment_word(alt) if alt else ""
            alt_plausible = bool(
                alt and (
                    self._is_plausible_fallback(alt)
                    or self.CALLSIGN_RE.match(alt)
                    or (segmented_alt and segmented_alt != alt)
                )
            )
            if self._prefer_decoded_word(text, pending_conf, alt, decoded_conf, alt_plausible):
                text = alt
        text = self._segment_word(text)
        conf = pending_conf
        if decoded_conf is not None:
            conf = max(conf, decoded_conf)
        text = self._clean_segment_text(text, conf)
        prev_token = self._recent_tokens[-1] if self._recent_tokens else ""
        self._pending_word = ""
        self._pending_word_conf = []
        self._word_tokens = []
        if not text:
            return
        have_any_text = any(token != " " for token in self._recent_tokens)
        if not have_any_text and 2 <= len(text) <= 3:
            vowels = sum(ch in "AEIOUY" for ch in text)
            if vowels >= len(text) - 1 and text not in self.KNOWN_TERMS and conf < 0.94:
                return
        have_prior_text = any(token != " " for token in list(self._recent_tokens)[:-1])
        if len(text) == 1 and text not in {"K", "R"}:
            return
        if len(text) == 1 and str(prev_token) == " " and conf < 0.85 and have_prior_text:
            return
        if len(text) == 2 and text not in self.KNOWN_TERMS:
            vowels = sum(ch in "AEIOUY" for ch in text)
            if vowels == 2:
                return
        if len(text) >= 5 and not self._is_reasonable_word(text, conf):
            return
        if len(text) <= 3 and conf < 0.70 and self._tone_locked:
            return
        self._recent_tokens.append(text)
        try:
            self._callback(CwDecodeEvent(text=text, confidence=conf, pattern="", recovered=False, is_space=False))
        except Exception:
            log.exception("Adaptive CW callback failed")

    def _segment_word(self, word: str) -> str:
        word = "".join(ch for ch in word.upper() if ch.isalnum())
        if not word:
            return ""
        if len(word) <= 2:
            return word
        if self.CALLSIGN_RE.match(word):
            return word

        n = len(word)
        best = [(-1e9, []) for _ in range(n + 1)]
        best[0] = (0.0, [])
        for i in range(n):
            score_i, parts_i = best[i]
            if score_i <= -1e8:
                continue
            for j in range(i + 1, min(n, i + 8) + 1):
                piece = word[i:j]
                piece_score = None
                if piece in self.KNOWN_TERMS:
                    piece_score = 4.0 + 0.35 * len(piece)
                elif self.CALLSIGN_RE.match(piece):
                    piece_score = 5.5 + 0.20 * len(piece)
                elif len(piece) == 1:
                    piece_score = -1.8
                elif len(piece) == 2:
                    piece_score = -0.8
                elif len(piece) >= 5:
                    vowels = sum(ch in "AEIOUY" for ch in piece)
                    if vowels >= 2:
                        piece_score = 0.2 * len(piece)
                if piece_score is None:
                    continue
                total = score_i + piece_score
                if total > best[j][0]:
                    best[j] = (total, parts_i + [piece])

        final_score, parts = best[n]
        if final_score <= 0.0 or not parts:
            return word
        if len(parts) >= 2 and len(parts[-1]) == 1 and parts[-1].isalnum():
            prev = parts[-2]
            if len(prev) >= 2:
                parts = parts[:-1]
        if len(parts) >= 2 and len(parts[-1]) == 1 and parts[-1].isalnum():
            prev = parts[-2]
            if prev and prev[-1] == parts[-1]:
                parts = parts[:-1]
        return " ".join(parts)

    def _clean_segment_text(self, text: str, conf: float) -> str:
        parts = [part for part in text.split() if part]
        if len(parts) <= 1:
            return text
        kept = []
        for part in parts:
            part = self._correct_known_term(part)
            if part in self.KNOWN_TERMS or self.CALLSIGN_RE.match(part):
                kept.append(part)
                continue
            if len(part) == 3 and part[1] == part[2]:
                part = part[:2]
            elif len(part) == 3 and part[0] == part[1]:
                part = part[1:]
            if len(part) == 1:
                if part in {"K", "R"}:
                    kept.append(part)
                continue
            if len(part) == 2:
                vowels = sum(ch in "AEIOUY" for ch in part)
                if vowels == 2 and part not in self.KNOWN_TERMS:
                    continue
                if part not in self.KNOWN_TERMS and vowels >= 1 and conf < 0.92:
                    continue
                if part not in self.KNOWN_TERMS and part[0] == part[1]:
                    continue
            if len(part) >= 4:
                vowels = sum(ch in "AEIOUY" for ch in part)
                max_repeat = max(part.count(ch) for ch in set(part))
                if (vowels >= len(part) - 1 or max_repeat >= len(part) - 1) and conf < 0.93:
                    continue
            kept.append(part)
        if not kept:
            return ""
        if len(kept) >= 2:
            kept = [
                part for part in kept
                if (
                    part in self.KNOWN_TERMS
                    or self.CALLSIGN_RE.match(part)
                    or len(part) >= 3
                )
            ] or kept
        return " ".join(kept)

    def _correct_known_term(self, part: str) -> str:
        part = "".join(ch for ch in part.upper() if ch.isalnum())
        if not part or part in self.KNOWN_TERMS:
            return part
        candidates = [term for term in self.KNOWN_TERMS if len(term) == len(part)]
        best = part
        best_dist = 99
        for term in candidates:
            if any(a.isdigit() != b.isdigit() for a, b in zip(part, term)):
                continue
            dist = sum(a != b for a, b in zip(part, term))
            if dist < best_dist:
                best = term
                best_dist = dist
        if best_dist <= 1:
            return best
        if len(part) >= 5:
            for term in self.BULLETIN_TERMS:
                if abs(len(term) - len(part)) != 1:
                    continue
                if any(ch.isdigit() for ch in part) or any(ch.isdigit() for ch in term):
                    continue
                shorter, longer = (part, term) if len(part) < len(term) else (term, part)
                if not self._is_subsequence(shorter, longer):
                    continue
                shared_prefix = 0
                for a, b in zip(part, term):
                    if a != b:
                        break
                    shared_prefix += 1
                shared_suffix = 0
                for a, b in zip(reversed(part), reversed(term)):
                    if a != b:
                        break
                    shared_suffix += 1
                if shared_prefix + shared_suffix >= max(4, len(shorter) - 1):
                    return term
        return part

    def _prefer_decoded_word(
        self,
        current_text: str,
        current_conf: float,
        alt_text: str,
        decoded_conf: float | None,
        alt_plausible: bool,
    ) -> bool:
        if not alt_text:
            return False
        if not current_text or len(alt_text) <= len(current_text) or alt_plausible:
            return True
        if decoded_conf is None:
            return False
        if len(alt_text) > len(current_text) + 2:
            return False
        return (
            decoded_conf >= current_conf + 0.18
            and self._is_reasonable_word(alt_text, decoded_conf)
        )

    def _is_reasonable_word(self, text: str, conf: float) -> bool:
        text = "".join(ch for ch in text.upper() if ch.isalnum())
        if not text:
            return False
        if len(text) <= 3:
            return True
        if text in self.KNOWN_TERMS or self.CALLSIGN_RE.match(text):
            return True
        if len(text) >= 5:
            unique = len(set(text))
            if unique <= 3 and conf < 0.92:
                return False
            etea = sum(ch in "ETAI" for ch in text)
            if etea >= len(text) - 1 and conf < 0.94:
                return False
            max_repeat = max(text.count(ch) for ch in set(text))
            if max_repeat >= max(3, len(text) - 2) and conf < 0.94:
                return False
        return True

    def _is_plausible_fallback(self, text: str) -> bool:
        text = "".join(ch for ch in text.upper() if ch.isalnum())
        if not text:
            return False
        if self.CALLSIGN_RE.match(text):
            return True
        if text in self.KNOWN_TERMS:
            return True
        if any(ch.isdigit() for ch in text):
            return False
        vowels = sum(ch in "AEIOUY" for ch in text)
        if len(text) >= 5 and vowels >= 1:
            return True
        return False

    def _greedy_letter_skeleton(self, greedy_chars, current_text: str) -> str:
        letters = []
        strong_count = 0
        for ch, conf, *_ in greedy_chars or []:
            if ch.isalpha() and conf >= 0.84:
                letters.append(ch)
                if conf >= 0.92:
                    strong_count += 1
        skeleton = "".join(letters)
        if len(skeleton) < max(3, len(current_text) + 1):
            return ""
        if len(skeleton) > max(7, len(current_text) + 3):
            return ""
        if current_text and not self._is_subsequence(current_text, skeleton):
            return ""
        if strong_count < max(2, len(skeleton) // 2):
            return ""
        return skeleton

    @staticmethod
    def _is_subsequence(needle: str, haystack: str) -> bool:
        if not needle:
            return True
        idx = 0
        for ch in haystack:
            if ch == needle[idx]:
                idx += 1
                if idx == len(needle):
                    return True
        return False

    def _normalize_word_tokens(self, tokens):
        tokens = list(tokens or [])
        while tokens and tokens[0][0] == "gap":
            tokens.pop(0)
        while tokens and tokens[-1][0] == "gap":
            tokens.pop()
        if len(tokens) < 3:
            return tokens

        dit = max(24.0, float(self._dit_ms))
        gap = max(24.0, float(self._gap_ms))
        merged = []
        i = 0
        while i < len(tokens):
            if (
                i + 2 < len(tokens)
                and tokens[i][0] == "elem"
                and tokens[i + 1][0] == "gap"
                and tokens[i + 2][0] == "elem"
            ):
                left = float(tokens[i][1])
                mid = float(tokens[i + 1][1])
                right = float(tokens[i + 2][1])
                self._dbg_token_merge_attempts += 1
                tiny_gap = mid <= max(40.0, gap * 0.72, dit * 0.78)
                combined = left + right + min(mid * 0.35, dit * 0.25)
                if tiny_gap and combined >= dit * 1.45:
                    self._dbg_token_merges += 1
                    merged.append(("elem", combined))
                    i += 3
                    continue
            merged.append(tokens[i])
            i += 1
        return merged

    def _decode_word_tokens(self, tokens):
        tokens = self._normalize_word_tokens(tokens)
        if not tokens:
            return []

        dit = max(24.0, float(self._dit_ms))
        dah = max(dit * 1.9, float(self._dah_ms))
        gap = max(24.0, float(self._gap_ms))
        char_target = max(self._char_gap_ms * 0.9, gap * 1.55, dit * 1.55)

        states = [_BeamState(text="", chars=[], current="", current_conf=[], score=0.0)]
        for kind, ms in tokens:
            next_states = []
            if kind == "elem":
                for state in states:
                    dot_fit = abs(ms - dit) / max(16.0, dit * 0.38)
                    dash_fit = abs(ms - dah) / max(24.0, dah * 0.30)
                    dot_conf = float(np.clip(1.0 - dot_fit * 0.28, 0.0, 1.0))
                    dash_conf = float(np.clip(1.0 - dash_fit * 0.28, 0.0, 1.0))
                    next_states.append(_BeamState(
                        text=state.text,
                        chars=list(state.chars),
                        current=state.current + ".",
                        current_conf=state.current_conf + [dot_conf],
                        score=state.score - dot_fit,
                    ))
                    next_states.append(_BeamState(
                        text=state.text,
                        chars=list(state.chars),
                        current=state.current + "-",
                        current_conf=state.current_conf + [dash_conf],
                        score=state.score - dash_fit,
                    ))
            else:
                for state in states:
                    current_len = len(state.current)
                    hold_penalty = abs(ms - gap) / max(18.0, gap * 0.45) * 0.25
                    if state.current and ms >= max(gap * 1.28, dit * 1.22):
                        hold_penalty += 0.06 if current_len >= 2 else 0.10
                    next_states.append(_BeamState(
                        text=state.text,
                        chars=list(state.chars),
                        current=state.current,
                        current_conf=list(state.current_conf),
                        score=state.score - hold_penalty,
                    ))
                    split_floor = max(gap * 1.16, dit * 1.12) if current_len >= 2 else max(gap * 1.22, dit * 1.18)
                    if state.current and ms >= split_floor:
                        flushed = self._flush_pattern_candidate(
                            state.current, state.current_conf, state.text, state.chars, state.score
                        )
                        if flushed is not None:
                            gap_fit = abs(ms - char_target) / max(24.0, char_target * 0.35)
                            weak_split_penalty = (0.18 if current_len >= 2 else 0.28) if ms < char_target * 0.92 else 0.0
                            split_bonus = 0.10 if current_len >= 2 and ms >= char_target * 0.90 else (0.06 if ms >= char_target * 0.98 else 0.0)
                            flushed.score -= gap_fit * 0.18 + weak_split_penalty
                            flushed.score += split_bonus
                            next_states.append(flushed)
            states = self._prune_beam(next_states)
            if not states:
                return []

        final_states = []
        for state in states:
            if state.current:
                flushed = self._flush_pattern_candidate(
                    state.current, state.current_conf, state.text, state.chars, state.score
                )
                if flushed is not None:
                    final_states.append(flushed)
            else:
                final_states.append(state)
        final_states = self._prune_beam(final_states)
        if not final_states:
            return []
        return max(final_states, key=lambda s: s.score + 0.045 * len(s.chars)).chars

    def _greedy_decode_word_tokens(self, tokens):
        tokens = self._normalize_word_tokens(tokens)
        if not tokens:
            return []

        chars = []
        pattern = ""
        confidences = []
        dit = max(24.0, float(self._dit_ms))
        dah = max(dit * 1.9, float(self._dah_ms))
        gap = max(24.0, float(self._gap_ms))
        char_gap = max(self._char_gap_ms * 0.85, gap * 1.45, dit * 1.45)

        def flush():
            nonlocal pattern, confidences
            if not pattern:
                return
            ch = MORSE_DECODE.get(pattern)
            if ch:
                conf = float(np.mean(confidences)) if confidences else 0.55
                chars.append((ch, conf, pattern, False))
            pattern = ""
            confidences = []

        for kind, ms in tokens:
            if kind == "elem":
                split = (dit + dah) * 0.5
                if ms <= split:
                    pattern += "."
                    conf = 1.0 - min(1.0, abs(ms - dit) / max(dit * 0.85, 15.0))
                else:
                    pattern += "-"
                    conf = 1.0 - min(1.0, abs(ms - dah) / max(dah * 0.70, 20.0))
                confidences.append(float(np.clip(conf, 0.0, 1.0)))
            elif ms >= char_gap:
                flush()
        flush()
        return chars

    def _flush_pattern_candidate(self, pattern, confidences, text, chars, score):
        candidates = []
        ch = MORSE_DECODE.get(pattern)
        if ch:
            conf = float(np.mean(confidences)) if confidences else 0.55
            pattern_score = score + (conf - 0.55) * 1.2
            if len(pattern) == 1:
                pattern_score -= 0.18
            if ch.isdigit():
                pattern_score -= 0.25
            if len(chars) >= 1:
                pattern_score -= 0.06
            pattern_score += self._sequence_bonus(text + ch)
            candidates.append(_BeamState(
                text=text + ch,
                chars=list(chars) + [(ch, conf, pattern, False)],
                current="",
                current_conf=[],
                score=pattern_score,
            ))

        for idx in range(len(pattern) - 1, 0, -1):
            prefix = pattern[:idx]
            suffix = pattern[idx:]
            ch = MORSE_DECODE.get(prefix)
            if not ch or suffix not in MORSE_PREFIXES:
                continue
            prefix_conf = confidences[:idx] if confidences else []
            conf = (float(np.mean(prefix_conf)) if prefix_conf else 0.45) * 0.76
            recover_score = score - 0.48 + self._sequence_bonus(text + ch)
            candidates.append(_BeamState(
                text=text + ch,
                chars=list(chars) + [(ch, conf, prefix, True)],
                current=suffix,
                current_conf=confidences[idx:] if confidences else [],
                score=recover_score,
            ))
            break
        if not candidates:
            return None
        return max(candidates, key=lambda s: s.score)

    def _prune_beam(self, states, keep=8):
        if not states:
            return []
        best_by_key = {}
        for state in states:
            key = (state.text[-5:], state.current)
            prev = best_by_key.get(key)
            if prev is None or state.score > prev.score:
                best_by_key[key] = state
        pruned = sorted(best_by_key.values(), key=lambda s: s.score, reverse=True)
        return pruned[:keep]
    KNOWN_TERMS = {
        "CQ", "DE", "K", "KN", "BK", "AR", "SK", "TU", "TNX", "TEST",
        "POTA", "SOTA", "QRP", "QRS", "QRM", "QRN", "QSL", "QTH", "QSY",
        "QRZ", "RST", "ANT", "RIG", "WX", "FER", "UR", "MY", "OP", "NAME",
        "GM", "GA", "GE", "73", "599",
        "QST", "W1AW", "PFB", "CW", "FROM", "ARRL", "HQ", "CT",
    }
    BULLETIN_TERMS = {
        "EXPECTED", "SEVERAL", "PROXIMITY", "ACTIVITY", "FOLLOWS",
        "BULLETIN", "NEWINGTON", "REGIONS", "OBSERVED", "THROUGH",
    }
    CALLSIGN_RE = re.compile(r"^[A-Z]{1,2}\d[A-Z]{1,4}$")
    COMMON_BIGRAMS = {
        "TH", "HE", "ER", "RE", "AN", "IN", "EN", "ES", "ST", "TE", "NT", "EA",
        "CQ", "DE", "TU", "UR", "MY", "OP", "NA", "ME", "PO", "OT", "TA", "TR",
        "RS", "WX", "GE", "GM", "GA", "SL", "QR", "QS",
    }
    COMMON_TRIGRAMS = {
        "THE", "ING", "EST", "ENT", "ION", "TER", "RST", "POT", "OTA", "QSL",
        "CQC", "TEST", "NAM", "AME", "WX ", "QTH", "RIG", "ANT",
    }

    def _sequence_bonus(self, text: str) -> float:
        text = "".join(ch for ch in text.upper() if ch.isalnum())
        if not text:
            return 0.0
        bonus = 0.0
        if len(text) >= 2:
            bigram = text[-2:]
            if bigram in self.COMMON_BIGRAMS:
                bonus += 0.12
        if len(text) >= 3:
            trigram = text[-3:]
            if trigram in self.COMMON_TRIGRAMS:
                bonus += 0.18
            tail = text[-3:]
            vowels = sum(ch in "AEIOUY" for ch in tail)
            if vowels >= 3:
                bonus -= 0.16
        if len(text) >= 4:
            tail = text[-4:]
            vowels = sum(ch in "AEIOUY" for ch in tail)
            if vowels == 0 and not any(chunk in tail for chunk in ("POTA", "QRST", "W8ST", "RST")):
                bonus -= 0.12
        return bonus
