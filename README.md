# ShackStack.Avalonia

This is the new sibling rewrite workspace for ShackStack.

It is intentionally separate from the existing Python application so the current working app remains untouched while the rewrite evolves.

## Rewrite Goals

- keep ShackStack's product identity
- move to a cleaner long-term desktop architecture
- preserve direct radio control as the primary path
- preserve fake FLRig support for HAMRS
- keep audio, waterfall, and CW as first-class features
- isolate experimental decoders from the operator shell

## Solution Shape

- `ShackStack.Desktop`
  - Avalonia app entry and composition root
- `ShackStack.UI`
  - views, viewmodels, UI-only behavior
- `ShackStack.Core`
  - application orchestration and state
- `ShackStack.Core.Abstractions`
  - contracts and shared models
- `ShackStack.Infrastructure.*`
  - radio, audio, waterfall, interop, decoder implementations
- `ShackStack.DecoderHost`
  - future out-of-process decoder worker

## First Milestone

1. compile-ready shell
2. dark muted theme
3. service boundaries in code
4. placeholder operating/settings/diagnostics workspaces
5. decoder host boundary stub

## Notes

- This workspace is the place for the C# / Avalonia rewrite.
- The Python repo remains the operational reference implementation.
- Waterfall rendering direction is render-thread Skia via Avalonia `ICustomDrawOperation`, not UI-thread bitmap painting.
- Windows packaging direction is self-contained `win-x64` publish + Inno Setup, not single-file publish.

## CW Direction

- The main ShackStack application remains a C# / Avalonia desktop app.
- CW receive decoding is treated as an isolated sidecar / plugin problem, not main-shell business logic.
- CW transmit stays in-process in the main app and is driven directly from CI-V keying commands.
- The first serious CW RX experimentation path should prioritize fast iteration and can use Python behind the sidecar boundary.
- If a decoder later proves worth hardening or optimizing, it can be kept as a sidecar or reimplemented in Rust/C# without reshaping the app shell.

## Current Plan

1. finish daily-use shell polish
2. keep voice and HAMRS/FLRig paths stable
3. build the CW tab around a decoder sidecar contract
4. implement CW TX in the main app via CI-V key down / key up timing
5. prototype CW RX in a separate decoder worker with text, confidence, tone, and WPM outputs
6. return to RTTY, WeFAX, SSTV, and other digital tabs after the CW boundary is in place
