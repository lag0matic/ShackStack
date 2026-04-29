# SSTV Regression Harness

This harness gives ShackStack a repeatable SSTV RX sanity check when the bands are bad or when decoder thresholds change.

## Run

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Run-SstvRegression.ps1 -Configuration Release
```

The script runs:

```powershell
dotnet run --project .\src\ShackStack.DecoderHost.Sstv.Harness\ShackStack.DecoderHost.Sstv.Harness.csproj -c Release -- --regression
```

## Current Cases

| Case | Purpose |
| --- | --- |
| `static_noise_reject` | Verifies Auto Detect does not start a bogus image from pure static. |
| `martin_1_qrn_34db_clock_plus50_auto` | Martin 1 with light synthetic QRN and positive sample-clock skew. |
| `martin_2_qrn_30db_auto` | Martin 2 with synthetic QRN/noise to protect weak-but-valid VIS/sync behavior. |
| `scottie_1_clock_75ppm_auto` | Scottie 1 with sample-clock skew to guard against slant/timing regressions. |
| `scottie_2_qrn_32db_clock_minus50_auto` | Scottie 2 with QRN and negative sample-clock skew, guarding against mode favoritism. |
| `robot_36_qrn_34db_auto` | Robot 36 auto-detect loopback with less-than-perfect signal. |
| `robot_36_tx_roundtrip_auto` | Robot 36 TX encoder smoke: generated TX audio must VIS-detect and auto-decode as Robot 36. |
| `pd_120_clean_auto` | PD 120 auto-detect loopback. |
| `martin_1_force_late` | Locked-mode late force-start path, simulating a missed preamble. |

## Artifacts

The harness writes WAVs, decoded BMPs, logs, and a tab-separated summary under:

```text
.tmp-sstv-harness\regression
```

The summary file is:

```text
.tmp-sstv-harness\regression\summary.txt
```

## Pass Criteria

Decode cases must:

- Detect the expected mode.
- Produce at least one image update and a decoded image.
- Keep image comparison within a broad sanity envelope: mean absolute error below `90`, and at least one color channel correlation above `0.35`.

The static-noise case must:

- Produce no image updates.
- Produce no decoded image.
- Avoid entering a receiving state.

These thresholds are intentionally not picture-perfect. The harness is meant to catch broken RX flow, false starts, mode selection regressions, and major color/timing failures without overfitting to one synthetic image.

Current 2026-04-29 baseline: `9/9` regression cases pass in Release.
