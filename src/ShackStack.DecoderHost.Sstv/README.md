# ShackStack.DecoderHost.Sstv

This project is the future native SSTV sidecar for ShackStack.

Purpose:
- host a ShackStack-owned SSTV engine
- harvest proven SSTV logic from MMSSTV
- keep the SSTV desk/UI in ShackStack while replacing the current prototype Python decoder

Planned responsibilities:
- RX SSTV mode/timing detection
- VIS decode
- sync/AFC/slant handling
- progressive image decode
- TX PCM generation later

Design constraint:
- no Windows VCL shell
- no old WinMM device layer
- stdin/stdout process protocol only

Implementation plan:
- see `docs/SSTV_MMSSTV_HARVEST.md`

