# ShackStack User Manual

Last refreshed: 2026-04-30.

This is the operator-facing manual for ShackStack. The README describes what the project is; this document describes how to use it at the radio.

ShackStack is built around desks. The main window is the launcher and station overview. Each desk is the working area for one operating style: Voice, Weak Signal, JS8, SSTV, WeFAX, RTTY, CW, keyboard modes, FreeDV, and Longwave logging.

## Station Setup

Before operating, confirm the basic station plumbing:

- Connect the radio from the main window settings/control area.
- Select the receive audio device that carries radio audio into the PC.
- Select the transmit audio device that feeds audio from the PC to the radio.
- Select the monitor/headphone output if you want ShackStack to play receive audio locally.
- Confirm the station callsign and grid are correct; digital modes, FreeDV Reporter, SSTV FSKID, JS8 heartbeat, and logging all depend on these values.
- For IC-7300-style digital audio modes, prefer USB-D above 10 MHz and LSB-D below 10 MHz unless a mode-specific workflow says otherwise.

## Main Window

Use the main window as the dispatch board:

- Open mode desks.
- Check radio connection and current frequency/mode.
- Watch audio levels.
- Keep long-running station status visible.
- Avoid doing dense operating work here; most mode controls intentionally live in desks.

## Voice Desk

The Voice desk is the SSB/POTA operating area.

- Use PTT for normal voice transmit.
- Use the POTA section to stage a spot from the current rig frequency and mode.
- Enter callsign, park, and note before posting a new spot.
- Use the Longwave logging controls to stage or log a contact.

## Weak Signal Desk

The Weak Signal desk covers FT8, FT4, WSPR receive, and related WSJT-X-style modes.

- Pick the mode and band frequency preset.
- Start RX and wait for the cycle boundary.
- Use the band activity filters to reduce noise: `CQ`, `POTA`, and `DX` filters are optional, while messages addressed to you remain visible.
- Select a decode to track its audio offset.
- Use the QSO rail and TX templates deliberately; the app no longer relies on a single opaque "suggested" action.
- Use `Stage` to prepare a message and `Arm This` only when you intend it to transmit on the next valid cycle.
- Keep an eye on the clock discipline display. FT8/FT4 timing depends on accurate UTC. ShackStack can use SNTP as its timing reference even when Windows Time is poor.

WSPR is receive-oriented for now. It is useful for propagation monitoring rather than interactive QSOs.

## JS8 Desk

JS8 supports conversational weak-signal text and heartbeat-style operation.

- Select JS8 speed: Normal, Fast, Turbo, or Slow.
- Use Normal unless you have a reason to move.
- Heartbeats should preserve `@HB`; ShackStack allows directed heartbeat transmit such as `W8STR: @HB HEARTBEAT EM79`.
- Use the TX offset control when you want to transmit away from the receive trace.
- Treat JS8 as conversational, but still timing-sensitive.

## SSTV Desk

The SSTV desk is split between receive and transmit workflows.

Receive:

- Auto-detect is the normal default.
- Lock a mode only when you already know the incoming mode or when auto-start misses a late/weak start.
- Live image preview updates during receive.
- FSKID, when present, can capture the other station's callsign.
- The decoder should stop and save when the signal disappears for enough lines rather than endlessly decoding static.

Transmit:

- Choose the SSTV mode.
- Add image/text/template elements.
- Drag text overlays directly on the image.
- Use `%mycall` for your callsign and `%tocall` for the last decoded FSKID callsign.
- Enable FSKID when you want MMSSTV-style callsign metadata in addition to visible text.
- Generated image/audio artifacts are temporary transmit products; they should not be treated as permanent archive unless explicitly saved elsewhere.

## WeFAX Desk

WeFAX is receive-only.

- Use the schedule panel to see upcoming or active transmissions.
- Tune the radio to the published frequency adjusted for the expected audio offset.
- Start RX before or during a fax transmission.
- Use slant, offset, and cleanup controls to correct live images.
- Archive review is available for received captures.

No WeFAX transmit workflow is planned.

## RTTY Desk

RTTY receive uses a fldigi-derived sidecar and expects hand tuning.

- Use USB-D/LSB-D rather than the radio's native RTTY mode when CI-V/audio control behaves badly in RTTY mode.
- Start with `45.45 baud / 170 Hz shift`.
- Tune until the mark/space tones sit around the expected audio center.
- Use the tune helper to estimate the visible tone pair.
- Reverse mark/space polarity if output looks close but wrong.
- Some idle RTTY decoders print junk when no signal is present; compare only during a real transmission.

## Keyboard Modes Desk

Keyboard modes currently focus on PSK31/PSK63.

- RX and TX scaffolding exists.
- Synthetic TX audio works.
- Live PSK receive still needs more real-signal validation.
- Treat this desk as in-progress rather than a finished operating workflow.

## CW Desk

CW is experimental.

- The UI exposes useful operator controls, but decode quality is not yet comparable to a skilled CW decoder.
- Treat decoded callsigns and copied text as hints.
- Confirm by ear before logging.

## FreeDV Desk

FreeDV is the HF digital voice desk. RADEV1 is the modern default; legacy Codec2 modes remain available for older stations.

Receive:

- Select `RADEV1` unless you know the other station is using a legacy mode.
- Choose a FreeDV speech output device. Decoded speech has its own output path and volume.
- Raw radio audio still follows the normal RX monitor slider. If you do not want to hear modem noise, turn the normal RX monitor down and leave FreeDV speech output up.
- `Start RX` captures radio audio, feeds the FreeDV sidecar, and plays only decoded speech on the FreeDV speech output.

Transmit:

- Use `PTT` as the FreeDV transmit toggle.
- Mic audio goes into the codec.
- Modem TX is the encoded audio actually sent to the radio.
- RADEV1 transmit appends end-of-over callsign metadata when available.

### FreeDV Reporter

FreeDV Reporter is the live internet board for FreeDV activity.

Controls:

- `Report me`: allows ShackStack to publish your station to the reporter.
- `RX only`: marks you as listening-only rather than generally available to transmit.
- `Connect`: connects to the reporter server.
- `Disconnect`: disconnects from the reporter server.
- `Send Msg`: updates your free-text reporter status message.
- `Website`: opens the public FreeDV Reporter page.
- `Use Selected For Log`: copies the selected reporter station into the Longwave logging fields.
- `Work Spot`: tunes the radio to the selected reporter station's advertised frequency and mode.

When `Report me` is checked and Reporter is connected, ShackStack sends:

- Your callsign.
- Your grid square.
- ShackStack software identity.
- Current FreeDV frequency when ShackStack knows it.
- Selected FreeDV mode, normally `RADEV1`.
- TX/RX state changes.
- Your optional status message when you press `Send Msg`.
- Received callsign reports from RADEV1 metadata when available.

Normal Reporter workflow:

1. Check `Report me`.
2. Click `Connect`.
3. Tune or use `Work Spot`.
4. Start FreeDV RX.
5. Use `Send Msg` only when you want to update your public status text.

Working a station from Reporter:

1. Select the station in the Reporter list.
2. Click `Work Spot`.
3. ShackStack copies the station's advertised mode, tunes the radio to the advertised frequency, and selects USB-D above 10 MHz or LSB-D below 10 MHz.
4. Click `Start RX`.
5. Use `PTT` if you want to reply.

If the radio is not connected, `Work Spot` still loads the status guidance so you can tune manually.

## Longwave Desk

Longwave is ShackStack's logging and spot-integration area.

- Select the target logbook.
- Stage contacts from the current rig state or from mode desks.
- Use callsign lookup where available.
- Review recent contacts synced from the Longwave server.
- QRZ upload results are surfaced from the server; subscription/API errors should appear as readable messages.

The Longwave server is external by design. ShackStack talks to it as a client and should not become the server administration surface by default.

## Shutdown

The normal shutdown path should release decoder workers and the CI-V COM port.

Recommended smoke test after major changes:

1. Open ShackStack.
2. Open each desk.
3. Start and stop RX where safe.
4. Close each desk.
5. Close the app.
6. Confirm no `ShackStack` or `.NET host` worker zombies remain.

## Operating Notes

- If a mode has a dedicated desk, prefer the desk over main-window controls.
- If digital decode fails, first verify radio mode, audio device, audio level, and passband placement.
- If transmit fails or instantly drops, suspect PTT/radio state first, then TX audio routing.
- If a decoder seems to work in synthetic tests but not live RF, save a short WAV capture. Real audio captures are the fastest way to separate bad propagation from bad code.
- ShackStack is intentionally source-backed where possible: WSJT-X/JS8, fldigi, MMSSTV, Codec2/FreeDV, and WeFAX reference paths are preferred over invented DSP.
