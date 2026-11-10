# 0240 — Resident memory is bounded by data and by configuration, never by commits

## Status

**Accepted — filed unaccepted by #222, 2026-11-10** (ADR 0066). Acceptance is
the maintainer's act on the pull request.

**Amends** by dated amendment:
- [ADR 0082](0082-the-soak-gate.md): the dataset's own, retired on
  2026-11-03, is restated as what is measured here;
- [ADR 0177](0177-seek-dont-narrow-intervals-beside-the-key.md): how a read
  inside a set's window reaches the set's seek directory;
- [ADR 0089](0089-the-commit-index-is-derived-and-paged.md): how a timestamp
  is found in a commit index blob.

No format changes: every byte in `log/` and `derived/` is what it was.

Records decision set [`resident-memory`](../decisions/resident-memory.md).

## Context

The four-hour soaks of 2026-11-02 and 2026-11-03 held a dataset steady at
667,000 quads and grew anyway: about 50 bytes of working set a commit. ADR
0082 had accounted for growth of that kind as "the dataset's own", charging a
table of commits at 144 bytes a commit. ADR 0089 had paged that table out a
month before, so the model described memory the process did not hold, and it
was retired on 2026-11-03 without saying what the process does hold. #222
asks for that, and for a bound.

**What was measured** (the issue's comment of 2026-11-10 has the tables).

- *The soak's own artifact*, nightly 41450438151, 549,204 commits, each
  column fitted against commits from minute 30. The managed heap after a
  full collection grows **36.5** bytes a commit. The directories of the
  files under `derived/extents/` grow **36.2**. The projection's runs'
  directories grow 0.1, and memory outside the collector's does not grow.
- *The same workload without its pause*, stopped every 250,000 commits for
  a process dump, each dump walked from its roots and every object charged
  to the Varve types that hold it. One owner grows, linearly, **59 bytes a
  commit**: what each extent set keeps for reads inside its window.

| Commits | Rows-within directories and column checks | The sets' own key directories | Commit index | Memtable |
|---:|---:|---:|---:|---:|
| 250,668 | 13.8 MB | 10.6 MB | 1.2 MB | 23.1 MB |
| 500,668 | 28.6 MB | 8.5 MB | 0.4 MB | 0.6 MB |
| 750,668 | 43.4 MB | 9.1 MB | 2.4 MB | 57.6 MB |
| 1,000,668 | 57.9 MB | 9.6 MB | 2.6 MB | 67.9 MB |

**The owner.** A format-5 extent (ADR 0177) carries, beside its asserted and
retracted sections, a thirteenth key section for its rows within and three
fixed-width columns of distances. Its **seek directory** is what finds a
key's intervals in those:

- the rows within's fences, block starts and block checks: 32, 8 and 8
  bytes per block of 128 rows;
- the three columns' checks: 8 bytes per block of 128 rows of each.

A set's rows within are its window's history, every interval that began and
ended inside it, so the directory grows with the commits whatever the size
of the data. It was read whole when the set was opened and held for as long
as the set was in the chain. Only a read as of a position inside the set's
window, a merge and a rewrite read it. A read of the present never does
(`ReadsOfThePresentNeverTouchAColumn`).

**What else is per commit.** One more structure, small: a commit index blob
keeps a fence, the first timestamp of each block of 128 entries, 8 bytes, in
memory (ADR 0089). That is 0.06 bytes a commit: 62 KiB at a million commits,
about 200 MB after a year at 100 commits a second. Nothing reads the fences
but `PositionAt`, and a fence is its block's first entry's timestamp, which
the blob already holds at a known offset.

**What is not per commit**, and stays as it is: the sets' asserted and
retracted sections' directories and the runs' (40 to 48 bytes per 128 keys,
which a read of the present uses, and which follow the data); the newest
commit entries (between one and two times `CommitCache`, 80 bytes each);
the memtable (`MemtableLimit`); the term dictionary's cache (two fixed
tables). The memtable's bound is not enforced against a writer faster than
maintenance, which is #227's finding and its change.

## Decision

**1. Nothing is resident per commit.** What a dataset holds in memory is
bounded by one of two things:

| Bounded by | What |
|---|---|
| the data | the key directories of the projection's runs and of each set's asserted and retracted sections; the graph-version index; the term filters |
| configuration | the memtable (`MemtableLimit`), the newest commit entries (`CommitCache`), the seek directories (`SeekDirectoryCache`, below) |

No structure grows with the number of commits. A change that adds one is a
change to this ADR.

**2. A seek directory is read on demand, under a ceiling in bytes.**
- When a set is opened its extents' directories are read and hashed whole,
  as before, and the seek directory's slices are **not kept**: only where
  each slice is, how long, and a 64-bit check of its words taken as it
  passed.
- A read that needs an extent's seek directory takes it from the dataset's
  **seek directory cache**, keyed by extent. A miss reads the slices from
  the extent, checks each against the check the open took, and keeps the
  directory.
- The cache keeps the most recently used directories up to
  **`DatasetOptions.SeekDirectoryCache`** bytes, 32 MiB by default, and
  drops the least recently used past it. A directory larger than the whole
  ceiling is given to its one read and not kept. Zero keeps none.
- A read holds the directories it was given until it ends. Eviction never
  takes one from under a read, and a read's memory beyond the ceiling is
  what it has in hand: at most six directories, one an order.
- A set leaving the chain drops its directories from the cache.
- The server's setting is `Varve:Store:SeekDirectoryCacheBytes`. The gauge
  `varve.store.seek_directories.bytes` reports what is kept.

**3. A slice read again is checked.** The check is of the slice's
little-endian words in order, independent of how the reads are cut. A slice
that does not match is `DerivedDamageException`: damage met in a read, which
ADR 0213 already answers from the log. The directory's hash is still
verified whole before a set is used at all.

**4. A commit index blob's fences are not held.** `PositionAt` finds the
last block whose first entry's timestamp is at or before the one sought by
binary search of those entries, read through the blob: about
log₂(n ÷ 128) reads of one entry, 13 at a million commits, and then the
seven it always read. The blob's first timestamp, which says whether a
timestamp is in the blob at all, is kept: 8 bytes a blob, and blobs are
logarithmic in number. The directory, fences included, is still written and
still verified when the blob is opened.

**5. No format change.** The slices are where derived format 5 and 6 put
them (storage format §7.5), and a commit index blob is what §7.4 says. A
build before this one reads what this one writes, and the reverse.

## Alternatives considered

- **Hold the seek directory sparsely**, every sixteenth fence and no block
  starts, as ADR 0080 did for checkpoints. A twentieth of the memory, and
  still a slope: 2 to 3 bytes a commit, 20 MB a day at 100 commits a second.
  The issue asks for a bound, not a smaller slope.
- **A two-level directory**, a fixed number of fences per section in memory
  and the rest paged. Bounded per section, so still a term per set, and the
  sets grow in number with the history once they reach `MaxSetBytes`.
  More code than reading a directory whole when it is wanted, for a cost a
  read pays once.
- **Page individual directory blocks** through a page cache. The finest
  grain, and a lock or a table lookup on the path of every key sought. The
  cache here is consulted once a read and an order.
- **Count the seek directory as the dataset's own**, and give the judge the
  allowance. That is what ADR 0082 did for the commit table: a model of
  growth instead of an end to it. 300 MB a day on a server whose data does
  not grow is the defect, not its accounting.
- **Keep the commit index's fences and state 0.06 bytes a commit.** Honest,
  and the one per-commit term left. Finding the timestamp in the entries
  costs a few more small reads on a path only a read by timestamp takes, and
  leaves the decision with no exception to explain.

## Consequences

- **An as-of read inside a set's window that misses the cache reads the
  directories it needs**: one per order it scans or looks up in. Its size is
  48 bytes per 128 rows within and 8 per 128 rows of each column; for the
  soak's sets, tens to hundreds of kilobytes. Read once, it serves every
  later read until it is evicted. The measurements are in the Checks.
- **Opening a set costs what it did.** The directory is read and hashed
  whole either way.
- **An operator can trade memory for as-of latency** with one setting, and
  see what is used.
- **The soak judge's allowance, 60 bytes a commit, has no cost left to
  cover, and is not lowered here.** A one-hour soak at a 1 ms pause measured
  139 bytes a commit of working set: its writer outruns the flush (#227),
  and the seek directory cache was still filling to its ceiling. Neither is
  growth per commit, and a fit against commits reads both as slope. The
  figure to lower it to comes from the gate's own four-hour run on this
  change, with the soak's seek cache small enough to be full before the
  judged window (`--seek-cache`), and is ADR 0215's to amend then.
- **`PositionAt` reads more.** It is the read behind `Varve-As-Of` with a
  date, once a request.

## Checks

- **Checked against the accepted ADRs** (0001–0239). Touches:
  - **0072, 0139, 0177**: the format is unchanged; how a directory is held
    is not part of it. 0177 amended.
  - **0080**: its sparse sections were for checkpoints of derived format 2,
    and stand for that kind.
  - **0082**: amended. **0215**: its judge and its allowance stand; see the
    consequences.
  - **0089**: amended; its entries, blobs and paging stand.
  - **0070**: a read takes no lock on the path of a key. The cache's lock is
    taken once a read and an order, and never across a blob read.
  - **0160**: a retired set is still read by the views that hold it; they
    keep the directories they took.
  - **0213**: a slice that fails its check is damage, answered as damage is.

  No conflict.
- **Layer ownership.** `Varve.Store`, layer 4; the setting is the server's.
  No contract changes.
- **Analyzer rule.** None. The seek's per-key paths stay `[HotPath]`;
  taking the directory, once a read and an order, is answered by
  `SeekDirectoriesAreReadOnDemand` at `ExtentSeek.Seek`.
- **Tests.** `SeekDirectoryTests`:
  - ADR 0177's property with the cache at zero, at 96 bytes and at 1 KiB:
    every view equals the model by scan, lookup and count at every position
    across any schedule of checkpoints, drops and merges, and the cache
    never holds more than its ceiling;
  - on a steady dataset, the chain's seek directories grow with the commits
    while what the dataset holds of them stays under the ceiling, after N
    commits and after 2N, by the store's own accounting;
  - a directory is read once when the cache keeps it, and at every read
    when it keeps none; the cache evicts the least recently used and does
    not keep a directory larger than itself;
  - a slice that does not match its check is damage;
  - the position at a timestamp, across paged blobs of several blocks,
    without fences in memory.

  `MidReadDamageTests` and `ExtentDamageTests` damage every block a read
  reads, the slices now among them.
- **Measured.** The harness of the context on the changed store, stopped
  every 250,000 commits with its writer and readers out and maintenance run:

  | Commits | Live heap, before | After | Extent sets, before | After |
  |---:|---:|---:|---:|---:|
  | 250,668 | 52.9 MB | 11.8 MB | 24.7 MB | 3.7 MB |
  | 500,668 | 43.6 MB | 7.5 MB | 37.3 MB | 3.5 MB |
  | 750,668 | 118.7 MB | 7.9 MB | 52.5 MB | 2.6 MB |
  | 1,000,668 | 146.5 MB | 7.8 MB | 67.7 MB | 3.8 MB |

  The "before" heap includes the memtable as it was in flight, 23 to 68 MB,
  which the first harness kept reachable; at rest the dataset holds it
  within its limit. In flight it peaked at 480,000 entries against a limit
  of 20,000, which is #227's.
- **Open questions owned.** None.
