# Next Steps And Tech-Debt Plan - 2026-04-27

Last refreshed: 2026-04-28.

This is the current work plan after the 1.0-ish baseline. The aim is not to keep adding controls forever. The aim is to make ShackStack feel intentional: each mode has a clear desk, logging is consistent, workers are packaged predictably, and the codebase has fewer mystery corners.

## Guiding Principles

- Keep ShackStack as the radio operating desk.
- Keep Longwave as the canonical log/sync server.
- Use direct API/service boundaries internally, not fake compatibility layers.
- Hide niche/advanced compatibility behind explicit options.
- Prefer source-backed ports over invented DSP.
- Keep mode desks useful without turning them into dense tax forms.
- Make code placement obvious enough that future work does not require archaeology.

## Phase 1 - Longwave API Coverage

Add missing Longwave API methods to ShackStack before redesigning the UI around them.

Status: mostly completed for the first practical pass. The remaining work is UX quality, selected-contact editing, and regression testing rather than basic connectivity.

| Task | Priority | Notes |
| --- | --- | --- |
| Contact/logbook selection and quick logging | Done / watch | Logbook dropdowns now populate across desks and quick-log previews are shared more consistently. |
| Contact update/editor UX | High | API coverage is less important than making selected-contact editing obvious in Longwave Desk. |
| ADIF export/import | Medium | Useful, but probably less important from ShackStack than from Longwave standalone. |
| Pending-only QRZ upload | Functional / watch | QRZ service rejection messages surface cleanly. Keep duplicate-upload behavior in regression tests once subscription is active again. |
| POTA spot posting | Functional | Voice desk can stage and post spots from current rig state plus callsign/park/note. |
| Add settings summary read | Medium | Useful for showing QRZ/POTA configured status without making ShackStack a full admin console. |

## Phase 2 - Longwave Desk Redesign

Make the Longwave desk the logbook office, not just a skinny helper panel.

Target layout:

- Connection/status strip: selected server, active logbook, QRZ/POTA configured indicators.
- Recent contacts list: filter by current logbook, mode, date, and callsign.
- Selected contact editor: callsign, date/time, band/mode/frequency, reports, park, grid, name/QTH, state/country/DXCC.
- Logbook actions: create, rename/update, delete, ADIF export/import, pending-only QRZ upload.
- POTA actions: spots, selected spot staging, spot posting/self-spot if activation logbook.
- Optional map panel after the basics are clean.

Important UX rule: Longwave Desk can be denser than mode desks, but it should still use progressive disclosure. Advanced import/export/admin controls should not crowd the basic log/contact editor.

## Phase 3 - Consistent Logging In Every Mode Desk

Every operating desk should have the same mental model:

- Show the currently selected logbook.
- Show a clear contact preview before logging.
- Let the operator adjust the few fields that matter for that mode.
- Use one obvious `Log Contact` action.
- After success, show exactly who/what/when was logged.

Desk-specific intent:

| Desk | Logging goal |
| --- | --- |
| Voice | SSB/POTA quick log with rig-derived frequency/mode, selected spot, QRZ lookup, and reports. |
| Weak Signal | QSO rail should log the active/tracked contact, not whichever row happens to be selected unless explicitly chosen. |
| JS8 | Log directed contacts/conversations deliberately; do not auto-log casual heartbeat noise. |
| SSTV | Quick logging exists, including FSKID handoff. Keep it lightweight so SSTV remains image-first. |
| RTTY / PSK / keyboard modes | Manual quick logging exists. Continue polishing after PSK live RX is proven. |
| CW | Keep logging simple; operator-confirmed calls only. Do not let experimental decode spam callsign fields. |
| WeFAX | No QSO logging needed. Archive/schedule only. |
| FreeDV | Voice-like quick logging exists with RADEV1 callsign handoff. Live RF validation is still needed. |

## Phase 4 - Interop Cleanup

Status: completed for the first cleanup pass on 2026-04-27.

FLRig compatibility remains useful, but it should stop looking like a core ShackStack feature.

Tasks:

- Completed: Rename settings copy from `Fake FLRig` to `External FLRig Compatibility`.
- Completed: Default it off for new home ShackStack installs/settings.
- Completed: Remove footer/status prominence unless enabled.
- Kept: Server code remains for HAMRS/field-tool compatibility.
- Kept: Internal ShackStack-to-Longwave behavior stays on native radio state plus direct Longwave API, not FLRig.
- Remaining polish: If settings grows an explicit advanced/interop expander, move host/port/enabled controls there.

## Phase 5 - Digital Mode Work

Continue only after the logging/UI pass has a clean foundation.

Recommended order:

1. PSK31/PSK63 live RX validation and fldigi-derived fixes.
2. FreeDV live RF validation, especially RADEV1 receive quality and latency.
3. PSK TX polish if RX is viable.
4. Olivia/Contestia from fldigi source patterns.
5. RTTY TX only if it becomes useful.
6. CW only as a contained maintenance effort, not an endless quest.

Avoid for now:

- Q65, MSK144, JT4, AVT SSTV, PACTOR, and full contest-suite scope.
- VARA native implementation; external integration only if we ever go there.

## Phase 6 - Final Sanity And Tech-Debt Pass

This should be the last major step after feature/UI direction settles. Do not do it too early, because active feature work would immediately disturb the cleanup.

### Code Organization

- Split obvious chunks out of `MainWindowViewModel.cs` into mode-specific coordinators/services where practical.
- Keep UI view models and infrastructure services from knowing too much about each other.
- Make worker host naming consistent: WSJT, JS8, RTTY, PSK, FreeDV, SSTV, WEFAX/CW.
- Review models/contracts for duplicate Longwave/spot/contact structures.

### Dead And Vestigial Code

- Remove hidden legacy panels once their desk replacements are stable; do this conservatively and build after each batch.
- Remove old Python workers that are no longer used, except where they remain the live default.
- Remove stale harness outputs and local generated artifacts from the repo surface.
- Review old comments that describe abandoned behavior.
- Search for unused commands/properties in XAML bindings and view models.

### Packaging

- Run `installer\Build-DecoderWorkers.ps1` from clean-ish state.
- Verify every worker folder in publish output contains exactly the needed executables/runtime DLLs.
- Verify installer is self-contained and does not ask for .NET.
- Verify app icon in installer/start menu/uninstall entries.
- Verify no local scratch folders, `.tmp-*`, recordings, generated WAVs/images, or build outputs are staged.

### Runtime Stress Pass

- Launch app, open every desk, start/stop every RX path, close desks, close app.
- Confirm no orphan `ShackStack.*` or decoder worker processes remain.
- Confirm COM port is released and app can reconnect after close/reopen.
- Confirm PTT starts and releases for FT8, SSTV, voice, and any generated-mode TX path.
- Confirm Longwave logging writes to the expected logbook and is visible from Longwave.
- Confirm QRZ upload does not resend already uploaded contacts.

### Documentation And Release

- Update README after the cleanup pass, not before every tiny change.
- Keep mode status and next-step docs current when a mode changes state.
- Keep license notices and borrowed-source notes honest.
- Tag or label the stable build only after installer smoke tests pass.

## Near-Term Recommended Chunk

The next concrete development pass should be:

1. Redesign Longwave Desk around recent contacts plus selected-contact editing.
2. Do a small consistency pass on quick-log blocks after a few more live logs.
3. Continue PSK/keyboard-mode validation when live signals or synthetic fldigi comparisons are available.
4. Keep FreeDV ready for live catches, but avoid big changes until real RF exposes a specific failure.
5. Run a conservative code-debt pass: stale comments, hidden legacy panels, unused commands/properties, and worker packaging names.

That gives the biggest daily-use payoff without opening another decoder dragon.
