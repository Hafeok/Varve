# 0158 — Background merges of extent sets: only the writer merges, and a merge rebases on the catalog

## Status

**Accepted — filed unaccepted by milestone Extent maintenance of #110,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Extent maintenance plan,
2026-10-19:
- only the writer merges, and there is no separate merge worker;
- a taken name takes the next sequence;
- uploads are staged through a file in the cache;
- a merge past the single-request limit is refused before it writes anything.

**Supersedes in part** [ADR 0139](0139-extents-and-the-projection-catalog.md)'s
deferral of merges to this milestone, and amends
[ADR 0138](0138-validity-intervals-in-the-default-projection.md)'s
consequence that "`Keep` merges by dropping", by dated amendment to each.

Records decision set [`background-extent-merges`](../decisions/background-extent-merges.md).

## Context

A checkpoint adds one extent set to the chain (ADR 0138). Every read of the
present merges the projection's runs, whose base after an open is the chain,
and an as-of read at or below the newest set's end reads the chain up to it.
Object storage (#92) measured the cost at REC size M, on files, against the
number of sets:

| Sets | Head: lookup p50 | Head: a subject's quads p50 | Head: exact count p50 |
|---:|---:|---:|---:|
| 10 | 42.0 µs | 5.7 µs | 28.3 µs |
| 100 | 98.7 µs | 78.1 µs | 354.0 µs |

A drop already merges a set into the next one, from the two sets' extents
alone. That merge preserves every interval, so a read at a position inside
the merged window narrows to the same delta as the two sets gave apart. What
#92 did not build is the merge that runs on its own. Until now the chain grew
by a set per checkpoint, and the operator guide set a budget of 20.

## Decision

**1. Only the writer merges, in the dataset's maintenance.** A merge is one
step of the maintenance round, off the sequencer (ADR 0070), after the
checkpoint step. The backend starts nothing of its own (ADR 0042). A
read-only open, a follower, and a writer that finds itself superseded never
merge: a merge that meets `WriterSupersededException` turns merges off for the
dataset's lifetime. There is no separate merge worker. Nothing measured asks
for one: a merge reads its inputs through the page cache and streams its
output, holding the sorts' buffers and never a set whole (point 6).

**2. A merge writes, then swaps once.**
- It writes its set under a fresh name by create-if-absent (ADR 0139). That
  is nine blobs: six order extents, the dictionary extent, the graph-version
  index and the commit index (ADR 0161).
- It swaps the catalog once, by replace-if-unchanged under the epoch fence
  (ADR 0136).
- A superseded writer's swap is refused, so its set is never named, and the
  deletion rule deletes it (ADR 0160).
- The writer announces the catalog version it is about to swap to before it
  swaps (ADR 0160).

**3. A merge never blocks a commit or a checkpoint.** It chooses its inputs
from the chain as it stands and takes a reference to each input's blobs. It
reads and writes holding neither the sequencer nor the checkpoint gate, and
takes the gate only for its swap. A checkpoint that comes meanwhile waits for
nothing, and it wins.

**4. The swap rebases.**
- **Kept.** The merged set replaces its inputs only if every input is still
  in the chain, contiguous and in order. Every set added since stays: a
  checkpoint above the chain appends one and is kept.
- **Abandoned.** If an input is gone, the merge is abandoned:
  - a checkpoint inside an input's window splits it;
  - a drop merges or removes it.

  What the abandoned merge wrote was never named, so the deletion rule
  deletes it at once (ADR 0160: a set no catalog version named is held by no
  record).
- **Not retried.** An abandoned merge is not retried on the same inputs. The
  next round chooses again from the chain as it then stands.

**5. A merge is a pure function of its inputs.**
- **Its rows.** For each order, the inputs' rows are merged as adjacent
  windows: each older window's open interval meets the next one's closing
  interval for the same quad and becomes one interval. Every other interval
  is kept exactly. This is ADR 0138's drop, folded over `F` inputs.
- **Its dictionary** is the inputs' term sections in order. Its graph-version
  index is the newest input's. Its commit index is the inputs' end to end.
- **No other effect.** It coalesces no history and drops no retracted
  interval, so a read at any position over the merged catalog equals the
  same read over the unmerged one. Compaction that drops history is not this
  ADR's (ADR 0015).

**6. The projection's base is rebased in the same swap.**
- Under the sequencer, if the projection's runs hold the inputs side by side
  among their frozen runs (ADR 0070) — as they do after an open, whose base is
  the chain — the swap replaces them with the merged set.
- The persisted state is then rewritten. A read of the present merges the
  fewer runs at once, not after the next open.
- A flush that read the runs before the swap finds them changed, and is
  discarded and done again (ADR 0070's rule).

**7. A taken name takes the next sequence.**
- A create-if-absent that finds a set's blob already there, with other bytes,
  means a superseded writer wrote that name. The set is written again under
  the next sequence, up to eight times.
- What the taken attempt left is never named, and the deletion rule takes it.
- Equal bytes are this writer's own earlier attempt, as before
  (ADR 0135, `LostResponseReadsBack`).
- A superseded writer's creates, checkpoints and merges therefore never fail
  the writer's checkpoint or merge.

**8. Uploads are staged through a file in the cache.**
- A derived blob bound for the store is written to a file under the cache
  directory's `staging/`, then uploaded streamed from it.
- An extent's staged file then becomes its page-cache file, every page held,
  so the extent just written enters the cache without a copy.
- The catalog, small, is replaced from memory.
- `IObjectStore` gains a create that streams `ObjectContent` from a file
  (point 9).

**9. A merge past the single-request limit is refused before it writes.**
- One Put Blob holds at most 5,000 MiB on Azure Blob Storage, and one
  PutObject 5 GiB on Amazon S3. `ObjectStorageOptions.MaxObjectBytes` says
  which applies, and the server sets it from the provider.
- A merge whose order extent, or dictionary, could pass that is refused
  before it writes anything. An output blob is bounded by its inputs' blobs
  of the same order together, plus a MiB.
- A refused merge is not tried again on the same inputs.
- The default cap on a merged set (ADR 0159) is far below either limit.
  Multipart upload is a later decision.

## Alternatives considered

- **A merge worker apart from the writer.** It would need the epoch to swap
  the catalog, and two processes holding one epoch is what ADR 0136 rules
  out. A worker that writes extents and asks the writer to swap them needs a
  protocol between them. Rejected: nothing measured asks for it.
- **A merge that holds the checkpoint gate throughout,** as a drop does.
  Simpler, with no rebase. But a checkpoint, which the operator or the
  policy asks for, would wait behind a merge of the oldest and largest sets.
  The prompt rules that a merge never blocks a checkpoint.
- **Retry a merge whose input is gone.** The next round chooses again from
  the chain as it then stands, which is the same thing without a loop.
- **Swap the projection's base only at the next open.** A head read would
  keep merging the old runs until a restart, and the latency the merges
  exist to win would show only after one.

## Consequences

- `Dataset` gains `ExtentMerges` in `DatasetOptions`, and `CatalogVersion` and
  `Retention`. The server configures merges under `Varve:Store:ExtentMerges`.
- `Varve.Store` gains `IDerivedStoreLimits`, `DerivedNameTakenException`,
  `ObjectContent`, and the streamed create on `IObjectStore`.
  `Varve.Store.Azure` and `Varve.Store.S3` stream it and state their
  single-upload limits.
- A set's name sequence and the catalog version share one counter
  (ADR 0160). Every swap advances it, so sequences have gaps.
- The checkpoints `Dataset.Checkpoints` lists, and the admin API's status,
  are the sets' ends. A merge removes the ends inside its window. No answer
  changes, but an as-of read inside a merged set narrows a larger set.
- `T.Options()` in the store's tests turns merges off, because those suites
  state what the checkpoints are, and several open a second dataset over one
  memory storage, which one process may write. The merge suites turn them on.

## Checks

- **Checked against the accepted ADRs** (0001–0147). Touches:
  - **0015**: no compaction drops history; a merge drops nothing.
  - **0042**: the backend starts nothing.
  - **0070**: off the sequencer, and a stale result is discarded.
  - **0134**: a sixth member of `IObjectStore`, the same create streamed.
    Amended.
  - **0136**: the epoch fences the swap.
  - **0138**: the drop's merge, generalised.
  - **0139**: names never reused; the deferral superseded.

  No conflict.
- **Layer ownership.**
  - The merge, the rebase and the policy: `Varve.Store`, layer 4.
  - `IDerivedStoreLimits`, `ObjectContent` and `DerivedNameTakenException`:
    `Varve.Store`, layer 4.
  - Staging and the limit: `Varve.Store.ObjectStorage`, layer 5.
  - The streamed create: `Varve.Store.Azure` and `Varve.Store.S3`, layer 5.
- **Analyzer rule.** None.
- **Tests.**
  - `ExtentMergeTests`: generated histories with retractions under any
    schedule of checkpoints, drops and merges equal the model at every
    position and at the head, before and after an open from the log and the
    extents alone; a checkpoint during a merge is kept or abandons it; the
    projection's base is rebased.
  - `ObjectExtentMaintenanceTests`: a taken name takes the next sequence and
    is never named.
  - `ObjectReaderScheduleTests`: a superseded writer's merge swap is refused.
