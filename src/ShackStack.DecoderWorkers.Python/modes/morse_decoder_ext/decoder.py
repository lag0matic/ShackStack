"""
decoder.py — MorseDecoder: the single public façade for host applications.

Wires together DSPPipeline → TimingEngine → Assembler into one object
with a minimal, stable API.  Host applications should only need to import
this module.

Typical usage
-------------
    from morse_decoder import MorseDecoder, MorseConfig

    cfg = MorseConfig(sample_rate=8000, tone_freq=750, if_bandwidth=500)
    dec = MorseDecoder(cfg)

    # Register callbacks
    dec.on_character = lambda ch, conf: print(f"{ch} ({conf:.2f})")
    dec.on_word      = lambda w,  conf: print(f"WORD: {w}")

    # Feed raw PCM chunks (float32, -1.0 … +1.0)
    for chunk in audio_source:
        dec.feed(chunk)

    # At end of stream
    leftover = dec.flush()

Threading note
--------------
    MorseDecoder is NOT thread-safe.  If audio arrives on a separate thread,
    protect calls to .feed() with a threading.Lock() in the host application.
"""

from __future__ import annotations

from typing import Callable, Iterator, List, Optional

import numpy as np

from .assembler import Assembler, DecodeResult
from .config import MorseConfig
from .dsp import DSPPipeline, KeyEvent
from .timing import MorseElement, TimingEngine


class MorseDecoder:
    """
    Complete Morse code decoder pipeline.

    Parameters
    ----------
    config : MorseConfig, optional
        Pass a MorseConfig to override any default.  You can also mutate
        config attributes between calls to feed() — most will take effect
        on the next chunk.

    Callbacks
    ---------
    on_character(char: str, confidence: float)
        Fired for every decoded character.  confidence is 1.0 for an exact
        Morse match, lower for fuzzy/corrected matches.

    on_word(word: str, mean_confidence: float)
        Fired at word boundaries.

    on_element(element: MorseElement)
        Low-level: fired for every DIT / DAH / GAP element.  Useful for
        building a CW monitor display.

    on_key_event(event: KeyEvent)
        Very low-level: fired for every KEY_UP / KEY_DOWN transition from
        the DSP stage.  Useful for oscilloscope-style views.

    on_result(result: DecodeResult)
        Like on_character but includes the raw Morse sequence and WPM.
    """

    def __init__(self, config: Optional[MorseConfig] = None):
        self.config = config or MorseConfig()
        self._dsp     = DSPPipeline(self.config)
        self._timing  = TimingEngine(self.config)
        self._asm     = Assembler(self.config, wpm_source=self._timing)

        # ── Public callbacks ────────────────────────────────────────────
        self.on_character:  Optional[Callable[[str, float], None]]      = None
        self.on_word:       Optional[Callable[[str, float], None]]      = None
        self.on_element:    Optional[Callable[[MorseElement], None]]    = None
        self.on_key_event:  Optional[Callable[[KeyEvent], None]]        = None
        self.on_result:     Optional[Callable[[DecodeResult], None]]    = None

        self._wire_callbacks()

    # ── Primary interface ─────────────────────────────────────────────────

    def feed(self, pcm: np.ndarray) -> None:
        """
        Feed a chunk of audio samples.

        Parameters
        ----------
        pcm : np.ndarray
            1-D float32 array, values in −1.0 … +1.0.
            Sample rate must match config.sample_rate.
            Any chunk size works, but ≥ 256 samples gives better Hilbert
            accuracy.  Chunks of 512 or 1024 are recommended.
        """
        key_events: List[KeyEvent] = self._dsp.process(pcm)
        for kev in key_events:
            if self.on_key_event:
                self.on_key_event(kev)
            elements: List[MorseElement] = self._timing.feed(kev)
            for el in elements:
                self._asm.feed(el)

    def flush(self) -> str:
        """
        Flush any buffered state and return remaining decoded text.
        Call at end of stream or after a long silence.
        """
        return self._asm.flush()

    def reset(self) -> None:
        """
        Reset all internal state.  Call when retuning to a new frequency
        or starting a new decoding session.
        """
        self._dsp.reset()
        self._timing.reset()
        # Re-create assembler to clear word buffer
        self._asm = Assembler(self.config, wpm_source=self._timing)
        self._wire_callbacks()

    # ── Convenience properties ────────────────────────────────────────────

    @property
    def current_wpm(self) -> float:
        """Estimated WPM based on recent element timing."""
        return self._timing.current_wpm

    @property
    def dit_duration_ms(self) -> float:
        """Estimated dit duration in milliseconds."""
        return self._timing.dit_duration * 1000.0

    @property
    def dsp(self) -> DSPPipeline:
        """Direct access to the DSP stage (for advanced use)."""
        return self._dsp

    @property
    def timing(self) -> TimingEngine:
        """Direct access to the timing stage (for advanced use)."""
        return self._timing

    @property
    def assembler(self) -> Assembler:
        """Direct access to the assembler stage (for advanced use)."""
        return self._asm

    # ── Convenience: iterator / generator interface ───────────────────────

    def decode_iter(
        self, audio_chunks: Iterator[np.ndarray]
    ) -> Iterator[DecodeResult]:
        """
        Generator-based interface — useful when you want decoded results as
        an iterable rather than via callbacks.

        Example
        -------
            for result in decoder.decode_iter(audio_source):
                print(result.character, result.confidence)
        """
        results: List[DecodeResult] = []

        original = self.on_result
        self.on_result = results.append
        self._wire_callbacks()

        for chunk in audio_chunks:
            self.feed(chunk)
            yield from results
            results.clear()

        # Flush
        self.flush()
        yield from results
        results.clear()

        self.on_result = original
        self._wire_callbacks()

    # ── Convenience: decode a complete numpy array in one call ────────────

    def decode_array(
        self, audio: np.ndarray, chunk_size: int = 1024
    ) -> List[DecodeResult]:
        """
        Decode a complete audio array and return all DecodeResult objects.

        Useful for offline / file decoding.  Internally splits into chunks
        of chunk_size so the streaming pipeline behaves correctly.
        """
        results: List[DecodeResult] = []
        original = self.on_result
        self.on_result = results.append
        self._wire_callbacks()

        for start in range(0, len(audio), chunk_size):
            self.feed(audio[start : start + chunk_size])
        self.flush()

        self.on_result = original
        self._wire_callbacks()
        return results

    # ── Internal ──────────────────────────────────────────────────────────

    def _wire_callbacks(self) -> None:
        """Propagate our public callbacks into sub-stages."""
        self._asm.on_character = self.on_character
        self._asm.on_word      = self.on_word
        self._asm.on_element   = self.on_element
        self._asm.on_result    = self.on_result
        # on_key_event is handled in feed() directly
