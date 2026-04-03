"""
Experimental HF WeFAX decoder prototype.

This is a cleaner lab receiver than the original `wefax_engine.py`.
It keeps the old module intact for comparison while adding:

- explicit auto-start vs force-start behavior
- selective 300/450 Hz start/stop tone detectors
- basic phasing-line alignment
- early line-clock drift correction from phasing offsets
"""

from __future__ import annotations

import logging
import threading
from collections import deque
from dataclasses import dataclass
from typing import Callable, Optional

import numpy as np
from PIL import Image
from scipy.signal import butter, hilbert, medfilt, sosfilt, sosfilt_zi

from .wefax_engine import BLACK_HZ, IOC, LPM, WHITE_HZ, WefaxEncoder

log = logging.getLogger(__name__)

START_HZ = 300.0
STOP_HZ = 450.0


@dataclass
class WefaxTelemetry:
    state: str
    samples_per_line: float
    aligned_offset: int
    start_confidence: float
    stop_confidence: float
    force_mode: bool


class WefaxDecoderPrototype:
    STATE_IDLE = "idle"
    STATE_WAIT_START = "wait_start"
    STATE_PHASING = "phasing"
    STATE_IMAGE = "image"
    STATE_STOP = "stop"

    def __init__(
        self,
        sample_rate: int = 48_000,
        lpm: int = LPM,
        ioc: int = IOC,
        image_callback: Optional[Callable[[Image.Image], None]] = None,
        line_callback: Optional[Callable[[np.ndarray], None]] = None,
        status_callback: Optional[Callable[[str], None]] = None,
        telemetry_callback: Optional[Callable[[WefaxTelemetry], None]] = None,
    ) -> None:
        self._sr = int(sample_rate)
        self._lpm = int(lpm)
        self._ioc = int(ioc)
        self._width = int(ioc * np.pi)
        self._nominal_samples_per_line = float(self._sr * 60.0 / self._lpm)
        self._samples_per_line = self._nominal_samples_per_line

        self._image_cb = image_callback
        self._line_cb = line_callback
        self._status_cb = status_callback
        self._telemetry_cb = telemetry_callback

        self._running = False
        self._thread: Optional[threading.Thread] = None
        self._lock = threading.Lock()
        self._queue: list[np.ndarray] = []

        self._bp_state = None
        self._gray_lp_state = None
        self._start_state = None
        self._stop_state = None

        self._state = self.STATE_IDLE
        self._force_start = False
        self._line_buffer = np.array([], dtype=np.float32)
        self._phasing_rows: list[np.ndarray] = []
        self._phasing_offsets: list[int] = []
        self._image_rows: list[np.ndarray] = []
        self._recent_start_scores: deque[float] = deque(maxlen=20)
        self._recent_stop_scores: deque[float] = deque(maxlen=20)
        self._recent_signal_scores: deque[float] = deque(maxlen=12)
        self._recent_subcarrier_offsets: deque[float] = deque(maxlen=24)
        self._aligned_offset = 0
        self._block_size = int(self._sr * 0.1)
        self._row_alignment_window = 24
        self._row_alignment_history: deque[int] = deque(maxlen=16)
        self._line_clock_error_history: deque[int] = deque(maxlen=24)
        self._seam_offset_history: deque[int] = deque(maxlen=16)
        self._manual_slant = 0

        self._build_filters()
        self.reset()

    def _build_filters(self) -> None:
        nyq = self._sr / 2.0
        self._bp_sos = butter(4, [1200 / nyq, min(0.99, 2600 / nyq)], btype="band", output="sos")
        seconds_per_line = 60.0 / self._lpm
        pixel_rate_hz = self._width / max(seconds_per_line, 1e-6)
        gray_cut_hz = float(np.clip(pixel_rate_hz * 0.45, 600.0, 2200.0))
        self._gray_lp_sos = butter(4, gray_cut_hz / nyq, btype="low", output="sos")
        self._start_sos = butter(4, [220 / nyq, 380 / nyq], btype="band", output="sos")
        self._stop_sos = butter(4, [380 / nyq, 540 / nyq], btype="band", output="sos")

    def start(self) -> None:
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(target=self._run, daemon=True, name="wefax-rx-proto")
        self._thread.start()
        self._set_state(self.STATE_WAIT_START)

    def stop(self) -> None:
        if self._image_rows:
            self._finish_image()
        self._running = False
        if self._thread is not None:
            self._thread.join(timeout=1.0)
            self._thread = None
        self._set_state(self.STATE_IDLE)

    def reset(self) -> None:
        with self._lock:
            self._queue.clear()
        self._bp_state = None
        self._gray_lp_state = None
        self._start_state = None
        self._stop_state = None
        self._line_buffer = np.array([], dtype=np.float32)
        self._phasing_rows = []
        self._phasing_offsets = []
        self._image_rows = []
        self._line_lock_acquired = False
        self._recent_start_scores.clear()
        self._recent_stop_scores.clear()
        self._recent_signal_scores.clear()
        self._recent_subcarrier_offsets.clear()
        self._row_alignment_history.clear()
        self._line_clock_error_history.clear()
        self._seam_offset_history.clear()
        self._aligned_offset = 0
        self._samples_per_line = self._nominal_samples_per_line
        self._force_start = False
        self._set_state(self.STATE_IDLE)
        self._emit_telemetry()

    def push_samples(self, samples: np.ndarray) -> None:
        with self._lock:
            self._queue.append(samples.astype(np.float32, copy=False))

    def force_start(self) -> None:
        self._force_start = True
        self._phasing_rows = []
        self._phasing_offsets = []
        self._image_rows = []
        self._line_buffer = np.array([], dtype=np.float32)
        self._aligned_offset = 0
        self._line_lock_acquired = False
        self._set_state(self.STATE_IMAGE)
        self._emit_telemetry()

    def set_mode(self, *, lpm: int, ioc: int) -> None:
        self._lpm = int(lpm)
        self._ioc = int(ioc)
        self._width = int(self._ioc * np.pi)
        self._nominal_samples_per_line = float(self._sr * 60.0 / self._lpm)
        self._samples_per_line = self._nominal_samples_per_line
        self._build_filters()
        self.reset()

    def set_manual_slant(self, manual_slant: int) -> None:
        self._manual_slant = int(np.clip(manual_slant, -200, 200))

    def _run(self) -> None:
        buffered = np.array([], dtype=np.float32)
        while self._running:
            chunk = None
            with self._lock:
                if self._queue:
                    chunk = self._queue.pop(0)
            if chunk is None:
                threading.Event().wait(0.01)
                continue
            buffered = np.concatenate([buffered, chunk])
            while len(buffered) >= self._block_size:
                block = buffered[: self._block_size]
                buffered = buffered[self._block_size :]
                self._process_block(block)

    def _set_state(self, state: str) -> None:
        if state != self._state:
            self._state = state
            if self._status_cb:
                self._status_cb(state)

    def _emit_telemetry(self) -> None:
        if self._telemetry_cb is None:
            return
        start_conf = float(np.mean(self._recent_start_scores)) if self._recent_start_scores else 0.0
        stop_conf = float(np.mean(self._recent_stop_scores)) if self._recent_stop_scores else 0.0
        self._telemetry_cb(
            WefaxTelemetry(
                state=self._state,
                samples_per_line=self._samples_per_line,
                aligned_offset=self._aligned_offset,
                start_confidence=start_conf,
                stop_confidence=stop_conf,
                force_mode=self._force_start,
            )
        )

    def _process_block(self, samples: np.ndarray) -> None:
        gray = self._demod_gray(samples)
        start_score = self._tone_score(samples, self._start_sos, "_start_state")
        stop_score = self._tone_score(samples, self._stop_sos, "_stop_state")
        signal_score = float(np.sqrt(np.mean(samples**2)))
        self._recent_start_scores.append(start_score)
        self._recent_stop_scores.append(stop_score)
        self._recent_signal_scores.append(signal_score)

        if self._state in (self.STATE_WAIT_START, self.STATE_IDLE):
            if self._force_start or self._start_detected():
                self._force_start = False
                self._line_buffer = np.array([], dtype=np.float32)
                self._phasing_rows = []
                self._phasing_offsets = []
                self._image_rows = []
                self._line_lock_acquired = False
                self._set_state(self.STATE_PHASING)
        elif self._state == self.STATE_PHASING:
            self._consume_phasing(gray)
        elif self._state == self.STATE_IMAGE:
            if self._stop_detected() or self._signal_dropped():
                self._finish_image()
                return
            self._consume_image(gray)
        elif self._state == self.STATE_STOP:
            if not self._stop_detected():
                self._set_state(self.STATE_WAIT_START)

        self._emit_telemetry()

    def _demod_gray(self, samples: np.ndarray) -> np.ndarray:
        if self._bp_state is None:
            self._bp_state = sosfilt_zi(self._bp_sos)
        filtered, self._bp_state = sosfilt(self._bp_sos, samples, zi=self._bp_state)
        analytic = hilbert(filtered)
        phase = np.unwrap(np.angle(analytic))
        inst_freq = np.diff(phase) * self._sr / (2 * np.pi)
        inst_freq = np.concatenate([inst_freq, [inst_freq[-1]]])

        offset_hz = self._estimate_subcarrier_offset(inst_freq)
        gray = (inst_freq - offset_hz - BLACK_HZ) / (WHITE_HZ - BLACK_HZ)
        gray = np.clip(gray, 0.0, 1.0) * 255.0
        # Knock down single-sample impulsive spikes before the smoother so
        # atmospheric/static hits don't turn into bright/dark streaks.
        gray = medfilt(gray, kernel_size=3)
        if self._gray_lp_state is None:
            self._gray_lp_state = sosfilt_zi(self._gray_lp_sos)
        gray, self._gray_lp_state = sosfilt(self._gray_lp_sos, gray, zi=self._gray_lp_state)
        # Blend a little of the original trace back in so cleanup doesn't wash
        # out map detail when signals are already decent.
        gray = (gray * 0.82) + (medfilt(gray, kernel_size=3) * 0.18)
        return np.clip(gray, 0.0, 255.0).astype(np.float32, copy=False)

    def _tone_score(self, samples: np.ndarray, sos: np.ndarray, state_attr: str) -> float:
        state = getattr(self, state_attr)
        if state is None:
            state = sosfilt_zi(sos)
        filtered, state = sosfilt(sos, samples, zi=state)
        setattr(self, state_attr, state)
        power = float(np.sqrt(np.mean(filtered**2)) + 1e-9)
        total = float(np.sqrt(np.mean(samples**2)) + 1e-9)
        return max(0.0, min(1.0, power / (total * 1.5)))

    def _estimate_subcarrier_offset(self, inst_freq: np.ndarray) -> float:
        inband = inst_freq[(inst_freq > 1300) & (inst_freq < 2500)]
        if len(inband) < max(32, len(inst_freq) // 8):
            return float(np.median(self._recent_subcarrier_offsets)) if self._recent_subcarrier_offsets else 0.0
        center = float(np.median(inband))
        offset = float(np.clip(center - 1900.0, -120.0, 120.0))
        self._recent_subcarrier_offsets.append(offset)
        return float(np.median(self._recent_subcarrier_offsets))

    def _start_detected(self) -> bool:
        if len(self._recent_start_scores) < self._recent_start_scores.maxlen:
            return False
        return float(np.mean(self._recent_start_scores)) > 0.42

    def _stop_detected(self) -> bool:
        if len(self._recent_stop_scores) < 8:
            return False
        return float(np.mean(list(self._recent_stop_scores)[-8:])) > 0.40

    def _signal_dropped(self) -> bool:
        if len(self._recent_signal_scores) < self._recent_signal_scores.maxlen:
            return False
        return float(np.mean(self._recent_signal_scores)) < 0.003

    def _consume_phasing(self, gray: np.ndarray) -> None:
        self._line_buffer = np.concatenate([self._line_buffer, gray.astype(np.float32, copy=False)])
        target = int(round(self._samples_per_line))
        if not self._line_lock_acquired and len(self._line_buffer) >= target * 6:
            start_idx, best_spl, best_offset = self._lock_phasing(self._line_buffer)
            if start_idx > 0:
                self._line_buffer = self._line_buffer[start_idx:]
            self._samples_per_line = float(best_spl)
            self._aligned_offset = best_offset
            self._line_lock_acquired = True
            target = int(round(self._samples_per_line))
        while len(self._line_buffer) >= target:
            raw = self._line_buffer[:target]
            self._line_buffer = self._line_buffer[target:]
            row = self._resample_line(raw)
            offset = self._estimate_phasing_offset(row)
            self._phasing_rows.append(row)
            self._phasing_offsets.append(offset)
            self._aligned_offset = int(np.median(self._phasing_offsets[-8:]))
            if len(self._phasing_rows) >= 12:
                self._seam_offset_history.clear()
                self._seam_offset_history.extend(self._phasing_offsets[-8:])
                self._line_clock_error_history.clear()
                self._set_state(self.STATE_IMAGE)

    def _consume_image(self, gray: np.ndarray) -> None:
        self._line_buffer = np.concatenate([self._line_buffer, gray.astype(np.float32, copy=False)])
        target = int(round(self._samples_per_line))
        while len(self._line_buffer) >= target:
            raw = self._line_buffer[:target]
            self._line_buffer = self._line_buffer[target:]
            row = self._resample_line(raw)
            row = self._align_row_to_seam(row)
            row = self._fine_align_row(row)
            row = self._apply_manual_slant(row, len(self._image_rows))
            self._image_rows.append(row)
            if self._line_cb:
                self._line_cb(row)
            if len(self._image_rows) >= 600:
                self._finish_image()
                return

    def _resample_line(self, raw_line: np.ndarray) -> np.ndarray:
        resampled = np.interp(
            np.linspace(0, len(raw_line) - 1, self._width),
            np.arange(len(raw_line)),
            raw_line,
        )
        return np.clip(resampled, 0, 255).astype(np.uint8)

    def _estimate_phasing_offset(self, row: np.ndarray) -> int:
        pulse_width = max(8, int(self._width * 0.018))
        dark_width = pulse_width * 3
        wrapped = np.concatenate([row.astype(np.float32), row.astype(np.float32)[: dark_width + pulse_width]])
        bright = np.convolve(wrapped, np.ones(pulse_width, dtype=np.float32), mode="valid")
        dark = np.convolve(wrapped[pulse_width:], np.ones(dark_width, dtype=np.float32), mode="valid")
        usable = min(len(bright), len(dark), self._width)
        score = bright[:usable] - dark[:usable]
        idx = int(np.argmax(score))
        if idx > self._width // 2:
            idx -= self._width
        return idx

    def _find_phasing_start(self, gray_stream: np.ndarray, target: int) -> int:
        white = max(16, int(self._sr * 0.025))
        dark = max(white * 4, min(target - white, int(self._sr * 0.15)))
        usable = min(len(gray_stream) - (white + dark), target)
        if usable <= 1:
            return 0
        arr = gray_stream.astype(np.float32)
        csum = np.concatenate([[0.0], np.cumsum(arr)])

        def window_sum(start: int, length: int) -> np.ndarray:
            s = np.arange(start, start + usable)
            e = s + length
            return csum[e] - csum[s]

        bright = window_sum(0, white) / white
        dark_mean = window_sum(white, dark) / dark
        score = bright - dark_mean
        return int(np.argmax(score))

    def _lock_phasing(self, gray_stream: np.ndarray) -> tuple[int, int, int]:
        nominal = int(round(self._nominal_samples_per_line))
        best_score = float("-inf")
        best_start = 0
        best_spl = nominal
        best_offset = 0

        min_spl = max(int(nominal * 0.97), nominal - 800)
        max_spl = min(int(nominal * 1.03), nominal + 800)

        for candidate_spl in range(min_spl, max_spl + 1, 40):
            start_idx = self._find_phasing_start(gray_stream, candidate_spl)
            required = start_idx + candidate_spl * 5
            if required > len(gray_stream):
                continue

            offsets: list[int] = []
            contrast_scores: list[float] = []
            valid = True
            for i in range(5):
                start = start_idx + i * candidate_spl
                end = start + candidate_spl
                raw = gray_stream[start:end]
                if len(raw) < candidate_spl:
                    valid = False
                    break
                row = self._resample_line(raw)
                offset = self._estimate_phasing_offset(row)
                offsets.append(offset)
                contrast_scores.append(self._phasing_row_contrast(row, offset))

            if not valid or not offsets:
                continue

            offset_std = float(np.std(offsets))
            offset_abs = float(np.mean(np.abs(offsets)))
            contrast = float(np.mean(contrast_scores))
            score = contrast - (offset_std * 0.40) - (offset_abs * 0.08) - abs(candidate_spl - nominal) * 0.002
            if score > best_score:
                best_score = score
                best_start = start_idx
                best_spl = candidate_spl
                best_offset = int(round(np.median(offsets)))

        return best_start, best_spl, best_offset

    def _phasing_row_contrast(self, row: np.ndarray, offset: int) -> float:
        pulse_width = max(8, int(self._width * 0.018))
        dark_width = pulse_width * 3
        idx = offset if offset >= 0 else self._width + offset
        idx = idx % self._width
        wrapped = np.concatenate([row.astype(np.float32), row.astype(np.float32)])
        bright = float(np.mean(wrapped[idx:idx + pulse_width]))
        dark = float(np.mean(wrapped[idx + pulse_width:idx + pulse_width + dark_width]))
        return bright - dark

    def _finish_image(self) -> None:
        if len(self._image_rows) < 10:
            self._set_state(self.STATE_STOP)
            return
        arr = np.array(self._image_rows, dtype=np.uint8)
        arr = self._trim_non_image_rows(arr)
        arr = self._stretch_contrast(arr)
        img = Image.fromarray(arr, mode="L")
        if self._image_cb:
            self._image_cb(img)
        self._image_rows = []
        self._line_buffer = np.array([], dtype=np.float32)
        self._row_alignment_history.clear()
        self._set_state(self.STATE_STOP)

    def _align_row_to_seam(self, row: np.ndarray) -> np.ndarray:
        seam_offset, confidence = self._estimate_image_seam_offset(row)
        if confidence >= 6.0 or not self._seam_offset_history:
            self._seam_offset_history.append(seam_offset)

        smoothed_seam = int(round(np.median(self._seam_offset_history))) if self._seam_offset_history else seam_offset
        seam_delta = seam_offset - smoothed_seam
        if seam_delta > self._width // 2:
            seam_delta -= self._width
        elif seam_delta < -(self._width // 2):
            seam_delta += self._width

        self._aligned_offset = smoothed_seam
        if confidence >= 6.0:
            self._line_clock_error_history.append(seam_delta)
            self._adjust_line_clock()

        if smoothed_seam:
            return np.roll(row, -smoothed_seam)
        return row

    def _estimate_image_seam_offset(self, row: np.ndarray) -> tuple[int, float]:
        values = row.astype(np.float32)
        band = max(6, int(self._width * 0.006))
        darkness = 255.0 - values

        kernel = np.ones(band, dtype=np.float32) / float(band)
        extended_dark = np.concatenate([darkness, darkness[: band - 1]])
        dark_score = np.convolve(extended_dark, kernel, mode="valid")[: self._width]

        transition_score = np.abs(np.roll(values, -1) - values)
        score = dark_score * 0.85 + transition_score * 0.15

        if self._seam_offset_history:
            center = int(round(np.median(self._seam_offset_history))) % self._width
            search_radius = max(48, int(self._width * 0.06))
            candidates = np.array(
                [(center + delta) % self._width for delta in range(-search_radius, search_radius + 1)],
                dtype=np.int32,
            )
            candidate_scores = score[candidates]
            best_idx = int(candidates[int(np.argmax(candidate_scores))])
            baseline = float(np.median(candidate_scores))
            confidence = float(np.max(candidate_scores) - baseline)
        else:
            best_idx = int(np.argmax(score))
            baseline = float(np.median(score))
            confidence = float(np.max(score) - baseline)

        if best_idx > self._width // 2:
            best_idx -= self._width
        return best_idx, confidence

    def _fine_align_row(self, row: np.ndarray) -> np.ndarray:
        if not self._image_rows:
            return row
        ref = self._image_rows[-1].astype(np.float32)
        cur = row.astype(np.float32)
        best_shift = 0
        best_score = float("-inf")
        fine_window = min(6, self._row_alignment_window)
        for shift in range(-fine_window, fine_window + 1):
            rolled = np.roll(cur, shift)
            score = float(np.dot(ref, rolled))
            if score > best_score:
                best_score = score
                best_shift = shift
        self._row_alignment_history.append(best_shift)
        smoothed_shift = int(round(np.median(self._row_alignment_history)))
        if smoothed_shift:
            return np.roll(row, smoothed_shift)
        return row

    def _adjust_line_clock(self) -> None:
        if len(self._line_clock_error_history) < self._line_clock_error_history.maxlen:
            return

        drift = float(np.median(self._line_clock_error_history))
        if abs(drift) < 0.5:
            return

        # Use seam drift as the primary timing cue. If the phasing edge keeps
        # walking sideways, trim the line clock so future rows land closer to the edge.
        correction = float(np.clip(drift * 0.08, -2.0, 2.0))
        next_spl = self._samples_per_line + correction
        min_spl = self._nominal_samples_per_line * 0.97
        max_spl = self._nominal_samples_per_line * 1.03
        self._samples_per_line = float(np.clip(next_spl, min_spl, max_spl))

    def _apply_manual_slant(self, row: np.ndarray, row_index: int) -> np.ndarray:
        if self._manual_slant == 0 or row_index <= 0:
            return row
        shift = int(round(row_index * (self._manual_slant / 1000.0)))
        if shift == 0:
            return row
        return np.roll(row, shift)

    def _trim_non_image_rows(self, arr: np.ndarray) -> np.ndarray:
        if len(arr) < 20:
            return arr
        row_std = arr.astype(np.float32).std(axis=1)
        start = 0
        while start < len(row_std) // 3 and row_std[start] < 6.0:
            start += 1
        end = len(row_std)
        while end > start + 10 and row_std[end - 1] < 4.0:
            end -= 1
        trimmed = arr[start:end]
        return trimmed if len(trimmed) >= 20 else arr

    def _stretch_contrast(self, arr: np.ndarray) -> np.ndarray:
        work = arr.astype(np.float32)
        low = float(np.percentile(work, 2.0))
        high = float(np.percentile(work, 98.0))
        if high <= low + 1.0:
            return arr
        work = (work - low) * (255.0 / (high - low))
        return np.clip(work, 0, 255).astype(np.uint8)


__all__ = [
    "WefaxDecoderPrototype",
    "WefaxEncoder",
    "WefaxTelemetry",
]
