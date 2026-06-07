from __future__ import annotations

import argparse
import json
import re
import shutil
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
from scipy.signal import butter, resample_poly, sosfilt


CALLISH_RE = re.compile(r"(?=.*[A-Z])(?=.*\d)[A-Z0-9/]{2,}")

SESSION_TAGS: dict[str, list[str]] = {
    "session-20260529T201748Z": ["rfi_stress", "older_sst"],
    "session-20260530T143718Z": ["rfi_stress", "psu_spike", "busy_cw"],
    "session-20260531T231934Z": ["contest", "crowded", "tight_lanes"],
    "session-20260531T232344Z": ["contest", "crowded", "tight_lanes"],
    "session-20260531T232616Z": ["contest", "cleaner_lanes"],
    "session-20260531T232938Z": ["contest", "low_band_edge"],
    "session-20260531T233227Z": ["contest", "messy_copy"],
    "session-20260531T233533Z": ["contest", "messy_copy"],
    "session-20260531T233857Z": ["contest", "busy_cw"],
    "session-20260531T234223Z": ["contest", "psu_spike", "hard_negative_source"],
}

LANE_OVERRIDES: dict[str, dict[int, tuple[str, str]]] = {
    "session-20260531T234223Z": {
        2: ("hard_negative_psu", "strong PSU/carrier spike lane near 1096 Hz"),
        3: ("hard_negative_psu", "strong PSU/carrier spike lane near 1300 Hz"),
        7: ("contaminated_keyed_cw", "real CW mixed with high-sideband interference"),
    },
    "session-20260530T143718Z": {
        0: ("skip", "manual/no-lane capture"),
    },
}


def utc_stamp() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def load_manifest(session: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    manifest = session / "manifest.jsonl"
    if not manifest.exists():
        return rows
    with manifest.open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            payload = json.loads(line)
            if payload.get("type") == "cw-skimmer-sample":
                rows.append(payload)
    return rows


def callish_tokens(text: str) -> list[str]:
    tokens = []
    for token in text.upper().replace("?", " ").split():
        token = token.strip(".,;:\\|")
        if CALLISH_RE.match(token):
            tokens.append(token)
    return tokens


def classify_sample(session_name: str, row: dict[str, Any]) -> tuple[str, str]:
    lane_id = int(row.get("laneId") or 0)
    if lane_id <= 0:
        return "skip", "manual/no-lane capture"

    override = LANE_OVERRIDES.get(session_name, {}).get(lane_id)
    if override is not None:
        return override

    transcript = str(row.get("laneTranscript") or "")
    tokens = callish_tokens(transcript)
    confidence = float(row.get("confidence") or 0.0)
    sidebands = int(row.get("sidebandCount") or 0)
    bandwidth = float(row.get("occupiedBandwidthHz") or 0.0)
    copy_rate = float(row.get("copyRateCharactersPerSecond") or 0.0)
    tone_probability = float(row.get("toneProbability") or 0.0)
    tags = SESSION_TAGS.get(session_name, [])

    has_contest_copy = bool(tokens) or "5NN" in transcript.upper() or "CQ TEST" in transcript.upper()
    very_wide = sidebands >= 55 or bandwidth >= 160.0
    plausible_copy_rate = 0.08 <= copy_rate <= 3.5

    if "psu_spike" in tags and very_wide and confidence >= 0.8 and len(transcript.strip()) <= 8:
        return "hard_negative_psu", "wide strong lane with little useful copy in PSU-tagged session"

    if has_contest_copy and plausible_copy_rate and confidence >= 0.33:
        if very_wide or tone_probability < 0.12:
            return "contaminated_keyed_cw", "contest copy with wide/dirty lane features"
        return "positive_keyed_cw", "contest/call-like copy"

    if very_wide and confidence >= 0.5 and not has_contest_copy:
        return "hard_negative_rfi", "wide high-confidence lane without call-like copy"

    if len(transcript.strip()) >= 10 and plausible_copy_rate:
        return "weak_positive_keyed_cw", "some sustained copy but no strong call token"

    return "skip", "insufficient copy or ambiguous lane"


def load_audio(session: Path, row: dict[str, Any]) -> np.ndarray:
    path = session / str(row["audioFile"]).replace("/", "\\")
    audio = np.fromfile(path, dtype="<f4")
    channels = int(row.get("channels") or row.get("sessionChannels") or 1)
    if channels > 1 and audio.size >= channels:
        audio = audio.reshape((-1, channels)).mean(axis=1)
    return np.nan_to_num(audio.astype(np.float32, copy=False))


def bandpass_lane(audio: np.ndarray, sample_rate: int, tone_hz: float, bandwidth_hz: float) -> np.ndarray:
    if tone_hz <= 0.0 or audio.size < 32:
        return audio
    half_width = max(120.0, bandwidth_hz / 2.0)
    low = max(40.0, tone_hz - half_width)
    high = min(sample_rate / 2.0 - 100.0, tone_hz + half_width)
    if high <= low + 40.0:
        return audio
    sos = butter(4, [low, high], btype="bandpass", fs=sample_rate, output="sos")
    return sosfilt(sos, audio).astype(np.float32)


def prepare_audio(
    session: Path,
    row: dict[str, Any],
    target_rate: int,
    seconds: float,
    bandwidth_hz: float,
) -> np.ndarray:
    source_rate = int(row.get("sampleRate") or row.get("sessionSampleRate") or 48000)
    audio = load_audio(session, row)
    audio = bandpass_lane(audio, source_rate, float(row.get("audioToneHz") or 0.0), bandwidth_hz)
    if source_rate != target_rate and audio.size:
        gcd = int(np.gcd(source_rate, target_rate))
        audio = resample_poly(audio, target_rate // gcd, source_rate // gcd).astype(np.float32)
    target_samples = int(round(target_rate * seconds))
    if audio.size > target_samples:
        start = max(0, (audio.size - target_samples) // 2)
        audio = audio[start : start + target_samples]
    elif audio.size < target_samples:
        audio = np.pad(audio, (0, target_samples - audio.size))
    peak = float(np.max(np.abs(audio))) if audio.size else 0.0
    if peak > 0.98:
        audio = audio * (0.98 / peak)
    return audio.astype("<f4", copy=False)


def split_for_session(session_name: str) -> str:
    validation_sessions = {
        "session-20260531T232616Z",
        "session-20260531T234223Z",
        "session-20260530T143718Z",
    }
    return "validation" if session_name in validation_sessions else "train"


def main() -> int:
    parser = argparse.ArgumentParser(description="Curate ShackStack real CW captures into lane-conditioned training clips.")
    parser.add_argument("root", type=Path)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--exclude-newest", action="store_true")
    parser.add_argument("--target-sample-rate", type=int, default=12000)
    parser.add_argument("--seconds", type=float, default=8.0)
    parser.add_argument("--bandwidth-hz", type=float, default=360.0)
    parser.add_argument("--copy-audio", action="store_true")
    parser.add_argument("--max-per-session", type=int, default=0)
    args = parser.parse_args()

    sessions = sorted((path for path in args.root.iterdir() if path.is_dir()), key=lambda path: path.name)
    excluded_session = None
    if args.exclude_newest and sessions:
        newest = max(sessions, key=lambda path: path.stat().st_mtime)
        excluded_session = newest.name
        sessions = [session for session in sessions if session != newest]

    out = args.out or (Path.home() / "ShackStack" / "cw-training" / f"real-curated-{utc_stamp()}")
    audio_dir = out / "audio"
    out.mkdir(parents=True, exist_ok=True)
    if args.copy_audio:
        audio_dir.mkdir(parents=True, exist_ok=True)

    rows: list[dict[str, Any]] = []
    label_counts: Counter[str] = Counter()
    split_counts: Counter[str] = Counter()
    session_counts: Counter[str] = Counter()

    for session in sessions:
        manifest_rows = load_manifest(session)
        emitted_for_session = 0
        for row in manifest_rows:
            label, reason = classify_sample(session.name, row)
            if label == "skip":
                label_counts[label] += 1
                continue
            if args.max_per_session > 0 and emitted_for_session >= args.max_per_session:
                continue

            split = split_for_session(session.name)
            curated_id = f"{session.name}-{row.get('id')}"
            audio_file = ""
            if args.copy_audio:
                audio = prepare_audio(session, row, args.target_sample_rate, args.seconds, args.bandwidth_hz)
                audio_file = f"audio/{curated_id}.f32"
                (audio_dir / f"{curated_id}.f32").write_bytes(audio.tobytes())
            else:
                audio_file = str(session / str(row["audioFile"]).replace("/", "\\"))

            transcript = str(row.get("laneTranscript") or "")
            curated = {
                "type": "cw-real-curated-sample",
                "id": curated_id,
                "split": split,
                "label": label,
                "labelReason": reason,
                "session": session.name,
                "sourceAudioFile": str(session / str(row["audioFile"]).replace("/", "\\")),
                "audioFile": audio_file,
                "audioFormat": "float32-le",
                "sampleRate": args.target_sample_rate if args.copy_audio else int(row.get("sampleRate") or row.get("sessionSampleRate") or 48000),
                "channels": 1 if args.copy_audio else int(row.get("channels") or row.get("sessionChannels") or 1),
                "durationSeconds": args.seconds if args.copy_audio else float(row.get("durationSeconds") or 0.0),
                "radioFrequencyHz": row.get("radioFrequencyHz"),
                "radioMode": row.get("radioMode"),
                "laneId": row.get("laneId"),
                "audioToneHz": row.get("audioToneHz"),
                "confidence": row.get("confidence"),
                "toneProbability": row.get("toneProbability"),
                "sidebandCount": row.get("sidebandCount"),
                "occupiedBandwidthHz": row.get("occupiedBandwidthHz"),
                "copyRateCharactersPerSecond": row.get("copyRateCharactersPerSecond"),
                "callishTokens": callish_tokens(transcript)[:12],
                "laneTranscript": transcript[:260],
                "tags": SESSION_TAGS.get(session.name, []),
            }
            rows.append(curated)
            emitted_for_session += 1
            label_counts[label] += 1
            split_counts[split] += 1
            session_counts[session.name] += 1

    header = {
        "type": "cw-real-curated-dataset",
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "root": str(args.root),
        "excludedNewestSession": excluded_session,
        "sampleCount": len(rows),
        "copyAudio": bool(args.copy_audio),
        "targetSampleRate": args.target_sample_rate,
        "seconds": args.seconds,
        "bandwidthHz": args.bandwidth_hz,
        "labelCounts": dict(label_counts),
        "splitCounts": dict(split_counts),
        "sessionCounts": dict(session_counts),
    }

    with (out / "manifest.jsonl").open("w", encoding="utf-8") as handle:
        handle.write(json.dumps(header, sort_keys=True) + "\n")
        for row in rows:
            handle.write(json.dumps(row, sort_keys=True) + "\n")

    (out / "summary.json").write_text(json.dumps(header, indent=2), encoding="utf-8")
    if not args.copy_audio:
        shutil.copyfile(args.root / "dataset-index.json", out / "source-dataset-index.json")

    print(f"wrote {out}")
    print(f"samples={len(rows)} labels={dict(label_counts)} splits={dict(split_counts)}")
    if excluded_session:
        print(f"excluded newest in-progress session: {excluded_session}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
