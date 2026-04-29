# Project Tracking Index

Last refreshed: 2026-04-28.

Use these documents when planning the next ShackStack work session.

| Document | Purpose |
| --- | --- |
| `DIGITAL_WEAK_MODE_STATUS_2026-04-27.md` | Current digital/weak mode status, planned modes, deferred/abandoned modes, and validation notes. Refreshed after WSPR RX, FreeDV RADEV1 harness work, and PSK scaffolding. |
| `CURRENT_FEATURE_STATUS_2026-04-27.md` | Whole-app feature inventory across shell, radio/audio, desks, Longwave, interop, packaging, and risk areas. Refreshed after logging, shutdown, FreeDV audio, and SSTV stability work. |
| `NEXT_STEPS_AND_TECH_DEBT_2026-04-27.md` | Prioritized next work: Longwave desk UX, selected-contact editing, digital-mode continuation, and final tech-debt pass. |
| `DIGITAL_MODE_ROADMAP.md` | High-level mode roadmap. Keep as a broad reference; prefer the dated mode-status document for detailed current truth. |
| `RELEASE_1.0_NOTES.md` | Public-ish release notes for the 1.0 baseline. |
| `DEPLOYMENT_NOTES.md` | Installer/build/deployment notes. |
| `FREEDV_PORT_NOTES.md` | Codec2/FreeDV integration notes. |
| `SSTV_REGRESSION_HARNESS.md` | Synthetic SSTV RX regression command, current cases, artifacts, and pass criteria. |
| `SSTV_MMSSTV_HARVEST.md` and `SSTV_MMSSTV_BASELINE_2026-04-24.md` | MMSSTV direct-port research and baseline notes. |
| `WEFAX_PY_WEFAX_HARVEST.md` | WeFAX receive implementation research. |

Recommended planning flow:

1. Read `CURRENT_FEATURE_STATUS_2026-04-27.md` for the full app picture.
2. Read `DIGITAL_WEAK_MODE_STATUS_2026-04-27.md` only if the work is mode/decoder related.
3. Use `NEXT_STEPS_AND_TECH_DEBT_2026-04-27.md` to pick the next chunk.
4. After a meaningful feature lands, update the dated status docs or replace them with a newer dated snapshot.
