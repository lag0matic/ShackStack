from __future__ import annotations

import argparse
import csv
import json
import re
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np

from cw_skimmer_file_harness import load_capture_session_audio, run_skimmer, summarize_skimmer_payloads


DEFAULT_ROOT = Path.home() / "ShackStack" / "cw-skimmer-captures"
TOKEN_RE = re.compile(r"[A-Z0-9/]{2,}")


@dataclass(frozen=True)
class BenchmarkCase:
    name: str
    session: str
    label: str
    start_utc: str
    end_utc: str
    low_hz: int
    high_hz: int
    expected_tokens: tuple[str, ...]
    expected_aliases: tuple[tuple[str, ...], ...] = ()
    avoid_tokens: tuple[str, ...] = ("EEEE", "TTTT", "IIII")
    max_lanes: int = 8


DEFAULT_CASES: tuple[BenchmarkCase, ...] = (
    BenchmarkCase(
        name="w1aw_20260604_20wpm",
        session="session-20260604T232934Z",
        label="W1AW 20 WPM rough RF",
        start_utc="2026-06-04T23:29:42Z",
        end_utc="2026-06-04T23:35:32Z",
        low_hz=1400,
        high_hz=1700,
        expected_tokens=("INTERPRETED", "LANGUAGE", "PYTHON", "MODULAR", "SYSTEM"),
        expected_aliases=(
            ("FLEXIBILITY", "FLXIBILITY", "FLXIBIL"),
            ("DESIGN", "EESIGN", "EESIGNLA"),
            ("ENDOF20WPMTEXT", "ENDOF20WPM", "20WPMTEXT"),
            ("QSTDEW1AW", "QSTDEM", "QSTDE"),
        ),
    ),
    BenchmarkCase(
        name="w1aw_20260604_15wpm",
        session="session-20260604T232934Z",
        label="W1AW 15 WPM rough RF",
        start_utc="2026-06-04T23:35:50Z",
        end_utc="2026-06-04T23:46:00Z",
        low_hz=1400,
        high_hz=1700,
        expected_tokens=("15WPM", "ANDROID", "RADIO", "COMMUNICATION", "DEVICE", "AMATEUR"),
        expected_aliases=(
            ("COMMUNICATION", "COMMUNICATIXN", "COMMUNICAT"),
            ("AMATEUR", "AMTTEUR"),
            ("QSTDEW1AW", "QSTDEW1AW", "QSTDE"),
            ("13WPM", "NOW13WPM", "VNOW13WPM"),
        ),
    ),
    BenchmarkCase(
        name="w1aw_20260604_13wpm",
        session="session-20260604T232934Z",
        label="W1AW 13 WPM rough RF",
        start_utc="2026-06-04T23:50:45Z",
        end_utc="2026-06-04T23:56:20Z",
        low_hz=1400,
        high_hz=1700,
        expected_tokens=("B1", "RADIO", "QST", "ANDROID", "SUPPORT", "VERSION"),
        expected_aliases=(
            ("VERSION", "NEVERSION", "VERSIONOF"),
            ("SOFTWARE", "IOFTWARE", "OFTWARE"),
            ("SUPPORT", "IUPPORT", "PPORT"),
            ("FEATURES", "FEAARES", "FEATURE"),
        ),
    ),
    BenchmarkCase(
        name="w1aw_20260604_10wpm",
        session="session-20260604T232934Z",
        label="W1AW 10 WPM rough RF",
        start_utc="2026-06-04T23:58:45Z",
        end_utc="2026-06-04T23:59:30Z",
        low_hz=1400,
        high_hz=1700,
        expected_tokens=("10WPM", "QST", "DEW1AW", "W1AW"),
        expected_aliases=(
            ("10WPM", "0WPM", "WF0WPM", "10WPMTEXT"),
            ("QSTDEW1AW", "STDEW1AW", "DEW1AW"),
        ),
    ),
    BenchmarkCase(
        name="live_qso_20260605",
        session="session-20260605T001412Z",
        label="Live QSO rough RF",
        start_utc="2026-06-05T00:14:20Z",
        end_utc="2026-06-05T00:16:08Z",
        low_hz=900,
        high_hz=1300,
        expected_tokens=("579", "QSB", "NC", "BOB", "WX", "VERTICAL", "TODAY"),
        expected_aliases=(
            ("QSB", "579QSB"),
            ("VERTICAL", "TO VERTICAL", "TOVERTICAL"),
            ("TODAY", "FBTODAY"),
        ),
    ),
)


def parse_utc(value: str) -> datetime:
    text = value.strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    return datetime.fromisoformat(text).astimezone(timezone.utc)


def load_manifest(session: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    manifest = session / "manifest.jsonl"
    if not manifest.exists():
        return rows
    with manifest.open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            payload = json.loads(line)
            if payload.get("type") == "cw-skimmer-sample":
                rows.append(payload)
    return rows


def sample_time(row: dict[str, Any]) -> datetime:
    return parse_utc(str(row.get("capturedUtc", "")))


def select_rows(rows: list[dict[str, Any]], case: BenchmarkCase) -> list[dict[str, Any]]:
    start = parse_utc(case.start_utc)
    end = parse_utc(case.end_utc)
    return [row for row in rows if start <= sample_time(row) <= end]


def session_channels(rows: list[dict[str, Any]]) -> int:
    for row in rows:
        channels = int(row.get("sessionChannels") or row.get("channels") or 1)
        if channels > 0:
            return channels
    return 1


def slice_for_rows(audio: np.ndarray, rows: list[dict[str, Any]], channels: int, pad_seconds: float, sample_rate: int) -> np.ndarray:
    if not rows:
        return np.empty(0, dtype=np.float32)
    starts = [int(row.get("rollingStartSample") or 0) for row in rows]
    ends = [int(row.get("rollingEndSample") or 0) for row in rows]
    if not starts or not ends or max(ends) <= 0:
        return np.empty(0, dtype=np.float32)
    pad_frames = int(round(pad_seconds * sample_rate))
    start_frame = max(0, min(starts) // max(1, channels) - pad_frames)
    end_frame = min(audio.size, max(ends) // max(1, channels) + pad_frames)
    return audio[start_frame:end_frame].astype(np.float32, copy=False)


def compact(value: str) -> str:
    return re.sub(r"[^A-Z0-9/]+", "", value.upper())


def extract_tokens(text: str) -> list[str]:
    return [token for token in TOKEN_RE.findall(text.upper()) if token]


def edit_distance(left: str, right: str, limit: int = 3) -> int:
    if abs(len(left) - len(right)) > limit:
        return limit + 1
    previous = list(range(len(right) + 1))
    for i, left_ch in enumerate(left, start=1):
        current = [i] + [0] * len(right)
        row_min = current[0]
        for j, right_ch in enumerate(right, start=1):
            current[j] = min(
                previous[j] + 1,
                current[j - 1] + 1,
                previous[j - 1] + (0 if left_ch == right_ch else 1),
            )
            row_min = min(row_min, current[j])
        if row_min > limit:
            return limit + 1
        previous = current
    return previous[-1]


def longest_common_substring_len(left: str, right: str) -> int:
    if not left or not right:
        return 0
    previous = [0] * (len(right) + 1)
    best = 0
    for left_ch in left:
        current = [0] * (len(right) + 1)
        for index, right_ch in enumerate(right, start=1):
            if left_ch == right_ch:
                current[index] = previous[index - 1] + 1
                best = max(best, current[index])
        previous = current
    return best


def fuzzy_token_match(token: str, transcript: str, aliases: tuple[str, ...]) -> tuple[float, str]:
    token_compact = compact(token)
    transcript_compact = compact(transcript)
    transcript_tokens = [compact(item) for item in extract_tokens(transcript)]
    if not token_compact:
        return 0.0, ""
    if token_compact in transcript_compact:
        return 1.0, f"{token}:exact"

    for alias in aliases:
        alias_compact = compact(alias)
        if alias_compact and alias_compact in transcript_compact:
            return 0.9, f"{token}:alias:{alias}"

    if len(token_compact) >= 4:
        best_distance = min(
            (edit_distance(token_compact, candidate, limit=2) for candidate in transcript_tokens if abs(len(candidate) - len(token_compact)) <= 2),
            default=99,
        )
        if best_distance <= 1:
            return 0.75, f"{token}:near1"
        if best_distance == 2 and len(token_compact) >= 6:
            return 0.55, f"{token}:near2"

    overlap = longest_common_substring_len(token_compact, transcript_compact)
    if len(token_compact) >= 5 and overlap >= max(4, int(len(token_compact) * 0.72)):
        return 0.45, f"{token}:overlap:{overlap}"
    return 0.0, ""


def alias_map(groups: tuple[tuple[str, ...], ...]) -> dict[str, tuple[str, ...]]:
    output: dict[str, tuple[str, ...]] = {}
    for group in groups:
        if not group:
            continue
        canonical = group[0]
        aliases = group[1:] if len(group) > 1 else ()
        output[canonical] = aliases
    return output


def token_hits(text: str, tokens: tuple[str, ...], aliases: tuple[tuple[str, ...], ...]) -> tuple[float, list[str]]:
    compact_text = compact(text)
    alias_lookup = alias_map(aliases)
    token_order = list(tokens)
    for canonical in alias_lookup:
        if canonical not in token_order:
            token_order.append(canonical)

    score = 0.0
    hits: list[str] = []
    for token in token_order:
        token_aliases = alias_lookup.get(token, ())
        value, reason = fuzzy_token_match(token, text, token_aliases)
        if value > 0.0:
            score += value
            hits.append(reason)
    return score, hits


def repeated_noise_score(text: str) -> int:
    compact_text = compact(text)
    score = 0
    for pattern in ("EEEE", "TTTT", "IIII", "NNNN", "AAAA"):
        score += compact_text.count(pattern)
    return score


def best_lane(summary: dict[str, Any]) -> dict[str, Any] | None:
    lanes = list(summary.get("laneSummaries", []))
    if not lanes:
        return None
    return max(
        lanes,
        key=lambda lane: (
            len(str(lane.get("transcript", ""))),
            float(lane.get("confidence", 0.0)),
            int(lane.get("candidateCount", 0)),
        ),
    )


def score_case(case: BenchmarkCase, root: Path, chunk_size: int, pad_seconds: float) -> dict[str, Any]:
    session = root / case.session
    rows = load_manifest(session)
    selected = select_rows(rows, case)
    audio, sample_rate = load_capture_session_audio(session)
    channels = session_channels(rows)
    segment_audio = slice_for_rows(audio, selected, channels, pad_seconds, sample_rate)
    if segment_audio.size == 0:
        return {
            "case": case.name,
            "label": case.label,
            "session": case.session,
            "status": "no-audio",
            "samples": len(selected),
        }

    payloads = run_skimmer(
        segment_audio,
        sample_rate=sample_rate,
        low=case.low_hz,
        high=case.high_hz,
        lanes=case.max_lanes,
        chunk_size=chunk_size,
    )
    summary = summarize_skimmer_payloads(payloads)
    lane = best_lane(summary)
    transcript = str(lane.get("transcript", "")) if lane else ""
    hit_score, hits = token_hits(transcript, case.expected_tokens, case.expected_aliases)
    noise = repeated_noise_score(transcript)
    score = int(round(hit_score * 10)) - (noise * 3)
    if lane:
        score += min(10, int(float(lane.get("confidence", 0.0)) * 10))
        score += min(10, len(transcript) // 25)

    return {
        "case": case.name,
        "label": case.label,
        "session": case.session,
        "status": "ok",
        "samples": len(selected),
        "durationSeconds": round(segment_audio.size / max(1, sample_rate), 1),
        "lowHz": case.low_hz,
        "highHz": case.high_hz,
        "laneId": int(lane.get("laneId", 0)) if lane else 0,
        "avgToneHz": round(float(lane.get("avgToneHz", 0.0)), 1) if lane else 0.0,
        "confidence": round(float(lane.get("confidence", 0.0)), 3) if lane else 0.0,
        "copyRate": round(float(lane.get("copyRate", 0.0)), 3) if lane else 0.0,
        "candidateCount": int(lane.get("candidateCount", 0)) if lane else 0,
        "expectedTokens": " ".join(case.expected_tokens),
        "hits": " ".join(hits),
        "hitCount": len(hits),
        "hitScore": round(hit_score, 2),
        "noiseScore": noise,
        "score": score,
        "transcript": transcript[:260],
        "textEvents": sum(summary["textCounter"].values()),
        "timingEvents": " ".join(f"{key}:{value}" for key, value in summary["timingCounter"].most_common(6)),
    }


def selected_cases(names: list[str]) -> list[BenchmarkCase]:
    if not names:
        return list(DEFAULT_CASES)
    wanted = set(names)
    return [case for case in DEFAULT_CASES if case.name in wanted or case.session in wanted]


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    if not rows:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    fields = list(rows[0].keys())
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def main() -> int:
    parser = argparse.ArgumentParser(description="Run ShackStack CW benchmark slices against saved capture sessions.")
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    parser.add_argument("--case", action="append", default=[], help="Case name or session name. Repeatable.")
    parser.add_argument("--chunk", type=int, default=4096)
    parser.add_argument("--pad-seconds", type=float, default=1.0)
    parser.add_argument("--csv-out", type=Path)
    parser.add_argument("--json-out", type=Path)
    parser.add_argument("--list", action="store_true")
    args = parser.parse_args()

    cases = selected_cases(args.case)
    if args.list:
        for case in DEFAULT_CASES:
            print(f"{case.name}: {case.label} [{case.session}] {case.start_utc}..{case.end_utc}")
        return 0

    results = [score_case(case, args.root, args.chunk, args.pad_seconds) for case in cases]
    for result in results:
        print()
        print(
            f"{result['case']} status={result['status']} score={result.get('score', 0)} "
            f"hits={result.get('hitScore', 0)} samples={result.get('samples', 0)} "
            f"duration={result.get('durationSeconds', 0)}s"
        )
        if result["status"] == "ok":
            print(
                f"  lane {result['laneId']} {result['avgToneHz']} Hz "
                f"conf={result['confidence']} cps={result['copyRate']} candidates={result['candidateCount']}"
            )
            print(f"  hits: {result['hits'] or '-'}")
            print(f"  timing: {result['timingEvents']}")
            print(f"  copy: {result['transcript']}")

    if args.csv_out:
        write_csv(args.csv_out, results)
        print(f"\nwrote {args.csv_out}")
    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(json.dumps(results, indent=2), encoding="utf-8")
        print(f"wrote {args.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
