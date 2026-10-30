# 0161 — The shared commit index: each extent set carries its window's commit index

## Status

**Accepted — filed unaccepted by milestone Extent maintenance of #110,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Extent maintenance plan,
2026-10-19: the optional ninth blob in derived format 4, and no format 5.

**Amends** [ADR 0139](0139-extents-and-the-projection-catalog.md):
- its cold start reads the commit index below the catalog from the store,
  and reads the log's headers there only for a set written without one;
- its "everything else under `derived/` stays local" no longer covers the
  commit index of a set's window.

By dated amendment.

Records decision set [`the-shared-commit-index`](../decisions/the-shared-commit-index.md).

**Superseded in part 2026-10-27 by [ADR 0213](0213-cached-derived-data-is-verified-before-use.md)**: `CommitIndexBesideEachSet` and `NoDerivedFormatChange` move to its set; a set's `commits` blob is written as derived version 6, with a check per block, when every announced reader reads 6. Filed unaccepted.

## Context

A cold start reads the log only after the catalog's cursor (ADR 0139).
Below the cursor it knows only the commit index entries the catalog pins,
one per set's end. The first read that needs another entry pays to read the
log's headers up to the catalog's position, keeping the result as a local
commit index blob:
- a feed from below the catalog;
- an as-of read below it, which needs an entry for its header hash;
- a lookup by time before it.

Every new instance pays that once: a follower, a writer on a fresh cache,
the command line. ADR 0139 named the cure, the commit index written to the
store beside the extents, and left it to this milestone.

## Decision

**1. Each extent set carries its window's commit index, as a ninth blob.**
- `extents/<from>-<to>.<n>/commits` holds the entries of the commits after
  `from` up to `to`, as a commit index blob: kind 4, in the layout it already
  has (storage format §7.4, ADR 0089). Its end hash is the log's header hash
  at `to`.
- A checkpoint writes it from the commit index. A merge writes the inputs'
  blobs end to end, and for an input written without one takes the entries
  from the commit index (ADR 0158).
- It is in the store with the set's other blobs, deleted with them by the
  deletion rule (ADR 0160).

**2. A cold start reads it, and no log below the catalog.** The commit index
below the catalog is still loaded on its first use (ADR 0139). It is taken
from the first source that holds:
1. a local blob from position 0 that agrees with the catalog's entry at its
   position, as before;
2. the chain's commits blobs. Each must exist, cover exactly its set's
   window, start where the one before ended, and end with the entry the
   catalog pins at its set's end. They are then written end to end as one
   local blob;
3. the log's headers up to the catalog's position, as before. This is used
   only when a set has no commits blob — one written before this release —
   or one does not verify.

**3. It is no derived format change.** Every byte of the ninth blob is a kind
derived format 4 already defines, in format 3's layout, under version 4.
- A preview.8 build reads a set that has it and ignores the blob.
- This build reads a preview.8 set without it, and reads the log's headers
  for that set's window.
- The storage format specification lists it in §7.5 as optional.

Derived formats are rebuilt, not read forever (ADRs 0072, 0139), and none
is added here.

## Alternatives considered

- **One shared commit index object, replaced at each checkpoint.** A rewrite
  of every entry of the history per checkpoint, and a second object to keep
  consistent with the catalog.
- **A new derived kind for a set's commit index, derived format 5.** A
  preview.8 follower during an upgrade would read every format-5 file as a
  cache miss and replay the log. Nothing in the layout needs a new kind. The
  maintainer ruled for format 4.
- **Commit index entries inside each extent's directory.** Six copies of the
  same entries, one per order.

## Consequences

- A set is nine blobs. On an object store a checkpoint is one create more;
  a set's commits blob is 80 bytes per commit of its window.
- A cold start that reads below the catalog reads the sets' commits blobs
  through the page cache, about 2 MB at REC size M's 25,000 commits, instead
  of every log header up to the catalog. The measured cost is in the
  traceability record and the operator guide.
- `FromSetsAsync` in `Dataset` is the new source. `PrefixLoader` takes the
  catalog's sets and pins.

## Checks

- **Checked against the accepted ADRs** (0001–0147). Touches:
  - **0072**: the format version stays 4;
  - **0089**: the commit index layout is unchanged;
  - **0139**: amended.

  No conflict.
- **Layer ownership.** `Varve.Store`, layer 4. The object-storage backend
  stores the blob with the set's others, as a name under `extents/`.
- **Analyzer rule.** None.
- **Tests.**
  - `ObjectColdStartTests`: a cold start from the catalog's cursor reads no
    log header below the catalog's slot, and serves feed, as-of and by-time
    reads at every position as an open of the whole log does.
  - `TermFilterTests`: the ninth blob is of kind 4, version 4.
  - `ExtentMergeTests`: merged sets read the same.
