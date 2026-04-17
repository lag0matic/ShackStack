from __future__ import annotations

import base64
import json
import math
import re
import sys
from collections import deque
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
from scipy.signal import resample_poly


TARGET_SR = 12_000
MIN_AUDIO_HZ = 200.0
MAX_AUDIO_HZ = 3000.0
MAX_CANDIDATES_PER_CYCLE = 8
FT_TONE_SPACING_HZ = 6.25
FT8_COSTAS = (3, 1, 4, 0, 6, 5, 2)
FT8_SYNC_POSITIONS = ((0, FT8_COSTAS), (36, FT8_COSTAS), (72, FT8_COSTAS))
FT8_SYMBOL_SECONDS = 0.160
FT8_SYMBOL_COUNT = 79
FT4_COSTAS_SEQUENCES = (
    (0, 1, 3, 2),
    (1, 0, 2, 3),
    (2, 3, 1, 0),
    (3, 2, 0, 1),
)
FT4_SYNC_POSITIONS = (
    (1, FT4_COSTAS_SEQUENCES[0]),
    (34, FT4_COSTAS_SEQUENCES[1]),
    (67, FT4_COSTAS_SEQUENCES[2]),
    (100, FT4_COSTAS_SEQUENCES[3]),
)
FT4_SYMBOL_SECONDS = 0.048
FT4_SYMBOL_COUNT = 105
FT_CODEWORD_BITS = 174
FT_MESSAGE_BITS = 91
FT8_GRAY_BITS = ("000", "001", "011", "010", "110", "100", "101", "111")
FT4_GRAY_BITS = ("00", "01", "11", "10")


def _parse_fortran_data_block(text: str, name: str) -> list[int]:
    match = re.search(rf"data\s+{name}/(.*?)/", text, re.IGNORECASE | re.DOTALL)
    if not match:
        raise RuntimeError(f"Missing WSJT parity block: {name}")
    block = match.group(1).replace("&", " ").replace("\n", " ")
    return [int(value.strip()) for value in block.split(",") if value.strip()]


def _load_ft8_ldpc_parity() -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    base_dir = Path(__file__).resolve().parent.parent
    candidate_paths = [
        base_dir / "references" / "wsjt" / "ldpc_174_91_c_parity.f90",
        base_dir / "_internal" / "references" / "wsjt" / "ldpc_174_91_c_parity.f90",
    ]

    meipass = getattr(sys, "_MEIPASS", None)
    if meipass:
        candidate_paths.append(Path(meipass) / "references" / "wsjt" / "ldpc_174_91_c_parity.f90")

    parity_path = next((path for path in candidate_paths if path.exists()), candidate_paths[0])
    text = parity_path.read_text(encoding="utf-8")
    mn_flat = _parse_fortran_data_block(text, "Mn")
    nm_flat = _parse_fortran_data_block(text, "Nm")
    nrw = np.array(_parse_fortran_data_block(text, "nrw"), dtype=np.int32)
    mn = np.array(mn_flat, dtype=np.int32).reshape(174, 3).T
    nm = np.array(nm_flat, dtype=np.int32).reshape(83, 7).T
    return mn, nm, nrw


FT8_LDPC_MN, FT8_LDPC_NM, FT8_LDPC_NRW = _load_ft8_ldpc_parity()


def build_sync_symbol_set(sync_layout: tuple[tuple[int, tuple[int, ...]], ...]) -> set[int]:
    symbols: set[int] = set()
    for symbol_start, tone_pattern in sync_layout:
        for tone_offset, _ in enumerate(tone_pattern):
            symbols.add(symbol_start + tone_offset)
    return symbols


def emit(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def cycle_length_seconds(mode_label: str) -> float:
    return {
        "FT4": 7.5,
        "WSPR": 120.0,
        "JT65": 60.0,
        "JT9": 60.0,
    }.get(mode_label, 15.0)


class WsjtxWorker:
    def __init__(self) -> None:
        self.mode_label = "FT8"
        self.frequency_label = "20m FT8 14.074 MHz USB-D"
        self.auto_sequence_enabled = True
        self.call_cq_enabled = False
        self.station_callsign = ""
        self.station_grid_square = ""
        self.transmit_first_enabled = False
        self.cycle_length = 15.0
        self.requires_accurate_clock = True
        self.is_running = False
        self.decode_count = 0
        self.signal_level_percent = 0
        self.status = "WSJT sidecar ready"
        self._sample_buf = np.zeros(0, dtype=np.float32)
        self._level_history = deque(maxlen=24)
        self._stream_start_utc: datetime | None = None
        self._consumed_samples = 0
        self._last_emitted_cycle = -1
        self._alignment_skip_samples: int | None = None

    def configure(self, payload: dict) -> None:
        self.mode_label = str(payload.get("modeLabel", self.mode_label))
        self.frequency_label = str(payload.get("frequencyLabel", self.frequency_label))
        self.auto_sequence_enabled = bool(payload.get("autoSequenceEnabled", self.auto_sequence_enabled))
        self.call_cq_enabled = bool(payload.get("callCQEnabled", self.call_cq_enabled))
        self.station_callsign = str(payload.get("stationCallsign", self.station_callsign))
        self.station_grid_square = str(payload.get("stationGridSquare", self.station_grid_square))
        self.transmit_first_enabled = bool(payload.get("transmitFirstEnabled", self.transmit_first_enabled))
        self.cycle_length = float(payload.get("cycleLengthSeconds", self.cycle_length))
        self.requires_accurate_clock = bool(payload.get("requiresAccurateClock", self.requires_accurate_clock))
        self.status = f"Configured {self.mode_label}"
        self.emit_telemetry()

    def start(self) -> None:
        self.is_running = True
        self.status = f"Listening for {self.mode_label}"
        self.emit_telemetry()

    def stop(self) -> None:
        self.is_running = False
        self.status = "Weak-signal digital receive stopped"
        self.emit_telemetry()

    def reset(self) -> None:
        self._sample_buf = np.zeros(0, dtype=np.float32)
        self._level_history.clear()
        self._stream_start_utc = None
        self._consumed_samples = 0
        self._last_emitted_cycle = -1
        self._alignment_skip_samples = None
        self.decode_count = 0
        self.signal_level_percent = 0
        self.status = "Weak-signal digital session reset"
        self.emit_telemetry()

    def handle_audio(self, payload: dict) -> None:
        raw = base64.b64decode(str(payload.get("samples", "")))
        if not raw:
            return

        samples = np.frombuffer(raw, dtype=np.float32)
        sr = int(payload.get("sampleRate", 48_000))
        channels = int(payload.get("channels", 1))
        if channels > 1 and samples.size >= channels:
            usable = (samples.size // channels) * channels
            if usable == 0:
                return
            samples = samples[:usable].reshape(-1, channels).mean(axis=1).astype(np.float32, copy=False)

        if sr != TARGET_SR and sr > 0:
            gcd = math.gcd(sr, TARGET_SR)
            samples = resample_poly(samples, TARGET_SR // gcd, sr // gcd).astype(np.float32, copy=False)

        if self._stream_start_utc is None:
            self._stream_start_utc = datetime.now(timezone.utc)
        if self._alignment_skip_samples is None:
            self._alignment_skip_samples = self._compute_alignment_skip_samples(self._stream_start_utc)
        if self._alignment_skip_samples and self._alignment_skip_samples > 0:
            skip = min(self._alignment_skip_samples, samples.size)
            samples = samples[skip:]
            self._alignment_skip_samples -= skip
            if samples.size == 0:
                self.status = f"Waiting for next {self.mode_label} cycle boundary"
                self.emit_telemetry()
                return

        self._sample_buf = np.concatenate([self._sample_buf, samples])
        rms = float(np.sqrt(np.mean(np.square(samples))) if samples.size else 0.0)
        self._level_history.append(rms)
        level = int(np.clip(round(np.mean(self._level_history) * 500.0), 0, 100))
        self.signal_level_percent = level

        frame_samples = max(1, int(TARGET_SR * self.cycle_length))
        complete_cycles = self._sample_buf.size // frame_samples
        if complete_cycles > 1:
            dropped_cycles = complete_cycles - 1
            drop_samples = dropped_cycles * frame_samples
            self._sample_buf = self._sample_buf[drop_samples:]
            self._consumed_samples += drop_samples
            self.status = f"Dropped {dropped_cycles} stale {self.mode_label} cycle(s) to stay live"
        while self._sample_buf.size >= frame_samples:
            cycle = self._sample_buf[:frame_samples]
            self._sample_buf = self._sample_buf[frame_samples:]
            cycle_index = self._consumed_samples // frame_samples
            cycle_start_utc = self._estimate_cycle_start(cycle_index)
            self._analyze_cycle(cycle, cycle_index, cycle_start_utc)
            self._consumed_samples += frame_samples

        self.status = f"Listening for {self.mode_label} candidates"

        self.emit_telemetry()

    def _compute_alignment_skip_samples(self, utc_now: datetime) -> int:
        timestamp = utc_now.timestamp()
        cycle_pos = math.fmod(timestamp, self.cycle_length)
        if cycle_pos < 0:
            cycle_pos += self.cycle_length
        seconds_to_next = self.cycle_length - cycle_pos
        if seconds_to_next >= self.cycle_length - 1e-3:
            seconds_to_next = 0.0
        return int(round(seconds_to_next * TARGET_SR))

    def _estimate_cycle_start(self, cycle_index: int) -> datetime:
        if self._stream_start_utc is None:
            return datetime.now(timezone.utc)
        return datetime.fromtimestamp(
            self._stream_start_utc.timestamp() + (cycle_index * self.cycle_length),
            tz=timezone.utc)

    def _analyze_cycle(self, cycle: np.ndarray, cycle_index: int, cycle_start_utc: datetime) -> None:
        if cycle_index <= self._last_emitted_cycle:
            return

        candidates = self._extract_cycle_candidates(cycle)
        if not candidates:
            self.status = f"{self.mode_label} cycle analyzed - no strong candidates"
            self._last_emitted_cycle = cycle_index
            return

        mode_hint = "likely FT4" if self.mode_label == "FT4" else f"likely {self.mode_label}"
        for offset_hz, snr_db, confidence, fit_score, width_hz, dt_seconds, payload_quality, tone_preview, bit_quality, bit_preview, codeword_quality, codeword_preview, has_full_codeword, ldpc_success, ldpc_nchecks, ldpc_preview in candidates:
            emit({
                "type": "decode",
                "timestampUtc": cycle_start_utc.isoformat().replace("+00:00", "Z"),
                "modeLabel": self.mode_label,
                "frequencyOffsetHz": offset_hz,
                "snrDb": snr_db,
                "deltaTimeSeconds": round(dt_seconds, 3),
                "messageText": f"[frame] {mode_hint} at {offset_hz:+d} Hz | width ~{width_hz:.0f} Hz | sync {fit_score:.0%} | payload {payload_quality:.0%} | bits {bit_quality:.0%} | llr {codeword_quality:.0%}{' full' if has_full_codeword else ' partial'} | ldpc {'ok' if ldpc_success else f'n{ldpc_nchecks}'} | tones {tone_preview} | {bit_preview} | {codeword_preview} | {ldpc_preview}",
                "confidence": round(confidence, 3),
                "isDirectedToMe": False,
                "isCq": False,
            })

        self.decode_count += len(candidates)
        self.status = f"{self.mode_label} cycle analyzed - {len(candidates)} candidate signals"
        self._last_emitted_cycle = cycle_index

    def _extract_cycle_candidates(self, cycle: np.ndarray) -> list[tuple[int, int, float, float, float, float, float, str, float, str, float, str, bool, bool, int, str]]:
        if self.mode_label == "FT8":
            return self._extract_protocol_candidates(
                cycle=cycle,
                symbol_seconds=FT8_SYMBOL_SECONDS,
                symbol_count=FT8_SYMBOL_COUNT,
                tones=8,
                sync_layout=FT8_SYNC_POSITIONS,
                gray_map=FT8_GRAY_BITS,
                expected_codeword_bits=FT_CODEWORD_BITS)

        if self.mode_label == "FT4":
            return self._extract_protocol_candidates(
                cycle=cycle,
                symbol_seconds=FT4_SYMBOL_SECONDS,
                symbol_count=FT4_SYMBOL_COUNT,
                tones=4,
                sync_layout=FT4_SYNC_POSITIONS,
                gray_map=FT4_GRAY_BITS,
                expected_codeword_bits=FT_CODEWORD_BITS)

        return self._extract_generic_cycle_candidates(cycle)

    def _extract_protocol_candidates(
        self,
        cycle: np.ndarray,
        symbol_seconds: float,
        symbol_count: int,
        tones: int,
        sync_layout: tuple[tuple[int, tuple[int, ...]], ...],
        gray_map: tuple[str, ...],
        expected_codeword_bits: int,
    ) -> list[tuple[int, int, float, float, float, float, float, str, float, str, float, str, bool, bool, int, str]]:
        symbol_samples = int(round(symbol_seconds * TARGET_SR))
        if symbol_samples <= 0:
            return []

        signal_samples = symbol_samples * symbol_count
        if cycle.size < signal_samples:
            return []

        slack = cycle.size - signal_samples
        step = max(1, symbol_samples // 4)
        bin_hz = TARGET_SR / symbol_samples
        min_bin = int(math.floor(MIN_AUDIO_HZ / bin_hz))
        max_bin = int(math.ceil(MAX_AUDIO_HZ / bin_hz)) - tones
        if max_bin <= min_bin:
            return []

        sync_symbols = build_sync_symbol_set(sync_layout)
        candidates: list[tuple[int, int, float, float, float, float, float, str, float, str, float, str, bool, bool, int, str]] = []
        prelim_candidates: list[tuple[int, int, float, float, int, float]] = []
        used_offsets: list[int] = []

        for start_offset in range(0, slack + 1, step):
            symbol_power = self._build_symbol_power(cycle, start_offset, symbol_samples, symbol_count)
            if symbol_power is None:
                continue

            noise_floor = float(np.median(symbol_power) + 1e-9)
            if noise_floor <= 0.0:
                continue

            for base_bin in range(min_bin, max_bin + 1):
                sync_energy = 0.0
                off_energy = 0.0
                hits = 0
                total = 0

                for symbol_start, tone_pattern in sync_layout:
                    for tone_offset, tone in enumerate(tone_pattern):
                        symbol_index = symbol_start + tone_offset
                        if symbol_index >= symbol_power.shape[0]:
                            continue
                        row = symbol_power[symbol_index]
                        sync_bin = base_bin + tone
                        if sync_bin >= row.size:
                            continue
                        peak = float(row[sync_bin])
                        sync_energy += peak
                        total += 1
                        if peak > noise_floor * 2.0:
                            hits += 1

                        mask_start = max(base_bin, sync_bin - 1)
                        mask_end = min(base_bin + tones, sync_bin + 2)
                        off_slice = row[base_bin:base_bin + tones].copy()
                        off_slice[(mask_start - base_bin):(mask_end - base_bin)] = 0.0
                        off_energy += float(np.sum(off_slice))

                if total == 0:
                    continue

                mean_sync = sync_energy / total
                mean_off = off_energy / max(total * max(tones - 1, 1), 1)
                if mean_sync <= mean_off:
                    continue

                fit = float(np.clip((hits / total) * (mean_sync / max(mean_sync + mean_off, 1e-9)), 0.0, 1.0))
                if fit < 0.22:
                    continue

                snr_db = int(round(20.0 * math.log10(max(mean_sync / max(noise_floor, 1e-9), 1e-6))))
                if snr_db < 3:
                    continue

                refined_base_bin = self._refine_protocol_base_bin(
                    symbol_power=symbol_power,
                    base_bin=base_bin,
                    tones=tones,
                    sync_layout=sync_layout)
                offset_hz = int(round(refined_base_bin * bin_hz))
                if any(abs(offset_hz - existing) < int(round(bin_hz * 2)) for existing in used_offsets):
                    continue

                dt_seconds = start_offset / TARGET_SR
                prelim_candidates.append((offset_hz, snr_db, fit, dt_seconds, refined_base_bin, start_offset))
                used_offsets.append(offset_hz)

        prelim_candidates.sort(key=lambda item: (item[2], item[1]), reverse=True)
        if self.mode_label == "FT8":
            prelim_candidates = prelim_candidates[:MAX_CANDIDATES_PER_CYCLE]
        else:
            prelim_candidates = prelim_candidates[:MAX_CANDIDATES_PER_CYCLE]

        for offset_hz, snr_db, fit, dt_seconds, refined_base_bin, start_offset in prelim_candidates:
            if self.mode_label == "FT8":
                payload_quality, tone_preview, bit_quality, bit_preview, codeword_quality, codeword_preview, has_full_codeword, ldpc_success, ldpc_nchecks, ldpc_preview = self._estimate_ft8_payload_quality(
                    cycle=cycle,
                    start_offset=start_offset,
                    frequency_hz=refined_base_bin * bin_hz,
                    gray_map=gray_map,
                    expected_codeword_bits=expected_codeword_bits)
            else:
                symbol_power = self._build_symbol_power(cycle, start_offset, symbol_samples, symbol_count)
                if symbol_power is None:
                    continue
                payload_quality, tone_preview, bit_quality, bit_preview, codeword_quality, codeword_preview, has_full_codeword, ldpc_success, ldpc_nchecks, ldpc_preview = self._estimate_payload_quality(
                    symbol_power=symbol_power,
                    base_bin=refined_base_bin,
                    tones=tones,
                    sync_symbols=sync_symbols,
                    gray_map=gray_map,
                    expected_codeword_bits=expected_codeword_bits)
            confidence = float(np.clip((fit * 0.4) + (payload_quality * 0.18) + (bit_quality * 0.17) + (codeword_quality * 0.15) + (min(max(snr_db, 0), 24) / 120.0), 0.05, 0.99))
            width_hz = tones * bin_hz
            candidates.append((offset_hz, snr_db, confidence, fit, width_hz, dt_seconds, payload_quality, tone_preview, bit_quality, bit_preview, codeword_quality, codeword_preview, has_full_codeword, ldpc_success, ldpc_nchecks, ldpc_preview))

        candidates.sort(key=lambda item: (item[2], item[1]), reverse=True)
        return candidates[:MAX_CANDIDATES_PER_CYCLE]

    def _refine_protocol_base_bin(
        self,
        symbol_power: np.ndarray,
        base_bin: int,
        tones: int,
        sync_layout: tuple[tuple[int, tuple[int, ...]], ...],
    ) -> int:
        best = base_bin
        best_score = -1.0

        for delta in (-2, -1, 0, 1, 2):
            candidate_bin = base_bin + delta
            sync_energy = 0.0
            total = 0

            for symbol_start, tone_pattern in sync_layout:
                for tone_offset, tone in enumerate(tone_pattern):
                    symbol_index = symbol_start + tone_offset
                    if symbol_index >= symbol_power.shape[0]:
                        continue

                    row = symbol_power[symbol_index]
                    tone_energies = self._extract_tone_energies(row, candidate_bin, tones)
                    if tone_energies.size != tones:
                        continue

                    sync_energy += float(tone_energies[tone])
                    total += 1

            if total == 0:
                continue

            score = sync_energy / total
            if score > best_score:
                best_score = score
                best = candidate_bin

        return best

    def _build_symbol_power(
        self,
        cycle: np.ndarray,
        start_offset: int,
        symbol_samples: int,
        symbol_count: int,
    ) -> np.ndarray | None:
        window = np.hanning(symbol_samples).astype(np.float32)
        rows: list[np.ndarray] = []
        for symbol_index in range(symbol_count):
            start = start_offset + (symbol_index * symbol_samples)
            end = start + symbol_samples
            if end > cycle.size:
                return None
            frame = cycle[start:end]
            weighted = frame.astype(np.float32, copy=False) * window
            spectrum = np.fft.rfft(weighted)
            rows.append(np.abs(spectrum).astype(np.float32, copy=False))
        return np.vstack(rows)

    def _estimate_payload_quality(
        self,
        symbol_power: np.ndarray,
        base_bin: int,
        tones: int,
        sync_symbols: set[int],
        gray_map: tuple[str, ...],
        expected_codeword_bits: int,
    ) -> tuple[float, str, float, str, float, str, bool, bool, int, str]:
        margins: list[float] = []
        preview_tones: list[str] = []
        payload_symbols: list[int] = []
        tone_vectors: list[np.ndarray] = []

        for symbol_index, row in enumerate(symbol_power):
            if symbol_index in sync_symbols:
                continue

            tone_slice = self._extract_tone_energies(row, base_bin, tones)
            if tone_slice.size != tones:
                continue

            strongest = int(np.argmax(tone_slice))
            sorted_slice = np.sort(tone_slice)
            peak = float(sorted_slice[-1]) if sorted_slice.size else 0.0
            second = float(sorted_slice[-2]) if sorted_slice.size > 1 else 0.0
            margin = (peak - second) / max(peak + second, 1e-9)
            margins.append(float(np.clip(margin, 0.0, 1.0)))
            payload_symbols.append(strongest)
            tone_vectors.append(tone_slice.astype(np.float32, copy=True))

            if len(preview_tones) < 18:
                preview_tones.append(str(strongest))

        payload_quality = float(np.mean(margins)) if margins else 0.0
        bit_preview, bit_quality = self._symbols_to_gray_bits(payload_symbols, margins, gray_map)
        codeword_preview, codeword_quality, has_full_codeword = self._build_soft_codeword(tone_vectors, gray_map, expected_codeword_bits)
        ldpc_success, ldpc_nchecks, ldpc_preview = self._attempt_ldpc_decode(codeword_preview, tone_vectors, gray_map, expected_codeword_bits, has_full_codeword)
        return payload_quality, "".join(preview_tones), bit_quality, bit_preview, codeword_quality, codeword_preview, has_full_codeword, ldpc_success, ldpc_nchecks, ldpc_preview

    def _estimate_ft8_payload_quality(
        self,
        cycle: np.ndarray,
        start_offset: int,
        frequency_hz: float,
        gray_map: tuple[str, ...],
        expected_codeword_bits: int,
    ) -> tuple[float, str, float, str, float, str, bool, bool, int, str]:
        start_offset, frequency_hz = self._refine_ft8_candidate_alignment(cycle, start_offset, frequency_hz)
        tone_vectors = self._extract_ft8_payload_tones(cycle, start_offset, frequency_hz)
        if not tone_vectors:
            return 0.0, "", 0.0, "", 0.0, "", False, False, -1, "ldpc:pending"

        margins: list[float] = []
        preview_tones: list[str] = []
        payload_symbols: list[int] = []
        for tone_slice in tone_vectors:
            strongest = int(np.argmax(tone_slice))
            sorted_slice = np.sort(tone_slice)
            peak = float(sorted_slice[-1]) if sorted_slice.size else 0.0
            second = float(sorted_slice[-2]) if sorted_slice.size > 1 else 0.0
            margin = (peak - second) / max(peak + second, 1e-9)
            margins.append(float(np.clip(margin, 0.0, 1.0)))
            payload_symbols.append(strongest)
            if len(preview_tones) < 18:
                preview_tones.append(str(strongest))

        payload_quality = float(np.mean(margins)) if margins else 0.0
        bit_preview, bit_quality = self._symbols_to_gray_bits(payload_symbols, margins, gray_map)
        codeword_preview, codeword_quality, has_full_codeword = self._build_soft_codeword(tone_vectors, gray_map, expected_codeword_bits)
        ldpc_success, ldpc_nchecks, ldpc_preview = self._attempt_ldpc_decode(codeword_preview, tone_vectors, gray_map, expected_codeword_bits, has_full_codeword)
        return payload_quality, "".join(preview_tones), bit_quality, bit_preview, codeword_quality, codeword_preview, has_full_codeword, ldpc_success, ldpc_nchecks, ldpc_preview

    def _refine_ft8_candidate_alignment(
        self,
        cycle: np.ndarray,
        start_offset: int,
        frequency_hz: float,
    ) -> tuple[int, float]:
        best_offset = start_offset
        best_frequency = frequency_hz
        best_score = -1.0
        symbol_step = 32
        sync_positions = ((0, FT8_COSTAS), (36, FT8_COSTAS), (72, FT8_COSTAS))

        for offset_delta in range(-8, 9, 2):
            candidate_offset = start_offset + (offset_delta * 60)
            for freq_delta in np.arange(-2.5, 2.51, 0.5, dtype=np.float32):
                candidate_frequency = frequency_hz + float(freq_delta)
                downsampled = self._downconvert_ft8_candidate(cycle, candidate_offset, candidate_frequency)
                if downsampled is None:
                    continue

                score = 0.0
                valid = 0
                for symbol_start, tone_pattern in sync_positions:
                    for tone_offset, tone in enumerate(tone_pattern):
                        symbol_index = symbol_start + tone_offset
                        symbol_pos = symbol_index * symbol_step
                        symbol = downsampled[symbol_pos:symbol_pos + symbol_step]
                        if symbol.size != symbol_step:
                            continue
                        spectrum = np.fft.fft(symbol)
                        score += float(np.abs(spectrum[tone]) ** 2)
                        valid += 1

                if valid <= 0:
                    continue

                score /= valid
                if score > best_score:
                    best_score = score
                    best_offset = candidate_offset
                    best_frequency = candidate_frequency

        return best_offset, best_frequency

    def _downconvert_ft8_candidate(
        self,
        cycle: np.ndarray,
        start_offset: int,
        frequency_hz: float,
    ) -> np.ndarray | None:
        downsampled = self._ft8_downsample_cycle(cycle, frequency_hz)
        if downsampled is None:
            return None
        start_200hz = int(round(start_offset / 60.0))
        expected_samples = FT8_SYMBOL_COUNT * 32
        if start_200hz < 0 or start_200hz + expected_samples > downsampled.size:
            return None
        return downsampled[start_200hz:start_200hz + expected_samples]

    def _ft8_downsample_cycle(
        self,
        cycle: np.ndarray,
        frequency_hz: float,
    ) -> np.ndarray | None:
        nmax = 15 * TARGET_SR
        nfft1 = 192000
        nfft2 = 3200
        if cycle.size < nmax:
            return None

        x = np.zeros(nfft1, dtype=np.float32)
        x[:nmax] = cycle[:nmax].astype(np.float32, copy=False)
        cx = np.fft.rfft(x)

        df = TARGET_SR / nfft1
        baud = TARGET_SR / 1920.0
        i0 = int(round(frequency_hz / df))
        ft = frequency_hz + (8.5 * baud)
        fb = frequency_hz - (1.5 * baud)
        it = min(int(round(ft / df)), nfft1 // 2)
        ib = max(1, int(round(fb / df)))
        if it <= ib:
            return None

        c1 = np.zeros(nfft2, dtype=np.complex64)
        band = cx[ib:it + 1]
        k = min(band.size, nfft2)
        if k <= 0:
            return None

        c1[:k] = band[:k].astype(np.complex64, copy=False)
        taper_len = min(101, k)
        if taper_len > 1:
            taper = 0.5 * (1.0 + np.cos(np.arange(taper_len, dtype=np.float32) * np.pi / max(taper_len - 1, 1)))
            c1[:taper_len] *= taper[::-1]
            c1[k - taper_len:k] *= taper

        shift = i0 - ib
        if shift != 0:
            c1 = np.roll(c1, shift)

        time_stream = np.fft.ifft(c1)
        fac = 1.0 / math.sqrt(float(nfft1) * nfft2)
        return (time_stream * fac).astype(np.complex64, copy=False)

    def _extract_ft8_payload_tones(
        self,
        cycle: np.ndarray,
        start_offset: int,
        frequency_hz: float,
    ) -> list[np.ndarray]:
        downsampled = self._downconvert_ft8_candidate(cycle, start_offset, frequency_hz)
        if downsampled is None:
            return []

        tone_vectors: list[np.ndarray] = []
        sync_symbols = build_sync_symbol_set(FT8_SYNC_POSITIONS)
        for symbol_index in range(FT8_SYMBOL_COUNT):
            start = symbol_index * 32
            symbol = downsampled[start:start + 32]
            if symbol.size != 32:
                return []
            spectrum = np.fft.fft(symbol)
            tone_slice = np.abs(spectrum[:8]).astype(np.float32, copy=False)
            if symbol_index not in sync_symbols:
                tone_vectors.append(tone_slice.copy())
        return tone_vectors

    def _extract_tone_energies(self, row: np.ndarray, base_bin: int, tones: int) -> np.ndarray:
        energies = np.zeros(tones, dtype=np.float32)
        left = max(0, base_bin - 3)
        right = min(row.size, base_bin + tones + 3)
        baseline = float(np.median(row[left:right])) if right > left else 0.0

        for tone_index in range(tones):
            center = base_bin + tone_index
            start = max(0, center - 1)
            end = min(row.size, center + 2)
            if end <= start:
                return np.zeros(0, dtype=np.float32)
            peak = float(np.max(row[start:end]))
            energies[tone_index] = max(0.0, peak - baseline)

        return energies

    def _symbols_to_gray_bits(
        self,
        payload_symbols: list[int],
        margins: list[float],
        gray_map: tuple[str, ...],
    ) -> tuple[str, float]:
        if not payload_symbols:
            return "", 0.0

        bits: list[str] = []
        bit_scores: list[float] = []
        for symbol, margin in zip(payload_symbols, margins):
            if symbol < 0 or symbol >= len(gray_map):
                continue

            symbol_bits = gray_map[symbol]
            bits.append(symbol_bits)
            bit_scores.extend([margin] * len(symbol_bits))

        bit_string = "".join(bits)
        preview = bit_string[:36]
        if len(bit_string) > 36:
            preview += "..."
        quality = float(np.mean(bit_scores)) if bit_scores else 0.0
        return preview, quality

    def _build_soft_codeword(
        self,
        tone_vectors: list[np.ndarray],
        gray_map: tuple[str, ...],
        expected_codeword_bits: int,
    ) -> tuple[str, float, bool]:
        if not tone_vectors or expected_codeword_bits <= 0:
            return "", 0.0, False

        if len(gray_map) == 8 and len(tone_vectors) >= 58:
            return self._build_ft8_group_soft_codeword(
                tone_vectors=tone_vectors,
                gray_map=gray_map,
                expected_codeword_bits=expected_codeword_bits)

        bit_llrs: list[float] = []
        bits_per_symbol = len(gray_map[0]) if gray_map else 0
        if bits_per_symbol <= 0:
            return "", 0.0, False

        for tone_slice in tone_vectors:
            if tone_slice.size != len(gray_map):
                continue

            for bit_index in range(bits_per_symbol):
                zero_group = []
                one_group = []
                for tone_index, bit_pattern in enumerate(gray_map):
                    energy = float(tone_slice[tone_index])
                    if bit_pattern[bit_index] == "0":
                        zero_group.append(energy)
                    else:
                        one_group.append(energy)

                if not zero_group or not one_group:
                    continue

                zero_metric = max(zero_group)
                one_metric = max(one_group)
                bit_llrs.append(one_metric - zero_metric)
                if len(bit_llrs) >= expected_codeword_bits:
                    break
            if len(bit_llrs) >= expected_codeword_bits:
                break

        if not bit_llrs:
            return "", 0.0, False

        has_full_codeword = len(bit_llrs) >= expected_codeword_bits
        bit_llrs = bit_llrs[:expected_codeword_bits]
        hard_bits = "".join("1" if value >= 0.0 else "0" for value in bit_llrs)
        preview = f"cw{len(hard_bits)}:{hard_bits[:36]}"
        if len(hard_bits) > 36:
            preview += "..."
        llr_magnitudes = [abs(value) for value in bit_llrs]
        peak = max(llr_magnitudes) if llr_magnitudes else 0.0
        quality = float(np.mean([value / max(peak, 1e-9) for value in llr_magnitudes])) if llr_magnitudes else 0.0
        return preview, quality, has_full_codeword

    def _build_ft8_group_soft_codeword(
        self,
        tone_vectors: list[np.ndarray],
        gray_map: tuple[str, ...],
        expected_codeword_bits: int,
    ) -> tuple[str, float, bool]:
        if len(tone_vectors) < 58 or expected_codeword_bits <= 0:
            return "", 0.0, False

        bmeta = np.zeros(expected_codeword_bits, dtype=np.float32)
        bmetb = np.zeros(expected_codeword_bits, dtype=np.float32)
        bmetc = np.zeros(expected_codeword_bits, dtype=np.float32)
        graymap = np.array([int(bits, 2) for bits in gray_map], dtype=np.int32)

        for nsym in (1, 2, 3):
            total_bits = 3 * nsym
            nt = 1 << total_bits
            metric_target = bmeta if nsym == 1 else bmetb if nsym == 2 else bmetc
            ibmax = 3 * nsym - 1

            for half_index, half_start in enumerate((0, 29)):
                half_vectors = tone_vectors[half_start:half_start + 29]
                for group_start in range(0, 29, nsym):
                    group = half_vectors[group_start:group_start + nsym]
                    if len(group) < nsym:
                        continue

                    candidate_scores = np.full(nt, -1e9, dtype=np.float32)
                    for candidate in range(nt):
                        score = 0.0
                        valid = True
                        for symbol_offset in range(nsym):
                            shift = 3 * (nsym - symbol_offset - 1)
                            symbol_value = (candidate >> shift) & 0x7
                            tone_matches = np.where(graymap == symbol_value)[0]
                            if tone_matches.size == 0:
                                valid = False
                                break
                            tone_index = int(tone_matches[0])
                            tone_slice = group[symbol_offset]
                            if tone_index >= tone_slice.size:
                                valid = False
                                break
                            score += float(tone_slice[tone_index])
                        if valid:
                            candidate_scores[candidate] = score

                    for ib in range(ibmax + 1):
                        codeword_index = (half_index * 87) + (group_start * 3) + ib
                        if codeword_index >= expected_codeword_bits:
                            continue

                        bit_position = ibmax - ib
                        one_scores = [candidate_scores[idx] for idx in range(nt) if ((idx >> bit_position) & 0x1) == 1]
                        zero_scores = [candidate_scores[idx] for idx in range(nt) if ((idx >> bit_position) & 0x1) == 0]
                        if not one_scores or not zero_scores:
                            continue
                        metric_target[codeword_index] = max(one_scores) - max(zero_scores)

        scalefac = 2.83
        llr = scalefac * (
            self._normalize_metric_vector(bmeta)
            + self._normalize_metric_vector(bmetb)
            + self._normalize_metric_vector(bmetc)
        ) / 3.0

        hard_bits = "".join("1" if value >= 0.0 else "0" for value in llr[:expected_codeword_bits])
        preview = f"cw{len(hard_bits)}:{hard_bits[:36]}"
        if len(hard_bits) > 36:
            preview += "..."
        llr_magnitudes = np.abs(llr[:expected_codeword_bits])
        peak = float(np.max(llr_magnitudes)) if llr_magnitudes.size else 0.0
        quality = float(np.mean(llr_magnitudes / max(peak, 1e-9))) if llr_magnitudes.size else 0.0
        return preview, quality, True

    def _attempt_ldpc_decode(
        self,
        codeword_preview: str,
        tone_vectors: list[np.ndarray],
        gray_map: tuple[str, ...],
        expected_codeword_bits: int,
        has_full_codeword: bool,
    ) -> tuple[bool, int, str]:
        if not has_full_codeword or len(gray_map) != 8 or expected_codeword_bits != FT_CODEWORD_BITS:
            return False, -1, "ldpc:pending"

        llr = self._build_ft8_llr_vector(tone_vectors, gray_map, expected_codeword_bits)
        if llr.size != expected_codeword_bits:
            return False, -1, "ldpc:partial"

        success, nchecks, message_bits, crc_ok = self._bp_decode_174_91(llr)
        if success:
            preview = "".join(str(bit) for bit in message_bits[:32])
            return True, 0, f"msg91:{preview}..."
        if nchecks == 0 and not crc_ok:
            return False, 0, "crc:fail"
        return False, nchecks, f"ldpc:n{nchecks}"

    def _build_ft8_llr_vector(
        self,
        tone_vectors: list[np.ndarray],
        gray_map: tuple[str, ...],
        expected_codeword_bits: int,
    ) -> np.ndarray:
        if len(tone_vectors) < 58:
            return np.zeros(0, dtype=np.float32)

        graymap = np.array([int(bits, 2) for bits in gray_map], dtype=np.int32)
        bmeta = np.zeros(expected_codeword_bits, dtype=np.float32)
        bmetb = np.zeros(expected_codeword_bits, dtype=np.float32)
        bmetc = np.zeros(expected_codeword_bits, dtype=np.float32)

        for nsym in (1, 2, 3):
            total_bits = 3 * nsym
            nt = 1 << total_bits
            metric_target = bmeta if nsym == 1 else bmetb if nsym == 2 else bmetc
            ibmax = 3 * nsym - 1

            for half_index, half_start in enumerate((0, 29)):
                half_vectors = tone_vectors[half_start:half_start + 29]
                for group_start in range(0, 29, nsym):
                    group = half_vectors[group_start:group_start + nsym]
                    if len(group) < nsym:
                        continue

                    candidate_scores = np.full(nt, -1e9, dtype=np.float32)
                    for candidate in range(nt):
                        score = 0.0
                        valid = True
                        for symbol_offset in range(nsym):
                            shift = 3 * (nsym - symbol_offset - 1)
                            symbol_value = (candidate >> shift) & 0x7
                            tone_matches = np.where(graymap == symbol_value)[0]
                            if tone_matches.size == 0:
                                valid = False
                                break
                            tone_index = int(tone_matches[0])
                            tone_slice = group[symbol_offset]
                            if tone_index >= tone_slice.size:
                                valid = False
                                break
                            score += float(tone_slice[tone_index])
                        if valid:
                            candidate_scores[candidate] = score

                    for ib in range(ibmax + 1):
                        codeword_index = (half_index * 87) + (group_start * 3) + ib
                        if codeword_index >= expected_codeword_bits:
                            continue
                        bit_position = ibmax - ib
                        one_scores = [candidate_scores[idx] for idx in range(nt) if ((idx >> bit_position) & 0x1) == 1]
                        zero_scores = [candidate_scores[idx] for idx in range(nt) if ((idx >> bit_position) & 0x1) == 0]
                        if not one_scores or not zero_scores:
                            continue
                        metric_target[codeword_index] = max(one_scores) - max(zero_scores)

        scalefac = 2.83
        return scalefac * (
            self._normalize_metric_vector(bmeta)
            + self._normalize_metric_vector(bmetb)
            + self._normalize_metric_vector(bmetc)
        ) / 3.0

    def _bp_decode_174_91(self, llr: np.ndarray, max_iterations: int = 30) -> tuple[bool, int, np.ndarray, bool]:
        n = FT_CODEWORD_BITS
        m = FT8_LDPC_NM.shape[1]
        nrw = FT8_LDPC_NRW
        nm = FT8_LDPC_NM
        mn = FT8_LDPC_MN

        toc = np.zeros((7, m), dtype=np.float32)
        tov = np.zeros((3, n), dtype=np.float32)

        for check_index in range(m):
            for edge_index in range(int(nrw[check_index])):
                bit_index = int(nm[edge_index, check_index]) - 1
                toc[edge_index, check_index] = llr[bit_index]

        ncnt = 0
        nclast = 0
        zn = np.zeros(n, dtype=np.float32)

        for iteration in range(max_iterations + 1):
            zn = llr + np.sum(tov, axis=0)
            cw = (zn > 0.0).astype(np.int8)

            ncheck = 0
            for check_index in range(m):
                bit_indices = [int(nm[edge_index, check_index]) - 1 for edge_index in range(int(nrw[check_index])) if int(nm[edge_index, check_index]) > 0]
                syndrome = int(np.sum(cw[bit_indices])) % 2
                if syndrome != 0:
                    ncheck += 1

            if ncheck == 0:
                message_bits = cw[:FT_MESSAGE_BITS]
                crc_ok = self._check_ft8_crc(message_bits)
                if crc_ok:
                    return True, 0, message_bits, True
                return False, 0, message_bits, False

            if iteration > 0:
                nd = ncheck - nclast
                if nd < 0:
                    ncnt = 0
                else:
                    ncnt += 1
                if ncnt >= 5 and iteration >= 10 and ncheck > 15:
                    return False, ncheck, cw[:FT_MESSAGE_BITS], False
            nclast = ncheck

            for check_index in range(m):
                for edge_index in range(int(nrw[check_index])):
                    bit_index = int(nm[edge_index, check_index]) - 1
                    value = zn[bit_index]
                    for kk in range(3):
                        if int(mn[kk, bit_index]) - 1 == check_index:
                            value -= tov[kk, bit_index]
                    toc[edge_index, check_index] = value

            tanhtoc = np.tanh(-toc / 2.0)
            for bit_index in range(n):
                for edge_slot in range(3):
                    check_index = int(mn[edge_slot, bit_index]) - 1
                    if check_index < 0:
                        continue
                    product = 1.0
                    for edge_index in range(int(nrw[check_index])):
                        other_bit = int(nm[edge_index, check_index]) - 1
                        if other_bit == bit_index:
                            continue
                        product *= tanhtoc[edge_index, check_index]
                    clipped = float(np.clip(-product, -0.999999, 0.999999))
                    tov[edge_slot, bit_index] = 2.0 * np.arctanh(clipped)

        final_cw = (zn > 0.0).astype(np.int8)
        return False, nclast, final_cw[:FT_MESSAGE_BITS], False

    def _check_ft8_crc(self, decoded91: np.ndarray) -> bool:
        if decoded91.size < FT_MESSAGE_BITS:
            return False
        message77 = [int(bit) for bit in decoded91[:77]]
        received_crc = [int(bit) for bit in decoded91[77:91]]
        computed_crc = self._compute_crc14(message77 + ([0] * 14))
        return computed_crc == received_crc

    def _compute_crc14(self, bits: list[int]) -> list[int]:
        if len(bits) < 15:
            return [0] * 14
        polynomial = [1, 1, 0, 0, 1, 1, 1, 0, 1, 0, 1, 0, 1, 1, 1]
        r = bits[:15]
        for i in range(len(bits) - 15 + 1):
            if i + 15 < len(bits):
                r[14] = bits[i + 15]
            r = [(r[j] + (r[0] * polynomial[j])) % 2 for j in range(15)]
            r = r[1:] + [r[0]]
        return r[:14]

    def _normalize_metric_vector(self, metrics: np.ndarray) -> np.ndarray:
        if metrics.size == 0:
            return metrics
        centered = metrics - float(np.mean(metrics))
        scale = float(np.std(centered))
        if scale <= 1e-9:
            return centered
        return centered / scale

    def _extract_generic_cycle_candidates(self, cycle: np.ndarray) -> list[tuple[int, int, float, float, float, float, float, str, float, str, float, str, bool]]:
        fft_size = 2048 if self.mode_label != "FT4" else 1024
        hop = fft_size // 4
        if cycle.size < fft_size * 2:
            return []

        window = np.hanning(fft_size).astype(np.float32)
        freqs = np.fft.rfftfreq(fft_size, 1.0 / TARGET_SR)
        mask = (freqs >= MIN_AUDIO_HZ) & (freqs <= MAX_AUDIO_HZ)
        if not np.any(mask):
            return []

        band_freqs = freqs[mask]
        if band_freqs.size < 16:
            return []

        frame_count = 1 + ((cycle.size - fft_size) // hop)
        if frame_count < 8:
            return []

        hit_hist = np.zeros(band_freqs.size, dtype=np.float32)
        energy_hist = np.zeros(band_freqs.size, dtype=np.float32)
        frame_hits: list[np.ndarray] = []

        for frame_index in range(frame_count):
            start = frame_index * hop
            frame = cycle[start:start + fft_size]
            if frame.size < fft_size:
                break

            weighted = frame.astype(np.float32, copy=False) * window
            spectrum = np.fft.rfft(weighted)
            band_power = np.abs(spectrum[mask])
            if band_power.size < 16:
                continue

            noise_floor = float(np.median(band_power) + 1e-9)
            ranked = np.argsort(band_power)[::-1]
            selected: list[int] = []
            for idx in ranked:
                if band_power[idx] < noise_floor * 2.2:
                    break
                if any(abs(idx - existing) < 4 for existing in selected):
                    continue
                selected.append(int(idx))
                if len(selected) >= 10:
                    break

            hit_row = np.zeros(band_freqs.size, dtype=np.float32)
            for idx in selected:
                hit_hist[idx] += 1.0
                energy_hist[idx] += float(band_power[idx])
                hit_row[idx] = 1.0
            frame_hits.append(hit_row)

        if not np.any(hit_hist):
            return []

        kernel = np.array([0.2, 0.6, 0.2], dtype=np.float32)
        hit_smooth = np.convolve(hit_hist, kernel, mode="same")
        energy_smooth = np.convolve(energy_hist, kernel, mode="same")
        baseline_energy = float(np.median(energy_smooth[energy_smooth > 0])) if np.any(energy_smooth > 0) else 0.0
        min_hits = max(3.0, frame_count * 0.18)

        frame_matrix = np.vstack(frame_hits) if frame_hits else np.zeros((0, band_freqs.size), dtype=np.float32)
        candidates: list[tuple[int, int, float, float, float, float, float, str, float, str, float, str, bool]] = []
        ranked = np.argsort(energy_smooth)[::-1]
        used_offsets: list[int] = []
        bin_hz = float(band_freqs[1] - band_freqs[0]) if band_freqs.size > 1 else 1.0
        for idx in ranked:
            if hit_smooth[idx] < min_hits:
                continue
            offset_hz = int(round(float(band_freqs[idx])))
            if any(abs(offset_hz - existing) < 30 for existing in used_offsets):
                continue

            peak_energy = float(energy_smooth[idx])
            if baseline_energy <= 0.0:
                snr_db = 0
            else:
                snr_db = int(round(20.0 * math.log10(max(peak_energy / baseline_energy, 1e-6))))
            if snr_db < 4:
                continue

            persistence = float(np.clip(hit_smooth[idx] / max(frame_count, 1), 0.0, 1.0))
            width_bins = 1
            left = idx
            while left > 0 and energy_smooth[left - 1] >= peak_energy * 0.45:
                left -= 1
                width_bins += 1
            right = idx
            while right < energy_smooth.size - 1 and energy_smooth[right + 1] >= peak_energy * 0.45:
                right += 1
                width_bins += 1
            width_hz = width_bins * bin_hz
            mode_fit = self._estimate_mode_fit(frame_matrix, idx, bin_hz)
            confidence = float(np.clip((persistence * 0.45) + (mode_fit * 0.35) + (min(max(snr_db, 0), 24) / 120.0), 0.05, 0.99))
            candidates.append((offset_hz, snr_db, confidence, persistence, width_hz, 0.0, 0.0, "", 0.0, "", 0.0, "", False))
            used_offsets.append(offset_hz)
            if len(candidates) >= MAX_CANDIDATES_PER_CYCLE:
                break

        return candidates

    def _estimate_mode_fit(self, frame_matrix: np.ndarray, center_idx: int, bin_hz: float) -> float:
        if frame_matrix.size == 0 or bin_hz <= 0:
            return 0.0

        spacing_bins = max(1, int(round(FT_TONE_SPACING_HZ / bin_hz)))
        # FT8/FT4 occupy 8 tones; score how often energy appears on a local
        # 6.25 Hz-like comb around the strongest candidate.
        support = 0.0
        total = 0.0
        for tone_index in range(-3, 5):
            idx = center_idx + (tone_index * spacing_bins)
            if idx < 0 or idx >= frame_matrix.shape[1]:
                continue
            total += 1.0
            activity = float(np.mean(frame_matrix[:, idx]))
            support += activity

        if total <= 0.0:
            return 0.0

        comb_score = support / total

        # Reward moderate continuity; lightning tends to be wide and brief.
        center_activity = frame_matrix[:, center_idx]
        run_lengths: list[int] = []
        run = 0
        for value in center_activity:
            if value > 0.0:
                run += 1
            elif run > 0:
                run_lengths.append(run)
                run = 0
        if run > 0:
            run_lengths.append(run)

        continuity = 0.0 if not run_lengths else float(np.mean(run_lengths)) / max(1.0, frame_matrix.shape[0] * 0.25)
        continuity = float(np.clip(continuity, 0.0, 1.0))
        return float(np.clip((comb_score * 0.7) + (continuity * 0.3), 0.0, 1.0))

    def emit_telemetry(self) -> None:
        emit({
            "type": "telemetry",
            "isRunning": self.is_running,
            "status": self.status,
            "activeWorker": "Python WSJT sidecar",
            "modeLabel": self.mode_label,
            "signalLevelPercent": self.signal_level_percent,
            "decodeCount": self.decode_count,
            "autoSequenceEnabled": self.auto_sequence_enabled,
            "isTransmitArmed": self.call_cq_enabled,
            "stationCallsign": self.station_callsign,
            "stationGridSquare": self.station_grid_square,
            "transmitFirstEnabled": self.transmit_first_enabled,
            "cycleLengthSeconds": self.cycle_length,
            "requiresAccurateClock": self.requires_accurate_clock,
        })


def main() -> int:
    worker = WsjtxWorker()
    worker.emit_telemetry()

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            payload = json.loads(line)
        except Exception:
            continue

        msg_type = payload.get("type")
        if msg_type == "configure":
            worker.configure(payload)
        elif msg_type == "start":
            worker.start()
        elif msg_type == "stop":
            worker.stop()
        elif msg_type == "reset":
            worker.reset()
        elif msg_type == "audio" and worker.is_running:
            worker.handle_audio(payload)
        elif msg_type == "shutdown":
            worker.stop()
            break

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
