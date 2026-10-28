# Nexus: Ideas Worth Borrowing

Recorded: 2026-09-24. Follow-up to [our code review](CODE_REVIEW_2026-09-16.md).

Update 2026-10-19: the user has now tried Nexus and requested a broader UI/workflow and decoder comparison. Use [the current comparison plan](NEXUS_COMPARISON_PLAN_2026-10-19.md) for next work; the earlier operating preferences and authorization checklist below are historical.

## Intent

Improve ShackStack selectively, without switching applications/frameworks or chasing Nexus's entire feature list. The user does not want to run Nexus; source inspection and independent tests are the preferred approach. No Nexus software was installed or executed during this comparison. No implementation or model training is authorized by this note alone.

CW remains shelved for active development. Preserve the offline DeepCW comparison below as a possible next experiment, not a commitment to resume our training pipeline.

## Priority Shortlist

### 1. Transmit safety and recovery

- Separate desired transmit state from observed/confirmed rig state.
- Retry failed unkey operations and attempt unkey through a freshly restored connection.
- Stop queued audio as well as CAT PTT, including VOX operation.
- Test stop/release during startup, reconnect while keyed, clock changes, and overlapping mode requests.
- Apply these ideas to our review findings 01-05 and 10-12 using ShackStack's existing architecture; do not copy their large radio loop wholesale.

Source: [Nexus radio service and regression tests](https://github.com/kd9taw/Nexus/blob/main/crates/tempo-audio/src/service.rs).

### 2. Failure-oriented regression tests

- Add deterministic tests using fake radios, audio backends, worker processes, and failed network responses.
- Ensure tests exercise optional/runtime paths, not just default builds.
- Keep build success, packaging success, and live-radio verification distinct.
- For digital modes, compare generated waveforms with an independent reference such as stock WSJT-X. Add noise-only false-decode tests.

Sources: [CI configuration](https://github.com/kd9taw/Nexus/blob/main/.github/workflows/ci.yml), [documented reference comparisons](https://github.com/kd9taw/Nexus#why-nexus).

Caution: Their published performance figures were not independently reproduced. Their CI explicitly distinguishes Windows compilation from complete installer production.

### 3. Actionable diagnostics

- Identify the actual failing radio/backend, device or port, baud rate, worker, and dependency.
- Preserve the underlying cause rather than replacing it with a generic worker-exited message.
- Distinguish missing components, unavailable audio, a disconnected rig, retrying, and healthy reception.
- Provide concise operator status with detailed diagnostics available separately.

Source: [Nexus CAT diagnostics](https://github.com/kd9taw/Nexus/blob/main/crates/tempo-audio/src/service.rs#L235).

### 4. Explicit release dependencies

- Maintain a required-payload manifest and fail packaging when a required worker/native library is absent.
- Test a clean-checkout build without old output or ignored temporary dependency trees.
- Make optional components explicit. Preserve our current decision not to bundle PyTorch for the unfinished CW experiment.

Source: [Nexus bundle configuration](https://github.com/kd9taw/Nexus/blob/main/src-tauri/tauri.conf.json).

### 5. Later operator improvements

- Waterfall pause/rewind while live history continues recording.
- Better keyboard and screen-reader access.
- Spots that show evidence, receiver location, and observation age rather than implying every distant reception means a local opening.

Source: [documented operator features](https://github.com/kd9taw/Nexus#why-nexus). These features were not exercised interactively.

## CW: Benchmark DeepCW Before More Training

### What we found

Nexus integrates e04's pretrained DeepCW model, not an original Nexus-trained neural decoder. It converts audio to a spectrogram and predicts character classes, then collapses those predictions into text using CTC. Its inspected neural path does not consume a conventional decoder's guessed text or WPM.

The current model contract specifies mono 3200 Hz audio and a 400-1200 Hz spectrogram band. Nexus uses local ONNX inference through Rust's tract library, avoiding a runtime PyTorch requirement. Its packaged model is specialized to fixed 15-second windows; do not assume that packaging transformation is necessary for a different inference runtime.

Nexus's streaming integration uses overlapping 15-second windows at a nominal two-second cadence, a one-second tail guard, timestamp-based text stitching, and adaptive CPU throttling. These are useful design ideas, not proof of accurate copy.

The inspected Nexus neural integration produces one transcript from one audio buffer. Its conventional multi-signal skimmer is separate. DeepCW's own web application advertises multi-channel decoding; distinguish that application's capabilities from the model and Nexus's integration.

Sources:

- [DeepCW engine and reference examples](https://github.com/e04/deepcw-engine)
- [Model metadata](https://github.com/e04/deepcw-engine/blob/main/model.onnx.json)
- [DeepCW web application and comparisons](https://github.com/e04/web-deep-cw-decoder)
- [Nexus inference wrapper](https://github.com/kd9taw/Nexus/blob/main/crates/deepcw/src/lib.rs)
- [Nexus live inference and stitching](https://github.com/kd9taw/Nexus/blob/main/crates/tempo-audio/src/aicw.rs)
- [Nexus audio buffers and separate skimmer](https://github.com/kd9taw/Nexus/blob/main/crates/tempo-app/src/engine.rs)
- [Nexus model preparation notes](https://github.com/kd9taw/Nexus/blob/main/src-tauri/resources/deepcw/README.md)

### Proposed bounded experiment

1. Pin the upstream model/code revision and record model checksums and preprocessing settings.
2. Run the upstream reference decoder offline before changing the ShackStack UI or adapting Nexus code.
3. Select saved recordings covering clean W1AW, speed transitions, dense contest activity, fading/weak signals, PSU tones, and noise-only intervals.
4. Start with isolated single signals. For our higher/lower-pitched lanes, filter and frequency-translate into the model's supported band before resampling; preserve the full audio timeline, including silence and fades.
5. Compare against our existing decoder on the same audio intervals. Use human-checked text, not existing decoder guesses, as reference truth where available.
6. Measure character errors, word boundaries, callsign accuracy, false copy, latency, and CPU cost. Without checked reference text, label results exploratory rather than an accuracy benchmark.
7. Proceed to integration only if the improvement is meaningful on our recordings, not merely on advertised benchmarks.

Potential integration: retain our lane detector and conventional copy, add DeepCW as an independent audio-to-text second opinion per lane, and show/compare the two outputs. Feeding provisional text into the pretrained model is not supported by its current input contract. This preserves the user's second-pass intent without pretending Nexus already implements that design.

### Reasons for caution

- The author's published examples still contain spacing and character errors.
- An independent AetherSDR prototype reported that simple synthetic weak-signal tests did not reproduce the headline advantage. Real off-air comparisons matter more than promises.
- Our known lane-continuity and preprocessing defects can corrupt inputs to any model. A successful isolated offline benchmark does not automatically validate our live pipeline.
- Inspect stale-result handling, retune/reset boundaries, and output stitching before adopting Nexus's streaming pattern.

Independent experiment: [AetherSDR DeepCW proposal and prototype observations](https://github.com/aethersdr/AetherSDR/issues/4817).

## Reuse and Attribution Checkpoint

Nexus identifies itself as GPL-3.0; the DeepCW engine/model is separately labeled AGPL-3.0-only. Treat those as separate dependencies, not as one blanket permission. Before copying or distributing anything, inspect the exact files' notices and applicable licenses, retain attribution/notices, record upstream revisions and modifications, and review source/distribution obligations. Do not assume moving a model into a sidecar or optional download resolves licensing questions.

Borrowing a design and independently implementing it is different from copying source or distributing model weights. Prefer the smallest proven idea that fits ShackStack. Code quality should be assessed through implementation and reproducible tests, regardless of which coding assistant was used.

## Status

- [x] Source comparison and borrowing shortlist recorded.
- [ ] User authorizes an implementation batch or the bounded offline CW benchmark.
- [ ] Verify current upstream revisions/licenses before reuse.
- [ ] Implement and test selected changes; update the main review with verified resolutions.

No Nexus installation, model download, benchmark run, application modification, or training run was performed as part of these notes.
