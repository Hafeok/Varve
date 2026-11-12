# Memory bounds for writing, checkpointing and reopening

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#227](https://github.com/Hafeok/Varve/issues/227); worked together with [#222](https://github.com/Hafeok/Varve/issues/222), which has its own record and pull request; [#255](https://github.com/Hafeok/Varve/issues/255) and [#256](https://github.com/Hafeok/Varve/issues/256) opened |
| **Date** | 2026-11-10 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app |
| **Model** | `claude-fable-5-2`, the session's model and the one last served, from the session's own metadata; the session was configured for `claude-opus-5-5` |
| **Session identifier** | `session_01NbWSYPHhKjTr9XFCti1cCs` |
| **Branch** | `claude/elegant-brahmagupta-h086d2-227`, from `main` at 2f7573d |
| **ADRs** | [0244](../adr/0244-memory-bounds-for-writing-checkpointing-and-reopening.md) (filed), [0070](../adr/0070-the-storage-engine-is-our-own.md) (superseded in part), [0110](../adr/0110-the-shipped-runtime-configuration.md), [0139](../adr/0139-extents-and-the-projection-catalog.md) and [0164](../adr/0164-the-tier-1-follower.md) (amended) |

## The prompts

### The task

> Read AGENTS.md. Work #222 and #227 together, as one PR per issue. Start by measuring: attribute the resident memory in each reproduction to its owners, using a heap snapshot and an allocation trace, before proposing anything. ADR 0082’s accounting is what’s being questioned, so any change to what counts as “the dataset’s own” is a dated amendment or a superseding ADR. #227 also owes docs/operator/small-machine.md a figure per dataset for writing and for reopening. Post the measurements and your plan on both issues before building.

### During the build

> Good work on both. Three things before the pull requests.
>
> 1. #227, the superseded runs on disk. State on #227 what bounds them on a server that never reopens: do tier merges keep them at about one extra copy of the derived data, or do they grow with each checkpoint? Measure it over several checkpoints in one process and post the disk figures.
>    * If bounded: put the disk figure in `small-machine.md` beside the memory ones, restate the gate honestly (memtable empty after a checkpoint; superseded runs retired at the next open), and open an issue for retiring them without a reopen, linked from #227.
>    * If it grows without bound: stop and say so before going further, because that needs a different design.
> 2. #222, the judge. […]
> 3. Open the issue for the graph-version index now (64 bytes per named graph, resident, paged by nothing), with your measurements, linked from #227.
>
> For point 4 of your plan, check whether a documented runtime setting (`GCConserveMemory`, or the collector’s retain-VM setting) gives the idle release before adding a forced collection in `Varve.Server`. Prefer the setting if it’s enough.

> Good results on #227, and #255 and #256 are right. Two things before the pull requests.
>
> 1. #222’s soak. […]
> 2. `ReleaseMemoryAfterCheckpoint`. Measure the pause the aggressive collection causes, and p99 write latency across a checkpoint with it on and off, on the 1.75M-quad workload. Then make it run only when the dataset has had no write for a stated interval after the checkpoint, unless the measured pause is small enough to state as acceptable. Put the figures in ADR 0244 and `configure.md`.
> 3. Post the disk figures for the superseded runs on #227 itself, as you noted.

The elided points are #222's and are in its record.

## The report

**Measured first**, and posted on the issue before anything was built.
- The reproduction: a generated import in the shape and size of
  `varve-demo`'s (its snapshot asset is not published, and the session did
  not build that repository's tool), against a server built from this
  checkout: Native AOT for resident memory and the limit, the JIT for heap
  dumps and allocation traces.
- The issue's figures reproduced, to the refusal's message under a 512 MB
  cgroup.
- Process dumps walked from their roots with ClrMD, each object charged to
  the Varve types that hold it; `GCAllocationTick` traces with stacks.
- Found: most resident memory is uncollected, not live; the default
  memtable does not fit a 512 MB container; nothing holds a writer to the
  memtable's limit; a checkpoint holds its window's terms and leaves the
  memtable; an open keeps every commit it replays, through a closure, and
  replays into an unbounded memtable.

**What was built.**
- A commit waits for the flush once the memtable holds twice its limit.
- An open replays through the limit, keeps 8 MiB of the scan's bodies and
  none afterwards.
- A checkpoint spills its window's terms and has the memtable flushed.
- The server's default memtable follows the collector's heap limit.
- The server releases memory after a checkpoint once the dataset has been
  idle for five seconds.
- `Varve.Benchmarks --import-memory`, the reproduction without the scripts.

**After**, Native AOT, the same import: it completes under 1 GB, 512, 384
and 256 MB with no write refused and nothing killed, peaks at 214 to 317
MB, is 107 to 116 MB idle after its checkpoint and 105 to 112 MB after a
restart. Killed at 1.75 million quads under 512 MB, it reopened under the
limit in 0.4 s.

**Changes of plan, each said on the issue.**
- A checkpoint flushes the memtable; it does not stand the projection on
  the new set. The rebase was built and withdrawn: it changed what
  `derived/` holds, which the format fixture pins, and left retired sets
  held. The runs it would have retired are bounded at one extra copy on
  disk, measured over thirteen checkpoints, and are #256.
- The idle release is a host act with a setting, not a runtime setting:
  `ConserveMemory` 9 and an ordinary forced collection release nothing, an
  aggressive one takes 372 MB to 155 MB. It waits for an idle dataset
  because under a writer it frees nothing and the longest commit beside it
  was 136 ms against 24 ms.

**Mistakes found and corrected in the session.**
- The first limit runs at 256 MB and 1 GB put the server into its cgroup
  after it had started, so the runtime could read the machine's limit. They
  were not quoted; every figure above is from a server started inside its
  cgroup.
- An intermittent failure of `FilePropertyTests` was the test harness, which
  opens a second dataset on the first's storage while the first's
  maintenance, now started by a checkpoint, was writing a run. The harness
  waits for maintenance first.
- A disk-full event in the session's container spoiled one 512 MB run,
  which was rerun.

**Renumbered, 2026-11-11.** This ADR was filed as 0241. `main` accepted
another ADR under that number (a commit message never names a model) before
this landed, so it is ADR 0244 from the merge of `main` on, in its file
name, its decision set and every reference. The commits pushed on
2026-11-10 stand as pushed: their messages say 0241 and their co-author
trailer names a model, which ADR 0241 refuses for commits made after it.
The commits from the merge on carry the trailer it allows.

**The pipeline, 2026-11-11 and 12.** `eng/ci.cs` stops at `build` on
`CS0618` alone. With `CS0618` not treated as an error for the run, each of
its 64 jobs passes on the head pushed on 2026-11-10, and the gates, the
build and the store, server, protocol and SPARQL-store suites pass on the
merge with `main`. `AllocationTests.a_solution_costs_the_evaluators_row_and_nothing_else`
failed once in 47 runs of the protocol suite, 55 bytes a solution against
56; it is not attributed to this change and is filed as its own issue.

**What the session could not show.**
- The container job the plan named, the AOT image under `docker run
  --memory 512m` in CI: there is no container runtime in the session. The
  cgroup runs stand in for it, and `--import-memory` is what such a job
  would run. It is not built.
- Object storage, followers and Windows.

**Unaccepted decisions the code cites** (ADR 0066): `MemoryBounds`' rulings,
`MaintenanceOffTheSequencer` among them, moved from ADR 0070's set and
restated.
