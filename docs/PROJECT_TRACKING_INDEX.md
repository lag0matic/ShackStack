# Project Tracking Index

Last refreshed: 2026-04-30.

Use these documents when planning the next ShackStack work session.

| Document | Purpose |
| --- | --- |
| `USER_MANUAL.md` | Operator-facing manual: desk workflows, setup notes, FreeDV Reporter, logging flow, and practical shutdown/testing guidance. |
| `DIGITAL_WEAK_MODE_STATUS_2026-04-29.md` | Current digital/weak mode status, planned modes, deferred/abandoned modes, and validation notes. Refreshed after WSPR RX, FreeDV RADEV1 harness work, PSK scaffolding, SSTV regression expansion, and Robot36 TX smoke testing. |
| `CURRENT_FEATURE_STATUS_2026-04-29.md` | Whole-app feature inventory across shell, radio/audio, desks, Longwave, interop, packaging, and risk areas. Refreshed after logging, shutdown, FreeDV audio/layout, SSTV stability work, and the main view-model split. |
| `NEXT_STEPS_AND_TECH_DEBT_2026-04-29.md` | Prioritized next work: Longwave desk UX, selected-contact editing, logging consistency, digital-mode continuation, and conservative post-split cleanup. |
| `DIGITAL_MODE_ROADMAP.md` | High-level mode roadmap. Keep as a broad reference; prefer the dated mode-status document for detailed current truth. |
| `RELEASE_1.0_NOTES.md` | Public-ish release notes for the 1.0 baseline. |
| `DEPLOYMENT_NOTES.md` | Installer/build/deployment notes. |
| `FREEDV_PORT_NOTES.md` | Codec2/FreeDV integration notes. |
| `SSTV_REGRESSION_HARNESS.md` | Synthetic SSTV RX regression command, current cases, artifacts, and pass criteria. |

Historical handoff, harvest, and one-off research notes are intentionally not part of the tracked 1.0 documentation set. Keep source-backed implementation details in the relevant sidecar README or a current dated status document when they are still actionable.

Recommended planning flow:

1. Read `CURRENT_FEATURE_STATUS_2026-04-29.md` for the full app picture.
2. Read `DIGITAL_WEAK_MODE_STATUS_2026-04-29.md` only if the work is mode/decoder related.
3. Use `NEXT_STEPS_AND_TECH_DEBT_2026-04-29.md` to pick the next chunk.
4. After a meaningful feature lands, update the dated status docs or replace them with a newer dated snapshot.
