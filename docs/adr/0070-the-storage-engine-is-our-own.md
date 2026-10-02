# 0070 — The storage engine is our own, and it is two different things

## Status

**Proposed — filed unaccepted by session 6a of #10, 2026-10-02** (ADR 0066).
The maintainer decided the shape on the 6a plan; acceptance is the
maintainer's act on the pull request.

Decides the open row of `docs/brief.md`'s tensions: "Managed storage engine for
the projections: write our own LSM or B+tree over the log, or adopt a managed
engine." The research note
[`managed-storage-engines.md`](../research/managed-storage-engines.md) narrowed
it; this closes it.

**Revisit condition:** a workload at milestone 7 where tier merges cannot keep
up with the commit rate — the memtable grows without bound, or a commit waits
on a merge — supersedes this ADR.

## Context

The research note's first point is the one that decides most of this: the
design needs **two different things**, and only one of them is a key-value
store.

- **`log/`** is the source of truth (§3). It is appended to, read in order and
  read by position, and never rewritten (§2, ADR 0015). It has no keys, no
  updates and no lookups. An engine's defining behaviour — merge runs and drop
  what is superseded — is exactly wrong for it.
- **`derived/`** holds the default projection and checkpoints. ADR 0041 already
  made both *immutable sorted runs* in six key orders, merged in tiers, and
  scanned by one cursor. In memory that design is complete; what it lacks is a
  disk.

And Varve already has, one layer up, the part of a storage engine that is
hardest to get right. The log is durable, ordered, hash-chained and never
rewritten; every projection persists its position atomically with its state and
can be rebuilt by replay (ADR 0016, I8). An engine's own write-ahead log,
recovery procedure and crash-consistency protocol between its log and its data
files would be a second source of truth beside the first.

The candidates (verified by the note on 2026-09-21) were ZoneTree 1.9.8,
Microsoft.FASTER.Core 2.6.5, Tsavorite, LiteDB 5.0.21 and DBreeze 1.138.

## Decision

**No managed engine is adopted. `log/` and `derived/` are written by
Varve, as two things.**

### `log/` is an append-only segment writer

Segments under `log/`, records appended to the newest, a seal when it is full
(ADRs 0013, 0018, 0040). **No keys, no index, no compaction.** Finding a commit
by position is the store's table of commit locations, built on open; finding a
quad is the projection's job, never the log's. Its byte layout is ADR 0072's.

### `derived/` is immutable sorted runs with the log as its write-ahead log

The default projection is ADR 0041's list of runs, now in two tiers:

- **the memtable**: the newest runs, in managed arrays, built from each
  commit's delta and merged in tiers inside the sequencer exactly as ADR 0041
  does — cheap, because they are small;
- **disk runs**: when the memtable holds more than `DatasetOptions.MemtableLimit`
  quads it is **flushed** — merged to one run and written as a derived blob —
  and disk runs are merged in tiers by the same geometric rule, each merge
  writing a new run.

**A disk run is the same run.** The same six orders, asserted and retracted
keys, the newest-decides merge, the same cursor: a run's keys are read through
the synchronous blob read of ADR 0071 in blocks, and the cursor walks blocks
instead of an array. Nothing about ADR 0041's semantics changes.

**The projection's persisted state is one blob** naming its disk runs and the
position they reach, replaced atomically (ADR 0072). That is ADR 0016's "position
persisted atomically with state", with no transaction spanning two files.

**There is no write-ahead log in `derived/`.** On open the store loads the
persisted state if it names this log (dataset id, and the header hash at its
position), then rebuilds the memtable by replaying the log from that position.
A missing, torn, stale or foreign state is a cache miss: the store rebuilds from
the newest valid checkpoint, or from position 0. A torn write under `derived/`
is a rebuild, never data loss.

### Maintenance runs off the sequencer

Flushes, disk merges and checkpoint writes are **maintenance**: work on derived
data that no commit depends on. When `DatasetOptions.Maintenance` is
`Background`, the dataset runs it on a task it owns, off the sequencer, and
**never blocks a commit on it**: a finished merge publishes a new version only
if the runs it replaced are still the current ones, and a run a pinned read
holds is deleted only after the pin is released. `Dataset.MaintainAsync` runs
one round on the caller's schedule, for hosts that run none in the
background. The default is `Background`, except in a browser, where it is
`Off` until 6b decides; ADR 0042 is amended to say why this is not the
registry it declined.

## Alternatives considered

- **ZoneTree** for `derived/`. The closest fit: managed, current, an LSM over
  sorted runs. Rejected on the note's deciding question: it **owns its own file
  I/O**, with no seam where the storage abstraction could be substituted. The
  memory backend would become a second engine, the browser backend impossible,
  and ADR 0018's contract a thing ZoneTree sits beside rather than on. Its
  compaction would also have to be kept away from `log/` by convention, and its
  write-ahead log would be a second one. Two transitive packages
  (`K4os.Compression.LZ4`, `ZstdSharp.Port`) for capabilities we would not use.
- **FASTER / Tsavorite.** Built around a **hash index** over a hybrid log, for
  point operations. RDF reads are ordered prefix scans over dictionary-encoded
  keys in six orders, which a hash model does not serve; ordering would be built
  on top, which is most of the work. FASTER's package stops at net7.0;
  Tsavorite is not published alone, and the package that ships it carries
  native assets (constraint 1).
- **LiteDB, DBreeze.** Document store and B+tree written to a pre-`Span`
  common denominator; neither fits fixed-width sorted keys, and both would own
  the files.
- **A B+tree of our own for `derived/`.** Updates in place, which fight R1's
  pinned reads (ADR 0041 rejected a mutable index for this reason) and turn a
  torn write into a corrupt page rather than a missing file.
- **No disk runs — memory only, with checkpoints as the only persistence.**
  Simplest, and correct. Rejected as the design: open would replay the whole
  tail since the last checkpoint into memory every time, and the projection's
  size would be bounded by memory.

## Consequences

- **Durability, crash recovery and merge scheduling are ours**, and that is
  where storage engines fail months later. The failure-injection suite of
  milestone 6a is the mitigation and is a gate, not a report.
- **No package is added.** The engine is AOT-safe because it is written so, and
  it runs over the storage abstraction, so the browser backend in 6b is an
  implementation of ADR 0018's contract rather than a port of an engine.
- **Scan cost grows with the number of runs**, memtable and disk together;
  tiering bounds both to `O(log n)`. A dataset that has just absorbed many
  commits scans more runs until maintenance catches up — which is what the
  revisit condition watches.
- **The term dictionary stays in memory** in 6a, rebuilt on open from the
  newest checkpoint and the log after it. A dictionary on disk is on 6b's list.

## Checks

- **Checked against the accepted ADRs** (0001–0069) and specification 1.4.
  Touches **0015** (no compaction of the log; merging runs is derived data),
  **0016** (the persisted state is the position atomically with state; I8),
  **0018** and **0040** (the engine sits on the contract), **0041** (runs, orders,
  tiers and the cursor are unchanged in meaning), **0042** (amended, for
  maintenance), and **0071** and **0072** (how a run is read and laid out). No
  conflict with any.
- **Layer ownership.** `Varve.Store`, **layer 4**. Nothing public but
  `DatasetOptions.MemtableLimit`, `DatasetOptions.Maintenance` and
  `Dataset.MaintainAsync`.
- **Analyzer rule.** None. The disk-run cursor's `MoveNext`, block refill and
  fence search are `[HotPath]` (ADR 0026).
- **Open questions owned.** None. Closes the brief's storage-engine tension.
