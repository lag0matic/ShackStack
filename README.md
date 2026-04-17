# ShackStack

ShackStack is a Windows desktop ham radio application built with C# and Avalonia.
This was written with the assistance of ChatGPT, Claude, and Other AI tools - I am not a professional coder, and there will be bugs and rough edges.

This clean beta branch contains the operator-facing app code for the `V-0.1 BETA` release. It is intended to be the GitHub-ready application repo, separate from the larger local development workspace and decoder experimentation harnesses.

## Current Beta Scope

- direct Icom CI-V radio control
- VFO A / VFO B, `A/B`, `A=B`, and split operation
- waterfall, spectrum, S-meter, monitor audio, and persisted operator display/audio settings
- voice controls and rig-backed transmit settings
- fake FLRig compatibility for HAMRS-style interop
- SSTV receive workflow with live preview and pop-out desk/archive
- WeFAX receive workflow with live preview and pop-out desk/archive
- decoder host plumbing for CW, RTTY, SSTV, and WeFAX
- weak-signal digital scaffold for FT8/FT4 and future WSJT-style modes

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
  - planned separate GPL sidecar boundary for direct WSJT-X-derived weak-signal decoding
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
& "C:\Program Files\Inno Setup 7\ISCC.exe" ".\installer\ShackStack.iss"
```

## Version

- current beta: `V-0.1 BETA`

## Notes

- This repo is intentionally app-focused.
- Local publish artifacts are excluded from source control.
- Local tests, scratch docs, and development notes are intentionally not included in this clean branch.
- The broader Python decoder lab and experimentation workspace remain separate.
- Weak-signal digital work is moving toward a dedicated external sidecar boundary so WSJT-X-derived decode logic can remain separated from the main app.
- If present, the app will prefer an external GPL sidecar specified by `SHACKSTACK_WSJTX_GPL_SIDECAR_PATH` before falling back to the in-repo development worker.
