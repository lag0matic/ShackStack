from __future__ import annotations

import argparse
import sys
import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
PY_WORKERS = ROOT / "src" / "ShackStack.DecoderWorkers.Python"
sys.path.insert(0, str(PY_WORKERS))

from modes.wefax_engine import WefaxEncoder  # noqa: E402
from modes.wefax_engine_proto import WefaxDecoderPrototype  # noqa: E402

SAMPLE_RATE = 48_000
IOC = 576
LPM = 120


@dataclass
class SmokeResult:
    name: str
    lines: int
    images: int
    best_correlation: float
    best_mae: float
    crop_y: int
    shift: int
    output_path: Path | None


@dataclass
class GeometryResult:
    name: str
    lines: int
    images: int
    drift_pixels: int
    output_path: Path | None


@dataclass
class MapResult:
    name: str
    lines: int
    images: int
    best_correlation: float
    best_mae: float
    crop_y: int
    shift: int
    source_path: Path
    output_path: Path | None


def build_expected_card(width: int, height: int = 200) -> np.ndarray:
    img_data = np.zeros((height, width), dtype=np.uint8)
    for y in range(height):
        img_data[y] = int(y / height * 180)
    for y in range(0, height, 20):
        img_data[max(0, y - 1) : y + 2, :] = 20
    cy, cx = height // 2, width // 2
    for y in range(height):
        for x in range(0, width, 4):
            dist = np.sqrt((y - cy) ** 2 + (x - cx) ** 2)
            if int(dist) % 30 < 2:
                img_data[y, x : x + 4] = 30
    img_data[:12, :] = 0
    img_data[:12, :20] = 255
    img_data[10:22, :] = 255
    return img_data


def build_vertical_card(width: int, height: int = 200) -> np.ndarray:
    img_data = np.full((height, width), 80, dtype=np.uint8)
    for x in range(0, width, 180):
        img_data[:, x : x + 8] = 240
    for y in range(0, height, 20):
        img_data[y : y + 2, :] = 30
    return img_data


def build_fake_map(width: int, height: int = 360) -> np.ndarray:
    rng = np.random.default_rng(20260426)
    image = Image.new("L", (width, height), 245)
    draw = ImageDraw.Draw(image)
    font = ImageFont.load_default()

    # Latitude/longitude grid.
    for x in range(42, width, 175):
        draw.line([(x, 0), (x, height - 22)], fill=88, width=2)
        draw.rectangle((x - 18, height - 22, x + 28, height - 5), outline=45, width=2)
        draw.text((x - 14, height - 20), f"{120 + (x // 175) * 10}W"[-4:], fill=25, font=font)
    for y in range(32, height - 45, 74):
        draw.line([(0, y), (width, y)], fill=105, width=2)
        draw.rectangle((8, y - 9, 52, y + 9), outline=45, width=2)
        draw.text((14, y - 7), f"{20 + y // 18}N", fill=25, font=font)

    # Fake coastlines/islands with NOAA-chart-like jitter.
    coast = []
    for i in range(0, 220):
        y = 18 + i * 1.35
        x = 36 + 18 * np.sin(i / 13.0) + 9 * np.sin(i / 4.5)
        coast.append((x, y))
    draw.line(coast, fill=25, width=3)
    for cx, cy, rx, ry in [(420, 72, 44, 18), (850, 252, 70, 28), (1230, 110, 38, 62), (1500, 270, 48, 26)]:
        points = []
        for a in np.linspace(0, 2 * np.pi, 80):
            rj = 1.0 + 0.18 * np.sin(5 * a)
            points.append((cx + rx * rj * np.cos(a), cy + ry * rj * np.sin(a)))
        draw.line(points + [points[0]], fill=32, width=2)

    # Pressure contours.
    centers = [(620, 95, 6), (985, 215, 7), (1460, 245, 5)]
    for cx, cy, count in centers:
        for n in range(count):
            rx = 56 + n * 32
            ry = 28 + n * 20
            draw.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), outline=15, width=3)
    for x0, y0, x1, y1 in [(180, 178, 770, 170), (760, 72, 1140, 150), (1040, 290, 1680, 190)]:
        for offset in range(0, 96, 24):
            points = []
            for i in range(90):
                t = i / 89.0
                x = x0 + (x1 - x0) * t
                y = y0 + (y1 - y0) * t + np.sin(t * np.pi * 3 + offset) * 18
                points.append((x, y + offset * 0.2))
            draw.line(points, fill=20, width=3)

    # Weather fronts: thick curves with triangles, dots, and semicircles.
    front_paths = [
        [(120, 290), (330, 250), (560, 255), (760, 235), (920, 260)],
        [(1030, 72), (1170, 118), (1260, 195), (1390, 235), (1580, 220)],
        [(260, 88), (470, 112), (640, 142), (805, 132)],
    ]
    for path_index, path in enumerate(front_paths):
        draw.line(path, fill=0, width=7, joint="curve")
        for i, (x, y) in enumerate(path):
            if (i + path_index) % 2 == 0:
                tri = [(x, y - 18), (x + 24, y + 12), (x - 24, y + 12)]
                draw.polygon(tri, fill=0)
            else:
                draw.ellipse((x - 18, y - 18, x + 18, y + 18), outline=0, width=5)

    # Marine labels and pressure centers.
    labels = [
        (101, 100, "HEAVY\nFREEZING\nSPRAY"),
        (520, 88, "967 L"),
        (905, 72, "48-HOUR SURFACE FORECAST\nISSUED: 1836 UTC\nVALID: 1200 UTC"),
        (1000, 170, "997"),
        (1110, 220, "HURCN\nFORCE"),
        (1390, 205, "GALE"),
        (1530, 128, "1022 H"),
        (360, 264, "1021 H"),
        (705, 240, "1004 L"),
        (1310, 286, "1032 H"),
    ]
    for x, y, text in labels:
        bbox = draw.multiline_textbbox((x, y), text, font=font, spacing=3)
        pad = 5
        draw.rectangle((bbox[0] - pad, bbox[1] - pad, bbox[2] + pad, bbox[3] + pad), fill=245, outline=0, width=2)
        draw.multiline_text((x, y), text, fill=0, font=font, spacing=3)

    # Station marks, wind barbs, and little symbols.
    for _ in range(70):
        x = int(rng.integers(65, width - 90))
        y = int(rng.integers(40, height - 55))
        if rng.random() < 0.45:
            draw.line((x - 14, y - 14, x + 14, y + 14), fill=0, width=2)
            draw.line((x - 14, y + 14, x + 14, y - 14), fill=0, width=2)
        else:
            draw.ellipse((x - 9, y - 9, x + 9, y + 9), outline=0, width=3)
            draw.line((x, y - 16, x, y + 16), fill=0, width=2)
        draw.text((x + 12, y - 9), str(int(rng.integers(0, 99))).zfill(2), fill=0, font=font)

    # Fax-like speckle and border noise.
    arr = np.asarray(image, dtype=np.int16)
    arr += rng.normal(0, 12, arr.shape).astype(np.int16)
    black_speckles = rng.random(arr.shape) < 0.012
    white_speckles = rng.random(arr.shape) < 0.006
    arr[black_speckles] = 0
    arr[white_speckles] = 255
    arr[:6, :] = rng.integers(0, 35, size=(6, width))
    arr[:, :8] = rng.integers(0, 40, size=(height, 8))
    arr[:, -8:] = rng.integers(0, 40, size=(height, 8))
    draw_arr = np.clip(arr, 0, 255).astype(np.uint8)
    return draw_arr


def score(decoded: Image.Image, expected: np.ndarray) -> tuple[float, float, int, int]:
    decoded_arr = np.asarray(decoded.convert("L"), dtype=np.float32)
    expected_arr = expected.astype(np.float32)
    height = expected_arr.shape[0]

    best: tuple[float, float, int, int] | None = None
    for y0 in range(max(1, decoded_arr.shape[0] - height + 1)):
        crop = decoded_arr[y0 : y0 + height]
        if crop.shape[0] != height:
            continue
        for shift in range(-120, 121, 4):
            rolled = np.roll(crop, shift, axis=1)
            corr = float(np.corrcoef(rolled.ravel(), expected_arr.ravel())[0, 1])
            mae = float(np.mean(np.abs(rolled - expected_arr)))
            candidate = (corr, -mae, y0, shift)
            if best is None or candidate > best:
                best = candidate

    if best is None:
        return 0.0, 255.0, 0, 0
    corr, neg_mae, y0, shift = best
    return corr, -neg_mae, y0, shift


def run_case(name: str, noise: float, output_dir: Path) -> SmokeResult:
    encoder = WefaxEncoder(sample_rate=SAMPLE_RATE, lpm=LPM, ioc=IOC)
    expected = build_expected_card(encoder._width)
    audio = encoder.encode_image(Image.fromarray(expected, mode="L"), noise=noise)

    lines: list[np.ndarray] = []
    images: list[Image.Image] = []

    decoder = WefaxDecoderPrototype(
        sample_rate=SAMPLE_RATE,
        lpm=LPM,
        ioc=IOC,
        line_callback=lambda row: lines.append(row.copy()),
        image_callback=lambda image: images.append(image.copy()),
    )
    decoder.configure_receive(max_rows=320, auto_align=True)
    decoder.start()
    chunk = int(SAMPLE_RATE * 0.1)
    for offset in range(0, len(audio), chunk):
        decoder.push_samples(audio[offset : offset + chunk])
        time.sleep(0.001)

    for _ in range(300):
        if images:
            break
        time.sleep(0.02)
    decoder.stop()

    output_path: Path | None = None
    corr = 0.0
    mae = 255.0
    crop_y = 0
    shift = 0
    if images:
        output_dir.mkdir(parents=True, exist_ok=True)
        output_path = output_dir / f"wefax_smoke_{name}.png"
        images[-1].save(output_path)
        corr, mae, crop_y, shift = score(images[-1], expected)

    return SmokeResult(name, len(lines), len(images), corr, mae, crop_y, shift, output_path)


def run_geometry_case(output_dir: Path) -> GeometryResult:
    encoder = WefaxEncoder(sample_rate=SAMPLE_RATE, lpm=LPM, ioc=IOC)
    expected = build_vertical_card(encoder._width)
    audio = encoder.encode_image(Image.fromarray(expected, mode="L"), noise=0.0)

    lines: list[np.ndarray] = []
    images: list[Image.Image] = []

    decoder = WefaxDecoderPrototype(
        sample_rate=SAMPLE_RATE,
        lpm=LPM,
        ioc=IOC,
        line_callback=lambda row: lines.append(row.copy()),
        image_callback=lambda image: images.append(image.copy()),
    )
    decoder.configure_receive(max_rows=320, auto_align=True)
    decoder.start()
    chunk = int(SAMPLE_RATE * 0.1)
    for offset in range(0, len(audio), chunk):
        decoder.push_samples(audio[offset : offset + chunk])
        time.sleep(0.001)

    for _ in range(300):
        if images:
            break
        time.sleep(0.02)
    decoder.stop()

    output_path: Path | None = None
    drift = 9999
    if images:
        output_dir.mkdir(parents=True, exist_ok=True)
        output_path = output_dir / "wefax_smoke_vertical.png"
        images[-1].save(output_path)
        arr = np.asarray(images[-1].convert("L"), dtype=np.float32)
        stripe_positions: list[int] = []
        for row in arr[70 : min(arr.shape[0], 240)]:
            if float(np.std(row)) < 20.0:
                continue
            stripe_positions.append(int(np.argmax(row)))
        if stripe_positions:
            drift = int(max(stripe_positions) - min(stripe_positions))

    return GeometryResult("vertical", len(lines), len(images), drift, output_path)


def run_map_case(output_dir: Path) -> MapResult:
    encoder = WefaxEncoder(sample_rate=SAMPLE_RATE, lpm=LPM, ioc=IOC)
    expected = build_fake_map(encoder._width)
    output_dir.mkdir(parents=True, exist_ok=True)
    source_path = output_dir / "wefax_smoke_fake_map_source.png"
    Image.fromarray(expected, mode="L").save(source_path)

    audio = encoder.encode_image(Image.fromarray(expected, mode="L"), noise=0.015)
    lines: list[np.ndarray] = []
    images: list[Image.Image] = []

    decoder = WefaxDecoderPrototype(
        sample_rate=SAMPLE_RATE,
        lpm=LPM,
        ioc=IOC,
        line_callback=lambda row: lines.append(row.copy()),
        image_callback=lambda image: images.append(image.copy()),
    )
    decoder.configure_receive(max_rows=520, auto_align=True, correlation_threshold=0.03)
    decoder.start()
    chunk = int(SAMPLE_RATE * 0.1)
    for offset in range(0, len(audio), chunk):
        decoder.push_samples(audio[offset : offset + chunk])
        time.sleep(0.001)

    for _ in range(450):
        if images:
            break
        time.sleep(0.02)
    decoder.stop()

    output_path: Path | None = None
    corr = 0.0
    mae = 255.0
    crop_y = 0
    shift = 0
    if images:
        output_path = output_dir / "wefax_smoke_fake_map_decoded.png"
        images[-1].save(output_path)
        corr, mae, crop_y, shift = score(images[-1], expected)

    return MapResult("fake_map", len(lines), len(images), corr, mae, crop_y, shift, source_path, output_path)


def main() -> int:
    parser = argparse.ArgumentParser(description="Synthetic ShackStack WeFAX receive smoke test.")
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".tmp-wefax-smoke")
    parser.add_argument("--clean-threshold", type=float, default=0.90)
    parser.add_argument("--noisy-threshold", type=float, default=0.78)
    args = parser.parse_args()

    cases = [
        ("clean", 0.0, args.clean_threshold),
        ("noisy", 0.02, args.noisy_threshold),
    ]

    failed = False
    for name, noise, threshold in cases:
        result = run_case(name, noise, args.output_dir)
        print(
            f"{result.name}: lines={result.lines} images={result.images} "
            f"corr={result.best_correlation:.3f} mae={result.best_mae:.2f} "
            f"crop_y={result.crop_y} shift={result.shift} output={result.output_path}"
        )
        if result.images < 1 or result.lines < 190 or result.best_correlation < threshold:
            failed = True

    geometry = run_geometry_case(args.output_dir)
    print(
        f"{geometry.name}: lines={geometry.lines} images={geometry.images} "
        f"drift_px={geometry.drift_pixels} output={geometry.output_path}"
    )
    if geometry.images < 1 or geometry.lines < 190 or geometry.drift_pixels > 12:
        failed = True

    weather_map = run_map_case(args.output_dir)
    print(
        f"{weather_map.name}: lines={weather_map.lines} images={weather_map.images} "
        f"corr={weather_map.best_correlation:.3f} mae={weather_map.best_mae:.2f} "
        f"crop_y={weather_map.crop_y} shift={weather_map.shift} "
        f"source={weather_map.source_path} output={weather_map.output_path}"
    )
    if weather_map.images < 1 or weather_map.lines < 330 or weather_map.best_correlation < 0.70:
        failed = True

    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
