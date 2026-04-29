# Digital And Weak-Mode Status - 2026-04-29

Last refreshed: 2026-04-29.

This document is the working map for ShackStack digital modes. It is intentionally more operational than the README: what works, what is in progress, what is planned, and what we have deliberately abandoned for this station.

Station assumption: HF-first home station, IC-7300-style CI-V control, wire antenna, Windows desktop. VHF/UHF, EME, satellite, meteor-scatter, and directional-antenna workflows are out of scope unless the station changes.

## Current Working Modes

| Mode | RX | TX | Current state | Source / strategy | Notes |
| --- | --- | --- | --- | --- | --- |
| Voice / SSB | Yes | Yes | Functional | Native ShackStack radio/audio path | Voice desk supports PTT, audio routing, POTA staging, spot posting, and Longwave logging. |
| FT8 | Yes | Yes | Functional | WSJT-X-derived GPL sidecar/runtime | RX/TX is strong after SNTP timing work, QSO rail cleanup, directed-message alert fix, and offset tracking. Auto sequencing still deserves caution. |
| FT4 | Yes | Yes | Functional | WSJT-X-derived GPL sidecar/runtime | Same operating family as FT8. Useful when short bursts are heard and FT8 does not explain them. |
| JS8 | Yes | Yes | Functional | JS8Call-compatible runtime/tools via GPL sidecar boundary | Live RX/TX works, including heartbeat and `@HB`. Richer JS8Call message packing can still improve later. |
| WSPR | Yes | Deferred | Functional RX | WSJT-X `wsprd.exe` | RX monitor works with two-minute cycle behavior. TX is deferred because it is propagation beaconing and needs power/duty-cycle guardrails. |
| RTTY | Yes | Not yet | Functional RX | fldigi-derived GPL sidecar | Practical live decode with hand tuning, reverse polarity, and 45.45/170 defaults. TX is possible later, but RX was the immediate need. |
| SSTV | Yes | Yes | Functional | MMSSTV-shaped native sidecar | Martin 1/2, Scottie 1/2, Robot36, and PD paths are the main focus. RX/TX, templates, FSKID, `%tocall`, slant/sync, and archive behavior are usable. Robot36 TX round-trip and imperfect auto-start cases are harness-green. |
| WeFAX | Yes | No | Functional RX | py-wefax/fldigi-inspired receive path | RX only by design. Schedule, live preview, archive, offset/slant, and cleanup controls are working. |
| FreeDV | Harness-proven, live pending | Harness-proven, live pending | Core loopback validated | Codec2/FreeDV runtime, RADEV1, and sidecar | Synthetic upstream and ShackStack TX->RX loopbacks pass, RADEV1 EOO callsign metadata round-trips, decoded audio has its own output path, and live RF is ready for more field catches. |
| CW | Experimental | Basic keying/TX exists | Rough | Python adaptive worker only | CW remains the dragon. It decodes somewhat, but not reliably enough to call solved. Treat operator confirmation as required. |
| PSK31 / PSK63 | Early | Early | In progress | fldigi-derived sidecar | Desk exists and audio generation/test WAVs exist. Live activity has been scarce, so RX quality is not yet proven. |

## Planned Next Digital Modes

| Mode family | Priority | Feasibility | Why it is worth doing |
| --- | --- | --- | --- |
| PSK31 / PSK63 polish | High | High | Common classic keyboard modes, good fit for fldigi-derived implementation. Already started. |
| Olivia / Contestia | High-Medium | High-Medium | Useful poor-condition keyboard modes; fldigi is the likely source of truth. |
| MFSK / THOR / DominoEX | Medium | Medium | More fldigi-derived keyboard-mode coverage. Do after PSK and Olivia if momentum remains good. |
| FreeDV live validation | Medium | Medium | HF digital voice is source-backed and harness-green through RADEV1. Live RF validation, latency, and operator polish are the remaining work. |
| RTTY TX | Medium-Low | Medium | Useful eventually, but not urgent compared with RX and other keyboard modes. |
| FST4 / FST4W | Low-Medium | Medium | HF-capable weak-signal modes. WSPR already covers much passive propagation-monitoring need. |
| JT65 / JT9 | Low | Medium-Low | Older HF compatibility modes. Keep possible, not a near-term focus. |
| ARDOP / Winlink-style integration | Low | Medium-Low | HF capable, but mailbox/session workflow deserves a dedicated future desk. |
| Packet / APRS / AX.25 | Low | Low-Medium | Dire Wolf would be the likely integration source. Not a current home-HF priority. |

## Deferred Or Abandoned

| Mode | Decision | Reason |
| --- | --- | --- |
| Q65 | Abandoned for now | Mostly VHF/UHF/EME/specialty weak-signal work. Not useful for the current HF wire station. |
| MSK144 | Abandoned for now | Meteor scatter workflow needs the wrong station setup for us. |
| JT4 | Abandoned for now | Niche VHF/UHF/microwave use. |
| AVT SSTV | Deferred / likely no | Rare in current ham SSTV use. Martin/Scottie/Robot/PD matter more. |
| WeFAX TX | Explicit no | We only receive weather fax. No reason to transmit WEFAX. |
| VARA HF native port | Not possible as a direct port | VARA is closed/proprietary. Future support would be external integration only. |
| PACTOR native support | No | Hardware/proprietary ecosystem; not a good ShackStack-native target. |
| Broad contest-suite scope | No | ShackStack is an operating desk, not a contest logger replacement. |

## Validation Status

| Area | Last known validation |
| --- | --- |
| FT8 | Live RX/TX improved after SNTP time correction, offset controls, QSO rail cleanup, alert tone fix, and logging fixes. |
| FT4 | Runtime support present through WSJT sidecar; live use should be checked when signals are present. |
| JS8 | Live heartbeat TX/RX confirmed on poor bands. Directed replies to `W8STR` seen. |
| WSPR | Live RX confirmed and UI labeling fixed. |
| SSTV | Live RX/TX usable, including FSKID and template overlay fixes. Synthetic regression now covers imperfect Martin 1/2, Scottie 1/2, Robot36 auto-start, static rejection, PD120, force-start, and Robot36 TX round-trip. |
| WeFAX | Live image decode looks good after slant/offset and desk changes. |
| RTTY | Live decode is roughly comparable to IC-7300 built-in under conditions tested. |
| FreeDV | Synthetic loopback passes through upstream and ShackStack RADEV1 paths. Real voice WAV loopback is intelligible. Live RF still needs more real on-air tests. |
| PSK | TX waveform smoke tested; live RX not confirmed due lack of signals. |
| CW | Still rough; do not spend disproportionate time here until higher-value modes/UI are settled. |

## Porting Rule

Prefer proven, source-backed implementations:

- WSJT-family modes should stay behind the WSJT-derived sidecar/runtime boundary.
- JS8 should stay aligned with JS8Call behavior where practical.
- Keyboard modes should be compared against fldigi before inventing DSP.
- SSTV should remain faithful to MMSSTV behavior where possible.
- WeFAX should keep borrowing proven py-wefax/fldigi-style receive patterns.
- Proprietary modes should become external integrations, not native rewrites.
