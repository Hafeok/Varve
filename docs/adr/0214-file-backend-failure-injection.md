# 0214 — File-backend failure injection: four faults, at every point, on simulated and on real files

## Status

**Accepted — filed unaccepted by 1.0 readiness A (#129, taking over #61's fault half),
2026-10-28** (ADR 0066). Acceptance is the maintainer's act on the pull
request. The plan it follows was approved on #129 on 2026-10-27.

**Amends** [ADR 0073](0073-durability-per-host-declared.md) by dated
amendment: what the file backend promises under a full disk, and how its
durability claims are tested on real files. **Supersedes in part**
[ADR 0124](0124-group-commit.md): its point 3 for a full disk, and the ruling
`AcknowledgedOnlyWhenTheBatchIsDurable`, which moves to this ADR's set,
restated. **Amends** [ADR 0119](0119-headers-and-the-problem-catalogue.md) by dated
amendment: the problem type `storage-full`.

Records decision set
[`file-backend-failure-injection`](../decisions/file-backend-failure-injection.md).

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
version 1.9: a failed batch answered by the log, a full disk recovered by
the next write, and an open that flushes what it adopts.

## Context

#129's first deliverable asks for four faults, each injected at every point
it can happen:
- a torn write at every write the backend issues;
- a full disk at every allocation;
- a process killed at every fsync boundary;
- a power cut, simulated by dropping unsynced pages through the file-system
  seam.

After each, the store opens, every committed position reads back exactly,
and no uncommitted position is visible. The runs belong in the release gate,
not the land gate (#126 measured where the time goes).

The seam is `IFileSystem` and `IFileHandle` (`src/Varve.Store/FileSystem.cs`),
which `DiskFileSystem` implements; `FileStorage` reaches the disk only through
it. The seam has no `SetLength` and no preallocation, so a file grows only by
a write past its end: **an allocation is a create, a write that extends a
file, or a rename to a new name.**

What the land gate already has (milestone 6a, `FaultInjectionTests`):
- a process crash at every operation, with the log's writes cut at every byte
  and other writes at their start, middle and end;
- a power loss with writes kept, dropped or torn at a sector up to the flush,
  sampled at three to six images a point;
- lost directory entries, a crash during recovery, and a copy taken
  mid-write.

All of it runs in memory, in `SimulatedFileSystem`. Nothing injects a full
disk, nothing kills a real process at a flush, and nothing cuts the power on
the real file system the store ships on.

## Decision

**1. Torn writes, at every byte of every write.** Under the process model,
every write the backend issues is cut at every byte, not only the log's.
That covers the segments, the manifest, every derived file and the small
files. Under the power-loss model, for every crash point, every unflushed
write is cut at every 512-byte sector boundary in turn, with the other
unflushed writes once kept whole and once dropped. These are enumerated, not
sampled.

**2. A full disk, at every allocation.** `SimulatedFileSystem` gains a
second injector that refuses an operation with the platform's disk-full
`IOException`, instead of crashing. It refuses each allocating operation of
the workload in turn, and every one after it until the space returns; for
batches, it also refuses every write and every flush of the log in turn. A full
disk is the platform's disk-full or quota-exceeded error: `ERROR_DISK_FULL`
and `ERROR_HANDLE_DISK_FULL` on Windows, `ENOSPC` and `EDQUOT` on Linux and
macOS. Then:
- **the commit that met it is not acknowledged.** It is answered
  `storage-full` when it was never closed, and outcome unknown when it was
  closed and its flush failed (below);
- **the dataset stays open for reads,** at its head;
- **the next write recovers the writer itself.** No timer, and no reopen or
  restart by an operator. The write request does what an open does, in
  place: what was written is flushed, the commits the log closed are applied,
  and the writer is opened on the scan, which fences a torn tail off with an
  abandoned trailer and starts the next commit in a new segment (storage
  format §5). When the space is back, the write proceeds. When it is not, the
  write is refused `storage-full` and the dataset stays as it was, for the
  next write to try again;
- **HTTP answers `507 Insufficient Storage`** with a new problem type,
  `storage-full` (ADR 0119): nothing was committed, and a retry once there is
  space is right. It is not `503 unavailable`, which says the dataset cannot
  take writes at all;
- **readiness reports it and stays ready.** `/health/ready` gains
  `writesRefused`, naming each dataset whose last write was refused for lack
  of space as `storage-full`. Reads go on, so the server is not unready;
- **derived writes** (checkpoints, maintenance, the projection's state) that
  meet a full disk fail without breaking the dataset. A later round retries
  them, and a reopen finds the cache as it was or rebuilds it.

**Committed means closed and flushed.** On a full disk the flush can fail
too, so the writer tracks two positions: the last commit whose closing record
it appended, and the last that a flush which returned covered. When a
batch's write fails, it flushes once more, unless a flush is what failed:
after a failed flush, no later flush vouches for the bytes before it. That is
the fsyncgate behaviour: after a failed `fsync`, Linux may mark the dirty
pages clean without writing them, and a later `fsync` then reports success
for bytes that never reached the device; other systems and file systems
differ, and the rule assumes the worst. Each
member is then answered by the log, before anyone is answered, with one of
three answers:
- **closed and flushed: committed.** A reopen would find it;
- **not closed: `507 storage-full`.** The writer's recovery fences it off
  (below), so it is never in the log;
- **closed, flush not confirmed: outcome unknown,** `503
  commit-outcome-unknown` with its position and the request id. Never `507`,
  since the record may survive. The request id is the commit's cause (ADR
  0094), so the commits resource settles it once the head reaches the
  position, as ADR 0136's amendment settles a conditional write whose answer
  is lost: by reading back and recognising the writer's own write. A retry
  conditional on the position before commits exactly once.

**One outcome-unknown type.** `commit-outcome-unknown` is also the answer to
a forwarded write whose answer from the writer was lost (ADR 0168 point 7),
in place of a `503 unavailable`; it then has no `position`. The problem page
covers both causes and the resend that is safe after each: conditional on
the position before. When #132's idempotency keys land, its ADR makes a
resend with the same `Idempotency-Key` the first option on that page.

The committed members are therefore a prefix of the batch, in order. This
restates ADR 0124's `AcknowledgedOnlyWhenTheBatchIsDurable`, which made a
failed batch all or nothing, and moves it to this ADR's set.

**The recovery fences.** The next write opens the writer on a scan stopped at
the head, the last commit closed and flushed. The newest segment gets an
abandoned trailer naming that head, so that nothing written after it is ever
read (storage format §5), an outcome-unknown commit included, and the next
commit starts a new segment. A crash before the fence is durable leaves an
outcome-unknown commit to the next open, which commits it if its record
survived; either way the commits resource says which.

**The term dictionary forgets its caches.** A failed batch's staging gave
ids to new terms, and the dictionary's caches remember them; those commits
never land, and the next batch gives the same ids to other terms. After a
failed append the caches are forgotten, inside the sequencer, before the
next staging. The batch sweeps found this: a term read back as another.
Every term committed before a failed append and after it must read back as
itself, whether the failure was a full disk or not and whether a recovery
or a reopen followed; the sweeps check it at every position.

**An open flushes what it adopts.** A dataset reopened after a failed
flush, by an operator, a restart or a process that closed it, finds the
records that flush did not confirm and takes the closed ones as committed. A
commit is committed when it is closed and flushed, so the open flushes the
newest segment before it builds anything on it: a commit index, a
checkpoint, an answer. The batch sweeps found this: a reopened dataset took a
commit whose flush had failed, a checkpoint covered it, and after a power
cut the checkpoint survived and the commit's records did not, so the log
refused to open. The same fsyncgate behaviour limits what the open's flush
proves: after a failed `fsync`, a later one on the same file in the same
boot may report success for pages the system dropped. The open does what
can be done in-process, and an outcome-unknown commit stays unknown until
the commits resource shows it either way. A full disk avoids the question:
the next write's recovery fences such a commit off instead of adopting it.

When the log cannot be read back after the failure, nothing more is applied:
every member past the head is answered by whether it was closed, and the
dataset waits for a reopen.

Any other failed append keeps ADR 0124 point 3: the dataset takes no more
writes until it is reopened. A bulk load that meets a full disk is refused the
same way, and the next write or bulk load recovers.

A full disk *during recovery*, at every allocation of the open, fails the
open, and the next open after space returns succeeds.

**3. A real process, killed at every flush.** The child is the store's own
test assembly, run as its own host with a kill point: it runs the workload on
`FileStorage` over a `DiskFileSystem` wrapper that counts flushes, and at
flush *n* kills its own process, just before the flush and, in a second run,
just after it. The parent then opens the real directory and checks it. A
reference run counts the workload's flushes, so every one is a kill point.
No new project is needed, and nothing is added to `src/`.

**4. A power cut on real files.** A wrapper over `DiskFileSystem` mirrors
every operation into a `SimulatedFileSystem` journal while it performs it on
the disk. At the cut it closes every handle and replaces the directory with
the image the journal gives under each of ADR 0073's models (power loss, and
lost directory entries): the flushed bytes, each unflushed write kept,
dropped or torn as in point 1, and the metadata journal cut where the model
allows. The store then opens the directory through the real
`DiskFileSystem`. The real code paths are exercised, and the model decides
what survives.

**5. What each fault must leave.** After every fault:
- the store opens;
- every position up to the last acknowledged commit reads back exactly,
  pinned and as of, against the history of a run that never failed;
- no position beyond what was durable is visible. A commit that was durable
  but not acknowledged may be there; that is correct per the spec, and
  "uncommitted" means never closed;
- a new commit continues and survives a reopen.

**6. Where it runs.**
- **The release gate** is a reusable workflow, `release-gate.yml`.
  `release.yml` calls it at the commit under release, beside `ci.yml`, and
  cuts nothing unless both pass. It runs points 1 to 4 at full depth on
  Linux and on Windows, with the soak of ADR 0215.
- **The nightly's store job** runs the same sweeps at full depth every
  night, beside the land gate's sweeps it already runs (docs/testing.md
  section 8). Its weekly Windows soak calls `release-gate.yml` with the
  faults off.
- **The land gate keeps a strided share,** as section 8 sets out for every
  sweep: each property runs a seeded share on a pull request.

The plan put the release gate in a job of `ci.yml`. A job of one workflow
can't be called from another, so it is a workflow of its own, called by the
release and by the nightly.

## Alternatives considered

- **A reopen after a full disk,** as ADR 0124 point 3 does for any failed
  append. It needs an operator, or a restart, to notice the space is back.
  The next write can do what the open does, and it is the first moment
  anyone needs the writer.
- **A timer that retries the recovery.** It would write when nobody asked,
  and a write request already arrives when the writer is needed.
- **`503 unavailable` for a full disk.** A client cannot tell it from a
  dataset that is closing or broken, and readiness would have to say the
  server is unready while reads go on.
- **A separate host project for the kill.** It would need `InternalsVisibleTo`
  for `FileStorage.Open`, which ADR 0062 allows only toward test assemblies,
  and a second build. The test assembly can host itself.
- **Fault injection with a FUSE file system or `LD_PRELOAD`.** Linux only,
  outside the BCL, and a new dependency each. The seam already exists.
- **Sampling the power-loss images,** as today. The bound on the number of
  unflushed writes at a point is small enough to enumerate.

## Consequences

- The release gate takes longer: the faults at full depth, and the soak
  (ADR 0215). The land gate does not.
- A full disk is a documented outcome: the write is answered `507
  storage-full`, reads go on, readiness reports `writesRefused`, and the
  first write after space returns proceeds. The operator guide says so.
- A failed batch's members are answered by what the log holds, not all as
  failed. A member written before the failure is committed, as a reopen
  would find it.

## Checks

- **Checked against the accepted ADRs.** Touches **0073** (amended), **0124**
  (point 3 for a full disk, and `AcknowledgedOnlyWhenTheBatchIsDurable`,
  superseded in part), **0119** (amended: `storage-full`), **0062** (no new
  `InternalsVisibleTo`) and **0088** (the land gate unchanged).
- **Layer ownership.** The store owns the recovery and `StorageFull`; the
  protocol owns the `507` answer; the server owns readiness. The faults are
  tests and workflows.
- **Analyzer rule.** None.
- **Open questions owned.** None.
