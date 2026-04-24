"""
config.py — Central configuration for the Morse decoder pipeline.

All tunable parameters live here so downstream stages stay stateless
and easily unit-testable.  Pass a MorseConfig instance to every stage.
"""

from dataclasses import dataclass, field
from typing import Literal


@dataclass
class MorseConfig:
    # ── Audio input ────────────────────────────────────────────────────────
    sample_rate: int = 8000          # Hz — 8 k is typical for narrowband CW

    # ── Signal detection ───────────────────────────────────────────────────
    #  The radio's IF filter has already done the heavy BPF work.
    #  We still run a *narrow* software BPF centred on the tone to reject
    #  nearby operators that leaked through the IF skirts.
    tone_freq: float         = 750.0  # Expected CW tone, 600–1000 Hz
    tone_bw:   float         = 150.0  # Software BPF width (Hz) around tone_freq
    #  IF bandwidth affects noise-floor estimates — tell the detector what
    #  the radio's front-end already did so it can scale thresholds.
    if_bandwidth: Literal[1200, 500, 250] = 500

    # ── Envelope detector ──────────────────────────────────────────────────
    envelope_attack_ms:  float = 1.5   # fast attack to catch leading edge of dit
    envelope_decay_ms:   float = 8.0   # slower decay to ride out brief dropouts

    # ── Adaptive noise floor / threshold ──────────────────────────────────
    noise_percentile:      float = 25.0  # percentile of recent envelope = noise
    signal_percentile:     float = 75.0  # percentile for signal estimate
    noise_history_ms:      float = 600.0 # rolling window for noise stats
    snr_open_threshold:    float = 4.0   # S/N ratio to declare KEY_DOWN
    snr_close_threshold:   float = 2.5   # S/N ratio to declare KEY_UP (hysteresis)

    # ── Timing / WPM adaptation ────────────────────────────────────────────
    #  At 20 WPM: dit = 60 ms, dah = 180 ms, inter-element gap = 60 ms,
    #             inter-char gap = 180 ms, inter-word gap = 420 ms
    #  The decoder estimates WPM from observed element durations using a
    #  running median so that one badly-keyed element doesn't skew everything.
    wpm_initial:        float = 20.0
    wpm_min:            float = 5.0
    wpm_max:            float = 60.0
    wpm_adapt_weight:   float = 0.12   # EMA weight — lower = smoother adaptation
    #  Tolerance windows: how far from nominal before we re-classify an element.
    #  Hand-keyed CW can be 30–50 % off; we're generous.
    dit_dah_boundary:   float = 0.50   # fraction of (dit+dah)/2 used as boundary
    #  Gap classification multipliers relative to nominal dit length
    intra_char_max:     float = 1.2    # below this → inter-element gap
    inter_char_max:     float = 4.0    # below this → inter-character gap
    #                                    above this → inter-word gap

    # ── Decoder ────────────────────────────────────────────────────────────
    max_decode_errors:  int   = 1      # fuzzy match: tolerate N dit↔dah swaps

    # ── Output ─────────────────────────────────────────────────────────────
    emit_unknown_char:  bool  = True   # emit '?' for unrecognised sequences
    emit_prosigns:      bool  = True   # emit prosigns like <SK>, <AR>, <BT>
