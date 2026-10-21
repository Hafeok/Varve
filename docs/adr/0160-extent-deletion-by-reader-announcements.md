# 0160 — Extent deletion by reader announcements: readers announce an interval, and no timer

## Status

**Accepted — filed unaccepted by milestone Extent maintenance of #110,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Extent maintenance plan,
2026-10-19:
- a record announces an interval, the oldest and the newest catalog versions
  its reader holds, not only the oldest;
- the writer writes a record too;
- announcing is required, and an unannounced read is explicit;
- the order of the writer's steps is stated and proved;
- a record of an unknown version holds everything;
- the reader record and the writer record are coordination state, read
  forever;
- a maintenance round may poll;
- the catalog version is the set-sequence counter.

**Supersedes in part** [ADR 0139](0139-extents-and-the-projection-catalog.md),
its section "Deletion waits for Extent maintenance". Its ruling
`DeletionWaitsForExtentMaintenance` moves to this ADR's set under the same
key, restated. **Amends** [ADR 0134](0134-the-object-store-contract.md): a
follower writes its reader record. Both by dated amendment.

Records decision set [`extent-deletion-by-reader-announcements`](../decisions/extent-deletion-by-reader-announcements.md).

## Context

A set no catalog names was retired once no view of this process held it.
On a directory, which one process holds under the OS lock, it was then
deleted. On an object store it was kept, because another instance — a
follower, a command-line read, a writer superseded but still answering reads —
may hold an older catalog version that names it (ADR 0139). Merges (ADR 0158)
retire sets at every round, so that store would only grow.

Deleting a set is safe once no instance can still read a catalog version
that names it. An instance that has died cannot say it is done, and deciding
it is dead from elapsed time is the timer ADRs 0116 and 0136 refuse. So every
instance says what it holds, and the writer deletes only what nobody holds.
A dead instance's statement holds until an event removes it.

## Decision

### The catalog version

**1. A catalog's version is its next set sequence, and every swap advances
it.**
- The catalog already carries the sequence the next set's name takes
  (ADR 0139). The writer now increments it at every swap, a swap that only
  drops the newest set included, so two catalog versions never share it.
- A set whose name carries sequence `s` is written after a catalog of version
  `≤ s`, so it is named only by versions after `s`: by the versions `v` with
  `s < v < r`, where `r` is the version whose swap stopped naming it.
- The writer knows `r` for a set it retires. For a set it finds unnamed at
  open, it takes the current version as `r`, which errs on the side of
  keeping. For a set the store's catalog still names past where this log's
  chain ends, it takes no `r` until its own next swap.
- No byte of the catalog changes: a preview.8 build reads a catalog whose
  sequence has gaps.

### The reader record

**2. Every instance that reads extents from the store keeps a reader
record.** That covers the writer, followers, and the command line's read
commands that attach (ADR 0162). The record:
- is the object `readers/<id>` under the dataset's prefix, beside `writer`;
- is JSON an operator can read: `format` 1, `reader`, `holder` (the
  instance's name), `catalogOldest`, `catalogNewest`, `at` (the host's clock,
  for the operator, never compared), and `state`, `announced` or `forgotten`;
- announces an **interval**: the oldest and the newest catalog versions the
  instance's reads hold.

A writer holds several versions at once: a view taken before a swap holds
the version it was taken at. A record that announced only the oldest would
let a writer delete a set named in the versions between that oldest and a
newer one another view still reads.

**3. The identity survives a restart of the same instance.**
- It is `ObjectStorageOptions.Reader`, configured. On the server that is
  `Varve:ObjectStorage:ReaderId`.
- Unset, it is a random 128-bit id kept in the cache directory as
  `reader.id`, which the cache's page wipe at open leaves.
- A container whose cache does not survive a restart sets it. Otherwise
  every restart leaves a record behind for an operator to forget.
- The command line's reads take a fresh id per run, in a cache they delete.

**4. Only its reader writes a record, and only when the interval changes.**
- The first announcement of an open creates the record by create-if-absent,
  or replaces the one a previous run of the same id left, which is how a
  restart removes a dead record.
- Afterwards the record is replaced only by replace-if-unchanged, against
  the version this instance last wrote, and only when the interval changes. A
  follower's interval is the version it attached and never changes. A
  writer's changes at a swap, and when a view of an old version closes.
- A clean close deletes the record.
- A replace that finds another version means the record was forgotten, or
  another instance of the same id wrote it. This instance is then forgotten
  (point 9).

**5. The writer's own interval.**
- Its oldest is the oldest version any set it retired, and some view of it
  still reads, could be named by: that set's sequence plus one. It is the
  current version when there is none.
- Its newest is the current version, or the version it is about to swap to.
- The writer's own views are kept safe within this process by the run
  refcount. The record is what keeps them safe after a supersession, when a
  newer writer that knows nothing of this process merges and deletes. With a
  record, every role is the same, as Replication's handover (#97) needs.

### The order of steps, and why it is safe

**6. A reader announces, then confirms.**
- A reader reads the catalog, of version `v`, and writes its record
  announcing `v`.
- It then reads the catalog again. If that is not version `v` — another
  sequence, position or set list — it announces the newer one and reads
  again.
- It reads no extent of a version until the catalog it reads is the one it
  announced.
- A writer announces a version before it swaps to it.

**7. The writer swaps, then reads the records, then deletes.**
- A set waits for deletion only once a swap has stopped naming it.
- At each attempt the writer brings its own record up to date, reads every
  record, and deletes a set — `s` its sequence, `r` the version that removed
  it — only when no record's `[oldest, newest]` meets the open interval
  `(s, r)`.
- A record of a format this build does not read holds every version: it
  blocks deletion and is never ignored. So does one that does not parse.

*Why no live reader loses a set it reads.* The store's reads and writes are
linearisable (ADR 0134). Take a reader that holds `v`, with `s < v < r`, and
the writer that deletes `s`:

- **The writer's read of the records comes before the reader's write of its
  record.** The writer swapped to `r` before that read, so the reader's
  confirming read, after its write, finds a version at least `r`. It never
  reads `v`'s extents.
- **The reader's write comes first.** The writer reads `[v, v]`, which meets
  `(s, r)`, and keeps `s`.

A writer that holds `v` announced it before swapping to it, so the same holds
across supersession. A record's interval only widens while a view it covers
lives: a new view is taken at the current version, which the record already
announces, and the oldest rises only when the views of older versions have
closed. `ObjectReaderScheduleTests` checks this over generated schedules,
with a writer holding several versions at once and a second writer taking
the epoch over.

### Dead and forgotten readers

**8. A dead reader's record holds deletion back until an event removes it.**
The events are:
- the same instance restarting, which replaces its record;
- the operator's `varve storage readers forget` (ADR 0162);
- later, fleet membership from the Management plane (#105), which knows when
  an instance has left. That is a consequence of this ADR for #105, and is not
  built here.

Never a timer. The retained bytes are visible on readiness and in
`varve storage readers list` (ADR 0162), so a dead record is found by its
cost, not guessed by its age.

**9. A forgotten reader fails, and attaches again.**
- The operator may forget a reader that is in fact alive, or two instances
  may share an id. Such a reader's reads may find an extent gone.
- A read that finds one checks the reader's own record. If the record is gone,
  tombstoned or written by another, the read fails with
  `ReaderForgottenException`, over HTTP `503 reader-forgotten`. Otherwise it
  is damage, an `IOException` naming it.
- The instance attaches again: it reopens, with a fresh record and the
  newest catalog.
- It never returns a wrong result. A name is never reused and an extent never
  changes, so a page the cache still holds is still right.

**10. Announcing is required, and an unannounced read is explicit.**
- An open whose record the store refuses (`403`) fails with
  `ObjectStoreAccessDeniedException`, whose message names the role it needs
  (point 12).
- `ReaderAnnouncing.Unannounced`, on the command line `--unannounced`, is a
  read-only open that writes no record, for an identity that may only read.
- Such a read may find an extent gone. It then fails with
  `UnannouncedExtentGoneException`, over HTTP and on the command line
  `read-unannounced-extent-gone`, never with the damage error and never with
  a wrong result.
- There is no silent fallback from one to the other.

**11. A maintenance round may poll.**
- The writer reads the records at a swap, at its open, and while sets wait
  for deletion at every sixteenth maintenance round.
- When it reads them decides only how soon a set is deleted, never whether
  deleting it is safe, which rests on point 7 alone.
- An idle writer — no commit, no checkpoint — deletes nothing until something
  happens, a forget included.

**12. A follower writes its reader record, and nothing else** (ADR 0134,
amended). Its identity needs write access to the dataset's `readers/`:
- **Azure:** Storage Blob Data Contributor with an ABAC condition on the
  blob path, `readers/*` under the dataset's prefix, beside Storage Blob Data
  Reader on the container.
- **S3:** `s3:PutObject` and `s3:DeleteObject` on `<prefix>readers/*` in a
  prefix-scoped policy, beside `s3:GetObject` and `s3:ListBucket`.

The workload's identity stays the only credential (ADR 0137). The operator
guide gives both.

**13. On a directory nothing changes.** The process holding the OS lock is
the only reader: `FileStorage` does not implement `IExtentReaders`, and a
retired set is deleted once no view holds it, as before.

**14. The records are coordination state, read forever.**
- The reader record's format 1, like the writer record's (ADR 0136), is read
  by every later release.
- Derived formats are rebuilt, not read forever (ADRs 0072, 0139).
- The storage format specification states the record, §8, with its version.

### The contract

**15. `IExtentReaders` in `Varve.Store`** is the contract an instance's
storage implements:
- `DeletesExtents`;
- `AnnounceAsync(CatalogVersion oldest, CatalogVersion newest)`;
- `ReadAnnouncementsAsync()`;
- `ExtentBytesAsync()`.

`Varve.Store.ObjectStorage` implements it. The model is `CatalogVersion`,
`ReaderId` and `ReaderAnnouncement` in `Varve.Store.Log`, with
`ReaderAnnouncement.Holds` as the rule. The Management plane's follower
(#105) uses it unchanged:
- **Opening.** It opens `ObjectStorageAccess.ReadOnly`, and `Dataset`'s one
  path that attaches a catalog announces and confirms.
- **Moving to a newer catalog.** When its tail moves to one, it attaches
  through that path, announcing the oldest version it still holds and the
  newest.

## Alternatives considered

- **Announce only the oldest version.** Unsafe for a reader holding two
  versions, as the maintainer pointed out (point 2).
- **No record for the writer; its refcount is enough.** The refcount ends
  with the writer's process; after a supersession its in-flight reads would
  be invisible to the new writer.
- **A grace period.** A timer that decides a reader is gone when it is slow,
  refused by ADRs 0116 and 0136, and by ADR 0139 for this very case.
- **The record as an epoch-style lease the writer renews.** The same timer.
- **The removal version kept in the store**, so a new writer knows `r` for
  every set. A second object to keep consistent with the catalog. The current
  version is a bound that errs only on the side of keeping, and the sets it
  keeps go at the next event.
- **Read unannounced when the record cannot be written.** A silent
  degradation: such a read can fail later, where its caller asked for none.

## Consequences

- `Varve.Store` gains:
  - `IExtentReaders`;
  - `CatalogVersion`, `ReaderId` and `ReaderAnnouncement`;
  - `ExtentRetention` and `ReaderRetention`;
  - `ReaderForgottenException`, `UnannouncedExtentGoneException` and
    `ObjectStoreAccessDeniedException`;
  - `Dataset.CatalogVersion` and `Dataset.Retention`.
- `Varve.Store.ObjectStorage` gains the record:
  - `ObjectStorageOptions.Reader` and `Announcing`;
  - `ObjectStorage.Reader`, `ReadReadersAsync`, `ForgetReaderAsync` and
    `ReadReportAsync`;
  - the model types `ReaderRecord`, `ReaderRecordState` and
    `ReaderAnnouncing`.
- Both adapters raise `ObjectStoreAccessDeniedException` for `401` and
  `403`.
- The problem catalogue gains `reader-forgotten` and
  `read-unannounced-extent-gone` (ADR 0119), both `503`.
- A follower's identity needs write access to `readers/` (point 12).
  `deploy/azure/` gives the writer's identity Storage Blob Data Contributor
  on the container already, which covers its record.
- An object store's unnamed sets are now deleted. The operator guide's
  checkpoint budget is gone (ADR 0159).
- The Management plane (#105) will remove the records of instances that left
  the fleet, which is the third event of point 8.

## Checks

- **Checked against the accepted ADRs** (0001–0147). Touches:
  - **0011**: no ambient clock or randomness; the id comes from the injected
    source.
  - **0042**: the dataset calls; the backend starts nothing.
  - **0072**: the reader record is not derived data, and is read forever.
  - **0116** and **0136**: no timer.
  - **0119**: two problem types.
  - **0134**: amended, a follower writes one object.
  - **0137**: identity only.
  - **0139**: superseded in its deletion section.

  No conflict.
- **Layer ownership.**
  - The contract, the rule and the exceptions: `Varve.Store`, layer 4.
  - The record and its format: `Varve.Store.ObjectStorage`, layer 5.
  - The problem types: `Varve.Protocol`, layer 5.
  - The command line and readiness: `Varve.Server`, layer 6.

  Every reference is downward.
- **Analyzer rule.** None.
- **Tests.**
  - `ObjectReaderScheduleTests`: the reader-schedule property, and a fault
    at every write and delete.
  - `ObjectExtentMaintenanceTests`:
    - a follower across merges and deletions, also on Azurite and RustFS;
    - a dead reader held until it restarts or is forgotten, with no timer;
    - a forgotten reader;
    - an unannounced read;
    - a `403` naming the role;
    - an unknown format holding everything.
