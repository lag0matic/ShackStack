# ShackStack.DecoderHost.GplFldigiPsk

GPL sidecar for fldigi-derived PSK keyboard modes.

Initial target modes:

- BPSK31 receive
- BPSK63 receive

Later targets:

- BPSK31/BPSK63 transmit
- QPSK modes if needed
- PSK63F / PSKR variants only after the plain BPSK path is trustworthy

This sidecar owns modem DSP and character decoding. ShackStack owns UI, audio routing, radio control, tuning workflow, and operator ergonomics.

## Source Map

Reference source: local fldigi checkout at `.tmp-fldigi-repo`, upstream remote `https://github.com/w1hkj/fldigi.git`.

Primary files:

- `src/psk/psk.cxx`: main PSK modem, RX/TX, NCO, FIR/downsample, symbol timing, phase decode, AFC.
- `src/include/psk.h`: PSK modem state and method layout.
- `src/psk/pskvaricode.cxx`: PSK31 Varicode tables and encode/decode helpers.
- `src/include/pskvaricode.h`: Varicode interface.
- `src/psk/pskcoeff.cxx` and `src/include/pskcoeff.h`: PSK filter coefficients.
- `src/psk/pskeval.cxx` and `src/include/pskeval.h`: signal evaluation helpers.

Porting rule: keep the receive chain shaped like fldigi. Avoid invented demodulator shortcuts unless they are explicitly marked as temporary harness code.

## Current Port State

The sidecar now contains the first BPSK receive slice:

- 8 kHz working sample rate, matching fldigi's BPSK31/BPSK63 path.
- BPSK31 `symbollen = 256` and BPSK63 `symbollen = 128`.
- fldigi PSK-core FIR/downsample chain from `psk::rx_process` and `pskcoeff.cxx`.
- fldigi-style 16-sample symbol timing recovery using the matched-filter magnitude buffer.
- fldigi-shaped differential symbol decision from `psk::rx_symbol`.
- fldigi-shaped BPSK DCD gate using the preamble/postamble shift-register patterns and phase-quality metric.
- fldigi-shaped phase AFC from `phaseafc`, bounded to +/-25 Hz around the operator-selected audio center.
- PSK Varicode decode table from `pskvaricode.cxx`.

Synthetic validation note:

- BPSK preamble must match fldigi `tx_symbol(0)`: continuous phase reversals.
- BPSK postamble must match fldigi `tx_symbol(2)`: steady phase.
- With that source-faithful framing, the sidecar decodes `CQ W8STR` and closes DCD on postamble.
- A +4 Hz synthetic carrier offset decodes and tracks to approximately +4 Hz via the phase-AFC path.

Next fidelity step:

- Wire the sidecar into ShackStack's keyboard-mode desk once live audio confidence is acceptable.
- Add an operator-visible tune helper that can apply `trackedAudioCenterHz` back to the receive control.
