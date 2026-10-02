---
set: bulk-load-validators-scan-the-delta-on-disk
namespace: varve
adr: 0077
decisions:
  - key: ValidatorsSeeTheBulkDeltaOnDisk
    statement: "For a bulk commit, validators receive the overlay of the pinned state and the delta as a run on disk, scanned through the synchronous blob read, and are never disabled"
---

The ruling of [ADR 0077](../adr/0077-bulk-load-validators-scan-the-delta-on-disk.md), filed unaccepted
by session 6a of #10 (ADR 0066). Implemented in 6b.
