"""
ShackStack - modes/wefax_engine.py

WeFax (HF Radiofax) receive decoder + test signal encoder.
"""

from __future__ import annotations

import logging
import threading
from typing import Callable, Optional

import numpy as np
from PIL import Image
from scipy.signal import butter, sosfilt, sosfilt_zi, hilbert

log = logging.getLogger(__name__)

BLACK_HZ = 1500.0
WHITE_HZ = 2300.0
IOC = 576
LPM = 120
LINE_WIDTH = int(IOC * np.pi)
START_FREQ = 300.0


class WefaxEncoder:
    def __init__(self, sample_rate: int = 48000, lpm: int = LPM, ioc: int = IOC):
        self._sr = sample_rate
        self._lpm = lpm
        self._ioc = ioc
        self._width = int(ioc * np.pi)

    def _tone(self, freq: float, duration_s: float) -> np.ndarray:
        n = int(self._sr * duration_s)
        t = np.arange(n) / self._sr
        return np.sin(2 * np.pi * freq * t).astype(np.float32)

    def _fm_line(self, pixels: np.ndarray) -> np.ndarray:
        samples_per_line = int(self._sr * 60 / self._lpm)
        pix_f = pixels.astype(np.float32) / 255.0
        pix_resampled = np.interp(
            np.linspace(0, len(pix_f) - 1, samples_per_line),
            np.arange(len(pix_f)),
            pix_f,
        )
        freqs = BLACK_HZ + pix_resampled * (WHITE_HZ - BLACK_HZ)
        phase = np.cumsum(2 * np.pi * freqs / self._sr)
        return np.sin(phase).astype(np.float32)

    def encode_image(self, img: Image.Image, noise: float = 0.0) -> np.ndarray:
        w, h = img.size
        new_h = min(400, int(h * self._width / w))
        img = img.resize((self._width, new_h)).convert("L")
        pixels = np.array(img)

        chunks = [self._tone(START_FREQ, 5.0)]

        for _ in range(60):
            spl = int(self._sr * 60 / self._lpm)
            white_s = int(self._sr * 0.025)
            black_s = spl - white_s
            white_tone = np.sin(2 * np.pi * WHITE_HZ * np.arange(white_s) / self._sr).astype(np.float32)
            black_tone = np.sin(2 * np.pi * BLACK_HZ * np.arange(black_s) / self._sr).astype(np.float32)
            chunks.append(np.concatenate([white_tone, black_tone]))

        for row in pixels:
            chunks.append(self._fm_line(row))

        n_stop = int(self._sr * 5.0)
        t_stop = np.arange(n_stop) / self._sr
        stop = np.sin(2 * np.pi * BLACK_HZ * t_stop)
        chunks.append(stop.astype(np.float32))
        chunks.append(np.zeros(int(self._sr * 1.0), dtype=np.float32))

        audio = np.concatenate(chunks)
        if noise > 0:
            audio += np.random.normal(0, noise, len(audio)).astype(np.float32)
            audio = np.clip(audio, -1.0, 1.0)
        return audio * 0.7

    def encode_test_pattern(self, noise: float = 0.0) -> np.ndarray:
        h = 200
        img_data = np.zeros((h, self._width), dtype=np.uint8)
        for y in range(h):
            img_data[y] = int(y / h * 180)
        for y in range(0, h, 20):
            img_data[max(0, y - 1):y + 2, :] = 20
        cy, cx = h // 2, self._width // 2
        for y in range(h):
            for x in range(0, self._width, 4):
                dist = np.sqrt((y - cy) ** 2 + (x - cx) ** 2)
                if int(dist) % 30 < 2:
                    img_data[y, x:x + 4] = 30
        img_data[:12, :] = 0
        img_data[:12, :20] = 255
        img_data[10:22, :] = 255
        img = Image.fromarray(img_data, "L")
        return self.encode_image(img, noise=noise)


class WefaxDecoder:
    STATE_IDLE = "idle"
    STATE_START = "start_tone"
    STATE_PHASING = "phasing"
    STATE_IMAGE = "image"
    STATE_STOP = "stop"

    def __init__(
        self,
        sample_rate: int = 48000,
        lpm: int = LPM,
        ioc: int = IOC,
        image_callback: Optional[Callable] = None,
        line_callback: Optional[Callable] = None,
        status_callback: Optional[Callable] = None,
    ):
        self._sr = sample_rate
        self._lpm = lpm
        self._ioc = ioc
        self._width = int(ioc * np.pi)
        self._spl = int(sample_rate * 60 / lpm)

        self._image_cb = image_callback
        self._line_cb = line_callback
        self._status_cb = status_callback

        self._state = self.STATE_IDLE
        self._running = False
        self._queue = []
        self._lock = threading.Lock()
        self._thread: Optional[threading.Thread] = None
        self._bp_state = None
        self._lp_state = None
        self._start_state = None
        self._prev_sample = 0.0 + 0j
        self._inband_count = 0
        self._inband_trigger = int(sample_rate * 2.0)
        self._lines = []
        self._line_buf = []
        self._phase_tick = 0
        self._start_energy_buf = np.zeros(int(sample_rate * 0.5))

        self._build_filters()

    def _build_filters(self):
        nyq = self._sr / 2.0
        self._bp_sos = butter(4, [1200 / nyq, min(0.99, 2600 / nyq)], btype="band", output="sos")
        lp_cut = (self._spl / self._width * 2) / nyq
        lp_cut = max(0.001, min(0.49, lp_cut))
        self._lp_sos = butter(4, lp_cut, btype="low", output="sos")
        self._start_sos = butter(4, [250 / nyq, 350 / nyq], btype="band", output="sos")

    def start(self):
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(target=self._run, daemon=True, name="wefax-rx")
        self._thread.start()

    def stop(self):
        self._running = False
        if self._thread:
            self._thread.join(timeout=1.0)
            self._thread = None

    def push_samples(self, samples: np.ndarray):
        with self._lock:
            self._queue.append(samples.astype(np.float32))

    def reset(self):
        with self._lock:
            self._queue.clear()
        self._state = self.STATE_IDLE
        self._lines = []
        self._line_buf = []
        self._bp_state = None
        self._lp_state = None
        self._start_state = None
        self._inband_count = 0
        self._phase_tick = 0

    def _set_state(self, state: str):
        if state != self._state:
            self._state = state
            if self._status_cb:
                self._status_cb(state)

    def _run(self):
        buf = np.array([], dtype=np.float32)
        while self._running:
            chunk = None
            with self._lock:
                if self._queue:
                    chunk = self._queue.pop(0)
            if chunk is None:
                threading.Event().wait(0.005)
                continue
            buf = np.concatenate([buf, chunk])
            block_size = int(self._sr * 0.1)
            while len(buf) >= block_size:
                self._process_block(buf[:block_size])
                buf = buf[block_size:]

    def _process_block(self, samples: np.ndarray):
        if self._bp_state is None:
            self._bp_state = sosfilt_zi(self._bp_sos)
        filtered, self._bp_state = sosfilt(self._bp_sos, samples, zi=self._bp_state)

        analytic = hilbert(filtered)
        phase = np.unwrap(np.angle(analytic))
        inst_freq = np.diff(phase) * self._sr / (2 * np.pi)
        inst_freq = np.concatenate([inst_freq, [inst_freq[-1]]])

        pixels = (inst_freq - BLACK_HZ) / (WHITE_HZ - BLACK_HZ)
        pixels = np.clip(pixels, 0.0, 1.0) * 255

        if self._lp_state is None:
            self._lp_state = sosfilt_zi(self._lp_sos)
        pixels, self._lp_state = sosfilt(self._lp_sos, pixels, zi=self._lp_state)
        pixels = np.clip(pixels, 0, 255).astype(np.uint8)

        in_band = np.sum((inst_freq > 1400) & (inst_freq < 2400))
        if self._state in (self.STATE_IDLE, self.STATE_START):
            self._inband_count += in_band
            if in_band < len(inst_freq) * 0.3:
                self._inband_count = 0
            if self._inband_count >= self._inband_trigger:
                self._set_state(self.STATE_IMAGE)
                self._lines = []
                self._line_buf = []
                self._inband_count = 0
        else:
            self._inband_count = 0

        if self._state == self.STATE_IDLE:
            self._detect_start(samples)
        elif self._state == self.STATE_START:
            self._detect_start(samples)
        elif self._state == self.STATE_PHASING:
            self._handle_phasing(pixels)
        elif self._state == self.STATE_IMAGE:
            self._handle_image(pixels)
        elif self._state == self.STATE_STOP:
            self._detect_start(samples)

    def _detect_start(self, samples: np.ndarray):
        if self._start_state is None:
            self._start_state = sosfilt_zi(self._start_sos)
        filtered_start, self._start_state = sosfilt(self._start_sos, samples, zi=self._start_state)
        energy = np.sqrt(np.mean(filtered_start ** 2))
        n = len(self._start_energy_buf)
        self._start_energy_buf = np.roll(self._start_energy_buf, -len(samples))
        self._start_energy_buf[-min(len(samples), n):] = energy
        sustained = np.mean(self._start_energy_buf > 0.01)
        if sustained > 0.6:
            self._set_state(self.STATE_PHASING)
            self._lines = []
            self._line_buf = []
            self._phase_tick = 0
            self._inband_count = 0

    def _handle_phasing(self, pixels: np.ndarray):
        self._line_buf.extend(pixels.tolist())
        self._phase_tick += len(pixels)
        if self._phase_tick >= self._spl * 55:
            self._set_state(self.STATE_IMAGE)
            self._line_buf = []

    def _handle_image(self, pixels: np.ndarray):
        self._line_buf.extend(pixels.tolist())
        while len(self._line_buf) >= self._spl:
            raw_line = np.array(self._line_buf[:self._spl], dtype=np.uint8)
            self._line_buf = self._line_buf[self._spl:]
            line = np.interp(
                np.linspace(0, self._spl - 1, self._width),
                np.arange(self._spl),
                raw_line,
            ).astype(np.uint8)
            self._lines.append(line)
            if self._line_cb:
                self._line_cb(line)
            if len(self._lines) > 30:
                last = np.array(self._lines[-8:], dtype=np.float32)
                if last.mean() < 10:
                    self._finish_image()
                    return
            if len(self._lines) >= 400:
                self._finish_image()

    def _finish_image(self):
        if len(self._lines) < 10:
            self._set_state(self.STATE_IDLE)
            return
        arr = np.array(self._lines, dtype=np.uint8)
        img = Image.fromarray(arr, mode="L")
        if self._image_cb:
            self._image_cb(img)
        self._lines = []
        self._line_buf = []
        self._set_state(self.STATE_STOP)
