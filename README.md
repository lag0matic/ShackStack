# ShackStack 1.0

ShackStack is a Windows desktop ham-radio operating app built with C# and Avalonia. It is designed around one real shack workflow: radio control, audio routing, decode desks, transmit helpers, image modes, logging, and enough operator ergonomics to make the computer feel like part of the station instead of another fight.

This project was built with substantial AI assistance and a lot of direct reference-porting from proven open-source amateur-radio tools. It is useful and increasingly capable, but it is still a hobby shack application, not certified radio-control software. Expect bugs, rough edges, and the occasional gremlin.

## 1.0 Snapshot

ShackStack 1.0 is the first "daily-driver" release. It packages the app, bundled decoder workers, native sidecars, GPL sidecars, JS8/WSJT runtime tools, and the current desktop UI into a self-contained `win-x64` Inno Setup installer.

The 1.0 release is published from `main`. Older cleanup and beta branches remain only as historical staging branches.

## What Works Today

- Icom CI-V radio control: connect/disconnect, frequency, mode, filters, VFO A/B, `A=B`, split, tuner, preamp/attenuator, noise controls, RF power, voice gain, and PTT.
- Audio routing: receive audio, transmit PCM/audio, monitoring, device selection, level display, and persisted audio/display settings.
- Desk-based UI: Voice, CW, RTTY, Weak Signal, JS8, WeFAX, SSTV, and Longwave each have their own operator desk.
- Voice operating: radio-backed voice controls, PTT, POTA-oriented workflow, and log staging.
- Weak-signal FT8/FT4: live decode, signal generation, on-air CQ/reply flow, RX/TX audio offset controls, QSO staging, SNTP-assisted clock discipline, and bundled WSJT-X-derived sidecar/runtime support.
- JS8: live JS8 receive and transmit for Normal/Fast/Turbo/Slow, heartbeat support including `@HB`, directed replies, audio offset control, and bundled JS8Call-compatible runtime tools.
- SSTV receive: MMSSTV-shaped receive path with live preview, VIS/sync handling, common ham mode support, slant correction, archive saving, FSKID callsign capture, and graceful image completion when signal drops.
- SSTV transmit: template overlays, draggable text blocks, received-image thumbnail replies, generated PCM transmit clips, optional CW ID, optional MMSSTV-style FSKID, and `%tocall` replacement from decoded FSKID.
- WeFAX receive: live image receive, dedicated desk, schedule view, manual slant/offset controls, fldigi/py-wefax-inspired alignment and cleanup controls, archive review, and improved live stability.
- RTTY receive: fldigi-derived GPL sidecar, manual audio-center tuning, reverse polarity, 45.45 baud / 170 Hz defaults, USB-D/LSB-D friendly radio handling, and practical decode quality against live signals.
- Longwave integration: POTA spots, callsign lookup/staging, logbook selection, manual QSO logging, and recent-contact sync from another Longwave client or machine.
- Packaging: self-contained Windows installer, bundled decoder workers, bundled native SSTV sidecar, bundled GPL RTTY/WSJT sidecars, app icon branding, and shutdown cleanup for decoder processes and CI-V COM ports.

## Functional Mode Coverage

| Area | Current 1.0 state |
| --- | --- |
| Voice | Functional for local station operation and logging workflow. |
| FT8 | Functional RX/TX with WSJT-X-derived decode and generated transmit audio. |
| FT4 | Functional RX/TX with WSJT-X-derived decode and generated transmit audio. |
| JS8 | Functional RX/TX for normal JS8 text/heartbeat workflows. |
| RTTY | Functional RX using fldigi-derived receive logic; hand tuning is expected. |
| SSTV | Functional RX/TX for common ham modes; strongest focus is Martin, Scottie, Robot, and PD families. |
| WeFAX | Functional RX only; no WeFAX transmit is planned. |
| CW | CW TX/keying exists; CW RX is experimental and not yet something to brag about. |
| Longwave | Functional integration for spots, logbook-aware logging, and recent contact sync. |
| POTA | Functional hunting/staging workflow through the Voice/Longwave path. |

## In Progress / Rough Edges

- CW decode remains experimental. The Python adaptive path is still the practical default; the `ggmorse` native bridge exists as a work-in-progress path but is not the dependable default.
- FT8/FT4 auto-sequencing and auto-logging should still be watched carefully during real QSOs.
- RTTY decode depends heavily on signal quality, polarity, passband placement, shift, and baud. Manual tuning is part of the workflow.
- JS8 TX supports the current short Varicode/Huffman text path and live heartbeat/reply use. Richer JS8Call message packing can still be expanded.
- WSPR is present as a weak-signal monitor target and frequency preset set, but WSPR TX/QSO automation is not part of 1.0.
- Q65, FST4, FST4W, JT65, JT9, JT4, and MSK144 are scaffolded as weak-signal monitor modes, but they are not 1.0 headline-tested operator workflows.
- SSTV AVT exists in the native workbench/harness path but is not a priority ham workflow for 1.0.
- Installer/update flow is simple: self-contained `win-x64` publish wrapped by Inno Setup. No auto-updater yet.
- Code signing is not solved yet, so Windows SmartScreen may complain about downloaded installers.

## Known Inops / Not Included

- VARA HF / VarAC integration is not implemented.
- MMTTY is not embedded; ShackStack's RTTY path is its own fldigi-derived receive sidecar.
- WeFAX transmit is intentionally not implemented.
- Full CW receive comparable to a skilled CW decoder is not solved.
- General-purpose rig support is not the goal yet; the app is currently shaped around Icom CI-V and the IC-7300-style operating path.
- This is not a contest logger, LoTW/eQSL manager, or complete shack automation suite.

## Build

Prerequisites:

- Windows x64
- .NET SDK 9
- Python 3.12 with PyInstaller and the Python packages used by the decoder workers
- Inno Setup 6

Build the workers and app:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-DecoderWorkers.ps1
dotnet build .\ShackStack.sln
```

Publish the self-contained app folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-DecoderWorkers.ps1
dotnet publish .\src\ShackStack.Desktop\ShackStack.Desktop.csproj -c Release -r win-x64 --self-contained true -o .\publish\ShackStack-win-x64-v1.0
```

Build the installer:

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" ".\installer\ShackStack.iss"
```

Expected output:

```text
publish\ShackStack-Setup-v1.0.exe
```

## Repository Layout

- `src/ShackStack.Desktop`: desktop entry point, DI bootstrap, app assets, and packaged decoder/runtime folders.
- `src/ShackStack.UI`: Avalonia views, desks, controls, and main view model.
- `src/ShackStack.Core`: app workflow helpers and core operating logic.
- `src/ShackStack.Core.Abstractions`: shared contracts, models, mode catalogs, and service interfaces.
- `src/ShackStack.Infrastructure.Audio`: NAudio-backed receive/transmit audio transport.
- `src/ShackStack.Infrastructure.Radio`: Icom CI-V radio control and serial session management.
- `src/ShackStack.Infrastructure.Decoders`: host wrappers for Python, GPL, and native decoder workers.
- `src/ShackStack.Infrastructure.Interop`: fake FLRig/HAMRS-style interop, Longwave integration, and band-condition services.
- `src/ShackStack.DecoderHost.GplWsjtx`: GPL weak-signal sidecar for WSJT-X/JS8-style decode and signal-generation boundaries.
- `src/ShackStack.DecoderHost.GplFldigiRtty`: GPL RTTY sidecar using fldigi-derived receive logic.
- `src/ShackStack.DecoderHost.Sstv`: native SSTV sidecar with MMSSTV-shaped RX/TX implementation.
- `src/ShackStack.DecoderWorkers.Python`: remaining Python worker sources for CW, WeFAX, and legacy/support workers.
- `vendor/ggmorse`: vendored ggmorse source used by the experimental native CW path.
- `installer`: Inno Setup packaging and worker-build scripts.
- `docs`: release notes, deployment notes, and implementation/porting notes.

## Licensing And Borrowed Code

ShackStack 1.0 includes original code, direct ports, derived logic, bundled binaries, and reference-shaped implementations from multiple open-source amateur-radio projects. Because several of these are GPL-family components, treat the 1.0 source distribution as GPL-compatible work and preserve upstream notices when redistributing.

Important upstream/license areas:

- WSJT-X-derived weak-signal logic and bundled WSJT runtime tools are GPL-family work. The GPL sidecar boundary exists to keep that ownership explicit, not to hide the obligation.
- JS8/JS8Call-compatible runtime/tooling and protocol work are GPL-family work.
- fldigi-derived RTTY receive logic is GPL-family work.
- MMSSTV-derived SSTV receive/transmit behavior is GPL/LGPL-family work based on the upstream MMSSTV source and notes.
- `py_wefax` inspired portions of the WeFAX receive DSP path; keep attribution to the upstream project when distributing source or binaries.
- `ggmorse` is MIT licensed. Its copyright/license notice must remain with vendored or redistributed copies.
- Avalonia, NAudio, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, SkiaSharp, and other NuGet/runtime dependencies retain their own upstream licenses.
- Bundled Python worker environments include third-party Python packages; their licenses must be preserved in binary distributions.
- Bundled WSJT/JS8 runtime folders may include Qt, FFTW, Hamlib, PortAudio, Boost, libusb, MinGW runtime libraries, and related dependencies; keep their license files/notices with redistributed binaries.

Practical release rule: if you ship a ShackStack installer, ship corresponding source and license notices for the GPL/LGPL-derived parts and do not remove upstream copyright/license text. A formal top-level license/notice audit is still recommended before broad public distribution.

## Data Locations

Operational data is kept outside the install directory:

- user settings and app state live under the user's application data folders
- received/transmitted images and radio artifacts live under the user's `ShackStack` folders
- publish/install output stays under `publish` and is excluded from source control

## Version

Current release package: `1.0`

Current installer name: `ShackStack-Setup-v1.0.exe`

## Notes

This repo is intentionally app-focused. Scratch captures, local publish artifacts, giant experimental branches, and one-off test outputs should stay out of GitHub. The goal for 1.0 is a useful, honest, reproducible shack app: powerful where it works, clearly labeled where it is still rough, and respectful of the open-source radio projects that made the direct-port path possible.
