# Testing strategy

What each kind of test in this repository is for, and which pattern to reach
for when adding one. The rules here are enforced where they can be; where a
rule is only written down, it says so.

## 1. The W3C suites are the acceptance gate

A syntax feature is not done until its manifest entries pass or each failure
carries a written, justified exemption. `eng/ratchet.cs` holds the line:
`tests/Varve.Conformance.Tests/baseline/passing.txt` lists what passes today,
and the build fails when one of them stops.

**Guard counts go with every suite.** A manifest that silently stops being read
makes its suite pass by having nothing in it, and "more than zero" does not
catch a suite read as nine cases instead of three hundred and thirteen. Each
wired suite has its case count pinned in `SubmoduleGuardTests`, and a second
test fails when a suite has no count at all.

**The SPARQL syntax suites are a second list, parsed at their own version.**
`SparqlSuite.All` names the fifteen SPARQL 1.0, 1.1 and 1.2 syntax suites,
each with the version its cases are parsed at (`docs/spec/sparql-grammar.md`
§5); `SparqlGuardTests` pins their counts, 554 in all. They are kept apart
from `ConformanceSuite.All` because that list is about streaming document
readers — a format, the chunk-boundary oracle, the pull-reader agreement —
and a SPARQL case is parsed whole. What stands in for the oracle is the
UTF-8 / UTF-16 agreement: every case parsed both ways must give the same
tree or the same error at the same offset. A positive case also passes only
if the serialiser's text parses back to the identical tree, which is the
corpus half of the round-trip property (§3). Same baseline, same ratchet.

**The JSON-LD API suites are a third list, read as JSON.** `ConformanceSuite`
wires the `json-ld-api` submodule's `toRdf` manifest beside the rdf-tests
suites — its results are datasets, compared by isomorphism — and
`ConformanceSuite.JsonLdApi` holds `expand` and `fromRdf`, whose results are
JSON documents compared by the suite's own JSON-LD object comparison
(`JsonLdApiConformanceTests`): members in any order, arrays as sets except
under `@list`, language tags without regard to case. Each entry runs under
its stated options; the 1.0-only and generalized-RDF entries are excluded by a
rule `docs/spec/json-ld.md` §1 states, and `SubmoduleGuardTests` pins the
counts run and excluded. Same baseline, same ratchet.

## 2. The chunk-boundary oracle — for every streaming reader

**Standing rule: every syntax package runs the chunk-boundary oracle over its
own manifests, and a new one is not done until it does.**

The shape:

> Parse each corpus input whole. Parse it again split into two segments at
> every byte offset. Require the same answer every time — the same quads when
> accepted, the same error kind and the same position when rejected.

Four things make it worth more than the tests you would write by hand.

- **The expected value is computed, not authored.** Whatever the whole-document
  parse said is the answer the split parses must give. So the corpus costs
  nothing to add to: point it at a manifest and it covers every file in it,
  with no one reading them.
- **It is an oracle, not a conformance test.** It never asks whether the answer
  agrees with the specification — the suites do that. It asks only whether the
  parser agrees with itself, which is a different defect class and one the
  suites cannot see, because they parse whole documents.
- **Negative cases count.** A parser that rejects the right documents in the
  wrong place, or for the wrong reason, is reporting something a user will act
  on. The position is the part most likely to go quietly wrong, because a
  buffer-relative computation gets it right on a whole document and wrong on a
  fragment.
- **It finds a defect class, not a defect.** Turtle's reader had six of them,
  none reached by hand-written tests: a keyword decided on too few bytes, a
  number or language tag ending where the buffer did, a multi-byte character
  cut in half, an escape near the end mistaken for truncation, a comment with
  no newline consumed as finished, and a blank node counter not rewound. Every
  one is "the split changed the answer".

**The rule it enforces in the reader.** A token whose end is settled by the
byte *after* it must not be decided at the end of a buffer that can still grow.
The scanner is told whether its buffer is the document's last, waits when it is
not, and decides when it is (`docs/spec/turtle.md` §8).

**A line-based reader may not need the mechanism, and still runs the oracle.**
N-Triples dispatches a line only once its newline has been found, so its parser
never sees a fragment and no token can be decided early. That is an argument;
the oracle is the measurement, and it is what turns "should be fine" into 213
inputs that agree.

**Where it lives.** `tests/Varve.Conformance.Tests/ChunkBoundaryOracleTests.cs`,
over `ConformanceSuite.OracleCorpus` — the ratcheted suites plus those whose
inputs exist but whose results are not claimed yet. Kept apart from the
ratchet's corpus so that widening the oracle can never widen the baseline.
`EveryFormatIsCoveredByTheOracle` fails when a format is added without one.

## 3. Round trips are properties, not examples

Where a reader and a writer are inverses, say so as a property and let the
corpus supply the cases.

- **N-Triples canonical form is byte-stable**: parse a canonical document,
  write it, get the same bytes.
- **Turtle and TriG writing is a fixed point**: write, read back, write again,
  get the same bytes. Weaker than byte-stability on purpose — blank node labels
  are the parser's, so the first write may rename (`docs/spec/turtle.md` §4) —
  and still strong enough to catch a scheme that renames on every pass, which
  is what a pipeline of stages would pay for repeatedly.

Both run over every manifest input as well as over generated documents. The
generator exists to reach the cases the corpus does not: for the fixed point
that is a document mixing named and anonymous blank nodes, so that the renaming
path is exercised rather than avoided.

- **The SPARQL algebra round-trips exactly**: write a tree, parse the text,
  get the identical tree, by record equality (`docs/spec/sparql-algebra.md`
  §6). Stronger than a fixed point, because the tree is the meaning and the
  text is canonical. Held over generated trees inside the parser's image and
  over every positive corpus case.

## 4. Allocation is measured as a difference

Zero bytes per quad is asserted by parsing two documents of different sizes and
subtracting, not by a single absolute number. An absolute number measures the
harness as much as the parser; the difference cancels it.

The measurement runs on the fragmented paths too, with segment sizes small
enough to cut constructs in half, because a buffer copied per chunk is a cost
the whole-span path never shows.

**Every reading goes through `tests/AllocationMeter.cs`**, linked into each
test project that measures (issue #32). Two things outside the code under
test move a single reading of `GC.GetAllocatedBytesForCurrentThread`. A
collection in the window — started by any thread — retires the thread's
allocation context and the counter keeps its unused tail, up to one 8 KB
quantum, so a reading is taken inside a no-GC region and discarded if the
region broke. Tiered compilation can stack-allocate an object the caller
discards once the call is inlined, so the measured action returns what it
made and the meter keeps it alive. Everything else — a pool miss, a cache
filling on its first call — only ever adds bytes, so **every figure is the
minimum of a fixed number of readings** (`AllocationMeter.Readings`, with its
reason beside it), the baseline and the candidate alike, interleaved so that
whatever the process does over time falls on both. No two readings have to
agree: the loop that waited for two rounds to agree exactly never ended on a
Windows run whose pool missed on alternate readings (7d43295, milestone 7b).

**A measurement across a transport counts no socket.** The protocol's
per-solution figure crosses Kestrel's threads, so it is read process-wide,
and a socket allocates by how far the reader falls behind: a flush that
waits boxes its awaits, a woken reader is a thread-pool work item. Over a
socket the figure read 57 on four busy cores and 55 on a Windows runner
against 56 when quiet. The request therefore goes over the test host's
in-memory transport, whose pipes never make a writer wait, run their
continuations inline and keep every block, so what is counted is the
server's code (#91).

## 5. Fitness tests for the things a review forgets

Layer direction and layer declaration are analyzers (`DD0001`, `VARVE0005`).
The dependency register, the native-asset ban and the package metadata are
file-based apps under `eng/`, run in CI. Each of them replaces a rule someone
would otherwise have to remember.

**Prove a gate's failure path by running it.** A gate that has never failed is
a gate nobody has tested. `eng/native-assets.cs` is pointed at a real restore
graph that contains a native asset and at one that does not, and both outcomes
are asserted.

**Every quad source passes `tests/QuadSourceContract.cs`** (ADR 0022, amended
2026-10-30; #191), linked into each test project with a source of its own. A
query names its terms before it matches its patterns, and a constant the
source denies compiles to a pattern that never runs. So a source that loads
quads lazily and answers "no such term" before it has turns "not loaded yet"
into an empty answer, which is #183. `QuadSourceContract.Holds` makes the
source fresh for each term it yields, asks for that term first, completes the
source with a match of every quad and the caller's own step for any other
load, and fails when a term denied is in what the completed source yields. A
new source, a projection's reserved graphs included, adds a case before it is
read by anything; a lazy one makes its source behind at the lookup and
reaching its position in the read's wait, the order #183 needed.

## 6. Failure injection — for everything that writes to disk

A store that has only been tested on runs that never crashed has been tested on
the case that does not matter. Milestone 6a makes crashing a gate, the way §2
makes chunk boundaries one: **every write path is crashed at every operation,
and every byte of the log's writes**, and the result must open at a closed
commit no earlier than the last one acknowledged, read exactly as a run that
never crashed, and continue.

The double is the file system, the one process boundary the store has
(`IFileSystem`, internal to `Varve.Store`); the file backend runs on it
unchanged. `tests/Varve.Store.Tests/Faults/SimulatedFileSystem.cs` counts
operations, crashes at any of them, and builds the image a restart would find,
under three models stated in ADR 0073:

| Model | What the image keeps |
|---|---|
| process crash | everything written; the write in flight cut at any byte |
| power loss | what was flushed; each later write kept, dropped or torn at a 512-byte sector in any combination, which is any reordering; metadata journaled in order and cut anywhere after the last flush |
| lost directory entries | as power loss, with metadata cut anywhere at all; consistency is checked, durability is not |

Around those, `FaultInjectionTests` runs crashes during recovery itself,
`derived/` deleted or replaced by a longer log's after a crash, and copies of
the directory taken file by file while it is written. Every defect the suite
finds is fixed with the case kept as a named regression, and the injection
points are counted and reported. `VARVE_FAULT_SEEDS=<n>` runs the same suites
over many seeds and longer workloads, which is for hunting rather than for the
gate, and is not in CI.

The suites on real files that are not about durability — the model
properties on files, the storage contract on files, the derived tests on
disk — run on `UnflushedFileSystem`, the real file system with a flush that
does nothing (ADR 0090): a flush on the Windows runner is milliseconds, once
a commit. The crash suites here keep the simulated file system's flushes, and
the durability job flushes the device.

Milestone 6c adds the bulk loader to it (`BulkLoadTests`): a load is crashed
before each of its operations — every spill, every derived write, every
record — and inside every write to a segment at its first, middle and last
byte, under process crash and power loss, and must reopen at the head before
the load with exactly its state, or, once the closing record is durable, with
all of the load. At a hundred million quads, where the simulated file system
is too small, the bulk gate (`--bulk-gate … --crash-every-record` in
`tests/Varve.Benchmarks`) opens the real log cut at the start of and one byte
into every record of the load's commit, with `derived/` as it was before the
load, without copying it (`CutStorage`).

## 7. Extent maintenance — merges, readers and deletion

Milestone Extent maintenance (#110; ADRs 0158–0161) adds three properties
and a fault search, each sized for the land gate, with a variable that
raises it for the release gate (#129) and for hunting:

| Test | Land gate | Release gate |
|---|---|---|
| `ExtentMergeTests`: over generated histories with retractions and any schedule of checkpoints, drops and merges, the view at every position and the head equal the model's, before and after an open from the log and the extents alone | 40 schedules, about 3 s | `VARVE_EXTENT_SCHEDULES=<n>` |
| `ExtentMergeTests`: the policy keeps its bound over any sequence of set sizes | 500 sequences, under a second | — |
| `ObjectReaderScheduleTests`: over generated schedules of readers attaching, pinning, crashing, restarting and being forgotten, a writer holding several versions, and a second writer taking the epoch over, no extent a live announced version names is deleted, and every unnamed set goes once no record holds it | 60 schedules, about 4 s | `VARVE_READER_SCHEDULES=<n>` (1,200 ran clean at development) |
| `ObjectReaderScheduleTests`: a fault before and after every write and delete the maintenance workload makes, under `derived/` and `readers/` | every injection point checked for whole catalogs; every eighth also read at every position; about 20 s | `VARVE_EXTENT_FAULTS_FULL=1`: every point read in full, about 30 s |

#133 (ADRs 0177, 0178) adds the seek's properties, under the same variable:

| Test | Land gate | Release gate |
|---|---|---|
| `ExtentSeekTests`: over generated histories with retractions and any schedule of checkpoints, drops and merges, every view at every position equals the model by scan, every quad any view holds is looked up in every view and found where the scan found it, each subject's estimate is exact and its scan's count, and no read writes a narrowed run | 40 schedules, about 2 s | `VARVE_EXTENT_SCHEDULES=<n>` |
| `ExtentSeekTests`: format-4 sets are narrowed, then rewritten by maintenance as format 5, equal at every position throughout | 20 histories, about 2 s | — |
| `ExtentSeekTests`: a point lookup inside a set of 16,000 quads reads no more bytes than inside one of 2,000; a count over many of a sought set's rows is estimated and its scan exact | about 3 s and 2 s | — |
| `ExtentLayoutStatistics`: the layout measurements of ADR 0177, over a dataset `--asof-seek file M` kept | does nothing | `VARVE_SEEK_LAYOUT_DATASET=<directory>` |

All of them run in memory against `MemoryObjectStore`, so the Windows store
step pays only their in-process cost. The follower that keeps serving across
merges and deletions runs against Azurite and RustFS as well, in the
`storage (services)` job (`AzuriteExtentMaintenanceTests`,
`RustFsExtentMaintenanceTests`). The suites that state what the checkpoints
are run with merges off (`T.Options()`); the merge suites turn them on and
drive maintenance by hand, because a second dataset opened over one memory
storage, which only one process may write, would race a background merge.

## 8. How long the tests take

Every `dotnet test` step in CI writes a TRX and uploads it, pass or fail,
and the store's steps write their slowest tests, and the classes that held
the parallel slots, to the job summary (`eng/test-durations.cs`, #126). A
slow run is explained from its own artifacts; two runs compare test by test.
Each store step has a time limit at about twice its slowest green run, and a
hang dump below it that names the tests in flight. The hang dump counts from
the last test that ended, so a step whose longest test runs on alone after
the rest sets it above that test's run: the `storage (services)` job's
`AzuriteFaultTests`, the HTTP fault workload with seven kinds at every
request (§9), runs alone for six minutes or more, and the job's hang dump
waits ten.

Some searches are wider than a pull request can wait for. Each such test
explores a pull request's share of its space by default, and the whole of
it when its variable is set. **The property is the same at either depth**;
only how much of the space is searched changes, and the pull request's share
is a subset of the nightly's. Where a share is chosen by a seed, each run
draws a new one, so that pull requests between them search the whole space;
the seed starts the test's output and any failure's message, and its
variable replays the run. The nightly run (`.github/workflows/nightly.yml`,
on `main`) sets every depth below.

| Variable | Test | Pull request | Nightly |
|---|---|---:|---:|
| `VARVE_FAULT_RECOVERY_STRIDE` | `FaultInjectionTests.CrashDuringRecovery.a_crash_during_recovery_recovers`: crashes the workload at every *n*th of its operations, then its recovery at every operation | 25, from operation 1 + seed mod 25 (about 11,500 points) | 1 (about 288,000 points; the gate's was 5, 57,679) |
| `VARVE_FAULT_RECOVERY_SEED` | the same: the seed that chooses where a pull request's share starts | a new one each run | n/a at stride 1 |
| `VARVE_FAULT_PROCESS_STRIDE`, `_SEED` | `FaultInjectionTests.ProcessCrash.a_process_crash_anywhere_recovers_to_the_last_closed_commit`: a process crash at every *n*th operation of the workload, at every cut of its write | 2, from operation 1 + seed mod 2 (about 4,400 and 5,200 points) | 1 (about 8,800 and 10,500) |
| `VARVE_FAULT_BATCH_STRIDE`, `_SEED` | `FaultInjectionTests.BatchedCrash.a_crash_inside_a_batch_recovers_to_the_last_closed_commit`: the same inside batches, with three power-loss images an operation | 2 (about 6,500 and 7,000 points) | 1 (about 13,000 and 14,000) |
| `VARVE_FAULT_POWER_STRIDE`, `_SEED` | `FaultInjectionTests.PowerLoss.a_power_loss_with_writes_reordered_up_to_the_flush_recovers`: power lost at every *n*th operation, six images each | 2 (about 4,300 and 3,900 points) | 1 (about 8,600 and 7,900) |
| `VARVE_FAULT_TORN_STRIDE`, `_SEED` | `FaultInjectionTests.TornWrites.a_write_torn_at_any_byte_recovers_to_the_last_closed_commit` (ADR 0214 point 1): every write the file backend issues, to the log, `derived/` and the small files, cut at every byte under the process model | 64 (908 of 58,169 cuts, about 7 s) | 1 (all, about 3 min) |
| `VARVE_FAULT_SECTOR_STRIDE`, `_SEED` | `FaultInjectionTests.SectorCuts.every_sector_cut_of_every_unflushed_write_recovers` (ADR 0214 point 1): every unflushed write at every operation cut at every 512-byte sector, the others kept and dropped, the metadata to the last flush and whole, enumerated | 16 (about 1,600 of 25,376 images, about 7 s) | 1 (all, about 70 s) |
| `VARVE_FAULT_DISKFULL_STRIDE`, `_SEED` | `FaultInjectionTests.DiskFull` and `FaultInjectionTests.FullDiskBatches` (ADR 0214 point 2): a full disk at every allocation, and inside generated batches a full disk, or an I/O error, at every write and every flush, followed by the next write's recovery or a reopen; the recovery's open at every allocation runs whole either way | 4 (about 980 of 3,926 points, about 30 s) | 1 (all, about 90 s) |
| `VARVE_FAULT_REAL_POWER_STRIDE`, `_SEED` | `FaultInjectionTests.RealPowerCut.a_power_cut_on_real_files_recovers_to_the_last_closed_commit` (ADR 0214 point 4): the workload on real files, mirrored into the simulated file system, and at every *n*th operation each of the models' images written back to the disk and opened through `DiskFileSystem` | 8 (336 of 2,682 images, about 11 s) | 1 (all, about 80 s) |
| `VARVE_FAULT_KILL_STRIDE`, `_SEED` | `FaultInjectionTests.RealKill.a_process_killed_at_any_flush_recovers_to_the_last_closed_commit` (ADR 0214 point 3): the test assembly run as its own host on real files, killed just before and just after every *n*th flush | 25 (8 of 194 kills, about 8 s) | 1 (all 194, about 3 min) |
| `VARVE_OBJECT_FAULT_SWEEP_STRIDE`, `_SEED` | `Objects.ObjectFaultTests.after_a_fault_at_any_request_the_dataset_reopens_at_the_last_closed_commit_with_nothing_lost_or_doubled`: each of four faults at every *n*th request of the workload (not `VARVE_OBJECT_FAULT_SEEDS`, the workloads hunted) | 2 (532 of 1,068 points) | 1 (all) |
| `VARVE_CACHE_DAMAGE_STRIDE` | `Objects.ObjectCacheTests.a_commit_index_blob_with_any_byte_zeroed_flipped_or_cut_is_a_miss` (#146, ADR 0213): reopens a cache whose version-6 commit index blob has one byte zeroed, flipped, or is cut there, at every byte of its entries, directory and header, one case in *n*; any position read differently fails it (#126's 985 of 1,920 zeroed entries did on format 4) | 8, from case seed mod 8 (791 of 6,324 cases, about 12 s) | 1 (all 6,324, about 65 s) |
| `VARVE_CACHE_DAMAGE_SEED` | the same: the seed that chooses which eighth | a new one each run | n/a at stride 1 |
| `VARVE_EXTENT_DAMAGE_STRIDE`, `_SEED` | `ExtentDamageTests.an_order_extent_with_a_byte_zeroed_flipped_or_cut_is_a_miss_never_a_different_answer_or_a_failed_read` (#133, #146): a format-5 extent with one byte zeroed, flipped, or the blob cut there, at every position of the middle set's six order extents, opened again from the log and the extents and read at every position by scan and by lookup; a failed read fails it too | 16, from case seed mod 16 (962 of 15,384 cases, about 9 s) | 1 (all, about 62 s) |
| `VARVE_DERIVED_DAMAGE_STRIDE`, `_SEED` | `DerivedDamageTests.every_kind_with_a_byte_zeroed_flipped_or_cut_is_a_miss_never_a_different_answer_or_a_failed_read` (#146, ADR 0213): every byte of every derived blob but the order extents (runs, state, graph indexes, catalog, the commit index, and each set's dictionary, commit index and graph index) zeroed, flipped or cut; every position's scan, lookups, graph versions and time lookups | 16 (1,894 of 30,302 cases, about 9 s) | 1 (all, about 2 min) |
| `VARVE_MIDREAD_DAMAGE_STRIDE`, `_SEED` | `MidReadDamageTests.on_files_…` and `on_objects_damage_at_any_block_a_read_reads_is_answered_from_the_log` (ADR 0213 point 4): damage at every block a read reads, after the open, alone, two at once and all at once; the streamed answer equals the undamaged one byte for byte | 4 (45 of 177 on files, 59 of 234 on objects, about 4 s) | 1 (all, about 11 s) |
| `VARVE_ENVELOPE_DAMAGE_STRIDE`, `_SEED` | `CheckedBlobTests.an_envelope_reads_back_whole_and_any_damage_is_a_miss`: a checked envelope (kind 9) with one byte zeroed, flipped or cut, at every byte of its directory and header and every 97th of its bytes | 4 (1,154 cases, about 1 s) | 1 (4,621, about 3 s) |
| `VARVE_SPILL_DAMAGE_STRIDE`, `_SEED` | `SpillDamageTests.a_damaged_spill_fails_its_load_with_nothing_committed`: a bulk load's spills damaged at each of their first 600 bytes while it runs; it fails with `BulkLoadSpillDamagedException` and nothing committed | 4 (150 loads, about 7 s) | 1 (600, about 24 s) |
| `VARVE_FENCE_SCHEDULES` | `Objects.ObjectEpochPropertyTests.a_stalled_claimant_never_fences_past_a_newer_epoch_whatever_was_sealed_or_consolidated_meanwhile` (#181): a claimant stalled before its listing or its fence's create, while the next epoch commits, seals, consolidates and deletes, and may stop at any of its writes; the chunks left never fall in epoch, and a holds nothing once the next epoch fenced | 300 schedules, about 3 s | 3,000, about 30 s (run clean at development) |

The nightly run also sets section 7's release-gate depths for Extent
maintenance: `VARVE_EXTENT_SCHEDULES=400` (40 on a pull request; it sets
both `ExtentMergeTests`' and #133's `ExtentSeekTests`' schedules),
`VARVE_READER_SCHEDULES=1200` (60; the count run clean at development) and
`VARVE_EXTENT_FAULTS_FULL=1`.

The nightly run's second job sets `VARVE_LONG_PROPERTIES=1` for the SHACL
projects: `Varve.Derivation.Tests`, `Varve.Shacl.Tests`,
`Varve.Shacl.Sparql.Tests` and `Varve.Shacl.Store.Tests`. Their properties
that have a long count then run at ten times a pull request's iterations.
The REC gate's three ways are compared at all 1,001 positions of REC S
(every 250th and the last, five, on a pull request, in about a minute) and
at 100 positions of REC M (none on a pull request) (ADR 0176).

The variables of §6 that hunt rather than gate (`VARVE_FAULT_SEEDS`,
`VARVE_OBJECT_FAULT_SEEDS`, `VARVE_OBJECT_FAULT_STEPS`,
`VARVE_HTTP_FAULT_STEPS`) stay what they were: neither a pull request nor the
nightly run sets them.

## 9. The live Azure leg — what the emulator cannot answer

The object-storage adapters are proved against Azurite and RustFS in CI (ADR
0134). Azurite copies the service's error precedence, but it is not the
service: the status codes it answers, its races, its visibility and its
latency are its own. The live leg (#143) runs the same suites against a real
storage account, and a few tests that only mean something there.

**Two traits, and every class carries one.** Every test class in
`Varve.Store.Tests.Objects` carries `Category=AzureObjects` or, for the
failure-injection workloads, `Category=AzureObjectsFaults`; `TraitGuardTests`
fails a class that carries neither or both, so no class drops out of CI
unseen. The optional CI leg `azure-objects.yml` runs the first against the
account; `azure-objects-faults.yml` runs the second, about an hour. Both
are dispatched by the maintainer in the `azure-objects` environment, which
the repository does not hold, so neither runs unattended. The live tests
skip unless the container is named:

```bash
VARVE_AZURE_BLOB_ENDPOINT=https://<account>.blob.core.windows.net/ \
VARVE_AZURE_BLOB_CONTAINER=varve-test \
VARVE_AZURE_TENANT_ID=<tenant> \
dotnet test --project tests/Varve.Store.Tests/Varve.Store.Tests.csproj --configuration Release \
  -- --filter-trait Category=AzureObjects
```

and the same with `Category=AzureObjectsFaults` for the workloads.

`VARVE_AZURE_CONTAINER`, a container URL, names it in one variable instead,
as `azure-objects.yml` does. The identity is the workload's and never a key
(ADR 0137): on a workstation the fixture runs `az account get-access-token
--tenant` itself, pinned to `VARVE_AZURE_TENANT_ID`, so a profile signed in to
several tenants answers for one, and `AZURE_CONFIG_DIR` picks the profile; in
CI the adapter's own sources apply (`AZURE_FEDERATED_TOKEN_FILE`). Only the
fixture pins; the adapter's production path is untouched.

**Every run is isolated.** A run writes under `<utc-time>-<random>/` in the
container, each test under a prefix of its own below it, and deletes only what
it wrote, at the end of each test. Nothing outside the run's prefix is listed,
written or deleted; a suite written for a container of its own runs under the
prefix through `PrefixedObjectStore`, with the real adapter beneath.

| Suite | Also on | What the live run adds |
|---|---|---|
| `AzureAccountObjectStoreContractTests`: the seven contract cases, the two race properties included | memory, Azurite, RustFS | the service's own answers to the five operations |
| `AzureAccountStorageContractTests`: the storage contract over the backend | memory, Azurite, RustFS | — |
| `AzureAccountExtentMaintenanceTests`: a follower across merges and deletions | memory, Azurite, RustFS | — |
| `AzureAccountWriterFenceTests` (`WriterFenceCases`): the claim, the fence slot, the release, a stalled writer superseded, six writers claiming at once, the operator's takeover | memory, Azurite | the race of the claim on the service |
| `AzureAccountFaultTests`: the HTTP failure-injection workload, seven fault kinds, the two applied-then-lost ones included (`HttpAppliedThenCancelled`, `HttpAppliedThenTimedOut`) | Azurite, every request | every `VARVE_AZURE_FAULT_STRIDE`-th request (every seventh unless set; `1` is every request, about an hour) |
| `AzureAccountLostAnswerTests` (`LostAnswerCases`): a reader-record replace applied and then cancelled, or timed out, for a named writer and a holder-less follower (#145) | Azurite | the fault the account showed, on the account |
| `AzureAccountConditionalWriteTests` | — | the status and error codes the service answers a create that finds a blob, a stale replace and a replace of nothing; a 503, a 500 and a lost answer on one create, which lands at most once |
| `AzureAccountReadAfterWriteTests` | — | a second client, with a pool of its own, reads and lists a blob at once; a follower reads a sealed segment at once |
| `AzureAccountRaceTests` | — | `VARVE_AZURE_RACERS` × `VARVE_AZURE_RACE_ROUNDS` (32 × 20 unless set) racing one create and one replace, with the losers' answers counted |
| `AzureAccountLatencyTests` | — | p50, p95 and p99 of a 1 KiB create, a 1 KiB read and a one-quad commit, `VARVE_AZURE_LATENCY_COMMITS` of each; informational, never a gate |

The results of a run, emulator beside live, go into a report under
`docs/reports/`, with every difference found linked to an issue.
