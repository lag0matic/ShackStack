# Local Checkout Retirement

The project remains parked. The `codex/nexus-layout` branch retains the final
local layout work, shared controls, preview launcher, comparison notes and
Raspberry Pi experiment plan before removal of the local working copy.

This is a preservation snapshot, not a new tested release. No radio behavior or
installer was changed for the archival commit. Refer to
`NEXUS_LAYOUT_STATUS_2026-10-19.md` for the state of the unfinished layout work.

The native build checkout's small local patch is preserved under
`tools/patches/`. The clean MMSSTV reference remains pinned by the existing Git
submodule. Generated decoder workers, compiler outputs and caches are not source;
their build inputs and scripts remain in the repository.

Nexus is maintained separately and is not part of this source snapshot. Local
Nexus source, history, packages, backups and Linux build workspaces are retained.
