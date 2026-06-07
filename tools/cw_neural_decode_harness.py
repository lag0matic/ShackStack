from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

import numpy as np
import torch

TOOLS_DIR = Path(__file__).resolve().parent
PY_WORKERS = TOOLS_DIR.parent / "src" / "ShackStack.DecoderWorkers.Python"
if str(PY_WORKERS) not in sys.path:
    sys.path.insert(0, str(PY_WORKERS))
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

from cw_eval_duration_model import load_model, prepare_real_features  # noqa: E402
from modes.cw_engine import MORSE_DECODE, MORSE_PREFIXES  # noqa: E402


LABELS = ["gap", "dit", "dah", "bad_lane"]
TOKEN_RE = re.compile(r"[A-Z0-9/]{2,}")
CONTEST_TOKENS = {
    "CQ",
    "TEST",
    "TU",
    "DE",
    "K",
    "KN",
    "73",
    "5NN",
    "599",
    "SST",
    "RST",
    "NR",
}


def load_manifest_rows(session: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    with (session / "manifest.jsonl").open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            payload = json.loads(line)
            if payload.get("type") == "cw-skimmer-sample":
                rows.append(payload)
    return rows


def load_audio(session: Path, row: dict[str, Any]) -> np.ndarray:
    audio_path = session / str(row["audioFile"]).replace("/", "\\")
    audio = np.fromfile(audio_path, dtype="<f4")
    channels = int(row.get("channels") or row.get("sessionChannels") or 1)
    if channels > 1 and audio.size >= channels:
        audio = audio.reshape((-1, channels)).mean(axis=1)
    return np.nan_to_num(audio.astype(np.float32, copy=False))


def smooth_probs(probs: np.ndarray, width: int = 5) -> np.ndarray:
    if width <= 1:
        return probs
    kernel = np.ones(width, dtype=np.float32) / float(width)
    return np.vstack([np.convolve(probs[index], kernel, mode="same") for index in range(probs.shape[0])])


def collapse_runs(labels: np.ndarray) -> list[tuple[int, int, int]]:
    if labels.size == 0:
        return []
    runs: list[tuple[int, int, int]] = []
    start = 0
    current = int(labels[0])
    for index, value in enumerate(labels[1:], start=1):
        value = int(value)
        if value != current:
            runs.append((current, start, index))
            start = index
            current = value
    runs.append((current, start, labels.size))
    return runs


def merge_short_runs(runs: list[tuple[int, int, int]], min_frames: int) -> list[tuple[int, int, int]]:
    if not runs or min_frames <= 1:
        return runs
    labels = []
    for label, start, end in runs:
        labels.extend([label] * (end - start))
    arr = np.asarray(labels, dtype=np.int64)
    changed = True
    while changed:
        changed = False
        current_runs = collapse_runs(arr)
        for index, (label, start, end) in enumerate(current_runs):
            if end - start >= min_frames or label == 3:
                continue
            left_label = current_runs[index - 1][0] if index > 0 else None
            right_label = current_runs[index + 1][0] if index + 1 < len(current_runs) else None
            replacement = right_label if left_label is None else left_label if right_label is None else left_label
            if replacement is not None:
                arr[start:end] = replacement
                changed = True
                break
    return collapse_runs(arr)


def decode_runs(runs: list[tuple[int, int, int]], frame_hz: int) -> tuple[str, list[str]]:
    text_parts: list[str] = []
    debug: list[str] = []
    pattern = ""
    element_frames: list[int] = []
    gap_frames: list[int] = []

    for label, start, end in runs:
        duration = end - start
        if label in {1, 2}:
            mark = "." if label == 1 else "-"
            pattern += mark
            element_frames.append(duration)
            debug.append(f"{mark}{duration}")
            if len(pattern) > 8:
                text_parts.append(MORSE_DECODE.get(pattern, "?"))
                pattern = ""
            continue

        if label == 0:
            gap_frames.append(duration)
            if not pattern:
                continue
            dit = max(3.0, float(np.median(element_frames[-12:])) if element_frames else frame_hz * 0.06)
            char_gap = max(dit * 1.65, 5.0)
            word_gap = max(dit * 4.0, char_gap * 2.0)
            if duration >= char_gap:
                decoded = MORSE_DECODE.get(pattern, "?")
                text_parts.append(decoded)
                debug.append(f"/{duration}->{decoded}({pattern})")
                pattern = ""
                if duration >= word_gap:
                    text_parts.append(" ")
            continue

        if label == 3:
            if pattern:
                decoded = MORSE_DECODE.get(pattern, "?")
                text_parts.append(decoded)
                debug.append(f"!{duration}->{decoded}({pattern})")
                pattern = ""

    if pattern:
        decoded = MORSE_DECODE.get(pattern, "?")
        text_parts.append(decoded)
        debug.append(f"end->{decoded}({pattern})")

    text = "".join(text_parts)
    text = " ".join(text.split())
    return text, debug


def run_class_score(probs: np.ndarray, label: int, start: int, end: int) -> float:
    segment = probs[label, start:end]
    if segment.size == 0:
        return -20.0
    return float(np.sum(np.log(np.clip(segment, 1e-5, 1.0))))


def beam_decode_runs(
    runs: list[tuple[int, int, int]],
    probs: np.ndarray,
    frame_hz: int,
    beam_width: int = 18,
    hint: str = "",
    hint_weight: float = 0.8,
) -> tuple[str, list[str]]:
    states: list[tuple[float, str, str, list[str]]] = [(0.0, "", "", [])]
    element_durations = [end - start for label, start, end in runs if label in {1, 2}]
    dit_frames = max(3.0, float(np.percentile(element_durations, 35.0)) if element_durations else frame_hz * 0.06)
    char_gap_frames = max(dit_frames * 1.6, 8.0)
    word_gap_frames = max(dit_frames * 4.0, char_gap_frames * 2.0)

    def push_state(next_states: list[tuple[float, str, str, list[str]]], state: tuple[float, str, str, list[str]]) -> None:
        score, text, pattern, debug = state
        if len(pattern) > 8:
            score -= 6.0 + len(pattern)
        next_states.append((score, text, pattern, debug))

    for label, start, end in runs:
        duration = end - start
        next_states: list[tuple[float, str, str, list[str]]] = []
        if label in {1, 2}:
            primary = "." if label == 1 else "-"
            alternate = "-" if label == 1 else "."
            primary_score = run_class_score(probs, label, start, end)
            alt_label = 2 if label == 1 else 1
            alternate_score = run_class_score(probs, alt_label, start, end) - 2.4
            skip_score = run_class_score(probs, 0, start, end) - 3.2
            for score, text, pattern, debug in states:
                push_state(next_states, (score + primary_score, text, pattern + primary, debug + [f"{primary}{duration}"]))
                if abs(primary_score - alternate_score) < 8.0:
                    push_state(next_states, (score + alternate_score, text, pattern + alternate, debug + [f"{alternate}{duration}~"]))
                if duration <= max(3, frame_hz // 20):
                    push_state(next_states, (score + skip_score, text, pattern, debug + [f"skip{duration}"]))
        elif label == 0:
            gap_score = run_class_score(probs, 0, start, end)
            for score, text, pattern, debug in states:
                if not pattern:
                    push_state(next_states, (score + gap_score * 0.25, text, "", debug))
                    continue

                # Keep the gap inside a character for very short pauses.
                if duration < char_gap_frames:
                    push_state(next_states, (score + gap_score * 0.25 + 0.4, text, pattern, debug + [f"g{duration}"]))
                    if pattern not in MORSE_PREFIXES and pattern not in MORSE_DECODE:
                        push_state(next_states, (score + gap_score * 0.2 - 2.5, text + "?", "", debug + [f"/{duration}->?({pattern})"]))
                    continue

                decoded = MORSE_DECODE.get(pattern)
                if decoded is not None:
                    next_text = text + decoded
                    hint_bonus = hint_weight * hint_increment_score(text, next_text, hint)
                    char_score = score + gap_score * 0.35 + 2.2 + min(1.6, len(pattern) * 0.22) + hint_bonus
                    push_state(next_states, (char_score, text + decoded, "", debug + [f"/{duration}->{decoded}({pattern})"]))
                    if duration >= word_gap_frames:
                        push_state(next_states, (char_score - 0.7, text + decoded + " ", "", debug + [f"//{duration}->{decoded}({pattern})"]))
                elif pattern in MORSE_PREFIXES:
                    push_state(next_states, (score + gap_score * 0.1 - 1.2, text, pattern, debug + [f"hold{duration}({pattern})"]))
                else:
                    push_state(next_states, (score + gap_score * 0.25 - 3.5, text + "?", "", debug + [f"/{duration}->?({pattern})"]))
        else:
            bad_score = run_class_score(probs, 3, start, end)
            for score, text, pattern, debug in states:
                if pattern and pattern in MORSE_DECODE:
                    decoded = MORSE_DECODE[pattern]
                    push_state(next_states, (score + bad_score * 0.15 - 0.5, text + decoded, "", debug + [f"!{duration}->{decoded}({pattern})"]))
                else:
                    push_state(next_states, (score + bad_score * 0.1 - 0.8, text, "", debug + [f"!{duration}"]))

        states = sorted(next_states, key=lambda item: item[0], reverse=True)[:beam_width]

    final_states: list[tuple[float, str, str, list[str]]] = []
    for score, text, pattern, debug in states:
        if not pattern:
            final_states.append((score, text, pattern, debug))
        elif pattern in MORSE_DECODE:
            decoded = MORSE_DECODE[pattern]
            final_states.append((score + 1.3, text + decoded, "", debug + [f"end->{decoded}({pattern})"]))
        else:
            final_states.append((score - 3.0, text + "?", "", debug + [f"end->?({pattern})"]))

    best = max(
        final_states,
        key=lambda item: item[0] + (hint_weight * hint_alignment_score(item[1], hint)),
    ) if final_states else (0.0, "", "", [])
    text = " ".join(best[1].split())
    debug = best[3] + [f"hint={hint_alignment_score(best[1], hint):0.2f}"]
    return text, debug


def compact_text(value: str) -> str:
    return re.sub(r"[^A-Z0-9/]+", "", value.upper())


def hint_alignment_score(candidate: str, hint: str) -> float:
    candidate_compact = compact_text(candidate)
    hint_compact = compact_text(hint)
    if not candidate_compact or not hint_compact:
        return 0.0

    score = 0.0
    for token in TOKEN_RE.findall(hint.upper()):
        if len(token) < 3:
            continue
        if token in candidate_compact:
            score += min(8.0, len(token) * 0.75)

    score += longest_common_substring_len(candidate_compact, hint_compact) * 0.18
    return score


def hint_increment_score(before: str, after: str, hint: str) -> float:
    if not hint:
        return 0.0
    before_score = hint_alignment_score(before, hint)
    after_score = hint_alignment_score(after, hint)
    return max(0.0, min(2.5, after_score - before_score))


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


def edit_distance(left: str, right: str, limit: int = 4) -> int:
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


def looks_callish(token: str) -> bool:
    return (
        len(token) >= 3
        and any(ch.isdigit() for ch in token)
        and any(ch.isalpha() for ch in token)
        and not token.isdigit()
    )


def extract_tokens(text: str) -> list[str]:
    tokens = []
    for token in TOKEN_RE.findall(text.upper()):
        token = token.strip()
        if len(token) < 2:
            continue
        tokens.append(token)
    return tokens


def support_score(token: str, other_tokens: list[str], other_compact: str) -> tuple[float, str]:
    if not token:
        return 0.0, "none"
    if token in other_tokens or token in other_compact:
        return min(5.0, len(token) * 0.8), "exact"
    best_distance = 99
    best_token = ""
    for other in other_tokens:
        if abs(len(token) - len(other)) > 2:
            continue
        distance = edit_distance(token, other, limit=2)
        if distance < best_distance:
            best_distance = distance
            best_token = other
    if best_distance <= 1 and len(token) >= 4:
        return 2.8, f"near:{best_token}"
    if best_distance == 2 and len(token) >= 5:
        return 1.4, f"near:{best_token}"
    overlap = longest_common_substring_len(token, other_compact)
    if overlap >= max(3, len(token) - 1):
        return 1.2 + overlap * 0.25, f"overlap:{overlap}"
    return 0.0, "none"


def token_prior(token: str) -> float:
    if token in CONTEST_TOKENS:
        return 2.0
    if looks_callish(token):
        return 2.4 + min(2.0, len(token) * 0.18)
    if token.isdigit() and 2 <= len(token) <= 4:
        return 0.9
    if len(token) >= 3:
        return 0.35
    return 0.0


def token_rerank(first_pass: str, neural_text: str, greedy_text: str) -> tuple[str, list[dict[str, object]]]:
    first_tokens = extract_tokens(first_pass)
    neural_tokens = extract_tokens(neural_text)
    greedy_tokens = extract_tokens(greedy_text)
    first_compact = compact_text(first_pass)
    neural_compact = compact_text(neural_text + " " + greedy_text)

    candidates: dict[str, dict[str, object]] = {}

    def add_candidate(token: str, source: str, base: float) -> None:
        if len(token) < 2:
            return
        candidate = candidates.setdefault(
            token,
            {"token": token, "score": 0.0, "sources": set(), "reasons": []},
        )
        candidate["score"] = float(candidate["score"]) + base
        candidate["sources"].add(source)

    for token in first_tokens:
        add_candidate(token, "first", 0.4)
    for token in neural_tokens:
        add_candidate(token, "beam", 0.8)
    for token in greedy_tokens:
        add_candidate(token, "greedy", 0.5)

    for token, candidate in list(candidates.items()):
        prior = token_prior(token)
        candidate["score"] = float(candidate["score"]) + prior
        if prior:
            candidate["reasons"].append(f"prior:{prior:0.1f}")

        if "first" in candidate["sources"]:
            support, reason = support_score(token, neural_tokens + greedy_tokens, neural_compact)
            candidate["score"] = float(candidate["score"]) + support
            if support:
                candidate["reasons"].append(f"neural:{reason}")
        else:
            support, reason = support_score(token, first_tokens, first_compact)
            candidate["score"] = float(candidate["score"]) + support
            if support:
                candidate["reasons"].append(f"first:{reason}")

        if looks_callish(token) and support == 0.0 and "first" not in candidate["sources"]:
            candidate["score"] = float(candidate["score"]) - 1.2
            candidate["reasons"].append("unsupported-call")

    ranked = sorted(candidates.values(), key=lambda item: float(item["score"]), reverse=True)
    filtered = [
        {
            "token": item["token"],
            "score": round(float(item["score"]), 2),
            "sources": sorted(item["sources"]),
            "reasons": item["reasons"],
        }
        for item in ranked
        if float(item["score"]) >= 2.0
    ][:16]

    # Preserve first-pass order for supported/high-value tokens; append strong neural-only tokens.
    output_tokens: list[str] = []
    accepted = {str(item["token"]) for item in filtered}
    for token in first_tokens:
        if token in accepted and token not in output_tokens:
            output_tokens.append(token)
    for item in filtered:
        token = str(item["token"])
        if token not in output_tokens:
            output_tokens.append(token)

    return " ".join(output_tokens[:14]), filtered


def score_row(
    model: Any,
    checkpoint: dict[str, Any],
    device: torch.device,
    session: Path,
    row: dict[str, Any],
    hint_weight: float,
    beam_width: int,
) -> dict[str, Any]:
    audio = load_audio(session, row)
    sample_rate = int(row.get("sampleRate") or row.get("sessionSampleRate") or 48000)
    tone_hz = float(row.get("audioToneHz") or 0.0)
    with torch.no_grad():
        x = prepare_real_features(audio, sample_rate, checkpoint["config"], tone_hz).to(device)
        probs = torch.softmax(model(x), dim=1)[0].detach().cpu().numpy()
    probs = smooth_probs(probs, width=5)
    labels = np.argmax(probs, axis=0)
    runs = merge_short_runs(collapse_runs(labels), min_frames=3)
    greedy_text, greedy_debug = decode_runs(runs, int(checkpoint["config"]["frameHz"]))
    hint = str(row.get("laneTranscript") or row.get("firstPassText") or "")
    text, debug = beam_decode_runs(
        runs,
        probs,
        int(checkpoint["config"]["frameHz"]),
        beam_width=beam_width,
        hint=hint,
        hint_weight=hint_weight,
    )
    token_text, token_candidates = token_rerank(hint, text, greedy_text)
    fractions = probs.mean(axis=1)
    return {
        "id": row.get("id"),
        "laneId": row.get("laneId"),
        "audioToneHz": tone_hz,
        "confidence": row.get("confidence"),
        "firstPassText": row.get("firstPassText", ""),
        "laneTranscript": row.get("laneTranscript", ""),
        "neuralText": text,
        "greedyText": greedy_text,
        "tokenText": token_text,
        "tokenCandidates": token_candidates,
        "fractions": {LABELS[index]: float(fractions[index]) for index in range(len(LABELS))},
        "debug": " ".join(debug[:80]),
        "greedyDebug": " ".join(greedy_debug[:80]),
    }


def select_rows(rows: list[dict[str, Any]], lane_id: int | None, contains: str | None, limit: int) -> list[dict[str, Any]]:
    selected = []
    needle = contains.upper() if contains else None
    for row in rows:
        if lane_id is not None and int(row.get("laneId") or 0) != lane_id:
            continue
        if needle and needle not in str(row.get("laneTranscript") or "").upper():
            continue
        if int(row.get("laneId") or 0) <= 0:
            continue
        selected.append(row)
        if limit > 0 and len(selected) >= limit:
            break
    return selected


def main() -> int:
    parser = argparse.ArgumentParser(description="Decode captured CW lanes with a neural duration model.")
    parser.add_argument("model", type=Path)
    parser.add_argument("session", type=Path)
    parser.add_argument("--lane-id", type=int)
    parser.add_argument("--contains")
    parser.add_argument("--limit", type=int, default=12)
    parser.add_argument("--hint-weight", type=float, default=0.8)
    parser.add_argument("--beam-width", type=int, default=32)
    parser.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    parser.add_argument("--json-out", type=Path)
    args = parser.parse_args()

    device = torch.device(args.device)
    model, checkpoint = load_model(args.model, device)
    rows = select_rows(load_manifest_rows(args.session), args.lane_id, args.contains, args.limit)
    results = [score_row(model, checkpoint, device, args.session, row, args.hint_weight, args.beam_width) for row in rows]

    print(f"model: {args.model}")
    print(f"session: {args.session.name}")
    print(f"rows: {len(results)}")
    print(f"hint_weight: {args.hint_weight:0.2f}")
    print(f"beam_width: {args.beam_width}")
    for result in results:
        fractions = result["fractions"]
        print()
        print(
            f"{result['id']} lane {result['laneId']} {result['audioToneHz']:0.0f} Hz "
            f"conf={float(result.get('confidence') or 0):0.2f} "
            f"bad={fractions['bad_lane']:0.3f} dit={fractions['dit']:0.3f} dah={fractions['dah']:0.3f}"
        )
        print(f"  first:  {str(result.get('laneTranscript') or '')[:180]}")
        print(f"  neural: {result['neuralText'][:180]}")
        print(f"  tokens: {result['tokenText'][:180]}")
        if result["tokenCandidates"]:
            print(
                "  token candidates: "
                + ", ".join(
                    f"{item['token']}:{item['score']}"
                    for item in result["tokenCandidates"][:8]
                )
            )
        print(f"  greedy: {result['greedyText'][:180]}")
        print(f"  trace:  {result['debug'][:220]}")

    if args.json_out:
        args.json_out.write_text(json.dumps(results, indent=2), encoding="utf-8")
        print(f"wrote {args.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
