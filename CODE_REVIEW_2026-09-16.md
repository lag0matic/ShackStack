# ShackStack Senior Engineering Review

Date: 2026-09-16. Baseline: `main`, commit `e587e0b` plus the existing uncommitted preset and view-model changes.

## Executive assessment

The highest-priority problems are transmit-state races, an audio shutdown deadlock, and logbook/settings data integrity. The CW experiment also has several pipeline and evaluation defects that should be corrected before treating another training run as evidence of better transcription. These are specific repairable problems, not a recommendation to rewrite the application.

This was a read-only application review, with five specialist reviews and targeted verification. No application fixes were made. The only new file is this report. Existing modifications to ModeFrequencyCatalog.cs, WsjtxModeCatalog.cs, and MainWindowViewModel.cs were preserved.

### Severity and evidence

- **P1:** Address before the next release or before relying on the affected operation. Potential unintended transmission, hangs, data loss, or a dead decoder path.
- **P2:** Important correctness, resilience, security, or workflow problems. Address in focused follow-up work.
- **P3:** Polish, maintainability, portability, or latent issues outside the normal current workflow.
- **Reproduced:** A focused local probe or existing test demonstrated the behavior. This does not mean it was reproduced on connected radio hardware.
- **Source-traced:** The implementation contains the failure path; the full live scenario was not exercised.

Locations are repository-relative, with line numbers from this review snapshot. They are starting points, not permanent identifiers.

## P1: Release and operational priorities

### 01. A quick PTT release can be lost

**Location:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.Radio.cs:343`.
**Evidence:** Source-traced. The release path compares the requested state with cached `PttState`. If a press is still awaiting startup and the cached state remains RX, release returns early. The pending press can subsequently key the radio without a remaining release request.
**Action:** Track desired PTT state independently of observed state; serialize transitions and invalidate pending presses on release. Release must always be able to unkey a pending/current transmission.
**Verify:** Delay the fake radio/audio startup, issue press then release, and assert the final radio state is RX with no late key-on.

### 02. Audio shutdown can deadlock

**Locations:** `src/ShackStack.Infrastructure.Audio/AudioService.cs:567`, `:628`; callbacks at `:428`, `:485`.
**Evidence:** Source-traced against both application and dependency implementation. Cleanup holds `_sync` while disposing capture. An already-running audio callback can be waiting for that same lock. NAudio disposal joins the capture thread, creating a circular wait. Unsubscribing the event does not stop an already-running callback. A timeout around a method that blocks before returning its task cannot recover this.
**Action:** Detach and transfer capture ownership under the lock, then stop/dispose outside it. Coordinate completion explicitly.
**Verify:** Repeated stop/reset/device changes while deliberately pausing a callback before lock acquisition.
**Dependency reference:** [NAudio 2.2.1 WasapiCapture disposal and callback implementation](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/WasapiCapture.cs#L291).

### 03. Unkey success is assumed, not confirmed

**Locations:** `src/ShackStack.Infrastructure.Radio/Icom/IcomCivCommands.cs:436`; `src/ShackStack.Infrastructure.Radio/RadioService.cs:240`.
**Evidence:** Source-traced. PTT uses a command without a reply matcher; the service immediately records the requested state after writing. A rejected or lost unkey command can display RX before readback corrects it, or leave the rig keyed during disconnect.
**Action:** Confirm PTT state, use bounded retries for unkey, expose an unresolved failure, and complete unkey verification before closing the connection where possible.
**Verify:** Inject dropped writes/rejected commands and stale readback; never report confirmed RX solely from successful serial output.

### 04. Transmit ownership is not coordinated across modes

**Locations:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.Sstv.cs:599`; `MainWindowViewModel.DigitalModes.cs:657`; `src/ShackStack.Infrastructure.Audio/AudioService.cs:175`.
**Evidence:** Source-traced. Per-mode flags do not protect a shared radio/audio transmitter. Starting SSTV while another mode is transmitting can retune the keyed rig and replace its audio; cleanup from the earlier mode can then stop or unkey the newer transmission.
**Action:** Introduce one transmit coordinator with an owner/token, mutually exclusive acquisition, cancellation, and owner-scoped cleanup.
**Verify:** Attempt every pair of overlapping transmit modes; the second must be rejected or explicitly cancel and await the first.

### 05. PSK transmission can proceed after tuning fails

**Locations:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.DigitalModes.cs:1445`, callers around `:611` and `:623`.
**Evidence:** Source-traced. Tuning/mode helpers catch failures and return normally. The transmit path continues into audio/PTT, potentially using the previous frequency or mode.
**Action:** Use a strict transmit preparation path that propagates failure and verifies prerequisites before keying.
**Verify:** Failed frequency and mode commands must each result in zero PTT-on calls.

### 06. UTC logbook dates can move backward by a day

**Location:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.Longwave.cs:1217`; create/update callers at `:595` and `:676`.
**Evidence:** Reproduced. `AssumeUniversal` without preserving UTC converts midnight to local time; taking `.Date` and then labeling it UTC loses a day in Indiana. Input `20260916 120000 UTC` produced `20260915 120000 Z` using the application's parsing logic. Repeated edits can repeat the damage.
**Action:** Parse date/time explicitly as UTC, or combine validated DateOnly/TimeOnly values into a UTC timestamp. Also reject invalid nonempty input instead of silently replacing it with the current time.
**Verify:** Round-trip create/edit across positive and negative UTC offsets, midnight, DST boundaries, and invalid inputs without changing the contact instant.

### 07. An interrupted settings save can prevent startup

**Location:** `src/ShackStack.Infrastructure.Configuration/JsonAppSettingsStore.cs:62`; load at `:39`.
**Evidence:** Source-traced. Save truncates the only settings file before serialization completes. Load has no malformed-file recovery, and startup depends on successful loading.
**Action:** Write and flush a temporary file, atomically replace the live file, keep a recoverable backup, and report recovery rather than failing startup outright.
**Verify:** Interrupt saves at multiple points and restart with truncated/invalid JSON; preserve the last valid settings.

### 08. One worker pipe failure can permanently stop audio delivery

**Locations:** `src/ShackStack.Infrastructure.Decoders/DecoderAudioPump.cs:56`; `PythonWsjtxModeHost.cs:632`.
**Evidence:** Reproduced by a review probe. A send exception faults the once-created pump task; subsequent enqueues do not send. The loop catches cancellation, not transport failure. Restarting the worker does not recreate the pump.
**Action:** Couple worker and pump lifecycles, surface transport faults, and recreate delivery safely on restart.
**Verify:** Kill a worker during an audio write, restart it, and confirm later audio is delivered without restarting ShackStack.

### 09. External decoder/waveform processes can hang indefinitely

**Locations:** `src/ShackStack.DecoderHost.GplWsjtx/Jt9ExternalDecoderPort.cs:133`, `Js8ExternalDecoderPort.cs:143`; `src/ShackStack.Infrastructure.Decoders/WsjtxExternalWaveformPort.cs:157`.
**Evidence:** Source-traced. Reading stdout and then stderr sequentially can deadlock when the child's stderr pipe fills. Waiting has no effective deadline; waveform cancellation is checked too late to interrupt a stuck child.
**Action:** Drain both streams concurrently, enforce cancellation/timeouts, and terminate/reap the process tree on failure.
**Verify:** Fake children that fill stderr, never exit, and ignore normal termination must all return a bounded failure.

## P2: Correctness and resilience

### 10. Generic CI-V acknowledgements can complete the wrong request

**Locations:** `src/ShackStack.Infrastructure.Radio/Civ/CivConnection.cs:198`, `CivDispatcher.cs:31`; `Icom/IcomCivCommands.cs:452`.
**Evidence:** Source-traced. Outstanding requests can overlap, while generic ACK matching takes the first matching dictionary entry. An ACK from a prior setter can satisfy a CW request; response identity is insufficiently constrained.
**Action:** Serialize transactions with indistinguishable replies, account for setter replies, and validate source addresses. Test interleaved ACK/NAK responses and retry behavior.

### 11. Editing VFO B forces the radio back to VFO A

**Location:** `src/ShackStack.Infrastructure.Radio/RadioService.cs:134`.
**Evidence:** Source-traced. The implementation selects B, changes frequency, then always selects A, regardless of the originally active VFO. Cached active-VFO state can disagree.
**Action:** Restore the captured active VFO in a finally path or use addressed-VFO commands. Test edits with both A and B initially active.

### 12. Shutdown races fire-and-forget startup

**Locations:** `src/ShackStack.Desktop/App.axaml.cs:56`; `src/ShackStack.Desktop/Bootstrap/AppStartup.cs:67`.
**Evidence:** Source-traced. Startup runs untracked with no application-lifetime cancellation. It can continue starting services/accessing dependencies while close is disposing them.
**Action:** Retain a startup task and lifetime cancellation source; cancel and await startup before teardown. Test immediate close during slow connection establishment.

### 13. Concurrent settings saves can overwrite newer values

**Locations:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.Settings.cs:141`, `:274`.
**Evidence:** Source-traced. Runtime/debounced and explicit saves capture separate snapshots without a shared save gate. Canceling a debounce does not cancel an already-started write; completion can overwrite newer in-memory or on-disk state.
**Action:** Use one save coordinator that merges runtime values into the latest settings and serializes durable writes. Test overlapping explicit and background saves.

### 14. Private-network HTTPS disables certificate authentication

**Locations:** `src/ShackStack.Infrastructure.Interop/Longwave/LongwaveService.cs:25`, `:427`, `:465`.
**Evidence:** Source-traced, not an exploit test. Any certificate error is accepted for loopback/private-IP hosts, while requests carry the API key. A private address does not make an on-path endpoint trustworthy.
**Action:** Use normal certificate validation with a private CA, or an explicit certificate pin/trust decision. Test rejection of an unexpected certificate on a private host.

### 15. FLRig multicall loses its arguments

**Location:** `src/ShackStack.Infrastructure.Interop/Flrig/MulticallParser.cs:22`.
**Evidence:** Reproduced using the compiled parser. A canonical `system.multicall` containing `rig.set_ptt` with an integer argument returns zero parsed arguments. The parser searches for nested `param` elements instead of array data/value entries and loses types.
**Action:** Parse the XML-RPC array structure and preserve typed values. Add canonical getter/setter/mixed multicall fixtures.

### 16. A disconnected FLRig client can terminate request processing

**Location:** `src/ShackStack.Infrastructure.Interop/Flrig/FlrigHttpServer.cs:122`; listener at `:82`.
**Evidence:** Source-traced. Response writing/closing is outside the protected request body. A write failure escapes the awaited handler and faults the listener loop while the server can still appear started.
**Action:** Isolate per-request failures, close responses in finally, and continue listening. Test clients disconnecting before response delivery.

### 17. Successful log creation can be reported as failed and duplicated

**Location:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.Longwave.cs:629`.
**Evidence:** Source-traced. Creation and subsequent refresh share one error path. A refresh failure after a successful POST removes the duplicate guard and reports failure; retry can create another contact.
**Action:** Separate mutation success from refresh failure, retain successful identity, and use server idempotency where supported. Test POST success followed by refresh failure.

### 18. Old logbook responses can overwrite the newly selected logbook

**Location:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.Longwave.cs:1349`.
**Evidence:** Source-traced. Results are applied after awaiting without checking whether account/logbook selection changed. Slow A results can populate the UI under B.
**Action:** Bind response acceptance to a request generation and endpoint/account/logbook identity. Test reversed completion order.

### 19. Disabling FLRig in settings does not stop the running server

**Location:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.Settings.cs:141`.
**Evidence:** Source-traced. Saving updates configuration/display but does not reconcile the live server lifecycle. Existing clients may retain radio control despite the disabled setting.
**Action:** Apply lifecycle changes on save; at minimum make restart requirements explicit, but disabling control should stop the listener immediately. Test enable, disable, and endpoint changes.

### 20. One transient band-conditions error stops future updates

**Location:** `src/ShackStack.Infrastructure.Interop/BandConditions/HamqslBandConditionsService.cs:43`.
**Evidence:** Source-traced. Error handling surrounds the whole polling loop and only handles cancellation. HTTP/XML errors fault the task; timeout cancellation can also end polling with the started flag still set.
**Action:** Handle individual fetch failures, retry with bounded backoff, distinguish application cancellation, and display stale data status. Test recovery after HTTP and malformed XML failures.

### 21. Dropped audio breaks WSJT cycle timing

**Locations:** `src/ShackStack.DecoderHost.GplWsjtx/GenericCycleBuffer.cs:29`; `src/ShackStack.Infrastructure.Decoders/PythonWsjtxModeHost.cs:33`.
**Evidence:** Reproduced by a review probe. A dropped one-second interval produces 15 seconds of concatenated samples spanning 16 seconds of actual time. The queue drops old packets, while the cycle buffer anchors only its initial timestamp and subsequently counts samples.
**Action:** Carry sequence/timing information, detect gaps, and discard/re-align affected cycles or insert correctly timed silence. Test overload recovery without restarting the decoder.

### 22. Worker startup is not serialized

**Location:** `src/ShackStack.Infrastructure.Decoders/DecoderWorkerProcess.cs:37`.
**Evidence:** Source-traced. Concurrent starts can both pass the started check, launch separate processes, and overwrite ownership. An older process exit callback can mark a newer instance stopped.
**Action:** Gate start/stop/dispose and tag process generations. Test concurrent starts and a delayed old-process exit.

### 23. Audio downsampling aliases unwanted signals

**Locations:** `src/ShackStack.DecoderHost.GplFldigiPsk/Program.cs:255`; `src/ShackStack.DecoderHost.GplFldigiRtty/Program.cs:455`; `src/ShackStack.Infrastructure.Decoders/PythonWsjtxModeHost.cs:594`.
**Evidence:** Reproduced by a review probe: a 7 kHz tone at 48 kHz becomes a full-strength 1 kHz tone at 8 kHz. Linear interpolation has no anti-alias low-pass; packet-local rounding also loses fractional phase.
**Action:** Share a stateful, anti-aliased resampler with retained phase/history. Test out-of-band rejection and chunk-invariance, including 44.1 kHz input.

### 24. Missing FreeDV native support permits unbounded buffers

**Locations:** `src/ShackStack.DecoderHost.GplCodec2Freedv/Program.cs:209`, `:268`.
**Evidence:** Source-traced. Samples are appended even when the native runtime is unavailable; readiness checks prevent consumption, not accumulation.
**Action:** Refuse startup clearly or discard/cap buffers while unavailable, clearing them on state changes. Test sustained audio with a deliberately missing native dependency.

### 25. CW lanes lose audio during detection gaps

**Location:** `src/ShackStack.DecoderWorkers.Python/Tools/cw_skimmer_sidecar_worker.py:1006`, unmatched-lane handling at `:1017`.
**Evidence:** Reproduced by a review probe: 45,056 elapsed samples resulted in only 4,096 samples in the neural lane buffer. Retained lanes receive decoder/neural audio only when matched to detections; unmatched windows do not preserve their time interval. Separate elements can become artificially adjacent.
**Action:** Feed every retained lane each audio interval, including quiet/faded intervals. Detection should update tracking, not determine whether time exists in the decoder input.
**Verify:** Drop detections during known gaps/fades and assert exact elapsed sample continuity and preserved word boundaries. Prioritize this before another training run.

### 26. Recorder format changes invalidate captured audio and offsets

**Location:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.CwExperiment.cs:278`; buffer/snapshot paths at `:131`, `:225`.
**Evidence:** Source-traced. Changed sample rate/channel count causes wideband recording to reject data while clip collection continues. Snapshot offsets and the session's original format no longer describe the collected audio correctly.
**Action:** Rotate recording segments on format changes, clear/reinitialize rolling buffers, and store per-segment format and offsets. Test device changes during a session.

### 27. Restarting the recorder within one second can overwrite a session

**Location:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.CwExperiment.cs:54`.
**Evidence:** Source-traced. Folder names have second precision; initialization uses truncating writes for existing manifest/audio paths.
**Action:** Generate collision-resistant session IDs and create files exclusively. Test rapid stop/start and an existing output directory.

### 28. Real-data training preprocessing can disagree with inference

**Location:** `tools/cw_train_duration_model.py:216`.
**Evidence:** Source-traced and probed by the specialist review. When a manifest points to raw captures, loading bypasses the common lane filtering/downmix/resampling path and maps row duration/rate into model frames inconsistently. Curating without `--copy-audio` permits this case. Prepared mono inputs may avoid parts of the problem; this is not proof every existing checkpoint is affected.
**Action:** Use a shared, explicit preprocessing contract for training, evaluation, and live inference; distinguish raw from already-prepared inputs.
**Verify:** The same recording/lane must produce matching features and timestamps in all three paths, including stereo and non-native sample rates.

### 29. Presence training silently treats missing labels as negative

**Location:** `tools/cw_train_presence_baseline.py:195`.
**Evidence:** Source-traced. Captured sample rows without `isCw` are accepted as false, enabling an all-negative training set with misleading accuracy.
**Action:** Require explicit labels and class-coverage checks. Reject unlabeled captures. This affects the presence-baseline tool; it does not establish corruption of duration-model training.

### 30. Presence holdout data is not independent

**Location:** `tools/cw_train_presence_baseline.py:201`.
**Evidence:** Source-traced. Row-level splitting can put overlapping windows and lanes from the same recording in training and holdout. The tiny-data fallback can evaluate on training samples.
**Action:** Split by session/source interval, enforce disjointness and minimum holdout size, and persist split membership. Test overlapping capture rows explicitly.

### 31. The best duration checkpoint is selected by training-batch accuracy

**Location:** `tools/cw_train_duration_model.py:359`.
**Evidence:** Source-traced. Current training-mode batch accuracy chooses the best checkpoint; batch difficulty varies, and the saved post-update weights differ from those that produced the selecting logits.
**Action:** Select checkpoints using a fixed held-out evaluation set in evaluation mode; report training metrics separately. Include difficult signals and transcription/spacing measures, not only frame accuracy.

### 32. Neural decoding systematically penalizes spaces

**Location:** `src/ShackStack.DecoderWorkers.Python/Tools/cw_skimmer_sidecar_worker.py:430`.
**Evidence:** Reproduced with the actual beam function and near-perfect frame probabilities: a clear `E E` sequence becomes `EE`. The spaced branch incurs an extra penalty, while the competing unspaced branch does not; stripped hint spaces do not resolve it. Related logic is duplicated in offline tooling.
**Action:** Score boundaries from gap duration/probability, preserve confident word gaps, and share the implementation. Test speed changes and exact spacing on controlled sequences.

### 33. CW evaluation can pass while transcription is wrong

**Locations:** `tools/cw_skimmer_smoke.py:175`; `tools/cw_skimmer_file_harness.py:199`; worker transcript retention around `cw_skimmer_sidecar_worker.py:188`.
**Evidence:** Reproduced. The smoke suite reports PASS for clean `CQ TEST` with text `KKOO TSHE4T`; assertions establish detection/plumbing, not accurate copy. The file harness also replaces lane text with rolling history, so long recordings can lose earlier decoded content from evaluation.
**Action:** Keep plumbing tests, but add exact/character-error/word-boundary transcription tests. Accumulate committed text with sequence/time metadata instead of scoring only the final rolling display. Label test claims accurately.

### 34. Packaging can report success after a native build failure

**Location:** `installer/Build-DecoderWorkers.ps1:114`, `:119`, final success around `:214`.
**Evidence:** Source-traced. Native tool exit codes are not checked explicitly; `ErrorActionPreference=Stop` does not reliably make nonzero native exits terminating errors in the documented Windows PowerShell workflow. Existing worker output is removed before replacement, allowing incomplete/stale packaging.
**Action:** Check every native exit code, stage builds separately, validate required payloads, and only then replace/package output. Inject failures into each build step and assert no installer/success announcement.

### 35. A clean checkout cannot reproduce all classic FreeDV dependencies

**Location:** `installer/Build-DecoderWorkers.ps1:152`, fallback around `:193`.
**Evidence:** Source-traced. Classic Codec2/FreeDV native files are copied opportunistically from an ignored temporary build tree or prior output. The normal script has no reproducible acquisition/build step for those dependencies. Building RADE does not supply all classic Codec2 assets.
**Action:** Add pinned dependency acquisition/build instructions and make required native payload validation mandatory. Verify from a clean checkout without previous outputs.
**Qualification:** This is a reproducibility finding, not a claim that the existing local installer lacks those files. Leaving PyTorch out of the installed experiment is intentional and is not a defect in this review.

### 36. Waterfall history is relabeled after retuning

**Location:** `src/ShackStack.Infrastructure.Waterfall/WaterfallService.cs:55`.
**Evidence:** Reproduced using the compiled service. A nonzero row at 14.074 MHz remains under a new 50.313 MHz center after a blank frame with unchanged width. Buffer retention considers width but not center/span, so old signals appear under new frequencies.
**Action:** Clear history/blending when the frequency coordinate system changes, or explicitly reproject it. Test same-width retunes and span changes.

### 37. Logbook editing has stale-field and duplicate-action traps

**Locations:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.Longwave.cs:699`, `:847`, `:926`; `src/ShackStack.UI/Views/LongwaveDeskWindow.axaml:186`.
**Evidence:** Source-traced. New Contact does not clear the old grid square. The standalone logbook keeps Log This QSO visible while editing, alongside Save Changes. Destructive contact/logbook actions lack a confirmation or undo path.
**Action:** Reset all contact-owned fields, show create/save actions according to edit state, and confirm destructive operations with the selected target named. Test edit-to-new transitions and accidental repeated actions. Server-side cascading behavior was not established.

### 38. Some desk interactions hide essential feedback or navigation

**Locations:** `src/ShackStack.UI/Views/SstvDeskWindow.axaml:261`; `ViewModels/MainWindowViewModel.Sstv.cs:753`; `Views/WsjtxDeskWindow.axaml:245`.
**Evidence:** Source-traced. SSTV template operations set a status that the view does not display. The bounded WSJT conversation list disables hit testing, also preventing normal pointer scrolling.
**Action:** Display template errors/success near the actions. Keep conversation scrolling enabled while independently controlling selection visuals. Verify both workflows interactively.

## P3: Polish, portability, and technical debt

### 39. The SSTV regression wrapper can hide test failure

**Location:** `tools/Run-SstvRegression.ps1:19`.
**Evidence:** Reproduced: substituting a failing native invocation returning 23 still produced wrapper exit code 0.
**Action:** Propagate native exit codes and add a wrapper failure test. This is a test-reliability issue even when the harness itself reports errors correctly.

### 40. Developer/harness paths point to an old checkout

**Locations:** `Run-ShackStack.ps1:5`; `src/ShackStack.DecoderHost.Sstv.Harness/Program.cs:10`, `:1132`.
**Evidence:** Source-traced. Paths point to `C:\Users\lag0m\Documents\ShackStack.Avalonia`, not the current D: checkout. The regression suite also recursively resets its configured output directory.
**Action:** Resolve paths from the repository/script location or explicit arguments; validate output containment before cleanup. Do not run a destructive harness against an assumed old workspace.

### 41. WeFAX live reconfiguration leaves a replacement engine stopped

**Locations:** `src/ShackStack.DecoderWorkers.Python/Tools/wefax_sidecar_worker.py:129`; `modes/wefax_engine_proto.py:214`.
**Evidence:** Source-traced latent protocol defect. Configuring while running stops/replaces the engine without starting its replacement, while the wrapper retains its running state. The current configure-then-start UI sequence can mask this.
**Action:** Define and enforce live configuration lifecycle behavior; test configuration during decoding and bound pending audio.

### 42. Image allocation/disposal needs an ownership and performance pass

**Locations:** `src/ShackStack.UI/ViewModels/MainWindowViewModel.Audio.cs:470`; SSTV preview around `MainWindowViewModel.Sstv.cs:1341`; WeFAX around `MainWindowViewModel.WeFax.cs:318`; VM disposal at `MainWindowViewModel.cs:1129`.
**Evidence:** Source inspection shows repeated bitmap replacement without explicit disposal of superseded images. This is not a demonstrated unbounded leak or measured slowdown. Archive/shared-image ownership needs care.
**Action:** Profile a long session, define image ownership, reuse fixed-size buffers where appropriate, and dispose only when no view/archive still owns the image.

### 43. VFO control has avoidable handler duplication and a latent display limit

**Location:** `src/ShackStack.UI/Controls/VfoDisplay.axaml.cs:73`, `:83`.
**Evidence:** Source-traced. Class handlers are registered per instance instead of once. Frequency is allowed up to 450 MHz while the eight-digit layout represents only two MHz digits; 145.500 MHz is rendered incorrectly. The current HF/6 m IC-7300 use does not normally reach this range.
**Action:** Register class handlers statically. Support the full accepted frequency range or constrain it consistently. Test multiple instances and 100+ MHz values. No claim is made that reopening desks leaks VFO controls.

### 44. Minimum-size layouts need a focused interactive check

**Locations:** `src/ShackStack.UI/Views/MainWindow.axaml:917`; `LongwaveDeskWindow.axaml:87`.
**Evidence:** Layout inspection, not screenshot reproduction. Unscrollable settings content and the logbook's fixed-height list plus editor/actions risk clipping at minimum window sizes and increased scaling.
**Action:** Add scrolling/shrinkable regions as needed after testing the supported minimum sizes and 125/150/200% scaling.

### 45. Regression coverage should become a first-class tracked deliverable

**Location:** `.gitignore:10` and solution/test inventory.
**Evidence:** Existing smoke and regression harnesses are useful, but no conventional test projects/CI were identified, and the root `tests/` directory is ignored. This is not a claim that the repository has no tests.
**Action:** Permit intentionally versioned tests, add a small deterministic suite around the failures above, and run it with packaging checks. Avoid a large testing-framework migration before adding the highest-value cases.

## Language and presentation cleanup

These are **P3** unless the wording incorrectly describes runtime state. The goal is operator-facing clarity, not mechanically banning informal words from comments or imported source.

| Location | Current wording/problem | Suggested action |
| --- | --- | --- |
| `src/ShackStack.UI/Views/MainWindow.axaml:921` | "Station identity and direct radio control settings live here." | "Set your callsign, grid square, and radio connection." Remove it entirely if the headings are sufficient. |
| `src/ShackStack.UI/Views/KeyboardModesDeskWindow.axaml:20` | "PSK31/63 receive using the fldigi-derived GPL chain. Olivia, Contestia, and MFSK can join this desk next." | Describe current supported operation only. Move implementation attribution to About/licenses and future modes to the roadmap. |
| `src/ShackStack.UI/ViewModels/MainWindowViewModel.WeakSignal.cs:2163` | "Receive is wired. Compose is ready." | "Enter a message or choose a quick message." Do not imply receive health from an empty compose field. |
| `src/ShackStack.UI/ViewModels/MainWindowViewModel.WeakSignal.cs:196` | "JS8 TX generation is wired for short Varicode/Huffman text frames; prepare before arming live PTT." | Use operator language such as "Prepare your message before enabling transmit." Present actual length/encoding limits separately when relevant. |
| `src/ShackStack.UI/Views/VoiceDeskWindow.axaml:202` | "Use this for POTA hunts or any other voice QSO. Logging here stays general on purpose." | "Log voice contacts, including POTA contacts." |
| `src/ShackStack.UI/ViewModels/MainWindowViewModel.DigitalModes.cs:662` | Comment describes CI-V readback being "in a bad mood." | Explain the exact failure and recovery policy after fixing transmit handling; do not normalize silently swallowed safety-relevant failures. |
| `src/ShackStack.UI/ViewModels/MainWindowViewModel.WeakSignal.cs:2548` | Long implementation/path-oriented readiness text | Keep a concise operational status and place executable paths/environment details in diagnostics. Only say ready after the relevant readiness check. |

Also standardize labels/statuses around actual states: unavailable, starting, receiving, transmitting, stopped, and failed. Keep experiments clearly labeled, but remove internal development narration from ordinary operating screens.

## Verification performed

| Check | Result |
| --- | --- |
| Release solution build, no restore | Passed: 0 warnings, 0 errors. |
| `tools/cw_smoke.py` | Passed all three exact-copy cases: CQ TEST 73, W8STR DE KE9CRR, 599 TU. |
| `tools/cw_skimmer_smoke.py` | Three reported passes; inspected output exposes inaccurate copy. See finding 33. |
| UTC parser probe | Reproduced previous-day timestamp using the application's parsing sequence. |
| Compiled XML-RPC multicall parser | Reproduced loss of setter arguments. |
| Compiled waterfall service | Reproduced old signal rows retained under a new tuned center. |
| Actual neural beam function, synthetic probabilities | Reproduced removal of an unambiguous word gap. |
| SSTV wrapper failure injection | Reproduced success exit code after child failure. |
| Specialist probes | Demonstrated audio pump failure persistence, cycle gap concatenation, resampling aliasing, and CW lane timeline loss. |

The full SSTV regression was not run because its hardcoded output path and recursive cleanup required correction/explicit containment first. No live radio transmission, installer installation, GUI interaction, new training run, or server mutation was performed. Hardware-specific behavior, long-running memory growth, visual clipping, and external-service failure scenarios remain follow-up verification work.

## Recommended work order

1. **Transmit and lifecycle safety:** Findings 01-05, 10-12. Add a fake radio/audio test harness first, then implement desired-state PTT and exclusive transmit ownership. Reproduce and fix the capture-disposal lock ordering.
2. **Protect persistent data:** Findings 06-07, 13, 17-18, 26-27, 37. Correct UTC handling and atomic settings writes before more editing/recording reliance.
3. **Recover from worker and network failures:** Findings 08-09, 14-16, 19-24. Add fault injection and bounded recovery rather than only happy-path smoke tests.
4. **Make CW evidence trustworthy:** Findings 25, 28-33. Preserve the audio timeline, unify preprocessing, correct word-gap scoring, and establish independent transcription benchmarks before selecting or retraining a model.
5. **Make releases reproducible:** Findings 34-35, 39-40, 45. Fail builds closed, validate every required worker/native asset, and exercise a clean-checkout build. Keep the agreed optional PyTorch packaging decision separate.
6. **Finish operator polish:** Findings 36, 38, 41-44 and the wording table. Test minimum-size/high-DPI layouts and long sessions, then change only issues confirmed by those checks.

## Scope and architecture notes

The review covered first-party startup/composition, radio control, audio, waterfall, configuration, interop/logbook, desks/view models, worker lifecycle, decoder adapters, CW capture/training/evaluation, scripts, and installer assembly. The inventory was roughly 55,000 nonblank first-party lines across 18 source directories, with 474 tracked files overall. Generated outputs, bundled executable internals, upstream DSP libraries, and scratch artifacts were not exhaustively audited. A clean build is not evidence that every mode works on RF.

The large partial MainWindowViewModel concentrates hardware state, UI state, persistence, and network operations. The concrete defects above support extracting narrowly owned coordinators for transmission, settings saves, and contact requests. They do not justify a blanket rewrite or an abstraction pass unrelated to the fixes.

No P0 finding was established. Absence from this report is not a guarantee of correctness; several concurrency issues require deliberately delayed/failing dependencies to exercise reliably.
