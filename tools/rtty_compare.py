#!/usr/bin/env python3
"""Replay a WAV through ShackStack RTTY and optional external comparators."""

from __future__ import annotations

import argparse
import base64
import json
import math
import shutil
import struct
import subprocess
import sys
import wave
from pathlib import Path


def read_wav_float32(path: Path) -> tuple[int, int, list[float]]:
    with wave.open(str(path), "rb") as wav:
        channels = wav.getnchannels()
        sample_rate = wav.getframerate()
        sample_width = wav.getsampwidth()
        frames = wav.readframes(wav.getnframes())

    if sample_width == 1:
        samples = [(b - 128) / 128.0 for b in frames]
    elif sample_width == 2:
        count = len(frames) // 2
        samples = [v / 32768.0 for v in struct.unpack("<" + ("h" * count), frames)]
    elif sample_width == 3:
        samples = []
        for i in range(0, len(frames), 3):
            value = frames[i] | (frames[i + 1] << 8) | (frames[i + 2] << 16)
            if value & 0x800000:
                value -= 0x1000000
            samples.append(value / 8388608.0)
    elif sample_width == 4:
        count = len(frames) // 4
        ints = struct.unpack("<" + ("i" * count), frames)
        samples = [v / 2147483648.0 for v in ints]
    else:
        raise ValueError(f"Unsupported WAV sample width: {sample_width}")

    return sample_rate, channels, samples


def write_mono_wav(path: Path, sample_rate: int, channels: int, samples: list[float]) -> None:
    mono: list[float]
    if channels == 1:
        mono = samples
    else:
        mono = []
        frames = len(samples) // channels
        for frame in range(frames):
            start = frame * channels
            mono.append(sum(samples[start : start + channels]) / channels)

    payload = bytearray()
    for sample in mono:
        value = max(-32768, min(32767, int(round(sample * 32767.0))))
        payload.extend(struct.pack("<h", value))

    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        wav.writeframes(bytes(payload))


def run_shackstack_sidecar(
    repo: Path,
    wav_path: Path,
    profile: str,
    shift: int,
    baud: float,
    audio_center: float,
    reverse: bool,
) -> str:
    exe = repo / "src" / "ShackStack.DecoderHost.GplFldigiRtty" / "bin" / "Release" / "net9.0" / "ShackStack.DecoderHost.GplFldigiRtty.exe"
    if not exe.exists():
        subprocess.run(
            ["dotnet", "build", str(repo / "src" / "ShackStack.DecoderHost.GplFldigiRtty" / "ShackStack.DecoderHost.GplFldigiRtty.csproj"), "-c", "Release"],
            check=True,
            cwd=repo,
        )

    sample_rate, channels, samples = read_wav_float32(wav_path)
    proc = subprocess.Popen(
        [str(exe)],
        cwd=repo,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
    )

    assert proc.stdin is not None
    proc.stdin.write(json.dumps({
        "type": "configure",
        "profileLabel": profile,
        "shiftHz": shift,
        "baudRate": baud,
        "frequencyLabel": str(wav_path),
        "audioCenterHz": audio_center,
        "reversePolarity": reverse,
    }) + "\n")
    proc.stdin.write(json.dumps({"type": "start"}) + "\n")

    chunk_frames = sample_rate
    chunk_samples = chunk_frames * channels
    for start in range(0, len(samples), chunk_samples):
        chunk = samples[start : start + chunk_samples]
        payload = struct.pack("<" + ("f" * len(chunk)), *chunk)
        proc.stdin.write(json.dumps({
            "type": "audio",
            "sampleRate": sample_rate,
            "channels": channels,
            "samples": base64.b64encode(payload).decode("ascii"),
        }) + "\n")
        proc.stdin.flush()

    proc.stdin.write(json.dumps({"type": "stop"}) + "\n")
    proc.stdin.close()
    stdout, stderr = proc.communicate(timeout=60)
    if stderr.strip():
        print("ShackStack sidecar stderr:", stderr.strip(), file=sys.stderr)

    decoded: list[str] = []
    telemetry: list[str] = []
    for line in stdout.splitlines():
        try:
            message = json.loads(line)
        except json.JSONDecodeError:
            continue
        if message.get("type") == "decode":
            decoded.append(message.get("text", ""))
        elif message.get("type") == "telemetry":
            status = message.get("status", "")
            if "auto carrier lock" in status or "RTTY running" in status:
                telemetry.append(status)

    if telemetry:
        print("ShackStack telemetry:")
        for line in telemetry[-5:]:
            print(f"  {line}")

    return "".join(decoded)


def windows_path_to_wsl(path: Path) -> str:
    resolved = path.resolve()
    drive = resolved.drive.rstrip(":").lower()
    parts = [part for part in resolved.parts[1:]]
    escaped = "/".join(part.replace("\\", "/") for part in parts)
    return f"/mnt/{drive}/{escaped}"


def run_minimodem(
    wav_path: Path,
    sample_rate: int,
    channels: int,
    samples: list[float],
    audio_center: float,
    shift: int,
    reverse: bool,
) -> str | None:
    if shutil.which("wsl") is None:
        return None

    check = subprocess.run(["wsl", "sh", "-lc", "command -v minimodem"], text=True, capture_output=True)
    if check.returncode != 0:
        return None

    temp_root = wav_path.parent / ".tmp-rtty-compare"
    temp_root.mkdir(exist_ok=True)
    mono_path = temp_root / "input-mono.wav"
    try:
        write_mono_wav(mono_path, sample_rate, channels, samples)
        wsl_path = windows_path_to_wsl(mono_path)
        mark = audio_center + (shift / 2.0)
        space = audio_center - (shift / 2.0)
        if reverse:
            mark, space = space, mark
        command = f"minimodem --rx --stopbits 1 -M {mark:0.3f} -S {space:0.3f} -f {json.dumps(wsl_path)} rtty"
        result = subprocess.run(["wsl", "sh", "-lc", command], text=True, capture_output=True, timeout=90)
        if result.stderr.strip():
            print("minimodem stderr:", result.stderr.strip(), file=sys.stderr)
        return result.stdout
    finally:
        try:
            mono_path.unlink()
        except FileNotFoundError:
            pass


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--profile", default="170 Hz / 45.45 baud")
    parser.add_argument("--shift", type=int, default=170)
    parser.add_argument("--baud", type=float, default=45.45)
    parser.add_argument("--audio-center", type=float, default=1700.0)
    parser.add_argument("--reverse", action="store_true")
    parser.add_argument("--minimodem", action="store_true", help="Also run WSL minimodem if installed.")
    args = parser.parse_args()

    wav_path = args.wav.resolve()
    if not wav_path.exists():
        raise FileNotFoundError(wav_path)

    sample_rate, channels, samples = read_wav_float32(wav_path)

    print("=== ShackStack fldigi-derived sidecar ===")
    shackstack = run_shackstack_sidecar(args.repo.resolve(), wav_path, args.profile, args.shift, args.baud, args.audio_center, args.reverse)
    print(shackstack or "(no decoded text)")

    if args.minimodem:
        print()
        print("=== minimodem ===")
        minimodem = run_minimodem(wav_path, sample_rate, channels, samples, args.audio_center, args.shift, args.reverse)
        print(minimodem if minimodem is not None and minimodem.strip() else "(minimodem unavailable or no decoded text)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
