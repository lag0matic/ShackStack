param(
    [string]$InputWav = "",
    [int[]]$OffsetsHz = @(0),
    [string]$WorkerDir = "",
    [string]$OutDir = ".tmp-freedv-rade-harness"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($WorkerDir)) {
    $WorkerDir = Join-Path $PSScriptRoot "..\src\ShackStack.Desktop\DecoderWorkers\freedv_codec2_sidecar"
}

$WorkerDir = if ([System.IO.Path]::IsPathRooted($WorkerDir)) {
    [System.IO.Path]::GetFullPath($WorkerDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $WorkerDir))
}

$OutDir = if ([System.IO.Path]::IsPathRooted($OutDir)) {
    [System.IO.Path]::GetFullPath($OutDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutDir))
}

if ([string]::IsNullOrWhiteSpace($InputWav)) {
    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    $candidates = @(
        (Join-Path $repoRoot ".tmp-freedv-gui-repo\wav\all_radev1.wav"),
        (Join-Path $repoRoot ".tmp-radae-nopy-repo\FDV_offair.wav"),
        "C:\Users\lag0m\Documents\Sound Recordings\Recording (15).wav"
    )

    $InputWav = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($InputWav) -or !(Test-Path $InputWav)) {
    throw "No RADEV1 input WAV found. Pass -InputWav with a modem WAV."
}

$workerExe = Join-Path $WorkerDir "ShackStack.DecoderHost.GplCodec2Freedv.exe"
if (!(Test-Path $workerExe)) {
    throw "FreeDV sidecar not found: $workerExe"
}

foreach ($required in @("librade.dll", "lpcnet_demo.exe")) {
    $path = Join-Path $WorkerDir $required
    if (!(Test-Path $path)) {
        throw "RADEV1 runtime file missing: $path"
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$offsetJson = "[" + (($OffsetsHz | ForEach-Object { [string][int]$_ }) -join ",") + "]"
$python = @'
import base64
import json
import struct
import subprocess
import sys
import wave
from pathlib import Path

worker = Path(sys.argv[1])
input_wav = Path(sys.argv[2])
out_dir = Path(sys.argv[3])
offsets = json.loads(sys.argv[4])
out_dir.mkdir(parents=True, exist_ok=True)

def write_wav(path, pcm, sample_rate):
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        wf.writeframes(pcm)

with wave.open(str(input_wav), "rb") as wf:
    channels = wf.getnchannels()
    sample_rate = wf.getframerate()
    sample_width = wf.getsampwidth()
    frames = wf.readframes(wf.getnframes())

if sample_width != 2:
    raise SystemExit(f"Only 16-bit PCM WAV is supported; got {sample_width * 8}-bit")

samples_i16 = struct.unpack("<" + "h" * (len(frames) // 2), frames)
samples_f32 = [max(-1.0, min(1.0, sample / 32768.0)) for sample in samples_i16]
frames_per_chunk = max(1, sample_rate // 2)
float_per_chunk = frames_per_chunk * channels
audio_messages = []

for start in range(0, len(samples_f32), float_per_chunk):
    chunk = samples_f32[start:start + float_per_chunk]
    raw = struct.pack("<" + "f" * len(chunk), *chunk)
    audio_messages.append(json.dumps({
        "type": "audio",
        "sampleRate": sample_rate,
        "channels": channels,
        "samples": base64.b64encode(raw).decode("ascii"),
    }, separators=(",", ":")))

results = []
for offset in offsets:
    input_path = out_dir / f"radev1_{offset:+d}.jsonl"
    stdout_path = out_dir / f"radev1_{offset:+d}.stdout.jsonl"
    speech_path = out_dir / f"radev1_{offset:+d}.wav"
    lines = [
        json.dumps({"type": "configure", "modeLabel": "RADEV1", "rxFrequencyOffsetHz": int(offset)}, separators=(",", ":")),
        json.dumps({"type": "start"}, separators=(",", ":")),
        *audio_messages,
        json.dumps({"type": "shutdown"}, separators=(",", ":")),
    ]
    input_path.write_text("\n".join(lines) + "\n", encoding="ascii")

    with input_path.open("r", encoding="ascii") as stdin, stdout_path.open("w", encoding="utf-8") as stdout:
        subprocess.run([str(worker)], stdin=stdin, stdout=stdout, stderr=subprocess.STDOUT, timeout=120, check=False)

    speech = bytearray()
    frames_seen = 0
    sync_frames = 0
    snrs = []
    callsigns = set()
    statuses = []

    for line in stdout_path.read_text(encoding="utf-8", errors="replace").splitlines():
        try:
            message = json.loads(line)
        except Exception:
            continue

        if message.get("type") == "speech":
            speech.extend(base64.b64decode(message.get("pcm16") or ""))
            continue

        if message.get("type") != "telemetry":
            continue

        status = message.get("status") or ""
        statuses.append(status)
        if message.get("radeCallsign"):
            callsigns.add(message["radeCallsign"])
        if "RX frame" in status:
            frames_seen += 1
            if message.get("syncPercent", 0) > 0:
                sync_frames += 1
            try:
                snrs.append(float(message.get("snrDb") or 0.0))
            except Exception:
                pass

    if speech:
        write_wav(speech_path, bytes(speech), 16000)

    results.append({
        "offsetHz": offset,
        "inputSeconds": round(len(samples_i16) / channels / sample_rate, 3),
        "decodedSpeechSeconds": round(len(speech) / 2 / 16000, 3),
        "framesSeen": frames_seen,
        "syncFrames": sync_frames,
        "averageSnrDb": round(sum(snrs) / len(snrs), 2) if snrs else 0.0,
        "callsigns": sorted(callsigns),
        "decodedWav": str(speech_path) if speech else "",
        "stdout": str(stdout_path),
        "lastStatus": statuses[-1] if statuses else "",
    })

(out_dir / "radev1_results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")

print(f"Input: {input_wav}")
for result in sorted(results, key=lambda item: item["decodedSpeechSeconds"], reverse=True):
    calls = ",".join(result["callsigns"]) if result["callsigns"] else "-"
    print(
        f"RADEV1 {result['offsetHz']:+5d} Hz | "
        f"decoded {result['decodedSpeechSeconds']:6.2f}s/{result['inputSeconds']:6.2f}s | "
        f"sync {result['syncFrames']:4d}/{result['framesSeen']:4d} | "
        f"SNR {result['averageSnrDb']:6.1f} dB | "
        f"calls {calls} | {result['decodedWav']}"
    )
print(f"Results: {out_dir / 'radev1_results.json'}")
'@

$scriptPath = Join-Path $OutDir "run_radev1_harness.py"
[System.IO.File]::WriteAllText($scriptPath, $python, [System.Text.UTF8Encoding]::new($false))

python $scriptPath $workerExe ([System.IO.Path]::GetFullPath($InputWav)) $OutDir $offsetJson
