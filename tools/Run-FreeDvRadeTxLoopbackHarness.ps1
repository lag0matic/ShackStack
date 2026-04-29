param(
    [string]$WorkerDir = ".\src\ShackStack.DecoderHost.GplCodec2Freedv\bin\Release\net9.0",
    [string]$Callsign = "W8STR",
    [int]$Seconds = 8
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$WorkerDir = if ([System.IO.Path]::IsPathRooted($WorkerDir)) {
    [System.IO.Path]::GetFullPath($WorkerDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $WorkerDir))
}

$exe = Join-Path $WorkerDir "ShackStack.DecoderHost.GplCodec2Freedv.exe"
if (!(Test-Path $exe)) {
    throw "FreeDV sidecar not found: $exe"
}

$python = @'
import base64
import json
import math
import os
import struct
import subprocess
import sys

exe = sys.argv[1]
callsign = sys.argv[2]
seconds = max(1, int(sys.argv[3]))

def run_worker(lines, timeout):
    process = subprocess.Popen(
        [exe],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        cwd=os.path.dirname(exe))
    payload = ''.join(json.dumps(line) + '\n' for line in lines)
    stdout, stderr = process.communicate(payload, timeout=timeout)
    return stdout, stderr, process.returncode

speech_rate = 16000
speech = bytearray()
for i in range(speech_rate * seconds):
    envelope = 0.1 + 0.15 * (0.5 + 0.5 * math.sin(2.0 * math.pi * 3.0 * i / speech_rate))
    sample = envelope * (
        math.sin(2.0 * math.pi * 220.0 * i / speech_rate)
        + 0.45 * math.sin(2.0 * math.pi * 660.0 * i / speech_rate))
    speech += struct.pack('<f', sample)

tx_lines = [
    {'type': 'configure', 'modeLabel': 'RADEV1', 'rxFrequencyOffsetHz': 0, 'transmitCallsign': callsign},
    {'type': 'startTx'},
    {'type': 'speech', 'sampleRate': speech_rate, 'channels': 1, 'samples': base64.b64encode(speech).decode('ascii')},
    {'type': 'stopTx'},
    {'type': 'shutdown'},
]

tx_stdout, tx_stderr, tx_code = run_worker(tx_lines, timeout=90)
modem_pcm = bytearray()
tx_statuses = []
for line in tx_stdout.splitlines():
    try:
        message = json.loads(line)
    except Exception:
        continue
    if message.get('type') == 'modem':
        modem_pcm += base64.b64decode(message.get('pcm16', ''))
    elif message.get('type') == 'telemetry':
        tx_statuses.append(message.get('status', ''))

if tx_code != 0 or not modem_pcm:
    print(json.dumps({
        'status': 'failed',
        'stage': 'tx',
        'exitCode': tx_code,
        'modemSamples': len(modem_pcm) // 2,
        'stderr': tx_stderr,
        'tail': tx_statuses[-8:],
    }, indent=2))
    sys.exit(1)

modem_shorts = struct.unpack('<' + 'h' * (len(modem_pcm) // 2), modem_pcm)
rx_lines = [
    {'type': 'configure', 'modeLabel': 'RADEV1', 'rxFrequencyOffsetHz': 0},
    {'type': 'start'},
]
for offset in range(0, len(modem_shorts), 2048):
    chunk = bytearray()
    for value in modem_shorts[offset:offset + 2048]:
        chunk += struct.pack('<f', max(-1.0, min(1.0, value / 32768.0)))
    rx_lines.append({
        'type': 'audio',
        'sampleRate': 8000,
        'channels': 1,
        'samples': base64.b64encode(chunk).decode('ascii'),
    })
rx_lines.append({'type': 'shutdown'})

rx_stdout, rx_stderr, rx_code = run_worker(rx_lines, timeout=120)
decoded_speech_samples = 0
sync_frames = 0
decoded_callsigns = []
rx_statuses = []
for line in rx_stdout.splitlines():
    try:
        message = json.loads(line)
    except Exception:
        continue
    if message.get('type') == 'speech':
        decoded_speech_samples += len(base64.b64decode(message.get('pcm16', ''))) // 2
    elif message.get('type') == 'telemetry':
        status = message.get('status', '')
        rx_statuses.append(status)
        if 'sync 1' in status:
            sync_frames += 1
        if message.get('radeCallsign'):
            decoded_callsigns.append(message['radeCallsign'])

ok = (
    rx_code == 0
    and decoded_speech_samples > 0
    and sync_frames > 0
    and callsign.upper() in [call.upper() for call in decoded_callsigns])

print(json.dumps({
    'status': 'passed' if ok else 'failed',
    'txModemSamples': len(modem_pcm) // 2,
    'rxSpeechSamples': decoded_speech_samples,
    'rxSyncFrames': sync_frames,
    'decodedCallsigns': decoded_callsigns[-5:],
    'txTail': tx_statuses[-5:],
    'rxTail': rx_statuses[-8:],
    'txStderr': tx_stderr.strip(),
    'rxStderr': rx_stderr.strip(),
}, indent=2))

sys.exit(0 if ok else 1)
'@

$tempScript = Join-Path ([System.IO.Path]::GetTempPath()) "shackstack_rade_loopback_$PID.py"
Set-Content -Path $tempScript -Value $python -Encoding UTF8
try {
    & python $tempScript $exe $Callsign $Seconds
    if ($LASTEXITCODE -ne 0) {
        throw "RADEV1 TX loopback harness failed."
    }
}
finally {
    Remove-Item -LiteralPath $tempScript -Force -ErrorAction SilentlyContinue
}
