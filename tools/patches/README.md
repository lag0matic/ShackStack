# Retained Native Build Patch

`freedv-rade-native-recovery.patch` preserves the small local changes from the
retired RADE build checkout. The original upstream repository is
https://github.com/peterbmarks/radae_nopy at commit
`4da110afd6de3bb2de8800d67c6d82a292a42e77`; retain its license and notices when
restoring the source.

The patch adjusts the Opus build commands for MSYS2/UCRT and flushes streamed
feature/audio output promptly. It is a recovery artifact, not a newly tested
build-system change. Check it with `git apply --check` on that upstream revision
before applying it. The corresponding build helper is
`tools/Build-FreeDvRadeNative.ps1`.
