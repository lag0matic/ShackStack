# Next Steps And Tech-Debt Plan - 2026-04-29

Last refreshed: 2026-04-29.

This is the current work plan after the 1.0 baseline and first cleanup/desk split. The aim is not to keep adding controls forever. The aim is to make ShackStack feel intentional: each mode has a clear desk, logging is consistent, workers are packaged predictably, and the codebase has fewer mystery corners.

## Guiding Principles

- Keep ShackStack as the radio operating desk.
- Keep Longwave as the canonical log/sync server.
- Use direct API/service boundaries internally, not fake compatibility layers.
- Hide niche/advanced compatibility behind explicit options.
- Prefer source-backed ports over invented DSP.
- Keep mode desks useful without turning them into dense tax forms.
- Make code placement obvious enough that future work does not require archaeology.

## Completed Cleanup Baseline

| Area | State |
| --- | --- |
| Main view-model split | First pass complete. The root view model is still the coordinator, but desk-specific chunks now live in partial files by concern. |
| Main window density | Main window is now mostly launcher/status surface; mode work happens in desks. |
| Worker shutdown | Improved. Continue smoke-testing after adding workers. |
| Installer packaging | Functional. Build script packages native SSTV, GPL WSJT/RTTY/PSK/FreeDV sidecars, JS8/WSJT tools, and Python workers. |
| SSTV regression harness | Expanded. Main auto-start modes and Robot36 TX round-trip are covered. |
| FreeDV audio/layout | Decoded audio has its own output path; live-state text no longer resizes the desk. |

## Phase 1 - Longwave Desk And Logging Polish

Make the Longwave desk the logbook office, not just a helper panel.

| Task | Priority | Notes |
| --- | --- | --- |
| Selected-contact editor | High | API support exists; make editing obvious in Longwave Desk. |
| Contact/logbook filtering | High-Medium | Recent contacts should filter by current logbook, mode, date, and callsign. |
| ADIF export/import | Medium | Useful, but probably less important from ShackStack than from Longwave standalone. |
| Pending-only QRZ upload regression | Medium | QRZ service rejection messages surface cleanly. Confirm duplicate-upload behavior once subscription is active again. |
| Settings summary read | Medium | Useful for QRZ/POTA configured status without turning ShackStack into server admin UI. |

Target layout:

- Connection/status strip: selected server, active logbook, QRZ/POTA configured indicators.
- Recent contacts list: filter by current logbook, mode, date, and callsign.
- Selected contact editor: callsign, date/time, band/mode/frequency, reports, park, grid, name/QTH, state/country/DXCC.
- Logbook actions: create, rename/update, delete, ADIF export/import, pending-only QRZ upload.
- POTA actions: spots, selected spot staging, spot posting/self-spot if activation logbook.
- Optional map panel after the basics are clean.

## Phase 2 - Consistent Logging In Every Mode Desk

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

## Phase 3 - Digital Mode Work

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

## Phase 4 - Conservative Tech-Debt Pass

Do this in small batches and build after each batch.

### Code Organization

- Consider extracting true services from the `MainWindowViewModel.*` partials only where a boundary is obvious.
- Keep UI view models and infrastructure services from knowing too much about each other.
- Make worker host naming consistent: WSJT, JS8, RTTY, PSK, FreeDV, SSTV, WEFAX/CW.
- Review models/contracts for duplicate Longwave/spot/contact structures.

### Dead And Vestigial Code

- Search for unused commands/properties in XAML bindings and view models.
- Remove old comments that describe abandoned behavior.
- Remove old Python workers only when they are definitely not the live default.
- Keep local generated artifacts out of GitHub: `.tmp-*`, recordings, generated WAVs/images, build outputs, and publish output.

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

## Near-Term Recommended Chunk

The next concrete development pass should be:

1. Redesign Longwave Desk around recent contacts plus selected-contact editing.
2. Do a small consistency pass on quick-log blocks after a few more live logs.
3. Continue PSK/keyboard-mode validation when live signals or synthetic fldigi comparisons are available.
4. Keep FreeDV ready for live catches, but avoid big changes until real RF exposes a specific failure.
5. Run a conservative binding/command cleanup pass now that the desk split is stable.

That gives the biggest daily-use payoff without opening another decoder dragon.
