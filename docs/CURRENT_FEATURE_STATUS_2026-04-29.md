# Current Feature Status - 2026-04-29

Last refreshed: 2026-04-29.

This is the internal working inventory of ShackStack features. The README is the public release snapshot; this file is the operator/developer truth table for the current `main` branch.

## Core App Shell

| Feature | Status | Notes |
| --- | --- | --- |
| Main window as desk launcher | Functional | Main window is mostly a launcher/status surface for Voice, CW, RTTY, Weak Signal, JS8, WeFAX, SSTV, FreeDV, keyboard modes, and Longwave desks. |
| Dedicated desk windows | Functional | Desk model is now the normal workflow. Most old main-window dense panels have been retired. |
| Main view-model split | Completed first pass | `MainWindowViewModel` has been split into partial files by concern: audio, digital modes, Longwave, radio, settings, SSTV, weak signal, and WeFAX. Further service extraction can happen later, but the obvious monster-file pressure is relieved. |
| App icon / branding | Functional | Antenna/ground ShackStack icon is wired into app and installer output. |
| Settings persistence | Functional | App settings persist across radio/audio/Longwave/interop options. Future cleanup should focus on stale setting names and advanced grouping. |
| Shutdown cleanup | Improved | Start/stop/close testing across desks no longer leaves worker zombies in the latest observed passes. Keep this in smoke tests when adding workers. |

## Radio And Audio

| Feature | Status | Notes |
| --- | --- | --- |
| IC-7300 CI-V control | Functional | Frequency, mode, filters, VFO controls, split, tuner, noise controls, power, gain, and PTT are usable. |
| General rig support | Not a goal yet | Current app is IC-7300-shaped. Broaden only for a concrete second-radio target. |
| PTT reliability | Improved | FT8/SSTV/voice PTT starts are improved. Continue watching live transmit starts/stops. |
| Audio routing | Functional | RX/TX/monitor devices and level displays work. FreeDV decoded speech is separated from raw RX monitor audio and has its own desk volume path. |
| COM port release | Improved | Radio reconnect behavior recovered after shutdown cleanup. Keep open/close/reopen in installer smoke tests. |

## Operating Desks

| Desk | Status | Notes |
| --- | --- | --- |
| Voice | Functional | Good home base for SSB/POTA operation. POTA spotting and Longwave quick logging are present. |
| Weak Signal / FT8 / FT4 / WSPR | Functional | Strong digital workflow. QSO rail, alert tone, offset tracking, WSPR RX, SNTP timing, and logging flow are improved. Auto-sequence should remain explicit and hard to misfire. |
| JS8 | Functional | RX/TX heartbeat and directed message flows work, including `@HB`. Continue polish for richer message composition. |
| SSTV | Functional | RX/TX are in good shape after MMSSTV porting, FSKID, templates, overlay fixes, and latest auto-start regression coverage. Robot36 TX round-trip is harness-green. |
| WeFAX | Functional RX | Schedule, live preview, archive, offset/slant, and cleanup controls work. No TX planned. |
| RTTY | Functional RX | Decodes reasonably with hand tuning. TX can wait. |
| CW | Experimental | UI is roomier, but decode quality is still the weak point. Treat decoded callsigns as operator-confirmed only. |
| Keyboard modes / PSK | In progress | fldigi-derived desk/sidecar and synthetic TX audio exist. Needs real signal validation and more RX polish. |
| FreeDV | Harness-proven / live-test ready | Codec2 and RADEV1 loopbacks work, RADEV1 callsign metadata round-trips, decoded audio has its own output path, and the FreeDV desk layout has been stabilized against telemetry flicker. Live RF still needs more catches. |
| Longwave | Functional, polish-worthy | Spots, spot posting, logbooks, logging, QRZ rejection messages, recent sync, and quick-log handoff work. Contact editing/export UX can still improve. |

## Logging And Longwave

| Feature | Status | Notes |
| --- | --- | --- |
| Longwave server integration | Functional | ShackStack talks directly to Longwave API for spots, spot posting, logbooks, callsign lookup, contact logging/deletion, QRZ upload/rejection status, and recent sync. |
| Longwave server ownership | External by design | Server lives on a separate machine for security/isolation. ShackStack should not become the server admin surface by default. |
| Quick log from mode desks | Improved | Voice, CW, RTTY, keyboard modes, SSTV, FreeDV, and weak-signal flows share a clearer Longwave quick-log model. |
| FT8/FT4 Longwave logging | Improved | Contacts log visibly with clearer preview/status. Continue live-QSO validation. |
| Contact editing | Partial / needs UX | API support exists; ShackStack still needs a stronger selected-contact editor. |
| ADIF export/import | Planned | Longwave has API support. Add to Longwave Desk, not every operating desk. |
| QRZ upload | Functional with server result surfaced | App presents QRZ API rejection messages, including subscription failure. Pending-only behavior should remain part of regression testing once subscription is active. |
| POTA spot posting | Functional | Voice desk can stage a spot from current rig state plus callsign/park/note. |
| Contact map | Missing in ShackStack | Longwave has it. Nice-to-have after log manager basics. |
| Offline queue/sync | Missing in ShackStack | Longwave Tauri client has local queue behavior. ShackStack may not need full offline mode immediately. |

## Interop

| Feature | Status | Decision |
| --- | --- | --- |
| Fake FLRig / XML-RPC server | Functional optional compatibility | Keep for HAMRS/field-tool compatibility, but do not use it as internal glue. |
| FLRig settings exposure | Needs polish | Hide under advanced/interop settings, default off. Label as compatibility for external apps. |
| HAMRS compatibility | Keep optional | Useful if the operator wants an external logger to see ShackStack as FLRig. |
| Longwave-through-FLRig | Avoid | ShackStack should use native radio state plus direct Longwave API. |

## Packaging

| Feature | Status | Notes |
| --- | --- | --- |
| Inno installer | Functional | Installer builds and packages app/workers. Latest fresh installer was built on 2026-04-29. |
| Worker build script | Functional but sensitive | Must package native SSTV, WSJT, JS8 tools, RTTY, PSK, FreeDV, WEFAX/CW Python workers, and runtime DLLs. |
| FreeDV runtime packaging | Improved | Harness exposed runtime DLL needs; packaged sidecar now includes required MinGW runtime pieces. |
| License notices | Needs final audit | README covers major obligations, but final distribution should include complete notices for GPL/LGPL/MIT/Python/NuGet/runtime dependencies. |

## Known Risk Areas

- Mode desks have uneven logging UI language and density.
- Worker folders and build scripts need periodic verification so installer output matches dev output.
- CW can absorb infinite time for low return; avoid over-investing until the rest is polished.
- FreeDV live RF still needs more real-world validation before calling it fully operator-proven.
- PSK live RX remains unproven due lack of signals.
- Longwave integration should grow carefully so ShackStack does not become a second full Longwave client unless that is explicitly desired.
