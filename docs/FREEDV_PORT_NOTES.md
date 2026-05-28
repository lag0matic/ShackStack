# FreeDV / Codec2 Integration Notes

ShackStack should treat FreeDV as a Codec2-backed digital voice workflow, not as a hand-written decoder.

## Source Of Truth

- Upstream source: https://github.com/drowe67/codec2
- License: LGPL 2.1 for Codec2/FreeDV API code.
- RADEV1 upstream source: `radae_nopy` / `librade` from the FreeDV RADE path.
- RADEV1 license: BSD-style license in `rade_api.h` / RADE source headers.
- Key files reviewed:
  - `README_freedv.md`
  - `src/freedv_api.h`
  - `demo/freedv_700d_rx.c`
  - `demo/freedv_700d_tx.c`
  - `rade_api.h`
  - `rade_demod_wav.c`
  - `rade_modulate_wav.c`
  - `radae_tx_nopy.c`
  - `rade_callsign_test.c`

## API Shape

The upstream FreeDV API model is:

```text
speech PCM -> freedv_tx -> modem PCM -> HF SSB radio -> freedv_rx -> speech PCM
```

For RADEV1, the upstream path is:

```text
speech PCM -> lpcnet_demo -features -> rade_tx -> real modem audio -> HF SSB radio
real modem audio as complex(real=pcm, imag=0) -> rade_rx -> lpcnet_demo -fargan-synthesis -> speech PCM
```

RADE end-of-over metadata is carried with `rade_tx_set_eoo_callsign`,
`rade_tx_eoo`, and `rade_rx_get_eoo_callsign`.

Both speech and modem samples are signed 16-bit PCM. The API exposes per-mode buffer sizes and rates:

- `freedv_get_speech_sample_rate`
- `freedv_get_modem_sample_rate`
- `freedv_get_n_speech_samples`
- `freedv_get_n_max_speech_samples`
- `freedv_get_n_nom_modem_samples`
- `freedv_nin`

## ShackStack Boundary

Current first pass adds:

- `IFreedvDigitalVoiceHost`
- FreeDV telemetry/configuration models
- `Codec2FreedvDigitalVoiceHost`
- `ShackStack.DecoderHost.GplCodec2Freedv`
- FreeDV desk UI and main-window launch pad
- RADEV1 RX and TX bridge through `librade.dll` plus `lpcnet_demo.exe`
- RADEV1 end-of-over callsign telemetry
- Persistent `lpcnet_demo.exe` feature/synthesis helpers so LPCNet/FARGAN state survives across frames

The sidecar looks for Codec2 via:

- `SHACKSTACK_FREEDV_CODEC2_PATH`
- `codec2.dll` or `libcodec2.dll` beside the sidecar
- `codec2\codec2.dll` or `codec2\libcodec2.dll` beside the sidecar

The sidecar looks for RADEV1 via:

- `SHACKSTACK_FREEDV_RADE_PATH`
- `librade.dll` beside the sidecar
- `rade\librade.dll` beside the sidecar

`lpcnet_demo.exe` must sit beside `librade.dll`.

## Native RADE Build

`tools/Build-FreeDvRadeNative.ps1` builds the RADE runtime from `radae_nopy`
under `.tmp-freedv-native`. The script keeps the port source-true while applying
two Windows integration patches:

- Build Opus through MSYS2/MinGW so the upstream configure/make flow works from
  PowerShell.
- Flush `lpcnet_demo.exe` stdout after each feature/speech frame. Upstream batch
  file mode is fine, but ShackStack keeps the process alive and streams frames
  through stdin/stdout to preserve vocoder state.

`installer/Build-DecoderWorkers.ps1` calls that script with `-SkipIfBuilt` and
prefers `.tmp-freedv-native\radae_nopy-build-mingw\src` when packaging
`librade.dll` and `lpcnet_demo.exe`.

## Verified Harnesses

- `tools/Run-FreeDvRadeHarness.ps1`: RADEV1 receive from known upstream/sample WAVs.
- `tools/Run-FreeDvRadeCallsignHarness.ps1`: upstream RADE EOO callsign round-trip, currently `6/6 PASSED`.
- `tools/Run-FreeDvRadeTxLoopbackHarness.ps1`: ShackStack RADEV1 TX modem output loops back through ShackStack RADEV1 RX, decodes speech, syncs, and recovers the EOO callsign.
- `Recording (17).wav` RADEV1 round-trip produced
  `C:\Users\lag0m\ShackStack\freedv-debug\roundtrip_radev1_decoded_from_recording17_streaming_native.wav`
  with the persistent native helper path.

## Next Port Steps

1. Validate RADEV1 RX/TX on live RF with real FreeDV stations.
2. If persistent process streaming still leaves artifacts on clean samples, port the LPCNet/FARGAN calls directly into a native bridge DLL instead of using `lpcnet_demo.exe`.
3. Add clearer operator UI for RADEV1 callsign/EOO events once live behavior is proven.
4. Keep legacy HF modes practical: `700D`, `700E`, `700C`, `1600`; defer LPCNet-heavy `2020/2020B` until the RADEV1 path is stable.
