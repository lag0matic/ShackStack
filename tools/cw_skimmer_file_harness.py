from __future__ import annotations

import argparse
import base64
import json
import queue
import struct
import subprocess
import sys
import threading
import time
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

import av
import numpy as np

REPO_ROOT = Path(__file__).resolve().parents[1]
PY_WORKERS = REPO_ROOT / "src" / "ShackStack.DecoderWorkers.Python"
WORKER = PY_WORKERS / "Tools" / "cw_skimmer_sidecar_worker.py"


class WorkerSession:
    def __init__(self) -> None:
        self.proc = subprocess.Popen(
            [sys.executable, str(WORKER)],
            cwd=str(REPO_ROOT),
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        self._stdout: queue.Queue[dict[str, Any]] = queue.Queue()
        self._stderr: queue.Queue[str] = queue.Queue()
        threading.Thread(target=self._read_stdout, daemon=True).start()
        threading.Thread(target=self._read_stderr, daemon=True).start()

    def _read_stdout(self) -> None:
        assert self.proc.stdout is not None
        for line in self.proc.stdout:
            line = line.strip()
            if not line:
                continue
            try:
                self._stdout.put(json.loads(line))
            except Exception:
                self._stdout.put({"type": "parse_error", "line": line})

    def _read_stderr(self) -> None:
        assert self.proc.stderr is not None
        for line in self.proc.stderr:
            self._stderr.put(line.rstrip())

    def send(self, payload: dict[str, Any]) -> None:
        assert self.proc.stdin is not None
        self.proc.stdin.write(json.dumps(payload) + "\n")
        self.proc.stdin.flush()

    def drain(self, seconds: float = 0.05) -> list[dict[str, Any]]:
        deadline = time.time() + seconds
        payloads: list[dict[str, Any]] = []
        while time.time() < deadline:
            timeout = max(0.0, min(0.02, deadline - time.time()))
            try:
                payloads.append(self._stdout.get(timeout=timeout))
            except queue.Empty:
                pass
        return payloads

    def close(self) -> None:
        try:
            self.send({"type": "shutdown"})
            self.proc.wait(timeout=2)
        except Exception:
            self.proc.kill()


def decode_m4a(path: Path, target_rate: int = 48000) -> np.ndarray:
    container = av.open(str(path))
    stream = next(s for s in container.streams if s.type == "audio")
    resampler = av.AudioResampler(format="flt", layout="mono", rate=target_rate)
    chunks: list[np.ndarray] = []

    for frame in container.decode(stream):
        frames = resampler.resample(frame)
        if not isinstance(frames, list):
            frames = [frames]
        for resampled in frames:
            arr = resampled.to_ndarray()
            arr = np.asarray(arr, dtype=np.float32)
            if arr.ndim == 2:
                arr = arr.mean(axis=0)
            chunks.append(arr.reshape(-1))

    return np.concatenate(chunks).astype(np.float32, copy=False) if chunks else np.empty(0, dtype=np.float32)


def load_capture_session_audio(path: Path) -> tuple[np.ndarray, int]:
    session_audio = path / "wideband.f32"
    if not session_audio.exists():
        return np.empty(0, dtype=np.float32), 48000

    sample_rate = 48000
    channels = 1
    manifest = path / "manifest.jsonl"
    if manifest.exists():
        with manifest.open("r", encoding="utf-8") as handle:
            for line in handle:
                if not line.strip():
                    continue
                try:
                    payload = json.loads(line)
                except Exception:
                    continue
                if payload.get("type") == "cw-skimmer-sample":
                    sample_rate = int(payload.get("sessionSampleRate") or payload.get("sampleRate") or sample_rate)
                    channels = int(payload.get("sessionChannels") or payload.get("channels") or channels)
                    break

    audio = np.fromfile(session_audio, dtype="<f4").astype(np.float32, copy=False)
    if channels > 1 and audio.size >= channels:
        usable = (audio.size // channels) * channels
        audio = audio[:usable].reshape(-1, channels).mean(axis=1).astype(np.float32, copy=False)
    return audio, sample_rate


def send_audio(session: WorkerSession, samples: np.ndarray, sample_rate: int, chunk_size: int) -> list[dict[str, Any]]:
    payloads: list[dict[str, Any]] = []
    for offset in range(0, len(samples), chunk_size):
        chunk = samples[offset : offset + chunk_size].astype(np.float32, copy=False)
        if chunk.size == 0:
            continue
        raw = struct.pack("<" + "f" * len(chunk), *chunk)
        session.send(
            {
                "type": "audio",
                "sampleRate": sample_rate,
                "channels": 1,
                "samples": base64.b64encode(raw).decode("ascii"),
            }
        )
        payloads.extend(session.drain(0.005))
    payloads.extend(session.drain(0.5))
    return payloads


def run_skimmer(
    audio: np.ndarray,
    *,
    sample_rate: int = 48000,
    low: int = 450,
    high: int = 2800,
    spacing: int = 45,
    lanes: int = 8,
    chunk_size: int = 2048,
) -> list[dict[str, Any]]:
    session = WorkerSession()
    try:
        session.drain(0.25)
        session.send(
            {
                "type": "configure",
                "lowerAudioHz": low,
                "upperAudioHz": high,
                "laneSpacingHz": spacing,
                "maxLanes": lanes,
                "wpmHint": 20,
                "scoutEngine": "Spectral Scout File Harness",
            }
        )
        session.drain(0.25)
        session.send({"type": "start"})
        session.drain(0.25)
        return send_audio(session, audio, sample_rate, chunk_size)
    finally:
        session.close()


def summarize_skimmer_payloads(payloads: list[dict[str, Any]]) -> dict[str, Any]:
    candidates = [payload for payload in payloads if payload.get("type") == "candidate"]
    telemetry = [payload for payload in payloads if payload.get("type") == "telemetry"]
    transcripts: dict[int, str] = {}
    lane_tones: dict[int, list[float]] = defaultdict(list)
    lane_snr: dict[int, list[float]] = defaultdict(list)
    lane_copy_rate: dict[int, float] = {}
    text_counter: Counter[str] = Counter()
    committed_text_counter: Counter[str] = Counter()
    provisional_text_counter: Counter[str] = Counter()
    timing_counter: Counter[str] = Counter()

    for payload in candidates:
        lane_id = int(payload.get("laneId", 0))
        if lane_id:
            lane_tones[lane_id].append(float(payload.get("audioToneHz", 0.0)))
            lane_snr[lane_id].append(float(payload.get("confidence", 0.0)))
            transcript = str(payload.get("laneTranscript", ""))
            if transcript:
                transcripts[lane_id] = transcript
                lane_copy_rate[lane_id] = float(payload.get("copyRateCharactersPerSecond", 0.0))
        text = str(payload.get("text", ""))
        if text:
            text_counter[text] += 1
            if payload.get("isProvisionalText"):
                provisional_text_counter[text] += 1
            else:
                committed_text_counter[text] += 1
        hint = str(payload.get("timingHint", ""))
        if hint:
            timing_counter[hint.split(" ", 1)[0]] += 1

    lane_summaries = []
    for lane_id, tones in lane_tones.items():
        avg_tone = sum(tones) / len(tones)
        confidence = sum(lane_snr.get(lane_id, [0.0])) / max(1, len(lane_snr.get(lane_id, [])))
        transcript = transcripts.get(lane_id, "")
        copy_rate = lane_copy_rate.get(lane_id, 0.0)
        lane_summaries.append(
            {
                "laneId": lane_id,
                "avgToneHz": avg_tone,
                "confidence": confidence,
                "transcript": transcript,
                "copyRate": copy_rate,
                "candidateCount": len(tones),
            }
        )

    lane_summaries.sort(key=lambda item: (-float(item["confidence"]), -int(item["candidateCount"])))

    return {
        "candidates": candidates,
        "telemetry": telemetry,
        "laneSummaries": lane_summaries,
        "textCounter": text_counter,
        "committedTextCounter": committed_text_counter,
        "provisionalTextCounter": provisional_text_counter,
        "timingCounter": timing_counter,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the CW skimmer worker against an audio file.")
    parser.add_argument("audio_file", type=Path)
    parser.add_argument("--low", type=int, default=250)
    parser.add_argument("--high", type=int, default=2800)
    parser.add_argument("--spacing", type=int, default=45)
    parser.add_argument("--lanes", type=int, default=8)
    parser.add_argument("--chunk", type=int, default=2048)
    args = parser.parse_args()

    if args.audio_file.is_dir():
        audio, sample_rate = load_capture_session_audio(args.audio_file)
    else:
        audio = decode_m4a(args.audio_file)
        sample_rate = 48000
    if audio.size == 0:
        print("No audio decoded.")
        return 1

    duration = audio.size / float(sample_rate)
    rms = float(np.sqrt(np.mean(audio * audio)))
    peak = float(np.max(np.abs(audio)))
    print(f"decoded samples={audio.size} duration={duration:0.1f}s rms={rms:0.4f} peak={peak:0.4f}")

    payloads = run_skimmer(audio, sample_rate=sample_rate, low=args.low, high=args.high, spacing=args.spacing, lanes=args.lanes, chunk_size=args.chunk)
    summary = summarize_skimmer_payloads(payloads)

    print(f"telemetry={len(summary['telemetry'])} candidates={len(summary['candidates'])} lanes={len(summary['laneSummaries'])} textEvents={sum(summary['textCounter'].values())}")
    print("transcripts:")
    for lane in summary["laneSummaries"]:
        transcript = str(lane["transcript"])
        if not transcript:
            continue
        marker = "*" if float(lane["confidence"]) >= 0.25 and len(transcript) >= 2 and float(lane["copyRate"]) <= 4.0 else " "
        print(f"{marker} lane {int(lane['laneId']):>2} {float(lane['avgToneHz']):7.1f} Hz conf={float(lane['confidence']):0.2f} cps={float(lane['copyRate']):0.1f}: {transcript[:100]}")

    print("top text events:", summary["textCounter"].most_common(12))
    print("top committed text:", summary["committedTextCounter"].most_common(12))
    print("top provisional text:", summary["provisionalTextCounter"].most_common(12))
    print("timing event types:", summary["timingCounter"].most_common())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
