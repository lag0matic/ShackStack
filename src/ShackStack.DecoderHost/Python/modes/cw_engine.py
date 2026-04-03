"""
ShackStack - modes/cw_engine.py

CW decoder using FFT envelope + histogram-based timing calibration.
Based on the RSCW algorithm (PA3FWM) — same approach as FLDigi.

Algorithm:
  1. Bandpass filter audio around CW tone
  2. Extract envelope (rectify + lowpass)
  3. Threshold envelope → key on/off events
  4. Accumulate element duration histogram
  5. Find dit/dah split from histogram valley
  6. Decode Morse pattern → character

CW encoder: convert text → dit/dah timing → key radio via rigctld
"""

import threading
import logging
import numpy as np
from dataclasses import dataclass
from collections import deque
from scipy.signal import butter, sosfilt

log = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Morse tables
# ---------------------------------------------------------------------------

MORSE_DECODE = {
    ".-": "A",   "-...": "B", "-.-.": "C", "-..": "D",  ".": "E",
    "..-.": "F", "--.": "G",  "....": "H", "..": "I",   ".---": "J",
    "-.-": "K",  ".-..": "L", "--": "M",   "-.": "N",   "---": "O",
    ".--.": "P", "--.-": "Q", ".-.": "R",  "...": "S",  "-": "T",
    "..-": "U",  "...-": "V", ".--": "W",  "-..-": "X", "-.--": "Y",
    "--..": "Z", ".----": "1","..---": "2","...--": "3","....-": "4",
    ".....": "5","-....": "6","--.." : "7","---..": "8","----.": "9",
    "-----": "0",".-.-.-": ".","--..--": ",","..--..": "?","-..-.": "/",
    "-.--.-": ")","-.--." : "(","---...": ":","-.-.-.": ";","-...-": "=",
    ".-.-.": "+", "-....-": "-", "..--.-": "_", ".-..-.": '"',
    "...-..-": "$", ".--.-.": "@", "...---...": "SOS",
}

MORSE_ENCODE = {v: k for k, v in MORSE_DECODE.items() if len(v) == 1}
MORSE_ENCODE.update({
    "1": ".----", "2": "..---", "3": "...--", "4": "....-", "5": ".....",
    "6": "-....", "7": "--...", "8": "---..", "9": "----.", "0": "-----",
    ".": ".-.-.-", ",": "--..--", "?": "..--..", "/": "-..-.",
    "=": "-...-",  "+": ".-.-.",  "-": "-....-",
})
MORSE_PREFIXES = {
    code[:i]
    for code in MORSE_DECODE
    for i in range(1, len(code) + 1)
}


@dataclass
class CwDecodeEvent:
    text: str
    confidence: float = 1.0
    pattern: str = ""
    recovered: bool = False
    is_space: bool = False


@dataclass
class _BeamState:
    text: str
    chars: list
    current: str
    current_conf: list
    score: float


# ---------------------------------------------------------------------------
# Decoder
# ---------------------------------------------------------------------------

class CwDecoder:
    """
    Real-time CW decoder.
    Feed audio chunks via push_samples().
    Decoded text arrives via text_callback(str).
    """

    BLOCK = 512          # FFT block size — ~10ms at 48kHz, good freq resolution
    HIST_BINS = 200      # Timing histogram bins
    HIST_MAX_MS = 600    # Max element duration tracked (ms)
    MIN_ELEM_MS = 24     # Ignore elements shorter than this (noise/ringing)
    MAX_ELEM_MS = 550    # Ignore elements longer than this (key-down noise)
    CHAR_SPACE_MULT = 1.6  # Split between intra-char and inter-char gaps
    AFC_MIN_SAMPLES = 2048
    AFC_SEARCH_HZ = 250
    AFC_ACQUIRE_MIN_SAMPLES = 4096
    AFC_ACQUIRE_LOW_HZ = 350
    AFC_ACQUIRE_HIGH_HZ = 1100
    IDLE_RESET_MS = 1400
    KEY_ON_BLOCKS = 3
    KEY_OFF_BLOCKS = 2
    MAX_PATTERN_LEN = 8

    def __init__(self, sample_rate: int = 48000, tone_hz: float = 700.0,
                 text_callback=None):
        self._sr          = sample_rate
        self._tone_hz     = tone_hz
        self._callback    = text_callback
        self._running     = False
        self._thread      = None
        self._queue       = deque(maxlen=2000)
        self._lock        = threading.Lock()

        # Timing histogram: buckets of 3ms each
        self._hist_res_ms = 3.0
        self._hist        = np.zeros(self.HIST_BINS, dtype=np.float32)

        # Callbacks
        self._afc_callback = None
        self._wpm_callback = None
        self._word_space_mult = 4.0

        # AFC state
        self._afc_enabled = True
        self._afc_buf     = np.array([], dtype=np.float32)
        self._threshold   = 0.25     # fraction of peak for key-on

        # Build filters
        self._build_filters()
        self.reset()

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def start(self):
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(target=self._run, daemon=True,
                                        name="cw-decoder")
        self._thread.start()

    def stop(self):
        self._running = False
        if self._thread:
            self._thread.join(timeout=2.0)

    def push_samples(self, samples: np.ndarray):
        with self._lock:
            self._queue.append(samples.astype(np.float32))

    @property
    def tone_hz(self):
        return self._tone_hz

    @tone_hz.setter
    def tone_hz(self, value: float):
        self._tone_hz = float(value)
        self._build_filters()
        self._afc_buf = np.array([], dtype=np.float32)

    @property
    def afc_enabled(self):
        return self._afc_enabled

    @afc_enabled.setter
    def afc_enabled(self, value: bool):
        self._afc_enabled = value

    @property
    def word_space_mult(self):
        return self._word_space_mult

    @word_space_mult.setter
    def word_space_mult(self, value: float):
        self._word_space_mult = float(value)

    def set_afc_callback(self, fn):
        self._afc_callback = fn

    def set_wpm_callback(self, fn):
        self._wpm_callback = fn

    def reset(self):
        """Clear timing history and decoder state — call when switching stations."""
        with self._lock:
            self._queue.clear()
        self._hist[:] = 0
        self._hist_count = 0
        self._current = ""
        self._current_confidences = []
        self._current_recovered = False
        self._dit_ms = 60.0
        self._dah_ms = 180.0
        self._gap_ms = 60.0
        self._last_gap_ms = 0.0
        self._recent_elements = deque(maxlen=24)
        self._accepted_elements = 0
        self._timing_stable = False
        self._lock_state = "acquire"
        self._lock_confidence = 0.0
        self._startup_tokens = []
        self._startup_replayed = False
        self._word_tokens = []
        self._space_samples = 0
        self._env_buf = np.array([], dtype=np.float32)
        self._env_val = 0.0
        self._mix_phase = 0.0
        self._i_lp_state = None
        self._q_lp_state = None
        self._key_down = False
        self._key_down_samples = 0
        self._key_up_samples = 0
        self._on_blocks = 0
        self._off_blocks = 0
        self._bp_state = None
        self._noise_floor = 0.01
        self._peak = 0.1
        self._signal_level = 0.08
        self._threshold = 0.25
        self._afc_buf = np.array([], dtype=np.float32)
        self._coarse_afc_buf = np.array([], dtype=np.float32)
        self._since_emit_samples = 0
        self._last_emitted = ""
        self._repeat_count = 0
        self._recent_emits = deque(maxlen=8)
        self._word_space_emitted = False

    # ------------------------------------------------------------------
    # Filter construction
    # ------------------------------------------------------------------

    def _build_filters(self):
        """Bandpass ±150Hz around tone + lowpass envelope smoother."""
        nyq = self._sr / 2.0
        lo  = max(50, self._tone_hz - 150) / nyq
        hi  = min(0.99, (self._tone_hz + 150) / nyq)
        self._bp_sos = butter(4, [lo, hi], btype='band', output='sos')
        # Lowpass for envelope smoothing: cutoff ~100Hz
        lp_cut = 100.0 / nyq
        self._lp_sos = butter(4, lp_cut, btype='low', output='sos')
        self._bp_state = None

    # ------------------------------------------------------------------
    # Main decode loop
    # ------------------------------------------------------------------

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
        """Process one chunk of audio."""
        self._since_emit_samples += len(samples)

        # 1. Bandpass filter
        if self._bp_state is None:
            # Match the filter's initial state to the first sample rather than
            # the default step response. This avoids a large startup transient
            # that can wipe out the first real character.
            n_sections = self._bp_sos.shape[0]
            first = float(samples[0]) if len(samples) else 0.0
            self._bp_state = np.zeros((n_sections, 2), dtype=np.float32)
            if first:
                from scipy.signal import sosfilt_zi
                self._bp_state = sosfilt_zi(self._bp_sos) * first
        filtered, self._bp_state = sosfilt(self._bp_sos, samples,
                                            zi=self._bp_state)

        # 2. Coherent tone detection: mix the filtered signal down to baseband,
        # low-pass I/Q, then measure magnitude. This is much less ringy than a
        # plain abs(filtered) envelope and holds up better on weak live CW.
        if self._i_lp_state is None or self._q_lp_state is None:
            n_sections = self._lp_sos.shape[0]
            self._i_lp_state = np.zeros((n_sections, 2), dtype=np.float32)
            self._q_lp_state = np.zeros((n_sections, 2), dtype=np.float32)

        phase_step = 2.0 * np.pi * self._tone_hz / self._sr
        phases = self._mix_phase + phase_step * np.arange(len(filtered), dtype=np.float32)
        osc_i = np.cos(phases).astype(np.float32)
        osc_q = (-np.sin(phases)).astype(np.float32)
        mixed_i = filtered * osc_i
        mixed_q = filtered * osc_q
        bb_i, self._i_lp_state = sosfilt(self._lp_sos, mixed_i, zi=self._i_lp_state)
        bb_q, self._q_lp_state = sosfilt(self._lp_sos, mixed_q, zi=self._q_lp_state)
        self._mix_phase = float((self._mix_phase + phase_step * len(filtered)) % (2.0 * np.pi))
        raw_env = np.sqrt(bb_i * bb_i + bb_q * bb_q)

        alpha_attack  = 1.0 - np.exp(-1.0 / (self._sr * 0.0007)) # 0.7ms attack
        alpha_release = 1.0 - np.exp(-1.0 / (self._sr * 0.0060))  # 6ms release
        envelope = np.empty_like(raw_env)
        env_val = getattr(self, '_env_val', 0.0)
        for i, s in enumerate(raw_env):
            if s > env_val:
                env_val += alpha_attack  * (s - env_val)
            else:
                env_val += alpha_release * (s - env_val)
            envelope[i] = env_val
        self._env_val = env_val

        # 3. Update noise/peak trackers
        block_max = float(np.max(envelope))
        peak_decay = 0.995 if not self._key_down else 0.999
        self._peak = max(self._peak * peak_decay, block_max)
        # Only update noise floor when key is down=False (silence)
        if not self._key_down:
            block_rms = float(np.mean(envelope))
            self._noise_floor = 0.995 * self._noise_floor + 0.005 * block_rms
            if block_rms > self._noise_floor * 1.6:
                self._signal_level = max(
                    self._signal_level * 0.996,
                    block_rms,
                )
            else:
                floor = max(self._noise_floor * 1.8, 0.02)
                self._signal_level = max(self._signal_level * 0.997, floor)
        else:
            self._signal_level = max(self._signal_level * 0.999, block_max)

        # 4. Key detection — block-level to avoid Python per-sample overhead
        # Use block RMS vs threshold rather than per-sample to avoid filter ringing
        # Threshold tracks the midpoint between the estimated noise floor and
        # the recent signal level. This is more tolerant of live-level changes
        # than a stale fraction of the absolute peak alone.
        signal_level = max(self._signal_level, self._noise_floor * 2.0, self._peak * 0.35)
        span = max(signal_level - self._noise_floor, self._noise_floor * 0.8, 0.01)
        hi_thresh = self._noise_floor + 0.42 * span
        lo_thresh = self._noise_floor + 0.28 * span

        # AFC: analyse raw audio while the decoder believes tone is present.
        # Using the raw samples lets AFC follow pitch changes instead of being
        # trapped by the current bandpass centre.
        if self._afc_enabled:
            afc_gate = hi_thresh * 1.05
            voiced = samples[envelope > afc_gate]
            if len(voiced) >= 128:
                self._afc_buf = np.concatenate([self._afc_buf, voiced.astype(np.float32)])
                if len(self._afc_buf) >= self.AFC_MIN_SAMPLES:
                    self._do_afc(self._afc_buf[:self.AFC_MIN_SAMPLES])
                    self._afc_buf = self._afc_buf[self.AFC_MIN_SAMPLES // 2:]
            elif self._since_emit_samples >= int(self._sr * 0.8):
                self._coarse_afc_buf = np.concatenate(
                    [self._coarse_afc_buf, samples.astype(np.float32)]
                )
                if len(self._coarse_afc_buf) >= self.AFC_ACQUIRE_MIN_SAMPLES:
                    self._do_coarse_afc(self._coarse_afc_buf[:self.AFC_ACQUIRE_MIN_SAMPLES])
                    self._coarse_afc_buf = self._coarse_afc_buf[self.AFC_ACQUIRE_MIN_SAMPLES // 2:]

        # Process in 64-sample sub-blocks (~1.3ms each) for timing resolution
        sub = 64
        for i in range(0, len(envelope), sub):
            block = envelope[i:i+sub]
            block_mean = float(np.mean(block))
            if self._key_down:
                present = block_mean > lo_thresh
            else:
                present = block_mean > hi_thresh
            # Count samples for timing
            n = len(block)

            if present:
                self._on_blocks += 1
                self._off_blocks = 0
            else:
                self._off_blocks += 1
                self._on_blocks = 0

            if not self._key_down:
                if not present:
                    self._key_up_samples += n
                if self._on_blocks >= self.KEY_ON_BLOCKS:
                    start_samples = self.KEY_ON_BLOCKS * sub
                    space_ms = self._key_up_samples / self._sr * 1000
                    self._on_space(space_ms)
                    self._key_down = True
                    self._key_down_samples = start_samples
                    self._key_up_samples = 0
                    self._word_space_emitted = False
                    self._on_blocks = 0
                    self._off_blocks = 0
                continue

            if present:
                self._key_down_samples += n
            else:
                self._key_up_samples += n
                if self._off_blocks >= self.KEY_OFF_BLOCKS:
                    trailing_samples = self.KEY_OFF_BLOCKS * sub
                    elem_ms = max(0, self._key_down_samples - trailing_samples) / self._sr * 1000
                    self._on_element(elem_ms)
                    self._key_down = False
                    self._key_down_samples = 0
                    self._key_up_samples = trailing_samples
                    self._on_blocks = 0
                    self._off_blocks = 0
                else:
                    continue

            up_ms = self._key_up_samples / self._sr * 1000
            if up_ms > self.IDLE_RESET_MS:
                self._reset_tracking_after_idle()
            if up_ms > self._dit_ms * 3.5 and self._current:
                self._flush_char()
            if up_ms > self._dit_ms * self._word_space_mult:
                if (not getattr(self, '_word_space_emitted', False)
                        and getattr(self, '_last_emitted', '') not in ('', ' ')):
                    self._emit(" ", confidence=0.95, is_space=True)
                    self._word_space_emitted = True



    def _on_element(self, duration_ms: float):
        """Key went up — record element duration in histogram."""
        if duration_ms < self.MIN_ELEM_MS or duration_ms > self.MAX_ELEM_MS:
            return
        if not self._startup_replayed:
            self._startup_tokens.append(("elem", float(duration_ms)))
        if not self._timing_stable:
            provisional_dit = max(
                self._dit_ms,
                self._gap_ms if 40.0 <= self._gap_ms <= 260.0 else 0.0,
                self._last_gap_ms if 40.0 <= self._last_gap_ms <= 260.0 else 0.0,
            )
            if provisional_dit > 0 and duration_ms < provisional_dit * 0.58:
                return
        # Add to histogram
        bin_idx = min(self.HIST_BINS - 1,
                      int(duration_ms / self._hist_res_ms))
        self._hist[bin_idx] += 1
        self._hist_count += 1
        self._accepted_elements += 1
        self._recent_elements.append(float(duration_ms))

        # Recalibrate timing every 4 elements for fast initial lock
        if self._hist_count % 4 == 0:
            self._calibrate()

        self._word_tokens.append(("elem", float(duration_ms)))
        self._lock_confidence = min(1.0, self._lock_confidence + 0.08)
        self._lock_state = "track" if self._timing_stable else "acquire"
        self._classify_element(duration_ms)
        self._maybe_release_startup()

    def _calibrate(self):
        """
        Update dit/dah estimates from recent element clusters, but only when
        the split looks physically plausible and well-separated.
        """
        recent = list(self._recent_elements)
        if len(recent) < 6:
            return

        samples = sorted(float(v) for v in recent)
        gaps = [samples[i + 1] - samples[i] for i in range(len(samples) - 1)]
        if not gaps:
            return

        max_gap_idx = int(np.argmax(gaps))
        max_gap = gaps[max_gap_idx]
        dits = samples[:max_gap_idx + 1]
        dahs = samples[max_gap_idx + 1:]
        if len(dits) < 3 or len(dahs) < 2:
            self._timing_stable = False
            return

        new_dit = float(np.mean(dits))
        new_dah = float(np.mean(dahs))
        if self._gap_ms >= 55.0:
            new_dit = max(new_dit, self._gap_ms * 0.88)
        ratio = new_dah / max(new_dit, 1e-6)
        separation_needed = max(18.0, new_dit * 0.35)
        if not (20.0 <= new_dit <= 320.0 and new_dit * 1.8 <= new_dah <= 620.0
                and 1.9 <= ratio <= 4.6 and max_gap >= separation_needed):
            self._timing_stable = False
            return

        self._timing_stable = True
        self._dit_ms = 0.78 * self._dit_ms + 0.22 * new_dit
        self._dah_ms = 0.78 * self._dah_ms + 0.22 * new_dah

        # Fire WPM estimate only after timing has been stable for a while.
        if self._wpm_callback and self._hist_count >= 20:
            effective_dit = max(self._dit_ms, self._gap_ms * 0.92)
            wpm = max(5, min(35, int(1200 / effective_dit)))
            self._wpm_callback(wpm)

    def _classify_element(self, duration_ms: float):
        """Classify element as dit or dah and add to current pattern."""
        if not self._timing_stable and 40.0 <= self._last_gap_ms <= 220.0:
            if duration_ms >= self._last_gap_ms * 1.65:
                self._current += "-"
                self._recent_elements.append(float(duration_ms))
                self._current_confidences.append(0.82)
                if len(self._current) > self.MAX_PATTERN_LEN:
                    self._emit_char(self._current, self._current_confidences, recovered=True)
                    self._current = ""
                    self._current_confidences = []
                    self._current_recovered = False
                self._recover_prefix_split()
                return
            if duration_ms <= self._last_gap_ms * 1.35:
                self._current += "."
                self._recent_elements.append(float(duration_ms))
                self._current_confidences.append(0.82)
                if len(self._current) > self.MAX_PATTERN_LEN:
                    self._emit_char(self._current, self._current_confidences, recovered=True)
                    self._current = ""
                    self._current_confidences = []
                    self._current_recovered = False
                self._recover_prefix_split()
                return

        short_mean, long_mean, short_spread, long_spread = self._local_timing_model(duration_ms)
        if abs(duration_ms - short_mean) <= abs(duration_ms - long_mean):
            self._current += "."
            cluster_mean = short_mean
            cluster_spread = short_spread
        else:
            self._current += "-"
            cluster_mean = long_mean
            cluster_spread = long_spread
        self._recent_elements.append(float(duration_ms))
        confidence = self._element_confidence(duration_ms, short_mean, long_mean,
                                              cluster_mean, cluster_spread)
        self._current_confidences.append(confidence)
        if len(self._current) > self.MAX_PATTERN_LEN:
            self._emit_char(self._current, self._current_confidences, recovered=True)
            self._current = ""
            self._current_confidences = []
            self._current_recovered = False
            return
        self._recover_prefix_split()

    def _on_space(self, duration_ms: float):
        """Key went down — classify the preceding space."""
        self._word_space_emitted = False
        if duration_ms < self.MIN_ELEM_MS:
            return
        self._last_gap_ms = float(duration_ms)
        if not self._startup_replayed:
            self._startup_tokens.append(("gap", float(duration_ms)))
        # Track the operator's actual short-gap rhythm separately from dit
        # length. Slow hand-sent CW can have intra-character gaps far longer
        # than our initial dit guess, so let startup gaps pull the estimate up
        # quickly until timing stabilizes.
        if not self._timing_stable:
            if duration_ms <= max(self._gap_ms * 1.9, self._dit_ms * 2.6):
                self._gap_ms = 0.55 * self._gap_ms + 0.45 * duration_ms
        else:
            if duration_ms <= self._dit_ms * 1.5:
                self._gap_ms = 0.72 * self._gap_ms + 0.28 * duration_ms
            elif duration_ms <= max(self._gap_ms * 1.9, self._dit_ms * 2.6):
                self._gap_ms = 0.88 * self._gap_ms + 0.12 * duration_ms

        if not self._timing_stable:
            char_gap_ms = max(self._gap_ms * 1.72, self._dit_ms * 2.15)
            word_gap_ms = max(self._gap_ms * 3.25, self._dit_ms * self._word_space_mult)
            early_char_gap_ms = max(self._gap_ms * 1.34, self._dit_ms * 1.7)
        else:
            char_gap_ms = max(self._gap_ms * 1.50, self._dit_ms * 1.55)
            word_gap_ms = max(self._gap_ms * self._word_space_mult,
                              self._dit_ms * self._word_space_mult)
            early_char_gap_ms = max(self._gap_ms * 1.16, self._dit_ms * 1.08)

        self._word_tokens.append(("gap", float(duration_ms)))

        # If we've already formed a plausible full character and the next gap
        # is clearly longer than the sender's normal intra-element spacing,
        # flush early. This helps bug-style sending where character gaps can
        # drift shorter than textbook spacing.
        if (self._current in MORSE_DECODE and len(self._current) >= 4
                and duration_ms > early_char_gap_ms):
            self._flush_char()
            if duration_ms > word_gap_ms:
                if (not getattr(self, '_word_space_emitted', False)
                        and getattr(self, '_last_emitted', '') not in ('', ' ')):
                    self._emit(" ", confidence=0.95, is_space=True)
                    self._word_space_emitted = True
            self._maybe_release_startup()
            return

        if duration_ms > char_gap_ms:
            self._flush_char()
        if duration_ms > word_gap_ms:
            if (not getattr(self, '_word_space_emitted', False)
                    and getattr(self, '_last_emitted', '') not in ('', ' ')):
                self._emit(" ", confidence=0.95, is_space=True)
                self._word_space_emitted = True
        self._maybe_release_startup()

    def _flush_char(self):
        if not self._current:
            return
        self._emit_char(self._current, self._current_confidences,
                        recovered=self._current_recovered)
        self._current = ""
        self._current_confidences = []
        self._current_recovered = False

    def _emit(self, text: str, confidence: float = 1.0, pattern: str = "",
              recovered: bool = False, is_space: bool = False):
        if not text:
            return
        # Suppress double spaces
        if text == " " and getattr(self, '_last_emitted', '') == " ":
            return
        if text == getattr(self, '_last_emitted', '') and text != " ":
            self._repeat_count += 1
        else:
            self._repeat_count = 0
        self._last_emitted = text
        if text != " ":
            self._recent_emits.append(text)
        self._since_emit_samples = 0
        self._coarse_afc_buf = np.array([], dtype=np.float32)
        if self._callback:
            try:
                self._callback(CwDecodeEvent(
                    text=text,
                    confidence=max(0.0, min(1.0, float(confidence))),
                    pattern=pattern,
                    recovered=recovered,
                    is_space=is_space,
                ))
            except Exception:
                pass

    def _recover_prefix_split(self):
        """
        If the accumulated pattern can no longer be the prefix of any single
        Morse character, try to salvage the longest valid leading character and
        keep decoding the remaining suffix. This helps recover from missed
        character-gap detection under noisy or bug-style timing drift.
        """
        pattern = self._current
        if not pattern or pattern in MORSE_PREFIXES:
            return

        best_split = None
        for idx in range(len(pattern) - 1, 0, -1):
            prefix = pattern[:idx]
            suffix = pattern[idx:]
            if prefix not in MORSE_DECODE:
                continue
            if suffix in MORSE_PREFIXES:
                best_split = (prefix, suffix)
                break

        if not best_split:
            return

        prefix, suffix = best_split
        ch = MORSE_DECODE.get(prefix)
        if ch:
            split_idx = len(prefix)
            prefix_conf = self._current_confidences[:split_idx]
            suffix_conf = self._current_confidences[split_idx:]
            self._emit_char(prefix, prefix_conf, recovered=True)
            self._current = suffix
            self._current_confidences = suffix_conf
            self._current_recovered = True

    def _reset_tracking_after_idle(self):
        """Relax fast-moving tracking state so decode/AFC can re-acquire cleanly."""
        self._peak = max(self._noise_floor * 4.0, 0.08)
        self._afc_buf = np.array([], dtype=np.float32)

    def _emit_char(self, pattern: str, confidences, recovered: bool = False):
        ch = MORSE_DECODE.get(pattern, "?")
        if confidences:
            confidence = float(np.mean(confidences))
        else:
            confidence = 0.3 if ch == "?" else 0.6
        if not self._startup_replayed:
            return
        if len(pattern) == 1:
            confidence *= 0.55
            # Single-element characters are especially ambiguous under noise.
            # Only let them rise high when spacing is strongly consistent.
            gap_ratio = self._gap_ms / max(self._dit_ms, 1e-6)
            if 0.75 <= gap_ratio <= 1.45:
                confidence *= 1.15
        if recovered:
            confidence *= 0.82
        if ch == "?":
            confidence *= 0.5
        gap_ratio = self._gap_ms / max(self._dit_ms, 1e-6)
        if gap_ratio < 0.72 or gap_ratio > 1.85:
            confidence *= 0.78
        dot_ratio = pattern.count(".") / max(1, len(pattern))
        if dot_ratio >= 0.85 and len(pattern) <= 4:
            confidence *= 0.9
        if pattern in {"....", "...--"}:
            confidence *= 0.88
        if self._lock_state != "track":
            confidence *= 0.88
        if len(pattern) >= 5:
            confidence *= 0.9
        recent = "".join(self._recent_emits)
        if recent and sum(ch2 in "EISHNRT" for ch2 in recent[-4:]) >= 4 and dot_ratio >= 0.85:
            confidence *= 0.88
        if confidence < 0.32:
            return
        if len(pattern) == 1 and confidence < 0.64:
            return
        if len(pattern) == 2 and confidence < 0.42:
            return
        if dot_ratio >= 0.9 and len(pattern) <= 4 and confidence < 0.68:
            return
        if ch == "?" and confidence < 0.75:
            return
        if not ch.isalpha() and ch != " " and confidence < 0.9:
            return
        if ch.isdigit():
            return
        if self._repeat_count >= 2 and confidence < 0.82:
            return
        self._emit(ch, confidence=confidence, pattern=pattern, recovered=recovered)

    def _local_timing_model(self, duration_ms: float):
        samples = list(self._recent_elements)
        samples.append(float(duration_ms))
        if len(samples) < 4:
            short_mean = self._dit_ms
            long_mean = self._dah_ms
        else:
            vals = np.array(samples, dtype=np.float32)
            if not self._timing_stable:
                c1 = float(np.percentile(vals, 25))
                c2 = float(np.percentile(vals, 75))
                if c2 <= c1 * 1.35:
                    c1 = float(np.min(vals))
                    c2 = float(np.max(vals))
            else:
                c1 = float(self._dit_ms)
                c2 = float(max(self._dah_ms, self._dit_ms * 2.0))
            for _ in range(4):
                d1 = np.abs(vals - c1)
                d2 = np.abs(vals - c2)
                m1 = d1 <= d2
                m2 = ~m1
                if np.any(m1):
                    c1 = float(np.mean(vals[m1]))
                if np.any(m2):
                    c2 = float(np.mean(vals[m2]))
            short_mean, long_mean = sorted((c1, c2))

        # Keep the local model physically plausible for Morse timing.
        short_mean = float(np.clip(short_mean, 20.0, 320.0))
        long_mean = float(max(long_mean, short_mean * 1.9))
        long_mean = float(min(long_mean, 620.0))

        short_vals = [v for v in samples if abs(v - short_mean) <= abs(v - long_mean)]
        long_vals = [v for v in samples if abs(v - short_mean) > abs(v - long_mean)]
        short_spread = max(8.0, float(np.std(short_vals)) if len(short_vals) >= 2 else short_mean * 0.18)
        long_spread = max(12.0, float(np.std(long_vals)) if len(long_vals) >= 2 else long_mean * 0.18)
        return short_mean, long_mean, short_spread, long_spread

    def _element_confidence(self, duration_ms: float, short_mean: float, long_mean: float,
                             cluster_mean: float, cluster_spread: float) -> float:
        split = (short_mean + long_mean) / 2.0
        margin = abs(duration_ms - split)
        separation = max(12.0, abs(long_mean - short_mean))
        margin_score = min(1.0, margin / (separation * 0.5))
        cluster_score = max(0.0, 1.0 - abs(duration_ms - cluster_mean) / (cluster_spread * 2.5))
        return float(np.clip(0.45 * margin_score + 0.55 * cluster_score, 0.0, 1.0))

    def _decode_buffered_word(self):
        tokens = self._word_tokens
        if not tokens:
            return
        self._word_tokens = []
        while tokens and tokens[-1][0] == "gap":
            tokens = tokens[:-1]
        if not tokens:
            return

        chars = self._beam_decode_tokens(tokens)
        greedy_chars = self._greedy_decode_tokens(tokens)
        if len("".join(ch for ch, *_ in greedy_chars)) > len("".join(ch for ch, *_ in chars)):
            chars = greedy_chars
        if not chars:
            self._lock_confidence = max(0.0, self._lock_confidence - 0.12)
            self._lock_state = "acquire"
            return

        emitted = 0
        for ch, conf, pattern, recovered in chars:
            before = self._last_emitted
            self._emit(ch, confidence=conf, pattern=pattern, recovered=recovered)
            if self._last_emitted != before:
                emitted += 1
        if emitted:
            self._lock_confidence = min(1.0, self._lock_confidence + 0.15)
            self._lock_state = "track"
        else:
            self._lock_confidence = max(0.0, self._lock_confidence - 0.08)
            if self._lock_confidence < 0.3:
                self._lock_state = "acquire"

    def _beam_decode_tokens(self, tokens):
        dit = max(24.0, float(self._dit_ms))
        dah = max(dit * 1.9, float(self._dah_ms))
        gap = max(24.0, float(self._gap_ms))
        intra_limit = max(gap * 1.45, dit * 1.40)
        char_target = max(gap * 2.05, dit * 2.15)
        word_target = max(gap * self._word_space_mult, dit * self._word_space_mult)

        states = [_BeamState(text="", chars=[], current="", current_conf=[], score=0.0)]

        for kind, ms in tokens:
            next_states = []
            if kind == "elem":
                for state in states:
                    dot_fit = abs(ms - dit) / max(22.0, dit * 0.42)
                    dash_fit = abs(ms - dah) / max(35.0, dah * 0.32)
                    dot_conf = float(np.clip(1.0 - dot_fit * 0.30, 0.0, 1.0))
                    dash_conf = float(np.clip(1.0 - dash_fit * 0.30, 0.0, 1.0))
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
                    intra_pen = abs(ms - gap) / max(20.0, gap * 0.50)
                    if ms <= word_target * 0.92:
                        next_states.append(_BeamState(
                            text=state.text,
                            chars=list(state.chars),
                            current=state.current,
                            current_conf=list(state.current_conf),
                            score=state.score - intra_pen * 0.55,
                        ))

                    if state.current:
                        flushed = self._flush_pattern_candidate(
                            state.current, state.current_conf, state.text, state.chars, state.score
                        )
                        if flushed is not None:
                            char_pen = abs(ms - char_target) / max(28.0, char_target * 0.42)
                            flush_state = flushed
                            flush_state.score -= char_pen * 0.45
                            next_states.append(flush_state)

                if not next_states:
                    next_states = states

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
        best = max(final_states, key=lambda s: s.score)
        if not best.chars:
            return []
        return best.chars

    def _greedy_decode_tokens(self, tokens):
        chars = []
        pattern = ""
        confidences = []
        dit = max(24.0, float(self._dit_ms))
        dah = max(dit * 1.9, float(self._dah_ms))
        gap = max(24.0, float(self._gap_ms))
        char_gap = max(gap * 1.52, dit * 1.58)

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
                short_mean, long_mean, short_spread, long_spread = self._local_timing_model(ms)
                if abs(ms - short_mean) <= abs(ms - long_mean):
                    pattern += "."
                    confidences.append(self._element_confidence(
                        ms, short_mean, long_mean, short_mean, short_spread
                    ))
                else:
                    pattern += "-"
                    confidences.append(self._element_confidence(
                        ms, short_mean, long_mean, long_mean, long_spread
                    ))
            elif ms > char_gap:
                flush()
        flush()
        return chars

    def _flush_pattern_candidate(self, pattern, confidences, text, chars, score):
        candidates = []
        ch = MORSE_DECODE.get(pattern)
        if ch:
            conf = float(np.mean(confidences)) if confidences else 0.55
            pattern_score = score + (conf - 0.55) * 1.4
            if len(pattern) == 1:
                pattern_score -= 0.28
            if not ch.isalpha():
                pattern_score -= 0.45
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
            conf = (float(np.mean(prefix_conf)) if prefix_conf else 0.45) * 0.78
            candidates.append(_BeamState(
                text=text + ch,
                chars=list(chars) + [(ch, conf, prefix, True)],
                current=suffix,
                current_conf=confidences[idx:] if confidences else [],
                score=score - 0.45,
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

    def _maybe_release_startup(self):
        if self._startup_replayed:
            return
        if not self._startup_tokens:
            return
        char_gap_ms = max(self._gap_ms * 1.5, self._dit_ms * 1.55)
        last_kind, last_ms = self._startup_tokens[-1]
        if last_kind != "gap" or last_ms <= char_gap_ms:
            return
        if not self._timing_stable and self._accepted_elements < 12:
            return

        tokens = self._startup_tokens
        self._startup_tokens = []
        self._startup_replayed = True
        self._current = ""
        self._current_confidences = []
        self._current_recovered = False

        word_gap_ms = max(self._gap_ms * self._word_space_mult,
                          self._dit_ms * self._word_space_mult)
        pattern = []

        def flush_pattern():
            if not pattern:
                return
            code = "".join(pattern)
            confidence = 0.68 if code in MORSE_DECODE else 0.28
            self._emit(MORSE_DECODE.get(code, "?"),
                       confidence=confidence,
                       pattern=code,
                       recovered=code not in MORSE_DECODE)
            pattern.clear()

        for kind, ms in tokens:
            if kind == "gap":
                if ms > word_gap_ms:
                    flush_pattern()
                    self._emit(" ", confidence=0.95, is_space=True)
                elif ms > char_gap_ms:
                    flush_pattern()
                continue

            if abs(ms - self._dit_ms) <= abs(ms - self._dah_ms):
                pattern.append(".")
            else:
                pattern.append("-")
            if len(pattern) > self.MAX_PATTERN_LEN:
                flush_pattern()

        flush_pattern()

    # ------------------------------------------------------------------
    # AFC
    # ------------------------------------------------------------------

    def _do_afc(self, buf: np.ndarray):
        """FFT-based AFC — only nudge when signal is present."""
        if len(buf) < 512:
            return

        spectrum = np.abs(np.fft.rfft(buf * np.hanning(len(buf))))
        freqs    = np.fft.rfftfreq(len(buf), 1.0 / self._sr)

        lo, hi = self._tone_hz - self.AFC_SEARCH_HZ, self._tone_hz + self.AFC_SEARCH_HZ
        mask = (freqs >= lo) & (freqs <= hi)
        if not mask.any():
            return

        region = spectrum[mask]
        region_freqs = freqs[mask]
        noise  = np.median(region)
        if noise == 0:
            return
        peak_mag = region.max()
        if peak_mag < noise * 4:
            return   # no clear signal

        strong_floor = max(noise * 4, peak_mag * 0.45)
        strong_idx = np.flatnonzero(region >= strong_floor)
        if len(strong_idx) == 0:
            strong_idx = np.array([int(np.argmax(region))])

        # Prefer strong peaks nearest the current tone. This avoids AFC
        # latching onto CW keying sidebands instead of the carrier tone.
        peak_idx = min(strong_idx, key=lambda idx: abs(region_freqs[idx] - self._tone_hz))

        # Power-weighted centroid around the chosen peak improves sub-bin
        # stability without letting distant sidebands pull the estimate.
        lo_idx = max(0, peak_idx - 1)
        hi_idx = min(len(region), peak_idx + 2)
        local_mag = region[lo_idx:hi_idx]
        local_freqs = region_freqs[lo_idx:hi_idx]
        peak_hz = float(np.sum(local_freqs * local_mag) / np.sum(local_mag))

        delta = peak_hz - self._tone_hz
        delta = float(np.clip(delta, -25.0, 25.0))
        new_hz  = round(self._tone_hz + 0.45 * delta)
        if abs(new_hz - self._tone_hz) >= 1:
            self._tone_hz = new_hz
            self._build_filters()
            if self._afc_callback:
                self._afc_callback(new_hz)

    def _do_coarse_afc(self, buf: np.ndarray):
        """Wideband tone acquisition for startup/re-lock when decode has gone quiet."""
        if len(buf) < 1024:
            return

        windowed = buf * np.hanning(len(buf))
        spectrum = np.abs(np.fft.rfft(windowed))
        freqs = np.fft.rfftfreq(len(buf), 1.0 / self._sr)
        mask = ((freqs >= self.AFC_ACQUIRE_LOW_HZ)
                & (freqs <= self.AFC_ACQUIRE_HIGH_HZ))
        if not mask.any():
            return

        region = spectrum[mask]
        region_freqs = freqs[mask]
        noise = float(np.median(region))
        if noise <= 0:
            return
        peak_idx = int(np.argmax(region))
        peak_mag = float(region[peak_idx])
        if peak_mag < noise * 7.0:
            return

        peak_hz = float(region_freqs[peak_idx])
        new_hz = round(0.7 * self._tone_hz + 0.3 * peak_hz)
        if abs(new_hz - self._tone_hz) >= 8:
            self._tone_hz = new_hz
            self._build_filters()
            self._afc_buf = np.array([], dtype=np.float32)
            if self._afc_callback:
                self._afc_callback(new_hz)


# ---------------------------------------------------------------------------
# Encoder
# ---------------------------------------------------------------------------

class CwEncoder:
    """Convert text to Morse timing for keying the radio."""

    def __init__(self, sample_rate: int = 48000,
                 tone_hz: float = 700.0, wpm: int = 20):
        self._sr      = sample_rate
        self._tone_hz = tone_hz
        self.wpm      = wpm

    @property
    def tone_hz(self) -> float:
        return self._tone_hz

    @tone_hz.setter
    def tone_hz(self, value: float) -> None:
        self._tone_hz = float(value)

    @property
    def wpm(self):
        return self._wpm

    @wpm.setter
    def wpm(self, value: int):
        self._wpm = max(5, min(60, int(value)))
        self._dit = int(self._sr * 1.2 / self._wpm)

    def encode_text(self, text: str) -> np.ndarray:
        """Return float32 audio array for the given text."""
        dit = self._dit
        chunks = []

        def tone(n):
            t = np.arange(n) / self._sr
            w = 2 * np.pi * self._tone_hz * t
            # Short ramp in/out to avoid clicks
            env = np.ones(n)
            r = min(int(self._sr * 0.005), n // 4)
            env[:r]  = np.linspace(0, 1, r)
            env[-r:] = np.linspace(1, 0, r)
            return (0.8 * np.sin(w) * env).astype(np.float32)

        def silence(n):
            return np.zeros(n, dtype=np.float32)

        for i, ch in enumerate(text.upper()):
            if ch == ' ':
                chunks.append(silence(dit * 4))  # inter-word extra space
                continue
            pattern = MORSE_ENCODE.get(ch)
            if not pattern:
                continue
            for j, el in enumerate(pattern):
                chunks.append(tone(dit if el == '.' else dit * 3))
                if j < len(pattern) - 1:
                    chunks.append(silence(dit))
            if i < len(text) - 1 and text[i + 1] != ' ':
                chunks.append(silence(dit * 2))  # inter-char gap

        return np.concatenate(chunks) if chunks else np.zeros(0, dtype=np.float32)

    def timing(self, text: str):
        """
        Yield (action, duration_ms) tuples for keying the radio.
        action: 'key_down' or 'key_up'
        """
        dit_ms = 1200 / self._wpm
        for i, ch in enumerate(text.upper()):
            if ch == ' ':
                yield ('key_up', dit_ms * 7)
                continue
            pattern = MORSE_ENCODE.get(ch)
            if not pattern:
                continue
            for j, el in enumerate(pattern):
                yield ('key_down', dit_ms if el == '.' else dit_ms * 3)
                yield ('key_up',   dit_ms)
            if i < len(text) - 1 and text[i + 1] != ' ':
                yield ('key_up', dit_ms * 2)
