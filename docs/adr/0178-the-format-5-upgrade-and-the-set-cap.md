# 0178 — The format-5 upgrade, mixed-version instances, and the set cap

## Status

**Filed unaccepted by #133, 2026-10-23** (ADR 0066). Acceptance is the
maintainer's act on the pull request.

**Amends** [ADR 0160](0160-extent-deletion-by-reader-announcements.md):
a reader record gains the optional `maxDerived` (point 2), by dated
amendment. **Revised 2026-10-26** on the pull request's review, before
acceptance: point 2, which rewrote the oldest format-4 set first whatever
read the store, now waits for every present reader to read format 5, and
point 3's upgrade order is a recommendation.

**Amends** [ADR 0159](0159-the-extent-merge-policy.md), point 3 and its
consequence: `MaxSetBytes` no longer bounds an as-of read's cost, and its
sizing advice changes, by dated amendment. Follows
[ADR 0177](0177-seek-dont-narrow-intervals-beside-the-key.md), which chose
derived format 5 for extents.

Records decision set
[`the-format-5-upgrade-and-the-set-cap`](../decisions/the-format-5-upgrade-and-the-set-cap.md).

## Context

ADR 0177 writes extents in derived format 5, so an as-of read inside a set
seeks it. Datasets written by `v0.1.0-preview.12` and before hold format-4
sets, on directories and in object stores, and a fleet upgrades one instance
at a time: for a while a writer and its followers may run different builds
over one store. Derived data is rebuilt, not read forever (ADR 0072; #129's
inventory), so the question is not whether format 4 is kept but how a
dataset gets from one to the other without failing a read, and what an
instance of the older build does with a set of the newer.

The cap on a merged set, `MaxSetBytes` (ADR 0159), was chosen so that
narrowing the largest set stayed within the gate. Seeking removes that
reason.

## Decision

**1. A format-4 set is read with the narrowing path, never failed.** It
loads, verifies and serves reads of the present as it did: its key sections
are format 5's. A read inside its window narrows it (ADR 0138), and costs
what it did. Nothing is rebuilt at open.

**2. Older readers decide when format-4 sets are rewritten.** Rewriting
the oldest set first would put a format-5 set at the front of the chain, and
a reader of format 4 that opens after it would replay the entire log, its
chain ending there. So the rule is safe whatever the order of the upgrade:
- **Every reader record says what its reader reads**: an optional
  `maxDerived` (storage format §8.1, ADR 0160 amended), the newest derived
  format the instance reads. Absent, it is 4, so a record written before #133
  — and one of a format this build does not read — counts as a reader of
  format 4, and the record stays format 1. Every record this build writes, by
  every path that writes one, says 5. `varve storage readers list` and
  readiness's `retention` show it.
- **While any present record says less than 5**, the writer rewrites no
  format-4 set, and a merge, a drop's merge or a split of a format-4 set
  writes format 4. New checkpoints, and merges of format-5 sets only, still
  write format 5. So format-5 sets stand only above the format-4 ones, an
  older reader's chain ends only where they begin, and it replays only their
  windows: the recent log.
- **Once every present record says 5** — the older reader forgotten by the
  operator, or restarted on this release, which replaces its record — the
  writer's maintenance rewrites format-4 sets, one a round when the merge
  policy has nothing due, the oldest first: a merge of one input, by ADR
  0158's machinery — written under a fresh name, swapped once under the
  epoch fence, rebased on the catalog, abandoned if a checkpoint or drop
  touched its input meanwhile, and refused past the single-upload limit. A
  merge of format-4 inputs then writes format 5 too, so a set the policy
  would merge anyway is not rewritten twice.
- **The writer reads the records** before it chooses a rewrite or writes a
  set in place of a format-4 one, and whenever its deletion rule reads them
  (ADR 0160), only while a format-4 set is left; until it first has, it
  holds. A store with one reader, a directory, has no records and rewrites
  at once.
- It is a pure function of its input's rows, as every merge is (ADR 0177,
  point 4); the window, the dictionary, the graph-version index and the
  commit index are the input's. The rewritten set's old blobs are deleted by
  the deletion rule (ADR 0160), as any merged input's are.
- With merges off (`ExtentMerges.Enabled` false) nothing is rewritten and
  format-4 sets stay narrowed.

So the upgrade's cost is one rewrite of each format-4 set, the bytes of the
chain once, spread over maintenance rounds once no older reader is left; it
adds to the write amplification once, not per checkpoint.

**3. An instance of an older build finds a format-5 extent a cache miss.**
A reader of derived format 4 refuses a header of version 5 (ADR 0072). The
catalog, the dictionary extents, the graph-version indexes and the commit
index blobs are still format 4 (ADR 0177, point 5), so it still reads the
catalog and attaches; its chain ends at the first set with a format-5 extent
(ADR 0139: "the first that fails ends the chain there"), and it reads the
log from that set's start. Measured with `v0.1.0-preview.12`'s store over
this build's sets, on a directory and on Azurite: the same answers at every
position sampled, its chain empty.

- **A follower of `v0.1.0-preview.12` behind a writer of this build** serves
  every position correctly, at the cost of the log's distance above its
  chain's end at open and on as-of reads, until it is upgraded. Its reader
  record is the same format (ADR 0160), so deletion is unchanged. It never
  fails and never returns a wrong result.
- **A writer of `v0.1.0-preview.12`** — a downgrade — writes its next
  checkpoint from the log, in format 4. On a directory it deletes the
  format-5 sets at open, as any set its chain does not name; on an object
  store they are unnamed after its next swap and deleted by the reader rule.
- **Followers first, then the writer, is a recommendation.** A follower of
  this build reads both formats, so nothing pays the log's distance. In the
  other order the store is safe too: by point 2 an older follower's chain
  ends only at the format-5 sets a new writer adds, so it replays only the
  log they cover, and the format-4 sets wait for it to go.

**4. `MaxSetBytes`: the default stays 256 MiB; the cap bounds a merge, not a read.**
- **What it no longer bounds.** An as-of read inside a set costs its
  matches whatever the set's size (ADR 0177), so the reason ADR 0159 gave for
  256 MiB — narrowing the largest set within the gate — is gone.
- **What it still bounds.** The work of one merge, which reads its inputs
  and writes the merged set, and the largest blob an upload carries: an
  order extent is about a seventh of its set, against the 5,000 MiB single
  upload of Azure Blob Storage and 5 GiB of S3 (ADR 0158, point 9).
- **Why the default stays.** Measured at REC M on files with the cap at
  1 GiB (`--asof-seek file M 1024`), the chain held 7 sets against 5 and its
  largest 282 MiB against 211, so head lookups merged more runs: 21.4 / 63.4
  µs at p50 / p99 against 15.2 / 52.1. Write amplification fell from 1.86 to
  1.75. The size-tiered policy places sets by their sizes, and a larger cap
  at this size only moves where the classes fall. Nothing measured asks for
  a larger default.
- **When to raise it.** On a dataset many times the cap, the sets past half
  of it are never merged again, about `2 × total / MaxSetBytes` of them, and
  every read of the present merges one run each. A cap of 1 to 2 GiB keeps
  that count down where the dataset is tens of GiB, at a merge's time and
  the bytes it rewrites; the operator guide says so. The upload limit
  bounds it from above.

## Alternatives considered

- **Rebuild every format-4 set at open.** A set is rebuilt from the log,
  so an open would cost the history's replay before the first read.
  Rejected: reads of the present are unchanged on format 4, and narrowing
  answers inside it as before; the rewrite can wait for maintenance.
- **Drop the narrowing path now**, and fail or rebuild a read inside a
  format-4 set. Fails the prompt's rule that a format-4 set is never failed
  during an upgrade, or makes a read wait on a rebuild.
- **A new kind (9) instead of a version.** The same effect on an older
  reader: it asks for kind 6 and finds a cache miss. The version keeps one
  kind for one role, as 0109 did for runs.
- **Hide the columns from an older reader**, as a tenth blob beside an
  unchanged format-4 extent. An older follower would keep its chain, but
  every set would carry its rows within twice, and the extents could never
  shed format 4's streams.

## Consequences

- A dataset upgraded from `v0.1.0-preview.12` keeps its as-of cost inside
  each set until maintenance rewrites it; the sets the policy merges first
  are rewritten by the merge.
- The narrowing path, `RestrictedRows` and the narrowed run, is kept for
  format-4 sets. It goes when no supported build writes format 4.
- `docs/operator/upgrade.md` gives the order and what an older follower
  costs; `docs/operator/backup-and-restore.md` and `configure.md` the cap's
  new trade.

## Checks

- **Checked against the accepted ADRs** (0001–0173). Touches:
  - **0072, 0139**: derived data rebuilt, not migrated; a version not read is
    a cache miss, and a chain ends at the first set that fails.
  - **0136**: the rewrite swaps under the epoch fence.
  - **0158**: the rewrite is a merge of one input.
  - **0159**: the cap's reason and advice. Amended.
  - **0160**: the rewritten set's blobs are deleted by the reader rule; a
    record of an older build is the same format.

  No conflict.
- **Layer ownership.** `Varve.Store`, layer 4.
- **Analyzer rule.** None.
- **Tests.**
  - `ExtentSeekTests.a_format_4_set_is_narrowed_until_maintenance_rewrites_it_as_format_5`:
    generated histories with retractions, checkpoints written in format 4,
    read by narrowing at every position; opened by this build, rewritten by
    maintenance until no format-4 set is left, and equal at every position
    throughout.
  - `ObjectFormatUpgradeTests.no_format_4_set_is_rewritten_while_a_reader_of_format_4_is_present`:
    format-4 sets written by an earlier release, and a reader record without
    `maxDerived` present; this build's checkpoints and every merge due leave
    format-4 sets first and format-5 sets only above where the older chain
    ended; then, the record forgotten, or its reader restarted on this
    release, rewriting proceeds until every set is format 5; every position
    reads the same throughout. With the rule removed it fails.
  - `ObjectStorageServerTests`: `varve storage readers list` shows
    `maxDerived`, against Azurite; readiness carries it.
  - The mixed-version check: `v0.1.0-preview.12`'s store reading this
    build's sets, on a directory and on Azurite, in the traceability record.
