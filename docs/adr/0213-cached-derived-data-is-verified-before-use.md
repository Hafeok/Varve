# 0213 — Cached derived data is verified before use: a check per block, and damage is a miss

## Status

**Accepted — filed unaccepted by 1.0 readiness A (#146, under #129), 2026-10-27** (ADR
0066). Accepted by the maintainer in 70d991c. Acceptance is the maintainer's act on the pull request. The plan it
follows was approved on #129 on 2026-10-27, with answers that this ADR
records:
- spills stay in scope under a problem type of their own;
- damage found mid-read is quarantined, re-run and resumed, with a property
  over every block;
- the flush is kept wherever a reader cannot verify.

**Supersedes in part**, each ruling moving to this ADR's set under its key
(ADR 0068 point 4):
- [ADR 0177](0177-seek-dont-narrow-intervals-beside-the-key.md):
  - `EveryBlockIsChecked`: a block that does not match is a miss, no longer
    a failed read;
  - `Format5ForExtentsOnly`: the checked kinds move to version 6;
- [ADR 0178](0178-the-format-5-upgrade-and-the-set-cap.md):
  - `ReaderRecordsStateMaxDerived`: a record this build writes says 6;
  - `OlderReadersDecideRewrites` and `MaintenanceRewritesFormat4Sets`:
    generalised from format 4 to any blob below the newest version;
- [ADR 0161](0161-the-shared-commit-index.md):
  - `CommitIndexBesideEachSet`: the set's `commits` blob is written in the
    newest layout its readers read;
  - `NoDerivedFormatChange`: the commit index changes format;
- [ADR 0073](0073-durability-per-host-declared.md): `FlushBeforeVisible`.
  The flush before a derived blob's rename is kept only where a reader cannot
  verify the blob.
- [ADR 0071](0071-synchronous-reads-over-asynchronous-storage.md):
  `BlobsArePublishedAtomically`. "Durable as the backend declares" is
  qualified to match.

**Amends**, by dated amendment:
- [ADR 0072](0072-format-version-1.md): derived version 6;
- [ADR 0174](0174-the-validation-projection.md): the projection's snapshot
  is written in the checked envelope;
- [ADR 0119](0119-headers-and-the-problem-catalogue.md): `load-spill-damaged`
  joins the catalogue.

**Adopted by Inference part 2** (#171): `derived/inference/` is written in
the checked envelope (point 6).

Records decision set
[`verified-derived-data`](../decisions/verified-derived-data.md).

## Context

ADR 0072 rules that derived data that does not verify is a cache miss,
rebuilt from the log. **That rule holds only where something verifies.**
The derived header and its directory are hashed (storage format §7.1), but
the contents the directory points at are not, in most kinds. §7.2 says so
outright: keys and entries "are verified by their directory's placement,
not hashed one by one", on the reasoning that a torn file is never
published and bit rot was outside 6a and 6c.

#126 tested that reasoning and it failed. A cached commit-index blob below
the catalog is accepted after checking only its last entry (`PrefixLoader`).
With one earlier byte zeroed, a read returned a wrong history or an error in
**985 of 1,920 cases** (`docs/traceability/2026-10-19-issue-126-store-test-time.md`).
The flush-then-rename that guards the write does nothing against:
- media errors and bit rot on the cache disk;
- a volume without atomic rename;
- an operator or a tool touching the directory;
- a bug in a later writer.

#15 puts verification in 1.0. A store a team trusts never turns local damage
into a wrong answer.

ADR 0177 already put a 64-bit check on every key block and column block of
a format-5 extent. Over every byte of a set's six order extents, that took
the wrong answers from 212 of 14,523 damaged extents to none of 15,384.
**But a block that fails its check fails the read** (point 6): an
`IOException` reaches the caller, and `ExtentDamageTests` counts that as a
pass. #146 rules it out: a failed check is a miss, never an error.

What each kind checks today (#129's plan, read on `main` at `3a8f636`):

| Kind | What is checked | What damage does |
|---|---|---|
| Commit index (kind 4), local `index/commits` and a set's `commits` | header, directory, and the last entry's hash | a wrong history, or `LogVerificationException` |
| Dictionary extent (kind 7) | header, directory, placement | an entry that still decodes is served |
| Runs and checkpoints (kinds 1 and 2) | header, directory, placement | a block that still decodes is served; otherwise `IOException` |
| Order extents (kind 6, version 5) | every block | `IOException` |
| Graph-version index (kind 5) | whole, by the directory's SHA-256, on load; **not** by `TryLookupAsync` for a checkpoint's index | a wrong graph version from a lookup |
| Projection state (3), catalog (8) | whole, by the directory's SHA-256 | a miss |
| Spills (`bulk/<load>/…`) | nothing | a wrong load, or `IOException` |
| Validation snapshot (`validation/…`, `VVP1`) | its binding's identity, and position ≤ head | a damaged report that still parses is served |

## Decision

**1. One check, per block, in the hashed directory.** Every checked kind
uses format 5's check (`BlockCheck`, storage format §7.5):
- 64 bits, one multiplication per eight-byte word, with the length mixed in;
- BCL only, so no new dependency.

The checks are held in the blob's directory. The directory is covered by
the header's SHA-256, so a damaged check is caught before it is trusted.

**2. Verified before first use.**
- **A kind read whole is verified whole when it is loaded:** the commit
  index, the graph-version index, the state, the catalog and the
  checked envelope. A blob that does not verify was never loaded.
- **A kind read by range is verified per block,** before the block is used:
  runs, the dictionary extent and order extents.
- **What it costs, and when.** A key block or a column block is checked each
  time it is read and decoded, as format 5's already are (ADR 0177): one pass
  of the fold over its bytes. A page of a term section is checked once per
  process, the first time any read touches it, and remembered.

**3. Per kind:**

| Kind | Block | Version |
|---|---|---|
| Commit index (4) | 128 entries (10,240 bytes). One check per block, after the directory's first timestamps. | **6** |
| Runs (1) | Each key block, as format 5's key sections. **The term section in 4 KiB pages**, from the entries' offset to the filter's end, so entries, offsets, the hash index and the filter are all covered. The page checks follow the directory's tail words, which say how many pages there are. | **6** |
| Checkpoints (2) | Not written since extent sets replaced them (ADR 0139). One an older release left is below 6, so a miss (point 7). | — |
| Dictionary extent (7) | Its term section in 4 KiB pages, as a run's | **6** |
| Order extents (6) | Format 5's key and column blocks, unchanged | 5 |
| Graph-version index (5) | The whole: its contents are its directory | 4, unchanged. `TryLookupAsync`, which looks one graph up in a checkpoint's index, now reads the directory whole (16 bytes a graph) and verifies its SHA-256 before it searches. Before, it read a few entries the hash never covered. |
| State (3), catalog (8) | The whole, as now | 4, unchanged |
| Checked envelope (9, new) | 64 KiB | **6** |
| Spills | Each spill's sort block, with its check written after it | — (never outlives its load) |

The catalog and the state stay at version 4, so an older build still attaches
to the chain and reads the log above where it ends (ADR 0178 point 3).

**4. Damage is a miss, wherever it is found.** A mismatch at load means the
blob is not loaded. A mismatch mid-read (a block, a page, a column, or a
blob shorter than its directory says) raises an internal
`DerivedDamageException`, which never reaches a caller. It is handled in
four steps:
1. **Re-run from the log, in place.** A run of `derived/` (a projection run
   or an extent set read as one) is its window's net delta: `Run.Merge`
   cancels opposite verdicts, and a set's open and closing sections are the
   window's net assertions and retractions. So the read that finds the
   damage rebuilds the run from the log for its window, in memory, once:
   its net delta and the dictionary entries the window allocated. From then
   on every section of the run reads from the rebuilt one, which holds the
   same keys in the same places, and the rebuild checks that it does.
2. **Resume.** A scan carries on from the block it was reading, so a streamed
   result has no quad twice and none missing, and equals the undamaged
   result byte for byte. A read that seeks a set, an as-of read inside its
   window (ADR 0177), also reads the set's rows within and its columns, for
   which there is no rebuilt counterpart. That read re-runs over the runs
   below the set plus the log's net delta from the set's start to its
   position, and a scan carries on after the quads it had yielded: both
   read the same quads in the order the pattern's index keeps them.
3. **Quarantine.** The blob is noted, and counted by
   `varve.store.derived.damaged`. A set found damaged by a read, or by a
   merge that reads its rows within and columns, is written again from the
   log by the writer's next maintenance round, before any merge, under a
   fresh name, and swapped in as a checkpoint's split swaps. Its window
   costs a read of the log once. A damaged projection run is written again
   by the next merge of runs, which reads it rebuilt. A follower writes
   nothing; it answers from the rebuilt run for as long as it holds it. Its
   page cache lives only in the process (ADR 0158), so a page damaged in the
   local cache is fetched afresh by the next process.
4. **Again, down to the log.** A second damage, in another blob, during the
   same read degrades the same way, and damage to every blob at once leaves
   every run rebuilt: the log alone, which its own hashes check (ADR 0072).
   Damage there is the log's, which is outside this ADR.

`ExtentDamageTests` no longer counts a failed read as a pass.

A bulk load's delta run is written before its commit is in the log, so
until then nothing can rebuild it: damage to it fails the load, uncommitted,
as a spill's does (point 5). From its commit on, it heals like any run.

**5. A damaged spill fails its load, with nothing committed.** A spill is
the load's own input, sorted and already consumed, so it cannot be rebuilt
from the log. This is the one kind where a miss cannot mean a rebuild.
- A mismatch ends the load before its commit, as a crash does: nothing
  of it is visible, and the dataset is at the position it held before.
- The embedded API throws `BulkLoadSpillDamagedException`, a
  `BulkLoadException` whose message says that nothing was committed and that
  resubmitting the load is right.
- The problem type is **`load-spill-damaged`**, `503`, in the catalogue
  (ADR 0119 amended). `varve load` names it and exits with its own code.
  No HTTP route runs a bulk load today; one that does answers with it.

**6. A projection outside the store writes a checked envelope.** A
projection kept in the local cache by another package cannot reach the
store's internal formats. The validation projection (ADR 0174) is one, and
Inference's `derived/inference/` (#171) is the next. For them, `Varve.Store`
offers `Dataset.WriteCheckedAsync` and `Dataset.TryReadCheckedAsync`, which
write and open a blob of **kind 9** through any `IDerivedStore`. The format's
own code, `CheckedBlob`, stays internal, since only the dataset binds an
envelope to its log.
- **The layout:** the projection's bytes in 64 KiB blocks, then a
  directory of one check per block, then the derived header (§7.1).
- **The binding:** the header carries the dataset id, the position the
  projection was built at, and the log's header hash there. A blob of
  another dataset, or of a position the log does not hold with that hash,
  is a miss.
- **The open** verifies the whole envelope, and returns the bytes or a miss.
  Any exception the projection's own decoder raises over verified bytes is a
  miss as well, never an error.
- **`VVP1` snapshots** are a miss on first open after the upgrade. The
  validation projection rebuilds from the log, as on a cold start.

**7. Version 6, and older readers.**
- **This build reads derived versions 2 to 6.** It writes 6 for kinds 1, 2,
  4, 7 and 9, 5 for order extents, and 4 for the state, the graph-version
  index and the catalog.
- **A shared blob is written in the newest version every announced reader
  reads.** This applies to a set's `dictionary` and `commits`, which an
  object store's followers read. While a present reader record says
  `maxDerived` below 6, those blobs are written as version 4, unchecked, as
  today. Every reader record this build writes says 6. A directory store has
  no records, so it writes 6 at once.
- **A local-only blob below version 6 is a miss at open**, and is rebuilt.
  This covers the local commit index below the catalog, `index/runs`,
  `index/commits` on a directory store, and the full checkpoints of kind 2
  that releases before extent sets wrote. The local commit index is rebuilt
  from the sets' `commits` blobs or the log (ADR 0161); runs and old
  checkpoints from the log. A directory written by such a release reads its
  log once at its first open under this one, and a checkpoint then starts
  its chain of sets (`Format3Tests`).
- **A set's blobs below version 6 are rewritten by maintenance**, ADR 0178's
  rewrite generalised: once no announced reader reads less than 6, the
  oldest set with any blob below the newest version is rewritten alone, one
  a round when the merge policy has nothing due. The rewrite is a pure
  function of the set's rows.
- **Until a set is rewritten, its older blobs are read as they always were:
  unchecked.** That window is stated, in the operator guide and on readiness's
  `retention`, as the number of sets still unchecked. It ends when the last
  is rewritten, and an operator ends it sooner by upgrading or forgetting
  the older readers.
- An older build finds a version-6 blob a miss, as it does any version it
  doesn't read. Its chain ends at the first set it cannot read, and it
  replays the log above, never failing and never returning a wrong result
  (ADR 0178 point 3).

**8. The flush is kept wherever a reader cannot verify.**
- **The default:** a derived blob is published by temporary file, flush and
  rename (ADR 0073).
- **When the flush goes:** only when the blob is written in a format that
  verifies itself, version 6 under point 3 or the envelope, **and** every
  announced reader reads that version. Then a torn write is a miss, so the
  flush buys no correctness.
- **When it stays:** a version-4 blob written while a preview.15 follower is
  announced keeps the flush, because that reader trusts what it reads.
- The writer asks the reader records it already reads (ADR 0160) whenever
  it chooses a version, and the file backend reads the version from the
  header it is about to publish: a checked kind in version 6 is not flushed,
  and every other blob is. A reader that reads no more than 5 finds a
  version-6 blob a miss before it reads a byte of it, so what such a reader
  can read is exactly what is flushed.
- **The rename always stays,** so no reader sees a half-written name. The
  manifest and `log/` are unchanged: every flush they have is the log's
  durability, not a cache's.

**9. The cost,** measured on the plan's machine (4 cores, Xeon at 2.1 GHz,
.NET 10.0.403), with the production fold copied into a benchmark:

| | Throughput |
|---|---:|
| The fold, over 16 KiB to 64 MiB | 4.3–5.5 GB/s |
| SHA-256, for comparison | 1.33 GB/s |

At REC M (25,000 commits, 5.05 M quads), that gives:
- **At open:** the commit index, about 2 MB, verifies in about 0.36 ms. The
  graph-version index, the state and the catalog are kilobytes.
- **On first read:** a 4 KiB page or a 16 KiB block takes about 1 to 4 µs to
  check, once per process.
- **Not paid:** a 211 MiB set (the largest at REC M, ADR 0159) would take
  about 50 ms to verify whole at open. Checking by block avoids that.

The pull request measures at REC M, per OS:
- open time;
- first-read time;
- head and as-of latency, against #146's budget of 3% over today, amended
  for one read (the amendment of 2026-10-28 below);
- the flush count, and the Windows store step's time, before and after point
  8.

The measurements are added here by dated amendment.

## Alternatives considered

- **One hash of the whole blob, checked at open, for every kind.** It is
  simple and verifies everything once. But it reads every byte of every set
  before the first read: about 50 ms for REC M's largest set with the fold,
  160 ms with SHA-256. On an object store it fetches every page of every set,
  which is the cost ADR 0177 removed. Kept only for the kinds read whole.
- **CRC32C or XxHash3, from `System.IO.Hashing`.** That is a package, and
  every package needs an ADR (constraint 4). The fold already exists and is
  measured: at 4.3 GB/s it is far from the cost that matters, which is the
  read. And one check means one way the store checks cached data, which is
  what #146's scope update asked for.
- **Check the commit index against the log's header chain.** Each entry
  carries its commit's header hash, so the entries could be compared with
  the log. But below the catalog that means reading the log's headers, which
  ADR 0161 exists to avoid. On a directory store the open already does this
  (`CommitIndexOpener`), and that stays.
- **Fail the read, as ADR 0177 point 6 did.** Rejected by #146: an error is
  not a wrong answer, but it is not the answer either, and the log holds the
  answer.
- **Verify a whole set at open instead of recovering mid-read.** That avoids
  the resume, at the cost of the first alternative. The maintainer chose
  quarantine, re-run and resume (#129, 2026-10-27).
- **Re-run every damaged read over the view below the blob, with an overlay
  of the log.** That is how an as-of read above the newest set is served
  (ADR 0138), and it is what a sought set falls back to. For any other run
  it changes the order a scan yields in, since an overlay yields its base
  first and its delta after, so a resumed scan would differ from the
  undamaged one. Rebuilding the run itself keeps the order, and the place.
- **Verify the commit index per block on first read, not whole at load.**
  Cheaper for a long history read sparsely, but a damaged block would then be
  met inside a synchronous read of an immutable index version, which would
  have to be replaced in place. Revisit if the load shows in a measurement.
- **Fetch a damaged object page again before rebuilding from the log.** A
  page damaged in the local cache would come back whole from the store,
  more cheaply than a window of the log. Not built: the page cache does not
  outlive its process, damage there is rare, and the log answers correctly
  either way.
- **Rebuild a damaged spill from the input.** The input is a stream the
  caller handed over and the load consumed. Keeping a second copy to rebuild
  from doubles the load's disk.
- **Drop every derived flush.** Rejected by the maintainer: a version-4
  blob read by a preview.15 follower is never verified, so a torn one would
  be trusted.
- **Leave older blobs in sets unchecked until a merge happens to rewrite
  them.** Under a policy that leaves the largest sets alone, that could be
  never. ADR 0178's rewrite already exists, and bounds the window.

## Consequences

- Local damage to any cached derived blob is a miss, then a rebuild, refetch
  or re-run. It is never an error and never a wrong result. A damaged spill
  fails its load before the commit, and the caller is told to resubmit.
- Opening reads no more than before, but for the commit index: a blob of it
  is verified whole when it is loaded, 80 bytes a commit, so the first read
  below the catalog in a process reads it once: 2 MB at REC M. On a
  directory store the open already compares every entry with the log
  (ADR 0089), so only the hashing is new. A key or column block costs one
  pass of the fold each time it is read; a term page once per process.
- `derived/` files of version 6 are slightly larger: 8 bytes per 10 KiB of
  commit index, and per 4 KiB of term section.
- **Checkpoints take about 18% longer, and that is accepted.** At REC M on
  Linux, 100 checkpoints took 30.6 s against 26.0 s (the amendment of
  2026-10-27): a checkpoint now checks every key block and term page it
  writes. Checkpoints run in the background, off the read path and off the
  commit path: no read waits for one, and a checkpoint is written off the
  sequencer and enters it only to publish (ADR 0070), so the checks add
  nothing to a commit's wait. Only Linux was measured: CI runs no REC M
  benchmark on Windows or macOS.
- **A subject's quads inside the largest set are about 8% slower, and that
  is accepted.** Over twenty alternating pairs at REC M on Linux (the
  amendment of 2026-10-28), the read's p50 is 7.40 µs against 6.85 µs:
  +8.0%, 95% confidence interval +1.4% to +11.3%, about 0.55 µs. It is the
  per-page check, and #146's budget of 3% is amended for this one read. The
  head's lookup, the head's subject read and the as-of lookup stay within
  the budget: their intervals include zero.
- **The load takes about 3% longer, and that is accepted.** Over twenty
  alternating pairs at REC M on Linux (the amendment of 2026-10-28), the
  load with 100 checkpoints and maintenance took 170.8 s against 165.6 s:
  +3.1%, 95% confidence interval +1.8% to +5.5%. Most of it, 4.6 s, is the
  checkpoints' checks above, which the benchmark runs inline. Maintenance
  took the same time, and merges rewrote 0.1% more bytes. The rest of the
  load, the commits and the runs their memtable flushes write, took 1.7 s
  more, +3.1% (+1.0% to +5.4%). The log's format is unchanged, so the runs'
  block checks are the likely cause of that part; it was not measured apart.
- While an older follower is announced, a set's shared blobs stay unchecked
  version 4, their flushes stay, and readiness says how many sets are not
  yet checked.
- `ExtentDamageTests` gets stricter, and every kind gets a damage test.
- Inference part 2 writes `derived/inference/` through
  `Dataset.WriteCheckedAsync` and `TryReadCheckedAsync`, and states no
  mechanism of its own.

## Checks

- **Checked against the accepted ADRs** (0001–0178 as accepted).
  - **0072:** derived data is rebuilt, not read forever. A version-6 blob is
    a cache of the log like every other. Amended: version 6.
  - **0073, 0071:** the flush before a derived rename, and what "published"
    means. Superseded in part (point 8).
  - **0138:** the view below a damaged blob is the as-of path. Not changed.
  - **0139:** a chain ends at the first set that fails. Point 4 cuts it there
    for a damaged set. Not changed.
  - **0158, 0178:** the rewrite of one set is ADR 0158's merge of one
    input. 0178's rulings are generalised from 4 to "below the newest".
  - **0160:** the reader records, and `maxDerived`, decide versions. Not
    changed.
  - **0161:** the `commits` blob's layout. Superseded in part.
  - **0174:** the snapshot's container. Amended.
  - **0177:** the checks themselves, now a miss. Superseded in part.
  - **0081:** a load commits once or not at all. Point 5 relies on it.
- **Layer ownership.** `Varve.Store` (layer 4): the formats, the checked
  envelope and `DerivedDamageException`. `Varve.Shacl.Store` (layer 5)
  writes its snapshot through `Dataset.WriteCheckedAsync`. `Varve.Protocol` (layer 5) owns the problem type.
  `Varve.Server` (layer 6) runs `varve load`.
- **Analyzer rule.** None new. The per-block check is `[HotPath]` and
  allocates nothing (`VARVE0003`).
- **Tests.** Each sweep checks a seeded share on a pull request and every
  case on the nightly, as `docs/testing.md` §8 sets out.
  - `DerivedDamageTests`: every blob of every kind but the order extents
    (runs, state, graph-version indexes, catalog, the local commit index,
    and each set's dictionary, commit index and graph index), a byte
    zeroed, flipped, or the blob cut, at every position. Each case must
    give a miss and the right answer at every position, by scan, by lookup,
    for every graph's version and for the position at every commit's time.
    The checks sit above the backend, so it runs on the memory backend;
    damage met after the open is `MidReadDamageTests`', on files and on
    objects.
  - `ObjectCacheTests.a_commit_index_blob_with_any_byte_zeroed_flipped_or_cut_is_a_miss`:
    #126's case in full, every byte of the commit-index blob in an object
    store's cache, 6,324 cases. Run against `main` it shows the 985 wrong
    histories of 1,920 zeroed entries; against this change, none.
  - `MidReadDamageTests`: on files and on objects, damage injected at every
    block a read reads, once the views are pinned: the head, the views at the
    sets' ends, and views inside each set, every pattern's stream with every
    term rendered, every quad looked up and every estimate. The result must
    equal the undamaged one byte for byte. Two damages in two blobs, and
    damage to every blob at once, must degrade the same way, down to the
    log. A set found damaged is written again by the writer's next round.
  - `ExtentDamageTests`: a failed read is now a failure.
  - `SpillDamageTests`: a load with a spill damaged at every block throws
    `BulkLoadSpillDamagedException`, and the dataset reopens at the head
    before the load.
  - `CheckedBlobTests`: the envelope's damage sweep, the binding to the
    dataset and the position, and a decoder's exception as a miss.
  - `FlushRuleTests`: under the power-loss model, a store with a reader
    announced at `maxDerived` 5 has every derived file renamed without a
    flush torn by a power cut. The set's shared blobs are version 4 and were
    flushed; only version-6 blobs were not, and the answers after the cut
    are the answers before it. With the reader at 6, the shared blobs are
    version 6 and not flushed either, and each torn blob is a miss with the
    right answer.

## Amendment, 2026-10-27 — the cost, measured at REC M

On this session's container (Intel Xeon, 4 cores at 2.1 GHz, 15 GiB, Ubuntu
24.04, .NET SDK 10.0.403), `Varve.Benchmarks --asof-seek file M`: REC M,
seed 20261015, a checkpoint every hundredth of the history with maintenance
after each, then opened again from a fresh cache. `main` at 3a8f636 and this
change, with the same benchmark, run alternately, four and three times. The
figures are medians of the runs.

| | `main` | This change | |
|---|---:|---:|---:|
| The open from a fresh cache | 496 ms | 291 ms | −41% |
| The head's lookups and a subject's quads, first pass | 79.5 ms | 67.8 ms | −15% |
| A quad's lookup at the head, p50 | 12.65 µs | 12.5 µs | −1% |
| A subject's quads at the head, p50 | 9.85 µs | 10.0 µs | +1.5% |
| A quad's lookup inside the largest set (213 MiB), p50 | 3.5 µs | 3.6 µs | +3% |
| A subject's quads inside the largest set, p50 | 7.6 µs | 7.9 µs | +4% |
| The load with 100 checkpoints and maintenance | 182.7 s | 189.2 s | +3.6% |
| Of which the checkpoints | 26.0 s | 30.6 s | +18% |
| `fsync` calls in the whole run (`strace -c`) | 28,222 | 27,938 | −284 |

- **Reads.** Three of the four p50s are within #146's 3%. The fourth, a
  subject's quads inside the largest set, is 0.3 µs over it. That is less
  than the spread between `main`'s own runs, 7.5 to 10.1 µs. The p99s vary
  more between runs than between builds.
- **The open is not slower.** It was faster in every run of this change
  than in any of `main`'s (270–346 ms against 463–548 ms). Why was not
  established here; what matters for #146 is that verifying at open does
  not show in it.
- **Checkpoints pay for the checks.** A checkpoint now takes a check of
  every key block and every term page as it writes them, 4.6 s more over
  100 checkpoints, while it no longer flushes its version-6 files.
- **The flush.** 284 derived files were published without a flush. Almost
  all the remaining `fsync`s are the log's, one per commit (ADR 0124), and
  those of the state, the catalog and the graph indexes, which stay version
  4.
- Windows and macOS are measured by the pull request's CI test durations,
  not by this benchmark. The Windows store step's time is the one to watch
  for point 8.

## Amendment, 2026-10-28 — the reads, over twenty alternating pairs

The maintainer asked for the as-of subject read, 4% over in the amendment
above, to be measured until its difference has a 95% confidence interval.
Same container and benchmark: `main` at 705b268 and this change at 1246f99,
both with the same benchmark, run alternately for twenty pairs, the order
swapped each pair. The interval is a bootstrap of the difference of the two
sides' medians, 20,000 resamples of the pairs.

| | `main` | This change | Difference | 95% CI |
|---|---:|---:|---:|---:|
| A quad's lookup at the head, p50 | 11.65 µs | 12.05 µs | +3.4% | −6.3% to +14.9% |
| A subject's quads at the head, p50 | 9.45 µs | 9.35 µs | −1.1% | −8.3% to +26.3% |
| A quad's lookup inside the largest set, p50 | 3.30 µs | 3.25 µs | −1.5% | −5.9% to +1.6% |
| A subject's quads inside the largest set, p50 | 6.85 µs | 7.40 µs | +8.0% | +1.4% to +11.3% |
| The open from a fresh cache | 494 ms | 281 ms | −43% | −47% to −38% |
| The head's first pass | 66.8 ms | 67.6 ms | +1.3% | −6.2% to +12.0% |
| The 100 checkpoints | 23.8 s | 28.35 s | +19.1% | +17.6% to +21.1% |
| Maintenance | 88.0 s | 88.15 s | +0.2% | −1.9% to +1.6% |
| The load with checkpoints and maintenance | 165.6 s | 170.8 s | +3.1% | +1.8% to +5.5% |

- **#146's budget is amended for one read.** A subject's quads inside the
  largest set costs +8.0% (+1.4% to +11.3%), about 0.55 µs at REC M, and
  that is accepted for the per-page check: the interval excludes zero, so
  the read is slower, most likely by about 8%. The other three reads keep
  the 3% budget: their intervals include zero. The median of the paired
  differences for the amended read is +4.4% (−3.6% to +12.1%).
- **The slow runs are not first-read cost, and both builds have them.** The p50 is over 300 scans of one
  subject in one view, and only the first reads its pages cold. In 3 of
  `main`'s runs and 5 of this change's, every read was about a third
  slower: the head's lookup about 14 µs against 11.5, the as-of lookup
  about 4.5 µs against 3.3. The as-of lookup separates the two modes
  cleanly (at most 3.6 µs, or at least 4.4). Over the runs in the fast
  mode only, the as-of subject read is 6.80 µs against 7.30 µs, +7.4%
  (−1.5% to +10.4%): the same as over all runs.
- The checkpoints' +19% and the load's +3.1% are stated as accepted in the
  consequences above.
