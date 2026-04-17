# WSJT-X Source Map

This sidecar is intended to become the direct-port weak-signal decoder boundary for ShackStack.

The goal is not "WSJT-like." The goal is "as close to the WSJT-X decode path as practical" while keeping ShackStack's UI/radio control outside the decoder core.

## Current Reference Tree

Local reference checkout used during development:

- `C:\Users\lag0m\Documents\ShackStack.Avalonia\_wsjtx_ref`

Primary FT8 decode sources:

- `lib/ft8/ft8b.f90`
- `lib/ft8/ft8_downsample.f90`
- `lib/ft8/sync8d.f90`
- `lib/ft8/decode174_91.f90`
- `lib/ft8/bpdecode174_91.f90`
- `lib/ft8/chkcrc14a.f90`
- `lib/ft8/get_crc14.f90`
- `lib/ft8/ldpc_174_91_c_parity.f90`
- `lib/ft8/ldpc_174_91_c_colorder.f90`
- `lib/ft8/osd174_91.f90`

## Intended Port Stages

1. Transport boundary
- this project executable
- stdin/stdout JSON contract compatible with ShackStack host

2. FT8 cycle ingest
- fixed 15-second cycle handling
- UTC-aligned cycle processing

3. FT8 lane extraction
- port from `ft8_downsample.f90`
- full-window band extraction to 200 Hz complex stream

4. Sync search / re-peak
- port from `sync8d.f90`
- offset and frequency peaking around Costas sync

5. Symbol metrics
- port the grouped `nsym=1/2/3` bit-metric construction from `ft8b.f90`

6. LDPC decode
- port `bpdecode174_91.f90`
- then port `decode174_91.f90` hybrid BP/OSD flow if needed

7. CRC gate
- port `chkcrc14a.f90` / `get_crc14.f90`

8. Message unpack
- port the FT8 message unpack path after valid CRC survivors

## Boundary Rule

ShackStack should own:

- radio control
- frequency/mode selection
- clock display
- decode list UI
- operator workflow
- future sequencing/TX policy

This sidecar should own:

- FT8/FT4 DSP
- sync search
- symbol metrics
- LDPC/CRC
- message unpack/pack
- later TX waveform generation

## Current State

- JSON worker contract exists
- UTC-aligned FT8 cycle ingest exists
- direct-port support classes now exist for:
  - `ft8_downsample.f90` shape
  - `sync8d.f90` shape
- the sidecar currently emits first-stage FT8 sync candidates from the direct-port path
- current ShackStack Python FT8 work remains fallback scaffolding only
