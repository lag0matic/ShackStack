# Nexus and ShackStack Workflow Comparison Plan

Date: 2026-10-19. Nexus source snapshot: `b27cfb7be1dc5b035514d426b15553807b667c0f`. ShackStack baseline: `79897a8` plus the existing Pi experiment documentation.

Improve the operating workflows that matter to the user while retaining Longwave logging and ShackStack's useful desks. Compare task completion and decoder output before selecting changes. The user's successful trial of Nexus, particularly its CW copy, is a strong reason to evaluate its components on our recordings.

The previous preference not to run Nexus is superseded by that trial. CW is reopened for comparison and a bounded pretrained-model experiment; this does not restart synthetic model training. The separate Pi bridge experiment remains available.

## Initial Findings

| Area | ShackStack today | Nexus evidence | Proposed decision |
| --- | --- | --- | --- |
| Operating layout | Separate desk windows share a large view model; control placement and logging presentation vary. | Phone, CW, and digital cockpit sources reuse tuning, transmit, scope, history, and panel components. | Evaluate a consistent operating structure in one desk before changing navigation throughout the app. |
| QRZ lookup | Callsign lookup goes through Longwave's `lookups/qrz` endpoint; the UI exposes name, QTH, county, grid, state, country, and DXCC values. | Direct QRZ XML transport exists; the manual documents automatic callbook lookup and an explicit Lookup button. | Both have lookup. Measure discoverability and field handling rather than treating QRZ as a missing Nexus feature. |
| Logging ownership | Longwave manages logbooks, contacts, and QRZ upload state. | Nexus has its own logbook and multiple connectors. | Preserve Longwave as our authoritative logging service. Copy useful interaction patterns without creating a competing local logbook. |
| CW | Existing conventional decoding, experimental lanes, recorder, and our duration-model second pass. | Pretrained e04 DeepCW character model plus a separate conventional decoder/skimmer. User reports outstanding live copy. | Highest-priority functional experiment: replay saved audio with the upstream model before UI integration. |
| Weak-signal operation | Mode catalog and worker-based decode; FT8/FT4 TX are declared supported, many other entries RX-only. | Operate cockpit includes roster, QSO strip, lookup, split, recording, and scope components. | Compare a complete FT8 contact and recovery paths. A catalog entry alone does not establish equivalent capability. |
| Tempo modes | No Tempo implementation. | TempoFast and TempoDeep are documented, with native modem integration. | Separate feasibility study after CW evidence; reuse the existing modem if warranted. |
| Failure feedback | Review identified generic statuses, recovery defects, and optimistic transmit state. | Specific CAT diagnostics and failure-oriented radio tests. | Make operational state reliable before connecting new screen actions to transmit. |
| Additional features | Dedicated FreeDV, WeFAX, SSTV, keyboard-mode, voice/POTA, and Longwave workflows. | Wider feature list and configurable panels. | Compare the workflows we use. Feature count is not the adoption criterion. |

These findings come from source/manual inspection and the user's trial. Rendered layouts, click counts, radio behavior, and decoder accuracy still need matched tests. The installed Nexus version may differ from this source snapshot; record its version in the visual comparison.

## Phase 1 Compare Real Operating Tasks

Use the same station identity, radio, audio route, window dimensions, Windows scaling, and representative contacts. Only one application should own the radio during a run. Work from recordings where possible; review configuration changes before swapping applications on the live rig.

For every task, record the starting state, screenshots before and after, actions and keystrokes, elapsed time, mistakes, hidden controls, and where the operator's attention must move. Separate first-use discovery from repeated operation. Source inspection can suggest a better layout but cannot supply measured click counts or pixel-level conclusions.

| Task | Comparison questions | Completion criterion |
| --- | --- | --- |
| Start listening | Is radio/audio readiness obvious? Which screen or action starts reception? Does entering a mode retune the rig? | Hear expected audio, see current frequency/mode and appropriate spectrum, understand RX/TX state. |
| Voice POTA hunt | Filter spots, pick station, tune, read park/callsign, fetch details, enter report, log, move on. Does context survive lookup and desk changes? | Correct contact saved to the chosen Longwave logbook once; return to hunting with minimal re-entry. |
| QRZ and history | Find Lookup, understand credentials/subscription failures, retain manually entered values, see previous contacts. Try a slow lookup followed by a changed callsign. | Apply data only to the intended contact, preserve intentional edits, distinguish lookup failure from no result. |
| FT8 contact | Find activity, select call, understand RX/TX offsets and period, follow exchange, stop, log, verify submission. | No ambiguity about whom we are working, when TX can occur, and whether the contact was saved. |
| CW listening | Tune/select one signal, read stable copy, adjust pitch/bandwidth only when needed, clear/retune/restart. | Useful transcript stays readable; stale results do not appear under another signal. |
| Crowded CW | Select a lane, follow identity through fades, inspect neighboring copy, return to main signal. | Main transcript remains stable without false confidence or silent lane reassignment. |
| Logbook correction | Search, select, edit UTC/date/grid/report, save, inspect upload state. Test service failure. | No duplicate creation, shifted timestamp, stale fields, or false success. |
| Mode change and window recovery | Move voice to FT8 to CW and back; close/reopen; resize and use keyboard navigation. | Preserve intended form/layout state and make rig mode changes explicit and predictable. |
| Interrupted operation | Disconnect audio/radio/network or terminate a worker during receive; exercise simulated TX failure separately. | Status identifies cause and recovery; UI remains responsive and no old audio/commands replay. |

Deliver a workflow matrix with evidence per row and concrete proposed changes. Choose the two or three operating tasks with the largest benefit rather than redesigning every desk at once.

## Phase 2 Adapt the UI Patterns That Win

### Visual Review of Published Screens

Reviewed the published Phone, CW, and Classic FT screenshots at the pinned source revision, alongside ShackStack's current Voice, Weak-Signal, and CW Experiment view definitions and OperatorPanelStyles. The screenshots themselves display Nexus v0.29.0; their presence in the newer repository does not establish that they match the user's installed version. A matched current-version screenshot comparison remains part of Phase 1.

The useful distinction is between organization and styling. Nexus's published screens establish an operating sequence visually: radio state and tuning, spectrum, then activity and the contact being worked. The dark blue surfaces, cyan highlights, numerous badges, and miniature controls are separate choices. They do not explain the workflow advantage and need not travel with it. Appearance alone does not establish which authoring tool produced a design.

| Observation | Nexus published screens | ShackStack current views | Proposed adaptation |
| --- | --- | --- | --- |
| Essential state stays near the work | Frequency, mode, band, and transmit controls appear within the cockpit rather than only elsewhere in the application. | Voice starts with a desk title and explanatory sentence, then separate audio, mic, transmit, spots, and logging groups. Its initial layout does not provide the same complete tuning strip. | Put a compact live station strip in every operating desk. Keep its ordering consistent and bind it to observed state. |
| Spectrum provides a common visual anchor | Phone and CW give the scope a horizontal band above the main activity/contact work area. | Our desk layouts differ, and the experimental CW surface prioritizes lane settings and text diagnostics. | Use a predictable scope position when relevant; give it resize/collapse controls instead of a decorative fixed frame. |
| Activity and contact form are adjacent | Phone and CW place activity/copy on the left and the current log entry on the right. | Voice already combines spots and logging, which is a strength, but the left column spends significant vertical space on controls. | Preserve the hunt-and-log combination; keep the chosen call, lookup/history, reports, and save result in a stable contact area. |
| Operation and engineering have different prominence | The CW screenshot centers copy and logging; speed, keyer, and pitch are compact controls above. | CW Experiment exposes search bounds, lane spacing/count, worker state, recording controls, provisional copy, neural copy, and diagnostics together. | Preserve the experiment as a diagnostic tool. A production CW view should foreground selected signal/copy, pitch, decoder health, and contact entry; expose advanced lane/recorder details on demand. |
| Several Nexus controls repeat their meaning | FT appears in navigation, the top mode selector, and a large in-screen mode control; theme controls and status badges occupy additional rows. | ShackStack also repeats desk titles and operational/status text, and uses helper sentences that explain why a panel exists. | One location for each persistent setting/status when possible. Remove development narration and redundant headers before adding another toolbar. |
| Small controls reduce the benefit of density | Published screenshots contain dense rows of memory shortcuts, status chips, tiny labels, and several nested outlines. | Weak-signal rows explicitly use 8-point badge text and 10-point data/labels, with several fixed-width columns. | Prefer fewer simultaneously visible controls, legible text, resizable columns, and a clear selection state. Do not gain rows by shrinking text indefinitely. |
| Color competes for attention | Cyan appears on navigation, frequency, switches, buttons, and modes; many additional status colors appear together. | Our current neutral black surfaces are closer to the user's stated preference; colored decode states and selected rows still need consistency. | Keep neutral surfaces. Use accent for active selection and semantic colors for meaningful radio/message states. Never rely on color alone. |
| Panel boundaries can become visual noise | The screenshots frame many regions with rounded outlines and use pill treatments for numerous controls/statuses. | Shared operator styles use modest radii, but views still contain nested frames and pill-shaped mode badges. | Use spacing, aligned columns, and subtle separators for sections. Reserve frames for genuinely bounded tools and avoid badges on every ordinary value. |
| Single-window organization has tradeoffs | Navigation and common station context stay in one shell. | Separate desks can suit multiple monitors, but context must remain visible and consistent in each. | Do not assume a single-window rewrite is necessary. First improve the common station/contact structure while retaining useful independent desks. |

For the first prototype, the proposed arrangement is: compact station strip at the top; scope immediately below; spots/activity beside contact/lookup/history in the main area; a consistent stop/transmit region that stays visible; secondary audio/DSP configuration in a collapsible area. Demonstrate this with a reviewable mockup before changing live command paths. The default must be useful without arranging panels.

Treat "obviously AI" as the user's description of the visual impression, not a quality verdict or a measurable property. Review concrete causes: repeated pills, excess status chrome, explanatory filler, uniformly framed sections, weak contrast, and too many equally prominent controls. Some occur in our views too. Good grouping and dense radio information are worth keeping even if their current skin is not.

Screenshot references:

- [Published Phone cockpit](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/docs/img/manual/phone-cockpit.webp)
- [Published CW cockpit](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/docs/img/manual/cw-cockpit.webp)
- [Published Classic FT cockpit](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/docs/img/manual/operate-classic.webp)

### Prototype Scope

First candidate: Voice/POTA, where our combined spots and Longwave flow is already useful. If the measured FT8 workflow has a clearer improvement, select that desk instead and record why.

Proposed structure to test:

- A compact, consistent station strip: live frequency/mode, selected band/preset, RX/TX state, audio/radio availability, and an obvious stop action.
- Scope/waterfall in the main working area with the selected frequency or audio target visible.
- Activity or spots next to the current contact, with callsign, lookup/history, report, notes, and save outcome kept together.
- Advanced DSP/setup controls behind an appropriate secondary surface. Keep frequently adjusted controls immediately available.
- Reusable log-entry and station-status components across desks, implemented in Avalonia and backed by narrowly owned services where needed.
- Persistent panel sizes and collapse states only where they reduce repeated setup. Start with a predictable layout rather than implementing a general drag-and-drop workspace.

Keep the dark neutral palette the user preferred. Evaluate desktop sizes and 100/150/200 percent scaling, keyboard access, focus behavior, long callsigns, lookup errors, and disconnected states. Preserve separate desks unless a matched comparison demonstrates that another navigation arrangement is better.

Before changing affected logging/transmit paths, resolve the relevant date parsing, duplicate submission, request ordering, and transmit ownership findings in [the review](CODE_REVIEW_2026-09-16.md). A clearer control must not make an unreliable action easier to trigger.

Deliver one reviewable desk prototype and before/after workflow evidence. Expand proven components gradually; no framework migration is needed.

## Phase 3 Evaluate DeepCW on Our Audio

### What We Can Reuse

The inspected Nexus wrapper uses e04's pretrained ONNX model. It converts mono audio to a spectrogram and obtains character probabilities, followed by CTC text decoding. It does not accept our provisional text or guessed WPM as model inputs.

Current model metadata specifies 3200 Hz audio, a 256-sample FFT, a 48-sample hop, and 65 frequency bins covering 400-1200 Hz. Nexus processes overlapping 15-second windows at a nominal two-second cadence with a one-second tail guard and timestamp stitching. Its inspected neural path remains separate from the conventional skimmer.

Use the upstream reference path first. If the model proves useful, evaluate C# ONNX Runtime with shared preprocessing fixtures. That could avoid a production PyTorch/Python requirement; runtime compatibility, performance, and native dependency packaging must be demonstrated. Nexus's tract-specific fixed-shape model transformation should not be copied without a runtime need.

### Benchmark Sequence

1. Inventory saved sessions and pin the model/reference code revisions, checksums, input files, crop times, and preprocessing parameters.
2. Choose clean W1AW, 20-to-15-to-slower speed changes, crowded contest signals, fading/weak audio, adjacent interference, steady PSU tones, and noise-only intervals. Keep an untouched selection for final acceptance.
3. Begin with single isolated signals at supported pitches. Establish reference transcriptions for selected intervals, with uncertain characters marked; old decoder output is not ground truth. Report character and word errors only where a checked reference exists.
4. Run the same audio through our baseline and DeepCW. Measure character error, spacing, callsigns, noise false-copy rate, latency, processing time, and memory. Distinguish what the user observed live from what the matched benchmark establishes.
5. Demonstrate repeatable improvement on checked real recordings, tolerable latency, and no material increase in noise-only copy before adopting the backend. Record numeric acceptance thresholds before evaluating the untouched selection.
6. Compare the reference output with any C# adapter on identical prepared samples. Establish preprocessing/tensor parity before tuning behavior.
7. Integrate one selected signal first. Fix our lane audio-continuity/preprocessing issues before feeding DeepCW live lane buffers. Test resets, retunes, cancellation, speed changes, and window stitching.
8. Extend to a small number of lanes only after single-signal operation is useful. Filter and frequency-translate each selected lane into the supported band before resampling, preserve quiet intervals, and measure CPU cost as lanes increase.

Present conventional and neural copy as independent results. Do not silently rewrite one with the other or pretend a model consumes hints it does not accept. Avoid displaying probability as guaranteed transcription accuracy.

Deliver a scorecard with input manifest and outputs, then a small live integration if the benchmark supports it. This work requires no new overnight training run.

## Phase 4 Assess Tempo as a Separate Mode

Yes: Nexus has its own experimental weak-signal waveform family. At this snapshot its manual describes:

| Mode | Documented properties | Planning implication |
| --- | --- | --- |
| TempoFast | Four-second slots; coherent 4-CPM; incremental-redundancy HARQ, combining extra error-correction information across retransmissions. | Faster conversational exchanges, with sensitivity and timing tradeoffs. Validate acquisition and retransmission behavior together. |
| TempoDeep | Fifteen-second slots; approximately 50 Hz non-coherent 8-FSK; no IR-HARQ in the documented chat path. | Separate waveform intended for fading conditions; needs its own reference tests. |

The documentation labels sensitivity/fading figures as simulations and reports initial on-air contacts. Do not present those figures as our measured performance or general superiority over FT8/JS8. Shared message structures do not make the waveforms decodable by stock FT8/FT4/JS8 software; verify exact interoperability from the implementation and reference tests.

Before adopting:

1. Identify the actual native entry points, dependencies, platform builds, mode/version identifiers, timing rules, and reference fixtures. Check for drift between manuals and code comments.
2. Inspect acquisition, framing, message packing, retransmission combining, sequencing, and text reassembly as one protocol contract. A waveform generator alone does not implement the mode.
3. The current `tempo-fast` wrapper documents process-global native state protected by a mutex. Respect that serialization; verify whether it covers every entry point and test multiple decoder instances or isolate workers.
4. Try a worker adapter around the existing modem rather than porting its DSP into C#. Require cross-decode against a pinned Nexus reference, noise tests, timing-offset tests, restart recovery, and waveform comparisons.
5. Establish who we will communicate with: the user needs a compatible peer or an arranged second station to make this useful. A successful local loopback is insufficient.
6. Rank its practical value against improving our existing FT8/FT4/JS8 operation. Keep Tempo behind an experimental label until independent interoperability is demonstrated.

Deliver a reuse/dependency estimate and go/no-go recommendation. Importing a new protocol is materially larger than adding a pretrained CW inference backend.

## Adoption Order

| Priority | Work | Relative scope | Gate |
| --- | --- | --- | --- |
| 1 | Matched workflow comparison and DeepCW offline replay | Bounded investigation; model/reference setup required | Actionable UI evidence and a checked decode comparison. |
| 2 | Relevant logbook/transmit reliability fixes | Medium, with focused regressions | No known data-integrity or transmit-state regression in the paths being reused. |
| 3 | One desk prototype and common contact/lookup behavior | Medium | User finds it easier on the actual operating tasks. |
| 4 | Single-signal live DeepCW, then selected lanes | Medium; multi-lane scheduling can grow | Offline benefit survives live input, retunes, resets, and clean installer testing. |
| 5 | Independent diagnostics and waterfall/history improvements | Small to medium per item | Measurable operating benefit; stale data clearly distinguished. |
| 6 | Tempo modem integration | Large and dependency-sensitive | Reproducible reference compatibility and a useful peer test. |

No calendar estimates until the model setup, UI evidence, and native modem build have been checked. The Pi bridge remains a separate experiment rather than a prerequisite for any of these tasks.

## Branches and Reuse Records

Keep this planning document independent of implementation. The working checkout currently contains the Pi documentation on `codex/pi-radio-bridge`; do not include it accidentally in a UI or CW implementation commit. Start later implementation on dedicated branches from a deliberately selected baseline, preserving all existing changes.

For copied code or distributed model weights, record upstream source, revision, file notices, license, modifications, and required attribution. Nexus advertises GPL-3.0; DeepCW is separately labeled AGPL-3.0-only. Check each reused component instead of treating the repository banner as permission for every dependency. A model download or separate process does not by itself settle license obligations.

## Sources and Evidence

Nexus links below are pinned to the inspected snapshot, so later changes do not silently alter the comparison.

- [Phone cockpit source](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/ui/src/components/PhoneCockpit.tsx)
- [CW cockpit source](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/ui/src/components/CwCockpit.tsx)
- [Digital operate source](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/ui/src/components/OperateCockpit.tsx)
- [QRZ transport](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/crates/propagation/src/live/qrz.rs)
- [Logbook and QRZ workflows](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/docs/manual/Logbook-and-Awards.md)
- [DeepCW inference wrapper](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/crates/deepcw/src/lib.rs)
- [Live DeepCW scheduling](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/crates/tempo-audio/src/aicw.rs)
- [DeepCW model and reference implementation](https://github.com/e04/deepcw-engine)
- [ONNX Runtime C# integration documentation](https://onnxruntime.ai/docs/get-started/with-csharp.html)
- [Tempo chat behavior](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/docs/manual/Tempo-Chat.md)
- [TempoFast protocol introduction](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/docs/protocols/tempofast.md)
- [TempoDeep protocol introduction](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/docs/protocols/tempodeep.md)
- [Native modem wrapper and serialization notes](https://github.com/kd9taw/Nexus/blob/b27cfb7be1dc5b035514d426b15553807b667c0f/crates/tempo-fast/src/lib.rs)

ShackStack implementation anchors: `src/ShackStack.UI/Views/VoiceDeskWindow.axaml`, `WsjtxDeskWindow.axaml`, `CwExperimentDeskWindow.axaml`; `src/ShackStack.UI/ViewModels/MainWindowViewModel.Longwave.cs`; `src/ShackStack.Infrastructure.Interop/Longwave/LongwaveService.cs`; `src/ShackStack.Core.Abstractions/Models/WsjtxModeCatalog.cs`.

Completed for this plan: source-backed initial matrix, published-screenshot visual review, task script, CW experiment sequence, Tempo feasibility scope, and adoption order. Pending: matched current-version screenshots and interactive workflow measurements, replay measurements, prototypes, and runtime interoperability tests. No application behavior changed.
