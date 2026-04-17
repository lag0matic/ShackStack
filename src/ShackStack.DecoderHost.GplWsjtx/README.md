# ShackStack.DecoderHost.GplWsjtx

This folder is the planned home for a dedicated weak-signal digital sidecar based on WSJT-X-derived decode logic.

Why this exists:

- ShackStack should stay responsible for UI, radio control, session workflow, and operator experience.
- The FT8/FT4/Q65/JT/WSPR decoder core is a solved problem with GPL-licensed reference code.
- Keeping the decoder core in a separate sidecar process gives us a cleaner boundary for licensing, packaging, and future replacement.

Intended shape:

- separate executable process
- launched by ShackStack through the existing worker protocol
- stdin/stdout JSON transport, matching the current `wsjtx_sidecar_worker` contract
- RX first
- TX left open for later through the same sidecar boundary

Expected messages:

- `configure`
- `start`
- `stop`
- `reset`
- `audio`
- `shutdown`

Expected outputs:

- `telemetry`
- `decode`

Resolution order in the app:

1. external executable specified by environment variable:
   - `SHACKSTACK_WSJTX_GPL_SIDECAR_PATH`
2. bundled `wsjtx_gpl_sidecar` executable in `DecoderWorkers`
3. fallback `wsjtx_sidecar_worker` implementation

Notes:

- This folder is deliberately documentation-first for now.
- The current Python `wsjtx_sidecar_worker` remains the fallback development worker.
- When the GPL sidecar lands, ShackStack should not need major UI or host changes to start using it.
