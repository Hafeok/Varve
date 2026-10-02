# 0073 — Durability per host, declared, and what each level survives

## Status

**Proposed — filed unaccepted by session 6a of #10, 2026-10-02** (ADR 0066).

Completes ADR [0018](0018-storage-abstraction.md)'s "durability is declared,
not assumed" for the file backend, and states, for every level, what survives
power loss. The browser's level is 6b's.

## Context

ADR 0018 declared three levels — `Synchronised`, `Committed`, `None` — and ADR
0040 gave the memory backend `None`. T1 step 7 says a commit returns after its
records are "durable", and ADR 0013 makes the closing record's durability the
commit point. What "durable" means on a file system is not one thing, and three
facts from this session decide what the file backend can promise.

1. **`RandomAccess.FlushToDisk` is the whole flush.** In .NET 10 it calls
   `SystemNative_FSync`, which is `fsync(2)` on Linux and the other Unixes,
   `fcntl(F_FULLFSYNC)` on macOS — the one call there that flushes the drive's
   cache — and `FlushFileBuffers` on Windows (runtime `release/10.0`,
   `src/native/libs/System.Native/pal_io.c`). `fsync` covers the file's data
   and its metadata, including its length.
2. **No managed API opens a directory.** `File.OpenHandle` and `FileStream` on
   a directory throw `UnauthorizedAccessException` (measured on Linux in this
   session); the runtime refuses directories on purpose. Flushing a directory
   — which POSIX requires before a newly created or renamed file's *name* is
   durable — therefore needs P/Invoke, which constraint 1 forbids.
3. **What a new file's directory entry survives depends on the file system**,
   because POSIX leaves it unspecified when only the file is flushed.

## Decision

### The file backend declares `Synchronised`

- **`ISegmentStore.FlushAsync` returns after `FlushToDisk` on the segment**: the
  appended bytes and the file's length are on the device. The store flushes
  once per commit, after its last record (ADR 0013), so the closing record is
  durable no earlier than every record before it.
- **Sealing** appends the trailer (ADR 0072), flushes, and marks the file
  read-only. **Creating** a segment writes its header and flushes it before the
  first record is appended.
- **Publishing a derived blob** writes a temporary file, flushes it, and
  renames it over its name. **The manifest** is written once with
  `FileMode.CreateNew` and flushed before any segment exists.
- **No directory is flushed**, by fact 2.

### What each level promises on power loss

The documentation of `Durability` says this, member by member:

| Level | After a commit returns, power loss keeps it when |
|---|---|
| `Synchronised` | always, on a device that honours its flush command — **except** a commit in a segment whose directory entry the file system had not yet made durable (below) |
| `Committed` | the transactional store kept what it reported committed |
| `None` | never: memory only |

### A lost directory entry is a segment never created

| File system | A new file's entry, after the file is flushed |
|---|---|
| **ext4** (`data=ordered`, the default) | Durable: `fsync` commits the journal transaction that holds the create. Documented by ext4's maintainers, not by POSIX. |
| **XFS** | Durable: `fsync` forces the log through the inode's last change, which includes its create. |
| **btrfs** | Durable: `fsync` logs the new name with the inode in the log tree. |
| **NTFS** | Not documented. The create is journaled in `$LogFile`; `FlushFileBuffers` flushes the file, and Microsoft does not state that it forces the journal record of the directory change. |
| **APFS** | Not documented for `F_FULLFSYNC` on the file alone. |
| **tmpfs**, and any file system mounted without barriers | Nothing survives power loss; `Synchronised` is a statement about the device, and such a mount is not one. |

These rows are what each file system documents. **None was measured by cutting
power in this session**; the power-loss simulation in the tests models the worst
row.

**The trailer design makes a lost entry harmless to consistency.** A segment is
created only after its predecessor's closed trailer is durable. If power loss
drops the new segment's entry, recovery finds a sealed predecessor and no
successor, which is exactly the state after "sealed, and the next segment never
created": the log opens, cleanly, at the predecessor's last commit, and the next
commit creates the segment again. **It is not harmless to durability**: on a
file system in the undocumented rows, commits acknowledged into a segment
whose entry was lost are lost with it. That is stated in `Durability`'s
documentation, and it is the cost of constraint 1. A derived blob whose rename
is lost is a cache miss; a manifest whose entry is lost leaves a directory
with no dataset in it, because nothing else was written before it.

### The memory backend declares `None`; the browser backend declares what 6b finds

## Alternatives considered

- **Flush the directory through P/Invoke** (`open(O_DIRECTORY)` and `fsync`).
  The correct POSIX answer, and the only way to close the gap above. Rejected by
  constraint 1, which admits no exception process.
- **Pre-create the next segment at each seal**, so that a commit never lands in
  a segment created after it began. Moves the gap rather than closing it: the
  pre-created file's entry has the same problem, one segment earlier.
- **Write one file only** (a single growing log file). No new directory entries
  after creation. Rejected: sealed segments are what keep files below hosting
  limits and what make copies cheap (§2).
- **Declare `Committed` for the file backend** on the undocumented file systems.
  It names a transactional store, which a file system is not; a weaker label on
  the strong platforms would hide a guarantee a caller has (ADR 0018's reason
  for declaring at all).

## Consequences

- **A commit returns after one device flush.** Commit latency on the file
  backend is the device's flush latency; the milestone report measures it.
- **The gap is named, per file system**, rather than discovered. A host that
  needs the undocumented rows closed has a superseding ADR to write about
  constraint 1, not a bug to find.
- **Copies need no flush at all**: `git add` reads whatever is there, and ADR
  0072's chain rules open whatever it read.

## Checks

- **Checked against the accepted ADRs** (0001–0069). Completes **0018** for the
  file backend; touches **0013** (one flush after the last record orders the
  closing record last), **0040** (`FlushAsync`'s meaning), **0071** (publishing
  is durable before visible), and **0072** (the trailer is what makes a lost
  entry consistent). No conflict with any.
- **Layer ownership.** `Varve.Store`, **layer 4**.
- **Analyzer rule.** None.
- **Open questions owned.** None.
