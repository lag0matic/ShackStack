"""
symbols.py — Complete ITU Morse code table plus fuzzy lookup.

The fuzzy lookup is used when the decoder receives a sequence that doesn't
match exactly — e.g. a dit accidentally keyed as a dah on a hand-key.
We use Hamming distance over the dit/dah string, bounded by max_errors.
"""

from __future__ import annotations
from typing import Optional

# ── ITU-R M.1677-1 standard table ─────────────────────────────────────────
# Key: dit/dah string ('.' / '-')   Value: decoded character or prosign label

MORSE_TABLE: dict[str, str] = {
    # Letters
    ".-":    "A",  "-...":  "B",  "-.-.":  "C",  "-..":   "D",
    ".":     "E",  "..-.":  "F",  "--.":   "G",  "....":  "H",
    "..":    "I",  ".---":  "J",  "-.-":   "K",  ".-..":  "L",
    "--":    "M",  "-.":    "N",  "---":   "O",  ".--.":  "P",
    "--.-":  "Q",  ".-.":   "R",  "...":   "S",  "-":     "T",
    "..-":   "U",  "...-":  "V",  ".--":   "W",  "-..-":  "X",
    "-.--":  "Y",  "--..":  "Z",
    # Digits
    ".----": "1",  "..---": "2",  "...--": "3",  "....-": "4",
    ".....": "5",  "-....": "6",  "--...": "7",  "---..": "8",
    "----.": "9",  "-----": "0",
    # Punctuation
    ".-.-.-": ".",  "--..--": ",",  "..--..": "?",  ".----.": "'",
    "-.-.--": "!",  "-..-.":  "/",  "-.--.":  "(",  "-.--.-": ")",
    ".-...":  "&",  "---...": ":",  "-.-.-.": ";",  "-...-":  "=",
    ".-.-.":  "+",  "-....-": "-",  "..--.-": "_",  ".-..-.": '"',
    "...-..-": "$", ".--.-.": "@",
    # Prosigns (sent as single unbroken sequences)
    "-.-.-":   "<KA>",   # Start of transmission
    ".-.-..":  "<AR>",   # End of message  (sometimes .-.-. but ITU is .-.-..)
    ".-.-.":   "<AR>",   # common variant
    "...-.-":  "<SK>",   # End of contact (go silent)
    "-...-":   "<BT>",   # Break / paragraph
    "-.--." :  "<KN>",   # Go ahead, specific station only
    "...---...": "<SOS>",
    "-.-":     "<K>",    # Invitation to transmit (also plain K)
    "...-.":   "<SN>",   # Understood
    "........": "<HH>",  # Error / correction
}

# Build reverse lookup (character → code) for informational use
REVERSE_TABLE: dict[str, str] = {v: k for k, v in MORSE_TABLE.items()
                                  if not v.startswith("<") or v in ("<AR>", "<SK>", "<BT>", "<KN>")}


def _hamming(a: str, b: str) -> int:
    """Hamming distance between two strings of equal length."""
    return sum(x != y for x, y in zip(a, b))


def decode_sequence(seq: str, max_errors: int = 1) -> tuple[str, float]:
    """
    Decode a dit/dah sequence (e.g. '.-..') to a character.

    Returns
    -------
    (character, confidence)
        confidence = 1.0 for exact match, < 1.0 for fuzzy match,
        0.0 if nothing found within max_errors.
    '?' is returned as character when nothing matches and emit_unknown is True.
    """
    if not seq:
        return ("", 0.0)

    # Exact match — fast path
    if seq in MORSE_TABLE:
        return (MORSE_TABLE[seq], 1.0)

    # Fuzzy match — allow substitutions (dit↔dah) up to max_errors
    # Only compare against same-length codes to avoid wild mismatches
    best_char  = "?"
    best_dist  = max_errors + 1
    best_score = 0.0

    for code, char in MORSE_TABLE.items():
        if len(code) != len(seq):
            continue
        d = _hamming(code, seq)
        if d < best_dist:
            best_dist  = d
            best_char  = char
            # confidence degrades with each error
            best_score = 1.0 - (d / len(seq))

    if best_dist <= max_errors:
        return (best_char, best_score)

    # Nothing in same length — try ±1 length (insertion/deletion on hand key)
    for code, char in MORSE_TABLE.items():
        if abs(len(code) - len(seq)) != 1:
            continue
        # Use simple edit distance for ±1 length (one insert or delete)
        d = _edit_distance_bounded(seq, code, max_errors)
        if d <= max_errors and d < best_dist:
            best_dist  = d
            best_char  = char
            best_score = 1.0 - (d / max(len(seq), len(code)))

    if best_dist <= max_errors:
        return (best_char, best_score * 0.8)   # slight penalty for length mismatch

    return ("?", 0.0)


def _edit_distance_bounded(a: str, b: str, bound: int) -> int:
    """
    Standard Levenshtein, but returns bound+1 early if distance exceeds bound.
    Only handles |len(a)-len(b)| == 1 for performance.
    """
    if abs(len(a) - len(b)) > bound:
        return bound + 1
    # Short sequences — full DP is fine (max code length ≈ 9)
    m, n = len(a), len(b)
    dp = list(range(n + 1))
    for i in range(1, m + 1):
        prev, dp[0] = dp[0], i
        for j in range(1, n + 1):
            temp = dp[j]
            if a[i - 1] == b[j - 1]:
                dp[j] = prev
            else:
                dp[j] = 1 + min(prev, dp[j], dp[j - 1])
            prev = temp
        if min(dp) > bound:
            return bound + 1
    return dp[n]
