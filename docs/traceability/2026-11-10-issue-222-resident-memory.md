# Resident memory per commit: measured, attributed and bounded

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#222](https://github.com/Hafeok/Varve/issues/222); worked together with [#227](https://github.com/Hafeok/Varve/issues/227), which has its own record and pull request |
| **Date** | 2026-11-10 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app |
| **Model** | `claude-fable-5-2`, the session's model and the one last served, from the session's own metadata; the session was configured for `claude-opus-5-5` |
| **Session identifier** | `session_01NbWSYPHhKjTr9XFCti1cCs` |
| **Branch** | `claude/elegant-brahmagupta-h086d2`, from `main` at 2f7573d |
| **ADRs** | [0240](../adr/0240-resident-memory-is-bounded-by-data-and-configuration.md) (filed), [0082](../adr/0082-the-soak-gate.md), [0089](../adr/0089-the-commit-index-is-derived-and-paged.md) and [0177](../adr/0177-seek-dont-narrow-intervals-beside-the-key.md) (amended) |

## The prompts

### The task

> Read AGENTS.md. Work #222 and #227 together, as one PR per issue. Start by measuring: attribute the resident memory in each reproduction to its owners, using a heap snapshot and an allocation trace, before proposing anything. ADR 0082’s accounting is what’s being questioned, so any change to what counts as “the dataset’s own” is a dated amendment or a superseding ADR. #227 also owes docs/operator/small-machine.md a figure per dataset for writing and for reopening. Post the measurements and your plan on both issues before building.

### During the build, on the judge

> 2. #222, the judge. The allowance goes down from today’s 60 B per commit, to what the one-hour soak measures plus stated noise. Post the before and after figures on #222. If the soak suggests an allowance looser than today’s, report that instead of adopting it.

The same message had points for #227, which are in its record.

## The report

**Measured first**, and posted on the issue before anything was built.
- The soak's own artifact (nightly 41450438151): the live heap grows 36.5
  bytes a commit and the extent sets' directories 36.2.
- The soak's workload without its pause, stopped every 250,000 commits for a
  `dotnet-gcdump` and a process dump. Each dump was walked from its roots
  with ClrMD and every object charged to the Varve types that hold it. One
  owner grows, 59 bytes a commit: each extent set's seek directory.
- An allocation trace (`GCAllocationTick` with stacks): 232 MB a second, a
  third of it the memtable's tier merges, none of it retained.

**What was built.**
- A format-5 extent's seek directory is hashed when its set is opened and
  not kept. A read takes it from a cache bounded in bytes
  (`DatasetOptions.SeekDirectoryCache`, 32 MiB), checked slice by slice
  against a check taken at the open.
- A commit index blob's fences are not held; `PositionAt` reads the blocks'
  first entries.
- The server's setting and gauge, the soak's `--pause` and `--seek-cache`.
- No format changes.

**After**, same harness: the live heap is 7.5 to 11.8 MB at every snapshot
to 1,000,000 commits, where it was 44 to 147 MB.

**A correction made on the issue.** The first table's memtable column was
the memtable in flight: the harness blocked a pool thread inside the last
commit's continuation and so kept the sequencer's previous state reachable.
With the harness fixed the dataset at rest holds its memtable within the
limit. A sampler then measured the memtable in flight at up to 480,000
entries against a limit of 20,000, which is #227's finding.

**The judge's allowance is not lowered.** The one-hour soak at a 1 ms pause
measured 139 bytes a commit of working set, looser than today's 60: its
writer outruns the flush, the seek cache was still filling, and the machine
was shared. That was reported on the issue instead of adopted, as asked.
`AllowanceBytesPerCommit` stays 60, ADR 0215 is not amended, and the figure
waits for a four-hour soak at the gate's pace, which this session could not
run.

**What the session could not show.**
- The gate's second half: the four-hour soaks on Linux and Windows.
- Windows at all.
- The cost of an as-of read that misses the cache, beyond the tests' count
  of directories read.

**Unaccepted decisions the code cites** (ADR 0066): `ResidentMemory`'s four
rulings, and `SeekingExtentSets.SeekDontNarrow`, restated.
