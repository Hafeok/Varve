---
set: durability-per-host-declared
namespace: varve
adr: 0073
decisions:
  - key: FileBackendIsSynchronised
    statement: "The file backend declares Synchronised: a flush returns after FlushToDisk, so the appended bytes and the file's length are on the device"
  - key: LevelsStatePowerLoss
    statement: "The documentation of each durability level states what survives power loss, and names the directory-entry exception for Synchronised"
  - key: NoDirectoryFlush
    statement: "The file backend flushes no directory, because no managed API opens one; a lost entry of a new segment is a segment never created"
  - key: FlushBeforeVisible
    statement: "A derived blob is flushed before it is renamed into place, a segment's header before its first record, and the manifest before any segment exists"
---

The rulings of [ADR 0073](../adr/0073-durability-per-host-declared.md), filed unaccepted by session 6a
of #10 (ADR 0066).
