# 0089 — The commit index is derived and paged

## Status

**Accepted — filed unaccepted by the commit-index slice of #61, 2026-10-06**
(ADR 0066). Acceptance is the maintainer's act on the pull request.

Decided by the maintainer on the milestone 6c pull request: "the commit table
becomes derived, paged state (position-to-offset index in derived/, read
through the blob contract, bounded recent-entry cache)", with the one-hour
soak re-run and the gate not restated. It changes the shared state that
readers capture without locks (ADR 0011), which is why it is an ADR.

**Revisit condition:** archive (T3), which would let the index's prefix go
with the log's; or a reader whose positions are not in time order.

## Context

The store kept an object per closed commit — its timestamp, header hash,
location, the dictionary's counters, its settings, the log's bytes to it —
144 bytes a commit, measured, in an array published with every state. It
grows without bound, and after 6c it was what was left of the soak's drift:
18 of the 27.6 MB the dataset held of its own at the hour, with the collector
committing about twice what is live (the 6c record). Every reader that
resolves a position used it: as-of reads, `PositionAt`, `DiffAsync`,
subscriptions resuming from a position, replay, checkpoints, the projection's
checks of its runs against the log, and shipping a replica.

## Decision

### An entry per commit, eighty bytes, in `derived/`

A commit's **entry** is its timestamp, the hash of its header, its location
(segment and offset), the dictionary's two counters after it, and the bytes
of the log to its end: 80 bytes, little-endian, the same in memory as on
disk. The settings are not in it: they change only at a settings commit, and
the index holds those positions and the settings from each on, which grow
with settings commits alone.

Entries are written to blobs under `derived/index/commits/`, each the entries
after a position up to another, end to end, in blocks of 128 whose first
timestamps are the blob's fences, with the derived header of ADR 0072 — its
end hash the header hash at the last position, its kind `commits` (4). A blob
of another version, kind or dataset, a damaged one, or one whose end hash is
not its last entry's is a cache miss (storage-format.md §7). An entry is read
by its offset through the synchronous blob read; a timestamp is found by the
fences in memory, 8 bytes per 128 commits, and at most seven entries read.

### The newest entries in memory, the rest paged out by maintenance

The index holds the newest `DatasetOptions.CommitCache` entries in memory —
**4,096 by default**, between that and twice that once there are that many:
320 to 640 KiB whatever the log's length, enough that what reads near the head
(the next commit, a subscriber keeping up, as-of reads within the soak's
checkpoint interval of 1,000) reads memory, and a blob written once in every
4,096 commits. When twice the cache is in memory, maintenance writes the
oldest half as a blob; it merges the newest two blobs while the newer is as
large as the older, so blobs stay logarithmic in number and each entry is
rewritten a logarithmic number of times. Paging and merging are maintenance,
under the option ADR 0042's amendment names; with it off, `MaintainAsync`
pages, as it flushes the memtable.

### What readers see during an extension

The index is **immutable**, a part of the state the sequencer publishes as one
reference (ADR 0011). A commit publishes a version with its entry appended in
memory; maintenance publishes, under the sequencer, a version with a new blob
in place of the entries it covers, or with a merged blob in place of two.
A reader keeps the version it captured:

- the entries **in memory** it reaches were written before the version was
  published, and are never written again;
- a **blob** it reaches is taken for the length of a read. A blob a newer
  version merged away is retired, and deleted only once the last reader lets
  go (ADR 0078's rule for runs). A reader that finds it closed reads the
  position from the current version instead — the same entry, because a
  commit's entry never changes — and caps a position found by timestamp at
  its own head, since the current version may reach further;
- a holder that takes the version (`TryAcquire`) keeps all of its blobs open
  until it lets go.

So a version captured before an extension resolves every position it could
before, from its own blobs while it holds them, and from the current version's
otherwise.

### Built on open, against the log

Opening walks the log as before, every header verified (I6), and now hands
each closed commit to the index as it passes instead of keeping them all. The
blobs that continue the chain from position 0 are checked **entry by entry**
against the commits the walk derives, and kept while they agree; from the
first that does not — missing, damaged, stale beside a log that diverged, or
another dataset's — the entries are rebuilt from the log and written out as
they reach twice the cache. Opening therefore holds no more of the index than
a running dataset does, and nothing the size of the log. It deletes the blobs
it listed and could not use; a blob that reaches past the log's head, or that
appeared after it listed, is not its to judge.

## Alternatives considered

- **A compact table in memory**: the entry's eighty bytes in chunks instead of
  an object per commit. Smaller by half, and still without bound; the soak's
  drift would shrink, not stop.
- **Bound the table by archive (T3).** The right end state for a log that
  leaves its prefix behind, and a larger decision than this; the index pages
  out whatever T3 later lets go.
- **Restate the soak gate** to count the collector's headroom over the
  dataset's own. Decided against by the maintainer.
- **Rebuild the index on every open and keep it in memory between.** Bounded
  only at rest.

## Consequences

- A dataset holds a constant amount of its commit index, whatever its length:
  the cache, and 0.06 bytes a commit of fences.
- A read of an old position's entry is a read of 80 bytes through the blob
  read, about a microsecond from the page cache, where it was an array access.
- `derived/` gains `index/commits/`, logarithmic in blobs; it is a cache, and
  deleting it costs a rebuild on the next open.
- **Properties**: the index answers as the list of its entries for random
  appends, pages and merges, through versions captured before their blobs were
  merged away (200); a version held across an extension resolves every
  position it could before, and its blobs are deleted only once it lets go;
  an index missing, damaged, stale or another dataset's is rebuilt from the
  log; and every property of the store's suites runs with a cache of two
  entries, so that each pages out, merges and reads back.

## Checks

- **Checked against the accepted ADRs** (0001–0088) and specification 1.5.
  Touches **0011** (the shared state), **0042** (maintenance), **0072**
  (derived/ is a cache; the walk), **0078** (deferred deletion), **0082** (the
  soak gate it serves). No conflict; `log/` is unchanged, so determinism is.
- **Layer ownership.** `Varve.Store`, layer 4. Public: `DatasetOptions.CommitCache`.
- **Analyzer rule.** None.
- **Open questions owned.** None.
