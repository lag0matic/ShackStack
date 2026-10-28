# Nexus Layout Adaptation

Branch: `codex/nexus-layout`. First implementation pass: 2026-10-19.

The operating desks now use a consistent left navigation rail, radio toolbar, and collapsible spectrum above their mode-specific content. The implementation uses ShackStack's existing Avalonia controls, commands, and neutral black palette. Nexus's screenshots informed the arrangement; no Nexus source or model weights were copied into this change.

## Changes

- Navigation is available in the main shell and all eleven desks. Selecting an already-open desk activates its existing window through the original shell lifecycle.
- Each desk has VFO A digit tuning, mode/band commands, A/B, A=B, split, connection state, S-meter text, and PTT state. The main shell retains its full two-VFO view.
- The shared scope displays the existing spectrum path, waterfall bitmap, and frequency markers. Collapse it to recover vertical room. The logbook omits it.
- Voice/POTA places activity and the current contact side by side. Callsign lookup, reports, station details, logbook selection, and log/save status are kept together. Spot posting and mic/power settings expand on demand. The existing hold-to-talk handlers remain in use.
- CW puts decoded copy beside the same contact form. Pitch/speed and receive actions sit above; macros and typed sending sit below. Decoder details and macro staging remain available in an expandable section.
- Weak-signal lists have larger text, stack vertically for usable message width, and keep conversation controls beside them. Offset controls wrap instead of extending outside the panel; the conversation list can receive pointer scrolling.
- Other desks retain their existing mode-specific layouts beneath the shared frame. FreeDV panels scroll; CW Lab transcript regions share available height rather than reserving fixed diagnostic heights.
- Desk default sizes allow room for the new frame. SSTV and WeFAX retain larger minimum widths to accommodate their existing editors.

Independent desk windows remain available for multiple monitors. This pass does not embed every mode into one window, replace decoders, or integrate DeepCW/Tempo. Some inherited dense forms and helper text still need individual layout passes after user feedback.

## Preview and Verification

Offline preview creates a view model without radio, audio, network, or decoder service instances. It uses default settings rather than the user's live configuration. It is intended for layout inspection, not RF operation.

```powershell
dotnet build .\ShackStack.sln -c Release --no-restore
dotnet .\src\ShackStack.Desktop\bin\Release\net9.0\ShackStack.Desktop.dll --layout-preview --desk=Voice
```

The render check invokes each desk through the navigation buttons twice, verifies that only one owned desk window exists, renders it at two requested sizes, closes it, and repeats for the other desks. Window minimum sizes clamp the smaller request where necessary.

```powershell
dotnet .\src\ShackStack.Desktop\bin\Release\net9.0\ShackStack.Desktop.dll --layout-preview --layout-snapshots=D:\ShackStack.Avalonia\.tmp-layout-review
```

Render artifacts are local ignored PNG files. The current test creates 22 images for the eleven desks at requested sizes 1440x960 and 1180x800. This exercises actual view construction, compiled bindings, shared content layout, navigation handlers, and window reuse. Representative images were inspected for clipping and several issues corrected. It does not verify live radio commands, audio, decoder output, Longwave submissions, every expanded state, or every DPI setting.

The main application starts normally without `--layout-preview`. No installed application or installer was replaced.

## Existing Work

Pi bridge and Nexus comparison documents were already present as uncommitted changes when this branch was created. They remain intact. Keep them separate from a future layout-only commit. The comparison plan remains the reference for later workflow, CW, and Tempo work.
