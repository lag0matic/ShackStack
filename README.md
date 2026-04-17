# ShackStack

ShackStack is a Windows desktop ham radio application built with C# and Avalonia.
This was written with the assistance of ChatGPT, Claude, and Other AI tools - I am not a professional coder, and there will be bugs and rough edges.

This clean release-candidate branch contains the operator-facing app code for the `V-0.9.0 RC` release. It is intended to be the GitHub-ready application repo, separate from the larger local development workspace and decoder experimentation harnesses.

## Current Release Candidate Scope

- direct Icom CI-V radio control
- VFO A / VFO B, `A/B`, `A=B`, and split operation
- waterfall, spectrum, S-meter, monitor audio, and persisted operator display/audio settings
- voice controls and rig-backed transmit settings
- fake FLRig compatibility for HAMRS-style interop
- SSTV receive workflow with live preview and pop-out desk/archive
- WeFAX receive workflow with live preview and pop-out desk/archive
- decoder host plumbing for CW, RTTY, SSTV, and WeFAX
- FT8 and FT4 receive/transmit workflow using WSJT-X-derived tooling and sidecars
- weak-signal desk with RX/TX offset control, auto-sequence helpers, and QSO staging
- Longwave integration for:
  - POTA spots
  - manual QSO logging
  - logbook selection and management
  - recent contact sync from another Longwave client or machine

## What Works Well Today

- voice operating with radio control, tuning, and logging support
- FT8 and FT4 operating with:
  - real receive decode path
  - real signal generation path
  - on-air CQ / reply workflow
  - compact band activity / RX frequency panes
- POTA hunting workflow in the Voice pane
- Longwave-backed log submission and logbook-aware logging

## Still Rough / In Progress

- FT8/FT4 auto-logging and auto-sequencing still need more real-world refinement
- RTTY, SSTV transmit, and additional digital-mode operating workflows are not first-class yet
- packaging and installed-build validation still deserve ongoing attention as the app evolves
- UI polish is active work; expect labels, layouts, and flows to keep changing

## Solution Layout

- `src/ShackStack.Desktop`
  - Windows desktop entry point, packaging metadata, and assets
- `src/ShackStack.UI`
  - Avalonia views, controls, and viewmodels
- `src/ShackStack.Core`
  - app orchestration and workflows
- `src/ShackStack.Core.Abstractions`
  - shared contracts and models
- `src/ShackStack.Infrastructure.*`
  - radio, audio, waterfall, configuration, interop, and decoder implementations
- `src/ShackStack.DecoderHost`
  - out-of-process decoder boundary components
- `src/ShackStack.DecoderHost.GplWsjtx`
  - GPL weak-signal sidecar boundary for WSJT-X-style digital decode orchestration
- `installer`
  - Inno Setup packaging script

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-DecoderWorkers.ps1
dotnet build .\ShackStack.sln
```

## Publish

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-DecoderWorkers.ps1
dotnet publish .\src\ShackStack.Desktop\ShackStack.Desktop.csproj -c Release -r win-x64 --self-contained true -o .\publish\ShackStack-win-x64-v0.1-beta
```

## Installer

```powershell
& "C:\Program Files\Inno Setup 6\ISCC.exe" ".\installer\ShackStack.iss"
```

## Version

- current release candidate: `V-0.9.0 RC`

## Notes

- This repo is intentionally app-focused.
- Local publish artifacts are excluded from source control.
- Local tests, scratch docs, and development notes are intentionally not included in this clean branch.
- The broader Python decoder lab and experimentation workspace remain separate.
- Weak-signal digital work uses dedicated external worker boundaries so WSJT-X-derived decode and signal-generation logic can remain separated from the main app.
- If present, the app will prefer an external GPL sidecar specified by `SHACKSTACK_WSJTX_GPL_SIDECAR_PATH` before falling back to the in-repo development worker.
