# ShackStack.DecoderHost.Sstv

This project is the native SSTV sidecar used by ShackStack.

Purpose:
- host the ShackStack-owned SSTV RX/TX engine
- keep the receive path faithful to MMSSTV behavior where practical
- expose the engine through the stdin/stdout worker protocol used by the UI

Current responsibilities:
- automatic RX mode and VIS detection
- line sync, slant, offset, and progressive image decode
- receive archive output and live preview frames
- FSKID decode for callsign handoff into the SSTV desk/logging flow
- SSTV TX PCM generation for the supported ShackStack transmit modes
- MMSSTV-style FSKID transmit metadata

Current operating note:
- SSTV auto-detect and live receive are working well enough that threshold churn should be avoided unless a regression appears.
- The synthetic regression harness should be used before changing detection, sync, or timing behavior.
- Known-good behavior should be compared against MMSSTV source and live captures before inventing new DSP.

Design constraints:
- no Windows VCL shell
- no old WinMM device layer
- sidecar process boundary only
- GPL/MMSSTV-derived behavior must stay documented honestly

Related docs:
- `docs/SSTV_MMSSTV_HARVEST.md`
- `docs/SSTV_MMSSTV_BASELINE_2026-04-24.md`
- `docs/SSTV_REGRESSION_HARNESS.md`
