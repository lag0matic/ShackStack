from __future__ import annotations

import base64
import json
import math
import os
import re
import sys
import threading
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from modes.cw_engine import MORSE_DECODE, MORSE_PREFIXES
from modes.cw_engine_fldigi import CwFldigiDecoder


_lock = threading.RLock()
_running = False
_config = {
    "lowerAudioHz": 250,
    "upperAudioHz": 2800,
    "laneSpacingHz": 45,
    "maxLanes": 8,
    "wpmHint": 20,
    "neuralSecondPassEnabled": False,
    "scoutEngine": "Spectral Scout",
}
_sample_rate = 0
_candidate_count = 0
_strongest_tone_hz = 0.0
_strongest_snr_db = 0.0
_last_telemetry_time = 0.0
_next_lane_id = 1
_audio_cursor_seconds = 0.0
_lanes: dict[int, "TrackedLane"] = {}
_analysis_buffer = np.empty(0, dtype=np.float32)
_neural_model: Any | None = None
_neural_checkpoint: dict[str, Any] | None = None
_neural_device: Any | None = None
_neural_status = ""
_neural_failed = False

LABELS = ["gap", "dit", "dah", "bad_lane"]
TOKEN_RE = re.compile(r"[A-Z0-9/]{2,}")
CONTEST_TOKENS = {"CQ", "TEST", "TU", "DE", "K", "KN", "73", "5NN", "599", "SST", "RST", "NR"}
DEFAULT_NEURAL_MODEL = Path(os.environ.get("SHACKSTACK_CW_NEURAL_MODEL", r"C:\Users\lag0m\ShackStack\cw-training\duration-overnight-001\final.pt"))


@dataclass
class CarrierDetection:
    tone_hz: float
    snr_db: float
    sideband_count: int
    occupied_bandwidth_hz: float


@dataclass
class TrackedLane:
    lane_id: int
    tone_hz: float
    snr_db: float
    sideband_count: int
    occupied_bandwidth_hz: float
    first_seen: float
    last_seen: float
    age_frames: int = 1
    missed_frames: int = 0
    last_emit: float = 0.0
    envelope_level: float = 0.0
    envelope_floor: float = 1e-6
    tone_probability: float = 0.0
    key_down: bool = False
    state_started_at: float = 0.0
    estimated_element_ms: float = 60.0
    timing_pattern: str = ""
    pending_timing_hint: str = ""
    pending_text: str = ""
    pending_text_provisional: bool = False
    last_decoded_text: str = ""
    decoded_buffer: str = ""
    committed_character_count: int = 0
    fldigi_decoder: Any | None = None
    fldigi_sample_rate: int = 0
    fldigi_buffer: str = ""
    fldigi_pending_text: str = ""
    fldigi_estimated_wpm: float = 0.0
    last_neural_emit: float = 0.0
    neural_text: str = ""
    neural_token_text: str = ""
    neural_token_candidates: str = ""
    neural_bad_lane_score: float = 0.0
    neural_status: str = ""
    neural_sample_rate: int = 0
    neural_audio: Any | None = None


def emit(payload: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(payload, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def emit_telemetry(status: str | None = None) -> None:
    with _lock:
        payload = {
            "type": "telemetry",
            "isRunning": bool(_running),
            "status": status or ("Scanning" if _running else "Stopped"),
            "activeWorker": str(_config.get("scoutEngine", "Spectral Scout")),
            "candidateCount": int(_candidate_count),
            "strongestToneHz": float(_strongest_tone_hz),
            "strongestSnrDb": float(_strongest_snr_db),
            "sampleRate": int(_sample_rate),
        }
    emit(payload)


def handle_configure(message: dict[str, Any]) -> None:
    with _lock:
        _config["lowerAudioHz"] = int(message.get("lowerAudioHz", _config["lowerAudioHz"]))
        _config["upperAudioHz"] = int(message.get("upperAudioHz", _config["upperAudioHz"]))
        _config["laneSpacingHz"] = int(message.get("laneSpacingHz", _config["laneSpacingHz"]))
        _config["maxLanes"] = int(message.get("maxLanes", _config["maxLanes"]))
        _config["wpmHint"] = int(message.get("wpmHint", _config["wpmHint"]))
        _config["neuralSecondPassEnabled"] = bool(message.get("neuralSecondPassEnabled", _config["neuralSecondPassEnabled"]))
        _config["scoutEngine"] = str(message.get("scoutEngine", _config["scoutEngine"]))

    emit_telemetry(
        f"Configured {int(_config['lowerAudioHz'])}-{int(_config['upperAudioHz'])} Hz, "
        f"{int(_config['maxLanes'])} lanes"
    )


def handle_start() -> None:
    global _running
    with _lock:
        _running = True
    emit_telemetry("Skimmer scout scanning")


def handle_stop() -> None:
    global _running
    with _lock:
        _running = False
    emit_telemetry("Skimmer scout stopped")


def handle_reset() -> None:
    global _candidate_count, _strongest_tone_hz, _strongest_snr_db, _lanes, _next_lane_id, _audio_cursor_seconds, _analysis_buffer
    with _lock:
        for lane in _lanes.values():
            _stop_lane_decoder(lane)
        _candidate_count = 0
        _strongest_tone_hz = 0.0
        _strongest_snr_db = 0.0
        _next_lane_id = 1
        _audio_cursor_seconds = 0.0
        _analysis_buffer = np.empty(0, dtype=np.float32)
        _lanes = {}
    emit_telemetry("Skimmer scout reset")


def _stop_lane_decoder(lane: TrackedLane) -> None:
    decoder = lane.fldigi_decoder
    if decoder is None:
        return
    try:
        decoder.stop()
    except Exception:
        pass
    lane.fldigi_decoder = None
    lane.fldigi_sample_rate = 0


def _append_fldigi_text(lane: TrackedLane, text: str) -> None:
    if not text:
        return
    lane.fldigi_pending_text += text
    lane.fldigi_buffer += text
    if len(lane.fldigi_buffer) > 240:
        lane.fldigi_buffer = lane.fldigi_buffer[-240:]


def _ensure_lane_decoder(lane: TrackedLane, sample_rate: int) -> CwFldigiDecoder | None:
    if sample_rate <= 0:
        return None

    if lane.fldigi_decoder is not None and lane.fldigi_sample_rate == sample_rate:
        return lane.fldigi_decoder

    _stop_lane_decoder(lane)

    def on_text(event: Any, tracked_lane: TrackedLane = lane) -> None:
        _append_fldigi_text(tracked_lane, str(getattr(event, "text", "")))

    def on_wpm(wpm: int, tracked_lane: TrackedLane = lane) -> None:
        tracked_lane.fldigi_estimated_wpm = float(wpm)

    decoder = CwFldigiDecoder(
        sample_rate=sample_rate,
        tone_hz=float(lane.tone_hz),
        text_callback=on_text,
        initial_wpm=int(_config.get("wpmHint", 20)),
        bandwidth_hz=220,
        matched_filter_enabled=True,
        tracking_enabled=True,
        tracking_range_wpm=18,
        lower_wpm_limit=5,
        upper_wpm_limit=60,
        auto_tone_search_enabled=False,
        afc_enabled=False,
        squelch="Low",
        noise_character="Suppress",
        spacing="Slow",
    )
    decoder.set_wpm_callback(on_wpm)
    decoder.start()
    lane.fldigi_decoder = decoder
    lane.fldigi_sample_rate = sample_rate
    return decoder


def _feed_lane_decoder(lane: TrackedLane, samples: np.ndarray, sample_rate: int) -> None:
    if lane.age_frames < 8 or lane.snr_db < 16.0:
        return
    decoder = _ensure_lane_decoder(lane, sample_rate)
    if decoder is None:
        return
    try:
        decoder.tone_hz = float(lane.tone_hz)
        decoder.push_samples(samples)
    except Exception as ex:
        lane.pending_timing_hint = f"fldigi lane decoder error: {ex}"
        _stop_lane_decoder(lane)


def _ensure_neural_model() -> tuple[Any | None, dict[str, Any] | None, Any | None, str]:
    global _neural_model, _neural_checkpoint, _neural_device, _neural_status, _neural_failed

    if _neural_model is not None and _neural_checkpoint is not None:
        return _neural_model, _neural_checkpoint, _neural_device, _neural_status
    if _neural_failed:
        return None, None, None, _neural_status

    model_path = DEFAULT_NEURAL_MODEL
    if not model_path.exists():
        _neural_failed = True
        _neural_status = f"neural model missing: {model_path}"
        return None, None, None, _neural_status

    try:
        import torch
        from torch import nn

        class DurationCnn(nn.Module):
            def __init__(self, channels: int, classes: int) -> None:
                super().__init__()
                self.net = nn.Sequential(
                    nn.Conv1d(channels, 48, kernel_size=7, padding=3),
                    nn.BatchNorm1d(48),
                    nn.SiLU(),
                    nn.Conv1d(48, 64, kernel_size=7, padding=6, dilation=2),
                    nn.BatchNorm1d(64),
                    nn.SiLU(),
                    nn.Conv1d(64, 96, kernel_size=7, padding=12, dilation=4),
                    nn.BatchNorm1d(96),
                    nn.SiLU(),
                    nn.Conv1d(96, 96, kernel_size=7, padding=24, dilation=8),
                    nn.BatchNorm1d(96),
                    nn.SiLU(),
                    nn.Conv1d(96, classes, kernel_size=1),
                )

            def forward(self, x: Any) -> Any:
                return self.net(x)

        device_name = os.environ.get("SHACKSTACK_CW_NEURAL_DEVICE")
        if not device_name:
            device_name = "cuda" if torch.cuda.is_available() else "cpu"
        device = torch.device(device_name)
        checkpoint = torch.load(model_path, map_location=device)
        config = checkpoint["config"]
        model = DurationCnn(channels=int(config["inputChannels"]), classes=len(checkpoint["labels"]))
        model.load_state_dict(checkpoint["modelState"])
        model.to(device)
        model.eval()
        _neural_model = model
        _neural_checkpoint = checkpoint
        _neural_device = device
        _neural_status = f"{model_path.name} on {device}"
        return model, checkpoint, device, _neural_status
    except Exception as ex:
        _neural_failed = True
        _neural_status = f"neural unavailable: {ex}"
        return None, None, None, _neural_status


def _smooth_probs(probs: np.ndarray, width: int = 5) -> np.ndarray:
    if width <= 1:
        return probs
    kernel = np.ones(width, dtype=np.float32) / float(width)
    return np.vstack([np.convolve(probs[index], kernel, mode="same") for index in range(probs.shape[0])])


def _collapse_runs(labels: np.ndarray) -> list[tuple[int, int, int]]:
    if labels.size == 0:
        return []
    runs: list[tuple[int, int, int]] = []
    start = 0
    current = int(labels[0])
    for index, value in enumerate(labels[1:], start=1):
        value = int(value)
        if value != current:
            runs.append((current, start, index))
            start = index
            current = value
    runs.append((current, start, labels.size))
    return runs


def _merge_short_runs(runs: list[tuple[int, int, int]], min_frames: int) -> list[tuple[int, int, int]]:
    if not runs or min_frames <= 1:
        return runs
    labels: list[int] = []
    for label, start, end in runs:
        labels.extend([label] * (end - start))
    arr = np.asarray(labels, dtype=np.int64)
    changed = True
    while changed:
        changed = False
        current_runs = _collapse_runs(arr)
        for index, (label, start, end) in enumerate(current_runs):
            if end - start >= min_frames or label == 3:
                continue
            left_label = current_runs[index - 1][0] if index > 0 else None
            right_label = current_runs[index + 1][0] if index + 1 < len(current_runs) else None
            replacement = right_label if left_label is None else left_label if right_label is None else left_label
            if replacement is not None:
                arr[start:end] = replacement
                changed = True
                break
    return _collapse_runs(arr)


def _run_class_score(probs: np.ndarray, label: int, start: int, end: int) -> float:
    segment = probs[label, start:end]
    if segment.size == 0:
        return -20.0
    return float(np.sum(np.log(np.clip(segment, 1e-5, 1.0))))


def _compact_text(value: str) -> str:
    return re.sub(r"[^A-Z0-9/]+", "", value.upper())


def _longest_common_substring_len(left: str, right: str) -> int:
    if not left or not right:
        return 0
    previous = [0] * (len(right) + 1)
    best = 0
    for left_ch in left:
        current = [0] * (len(right) + 1)
        for index, right_ch in enumerate(right, start=1):
            if left_ch == right_ch:
                current[index] = previous[index - 1] + 1
                best = max(best, current[index])
        previous = current
    return best


def _hint_alignment_score(candidate: str, hint: str) -> float:
    candidate_compact = _compact_text(candidate)
    hint_compact = _compact_text(hint)
    if not candidate_compact or not hint_compact:
        return 0.0
    score = 0.0
    for token in TOKEN_RE.findall(hint.upper()):
        if len(token) >= 3 and token in candidate_compact:
            score += min(8.0, len(token) * 0.75)
    score += _longest_common_substring_len(candidate_compact, hint_compact) * 0.18
    return score


def _hint_increment_score(before: str, after: str, hint: str) -> float:
    if not hint:
        return 0.0
    return max(0.0, min(2.5, _hint_alignment_score(after, hint) - _hint_alignment_score(before, hint)))


def _beam_decode_runs(runs: list[tuple[int, int, int]], probs: np.ndarray, frame_hz: int, hint: str) -> str:
    states: list[tuple[float, str, str]] = [(0.0, "", "")]
    element_durations = [end - start for label, start, end in runs if label in {1, 2}]
    dit_frames = max(3.0, float(np.percentile(element_durations, 35.0)) if element_durations else frame_hz * 0.06)
    char_gap_frames = max(dit_frames * 1.6, 8.0)
    word_gap_frames = max(dit_frames * 4.0, char_gap_frames * 2.0)

    for label, start, end in runs:
        duration = end - start
        next_states: list[tuple[float, str, str]] = []
        if label in {1, 2}:
            primary = "." if label == 1 else "-"
            alternate = "-" if label == 1 else "."
            primary_score = _run_class_score(probs, label, start, end)
            alternate_score = _run_class_score(probs, 2 if label == 1 else 1, start, end) - 2.4
            skip_score = _run_class_score(probs, 0, start, end) - 3.2
            for score, text, pattern in states:
                next_states.append((score + primary_score, text, pattern + primary))
                if abs(primary_score - alternate_score) < 8.0:
                    next_states.append((score + alternate_score, text, pattern + alternate))
                if duration <= max(3, frame_hz // 20):
                    next_states.append((score + skip_score, text, pattern))
        elif label == 0:
            gap_score = _run_class_score(probs, 0, start, end)
            for score, text, pattern in states:
                if not pattern:
                    next_states.append((score + gap_score * 0.25, text, ""))
                elif duration < char_gap_frames:
                    next_states.append((score + gap_score * 0.25 + 0.4, text, pattern))
                else:
                    decoded = MORSE_DECODE.get(pattern)
                    if decoded is not None:
                        next_text = text + decoded
                        char_score = score + gap_score * 0.35 + 2.2 + min(1.6, len(pattern) * 0.22) + (0.8 * _hint_increment_score(text, next_text, hint))
                        next_states.append((char_score, next_text, ""))
                        if duration >= word_gap_frames:
                            next_states.append((char_score - 0.7, next_text + " ", ""))
                    elif pattern in MORSE_PREFIXES:
                        next_states.append((score + gap_score * 0.1 - 1.2, text, pattern))
                    else:
                        next_states.append((score + gap_score * 0.25 - 3.5, text + "?", ""))
        else:
            bad_score = _run_class_score(probs, 3, start, end)
            for score, text, pattern in states:
                if pattern and pattern in MORSE_DECODE:
                    next_states.append((score + bad_score * 0.15 - 0.5, text + MORSE_DECODE[pattern], ""))
                else:
                    next_states.append((score + bad_score * 0.1 - 0.8, text, ""))

        states = sorted(
            ((score - (6.0 + len(pattern)) if len(pattern) > 8 else score, text, pattern) for score, text, pattern in next_states),
            key=lambda item: item[0],
            reverse=True,
        )[:18]

    final_states: list[tuple[float, str]] = []
    for score, text, pattern in states:
        if not pattern:
            final_states.append((score, text))
        elif pattern in MORSE_DECODE:
            final_states.append((score + 1.3, text + MORSE_DECODE[pattern]))
        else:
            final_states.append((score - 3.0, text + "?"))
    best = max(final_states, key=lambda item: item[0] + (0.8 * _hint_alignment_score(item[1], hint))) if final_states else (0.0, "")
    return " ".join(best[1].split())


def _edit_distance(left: str, right: str, limit: int = 4) -> int:
    if abs(len(left) - len(right)) > limit:
        return limit + 1
    previous = list(range(len(right) + 1))
    for i, left_ch in enumerate(left, start=1):
        current = [i] + [0] * len(right)
        row_min = current[0]
        for j, right_ch in enumerate(right, start=1):
            current[j] = min(previous[j] + 1, current[j - 1] + 1, previous[j - 1] + (0 if left_ch == right_ch else 1))
            row_min = min(row_min, current[j])
        if row_min > limit:
            return limit + 1
        previous = current
    return previous[-1]


def _looks_callish(token: str) -> bool:
    return len(token) >= 3 and any(ch.isdigit() for ch in token) and any(ch.isalpha() for ch in token) and not token.isdigit()


def _extract_tokens(text: str) -> list[str]:
    return [token.strip() for token in TOKEN_RE.findall(text.upper()) if len(token.strip()) >= 2]


def _support_score(token: str, other_tokens: list[str], other_compact: str) -> tuple[float, str]:
    if token in other_tokens or token in other_compact:
        return min(5.0, len(token) * 0.8), "exact"
    best_distance = 99
    best_token = ""
    for other in other_tokens:
        if abs(len(token) - len(other)) <= 2:
            distance = _edit_distance(token, other, limit=2)
            if distance < best_distance:
                best_distance = distance
                best_token = other
    if best_distance <= 1 and len(token) >= 4:
        return 2.8, f"near:{best_token}"
    if best_distance == 2 and len(token) >= 5:
        return 1.4, f"near:{best_token}"
    overlap = _longest_common_substring_len(token, other_compact)
    if overlap >= max(3, len(token) - 1):
        return 1.2 + overlap * 0.25, f"overlap:{overlap}"
    return 0.0, "none"


def _token_prior(token: str) -> float:
    if token in CONTEST_TOKENS:
        return 2.0
    if _looks_callish(token):
        return 2.4 + min(2.0, len(token) * 0.18)
    if token.isdigit() and 2 <= len(token) <= 4:
        return 0.9
    return 0.35 if len(token) >= 3 else 0.0


def _token_rerank(first_pass: str, neural_text: str) -> tuple[str, str]:
    first_tokens = _extract_tokens(first_pass)
    neural_tokens = _extract_tokens(neural_text)
    first_compact = _compact_text(first_pass)
    neural_compact = _compact_text(neural_text)
    candidates: dict[str, dict[str, Any]] = {}

    def add_candidate(token: str, source: str, base: float) -> None:
        candidate = candidates.setdefault(token, {"token": token, "score": 0.0, "sources": set(), "reasons": []})
        candidate["score"] = float(candidate["score"]) + base
        candidate["sources"].add(source)

    for token in first_tokens:
        add_candidate(token, "first", 0.4)
    for token in neural_tokens:
        add_candidate(token, "beam", 0.8)

    for token, candidate in candidates.items():
        candidate["score"] = float(candidate["score"]) + _token_prior(token)
        if "first" in candidate["sources"]:
            support, reason = _support_score(token, neural_tokens, neural_compact)
        else:
            support, reason = _support_score(token, first_tokens, first_compact)
        candidate["score"] = float(candidate["score"]) + support
        if support:
            candidate["reasons"].append(reason)
        if _looks_callish(token) and support == 0.0 and "first" not in candidate["sources"]:
            candidate["score"] = float(candidate["score"]) - 1.2

    ranked = sorted(candidates.values(), key=lambda item: float(item["score"]), reverse=True)
    filtered = [item for item in ranked if float(item["score"]) >= 2.0][:12]
    accepted = {str(item["token"]) for item in filtered}
    output_tokens: list[str] = []
    for token in first_tokens:
        if token in accepted and token not in output_tokens:
            output_tokens.append(token)
    for item in filtered:
        token = str(item["token"])
        if token not in output_tokens:
            output_tokens.append(token)
    candidate_text = ", ".join(f"{item['token']}:{float(item['score']):0.1f}" for item in filtered[:8])
    return " ".join(output_tokens[:10]), candidate_text


def _bandpass_lane(audio: np.ndarray, sample_rate: int, tone_hz: float) -> np.ndarray:
    if tone_hz <= 0.0 or audio.size < 32:
        return audio
    try:
        from scipy.signal import butter, sosfilt

        low = max(40.0, tone_hz - 180.0)
        high = min(sample_rate / 2.0 - 100.0, tone_hz + 180.0)
        if high <= low + 40.0:
            return audio
        sos = butter(4, [low, high], btype="bandpass", fs=sample_rate, output="sos")
        return sosfilt(sos, audio).astype(np.float32)
    except Exception:
        return audio


def _envelope_features(audio: np.ndarray, frame_hz: int, frames: int) -> np.ndarray:
    hop = max(1, int(round(audio.size / frames)))
    env = np.abs(audio.astype(np.float32))
    rms = np.zeros(frames, dtype=np.float32)
    peak = np.zeros(frames, dtype=np.float32)
    zcr = np.zeros(frames, dtype=np.float32)
    for index in range(frames):
        start = index * hop
        end = min(audio.size, start + hop)
        block = env[start:end]
        raw = audio[start:end]
        if block.size:
            rms[index] = float(np.sqrt(np.mean(block * block)))
            peak[index] = float(np.max(block))
            signs = np.signbit(raw)
            zcr[index] = float(np.mean(signs[1:] != signs[:-1])) if raw.size > 1 else 0.0

    def normalize_feature(values: np.ndarray) -> np.ndarray:
        scale = float(np.percentile(values, 98.0) + 1e-6)
        return np.clip(values / scale, 0.0, 3.0).astype(np.float32)

    rms = normalize_feature(rms)
    peak = normalize_feature(peak)
    delta = np.concatenate([[0.0], np.diff(rms)]).astype(np.float32)
    slow = np.convolve(rms, np.ones(max(1, frame_hz // 10), dtype=np.float32) / max(1, frame_hz // 10), mode="same")
    return np.stack([rms, peak, delta, slow.astype(np.float32), zcr], axis=0)


def _prepare_neural_features(audio: np.ndarray, source_rate: int, config: dict[str, Any], tone_hz: float) -> Any:
    import torch

    target_rate = int(config["sampleRate"])
    seconds = float(config["seconds"])
    frame_hz = int(config["frameHz"])
    audio = _bandpass_lane(audio, source_rate, tone_hz)
    if source_rate != target_rate and audio.size:
        try:
            from scipy.signal import resample_poly

            gcd = int(np.gcd(source_rate, target_rate))
            audio = resample_poly(audio, target_rate // gcd, source_rate // gcd).astype(np.float32)
        except Exception:
            stride = max(1, int(round(source_rate / target_rate)))
            audio = audio[::stride].astype(np.float32)
    target_samples = int(target_rate * seconds)
    if audio.size > target_samples:
        start = max(0, audio.size - target_samples)
        audio = audio[start : start + target_samples]
    elif audio.size < target_samples:
        audio = np.pad(audio, (0, target_samples - audio.size))
    features = _envelope_features(audio, frame_hz, int(seconds * frame_hz))
    return torch.from_numpy(features).unsqueeze(0)


def _run_neural_second_pass(lane: TrackedLane, samples: np.ndarray, sample_rate: int, now: float) -> None:
    if not bool(_config.get("neuralSecondPassEnabled", False)):
        return
    if lane.age_frames < 8 or sample_rate <= 0 or samples.size < 256:
        return
    if now - lane.last_neural_emit < 2.5:
        return

    model, checkpoint, device, status = _ensure_neural_model()
    lane.neural_status = status
    lane.last_neural_emit = now
    if model is None or checkpoint is None or device is None:
        return

    try:
        import torch

        hint = lane.fldigi_buffer.strip() or lane.decoded_buffer
        with torch.no_grad():
            x = _prepare_neural_features(samples, sample_rate, checkpoint["config"], lane.tone_hz).to(device)
            probs = torch.softmax(model(x), dim=1)[0].detach().cpu().numpy()
        probs = _smooth_probs(probs, width=5)
        labels = np.argmax(probs, axis=0)
        runs = _merge_short_runs(_collapse_runs(labels), min_frames=3)
        neural_text = _beam_decode_runs(runs, probs, int(checkpoint["config"]["frameHz"]), hint)
        token_text, token_candidates = _token_rerank(hint, neural_text)
        fractions = probs.mean(axis=1)
        lane.neural_text = neural_text
        lane.neural_token_text = token_text
        lane.neural_token_candidates = token_candidates
        lane.neural_bad_lane_score = float(fractions[3]) if len(fractions) > 3 else 0.0
        lane.neural_status = status
    except Exception as ex:
        lane.neural_status = f"neural error: {ex}"


def _append_lane_neural_audio(lane: TrackedLane, samples: np.ndarray, sample_rate: int) -> None:
    if not bool(_config.get("neuralSecondPassEnabled", False)) or samples.size == 0 or sample_rate <= 0:
        return
    if lane.neural_sample_rate != sample_rate:
        lane.neural_sample_rate = sample_rate
        lane.neural_audio = np.empty(0, dtype=np.float32)
    existing = lane.neural_audio if isinstance(lane.neural_audio, np.ndarray) else np.empty(0, dtype=np.float32)
    next_audio = np.concatenate([existing, samples.astype(np.float32, copy=False)])
    max_samples = max(4096, int(sample_rate * 8.0))
    if next_audio.size > max_samples:
        next_audio = next_audio[-max_samples:].copy()
    lane.neural_audio = next_audio


def _mono_samples(message: dict[str, Any]) -> np.ndarray:
    samples_b64 = message.get("samples")
    if not isinstance(samples_b64, str) or not samples_b64:
        return np.empty(0, dtype=np.float32)

    raw = base64.b64decode(samples_b64)
    samples = np.frombuffer(raw, dtype="<f4")
    channels = int(message.get("channels", 1))

    if channels > 1 and len(samples) >= channels:
        usable = (len(samples) // channels) * channels
        if usable == 0:
            return np.empty(0, dtype=np.float32)
        samples = samples[:usable].reshape(-1, channels).mean(axis=1)

    return samples.astype(np.float32, copy=False)


def _update_analysis_buffer(samples: np.ndarray, sample_rate: int) -> np.ndarray:
    global _analysis_buffer

    max_samples = max(4096, min(sample_rate, int(sample_rate * 0.45)))
    if _analysis_buffer.size == 0:
        _analysis_buffer = samples.astype(np.float32, copy=True)
    else:
        _analysis_buffer = np.concatenate([_analysis_buffer, samples.astype(np.float32, copy=False)])

    if _analysis_buffer.size > max_samples:
        _analysis_buffer = _analysis_buffer[-max_samples:].copy()

    return _analysis_buffer


def _pick_lanes(samples: np.ndarray, sample_rate: int) -> list[CarrierDetection]:
    lower = max(50, int(_config.get("lowerAudioHz", 250)))
    upper = min(sample_rate // 2 - 50, int(_config.get("upperAudioHz", 2800)))
    max_lanes = max(1, int(_config.get("maxLanes", 8)))
    lane_spacing = max(15, int(_config.get("laneSpacingHz", 45)))
    carrier_group_hz = max(lane_spacing * 1.8, 100.0)
    sideband_reject_hz = max(lane_spacing * 2.8, 125.0)

    if samples.size < 256 or lower >= upper:
        return []

    window_size = min(16384, 2 ** int(math.floor(math.log2(samples.size))))
    if window_size < 2048:
        return []

    segment = samples[-window_size:]
    segment = segment - float(np.mean(segment))
    windowed = segment * np.hanning(segment.size)
    spectrum = np.abs(np.fft.rfft(windowed))
    freqs = np.fft.rfftfreq(windowed.size, d=1.0 / sample_rate)
    mask = (freqs >= lower) & (freqs <= upper)
    if not np.any(mask):
        return []

    band_freqs = freqs[mask]
    band_power = spectrum[mask]
    floor = float(np.median(band_power)) + 1e-9
    if floor <= 0:
        return []

    ranked = np.argsort(band_power)[::-1]
    lanes: list[CarrierDetection] = []
    consumed = np.zeros(len(band_power), dtype=bool)
    for idx in ranked[: max_lanes * 8]:
        if consumed[idx]:
            continue

        tone_hz = float(band_freqs[idx])
        stronger_nearby = (np.abs(band_freqs - tone_hz) <= sideband_reject_hz) & (band_power > band_power[idx] * 1.35)
        if np.any(stronger_nearby):
            consumed[np.abs(band_freqs - tone_hz) <= lane_spacing] = True
            continue

        snr_db = 20.0 * math.log10((float(band_power[idx]) + 1e-9) / floor)
        if snr_db < 8.0:
            break

        group_mask = np.abs(band_freqs - tone_hz) <= carrier_group_hz
        active_group_mask = group_mask & (band_power >= floor * 2.8)
        active_freqs = band_freqs[active_group_mask]
        occupied_bandwidth_hz = float(active_freqs.max() - active_freqs.min()) if active_freqs.size > 1 else 0.0
        sideband_count = int(max(0, np.count_nonzero(active_group_mask) - 1))
        consumed[group_mask] = True

        lanes.append(
            CarrierDetection(
                tone_hz=tone_hz,
                snr_db=snr_db,
                sideband_count=sideband_count,
                occupied_bandwidth_hz=occupied_bandwidth_hz,
            )
        )
        if len(lanes) >= max_lanes:
            break

    return lanes


def _estimate_lane_envelope(samples: np.ndarray, sample_rate: int, tone_hz: float) -> tuple[float, float]:
    if samples.size < 64 or sample_rate <= 0:
        return 0.0, 0.0

    segment = samples.astype(np.float64, copy=False)
    segment = segment - float(np.mean(segment))
    t = np.arange(segment.size, dtype=np.float64) / float(sample_rate)
    mixed = segment * np.exp(-2j * math.pi * tone_hz * t)
    envelope = float(abs(np.mean(mixed)) * 2.0)
    rms = float(np.sqrt(np.mean(segment * segment)) + 1e-9)
    relative = envelope / max(rms, 1e-9)
    return envelope, relative


def _classify_mark(duration_ms: float, element_ms: float) -> str:
    return "dah" if duration_ms >= element_ms * 2.05 else "dit"


def _classify_gap(duration_ms: float, element_ms: float) -> str:
    if duration_ms >= element_ms * 5.0:
        return "word gap"
    if duration_ms >= element_ms * 1.6:
        return "char gap"
    return "gap"


def _decode_pattern(pattern: str) -> tuple[str, float]:
    if not pattern:
        return "", 0.0
    decoded = MORSE_DECODE.get(pattern)
    if decoded is not None and len(decoded) == 1:
        return decoded, 0.82
    return "?", 0.22


def _format_percent(value: float) -> str:
    return f"{float(value) * 100.0:0.0f}%"


def _allow_transcript_commit(lane: TrackedLane, decoded: str, confidence: float) -> bool:
    if not decoded or decoded == "?":
        return False
    if confidence < 0.5:
        return False
    if lane.age_frames < 3:
        return False
    if lane.snr_db < 10.0:
        return False

    pattern = lane.timing_pattern
    if len(pattern) == 1 and decoded in {"E", "T"}:
        return False

    return True


def _commit_pattern(lane: TrackedLane, reason: str, duration_ms: float) -> None:
    pattern = lane.timing_pattern
    decoded, confidence = _decode_pattern(pattern)
    lane.pending_text = decoded
    allow_commit = _allow_transcript_commit(lane, decoded, confidence)
    lane.pending_text_provisional = not allow_commit
    lane.last_decoded_text = decoded
    if allow_commit:
        lane.decoded_buffer += decoded
        lane.committed_character_count += len(decoded.strip())
    lane.pending_timing_hint = f"{reason} {duration_ms:0.0f} ms after {pattern} -> {decoded} ({_format_percent(confidence)})"
    lane.timing_pattern = ""


def _force_boundary_if_needed(lane: TrackedLane, duration_ms: float) -> None:
    pattern = lane.timing_pattern
    if not pattern:
        return

    if pattern in MORSE_PREFIXES and len(pattern) <= 6:
        return

    best_pattern = ""
    best_text = ""
    for end in range(min(len(pattern), 6), 0, -1):
        candidate = pattern[:end]
        decoded = MORSE_DECODE.get(candidate)
        if decoded is not None and len(decoded) == 1:
            best_pattern = candidate
            best_text = decoded
            break

    if best_pattern:
        confidence = 0.68
        lane.pending_text = best_text
        allow_commit = _allow_transcript_commit(lane, best_text, confidence)
        lane.pending_text_provisional = not allow_commit
        lane.last_decoded_text = best_text
        if allow_commit:
            lane.decoded_buffer += best_text
            lane.committed_character_count += len(best_text.strip())
        remainder = pattern[len(best_pattern):]
        lane.pending_timing_hint = f"forced boundary after {best_pattern} -> {best_text}; carry {remainder or '-'}"
        lane.timing_pattern = remainder if remainder in MORSE_PREFIXES else ""
    else:
        lane.pending_text = "?"
        lane.pending_text_provisional = False
        lane.last_decoded_text = "?"
        lane.pending_timing_hint = f"forced unknown after {pattern}"
        lane.timing_pattern = ""


def _update_timing(lane: TrackedLane, tone_probability: float, audio_time: float) -> None:
    high_threshold = 0.55
    low_threshold = 0.45
    is_down = lane.key_down
    next_down = is_down

    if is_down and tone_probability <= low_threshold:
        next_down = False
    elif not is_down and tone_probability >= high_threshold:
        next_down = True

    if lane.state_started_at <= 0.0:
        lane.state_started_at = audio_time
        lane.key_down = next_down
        return

    if next_down == is_down:
        return

    duration_ms = max(0.0, (audio_time - lane.state_started_at) * 1000.0)
    lane.state_started_at = audio_time
    lane.key_down = next_down

    if is_down:
        element_ms = max(20.0, lane.estimated_element_ms)
        mark = _classify_mark(duration_ms, element_ms)
        if mark == "dit":
            lane.estimated_element_ms = (lane.estimated_element_ms * 0.82) + (duration_ms * 0.18)
            lane.timing_pattern += "."
        else:
            lane.estimated_element_ms = (lane.estimated_element_ms * 0.9) + ((duration_ms / 3.0) * 0.1)
            lane.timing_pattern += "-"
        provisional_text = ""
        provisional_suffix = ""
        if len(lane.timing_pattern) >= 3:
            provisional_text, provisional_confidence = _decode_pattern(lane.timing_pattern)
            if provisional_text and provisional_text != "?":
                lane.pending_text = provisional_text
                lane.pending_text_provisional = True
                lane.last_decoded_text = provisional_text
                provisional_suffix = f" -> {provisional_text} ({_format_percent(provisional_confidence)})"
        lane.pending_timing_hint = f"{mark} {duration_ms:0.0f} ms ({lane.timing_pattern}){provisional_suffix}"
        _force_boundary_if_needed(lane, duration_ms)
    else:
        element_ms = max(20.0, lane.estimated_element_ms)
        gap = _classify_gap(duration_ms, element_ms)
        if gap in {"char gap", "word gap"} and lane.timing_pattern:
            _commit_pattern(lane, gap, duration_ms)
            if gap == "word gap":
                lane.pending_text = f"{lane.pending_text} "
        elif gap != "gap":
            lane.pending_timing_hint = f"{gap} {duration_ms:0.0f} ms"


def _assign_lanes(detections: list[CarrierDetection], samples: np.ndarray, sample_rate: int, audio_time: float) -> list[TrackedLane]:
    global _next_lane_id

    lane_spacing = max(15, int(_config.get("laneSpacingHz", 45)))
    assignment_radius = max(lane_spacing * 3.0, 120.0)
    duplicate_radius = max(lane_spacing * 2.5, 115.0)
    max_missed_frames = 240
    visible_missed_frames = 80
    smoothing = 0.22

    matched_lane_ids: set[int] = set()
    active_lanes: list[TrackedLane] = []
    visible_lanes: list[TrackedLane] = []

    for detection in detections:
        best_lane: TrackedLane | None = None
        best_distance = assignment_radius

        for lane in _lanes.values():
            if lane.lane_id in matched_lane_ids:
                continue
            distance = abs(detection.tone_hz - lane.tone_hz)
            if distance < best_distance:
                best_lane = lane
                best_distance = distance

        if best_lane is None:
            if any(abs(detection.tone_hz - lane.tone_hz) < duplicate_radius for lane in _lanes.values()):
                continue

            best_lane = TrackedLane(
                lane_id=_next_lane_id,
                tone_hz=detection.tone_hz,
                snr_db=detection.snr_db,
                sideband_count=detection.sideband_count,
                occupied_bandwidth_hz=detection.occupied_bandwidth_hz,
                first_seen=audio_time,
                last_seen=audio_time,
            )
            best_lane.estimated_element_ms = 1200.0 / max(5, int(_config.get("wpmHint", 20)))
            _lanes[best_lane.lane_id] = best_lane
            _next_lane_id += 1
        else:
            best_lane.tone_hz = (best_lane.tone_hz * (1.0 - smoothing)) + (detection.tone_hz * smoothing)
            best_lane.snr_db = (best_lane.snr_db * (1.0 - smoothing)) + (detection.snr_db * smoothing)
            best_lane.sideband_count = detection.sideband_count
            best_lane.occupied_bandwidth_hz = detection.occupied_bandwidth_hz
            best_lane.last_seen = audio_time
            best_lane.age_frames += 1
            best_lane.missed_frames = 0

        envelope, relative = _estimate_lane_envelope(samples, sample_rate, best_lane.tone_hz)
        best_lane.envelope_level = (best_lane.envelope_level * 0.75) + (envelope * 0.25)
        if relative < best_lane.envelope_floor or best_lane.envelope_floor <= 1e-6:
            best_lane.envelope_floor = (best_lane.envelope_floor * 0.8) + (max(relative, 1e-6) * 0.2)
        else:
            best_lane.envelope_floor = (best_lane.envelope_floor * 0.98) + (max(relative * 0.35, 1e-6) * 0.02)
        envelope_score = relative / max(best_lane.envelope_floor * 4.0, 1e-6)
        best_lane.tone_probability = max(0.0, min(0.99, envelope_score / (1.0 + envelope_score)))
        _update_timing(best_lane, best_lane.tone_probability, audio_time)
        _feed_lane_decoder(best_lane, samples, sample_rate)
        _append_lane_neural_audio(best_lane, samples, sample_rate)

        matched_lane_ids.add(best_lane.lane_id)
        active_lanes.append(best_lane)

    stale_lane_ids: list[int] = []
    for lane in _lanes.values():
        if lane.lane_id in matched_lane_ids:
            continue
        lane.missed_frames += 1
        lane.snr_db *= 0.985
        lane.tone_probability *= 0.82
        _update_timing(lane, 0.0, audio_time)
        if lane.missed_frames > max_missed_frames:
            stale_lane_ids.append(lane.lane_id)
        elif lane.age_frames >= 4 and lane.missed_frames <= visible_missed_frames:
            visible_lanes.append(lane)

    for lane_id in stale_lane_ids:
        lane = _lanes.pop(lane_id, None)
        if lane is not None:
            _stop_lane_decoder(lane)

    _merge_close_lanes(duplicate_radius)

    visible_lane_ids = {lane.lane_id for lane in visible_lanes}
    for lane in active_lanes:
        if lane.lane_id not in visible_lane_ids:
            visible_lanes.append(lane)

    visible_lanes = [lane for lane in visible_lanes if lane.lane_id in _lanes]
    return sorted(visible_lanes, key=lambda lane: (lane.missed_frames > 0, lane.tone_hz))


def _merge_close_lanes(radius_hz: float) -> None:
    lanes = sorted(_lanes.values(), key=lambda lane: lane.tone_hz)
    removed: set[int] = set()
    for index, lane in enumerate(lanes):
        if lane.lane_id in removed:
            continue
        for other in lanes[index + 1 :]:
            if other.lane_id in removed:
                continue
            if other.tone_hz - lane.tone_hz > radius_hz:
                break

            keep, drop = _pick_lane_to_keep(lane, other)
            keep.tone_hz = (keep.tone_hz * 0.8) + (drop.tone_hz * 0.2)
            keep.snr_db = max(keep.snr_db, drop.snr_db)
            keep.sideband_count = max(keep.sideband_count, drop.sideband_count)
            keep.occupied_bandwidth_hz = max(keep.occupied_bandwidth_hz, drop.occupied_bandwidth_hz)
            keep.first_seen = min(keep.first_seen, drop.first_seen)
            keep.last_seen = max(keep.last_seen, drop.last_seen)
            keep.age_frames = max(keep.age_frames, drop.age_frames)
            keep.missed_frames = min(keep.missed_frames, drop.missed_frames)
            if len(drop.decoded_buffer) > len(keep.decoded_buffer):
                keep.decoded_buffer = drop.decoded_buffer
                keep.committed_character_count = drop.committed_character_count
            if len(drop.fldigi_buffer) > len(keep.fldigi_buffer):
                keep.fldigi_buffer = drop.fldigi_buffer
            if drop.fldigi_pending_text:
                keep.fldigi_pending_text += drop.fldigi_pending_text
            if drop.pending_timing_hint and not keep.pending_timing_hint:
                keep.pending_timing_hint = drop.pending_timing_hint

            removed.add(drop.lane_id)
            _lanes.pop(drop.lane_id, None)
            _stop_lane_decoder(drop)


def _pick_lane_to_keep(left: TrackedLane, right: TrackedLane) -> tuple[TrackedLane, TrackedLane]:
    left_score = (left.age_frames * 0.02) + left.snr_db + (len(left.fldigi_buffer) * 0.15)
    right_score = (right.age_frames * 0.02) + right.snr_db + (len(right.fldigi_buffer) * 0.15)
    if right_score > left_score:
        return right, left
    return left, right


def handle_audio(message: dict[str, Any]) -> None:
    global _sample_rate, _candidate_count, _strongest_tone_hz, _strongest_snr_db, _last_telemetry_time, _audio_cursor_seconds

    with _lock:
        if not _running:
            return

    sample_rate = int(message.get("sampleRate", 48000))
    samples = _mono_samples(message)
    if samples.size == 0:
        return

    analysis_samples = _update_analysis_buffer(samples, sample_rate)
    detections = _pick_lanes(analysis_samples, sample_rate)
    now = time.monotonic()
    chunk_seconds = samples.size / float(sample_rate) if sample_rate > 0 else 0.0

    with _lock:
        _audio_cursor_seconds += chunk_seconds
        audio_time = _audio_cursor_seconds
        lanes = _assign_lanes(detections, samples, sample_rate, audio_time)
        emit_lanes = list(lanes)
        for tracked_lane in _lanes.values():
            if tracked_lane.pending_timing_hint and all(lane.lane_id != tracked_lane.lane_id for lane in emit_lanes):
                emit_lanes.append(tracked_lane)
        _sample_rate = sample_rate
        _candidate_count = len(lanes)
        _strongest_tone_hz = lanes[0].tone_hz if lanes else 0.0
        _strongest_snr_db = lanes[0].snr_db if lanes else 0.0

    for lane in emit_lanes:
        should_emit_timing = bool(lane.pending_timing_hint)
        timing_hint = lane.pending_timing_hint
        lane.pending_timing_hint = ""
        text = lane.pending_text
        is_provisional_text = lane.pending_text_provisional
        lane.pending_text = ""
        lane.pending_text_provisional = False
        fldigi_text = lane.fldigi_pending_text
        lane.fldigi_pending_text = ""
        confidence = max(0.0, min(0.99, (lane.snr_db - 8.0) / 24.0))
        if text == "?":
            confidence = min(confidence, 0.25)
        lane_duration_seconds = max(0.0, lane.last_seen - lane.first_seen)
        transcript = lane.fldigi_buffer.strip() or lane.decoded_buffer
        transcript_characters = len(lane.fldigi_buffer.strip()) if lane.fldigi_buffer.strip() else lane.committed_character_count
        copy_rate = transcript_characters / max(1.0, lane_duration_seconds)
        candidate_text = fldigi_text or text
        estimated_wpm = int(round(lane.fldigi_estimated_wpm)) if lane.fldigi_estimated_wpm > 0 else int(_config.get("wpmHint", 20))
        has_copy = bool(transcript or fldigi_text)
        probably_real_cw = lane.snr_db >= 16.0 or (has_copy and confidence >= 0.33)
        if not probably_real_cw:
            continue
        neural_audio = lane.neural_audio if isinstance(lane.neural_audio, np.ndarray) else samples
        _run_neural_second_pass(lane, neural_audio, lane.neural_sample_rate or sample_rate, now)
        if not should_emit_timing and not fldigi_text:
            if now - lane.last_emit < (2.25 if lane.missed_frames > 0 else 1.25):
                continue
            if lane.age_frames < 4 or (not has_copy and confidence < 0.25):
                continue
        if not fldigi_text and is_provisional_text and confidence < 0.25:
            candidate_text = ""
        lane.last_emit = now
        hold_suffix = f", holding {lane.missed_frames}" if lane.missed_frames > 0 else ""
        emit(
            {
                "type": "candidate",
                "text": candidate_text,
                "confidence": confidence,
                "audioToneHz": lane.tone_hz,
                "estimatedWpm": estimated_wpm,
                "scoutText": f"SNR {lane.snr_db:0.1f} dB, sidebands {lane.sideband_count}{hold_suffix}, wpm {estimated_wpm}, scout {lane.decoded_buffer[-24:] or '-'}",
                "source": f"{str(_config.get('scoutEngine', 'Spectral Scout'))} + fldigi lane copy",
                "laneId": lane.lane_id,
                "laneAge": lane.age_frames,
                "toneProbability": lane.tone_probability,
                "envelopeLevel": lane.envelope_level,
                "sidebandCount": lane.sideband_count,
                "occupiedBandwidthHz": lane.occupied_bandwidth_hz,
                "timingHint": timing_hint,
                "timingPattern": lane.timing_pattern,
                "estimatedElementMs": lane.estimated_element_ms,
                "laneTranscript": transcript,
                "isProvisionalText": is_provisional_text and not fldigi_text,
                "laneDurationSeconds": lane_duration_seconds,
                "committedCharacterCount": transcript_characters,
                "copyRateCharactersPerSecond": copy_rate,
                "neuralText": lane.neural_text,
                "neuralTokenText": lane.neural_token_text,
                "neuralBadLaneScore": lane.neural_bad_lane_score,
                "neuralTokenCandidates": lane.neural_token_candidates,
                "neuralStatus": lane.neural_status,
            }
        )

    if now - _last_telemetry_time >= 0.5:
        _last_telemetry_time = now
        emit_telemetry("Scanning" if lanes else "Scanning, no lanes above threshold")


def main() -> int:
    emit_telemetry("CW skimmer scout ready")

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            message = json.loads(line)
        except Exception as ex:
            emit({"type": "telemetry", "isRunning": False, "status": f"Protocol error: {ex}", "activeWorker": "CW skimmer scout", "candidateCount": 0, "strongestToneHz": 0.0, "strongestSnrDb": 0.0, "sampleRate": 0})
            continue

        msg_type = message.get("type")
        try:
            if msg_type == "configure":
                handle_configure(message)
            elif msg_type == "start":
                handle_start()
            elif msg_type == "stop":
                handle_stop()
            elif msg_type == "reset":
                handle_reset()
            elif msg_type == "audio":
                handle_audio(message)
            elif msg_type == "shutdown":
                handle_stop()
                break
        except Exception as ex:
            emit_telemetry(f"Worker error: {ex}")

    with _lock:
        for lane in _lanes.values():
            _stop_lane_decoder(lane)
    handle_stop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
