# 0139 — Extents and the projection catalog; derived format 4

## Status

**Accepted — filed unaccepted by milestone Object storage of #92, 2026-10-15**
(ADR 0066). Acceptance is the maintainer's act on the pull request. The term
"extent" and the cut — background merges and the deletion rule moved to a
named milestone, Extent maintenance — were decided by the maintainer on the
Object storage plan, 2026-10-15.

Builds on [ADR 0138](0138-validity-intervals-in-the-default-projection.md),
whose sets this ADR lays out, and on ADR 0140's T7. **Amends**
[ADR 0071](0071-synchronous-reads-over-asynchronous-storage.md) (a blob read
may wait on a page fetch) and [ADR 0072](0072-format-version-1.md) (derived
version 4, and the catalog as derived data), by dated amendment.

Records decision set
[`extents-and-the-projection-catalog`](../decisions/extents-and-the-projection-catalog.md).

## Context

ADR 0138 makes a checkpoint a chain of extent sets. This ADR decides how a
set is laid out, how the chain is named, how an object store holds both so a
follower and the next writer find them, how the local disk caches them, and
how a dataset written by the previous release opens.

"Segment" in this repository is a log segment, `log/NNNNNNNN.seg`. The
projection's immutable objects are **extents**, the term T7 borrows from Azure
Data Explorer; AGENTS.md's vocabulary table carries it, and no document uses
one word for both.

## Decision

### An extent set: one extent per order, and a dictionary extent

A set for the window `(from, to]` is eight blobs under one name,
`extents/<from:D20>-<to:D20>.<sequence>/`:

| Blob | Kind | Holds |
|---|---|---|
| `order0` … `order5` | 6, extent | one index order's open keys (its asserted section), closing keys (its retracted section), then their positions and the rows within |
| `terms` | 7, extent dictionary | a run with no keys and the window's term section: id-to-term entries, term-to-id hashes and the filter (ADR 0079, ADR 0109) |
| `graphs` | 5, graph versions | the graph-version index at `to` (ADR 0125) |

An extent is a run of derived format 4 (below): the twelve key sections of
ADR 0041 — eleven of them empty — then the term section, then its own three
sections, then the directory and the header. Its directory carries sixteen
more words after a run's: its order; the smallest and largest key of any row
(its **statistics**, in the directory the header points at, so one ranged
read of the tail gets them); and the offset and length of each of its own
sections.

- **Positions.** ULEB128 of the distance after `from`, one per open key and
  one per closing key, in key order, so the `i`th position belongs to the
  `i`th key.
- **Rows within.** The key's 32 bytes, then ULEB128 of the asserting and the
  retracting position's distance after `from`, each 0 for the window's
  boundary.

The sequence in the name is the catalog's counter; a name is **never reused**,
so an extent, once written, is never replaced: by create-if-absent on an
object store, and atomically on a directory.

### The catalog

`catalog` is one derived blob of kind 8: a header naming the position the
chain reaches and the log's header hash there, and a directory listing the
sets oldest first — name, `from`, `to` — each window starting where the one
before ends, and the next sequence.

- It is **replaced whole**, and only after every set it names is written.
  Only then are the sets it no longer names retired. So no reader of any
  catalog version finds a set it names missing.
- **On an object store** it is stored behind a 12-byte wrapper carrying the
  writer's epoch (ADR 0136), and replaced only by replace-if-unchanged against
  the version this writer last read or wrote. A writer's claim rewrites it —
  or writes an empty one — under the new epoch, so every older writer's
  version is stale and **its swap is refused** with `writer-superseded`.
- **Followers read it and never write it.** A read-only open holds no
  version, and a checkpoint on it is refused.
- At open, each named set is loaded and checked against the log: its header
  hash, its dictionary's counters. The first that fails ends the chain there:
  derived data that does not verify is a cache miss (ADR 0072).

### Where it lives

- **On a directory** (the file backend, ADR 0074): `derived/extents/` and
  `derived/catalog`, fenced by the OS lock. Determinism across backends
  extends to the projection's checkpoints.
- **On an object store**: `derived/extents/…` and `derived/catalog` under the
  dataset's prefix. Everything else under `derived/` — runs, the projection's
  state, the commit index, spills — stays in the local cache: it is rebuilt by
  any instance and never shared.

### The cache

On an object store an extent is read through a **page cache** on local disk,
1 MiB pages in a sparse file per extent:

- Opening an extent reads its first and last pages, which hold the header,
  the directory and the statistics; nothing else is fetched until a read
  needs it. That is **lazy attach**.
- **A page the cache does not hold is fetched on the miss** by a ranged read,
  and the synchronous blob read waits for it (amending ADR 0071). An extent is
  never replaced, so every fetch of a page returns the same bytes.
- The cache holds at most `CacheBytes` (default 8 GiB). Past it, the least
  recently read extent's pages are dropped and fetched again when next read.
  **Eviction never changes a result.** Pages are the process's own: the cache
  is emptied when the storage opens.
- An extent this process just wrote enters the cache whole.

### Deletion waits for Extent maintenance

A set no catalog names is retired once no view of this process holds it. On a
directory — one process, the OS lock's — it is then deleted. On an object
store a follower may hold an older catalog version that still names it, and
the rule that decides when no live reader can is milestone **Extent
maintenance**'s, with background merges; until then such extents stay in the
store. No time-based grace period is used, now or then.

### Derived format 4, rebuilt not migrated

- What this build writes is **derived version 4**. A run, a checkpoint of the
  previous release, the projection's state and the commit index keep format
  3's layout under it; version 4 adds kinds 6, 7 and 8. It reads 2 to 4.
- **Format 4 is not read forever.** Extents are derived data: a change to
  their format is a rebuild from the log, never a migration a later release
  must keep reading. `log/` stays storage format 1.
- **A format-3 `derived/` migrates as it runs** (ADR 0109's phrase). Its full
  checkpoints load and serve as they did, until dropped; the first checkpoint
  written under this release starts the chain. A full checkpoint at a position
  a set comes to end at gives way to the set.

### Cold start

Open reads the manifest, the writer record and the catalog, attaches the
chain's extents lazily, and reads **the log only after the catalog's
position**. That needs no shared commit index:

- **The catalog records the log's cursor.** Beside its position it records
  where the log continues: the segment and the offset where the next commit's
  records start, and the backend's slot for that byte — on an object store
  the chunk number (ADR 0135). It also carries the commit index entry of
  every set's end, and the settings commits up to its position. A backend
  that keeps its log in parts implements `ISegmentCursor`; an open hands the
  slot back before reading the log, and the object-storage backend then lists
  names and reads only the chunks from that slot on. The scan starts at the
  cursor, its chain at the catalog's position and hash. A cursor the log does
  not bear out — the record there is not the next position's, chained to the
  catalog's hash — is ignored, and the open reads the whole log as before.
- **The commit index below the catalog is read on first use.** The entries a
  pin carries are known at once. The first read that needs any other — a
  feed or an as-of read below the catalog, a lookup by time before it — reads
  the log's headers up to the catalog's position, checks that they end at the
  commit the catalog names, and keeps them as a commit index blob in the local
  cache; the read waits for it, as an extent page's miss does. Derived data in
  the local cache checked against a position it has not read is a cache miss.
- **A shared commit index is what Extent maintenance (#110) adds**, to remove
  that first read's cost: the commit index written to the store beside the
  extents, so no instance reads the log below the catalog at all.
- **Consolidation runs on seal** (ADR 0135): a sealed segment becomes one
  object, and its chunks are deleted except the fence slots. Chunks then stay
  bounded by the open segment, and a cold start reads the open segment from
  the cursor's slot and no sealed segment at all.

The measured cost at REC sizes S and M is in the milestone's traceability
record and the operator guide.

## Alternatives considered

- **One blob per set holding all six orders.** Fewer objects, but a read of
  one order would fetch pages of a blob shared by all six, and the addendum
  asks for an object per permutation. The page cache makes either cheap;
  per order keeps a follower's working set to the orders its queries use.
- **The catalog's epoch in its name** (a catalog per epoch). A reader would
  have to list to find the newest, and a superseded writer's catalog would
  exist beside the current one. The wrapper keeps one name and one version.
- **Download every extent of the chain at open.** Simple, and the cold-start
  cost would be the dataset's size again: what T7 exists to avoid.
- **Delete unnamed extents after a grace period.** A timer deciding a reader
  is gone — refused by ADR 0116's reasoning; Extent maintenance decides by
  event.

## Consequences

- An object-store dataset's checkpoints are in the store, shared by every
  instance; a fresh container serves history without rebuilding `derived/`.
- The local disk is a bounded cache, not the bound on dataset size.
- Extents an object store keeps after a drop or a merge accumulate until
  Extent maintenance; each is the window's changes, not the state.
- The read path may wait on the network on a cache miss; the operator guide
  sizes `CacheBytes` against the working set.

## Checks

- Eviction never changes a result: a cache of 1 byte, of 1 MiB and of 8 GiB
  serve the model's view at every position (`ObjectExtentTests`).
- A superseded writer's catalog swap is refused and its reads are unchanged.
- A follower attaching the writer's catalog serves the same results at every
  position, and its checkpoint is refused.
- A fault at every extent write and catalog swap, after which the writer can
  write nothing more: the next writer's catalog names only whole sets, and the
  results are unchanged.
- Runs and extents are written as version 4 (`DictionaryTests`); the layout on
  disk (`FileStorageTests`).
- A cold start from the catalog's cursor reads no chunk below its slot, and
  serves at every position — feed, as-of, by time — what an open of the whole
  log serves; the first read below the catalog reads the headers once
  (`ObjectColdStartTests`).
