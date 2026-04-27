from __future__ import annotations

import math
from collections import deque
from dataclasses import dataclass

import numpy as np
from scipy.signal import butter, resample_poly, sosfilt, sosfilt_zi

from modes.cw_engine import CwDecodeEvent, MORSE_DECODE


@dataclass
class FldigiCwStats:
    tracked_tone_hz: float
    estimated_wpm: float
    confidence: float
    signal_level: float
    noise_floor: float
    agc_peak: float
    two_dots: float


class _MovingAverage:
    def __init__(self, length: int):
        self.set_length(length)

    def set_length(self, length: int) -> None:
        self._length = max(1, int(length))
        self._buf: deque[float] = deque(maxlen=self._length)
        self._sum = 0.0

    def reset(self) -> None:
        self._buf.clear()
        self._sum = 0.0

    def run(self, value: float) -> float:
        if len(self._buf) == self._length:
            self._sum -= self._buf[0]
        self._buf.append(float(value))
        self._sum += float(value)
        return self._sum / max(1, len(self._buf))


class CwFldigiDecoder:
    """
    CW receive path ported from fldigi's src/cw_rtty/cw.cxx.

    The important behavior here is fldigi's shape:
    complex-mix to audio CW pitch, low-pass/matched-ish filtering, decimate,
    moving-average envelope, AGC-derived hysteresis, and the keydown/keyup
    state machine that tracks dot/dash timing from dot-dash pairs.
    """

    CW_SAMPLE_RATE = 8000
    DEC_RATIO = 16
    KWPM = 12 * CW_SAMPLE_RATE / 10
    MAX_MORSE_ELEMENTS = 6

    RS_IDLE = 0
    RS_IN_TONE = 1
    RS_AFTER_TONE = 2

    def __init__(
        self,
        sample_rate: int = 48000,
        tone_hz: float = 700.0,
        text_callback=None,
        initial_wpm: int = 20,
        bandwidth_hz: int = 220,
        matched_filter_enabled: bool = True,
        tracking_enabled: bool = True,
        tracking_range_wpm: int = 8,
        lower_wpm_limit: int = 5,
        upper_wpm_limit: int = 60,
        attack: str = "Normal",
        decay: str = "Slow",
        noise_character: str = "Suppress",
        auto_tone_search_enabled: bool = True,
        afc_enabled: bool = True,
        tone_search_span_hz: int = 250,
        squelch: str = "Off",
        spacing: str = "Normal",
    ):
        self._input_rate = int(sample_rate)
        self._tone_hz = float(tone_hz)
        self._callback = text_callback
        self._initial_wpm = int(np.clip(initial_wpm, 5, 60))
        self._bandwidth_hz = int(np.clip(bandwidth_hz, 40, 600))
        self._matched_filter_enabled = bool(matched_filter_enabled)
        self._tracking_enabled = bool(tracking_enabled)
        self._tracking_range_wpm = int(np.clip(tracking_range_wpm, 1, 30))
        self._lower_wpm_limit = int(np.clip(lower_wpm_limit, 5, 60))
        self._upper_wpm_limit = int(np.clip(upper_wpm_limit, self._lower_wpm_limit + 1, 80))
        self._attack = str(attack or "Normal")
        self._decay = str(decay or "Slow")
        self._noise_character = str(noise_character or "Suppress")
        self._auto_tone_search_enabled = bool(auto_tone_search_enabled)
        self._afc_enabled = bool(afc_enabled)
        self._tone_search_span_hz = int(np.clip(tone_search_span_hz, 50, 800))
        self._squelch = str(squelch or "Off")
        self._spacing = str(spacing or "Normal")
        self._tracking = _MovingAverage(16)
        self._bitfilter = _MovingAverage(8)
        self._running = False
        self._afc_callback = None
        self._wpm_callback = None
        self._resample_tail = np.array([], dtype=np.float32)
        self._build_filters()
        self.reset()

    def start(self) -> None:
        self._running = True

    def stop(self) -> None:
        self._running = False
        self._query(force=True)

    def reset(self) -> None:
        self._tracking.reset()
        self._cw_send_speed = self._initial_wpm
        self._two_dots = 2.0 * self.KWPM / self._cw_send_speed
        self._sync_parameters()
        self._state = self.RS_IDLE
        self._sample_counter = 0
        self._dec_counter = 0
        self._start_timestamp = 0
        self._end_timestamp = 0
        self._rx_pattern = ""
        self._last_element = 0
        self._space_sent = True
        self._agc_peak = 1.0
        self._noise_floor = 1.0
        self._sig_avg = 0.0
        self._signal_level = 0.0
        self._metric = 0.0
        self._upper_threshold = 0.65
        self._lower_threshold = 0.35
        self._phase = 0.0
        self._last_tone_search_sample = 0
        self._tone_lock_score = 0.0
        self._tone_search_buffer: deque[float] = deque(maxlen=self.CW_SAMPLE_RATE // 2)
        self._squelch_open = self._squelch_threshold() <= 0.0
        self._squelch_below_count = 0
        self._bitfilter.reset()
        self._resample_tail = np.array([], dtype=np.float32)
        self._lpf_state = sosfilt_zi(self._lpf_sos).astype(np.complex128) * 0.0

    def set_initial_wpm(self, wpm: int) -> None:
        self._initial_wpm = int(np.clip(wpm, 5, 60))
        self._cw_send_speed = self._initial_wpm
        self._two_dots = 2.0 * self.KWPM / self._cw_send_speed
        self._build_filters()
        self._sync_parameters()

    def set_afc_callback(self, fn) -> None:
        self._afc_callback = fn
        self._report_pitch()

    def set_wpm_callback(self, fn) -> None:
        self._wpm_callback = fn
        self._report_wpm()

    @property
    def tone_hz(self) -> float:
        return self._tone_hz

    @tone_hz.setter
    def tone_hz(self, value: float) -> None:
        self._tone_hz = float(value)
        self._report_pitch()

    @property
    def stats(self) -> FldigiCwStats:
        confidence = max(0.0, min(1.0, self._metric / 40.0))
        return FldigiCwStats(
            tracked_tone_hz=float(self._tone_hz),
            estimated_wpm=float(self._estimated_wpm()),
            confidence=float(confidence),
            signal_level=float(self._signal_level),
            noise_floor=float(self._noise_floor),
            agc_peak=float(self._agc_peak),
            two_dots=float(self._two_dots),
        )

    def push_samples(self, samples: np.ndarray) -> None:
        if not self._running:
            return
        if samples is None or len(samples) == 0:
            return
        audio = self._to_8k(np.asarray(samples, dtype=np.float32))
        if audio.size == 0:
            return
        self._process_8k(audio)

    def _build_filters(self) -> None:
        # fldigi uses fftfilt after complex mixing. A narrow low-pass gives us
        # the same effective "energy around the selected CW pitch" detector.
        bandwidth = (5.0 * self._initial_wpm / 1.2) if self._matched_filter_enabled else float(self._bandwidth_hz)
        bandwidth = max(35.0, min(600.0, bandwidth))
        cutoff = min(0.45, bandwidth / (self.CW_SAMPLE_RATE / 2.0))
        self._lpf_sos = butter(4, cutoff, btype="low", output="sos")

    def _to_8k(self, samples: np.ndarray) -> np.ndarray:
        if self._input_rate == self.CW_SAMPLE_RATE:
            return samples.astype(np.float32, copy=False)
        if self._input_rate <= 0:
            return samples.astype(np.float32, copy=False)

        data = np.concatenate([self._resample_tail, samples.astype(np.float32, copy=False)])
        if self._input_rate % self.CW_SAMPLE_RATE == 0:
            factor = self._input_rate // self.CW_SAMPLE_RATE
            usable = (len(data) // factor) * factor
            self._resample_tail = data[usable:].copy()
            if usable <= 0:
                return np.array([], dtype=np.float32)
            return data[:usable].reshape(-1, factor).mean(axis=1).astype(np.float32, copy=False)

        gcd = math.gcd(self._input_rate, self.CW_SAMPLE_RATE)
        up = self.CW_SAMPLE_RATE // gcd
        down = self._input_rate // gcd
        usable = (len(data) // down) * down
        self._resample_tail = data[usable:].copy()
        if usable <= 0:
            return np.array([], dtype=np.float32)
        return resample_poly(data[:usable], up, down).astype(np.float32, copy=False)

    def _process_8k(self, samples: np.ndarray) -> None:
        self._maybe_update_tone(samples)
        n = len(samples)
        omega = 2.0 * np.pi * self._tone_hz / float(self.CW_SAMPLE_RATE)
        osc = np.exp(-1j * (self._phase + (omega * np.arange(n, dtype=np.float64))))
        self._phase = (self._phase + (omega * n)) % (2.0 * np.pi)
        mixed = samples.astype(np.float64, copy=False) * osc
        filtered, self._lpf_state = sosfilt(self._lpf_sos, mixed, zi=self._lpf_state)

        for value in np.abs(filtered):
            self._sample_counter += 1
            self._dec_counter += 1
            if self._dec_counter < self.DEC_RATIO:
                continue
            self._dec_counter = 0
            envelope = self._bitfilter.run(float(value))
            self._decode_stream(envelope)

    def _sync_parameters(self) -> None:
        receive_speed = self._estimated_wpm()
        self._receive_dot = self.KWPM / max(5.0, receive_speed)
        self._receive_dash = 3.0 * self._receive_dot
        self._noise_spike_threshold = self._receive_dot / 2.0
        bfv = int(round(self._receive_dot / (4.0 * self.DEC_RATIO)))
        self._bitfilter.set_length(max(1, bfv))

    def _estimated_wpm(self) -> float:
        if not self._tracking_enabled:
            return float(np.clip(self._cw_send_speed, self._lower_wpm_limit, self._upper_wpm_limit))
        if self._two_dots <= 0:
            return float(self._cw_send_speed)
        lower = max(float(self._lower_wpm_limit), float(self._cw_send_speed - self._tracking_range_wpm))
        upper = min(float(self._upper_wpm_limit), float(self._cw_send_speed + self._tracking_range_wpm))
        if upper <= lower:
            upper = lower + 1.0
        return float(np.clip(self.KWPM / (self._two_dots / 2.0), lower, upper))

    def _update_tracking(self, dur_1: int, dur_2: int) -> None:
        min_dot = self.KWPM / 200.0
        max_dash = 3.0 * self.KWPM / 5.0
        if dur_1 > dur_2 and dur_1 > 4 * dur_2:
            return
        if dur_2 > dur_1 and dur_2 > 4 * dur_1:
            return
        if dur_1 < min_dot or dur_2 < min_dot:
            return
        if dur_1 > max_dash or dur_2 > max_dash:
            return
        if not self._tracking_enabled:
            return
        self._two_dots = self._tracking.run((dur_1 + dur_2) / 2.0)
        self._sync_parameters()
        self._report_wpm()

    @staticmethod
    def _decayavg(current: float, value: float, weight: float) -> float:
        weight = max(1.0, float(weight))
        return current + ((value - current) / weight)

    def _decode_stream(self, value: float) -> None:
        attack = self._attack_weight()
        decay = self._decay_weight()

        self._sig_avg = self._decayavg(self._sig_avg, value, decay)

        if value < self._sig_avg:
            if value < self._noise_floor:
                self._noise_floor = self._decayavg(self._noise_floor, value, attack)
            else:
                self._noise_floor = self._decayavg(self._noise_floor, value, decay)

        if value > self._sig_avg:
            if value > self._agc_peak:
                self._agc_peak = self._decayavg(self._agc_peak, value, attack)
            else:
                self._agc_peak = self._decayavg(self._agc_peak, value, decay)

        agc_peak = max(self._agc_peak, 1e-9)
        norm_noise = self._noise_floor / agc_peak
        norm_sig = self._sig_avg / agc_peak
        self._signal_level = norm_sig
        normalized = value / agc_peak

        self._metric *= 0.8
        if self._noise_floor > 1e-4 and self._noise_floor < self._sig_avg:
            db = 20.0 * math.log10(max(self._sig_avg / self._noise_floor, 1e-9))
            self._metric += 0.2 * max(0.0, min(100.0, 2.5 * db))

        diff = norm_sig - norm_noise
        self._upper_threshold = norm_sig - 0.15 * diff
        self._lower_threshold = norm_noise + 0.85 * diff

        if not self._update_squelch_gate():
            self._query()
            return

        if normalized > self._upper_threshold and self._state != self.RS_IN_TONE:
            self._key_down()
        if normalized < self._lower_threshold and self._state == self.RS_IN_TONE:
            self._key_up()
        self._query()

    def _key_down(self) -> None:
        if self._state == self.RS_IN_TONE:
            return
        if self._state == self.RS_IDLE:
            self._sample_counter = 0
            self._rx_pattern = ""
        self._start_timestamp = self._sample_counter
        self._state = self.RS_IN_TONE

    def _key_up(self) -> None:
        if self._state != self.RS_IN_TONE:
            return
        self._end_timestamp = self._sample_counter
        element = max(0, self._end_timestamp - self._start_timestamp)
        self._sync_parameters()

        if self._noise_spike_threshold > 0 and element < self._noise_spike_threshold:
            self._state = self.RS_IDLE
            return

        if self._last_element > 0:
            if element > 2 * self._last_element and element < 4 * self._last_element:
                self._update_tracking(self._last_element, element)
            if self._last_element > 2 * element and self._last_element < 4 * element:
                self._update_tracking(element, self._last_element)

        self._last_element = int(element)
        self._rx_pattern += "." if element <= self._two_dots else "-"

        if len(self._rx_pattern) > self.MAX_MORSE_ELEMENTS:
            self._state = self.RS_IDLE
            self._rx_pattern = ""
            self._sample_counter = 0
            return

        self._state = self.RS_AFTER_TONE

    def _query(self, *, force: bool = False) -> None:
        if self._state == self.RS_IN_TONE:
            return
        self._sync_parameters()
        silence = max(0, self._sample_counter - self._end_timestamp)
        char_gap, word_gap = self._spacing_thresholds()

        if not force and silence < (char_gap * self._receive_dot):
            return

        if self._rx_pattern and (
            force
            or (
                silence >= (char_gap * self._receive_dot)
                and silence <= (word_gap * self._receive_dot)
                and self._state == self.RS_AFTER_TONE
            )
        ):
            char = MORSE_DECODE.get(self._rx_pattern, "")
            if char:
                self._emit(char, pattern=self._rx_pattern, confidence=max(0.35, min(1.0, self._metric / 40.0)))
            else:
                noise = self._noise_text()
                if noise:
                    self._emit(noise, pattern=self._rx_pattern, confidence=0.1)
            self._rx_pattern = ""
            self._state = self.RS_IDLE
            self._space_sent = False
            return

        if silence > (word_gap * self._receive_dot) and not self._space_sent:
            self._emit(" ", confidence=0.95, is_space=True)
            self._space_sent = True

    def _emit(self, text: str, *, pattern: str = "", confidence: float = 1.0, is_space: bool = False) -> None:
        if self._callback is None or not text:
            return
        self._callback(CwDecodeEvent(text=text, confidence=float(confidence), pattern=pattern, is_space=is_space))

    def _attack_weight(self) -> float:
        return {
            "Fast": 100.0,
            "Normal": 200.0,
            "Slow": 400.0,
        }.get(self._attack, 200.0)

    def _decay_weight(self) -> float:
        return {
            "Fast": 500.0,
            "Normal": 1000.0,
            "Slow": 2000.0,
        }.get(self._decay, 1000.0)

    def _noise_text(self) -> str:
        return {
            "Asterisk": "*",
            "Underscore": "_",
            "Space": " ",
        }.get(self._noise_character, "")

    def _spacing_thresholds(self) -> tuple[float, float]:
        return {
            "Tight": (1.65, 3.35),
            "Normal": (2.0, 4.0),
            "Loose": (2.4, 5.0),
        }.get(self._spacing, (2.0, 4.0))

    def _squelch_threshold(self) -> float:
        return {
            "Off": 0.0,
            "Low": 1.0,
            "Medium": 3.0,
            "High": 6.0,
        }.get(self._squelch, 0.0)

    def _update_squelch_gate(self) -> bool:
        threshold = self._squelch_threshold()
        if threshold <= 0.0:
            self._squelch_open = True
            self._squelch_below_count = 0
            return True

        open_threshold = threshold
        close_threshold = threshold * 0.55
        if self._metric >= open_threshold:
            self._squelch_open = True
            self._squelch_below_count = 0
            return True

        if self._squelch_open:
            if self._metric < close_threshold:
                self._squelch_below_count += 1
            else:
                self._squelch_below_count = 0

            # fldigi-style squelch should have hang time. Closing immediately
            # clips the first/last characters on synthetic and real weak CW.
            if self._squelch_below_count > max(4, int(self._receive_dot / self.DEC_RATIO)):
                self._squelch_open = False
                if self._state != self.RS_IN_TONE:
                    self._state = self.RS_IDLE
                    self._rx_pattern = ""

        return self._squelch_open

    def _maybe_update_tone(self, samples: np.ndarray) -> None:
        if not self._auto_tone_search_enabled or samples.size == 0:
            return

        self._tone_search_buffer.extend(float(x) for x in samples)
        if len(self._tone_search_buffer) < self.CW_SAMPLE_RATE // 4:
            return
        if self._sample_counter - self._last_tone_search_sample < self.CW_SAMPLE_RATE // 2:
            return

        window = np.asarray(self._tone_search_buffer, dtype=np.float64)
        if window.size < 256:
            return
        window = window - float(np.mean(window))
        window *= np.hanning(window.size)

        center = float(self._tone_hz)
        low = max(250.0, center - float(self._tone_search_span_hz))
        high = min(1500.0, center + float(self._tone_search_span_hz))
        if high <= low:
            return

        freqs = np.arange(low, high + 1.0, 10.0, dtype=np.float64)
        n = np.arange(window.size, dtype=np.float64)
        phases = np.exp(-2j * np.pi * freqs[:, None] * n[None, :] / float(self.CW_SAMPLE_RATE))
        powers = np.abs(phases @ window)
        best_index = int(np.argmax(powers))
        best_power = float(powers[best_index])
        median_power = float(np.median(powers)) + 1e-9
        score = best_power / median_power
        if score < 2.5:
            return

        best_freq = float(freqs[best_index])
        if 0 < best_index < len(freqs) - 1:
            left = float(powers[best_index - 1])
            center_power = float(powers[best_index])
            right = float(powers[best_index + 1])
            denom = left - (2.0 * center_power) + right
            if abs(denom) > 1e-9:
                best_freq += 0.5 * (left - right) / denom * 10.0

        self._last_tone_search_sample = self._sample_counter
        self._tone_lock_score = (0.8 * self._tone_lock_score) + (0.2 * min(score, 20.0))
        if self._afc_enabled:
            max_step = 6.0 if self._tone_lock_score >= 5.0 else 2.0
            delta = float(np.clip(best_freq - self._tone_hz, -max_step, max_step))
            self._tone_hz += delta
        else:
            self._tone_hz = best_freq
        self._report_pitch()

    def _report_pitch(self) -> None:
        if self._afc_callback is not None:
            self._afc_callback(float(self._tone_hz))

    def _report_wpm(self) -> None:
        if self._wpm_callback is not None:
            self._wpm_callback(int(round(self._estimated_wpm())))
