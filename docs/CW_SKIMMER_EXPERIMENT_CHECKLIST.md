# CW Skimmer Experiment Checklist

This is the "oh no, we forgot the boring important thing" list for the experimental wideband CW decoder.

## Product Shape

- Add the skimmer path as an experimental CW desk feature, not a replacement for the existing narrowband CW decoder.
- Keep the current fldigi-style CW sidecar usable while the skimmer is noisy.
- Show lane/candidate telemetry separately from copied text so weak guesses do not look like confirmed copy.
- Make the neural pass optional until there is a trained model artifact and a repeatable evaluation set.

## Sidecar Contract

- Launch a separate `cw_skimmer_sidecar_worker` so worker crashes cannot break the current CW decoder.
- Accept the same base audio envelope as other workers: float32 samples, sample rate, channels, JSON over stdin/stdout.
- Emit telemetry with candidate count, strongest audio tone, SNR estimate, sample rate, and active engine name.
- Emit candidate messages with audio tone, estimated WPM when known, scout text, confidence, and source.
- Keep protocol tolerant of missing future fields so older installers can still run newer UI experiments.

## Skimmer Scout

- Start with a simple wideband spectral scout over the rig passband.
- Track multiple candidate lanes instead of only the currently selected CW pitch.
- De-duplicate adjacent peaks so one strong signal does not appear as several lanes.
- Add per-lane hysteresis so candidates do not flicker every audio buffer.
- Give each lane a stable identity so downstream decoders can follow a signal through short fades and small frequency drift.
- Model lane assignment explicitly: nearest-neighbor is fine for MVP, but keep the door open for split/merge handling when interference appears.
- Track lane age, last-seen time, missed frames, and tone smoothing rather than treating each FFT frame as a fresh signal list.
- Save enough debug metadata to compare "what the scout saw" with "what the decoder copied."

## Decoder Passes

- First pass: classic DSP scout for tone, SNR-ish confidence, WPM hints, and optional crude Morse text.
- Second pass: neural timing model trained on dirty synthetic CW and later real captures.
- Make the early ML target duration-aware, not only `P(tone_on)`: include weak hints for dit-like, dah-like, gap-like, or continuous element length.
- Treat the adaptive timing decoder as a probabilistic state machine with beam search, not a single deterministic threshold path.
- Score candidate paths with timing likelihood, symbol validity, WPM drift penalty, and confidence from the scout/model.
- Third pass: symbolic/callsign/QSO re-ranker that can use the scout guess without treating it as gospel.
- Keep language-model/callsign correction in debug/rescore mode first; it can suggest alternates, but must not silently override base decode until much later.
- Make every pass report confidence and alternates; never let a later pass silently overwrite uncertainty.

## Training Data

- Generate synthetic dirty CW with variable WPM, tone, fading, drift, QRN/QRM, keying imperfections, and off-frequency tuning.
- Keep a tone-conditioned training path: recorded samples are wideband, so lane scoring must evaluate the candidate tone, not the strongest signal in the whole passband.
- Store WAV plus JSON labels for tone, timing, text, WPM, and noise recipe.
- Store float32 raw clips plus JSONL first; export WAV only when listening or sharing outside the tooling.
- Include common ham text patterns: CQ, DE, callsigns, RST, POTA/SOTA, contest exchanges, and messy spacing.
- Keep train/validation recipes separated so the model does not simply memorize one generator profile.
- Add operator-specific quirks that persist through a sample: dit/dah ratio, hand-key wobble, spacing habits, and speed drift.
- Add compositional interference: overlapping CW, partial collisions, near-frequency bleed, burst noise, QSB envelope changes, and AGC-like pumping.
- Include explicit negatives: static, broadband noise, solid carriers, switching-supply spikes, and solid carriers with nearby real keyed CW.
- Use the first model as a CW-presence gate before attempting second-pass transcription.
- Train the first duration-aware timing model with `tools/cw_train_duration_model.py`; it predicts `gap`, `dit`, `dah`, and `bad_lane` frames from dirty synthetic examples.
- Use `tools/cw_neural_decode_harness.py` to compare model-derived text against first-pass lane transcripts. Current beam decoding finds recognizable islands (`N5RZ`, `NS1S`, `5NN`, `N4BP`, `BOB`, `TU`) but is not yet reliable full-line transcription.
- Keep bad-lane/RFI training as a guardrail. The next transcription gain should come from a probabilistic/beam decoder over model probabilities, not from more PSU-focused training.
- Add a minimal human correction path for real captures so obvious callsign/text fixes can become high-value training labels.

## Debug And Replay

- Capture short audio windows around candidate lanes for replay.
- Show spectrogram overlays for lane identity, tone probability, chosen segmentation, and alternate paths.
- Let a developer replay a decode at slower/faster speed while preserving the model/scout debug timeline.
- Store per-character confidence, alternate symbol paths, WPM estimates, and lane assignment history with each debug capture.

## Packaging

- Include the skimmer worker in `Build-DecoderWorkers.ps1`.
- Include any Python dependencies in the build dependency check before the installer is rebuilt.
- Package model files separately from code, with a clear "model missing" status instead of a crash.
- Verify fresh Windows behavior: .NET runtime, Python worker bundle, model artifact, and native DLL assumptions.

## Smoke Tests

- Start/stop/reset the skimmer sidecar from the desk.
- Feed synthetic single-tone CW audio and confirm the reported lane is near the expected audio tone.
- Feed two simultaneous tones and confirm both lanes appear.
- Feed silence/noise and confirm candidate count drops instead of producing confident nonsense.
- Confirm the existing CW decoder still starts and copies exactly as before.

## Open Decisions

- Whether PyMorseLive becomes an imported dependency, a reference implementation, or just a design influence.
- Whether the first neural model predicts tone-on probability only, symbols, or text directly.
- Where to store user-captured training/debug clips.
- Whether external commercial decoders can be optional teacher inputs when installed by the user.
