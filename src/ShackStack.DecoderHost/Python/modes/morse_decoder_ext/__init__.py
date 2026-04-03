"""
morse_decoder — Real-world CW decoder for ham radio audio.

Public API
----------
    MorseDecoder    — main decoder object (feed PCM, receive text)
    MorseConfig     — all tunable parameters
    DecodeResult    — per-character decode output (char, confidence, WPM, …)
    MorseElement    — timing-stage element (DIT/DAH/GAP)
    ElementType     — enum for element kinds
    KeyEvent        — DSP-stage key transition
    KeyState        — enum KEY_UP / KEY_DOWN
    MORSE_TABLE     — dict[code_string, character]
    decode_sequence — decode a dit/dah string to (char, confidence)
"""

from .config   import MorseConfig
from .decoder  import MorseDecoder
from .assembler import DecodeResult
from .timing   import MorseElement, ElementType
from .dsp      import KeyEvent, KeyState
from .symbols  import MORSE_TABLE, decode_sequence

__all__ = [
    "MorseDecoder",
    "MorseConfig",
    "DecodeResult",
    "MorseElement",
    "ElementType",
    "KeyEvent",
    "KeyState",
    "MORSE_TABLE",
    "decode_sequence",
]

__version__ = "1.0.0"
