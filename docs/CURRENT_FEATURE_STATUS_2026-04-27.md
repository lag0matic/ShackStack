# Current Feature Status - 2026-04-27

Last refreshed: 2026-04-28.

This is the internal working inventory of ShackStack features. The README is the public release snapshot; this file is the operator/developer truth table.

## Core App Shell

| Feature | Status | Notes |
| --- | --- | --- |
| Main window as desk launcher | Functional | Main window now mostly launches Voice, CW, RTTY, Weak Signal, JS8, WeFAX, SSTV, FreeDV, keyboard modes, and Longwave desks. |
| Dedicated desk windows | Functional | This has helped reduce main-window density. Some desks still need layout polish. |
| App icon / branding | Functional | Antenna/ground ShackStack icon is wired into app/installer path, but installer icon regressions should be watched. |
| Settings persistence | Functional | App settings persist, including radio/audio/Longwave/interop options. Needs future review for stale settings and naming. |
| Shutdown cleanup | Improved | Desk open/start/stop/close testing no longer leaves worker zombies in the latest observed pass. Keep this in the smoke checklist after new workers are added. |

## Radio And Audio

| Feature | Status | Notes |
| --- | --- | --- |
| IC-7300 CI-V control | Functional | Frequency, mode, filters, VFO controls, split, tuner, noise controls, power, gain, and PTT are usable. |
| General rig support | Not a goal yet | The current design is IC-7300-shaped. Do not broaden unless there is a concrete second-radio target. |
| PTT reliability | Improved | Recent changes helped FT8/SSTV PTT flakiness. Continue watching live transmit starts/stops. |
| Audio routing | Functional | RX/TX/monitor devices and level displays work. FreeDV decoded speech is now separated from raw RX monitor audio with its own desk volume path. |
| COM port release | Improved | Radio reconnect behavior recovered after shutdown cleanup. Keep open/close/reopen in the installer smoke test. |

## Operating Desks

| Desk | Status | Notes |
| --- | --- | --- |
| Voice | Functional | Good home base for SSB/POTA operation. POTA spotting and Longwave quick logging are present. |
| Weak Signal / FT8 / FT4 / WSPR | Functional | Strongest digital workflow overall. QSO rail, alert tone, offset tracking, WSPR RX, and logging flow are improved. Auto-sequence should remain explicit and hard to misfire. |
| JS8 | Functional | RX/TX heartbeat and directed message flows work. Needs continued polish for richer message composition. |
| SSTV | Functional | RX/TX are in good shape after MMSSTV porting, FSKID, template, and overlay fixes. Latest live receive auto-picked the right mode and started cleanly; leave thresholds alone unless broken. |
| WeFAX | Functional RX | Desk is useful. Schedule and archive work; no TX planned. |
| RTTY | Functional RX | Decodes reasonably with hand tuning. TX can wait. |
| CW | Experimental | UI is roomier now, but decode quality is still the weak point. |
| Keyboard modes / PSK | In progress | fldigi-derived desk/sidecar work exists, with synthetic TX audio available. Needs real signal validation and more decoder polish. |
| FreeDV | Harness-proven / live-test ready | Codec2 and RADEV1 loopbacks work, RADEV1 callsign metadata round-trips, and decoded audio has a dedicated output path. Live RF still needs more catches. |
| Longwave | Functional but still worth polishing | Spots, spot posting, logbooks, logging, QRZ rejection messages, recent sync, and quick-log handoff work. Contact editing/export UX can still improve. |

## Logging And Longwave

| Feature | Status | Notes |
| --- | --- | --- |
| Longwave server integration | Functional | ShackStack talks directly to Longwave API for spots, spot posting, logbooks, callsign lookup, contact logging/deletion, QRZ upload/rejection status, and recent sync. |
| Longwave server ownership | External by design | Server lives on a separate machine for security/isolation. ShackStack should not become the server admin surface by default. |
| Quick log from mode desks | Improved | Voice, CW, RTTY, keyboard modes, SSTV, FreeDV, and weak-signal flows now share a clearer Longwave quick-log model. |
| FT8/FT4 Longwave logging | Improved | Recent work fixed missing/unclear logging and added clearer preview/status. User testing confirmed contacts now log visibly. |
| Contact editing | Partial / needs UX | Longwave API support exists; ShackStack still needs a stronger selected-contact editor. |
| ADIF export/import | Planned | Longwave has API support. Add to Longwave Desk, not every operating desk. |
| QRZ upload | Functional with server result surfaced | The app now presents sane QRZ API rejection messages, including subscription failure. Pending-only behavior should remain part of regression testing. |
| POTA spot posting | Functional | Voice desk can stage a spot from current rig state plus callsign/park/note. |
| Contact map | Missing in ShackStack | Longwave has it. Nice-to-have after log manager basics. |
| Offline queue/sync | Missing in ShackStack | Longwave Tauri client has local queue behavior. ShackStack may not need full offline mode immediately. |

## Interop

| Feature | Status | Decision |
| --- | --- | --- |
| Fake FLRig / XML-RPC server | Functional | Keep, but demote to optional external compatibility. |
| FLRig settings exposure | Needs cleanup | Hide under advanced/interop settings, default off. Label as compatibility for external apps, not core ShackStack. |
| HAMRS compatibility | Keep optional | Useful if the operator wants an external logger to see ShackStack as FLRig. |
| Longwave-through-FLRig | Avoid | ShackStack should use native radio state plus direct Longwave API, not fake FLRig as internal glue. |

## Packaging

| Feature | Status | Notes |
| --- | --- | --- |
| Inno installer | Functional | Installer builds and packages app/workers. Watch .NET/self-contained regressions. |
| Worker build script | Functional but sensitive | Must package native SSTV, WSJT, JS8 tools, RTTY, PSK, FreeDV, WEFAX/CW Python workers, and runtime DLLs. |
| FreeDV runtime packaging | Recently fixed | Harness exposed missing `libstdc++-6.dll`/`libwinpthread-1.dll` requirements. |
| License notices | Needs final audit | README covers major obligations, but final distribution should include complete notices for GPL/LGPL/MIT/Python/NuGet/runtime dependencies. |

## Known Risk Areas

- Large `MainWindowViewModel.cs` has accumulated too much responsibility.
- Main-window legacy panels have been removed; keep watching for duplicated controls as desks evolve.
- Worker folders and build scripts need periodic verification so installer output matches dev output.
- Mode desks have uneven logging UI language and density.
- CW can absorb infinite time for low return; avoid over-investing until the rest is polished.
- Longwave integration should grow carefully so ShackStack does not become a second full Longwave client unless that is explicitly desired.
