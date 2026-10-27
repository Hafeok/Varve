# 0213 — Cached derived data is verified before use: a check per block, and damage is a miss

## Status

**Filed unaccepted by 1.0 readiness A (#146, under #129), 2026-10-27** (ADR
0066). Acceptance is the maintainer's act on the pull request. The plan it
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
- **A kind read by range is verified per block,** the first time that block
  is read: runs, checkpoints, the dictionary extent and order extents.
- **Paid once per block per process.** A block that verified is not hashed
  again while it stays in memory or in the page cache. An evicted page that
  is fetched again is checked again.

**3. Per kind:**

| Kind | Block | Version |
|---|---|---|
| Commit index (4) | 128 entries (10,240 bytes). One check per block, after the directory's first timestamps. | **6** |
| Runs and checkpoints (1, 2) | Each key block, as format 5's key sections. **The term section in 4 KiB pages**, from the entries' offset to the filter's end, so entries, offsets, the hash index and the filter are all covered. | **6** |
| Dictionary extent (7) | Its term section in 4 KiB pages, as a run's | **6** |
| Order extents (6) | Format 5's key and column blocks, unchanged | 5 |
| Graph-version index (5) | The whole: its contents are its directory | 4, unchanged. `TryLookupAsync` now verifies the directory's SHA-256 once before its first lookup. |
| State (3), catalog (8) | The whole, as now | 4, unchanged |
| Checked envelope (9, new) | 64 KiB | **6** |
| Spills | Each spill's sort block, with its check written after it | — (never outlives its load) |

The catalog and the state stay at version 4, so an older build still attaches
to the chain and reads the log above where it ends (ADR 0178 point 3).

**4. Damage is a miss, wherever it is found.** A mismatch at load means the
blob is not loaded. A mismatch mid-read — a block, a page, a column —
raises an internal `DerivedDamage`, never an `IOException` to a caller,
and is handled in four steps:
1. **Quarantine.** The dataset stops using the blob.
   - On files, the blob is deleted. Maintenance writes it again from the log
     or from the set's other blobs.
   - On an object store, the damaged page is dropped from the page cache
     and fetched once more. If the store's own copy fails too, the set is cut
     from this instance's chain: its chain ends below the set, and the reads
     the set served are answered from the log above.
2. **Re-run.** The operation runs again over the view without that blob.
   That view is the sets and checkpoints below it plus the log's overlay
   above them, the path as-of reads above the newest set already take (ADR
   0138). It has the same answer.
3. **Resume.** A scan that has already yielded rows does not start again. It
   continues in the same order, over the new view, after the last key it
   yielded. So a streamed result has no duplicate and no gap, and equals
   the undamaged result byte for byte.
4. **Again, down to the log.** A second damage during the re-run degrades
   the same way, one blob further down. The last view is the log alone,
   which is checked by its own hashes (ADR 0072). Damage there is the log's,
   which is outside this ADR.

`ExtentDamageTests` no longer counts a failed read as a pass.

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
offers `CheckedBlob`, which writes and opens a blob of **kind 9** through
any `IDerivedStore`.
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
  This covers the local commit index below the catalog, `index/runs`, and
  `index/commits` on a directory store. The local commit index is rebuilt
  from the sets' `commits` blobs or the log (ADR 0161); runs from the log
  above the newest set.
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
  it chooses a version.
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
- head and as-of latency, against #146's budget of 3% over today;
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
- Opening reads no more than before. A range-read block costs one pass of
  the fold the first time it is read.
- `derived/` files of version 6 are slightly larger: 8 bytes per 10 KiB of
  commit index, and per 4 KiB of term section.
- While an older follower is announced, a set's shared blobs stay unchecked
  version 4, their flushes stay, and readiness says how many sets are not
  yet checked.
- `ExtentDamageTests` gets stricter, and every kind gets a damage test.
- Inference part 2 writes `derived/inference/` through `CheckedBlob`, and
  states no mechanism of its own.

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
- **Layer ownership.** `Varve.Store` (layer 4): the formats, `CheckedBlob`
  and `DerivedDamage`. `Varve.Shacl.Store` (layer 5) writes its snapshot
  through `CheckedBlob`. `Varve.Protocol` (layer 5) owns the problem type.
  `Varve.Server` (layer 6) runs `varve load`.
- **Analyzer rule.** None new. The per-block check is `[HotPath]` and
  allocates nothing (`VARVE0003`).
- **Tests.** Each sweep checks a seeded share on a pull request and every
  case on the nightly, as `docs/testing.md` §8 sets out.
  - `DerivedDamageTests`: for each kind of point 3, on files and on objects,
    a byte zeroed, flipped, or the blob cut, at every position. Each case
    must give a miss and the right answer at every position.
  - `CommitIndexDamageTests`: #126's case in full, every byte of the
    commit-index blob, all 1,920 cases. It is run against `main` to show the
    985 wrong histories, then against this change to show none.
  - `MidReadDamageTests`: a property on files and on objects that injects
    damage at every block a scan reads. The streamed result must equal the
    undamaged one byte for byte, with no duplicate and no gap. A second
    damage injected during the re-run must degrade the same way, down to the
    log.
  - `ExtentDamageTests`: a failed read is now a failure.
  - `SpillDamageTests`: a load with a spill damaged at every block throws
    `BulkLoadSpillDamagedException`, and the dataset reopens at the head
    before the load.
  - `CheckedBlobTests`: the envelope's damage sweep, the binding to the
    dataset and the position, and a decoder's exception as a miss.
  - `FlushRuleTests`: under the power-loss model, a store with a reader
    announced at `maxDerived` 5 tears a write at every derived publish.
    Every blob that reader can read was flushed before it was renamed, and
    nothing it reads is torn. With every reader at 6, no derived publish
    flushes, and each torn blob is a miss with the right answer.
