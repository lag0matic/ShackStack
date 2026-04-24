"""
assembler.py — Character and word assembler.

Buffers MorseElements from the timing engine and emits decoded characters
and words.

This is the final logic stage before output.  It:
  1. Accumulates DIT/DAH elements until a CHAR_GAP or WORD_GAP is seen.
  2. Calls the symbol decoder (with optional fuzzy matching).
  3. Fires callback(s) so the host application receives text in real time.
  4. Optionally buffers a complete word before emitting.

Callbacks
---------
  on_character(char: str, confidence: float)
      Fired after each character gap (or word gap).
  on_word(word: str, mean_confidence: float)
      Fired after each word gap.  word is the assembled string so far.
  on_element(element: MorseElement)
      Optional low-level hook for visualisation / debugging.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Callable, List, Optional

from .config import MorseConfig
from .symbols import decode_sequence
from .timing import ElementType, MorseElement


DecodedCallback = Callable[[str, float], None]


@dataclass
class DecodeResult:
    """A fully decoded character with metadata."""
    character:   str
    morse_seq:   str           # the raw dit/dah string that was decoded
    confidence:  float         # 1.0 = exact match, < 1.0 = fuzzy
    wpm_at_decode: float = 0.0


class Assembler:
    """
    Stateful character assembler.

    Usage
    -----
        def got_char(ch, conf):
            print(ch, conf)

        asm = Assembler(config, wpm_source=timing_engine)
        asm.on_character = got_char
        for el in timing_engine.feed(event):
            asm.feed(el)
    """

    def __init__(
        self,
        cfg: MorseConfig,
        wpm_source=None,   # object with .current_wpm property (TimingEngine)
    ):
        self._cfg        = cfg
        self._wpm_source = wpm_source
        self._buf: List[str] = []          # current character's DIT/DAH string
        self._word_chars: List[DecodeResult] = []

        # ── Callbacks (assign directly) ────────────────────────────────────
        self.on_character: Optional[DecodedCallback] = None
        """Called with (character, confidence) after each decoded character."""

        self.on_word: Optional[Callable[[str, float], None]] = None
        """Called with (word_string, mean_confidence) after each word gap."""

        self.on_element: Optional[Callable[[MorseElement], None]] = None
        """Optional low-level element hook."""

        self.on_result: Optional[Callable[[DecodeResult], None]] = None
        """Called with full DecodeResult after each character."""

    # ── Public API ────────────────────────────────────────────────────────

    def feed(self, element: MorseElement) -> None:
        """Process one MorseElement."""
        if self.on_element:
            self.on_element(element)

        kind = element.kind

        if kind == ElementType.DIT:
            self._buf.append(".")
        elif kind == ElementType.DAH:
            self._buf.append("-")
        elif kind == ElementType.INTRA_GAP:
            pass   # just the space between dits/dahs — no action needed
        elif kind == ElementType.CHAR_GAP:
            self._flush_character()
        elif kind == ElementType.WORD_GAP:
            self._flush_character()
            self._flush_word()

    def flush(self) -> str:
        """
        Force-decode whatever is buffered (call at end of stream).
        Returns the decoded text.
        """
        self._flush_character()
        out = "".join(r.character for r in self._word_chars)
        self._flush_word()
        return out

    @property
    def buffered_sequence(self) -> str:
        """The currently accumulating dit/dah sequence (for display)."""
        return "".join(self._buf)

    # ── Internal ──────────────────────────────────────────────────────────

    def _flush_character(self) -> None:
        if not self._buf:
            return
        seq = "".join(self._buf)
        self._buf.clear()

        char, conf = decode_sequence(seq, self._cfg.max_decode_errors)

        # Filter unknown characters if configured
        if char == "?" and not self._cfg.emit_unknown_char:
            return
        # Filter prosigns if configured
        if char.startswith("<") and not self._cfg.emit_prosigns:
            return

        wpm = self._wpm_source.current_wpm if self._wpm_source else 0.0
        result = DecodeResult(
            character=char,
            morse_seq=seq,
            confidence=conf,
            wpm_at_decode=wpm,
        )
        self._word_chars.append(result)

        if self.on_character:
            self.on_character(char, conf)
        if self.on_result:
            self.on_result(result)

    def _flush_word(self) -> None:
        if not self._word_chars:
            return
        word     = "".join(r.character for r in self._word_chars)
        mean_conf = sum(r.confidence for r in self._word_chars) / len(self._word_chars)
        self._word_chars.clear()

        if self.on_word:
            self.on_word(word, mean_conf)
