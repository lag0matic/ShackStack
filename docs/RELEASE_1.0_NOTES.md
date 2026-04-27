# ShackStack 1.0 Release Notes

ShackStack 1.0 is the first "good enough to live with on the desk" build. It packages the desktop app, bundled decoder workers, and the current FT8/FT4, SSTV, RTTY, voice, POTA, and Longwave workflows into a self-contained Windows installer.

## Highlights

- FT8/FT4 receive and transmit use the GPL WSJT-X sidecar boundary, with SNTP fallback timing when Windows Time is unavailable or stale.
- JS8 receive and transmit are available from the JS8 desk using bundled JS8Call-compatible runtime tools.
- SSTV receive follows the MMSSTV-derived path for the common ham modes, including slant/sync correction, archive saving, live preview, and post-signal completion handling.
- SSTV transmit includes clean template overlays, received-image thumbnail replies, optional MMSSTV-style FSKID callsign encoding, and `%tocall` replacement from decoded FSKID.
- Voice, CW, RTTY, weak-signal, WeFAX, SSTV, JS8, and Longwave workflows now use dedicated desk windows with the main app serving as a launcher/status surface.
- WeFAX includes a schedule-aware receive desk, manual slant/offset correction, image cleanup controls, and archive review.
- RTTY receive uses the GPL fldigi-derived sidecar instead of the old Python worker, with manual audio-center tuning, reverse polarity support, and USB-D/LSB-D friendly rig handling.
- ShackStack now uses the antenna/ground circuit badge branding for the app, window, and installer icons.
- The installer build now rebuilds and packages the native SSTV sidecar, GPL RTTY sidecar, GPL WSJT-X sidecar, and remaining Python workers so installed builds are not dependent on random local decoder paths.

## Known Rough Edges

- CW decode is still experimental.
- JS8 TX supports the current Varicode/Huffman text path; richer JS8Call compressed chat packing can be expanded later.
- RTTY decode quality remains dependent on signal quality, polarity, shift/baud profile, and tuning.
- FT8/FT4 auto-sequencing and logging should still be watched carefully during real QSOs.
- Packaging is intentionally simple for now: self-contained `win-x64` publish wrapped by Inno Setup.

## Validation Checklist

- Build bundled workers with `installer\Build-DecoderWorkers.ps1`.
- Publish `src\ShackStack.Desktop` self-contained for `win-x64`.
- Compile `installer\ShackStack.iss` with Inno Setup.
- Smoke test FT8 time status, radio connection, SSTV RX/TX, and RTTY RX against a known signal or generated WAV.
