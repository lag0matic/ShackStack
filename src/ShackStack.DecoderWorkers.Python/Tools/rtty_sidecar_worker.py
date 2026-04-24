import base64
import json
import math
import sys
from collections import deque

import numpy as np
from scipy.signal import butter, sosfilt, sosfilt_zi, resample_poly


class RttyWorker:
    def __init__(self):
        self.profile_label = "170 Hz / 45.45 baud"
        self.shift_hz = 170
        self.baud_rate = 45.45
        self.frequency_label = "14.080 MHz USB"
        self.is_running = False
        self.output_sr = 12000
        self.center_hz = 1615.0
        self.mark_hz = self.center_hz + (self.shift_hz / 2.0)
        self.space_hz = self.center_hz - (self.shift_hz / 2.0)
        self.frame_len = 512
        self.signal_level_percent = 0
        self.mark_buf = deque(maxlen=18)
        self.space_buf = deque(maxlen=18)
        self.bias_buf = deque(maxlen=18)
        self.last_status = "RTTY scaffold ready"
        self._sample_buf = np.zeros(0, dtype=np.float32)
        self._mark_sos = None
        self._space_sos = None
        self._mark_zi = None
        self._space_zi = None
        self._build_filters()

    def _build_filters(self):
        nyq = self.output_sr / 2.0
        bw = 70.0
        mark_lo = max(50.0, self.mark_hz - bw) / nyq
        mark_hi = min(nyq * 0.99, self.mark_hz + bw) / nyq
        space_lo = max(50.0, self.space_hz - bw) / nyq
        space_hi = min(nyq * 0.99, self.space_hz + bw) / nyq
        self._mark_sos = butter(4, [mark_lo, mark_hi], btype="band", output="sos")
        self._space_sos = butter(4, [space_lo, space_hi], btype="band", output="sos")
        self._mark_zi = sosfilt_zi(self._mark_sos)
        self._space_zi = sosfilt_zi(self._space_sos)

    def configure(self, payload):
        self.profile_label = payload.get("profileLabel", self.profile_label)
        self.shift_hz = int(payload.get("shiftHz", self.shift_hz))
        self.baud_rate = float(payload.get("baudRate", self.baud_rate))
        self.frequency_label = payload.get("frequencyLabel", self.frequency_label)
        self.mark_hz = self.center_hz + (self.shift_hz / 2.0)
        self.space_hz = self.center_hz - (self.shift_hz / 2.0)
        self._build_filters()
        self.last_status = f"Configured {self.profile_label}"
        self.emit_telemetry()

    def start(self):
        self.is_running = True
        self.last_status = f"Listening for {self.profile_label}"
        self.emit_telemetry()

    def stop(self):
        self.is_running = False
        self.last_status = "RTTY receiver stopped"
        self.emit_telemetry()

    def reset(self):
        self._sample_buf = np.zeros(0, dtype=np.float32)
        self.mark_buf.clear()
        self.space_buf.clear()
        self.bias_buf.clear()
        self.signal_level_percent = 0
        self._build_filters()
        self.last_status = "RTTY scaffold reset"
        self.emit_telemetry()

    def handle_audio(self, payload):
        raw = base64.b64decode(payload["samples"])
        samples = np.frombuffer(raw, dtype=np.float32)
        sr = int(payload.get("sampleRate", 48000))
        channels = int(payload.get("channels", 1))
        if channels > 1:
            samples = samples.reshape(-1, channels).mean(axis=1).astype(np.float32)
        if sr != self.output_sr:
            samples = resample_poly(samples, self.output_sr, sr).astype(np.float32)

        self._sample_buf = np.concatenate([self._sample_buf, samples])
        while self._sample_buf.size >= self.frame_len:
            frame = self._sample_buf[: self.frame_len]
            self._sample_buf = self._sample_buf[self.frame_len :]
            self._process_frame(frame)

    def _process_frame(self, frame):
        mark_filtered, self._mark_zi = sosfilt(self._mark_sos, frame, zi=self._mark_zi)
        space_filtered, self._space_zi = sosfilt(self._space_sos, frame, zi=self._space_zi)
        mark_level = float(np.sqrt(np.mean(np.square(mark_filtered)) + 1e-12))
        space_level = float(np.sqrt(np.mean(np.square(space_filtered)) + 1e-12))
        self.mark_buf.append(mark_level)
        self.space_buf.append(space_level)
        bias = mark_level - space_level
        self.bias_buf.append(bias)
        total = mark_level + space_level
        self.signal_level_percent = int(max(0, min(100, round(total * 4200.0))))

        if len(self.bias_buf) >= 12:
            mean_bias = float(np.mean(self.bias_buf))
            confidence = min(1.0, abs(mean_bias) / max(total, 1e-6))
            if confidence > 0.82:
                text = "M" if mean_bias > 0 else "S"
                self.emit_decode(text, confidence)
                self.bias_buf.clear()

        self.last_status = (
            f"RTTY scaffold running  |  mark {mark_level:.4f}  "
            f"space {space_level:.4f}"
        )
        self.emit_telemetry()

    def emit_decode(self, text, confidence):
        print(json.dumps({
            "type": "decode",
            "text": text,
            "confidence": confidence,
        }), flush=True)

    def emit_telemetry(self):
        print(json.dumps({
            "type": "telemetry",
            "isRunning": self.is_running,
            "status": self.last_status,
            "activeWorker": "Python RTTY sidecar",
            "signalLevelPercent": self.signal_level_percent,
            "estimatedShiftHz": self.shift_hz,
            "estimatedBaud": self.baud_rate,
            "profileLabel": self.profile_label,
        }), flush=True)


def main():
    worker = RttyWorker()
    worker.emit_telemetry()
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            payload = json.loads(line)
        except Exception:
            continue

        kind = payload.get("type")
        if kind == "configure":
            worker.configure(payload)
        elif kind == "start":
            worker.start()
        elif kind == "stop":
            worker.stop()
        elif kind == "reset":
            worker.reset()
        elif kind == "audio" and worker.is_running:
            worker.handle_audio(payload)


if __name__ == "__main__":
    main()
