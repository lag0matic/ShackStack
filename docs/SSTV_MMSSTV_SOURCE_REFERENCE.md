# MMSSTV Source Reference

This document pins the local MMSSTV source copy used for ShackStack SSTV receive/transmit port work.

## Upstream Copy

- Repository: `https://github.com/n5ac/mmsstv.git`
- Local path: `vendor/mmsstv`
- Commit checked out: `8060b5f1e9727b0052d74108081c6db7b26babad`
- Commit date: `2013-07-05T15:15:14-05:00`
- License files present: `COPYING.txt` (GPLv3) and `COPYING.LESSER.txt` (LGPLv3)

Keep this source as reference material. Do not mix generated test artifacts or local experiments into `vendor/mmsstv`.

## Receive Chain Map

Use these MMSSTV locations when auditing ShackStack's SSTV sidecar:

- `vendor/mmsstv/sstv.cpp`: `CSSTVDEM::Do(double s)` is the main receive pipeline.
- `vendor/mmsstv/sstv.cpp`: `CSSTVDEM::Start()` resets the active receive, buffer pointers, AFC, mode width, and staged receive buffers.
- `vendor/mmsstv/sstv.cpp`: `CSSTVDEM::Start(int mode, int f)` sets the requested mode and either enters immediate receive or leaves sync mode idle.
- `vendor/mmsstv/sstv.cpp`: `CSSTVDEM::IncWP()` advances `m_Buf/m_B12` write pages and wraps by `SSTVDEMBUFMAX`.
- `vendor/mmsstv/sstv.cpp`: `CSSTVDEM::SyncFreq(double d)` contains the AFC sync-lock timing and tone retune behavior.
- `vendor/mmsstv/sstv.cpp`: `CSSTVDEM::DecodeFSK(int m, int s)` handles FSKID and narrow-mode FSK metadata.
- `vendor/mmsstv/Main.cpp`: `TMmsstv::CopyStgBuf()` copies `m_Buf/m_B12` pages into `m_StgBuf/m_StgB12` using exactly `SSTVSET.m_WD` shorts per staged row.
- `vendor/mmsstv/Main.cpp`: `TMmsstv::AutoStopJob()` tracks line-sync position stability and triggers auto-stop once `m_AutoStopCnt >= 8`.
- `vendor/mmsstv/Main.cpp`: `TMmsstv::ReSyncSSTV()` builds its sync histogram from up to 32 staged rows before applying `AdjustSyncPos`.

## Current Port Watchpoints

- Audio ingress: MMSSTV `CSSTVDEM::Do` expects signed PCM-scale input and flags overflow beyond about `24578`; ShackStack normalizes radio audio, then scales into the same shape for the control path.
- Front end: MMSSTV applies a light one-sample LPF, optional FIR BPF, `CLVL` AGC, then multiplies the AGC output by `32` and clamps to `+/-16384` before deriving 1200/1900/FSK tone envelopes.
- BPF switching: MMSSTV uses the wide search BPF while waiting for lead-in/VIS (`m_SyncMode` 0, 1, and 2), then switches to receive BPF only when already synced or `m_SyncMode >= 3`. ShackStack's control path should follow that split instead of tightening the BPF during VIS bit decode.
- BPF state: MMSSTV's `CFIR2::Do(d, HBPFS/HBPF)` switches between precomputed coefficient arrays without clearing the FIR delay line. ShackStack's control BPF should preserve delay history when swapping search/receive coefficients, otherwise the VIS follow-up boundary gets an artificial filter transient.
- VIS flow: MMSSTV's state machine goes `0 -> 1 -> 2 -> 3 -> 256 -> Start()` for standard VIS. Once state `256` is reached, the source starts receive directly; ShackStack should not add a second unrelated multi-line confidence gate after VIS has already been confirmed.
- VIS ownership: ShackStack's separate buffered VIS detector is now opt-in with `SHACKSTACK_SSTV_USE_BUFFERED_VIS=1`. The default path should keep VIS acquisition inside the MMSSTV-shaped demod state machine so the reported VIS mode, follow-up timer, and forced-start transition cannot drift apart.
- VIS follow-up timing: MMSSTV sets the next 30 ms timing window inside the demodulator state machine. ShackStack handlers must not re-call `BeginApplyNextMode` or `BeginExtendedVisDecode` after the fact, because an audio chunk may already have advanced through the follow-up tone. Delayed re-arming can leave RX stuck at "confirming 1200 Hz sync" even when VIS was valid.
- Start sample: MMSSTV `CSSTVDEM::Do` calls `Start()` from inside the current sample's processing path, then writes that same sample into `m_Buf/m_B12`. ShackStack full-RX seeding should therefore use the current sample index for forced/interval starts, not the already-consumed next index.
- AFC reset: MMSSTV `InitAFC()` resets the AFC lock smoother, restores `m_AFCData/m_AFCLock` to the source sync value, clears `m_AFCDiff`, restores guard to `10`, and retunes tones to zero offset. ShackStack's `MmsstvAfcTracker.Reset()` should preserve those semantics instead of zeroing the lock state.
- Receive start reset: MMSSTV `CSSTVDEM::Start()` calls `InitAFC()` before clearing write/read pointers and enabling sync receive. ShackStack should reset the receiver-level AFC/tone bank when entering a started receive mode, not only inside the per-image receive buffer.
- Mode configure/start reset: MMSSTV `CSSTVDEM::Start(int mode, int f)` resets sync intervals, write-begin state, receive base, sync mode, and selected width before waiting or starting. ShackStack should clear idle acquisition buffers/state on configure so a fresh Start RX cannot inherit stale VIS/sync candidates from an earlier worker session.
- Skip semantics: MMSSTV `m_Skip > 0` drops incoming samples before writing `m_Buf/m_B12`; `m_Skip < 0` waits until the next demodulated sample, inserts that current picture value repeatedly with zero sync until it reaches zero, and does not also write the live sample from that same `Do()` call. Normal auto-sync uses a stable-position threshold of `2*m_Mult`; MMSSTV widens it to `5*m_Mult` only while the slant tool (`KRSA`) is active.
- Auto-stop: MMSSTV's `AutoStopJob()` increments `m_AutoStopCnt` when sync positions are unstable or weak and calls `RxAutoPush(TRUE)` once the count reaches 8. ShackStack's image session surfaces that source-shaped stop request to the receiver; the receiver's no-signal timeout remains a practical fallback for incomplete or abruptly-ended RF captures.
- Sync base adjustment: MMSSTV has two related but different paths. Initial receive start uses `TMmsstv::SyncSSTV`, which subtracts `m_OFP`, negates, applies the Scottie wrap special case, and subtracts the Hilbert quarter-tap only for type-2 demod. Later staged redraw/re-sync uses `TMmsstv::AdjustSyncPos`, which adds mode-specific nudges for Martin, Robot, PD, WRASSE, etc. ShackStack keeps those paths split so a newly-acquired live image is not pre-shifted by replay-only corrections.
- Post-receive re-sync: MMSSTV's `ReSyncSSTV()` accumulates sync energy over up to 32 staged rows, not just the minimum rows needed to prove a buffer exists. ShackStack should use the same 32-row replay histogram for post-receive redraw/slant correction while keeping initial live-start acquisition conservative.
- FSKID: MMSSTV accepts a 50 ms space carrier, then looks for the 1900 Hz start mark within a guard window before reading 6-bit symbols. ShackStack's RX decoder should use that guard/start-window shape and trust the XOR checksum rather than adding an unrelated callsign-shape validator.
- Sync interval auto-start: MMSSTV also permits interval-based starts for a limited set of modes, but those are separate from VIS-confirmed starts and should remain stricter to avoid static false starts.
- Staging: MMSSTV stages only page-width rows from the ring buffer. Any ShackStack change around slant, sync adjustment, or image redraw should compare against `CopyStgBuf` before changing page width, line base, or wrap behavior.
- Sample rate: MMSSTV defaults to `11025 Hz`, but ShackStack currently keeps the SSTV sidecar at `12000 Hz` by default. The sidecar and harness can be launched with `SHACKSTACK_SSTV_WORKING_SAMPLE_RATE=11025` for source-fidelity experiments. As of the current regression matrix, both `12000` and `11025` pass 9/9, but `12000` produces better synthetic image correlation after the VIS follow-up timing fix. Keep `12000` as production default until live captures show the native rate is a net improvement.

## ShackStack Counterparts

- `src/ShackStack.DecoderHost.Sstv/Core/NativeSstvReceiver.cs`: receive state orchestration, VIS/auto-start handoff, session lifecycle.
- `src/ShackStack.DecoderHost.Sstv/Core/MmsstvDemodState.cs`: source-shaped `m_SyncMode` state bag and early sync state machine.
- `src/ShackStack.DecoderHost.Sstv/Core/NativeImageSession.cs`: image drawing, staged rows, sync/slant correction.
- `src/ShackStack.DecoderHost.Sstv/Core/MmsstvReceiveBuffer.cs`: source-shaped receive ring for `m_Buf/m_B12`.
- `src/ShackStack.DecoderHost.Sstv/Core/MmsstvModeCatalog.cs`: mode dimensions, VIS codes, scan timing, and decode support flags.

Run the SSTV regression suite after any receive-chain change:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-SstvRegression.ps1 -Configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-SstvRegression.ps1 -Configuration Release -WorkingSampleRate 11025
```
