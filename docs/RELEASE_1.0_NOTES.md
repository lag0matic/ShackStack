# ShackStack 1.0 Release Notes

Last refreshed: 2026-04-29.

ShackStack 1.0 is the first "good enough to live with on the desk" build. It packages the desktop app, bundled decoder workers, and the current FT8/FT4, SSTV, RTTY, voice, POTA, and Longwave workflows into a self-contained Windows installer.

## Highlights

- FT8/FT4 receive and transmit use the GPL WSJT-X sidecar boundary, with SNTP fallback timing when Windows Time is unavailable or stale.
- WSPR receive is available through the weak-signal desk using the bundled WSJT-X `wsprd.exe` path.
- JS8 receive and transmit are available from the JS8 desk using bundled JS8Call-compatible runtime tools.
- SSTV receive follows the MMSSTV-derived path for the common ham modes, including slant/sync correction, archive saving, live preview, and post-signal completion handling.
- SSTV transmit includes clean template overlays, received-image thumbnail replies, optional MMSSTV-style FSKID callsign encoding, and `%tocall` replacement from decoded FSKID.
- Voice, CW, RTTY, weak-signal, WeFAX, SSTV, JS8, and Longwave workflows now use dedicated desk windows with the main app serving as a launcher/status surface.
- WeFAX includes a schedule-aware receive desk, manual slant/offset correction, image cleanup controls, and archive review.
- RTTY receive uses the GPL fldigi-derived sidecar instead of the old Python worker, with manual audio-center tuning, reverse polarity support, and USB-D/LSB-D friendly rig handling.
- FreeDV/RADEV1 support is harness-proven with Codec2/RADE sidecar integration, end-of-over callsign metadata, separated decoded-audio output, and stable live-state desk layout. Live RF validation remains in progress.
- PSK31/PSK63 work has a desk/sidecar and synthetic TX audio path, but real-signal RX is still in progress.
- Longwave logging is now more consistent across desks, including mode quick-log previews, POTA spot posting, QRZ upload result messages, and logbook selection fixes.
- SSTV regression coverage now includes imperfect auto-start samples for Martin 1/2, Scottie 1/2, Robot36, static rejection, force-start, PD120, and Robot36 TX round-trip smoke testing.
- `MainWindowViewModel` has been split into concern-based partial files so the codebase is less of a single giant panel controller.
- ShackStack now uses the antenna/ground circuit badge branding for the app, window, and installer icons.
- The installer build now rebuilds and packages the native SSTV sidecar, GPL RTTY sidecar, GPL WSJT-X sidecar, and remaining Python workers so installed builds are not dependent on random local decoder paths.

## Known Rough Edges

- CW decode is still experimental.
- JS8 TX supports the current Varicode/Huffman text path; richer JS8Call compressed chat packing can be expanded later.
- RTTY decode quality remains dependent on signal quality, polarity, shift/baud profile, and tuning.
- FreeDV live operation still needs more on-air validation and latency polish.
- PSK31/PSK63 receive needs real-signal validation.
- FT8/FT4 auto-sequencing and logging should still be watched carefully during real QSOs.
- Packaging is intentionally simple for now: self-contained `win-x64` publish wrapped by Inno Setup.

## Validation Checklist

- Build bundled workers with `installer\Build-DecoderWorkers.ps1`.
- Publish `src\ShackStack.Desktop` self-contained for `win-x64`.
- Compile `installer\ShackStack.iss` with Inno Setup.
- Smoke test FT8 time status, radio connection, SSTV RX/TX, and RTTY RX against a known signal or generated WAV.
