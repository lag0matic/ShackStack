# Raspberry Pi Radio Bridge Experiment

Started: 2026-10-16. Branch: `codex/pi-radio-bridge`.

Connect the IC-7300 by USB to a Raspberry Pi and operate it from ShackStack on Windows over the home's wired gigabit network. Keep decoding, the operating desks, and local headset/microphone handling on Windows. First establish reliable receive audio and radio state; remote transmission comes after measured timing and disconnect behavior.

## Starting Point

The branch contains the experiment plan and tracking entry. Network transports and Pi software have not yet been implemented or tested. No radio has been contacted and no Pi has been provisioned.

The existing direct USB connection is the comparison baseline. An eventual network connection must be an explicit alternative, with no automatic selection of an unrelated local audio device on failure.

```text
IC-7300 -- USB -- Raspberry Pi -- wired Ethernet -- Windows ShackStack
                       |                               |
                  serial and audio                desks and decoders
                                                  headset and microphone
```

## Bandwidth and Timing

Proposed initial audio format: mono PCM16 at 48,000 samples per second, provided the radio capture endpoint supports it. Confirm the actual device formats on the Pi before fixing the transport contract.

One direction uses 768,000 bits per second, or 96,000 bytes per second, before framing overhead. Simultaneous receive and transmit would use about 1.536 Mbps before overhead, plus radio control and scope traffic. This is modest on wired gigabit Ethernet.

Bandwidth alone does not establish quality. Measure jitter, buffer occupancy, capture/playback clock drift, dropped audio, and receive timing. The current AudioBuffer contains samples, rate, and channels but no capture timestamp or sequence; a network packet must carry enough information to detect gaps and place audio on its original timeline.

For timed digital modes, account for network and playback delay instead of assigning delayed audio the PC's arrival time. Establish how the Pi and PC clocks relate and how time corrections affect an active session.

## Existing Integration Boundaries

| Component | Current implementation | Proposed boundary |
| --- | --- | --- |
| Radio connection | `src/ShackStack.Infrastructure.Radio/Civ/CivConnection.cs` owns System.IO.Ports.SerialPort and its reader/writer loops. | Retain CI-V parsing/dispatch where appropriate; place local serial or network communication behind a transport boundary. |
| Connection options | `src/ShackStack.Core.Abstractions/Models/RadioConnectionOptions.cs` contains port name, baud rate, and radio address. | Add an explicit connection type and network endpoint rather than overloading the COM-port field. |
| Radio session | `src/ShackStack.Infrastructure.Radio/Civ/CivSession.cs` opens the connection and forwards commands. | Keep connection lifecycle and pending-command failures consistent across transports. |
| Scope stream | `IRadioService.ScopeRowStream` is populated from CI-V scope frames. | Preserve full scope traffic; a basic frequency/PTT-only proxy is insufficient for feature parity. |
| Audio | `src/ShackStack.Infrastructure.Audio/AudioService.cs` implements IAudioService using Windows capture/playback. | Separate remote radio audio from local headset/microphone functions; do not move Windows audio code to the Pi. |
| Decoder input | `IAudioService.ReceiveStream` publishes AudioBuffer values. | Feed correctly timed remote samples through the established decoder input after validation. |
| Composition | `src/ShackStack.Desktop/Bootstrap/ServiceCollectionExtensions.cs` selects concrete services. | Select local/network implementations from explicit settings once the standalone transport works. |

## Choose the Bridge Before Implementing a Protocol

Investigate [wfview's server](https://wfview.org/wfview-user-manual/remote-operation-server/) first. It documents USB-connected radios served from a Raspberry Pi with control and full-duplex audio. Its client protocol is not automatically a raw CI-V TCP stream. Verify authentication/session handling, audio formats, CI-V access, scope forwarding, Linux deployment requirements, and license terms before choosing an adapter.

If its protocol can preserve our controls, scope, and decoder-quality audio with reasonable effort, reuse it. Otherwise, prototype a small dedicated bridge with bounded queues and explicit session ownership. Record the source/protocol evidence and decision here before substantial implementation. Do not confuse a plain serial-over-TCP bridge with wfview compatibility.

A USB-over-network shortcut also needs evidence that continuous USB audio behaves correctly; do not assume a remotely enumerated device is equivalent to a reliable audio transport.

## First Hardware Milestone

1. Identify the available Pi model, OS, USB serial endpoint, USB audio capture/playback formats, and Ethernet address. Use stable device identities where possible.
2. Capture a short radio recording locally on the Pi and verify channel mapping, levels, and duration.
3. Prove network receive audio with a standalone Windows receiver before connecting the decoder pipeline. Record audio and timing statistics.
4. Read frequency/mode and forward scope data. Manually tune the front panel and confirm updates on Windows. Initial testing must exclude PTT, keying, tuner activation, and transmit-audio playback. Review connection-initialization commands before using the live rig.
5. Add receive-only network service selection in ShackStack. Verify listening, scope behavior, and a known non-CW decoder against the local USB baseline.
6. Disconnect Ethernet, restart the bridge, stop the radio, and switch back to local USB. Verify bounded recovery, accurate status, and no UI hangs or replayed stale commands/audio.

Acceptance: a continuous 30-minute wired receive run with measured audio continuity, bounded buffers, responsive front-panel state updates, usable scope, and recovery from deliberately interrupted connections. Report observed latency and failures rather than declaring success from connection establishment alone. Hardware and timing results determine whether a transmit milestone is justified.

## Later Transmit Milestone

The bridge must own a transmit lease and locally stop audio/unkey when the client disconnects or its heartbeat expires. A timeout in the Windows UI alone cannot release a disconnected radio. If the Pi itself crashes, software on the Pi cannot enforce that timeout; verify and use a radio-side transmit timeout where available.

Coordinate PTT with actual playback progress and clear queued samples on abort, including VOX. Preserve confirmed state across failed unkey commands and reconnects. Resolve relevant transmit ownership and lifecycle findings in CODE_REVIEW_2026-09-16.md before extending those paths over the network.

First exercise transmit behavior with simulated devices and recorded waveforms. Live tests require controlled operation and appropriate load. Timed digital modes need playback scheduling and clock/latency verification; voice tolerance does not establish FT8 or FT4 timing correctness.

## Work List

- [x] Create isolated experiment branch.
- [x] Inspect current serial, audio, scope, and dependency-injection boundaries.
- [x] Record wired-network assumptions and receive milestone.
- [ ] Decide wfview adapter versus dedicated bridge from protocol evidence.
- [ ] Inventory Pi hardware and USB device formats.
- [ ] Build standalone receive transport and fault-injection tests.
- [ ] Add explicit receive-only network selection to ShackStack.
- [ ] Complete the 30-minute hardware run and compare with direct USB.
- [ ] Design and verify remote transmit lifecycle and scheduling.

Existing CW work remains shelved. This experiment does not require moving decoder workers to Linux or retraining models.
