# Digital Mode Roadmap

Last refreshed: 2026-04-29.

This document captures the next likely digital-mode targets after the ShackStack 1.0 baseline.

The current target station is HF-first: IC-7300-class rig, wire antenna, practical operating from common HF bands. VHF/UHF, satellite, EME, meteor-scatter, and specialized directional-antenna workflows are intentionally out of scope unless the station changes.

Current 1.0 coverage already includes voice/POTA workflow, FT8, FT4, JS8, WSPR receive, RTTY receive, SSTV receive/transmit, WeFAX receive, FreeDV/RADEV1 harness support with separated decoded audio, Longwave integration, and experimental CW.

## Best Next Targets

| Mode | Current status | Feasibility | Note |
| --- | --- | --- | --- |
| WSPR | RX functional | High | Uses bundled WSJT-X `wsprd.exe` with two-minute cycle buffering. RX is useful as passive HF propagation awareness. TX can wait; it needs careful power and duty-cycle guardrails. |
| PSK31 / PSK63 | In progress | High | fldigi is the source path. Desk/sidecar and synthetic TX audio exist; live RX still needs real signals and more polish. |
| Olivia / Contestia | Unsupported | High-Medium | fldigi-derived implementation should be practical. Strong poor-condition keyboard modes. |
| MFSK / THOR / DominoEX | Unsupported | Medium | Also fldigi territory. Useful, but less common than PSK or Olivia. |
| Hellschreiber | Unsupported | Medium | Visually interesting and approachable from fldigi, but lower practical priority. |
| FST4 / FST4W | Scaffolded weak-signal modes | Medium | HF-capable WSJT-family modes. FST4W overlaps WSPR-style propagation monitoring, so it is useful but not urgent now that WSPR RX works. |
| JT65 / JT9 | Scaffolded weak-signal modes | Medium-Low | HF-capable but older and less central than FT8/FT4/JS8/WSPR. Keep as possible compatibility modes, not near-term focus. |

## Bigger Or More Specialized

| Mode | Current status | Feasibility | Note |
| --- | --- | --- | --- |
| VARA HF / VarAC | Unsupported | Low-Medium | VARA itself is closed/proprietary. ShackStack can potentially integrate with an installed VARA/VarAC setup, but cannot direct-port it like MMSSTV, fldigi, or WSJT-X. |
| ARDOP / Winlink-style modes | Unsupported | Medium-Low | HF-capable through ardopcf or external integration, but the workflow becomes mailbox/session based rather than normal live decode. |
| FreeDV | Harness-proven | Medium | HF-capable digital voice. ShackStack now has Codec2/RADEV1 sidecar support, synthetic TX/RX loopback, EOO callsign metadata, separated decoded-audio path, and stabilized desk telemetry layout. Live RF validation and latency reduction are next. |
| ALE / MIL-STD-188-141A monitor | Unsupported | Medium | Down-the-line HF utility monitor target. RX-first only: detect ALE bursts, decode addresses/calls and link-quality metadata if practical, and keep it separate from RTTY/PSK because ALE is not a two-tone keyboard mode. Look for a proven open-source decoder before attempting any native implementation. |
| Packet / APRS / AX.25 | Unsupported | Low-Medium | Dire Wolf is the obvious source/integration path. HF packet exists, but this is more of a packet/TNC desk than a simple decoder and is not a near-term need. |
| PACTOR | Unsupported | Very Low | Mostly hardware/proprietary ecosystem. Not a good ShackStack-native target. |
| AMTOR / Clover / obscure legacy modes | Unsupported | Low | Mostly historical or rare. Low priority unless a specific use case appears. |

## Out Of Scope For This Station

| Mode | Reason |
| --- | --- |
| Q65 | Primarily interesting for weak VHF/UHF, EME, and specialized marginal paths. Not important for an HF-only wire station. |
| MSK144 | Meteor-scatter mode. Without a VHF setup and directional antenna workflow, this is bin-bait. |
| JT4 | Mostly niche VHF/UHF/microwave usage. Not worth carrying as a ShackStack focus item right now. |

## Suggested Focus Order

1. PSK31 / PSK63 live RX validation and polish.
2. FreeDV live RF validation and latency/output polish.
3. Olivia / Contestia.
4. MFSK / THOR / DominoEX if fldigi porting momentum is good.
5. FST4 / FST4W only if we want more HF propagation-monitoring coverage after WSPR.
6. JT65 / JT9 only if older HF compatibility becomes useful.
7. ALE monitor only after the main keyboard/digital voice paths are stable.
8. VARA/VarAC, ARDOP/Winlink, or packet only as external/specialized desk integrations.

## Practical Rule

Prefer proven source-backed implementations over invented decoders:

- WSJT-family modes should come through the WSJT-derived sidecar boundary where possible.
- Keyboard modes should be compared against fldigi before inventing anything new.
- Proprietary modes should be treated as external integrations, not native ports.
