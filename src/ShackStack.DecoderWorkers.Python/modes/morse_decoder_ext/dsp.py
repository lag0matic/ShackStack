"""
dsp.py — DSP pipeline: BPF → frame RMS → 3-frame median → peak-tracked threshold → KeyEvents.

Signal chain (validated by simulation):

  1. Software BPF  — Butterworth 6th-order SOS, ±tone_bw/2 around tone_freq.
     Stateful across chunk boundaries; rejects adjacent-channel leakage.

  2. 5 ms frame RMS  — phase-independent amplitude; 40 samples at 8 kHz.

  3. Causal 3-frame running median  — eliminates single-frame noise spikes
     without bridging the 40-60 ms inter-element gaps in CW.

  4. Dual-floor close threshold
       close_t = max(noise_floor × 2.5,  peak_est × 0.05)
     The signal-relative floor (5 % of recent peak) is critical at high SNR,
     where the BPF fall-slope keeps power well above the noise-absolute floor
     for several frames after the key opens.  At low SNR the noise-absolute
     floor takes over, preventing premature closure from noise troughs.

  5. Gated adaptive noise floor  — updated only when the frame is below
     30 % of the close threshold (confirmed silence).  Asymmetric: slow rise
     (200 ms TC) tracks QRN buildup; fast fall (8 ms TC) tracks QRN reductions.

  6. Peak tracker  — fast rise (2 frames = 10 ms), slow fall (600 ms).
     Updated when the frame is above the close threshold.

  7. Open-hold squelch  — OPEN_HOLD = 2 frames before KEY_DOWN is declared.
     Eliminates residual noise spikes that survive the median.  The resulting
     ≤10 ms leading-edge bias is handled by the adaptive timing engine.
"""

from __future__ import annotations

import math
from collections import deque
from dataclasses import dataclass
from enum import Enum, auto
from typing import Deque, List

import numpy as np
from scipy.signal import butter, sosfilt, sosfilt_zi

from .config import MorseConfig


class KeyState(Enum):
    KEY_UP   = auto()
    KEY_DOWN = auto()


@dataclass
class KeyEvent:
    """A completed key-state interval."""
    state:      KeyState
    duration:   float        # seconds that state lasted
    mean_level: float = 0.0  # mean smoothed frame RMS during the interval


class DSPPipeline:
    """
    Stateful streaming DSP pipeline.

    Feed float32 PCM chunks of any size; receive KeyEvents.

    Example::

        pipeline = DSPPipeline(config)
        for chunk in audio_source:
            for ev in pipeline.process(chunk):
                timing_stage.feed(ev)
    """

    _OPEN_RATIO:   float = 5.0   # open_t  = noise × this
    _CLOSE_RATIO:  float = 2.5   # noise-relative close floor
    _PEAK_CLOSE:   float = 0.05  # signal-relative close floor (5 % of peak)
    _OPEN_HOLD:    int   = 2     # frames above open_t before KEY_DOWN
    _CLOSE_HOLD:   int   = 1     # frames below close_t before KEY_UP
    _MED_K:        int   = 3     # median window width (frames)

    def __init__(self, cfg: MorseConfig):
        self._cfg  = cfg
        self._sr   = cfg.sample_rate
        self._flen = max(1, int(5e-3 * self._sr))   # samples per 5 ms frame

        # ── BPF ──────────────────────────────────────────────────────────
        bw   = cfg.tone_bw / 2.0
        low  = max(cfg.tone_freq - bw,  20.0)
        high = min(cfg.tone_freq + bw,  self._sr / 2.0 - 10.0)
        self._sos    = butter(6, [low, high], btype="band",
                              fs=self._sr, output="sos")
        self._sos_zi = sosfilt_zi(self._sos)

        # ── Noise floor EMA constants (units: frames; 1 frame ≈ 5 ms) ───
        self._warmup_n  = 30
        self._a_nup     = math.exp(-1.0 / (200 / 5))   # 200 ms TC (noise rise)
        self._a_ndown   = math.exp(-1.0 / (8   / 5))   # 8 ms TC  (noise fall)

        # ── Peak tracker EMA constants ────────────────────────────────────
        self._a_pup     = math.exp(-1.0 / (10  / 5))   # 10 ms TC  (peak rise)
        self._a_pdown   = math.exp(-1.0 / (600 / 5))   # 600 ms TC (peak fall)

        self._noise      = 1e-4
        self._peak       = 1e-3
        self._state      = KeyState.KEY_UP
        self._n_frames   = 0
        self._st_frames  = 0
        self._level_acc  = 0.0
        self._open_hold  = 0
        self._close_hold = 0
        self._sample_buf: List[float]  = []
        self._med_buf:    Deque[float] = deque(maxlen=self._MED_K)

    # ── Public ────────────────────────────────────────────────────────────

    def process(self, pcm: np.ndarray) -> List[KeyEvent]:
        """Feed a 1-D float32 PCM chunk; return zero or more KeyEvents."""
        pcm = np.asarray(pcm, dtype=np.float32)
        filtered, self._sos_zi = sosfilt(self._sos, pcm, zi=self._sos_zi)
        self._sample_buf.extend(filtered.tolist())
        events: List[KeyEvent] = []
        while len(self._sample_buf) >= self._flen:
            frame = self._sample_buf[:self._flen]
            self._sample_buf = self._sample_buf[self._flen:]
            rms = float(np.sqrt(np.mean(np.square(frame))))
            events.extend(self._tick(rms))
        return events

    def reset(self) -> None:
        """Reset all state (call when retuning or starting a new session)."""
        self._sos_zi     = sosfilt_zi(self._sos)
        self._noise      = 1e-4
        self._peak       = 1e-3
        self._state      = KeyState.KEY_UP
        self._n_frames   = 0
        self._st_frames  = 0
        self._level_acc  = 0.0
        self._open_hold  = 0
        self._close_hold = 0
        self._sample_buf = []
        self._med_buf.clear()

    @property
    def key_state(self) -> KeyState:
        return self._state

    @property
    def noise_floor(self) -> float:
        """Current RMS noise floor estimate (after BPF)."""
        return self._noise

    @property
    def peak_level(self) -> float:
        """Current RMS peak signal estimate (after BPF)."""
        return self._peak

    # ── Internal ──────────────────────────────────────────────────────────

    def _tick(self, raw_rms: float) -> List[KeyEvent]:
        events: List[KeyEvent] = []
        self._n_frames += 1

        # 3-frame causal median (1-frame lag)
        self._med_buf.append(raw_rms)
        rms = float(np.median(self._med_buf))

        # Compute thresholds
        open_t  = self._noise * self._OPEN_RATIO
        close_t = max(self._noise  * self._CLOSE_RATIO,
                      self._peak   * self._PEAK_CLOSE)

        # ── Warmup: bootstrap noise estimate, no transitions ─────────────
        if self._n_frames <= self._warmup_n:
            self._noise = self._a_nup * self._noise + (1 - self._a_nup) * rms
            self._peak  = max(self._peak, rms)
            return events

        # ── Peak tracker ─────────────────────────────────────────────────
        if rms > close_t:
            self._peak = self._a_pup   * self._peak + (1 - self._a_pup)   * rms
        else:
            self._peak = self._a_pdown * self._peak + (1 - self._a_pdown) * rms

        # ── Gated noise floor (only update in confirmed silence) ──────────
        if rms < close_t * 0.3:
            alpha = self._a_ndown if rms < self._noise else self._a_nup
            self._noise = alpha * self._noise + (1 - alpha) * rms
            # Refresh thresholds after noise update
            open_t  = self._noise * self._OPEN_RATIO
            close_t = max(self._noise  * self._CLOSE_RATIO,
                          self._peak   * self._PEAK_CLOSE)

        self._st_frames += 1
        self._level_acc += rms

        # ── Squelch state machine ─────────────────────────────────────────
        if self._state is KeyState.KEY_UP:
            if rms > open_t:
                self._open_hold += 1
                if self._open_hold >= self._OPEN_HOLD:
                    events.append(self._emit(KeyState.KEY_UP))
                    self._state = KeyState.KEY_DOWN
                    self._open_hold = self._close_hold = 0
            else:
                self._open_hold = 0
        else:  # KEY_DOWN
            if rms < close_t:
                self._close_hold += 1
                if self._close_hold >= self._CLOSE_HOLD:
                    events.append(self._emit(KeyState.KEY_DOWN))
                    self._state = KeyState.KEY_UP
                    self._close_hold = self._open_hold = 0
            else:
                self._close_hold = 0

        return events

    def _emit(self, ending: KeyState) -> KeyEvent:
        dur   = self._st_frames * self._flen / self._sr
        level = self._level_acc / max(1, self._st_frames)
        self._st_frames = 0
        self._level_acc = 0.0
        return KeyEvent(state=ending, duration=dur, mean_level=level)
