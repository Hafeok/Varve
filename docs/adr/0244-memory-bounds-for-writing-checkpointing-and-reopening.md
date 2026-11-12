# 0244 — Memory bounds for writing, checkpointing and reopening

## Status

**Accepted — filed unaccepted by #227, 2026-11-10** (ADR 0066). Acceptance is
the maintainer's act on the pull request.

**Amends** by dated amendment:
- [ADR 0070](0070-the-storage-engine-is-our-own.md), which it **supersedes
  in part**, its revisit condition being met: a commit may wait for a flush,
  an open flushes, and so does a checkpoint. `MaintenanceOffTheSequencer`
  moves to this ADR's set, restated;
- [ADR 0110](0110-the-shipped-runtime-configuration.md): the server's default
  memtable follows the heap limit;
- [ADR 0139](0139-extents-and-the-projection-catalog.md): a set written
  from the log spills its window's terms;
- [ADR 0164](0164-the-tier-1-follower.md): what the default means for a
  follower's re-open.

No format changes.

Records decision set [`memory-bounds`](../decisions/memory-bounds.md).

[ADR 0240](0240-resident-memory-is-bounded-by-data-and-configuration.md)
(#222) says what a dataset holds at rest. This one bounds what it holds
while it works.

## Context

#227: a served dataset's resident memory grows with what the server has
written, a checkpoint raises it and does not release it, and under a
container limit the server refuses writes and then cannot reopen the
dataset. Nothing acknowledged is lost. The operator's guide gives no figure.

**Measured** (the issue's comment of 2026-11-10 has the method and the
tables). The workload is `varve-demo`'s import in shape and size: 3,000
commits of about 1,000 quads each into 512,390 small named graphs, then
1,731 small commits; 3.06 million quads.

| Point, Native AOT, nothing attached | Resident |
|---|---:|
| start | 51 MB |
| end of the import (peak) | 307 MB (615 MB) |
| after the checkpoint | 932 MB |
| 60 s later, idle | 931 MB |
| after a restart | 129 MB |

Under a 512 MB cgroup, commit 674 is refused with `OutOfMemoryException`
from the default projection at 422 MB resident, and the restarted server is
killed by the kernel in under three seconds while it opens the dataset.
Without the limit it opens at 602 MB.

**Where the memory is**, from process dumps attributed by owner and
allocation traces:

1. **Most of it is not live.** The live heap is 99 MB at the end of the
   import and 108 MB after the checkpoint. The rest is what the collector
   has not collected: the import allocates 243 MB a second, 59% of it large
   objects, and after a checkpoint nothing allocates, so nothing collects.
2. **The memtable costs 192 bytes a quad and about 96 a term**: six index
   orders of 32-byte keys. Its limit is 1,000,000 entries by default, and a
   tier merge builds the merged run beside its inputs. At the refusal it
   held 153 MB, just short of its first flush, under a heap limit of 384 MB.
   A checkpoint does not flush it, so checkpoints did not help.
3. **Nothing bounds it against a fast writer.** Maintenance flushes off the
   sequencer and a commit never waits for it (ADR 0070). #222's harness
   held about 350,000 entries in a memtable limited to 20,000.
4. **A checkpoint holds its window's terms**: every `Allocation` of the
   window, then one term section built from them. 1,212,574 terms were 269
   MB as objects and 118 MB as the section; the live heap reached 490 MB.
5. **A checkpoint leaves the memtable and the projection's runs in place**,
   54 MB here. An open on the same files stands the projection on the
   checkpoint's set and holds 8 MB.
6. **An open reads every commit above `derived/` into memory before it
   applies the first**, 2.9 times their bytes in the log; keeps them for the
   dataset's lifetime, because the commit index reads through a closure that
   captured the opener; and replays them into a memtable that no flush
   bounds, at 285 MB a second of allocation. Reopened, the 704-commit
   dataset held 248 MB live where its writer had held 168 MB.

## Decision

**1. A commit waits for the flush at twice the memtable's limit.**
- With `MaintenanceMode.Background`, a commit that arrives while the
  memtable holds `WaitBoundFactor` × `MemtableLimit` or more, two times,
  waits before it queues. Maintenance starts a flush at the limit, as it
  always has, so a writer no faster than the flush never waits.
- It waits for maintenance's next step, and looks again. No timer: every
  step of maintenance and its end wake the waiters.
- **It does not wait where waiting could not end**: with maintenance off,
  where the host flushes; while spills fail, where the writer goes on and
  the spill bound at four times decides (ADR 0179 point 6); when
  maintenance has failed; when maintenance has run its steps eight times
  without shrinking the memtable; once the dataset is closing or its writer
  has stepped down.
- So a writer's memtable holds less than twice its limit and one commit. A
  wait is part of the commit's duration, as its place in the queue is.

**2. The open stays within the writer's bound.**
- It replays the log above `derived/` through the memtable's limit: once
  the memtable holds the limit, it is written to `derived/` as a run by the
  flush maintenance runs, and the replay continues above it. Only where the
  dataset runs its own maintenance, and never for a follower, which writes
  no run (ADR 0164).
- The scan keeps the bodies of the commits it reads for the replay only up
  to 8 MiB of log. The rest are read from the log again as they are
  applied: a second read, by the path a stale claim already used.
- Each body is let go when it has been applied, and the commit index reads
  through an object that holds the dataset and nothing else. Nothing of the
  scan outlives the open.

**3. A checkpoint holds buffers, not its window.** It holds at most about
16 MiB of its window's terms, by 224 bytes a term and its bytes. Past that
it writes them to a spill as a term section, reads the section back from
the spill, and writes the set's dictionary from the spilled sections and
the last, as a merge writes one from its inputs (ADR 0079). The spills are
deleted when the set is written, and with any other spill at the next open.

**4. A checkpoint releases what it makes unnecessary.** When a checkpoint
is published, a dataset that runs its own maintenance flushes its memtable
in the round that follows, whatever it holds. What the checkpoint covers is
then on disk twice, as the set and as the projection's runs, and in memory
not at all. The runs' key directories stay, 40 bytes per 128 keys, until the
next open stands the projection on the set. The second copy is bounded:
tier merges keep the projection's runs in one or two files the size of the
present state and the dictionary, 23.7 MB flat over thirteen checkpoints of
a 120,000-quad dataset whose terms repeat. Retiring it without a reopen is
#256.

**5. The server's default memtable follows the heap limit.**
`Varve:Store:MemtableLimit`, unset, is the smaller of 1,000,000 entries and
a sixteenth of the collector's heap limit at 215 bytes an entry, and at
least 10,000.
- The heap limit is `GCMemoryInfo.TotalAvailableMemoryBytes`: three
  quarters of a container's memory limit by default, or the machine's
  memory.
- The memtable holds between one and two times its limit, and a tier merge
  a second copy of what it merges, so a sixteenth keeps the memtable's
  worst case at a quarter of the heap.
- At 3.5 GB of heap or more the default is what it was. In a 512 MB
  container it is about 117,000 entries, 24 MiB.
- A value the operator sets is kept as set.

**6. An idle server returns what a checkpoint's work left.** Once a
checkpoint a caller asked for has been taken, `Varve.Server` lets the
dataset's maintenance finish and waits five seconds. If the dataset took no
commit in them, it asks the collector for one aggressive, compacting
collection. `Varve:Store:ReleaseMemoryAfterCheckpoint`, true by default,
turns it off. The checkpoint's answer does not wait for it, and the store
itself never forces a collection: this is the host's.

Measured, Native AOT, the import of the context:

| After the checkpoint, idle | Resident |
|---|---:|
| shipped settings (`ConserveMemory` 5) | 372 MB |
| `ConserveMemory` 9 | 322 MB |
| retain-VM | off by default already |
| a forced, compacting collection | 375 MB |
| an aggressive, compacting collection | 155 MB |

So no documented setting gives the release, and only the aggressive mode
decommits. Why it waits for the dataset to be idle, at 1.75 million quads
with one writer committing back to back across the checkpoint:

| | Release on, no wait | Off |
|---|---:|---:|
| resident 20 s after the checkpoint | 315 MB | 330 MB |
| p99 of a commit, during the checkpoint | 10.2 ms | 9.7 ms |
| p99, the 18 s after | 3.4 ms | 3.1 ms |
| longest commit, the 18 s after | 136 ms | 24 ms |

Under a writer it frees nothing, because a server that allocates collects
on its own, and it costs one commit about a tenth of a second. Idle, it is
the difference between 372 MB and 155 MB.

## Alternatives considered

- **Bound the memtable in bytes in the store.** The right unit, and a
  change to a public option every host sets in entries. The server converts
  at one documented rate instead, and the store's contract is unchanged.
- **Flush on the commit path at the limit**, so a commit never waits on
  another task. Then every commit that crosses the limit pays a flush, and
  the sequencer is held through it. Waiting at twice the limit costs
  nothing until the writer is a whole memtable ahead.
- **Keep the memtable in one order and build the other five at the flush.**
  A sixth of the memory, and a read of the present that cannot use five
  orders above the newest run. A redesign of the projection, not this fix.
- **Merge tiers without a second copy**, in chunks below the large object
  heap. It would cut the allocation rate that the collector's overhead
  follows. The bound in point 5 makes the copy fit; this is the next thing
  to do if the collector's share is still the larger part.
- **Force a collection after every flush.** It would hide the allocation
  rate at the cost of a pause in every write path.
- **Replay during the scan**, one pass, no bodies kept. The projection's
  base is known only after the scan, from the commit index it builds.
  Keeping a budget of bodies gives the common open its single pass and
  bounds the rest.
- **Stand the projection on the new set at once**, as an open does, and
  release the runs it repeats as well as the memtable. Built first, and
  withdrawn: it would free the runs' directories too, a few megabytes here,
  but it changes
  which blobs `derived/` holds after a checkpoint, which the format
  fixtures pin, and a set under the projection must then be found side by
  side by every later merge and drop. The flush frees the part that
  matters, with the machinery maintenance already has.

## Consequences

- **A dataset written under a limit can be reopened under it.** The open's
  peak is the writer's: the memtable's bound, 8 MiB of log decoded, and what
  both hold at rest.
- **A fast writer is slowed to the flush's pace** once it is a memtable
  ahead. On the import it costs little: 56.1 s before and 57.1 s after with
  no limit, where the default memtable is what it was, and 57 to 68 s under
  512 to 256 MB, where it is an eighth to a sixteenth of that.
- **An open that replays more than 8 MiB above `derived/` reads those
  commits twice.** It happens after a crash or a kill with a large memtable
  unflushed; a clean shutdown leaves little above `derived/`.
- **A small container flushes more often**, and a follower in one is
  re-opened sooner (ADR 0164 point 5).
- **`docs/operator/small-machine.md` states what a dataset needs**, to write
  and to reopen, from these bounds.
- **The graph-version index is not bounded here**: 64 bytes a named graph,
  33 MB at 515,207 graphs, resident at rest. It follows the data (ADR 0240)
  and is stated in the operator's guide. Paging it is #255.

## Checks

- **Checked against the accepted ADRs** (0001–0241; 0241, a commit message never names a model, is not touched). Touches:
  - **0011, 0124**: the sequencer and group commit are unchanged; the wait
    is before a commit queues.
  - **0070**: "a commit never waits for maintenance" becomes "waits only at
    twice the memtable's limit". Amended.
  - **0072, 0079, 0139**: no format change; a spill is a term section in a
    run of kind 7 under the spill prefix, deleted by the open's sweep.
  - **0089**: the commit index reads through to the dataset as before, by
    an object in place of a closure.
  - **0110**: the default memtable. Amended.
  - **0138**: unchanged: the projection stands on a set at the next open.
  - **0139**: amended.
  - **0164, 0180**: a follower's open writes nothing; its re-open bound is
    the same option. Amended for the default.
  - **0179**: the spill bound stands, and the wait defers to it.
  - **0213**: a spill is read back through the checked run format.
  - **0240**: adds nothing resident per commit.

  No conflict.
- **Layer ownership.** `Varve.Store`, layer 4; the default is the server's.
  `Dataset.WaitBoundFactor` is the one addition to the public API.
- **Analyzer rule.** None.
- **Tests.** `MemoryBoundsTests`, by the store's own counts:
  - a commit waits once the memtable holds twice its limit with the flush
    held, and is written when the flush has made room;
  - a writer with every flush slow never holds more than twice the limit
    and a commit;
  - nothing waits with maintenance off or while spills fail;
  - an open of forty commits above `derived/`, ten times the limit, never
    holds more than the limit and a commit, leaves runs in `derived/`, and
    answers the same with the bodies' budget at 8 MiB and at one byte; with
    maintenance off it writes no run;
  - a checkpoint with a term budget of 512 bytes equals the model at every
    position, before and after a reopen, over generated histories, and
    leaves no spill;
  - a checkpoint under background maintenance leaves the memtable empty
    and the answers what they were; with maintenance off it flushes nothing.
- **Measured.** The import of the context on the Native AOT server, nothing
  attached, the server started inside a memory cgroup (memory and swap) for
  each limit:

  | Limit | Import | Peak | After the checkpoint | Idle, 60 s later | After a restart |
  |---|---:|---:|---:|---:|---:|
  | before, none | 56 s | 615 MB | 932 MB | 931 MB | 129 MB |
  | before, 512 MB | refused at commit 674 | 422 MB | | | killed on reopen |
  | none | 62 s | 704 MB | 150 MB | 154 MB | 111 MB |
  | 1 GB | 72 s | 317 MB | 112 MB | 116 MB | 112 MB |
  | 512 MB | 57 s | 246 MB | 182 MB | 107 MB | 108 MB |
  | 384 MB | 59 s | 268 MB | 232 MB | 108 MB | 108 MB |
  | 256 MB | 68 s | 214 MB | 193 MB | 108 MB | 105 MB |

  No write was refused and the kernel killed nothing, at any limit. Under
  512 MB, killed with `kill -9` at 1.75 million quads and started again
  under the same limit, the server answered in 0.4 s at 91 MB with all 1,782
  commits. The runs shared the machine with a soak, so the times are
  comparable only roughly; the two with no limit and nothing else running
  took 56.1 s before and 57.1 s after.
- **Open questions owned.** None.
