from __future__ import annotations

import base64
import json
import math
import queue
import struct
import subprocess
import sys
import threading
import time
from pathlib import Path

import numpy as np

REPO_ROOT = Path(__file__).resolve().parents[1]
PY_WORKERS = REPO_ROOT / "src" / "ShackStack.DecoderWorkers.Python"
WORKER = PY_WORKERS / "Tools" / "cw_skimmer_sidecar_worker.py"
sys.path.insert(0, str(PY_WORKERS))

from modes.cw_engine import MORSE_ENCODE


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
        self._stdout: queue.Queue[dict[str, object]] = queue.Queue()
        self._stderr: queue.Queue[str] = queue.Queue()
        self._stdout_thread = threading.Thread(target=self._read_stdout, daemon=True)
        self._stderr_thread = threading.Thread(target=self._read_stderr, daemon=True)
        self._stdout_thread.start()
        self._stderr_thread.start()

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

    def send(self, payload: dict[str, object]) -> None:
        assert self.proc.stdin is not None
        self.proc.stdin.write(json.dumps(payload) + "\n")
        self.proc.stdin.flush()

    def drain(self, seconds: float = 0.25) -> list[dict[str, object]]:
        deadline = time.time() + seconds
        payloads: list[dict[str, object]] = []
        while time.time() < deadline:
            timeout = max(0.0, min(0.05, deadline - time.time()))
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


def generate_keyed_cw(
    text: str,
    *,
    sample_rate: int = 48000,
    tone_hz: float = 700.0,
    wpm: int = 20,
    amplitude: float = 0.45,
    drift_hz: float = 0.0,
) -> np.ndarray:
    dit = 1.2 / float(wpm)
    chunks: list[np.ndarray] = []
    cursor = 0

    def silence(seconds: float) -> None:
        nonlocal cursor
        count = int(round(seconds * sample_rate))
        chunks.append(np.zeros(count, dtype=np.float32))
        cursor += count

    def tone(seconds: float) -> None:
        nonlocal cursor
        count = int(round(seconds * sample_rate))
        if count <= 0:
            return
        t = (np.arange(count, dtype=np.float64) + cursor) / float(sample_rate)
        duration_cursor = cursor / float(sample_rate)
        freq = tone_hz + (drift_hz * duration_cursor)
        envelope = np.ones(count, dtype=np.float64)
        ramp = min(count // 2, max(8, int(sample_rate * 0.004)))
        envelope[:ramp] = np.linspace(0.0, 1.0, ramp)
        envelope[-ramp:] = np.linspace(1.0, 0.0, ramp)
        chunks.append((amplitude * envelope * np.sin(2.0 * math.pi * freq * t)).astype(np.float32))
        cursor += count

    silence(0.2)
    for word_index, word in enumerate(text.upper().split()):
        if word_index:
            silence(7.0 * dit)
        for char_index, char in enumerate(word):
            code = MORSE_ENCODE.get(char)
            if not code:
                continue
            for element_index, element in enumerate(code):
                tone((3.0 if element == "-" else 1.0) * dit)
                if element_index != len(code) - 1:
                    silence(dit)
            if char_index != len(word) - 1:
                silence(3.0 * dit)
    silence(0.2)
    return np.concatenate(chunks) if chunks else np.empty(0, dtype=np.float32)


def push_audio(session: WorkerSession, audio: np.ndarray, sample_rate: int = 48000) -> list[dict[str, object]]:
    payloads: list[dict[str, object]] = []
    for offset in range(0, len(audio), 2048):
        chunk = audio[offset : offset + 2048].astype(np.float32, copy=False)
        if chunk.size == 0:
            continue
        raw = struct.pack("<" + "f" * len(chunk), *chunk)
        session.send(
            {
                "type": "audio",
                "sampleRate": sample_rate,
                "channels": 1,
                "samples": base64.b64encode(raw).decode("ascii"),
            },
        )
        payloads.extend(session.drain(0.03))
    payloads.extend(session.drain(0.5))
    return payloads


def run_case(name: str, audio: np.ndarray, expected_tones: list[float]) -> bool:
    session = WorkerSession()
    try:
        session.drain(0.5)
        session.send({"type": "configure", "lowerAudioHz": 250, "upperAudioHz": 1800, "laneSpacingHz": 45, "maxLanes": 6, "wpmHint": 20})
        session.drain(0.5)
        session.send({"type": "start"})
        session.drain(0.5)
        payloads = push_audio(session, audio)
        candidates = [payload for payload in payloads if payload.get("type") == "candidate"]
        lane_ids = {int(payload.get("laneId", 0)) for payload in candidates}
        tones = [float(payload.get("audioToneHz", 0.0)) for payload in candidates]
        ok = bool(candidates)
        for expected in expected_tones:
            ok = ok and any(abs(tone - expected) < 70.0 for tone in tones)
        ok = ok and any(float(payload.get("toneProbability", 0.0)) > 0.2 for payload in candidates[:8])
        timing_hints = [str(payload.get("timingHint", "")) for payload in candidates if payload.get("timingHint")]
        decoded_text = "".join(str(payload.get("text", "")) for payload in candidates if payload.get("text"))
        transcripts = [str(payload.get("laneTranscript", "")) for payload in candidates if payload.get("laneTranscript")]
        provisional_count = sum(1 for payload in candidates if payload.get("isProvisionalText"))
        if name == "single-keyed-cw":
            ok = ok and any(("dit" in hint or "dah" in hint) for hint in timing_hints)
            ok = ok and len(decoded_text) >= 2
            ok = ok and any(ch in decoded_text for ch in "CQTEST")
            ok = ok and any(len(transcript) >= 1 for transcript in transcripts)
            ok = ok and provisional_count > 0
        if name == "drift":
            primary_lane_ids = {
                int(payload.get("laneId", 0))
                for payload in candidates
                if abs(float(payload.get("audioToneHz", 0.0)) - expected_tones[0]) < 90.0
            }
            ok = ok and 0 < len(primary_lane_ids) <= 2
        tone_probs = [round(float(payload.get("toneProbability", 0.0)), 2) for payload in candidates[:8]]
        bandwidths = [round(float(payload.get("occupiedBandwidthHz", 0.0)), 1) for payload in candidates[:8]]
        print(f"{'PASS' if ok else 'FAIL'} {name}: tones={tones[:8]} lanes={sorted(lane_ids)} toneP={tone_probs} bw={bandwidths} text={decoded_text[:12]!r} provisional={provisional_count} transcript={transcripts[-2:]} timing={timing_hints[:5]}")
        return ok
    finally:
        session.close()


def main() -> int:
    single = generate_keyed_cw("CQ TEST", tone_hz=700.0, wpm=20)
    dual = single + generate_keyed_cw("DE W8STR", tone_hz=1100.0, wpm=18, amplitude=0.32)[: len(single)]
    drift = generate_keyed_cw("CQ CQ TEST", tone_hz=650.0, wpm=22, drift_hz=8.0)

    results = [
        run_case("single-keyed-cw", single, [700.0]),
        run_case("dual-keyed-cw", dual, [700.0, 1100.0]),
        run_case("drift", drift, [650.0]),
    ]
    return 0 if all(results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
