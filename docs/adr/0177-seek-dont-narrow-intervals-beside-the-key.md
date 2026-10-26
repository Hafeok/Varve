# 0177 — Seek, don't narrow: an extent's intervals beside its keys, derived format 5

## Status

**Filed unaccepted by #133, 2026-10-23** (ADR 0066). Acceptance is the
maintainer's act on the pull request. The decision to seek rather than
narrow, and the candidates measured, are #133's and the session's prompt
(1.0, #15, amended 2026-10-19). The layout, candidate 1, was approved on the
pull request's review, 2026-10-26; the same review asked for the block
checks (point 6) and the head's broad scan beside the as-of one.

**Amends** by dated amendment:
- [ADR 0138](0138-validity-intervals-in-the-default-projection.md), its
  section "As-of reads": a read inside a format-5 set's window seeks the set;
  only a format-4 set is narrowed;
- [ADR 0139](0139-extents-and-the-projection-catalog.md): an extent is
  written in derived format 5;
- [ADR 0072](0072-format-version-1.md): derived version 5, for extents only;
- [ADR 0049](0049-cardinality-estimates-on-the-quad-source.md): an as-of
  view inside a set may estimate.

The upgrade from format 4, mixed-version instances and `MaxSetBytes` are
[ADR 0178](0178-the-format-5-upgrade-and-the-set-cap.md)'s.

Records decision set [`seeking-extent-sets`](../decisions/seeking-extent-sets.md).

## Context

An as-of read at `P` strictly inside an extent set's window `(a, e]`
narrows the set (ADR 0138): it reads every row of the set, restricts each to
`(a, P]`, classifies them again, writes the result as a run, and only then
reads. The cost is the set's size. Extent maintenance (#110) measured it at
REC M: **2,736 ms** inside the largest merged set (211 MiB), against
**12.1 µs** for a quad's lookup at the head (p50). Merges make sets larger, up
to `MaxSetBytes`, so the cap was what bounded it (ADR 0159).

The cost is the layout's, not a backend's. An extent's open and closing keys
are compressed key sections, sought by their fences; but their positions are
one ULEB128 stream per section, and the rows within are one stream of 32-byte
keys and ULEB128 pairs (storage format §7.5, version 4). Neither has an index:
the `i`th position is found only by decoding the `i − 1` before it, and a
key's rows within only by reading from the start. So nothing can be read at
`P` without reading everything.

Time travel is the store's thesis. An as-of lookup cannot cost seconds when
the same lookup at the head costs microseconds.

## Decision

**1. Seek, don't narrow.** A read at `P` inside a set's window looks its
keys up in the set exactly as a read of the present does, then classifies
only the matching rows' intervals against `(a, P]`. Nothing is written, and
nothing of the set but the matching keys' blocks and column entries is read.

**2. The layout: intervals beside the key (candidate 1).** An extent of
derived format 5 is format 4 with two changes (storage format §7.5):
- **Every distance is a fixed-width column beside its keys.** A distance is
  a position minus the window's start, in `w` bytes little-endian, `w` the
  fewest that hold the window's length (1 to 8, a directory word). The open
  positions are one distance per key of the asserted section and the closing
  positions one per key of the retracted section, so the `i`th key's is at
  `i × w`: one read.
- **The rows within are a thirteenth key section.** Their keys are in blocks
  of 128 with fences and block starts in the directory, like every section's,
  and their column holds the assertion's and the retraction's distances, two
  a row.

The asserted and retracted sections are byte for byte what format 4 wrote,
and a read of the present reads them and nothing else: it never touches a
column. The term section, the filter, the statistics and the directory's
other words are unchanged.

**3. What a read at `P` computes.** A key's rows are its open row (asserted
in the window, never at its start), its closing row (asserted at the start,
retracted in the window) and its rows within. The quad is held at the
window's start when one of its rows begins there, and at `P` when one of its
rows covers `P` — asserted at or before it, retracted after it or not at all.
Where the two differ the set at `P` asserts or retracts it; where they agree
it says nothing, and the older sets decide. That is exactly the delta
narrowing computed, `net(L(a..P])`, so no answer changes (the property in
Checks).

- **A quad's lookup** is three binary searches of fences — the open, closing
  and within sections in SPOG — one block of each, and one column read for
  each row found.
- **A pattern's scan** merges, in the order that serves the pattern, the
  three sections' rows in the pattern's range and classifies them a key at a
  time. The view's scan overlays that delta on the older sets' scan: a key it
  asserts is yielded, one it retracts is not, and every other key is the
  older sets'.
- **An estimate** counts the set's rows in the range by the fences. When
  they are at most 4,096 it reads them and is **exact**; past that it is the
  set's count at its end, **estimated** (ADR 0049's third promise, amended).
  A view at a set's end, and the head, stay exact.

**4. Every writer writes format 5, merges included.** A checkpoint writes
its set from the log in format 5. A merge (ADR 0158) and a drop write theirs
from their inputs' rows in format 5, whatever their inputs' format — except
where ADR 0178 keeps format 4 for a reader that reads no more: a merge
stays a pure function of its inputs' rows, and the layout is only how the
output is written. The rows, their classification and every interval are
those format 4 had.

**5. Derived format 5 is for extents only.** The extent's header says 5;
the dictionary extent, the graph-version index, the commit index, the
catalog, runs and the projection's state are written as 4, unchanged. A
reader of format 4 therefore still reads the catalog and every other blob of
a set, and finds a format-5 extent a cache miss (ADR 0178). Format 5 is not
read forever (ADR 0072): a later change to extents is a rebuild.

**6. Every block is checked.** The extent's directory — hashed whole —
carries a 64-bit check of every key block and of every column block of 128
rows (storage format §7.5), and a block is checked when it is read: by a
read of the present, which reads key blocks, as by a seek, which reads
columns too. A block that does not match fails the read.
- **Why now.** #146 found that one damaged byte where nothing checks it can
  give a wrong history. Over every byte of a set's six order extents, zeroed,
  flipped or the blob cut there (`ExtentDamageTests`), format 5 without
  checks served a different answer for **212 of 14,523** damaged extents —
  every one a byte of a key block or a column, which only the directory's
  placement guarded; the header and the directory, hashed, made the rest a
  cache miss. With the checks, **none of 15,384**: 305 fail a read, and the
  rest are a cache miss or never read. Format 4's key blocks and streams are
  no better guarded; it is not changed, and is rewritten (ADR 0178).
- **The check** is one multiplication a word, each step a bijection, so
  damage inside one eight-byte word always changes it, and a cut changes the
  length it mixes in. It costs a read of the present a pass over the bytes of
  each block it decodes. Not a cryptographic hash: it detects damage, not
  tampering, which the store's credentials guard (ADR 0134).
- **So #146 needs no format 6** for extents: the check is in format 5 from
  its first release.

### What was measured

REC M, generator output version 2, on files, a checkpoint every hundredth of
the history with maintenance after each (`--extent-maintenance M` and
`--asof-seek file M`), on one machine:

Layout 1 (this ADR) against format 4's narrowing, the same benchmark on
`main`'s store and on this one, two runs each, the largest set 211 MiB:

| | Format 4: narrow | Format 5: seek |
|---|---:|---:|
| Opening a view inside the largest set, with its estimate of every quad | 2,950 / 3,246 ms | 0.01 ms |
| A quad's lookup inside it, p50 / p99 | 3.3 / 10.3 µs, after the narrowing | 4.4 / 15.0 µs |
| Its extent pages, p50 / max | 0 / 3 (the narrowed run, written) | 2 / 5 |
| A quad's lookup at the head, p50 / p99, same run | 12.5 / 40.6 µs | 15.2 / 52.1 µs |
| A subject's quads inside it, p50 / p99 | 7.6 / 29.0 µs | 10.5 / 44.8 µs |
| One predicate's quads inside it, a quarter / half / three quarters in | 0.7 / 1.3 / 1.8 ms | 4.1 / 9.4 / 12.0 ms |
| The same predicate's quads at the head, 132,587 (rerun with block checks) | 48.3 ms | 53.4 / 55.5 ms |
| Bytes checkpointed over the history | 420,777,999 | 405,933,553 |
| Bytes merges rewrote, and per byte checkpointed | 750,437,062, 1.78 | 753,709,040, 1.86 |
| Checkpoints, all 100, and maintenance, all of it | 24.7 s, 80.7 s | 24.2 s, 91.8 s |

- **The gate.** An as-of lookup inside the largest set is 0.29× a head
  lookup at p50 and at p99 (4.4 against 15.2 µs, 15.0 against 52.1 µs), from
  the first read: there is no narrowing to pay. The sets before it are
  fewer than the head's, so it reads fewer runs.
- **Scans cost their matches.** A subject's quads inside the 211 MiB set and
  inside one of 7.5 MiB, 28× apart, over the same 200 subjects and 705
  quads: 0.567 and 0.562 pages a match, 2.37 and 4.04 µs a match.
- **Head reads are unchanged.** The benchmark's head row differs by run
  (12.5 to 15.3 µs at p50 over four runs of both), so the same head lookups
  and scans were timed over one dataset of each format, in three rounds
  alternating `main`'s store and this one: 21.9–24.7 µs on `main` and
  format 4, 23.6–26.1 µs on this store and format 4, 22.2–23.1 µs on format 5,
  at p50; the p99s overlap as well.
- **What a seek costs that narrowing does not**, once paid: a broad scan
  reads the columns as well as the keys, about 0.25 µs a match against 0.04 —
  but narrowing paid three seconds first, the cost of some 14 million such matches.
- **A broad scan against the head's.** The same predicate at the head,
  132,587 quads, took 53.4 and 55.5 ms in two later runs with block checks
  (0.40–0.42 µs a match); inside the largest set 5.0–6.2, 7.7–8.6 and
  8.1–11.1 ms (0.16–0.35 µs a match). An as-of broad scan is at most 0.84×
  the head's per match, well inside 3×, so the skip index below stays not
  built. The head's 48.3 against 53–55 ms between formats is between
  processes: one kept dataset's head, one predicate's 1,512,879 quads, in
  three alternating rounds, 86.8–94.7 ms at best on `main`, 90.0–90.2 ms on
  this build over format 4 after a cold first round, 86.1–92.5 ms over format 5.
- **Write amplification** is up from 1.78 to 1.86 only because format 5's
  extents are 3.5% smaller: merges rewrote the same bytes (+0.4%) for fewer
  checkpointed. Maintenance takes 14% longer: a merge reads its inputs' rows
  once more, for the rows within's keys.
- **On Azurite** (3.37.0, on loopback, started empty for each run): an
  as-of lookup inside the largest set 4.2 / 15.6 µs at p50 / p99 against
  14.9 µs / 7.4 ms at the head (a head lookup's p99 meets pages not yet
  cached); opening the view 0.01 ms against format 4's 3,103 ms, whose
  narrowing reads every page of the 211 MiB set. From a fresh cache, 200
  as-of lookups made 25 ranged reads, at most 5 for one lookup — the same 2
  to 5 pages a lookup touches on files.

## Alternatives considered

- **An interval index per set (candidate 2)**: an interval tree, or sorted
  endpoints with a skip index, so a scan passes keys whose intervals do not
  cover `P`. It answers "which rows cover `P`", not "what are this key's
  intervals", so a lookup still needs the key's intervals reachable from the
  key — candidate 1 — and the index would sit beside it. What it could save
  on a scan is the rows whose verdict at `P` is the start's, read and
  passed. Measured on the largest set (Checks: `ExtentLayoutStatistics`):
  REC M's largest set, (0, 3000], holds 2,678,212 rows, every one an open
  row: a skip index could pass 75%, 50% and 25% of them, in whole 128-row
  blocks, a quarter, half and three quarters into the window, because ids are
  allocated in commit order. That is the broad scan's gap above, at most:
  a predicate's quads a quarter in would read a quarter of the rows. A point
  lookup or a subject's quads read one or two blocks either way. Not built
  now: it changes no lookup, and adds a second structure every checkpoint
  and merge writes and every format reader keeps; it is a block summary
  (the least assertion and the greatest retraction of each block of a
  column) that a later format can add beside these columns without moving
  them.
- **Position-sorted runs within a key range (candidate 3)**, for keys with
  many intervals: only if keys have many. Measured: every key of the largest set has exactly one interval (mean 1.000, max 1). A key's rows
  within are adjacent and few, so a run sorted by position inside a key
  would order one or two rows.
- **Positions inline in the key sections** (a 48-byte row instead of a
  compressed key). Rejected again for ADR 0015's and 0138's reason: a read of
  the present would read and skip them.
- **ULEB128 columns with a block index**, the distances compressed per block
  of 128 keys with each block's offset in the directory. Smaller than fixed
  width by about a byte a distance, at a second read per seek and a decode of
  up to 127 distances. The window's length bounds a distance, so `w` is
  rarely more than three or four bytes; fixed width is one read and no
  decode.
- **Narrowing, cached.** Keep narrowing, and keep the narrowed run for the
  next read at the same position. A read at a new position still pays the
  set, and the cache is a second copy of a set per position read.

## Consequences

- An as-of read inside a format-5 set costs its matches: a quad's lookup is a
  few block reads more than the same lookup at the head, and a scan is
  proportional to the rows of the set in its range, not to the set.
- `MaxSetBytes` no longer bounds an as-of read's cost (ADR 0178).
- An as-of view's estimate inside a set may be estimated; the optimiser
  already takes an estimate as a guide, and `QuadOverlay` and the graph
  scopes pass the flag on (ADR 0049).
- Extents are larger or smaller by their columns: a distance takes `w`
  bytes where ULEB128 took one to three, and a row within's key is
  compressed in a block where it took 32 bytes. Measured above.
- A replica (#97) copies a set's nine blobs as they are; the layout is this
  ADR's, and a follower reads a format-5 set as a writer does.

## Checks

- **Checked against the accepted ADRs** (0001–0173). Touches:
  - **0015, 0138**: the log is kept and a checkpoint is a fold of it; only
    how a set is read inside its window changes. Amended.
  - **0041**: six orders, every pattern a prefix range in one.
  - **0049**: an estimate may be estimated inside a set. Amended.
  - **0070**: a read takes no lock and writes nothing.
  - **0072, 0139**: derived data is rebuilt, not migrated; format 5 for
    extents. Amended.
  - **0080**: key blocks and fences, now for the rows within too.
  - **0158, 0159**: merges write format 5 and stay pure functions of their
    inputs' rows.
  - **0160, 0161**: unchanged: reader announcements, and the commit index
    blob.

  No conflict.
- **Layer ownership.** The layout, the seek and the scan: `Varve.Store`,
  layer 4. No contract changes.
- **Analyzer rule.** None. The seek's per-key and per-row paths are
  `[HotPath]` and allocate nothing (`VARVE0003`).
- **Tests.**
  - `ExtentSeekTests`: over generated histories with retractions and any
    schedule of checkpoints, drops and merges, every view equals the model by
    scan at every position, every quad any view holds is looked up in every
    view and found exactly where the scan found it, every subject's estimate
    is exact and is its scan's count, and no read writes a narrowed run; a
    point lookup inside a set of 16,000 quads reads no more than inside one of
    2,000; a count over more rows than are counted one by one is estimated,
    and its scan exact.
  - `ExtentSeekTests.a_quad_retracted_and_asserted_again_inside_a_set_is_looked_up_as_the_scan_finds_it`:
    the rows within hold one key an interval, and a lower bound found by a
    search that stops at any equal key missed a quad's row from the window's
    start, so the older set answered a lookup for it. `ExtentDamageTests`'
    undamaged check found it; a lower bound is now the first of equal keys,
    and this test fails without that.
  - `ExtentDamageTests`: every byte of a set's six order extents zeroed,
    flipped or cut, opened again from the log and the extents and read at
    every position by scan and by lookup: never a different answer (point
    6). A pull request checks every sixteenth case, the nightly run all
    (`VARVE_EXTENT_DAMAGE_STRIDE`).
  - `ExtentTests`, `ExtentMergeTests`, `EstimateTests`: unchanged, now over
    format-5 sets.
  - `ObjectExtentTests.eviction_never_changes_a_result`: a cache of 1 byte, of
    1 MiB and of 8 GiB serve the model's view at every position through the
    seek.
  - `ExtentLayoutStatistics`: the measurements above, from a dataset the
    benchmark kept; not a gate.
