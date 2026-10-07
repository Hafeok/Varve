---
set: the-file-suites-run-without-the-device-flush
namespace: varve
adr: 0090
decisions:
  - key: FileSuitesUnflushed
    statement: "The file property tests, the storage contract on files and the derived tests on disk run on a test-only file system over the real one whose flush does nothing; every other call reaches the disk"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
  - key: FlushesKeptWhereTheyAreTheSubject
    statement: "FaultInjectionTests and BulkLoadTests keep the simulated file system's flushes, the durability job flushes the device, and FileStorageTests opens storage through the public FileStorage.OpenAsync, which always flushes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
  - key: DefenderSkipsTheBuildJobsFiles
    statement: "On windows-latest the build job excludes the workspace and the temporary folders from Defender before it checks out"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
---

The rulings of [ADR 0090](../adr/0090-file-suites-without-the-device-flush.md),
filed unaccepted by the Windows test-time slice of #10 (ADR 0066).
