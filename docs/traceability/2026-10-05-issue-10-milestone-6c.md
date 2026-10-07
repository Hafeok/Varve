# Milestone 6c — the dictionary on disk, the bulk loader, the browser backend, the soak's causes

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#10](https://github.com/Hafeok/Varve/issues/10), [#61](https://github.com/Hafeok/Varve/issues/61) |
| **Date** | 2026-10-05 |
| **Tool** | Claude Code 2.1.289, a cloud session started from the Android app; one subagent, in a git worktree, built the browser backend ([its record](2026-10-05-issue-10-milestone-6c-browser-backend.md)) |
| **Model** | `claude-opus-5-5` (Claude Opus 5.5), configured and served — confirmed from the session's own metadata (`get_session`: `session_context.model` and `last_served_model`), not from memory |
| **Session identifier** | `session_01JZjxTGanMjuGR2KWbPjWRi` |
| **Branch** | `ccr-73c21edc-57a4zy`, landing on `main` as one pull request |

## The prompt

> Milestone 6c (Refs #10, #61): bulk loader, checkpoint policy, on-disk dictionary, browser backend, and the soak. 6a is merged (format version 1 frozen, ADRs 0070–0077). Read the 6a traceability record, storage-format.md, spec 1.5 and ADRs 0070–0077 before planning. AGENTS.md applies; plan first; one PR, red only on CS0618.
>
> 1. #61 first. Separate the causes of the soak's working-set growth (168 MB median to 730 MB, peaks 2.8 GB). Checkpoints built in memory are the known one: write checkpoints as a streaming merge of the runs and the dictionary section directly to the derived blob, bounded by one block per input run plus the output buffer; state the bound. Find and fix whatever remains; report each cause with its measurement. Re-run the one-hour soak and report working set, file handles and derived/ counts; the 1.0 soak gate is a flat working set within a stated band.
> 2. On-disk dictionary (6a's deferral): the term dictionary as derived state in the same run and checkpoint machinery, so open is O(tail since the last checkpoint) in time and bounded in memory, not O(terms). Lookup by term and by id through the synchronous blob read; the hot-path allocation tests extend to it. ADR.
> 3. Bulk loader per ADRs 0076 and 0077: streaming input from any Varve parser, external sort into runs spilling to derived/, merge-join against the pinned runs for the effective delta, one multi-record commit, validators over the on-disk overlay. Gate: 100 million generated N-Quads into an empty dataset and into a populated one, bounded memory (state the bound and assert it), a crash injected at every record boundary during the load leaving the dataset at the previous head with no partial commit visible, result equal to the reference model, throughput against pyoxigraph's bulk loader.
> 4. Checkpoint policy: background work under the explicit maintenance option from the 0042 note (every N commits or M bytes of log, configurable), checkpoints as 6a defined them, the as-of latency property measured (cost proportional to log distance), deferred deletion of runs a pin still holds proven by a test that holds a pin across a merge.
> 5. Browser backend: OPFS with synchronous access handles in a worker (decision 0071's synchronous read on the browser); IndexedDB fallback declaring TransactionCommitted durability where OPFS sync handles are unavailable. Contract tests in headless Chromium; the determinism test across desktop and browser (same script, byte-identical log/). If OPFS sync handles cannot be reached from .NET WASM without JS interop that breaks AOT, say so with evidence and bring the alternative; maintenance stays off by default in the browser.
> 6. Replica bootstrap as a file-level operation: ship a checkpoint plus the log tail into a second dataset directory and open it; a property that the replica equals the source at the shipped position. No protocol.
>
> Report as 6a did: ADRs, defects found with seeds, iteration counts, the soak with causes separated, benchmarks with hardware, proposed spec changes, and what milestone 7 needs from the maintainer.

The plan was posted in the session before any code and the work went ahead
on it; the prompt did not ask to wait for approval, and every decision is
filed unaccepted, so the maintainer's review is on the pull request.

## The report

### The decisions, as ADRs

All seven are **Proposed**, filed with their decision sets unaccepted, so the
build is red only on `CS0618` until the maintainer accepts them (ADR 0066).

| ADR | Decides | Brief |
|---|---|---|
| [0078](../adr/0078-checkpoints-streamed-and-written-by-policy.md) | A checkpoint is a streaming merge of the runs, written straight to its blob, bounded by one 4 KiB block per input section of the order being written, plus the writer's 64 KiB output buffer, plus the fences (40 bytes per 128 keys per section). A memtable flush and a disk merge are the same merge. `CheckpointPolicy` (every N commits, every M bytes of log, keep K) runs as maintenance, under the explicit option. A run a pin still reads is retired, not deleted, and is deleted once the last reader lets go. | 1, 4 |
| [0079](../adr/0079-the-term-dictionary-on-disk.md) | The term dictionary on disk, carried by the runs: every run has a term section holding the entries of the canonical ids its commits allocated. Lookup by id is an offset table; lookup by term is a hash index searched by interpolation; both go through the synchronous blob read, behind two bounded direct-mapped caches. Open is O(log tail since the last checkpoint or state), not O(terms). | 2 |
| [0080](../adr/0080-derived-blocks-compressed.md) | Derived keys compressed in their blocks of 128: the first key whole, then a diff-index byte and varints. This makes derived format **version 2**; `log/` is untouched. A reader holds a run's directory in chunks below the large object heap, and a checkpoint's sparsely: every sixteenth fence. | 6a's list, 1 |
| [0081](../adr/0081-the-bulk-loader.md) | The bulk loader. A new term is referred to by a 120-bit SHA-256 reference in a segment that sorts as its final id will. The load is an external sort spilling to `derived/bulk/`, a merge-join against the pinned runs, ranks as final ids, and one commit streamed in records. Validators read the delta where it lies. Memory is bounded by `MemoryBytes` + 2 B per quad of the delta + the dataset. Recovery is bounded too. | 3 |
| [0082](../adr/0082-the-soak-gate.md) | The 1.0 soak gate: over the last 50 minutes every sample of the working set, less the dataset's own (what a reader holds of its runs' directories, and its commit table), lies within ±25% of their median, and the last ten minutes' median is within 10% of that of minutes 10–20; handles and `derived/` files stay within fixed bounds. | 1 |
| [0083](../adr/0083-replica-bootstrap-is-a-file-copy.md) | Replica bootstrap is a copy of files through the storage contract (`Dataset.ShipAsync`): the manifest, the segments up to the shipped commit, a checkpoint at or below it. No protocol. | 6 |
| [0084](../adr/0084-the-browser-backend.md) | `Varve.Store.Browser` (layer 5). OPFS sync access handles in a worker give ADR 0071's synchronous read, through source-generated `[JSImport]`, AOT- and trim-safe. IndexedDB is the fallback on the main thread. Both declare `Durability.Committed`. Maintenance stays off by default. | 5 |

The specification stays at **1.5**; the changes proposed are below.
`storage-format.md` §7 is rewritten for derived format version 2.

### #61: the soak's working set, causes separated

Ablations of the 6a workload on the 6a code, 20 minutes each, on this machine.
Working set in MB, sampled every 30 s after a full collection:

| Run | Mean | Last 10 min, median | Peak |
|---|---:|---:|---:|
| Everything (6a's soak) | 354 | 449 | 1,387 |
| No checkpoints | 739 | 1,186 | 2,152 |
| No as-of reads | 272 | 311 | 552 |
| Neither | 188 | 178, flat | 252 |

The two causes:

1. **Checkpoints built in memory.** A checkpoint was the whole state
   materialised and sorted six ways. Each one put hundreds of MB on the large
   object heap, which the collector keeps as its high-water mark and
   fragments: in the full run the live heap was about 26 MB and the heap
   about 136 MB.
2. **As-of reads that materialise the log tail since the nearest
   checkpoint.** Their cost grows with the distance to that checkpoint.
   Without checkpoints the distance grows without bound, which is why the
   no-checkpoints run is the *worst*. Allocation ran at about 1 GB/s.

Removing both leaves a flat working set. Fixed by:

- streaming checkpoints from the runs (ADR 0078, bound above);
- bounding the as-of distance with the policy: a checkpoint every 1,000
  commits, keeping 12, so an as-of read up to 10,000 back has one within
  1,000 (R2's cost, measured below).

On 6a's code nothing else showed: with both removed, the working set was
flat at 178 MB. With the policy on and the dataset growing for an hour, two
more causes appeared, below.

**The soak, re-run.** `--soak 60 --policy` (ADR 0082) has one writer
committing batches of 50 every 25 ms, and two readers that pin and scan and
then read as of up to 10,000 positions back. Maintenance runs in the
background, the memtable holds 20,000, and the policy checkpoints every
1,000 commits, keeping 12. Each run below reached about 126,000 commits and
3.1 million quads, and reopened at its last commit in about 1 s.

It took three runs, each finding the next cause:

| Build | Working set median, min 10–20 → 50–60 | Peak | LOH, min 50–60 | Fragmented | Live heap | Band (±25%) | Drift (≤10%) |
|---|---|---:|---:|---:|---:|---|---|
| 6a, for reference (no policy, checkpoints every 2 minutes) | 256 → 730 MB | 2,812 MB | | | | no | no |
| Streamed checkpoints and the policy (`24e2cf6`, `428762b`) | 254 → 492 MB | 681 MB | 300 MB | 222 MB | 93 MB | no (−31%/+92%) | no (+94%) |
| … with fences in chunks (`7f4b846`) | 186 → 342 MB | 438 MB | 37 MB | 32 MB | 94 MB | no (−42%/+56%) | no (+84%) |
| … and checkpoints held sparsely (`87b692e`) | 153 → 200 MB | 218 MB | 28 MB | 24 MB | 34 MB | **yes** (−21%/+20%, less own) | **no** (+20.6%, less own) |

Throughout the last run, open file handles stayed at 73–78 and `derived/`
at 17–21 files. Allocation fell from 23 to 8 GB a minute, mostly as-of tails.

The causes after 6c's streaming were separated by 20-minute ablations on
the policy build. Minutes 15–20:

| Ablation | Live heap | Fragmented | LOH | Working set |
|---|---:|---:|---:|---:|
| The policy | 37 MB | 66 MB | 90 MB | 263 MB |
| … no as-of reads (0.4 GB/min allocated) | 36 MB | 49 MB | 72 MB | 294 MB |
| … `DOTNET_GCConserveMemory=5` (the LOH compacted) | 38 MB | 20 MB | 43 MB | 180 MB |
| … fences in chunks | 44 MB | 24 MB | 30 MB | 217 MB |

3. **Fences rebuilt on the large object heap.** Every merge and checkpoint
   allocated its fences and block starts as dataset-sized arrays, and the
   runtime does not compact that heap. The growth survives removing as-of
   reads. Compacting the LOH removes it, and so does holding the fences in
   64 KiB chunks: the LOH flat at 28–40 MB for the hour.
4. **Twelve copies of the fences, and the collector's headroom.** Each kept
   checkpoint held a whole set of the dataset's fences: 73.5 MB of 94 MB
   live at the hour. The collector keeps memory committed in proportion to
   what is live, about twice the heap. A checkpoint's sections are now held
   sparsely (ADR 0080): 3.4 MB for twelve.

**The gate is not met.** Over the last 50 minutes the working set less the
dataset's own stays within ±25% of its 160 MB median (127–191 MB), and
raw within ±25% too. The last ten minutes' median is +20.6% over minutes
10–20 against the 10% allowed. The residual is accounted for:

- the live heap less the dataset's own is flat (7.6 → 8.1 MB);
- the dataset's own grew 18.5 MB, two thirds of it the commit table, at 144
  bytes a commit, measured;
- fragmentation grew 7 MB;
- committed memory grew 45 MB, about twice the heap's growth.

The working set less committed memory is flat at 75–77 MB. The rest is the
collector's headroom over a commit table that grows by design. Closing it
needs one of:
- a commit table of a few dozen bytes a commit in chunks, not an object per
  commit;
- an archive horizon (T3) that bounds it;
- a gate that counts the collector's headroom over the dataset's own.

That is put to the maintainer below. The dataset's own at the hour was
27.6 MB, 8.8 B per quad: run directories 6.0 MB, checkpoints held sparsely
3.4 MB, commits 18.2 MB.

### The dictionary on disk

Open reads the log only after the newest checkpoint or the persisted state.
No dictionary is rebuilt: the runs' term sections are the dictionary.
`a_checkpoint_is_the_dictionary_and_a_dataset_opens_from_it_alone` proves
this; `every_term_resolves_both_ways_from_runs_on_disk_and_after_reopening`
covers lookups across flushes, merges and reopens. The hot-path allocation
test extends to it: `a_dictionary_lookup_on_disk_allocates_nothing` looks
terms up both ways through runs on disk and allocates nothing.

### The bulk loader: the gate

100 million generated N-Quads, a heap cap of 1.5 GiB (`DOTNET_GCHeapHardLimit=0x60000000`),
`BulkLoadOptions.MemoryBytes` 1 GiB, so the stated bound is 1 GiB + 2 B × 10⁸ ≈ 1.27 GB
plus the dataset. The runtime asserts the cap: an allocation past it fails the run.

| Load | Time | Quads/s | Peak managed heap | Peak working set | `log/` | `derived/` |
|---|---:|---:|---:|---:|---:|---:|
| 100M into an empty dataset (three runs) | 874–1,027 s | 97,389–114,399 | 1,397–1,401 MB | 1,556–1,667 MB | 2.30 GB, 23.0 B/quad | 7.55 GB, 75.5 B/quad |
| 100M into a dataset of 10M, half of them overlapping (two runs) | 737–745 s | 134,309–135,640 | 1,418–1,477 MB | 1,576–1,607 MB | 1.85 GB, 17.7 B/quad | 6.35 GB, 60.4 B/quad |

Both loads end at exactly the expected count: 100,000,000, and 105,000,000
over the populated dataset. A sample of 10,000 generated quads is present and
1,000 never generated are absent. The reference model is the generator itself.
At small scale the property `a_bulk_load_equals_the_same_operations_committed_the_ordinary_way`
checks equality with the ordinary commit path (150 iterations).

The 100M gates ran on `97a966b`. The changes after it are fences in
chunks, checkpoints held sparsely and the single-decode seek. Neither the
load nor its commit changed with them; the run loader did. So the 1M gate
was re-run on the final code, crash checks included, and passed.

**Crashes.** The log was cut at the start of every record of the load's
commit, and one byte into a sample of 16 records. Each cut log was opened
over `derived/` as it was before the load:

- populated: all 1,578 + 15 cuts opened at the previous head with exactly
  its quads, in 2,546 s;
- empty: all 2,197 + 16 cuts likewise, in 5,311 s.

The window after the commit closed and before the state was written also
opened at the load's head with all its quads: 3.5 s with 577 MB of heap
(populated) and 5.3 s with 568 MB (empty). At small scale, the
simulated-file-system suite crashed at every operation of a load and inside
every segment write. That was 236 operations and 299 crash points; of the
598 images, 532 reopened at the previous head and 66 with the whole load,
and none with part of it.

**Against pyoxigraph's bulk loader.** The same 10 million generated
N-Quads from one file (1.01 GB) into an empty store on disk, run in turn on
the quiet machine:

| Loader | Time | Quads/s | On disk |
|---|---:|---:|---:|
| pyoxigraph 0.5.11 `Store.bulk_load`, two runs | 56.6–58.9 s | 169,923–176,656 | 2.69–2.95 GB, 269–295 B/quad |
| Varve, `MemoryBytes` 256 MiB under a 512 MiB heap cap, two runs | 71.0–71.2 s | 140,390–140,809 | 0.87 GB, 87.0 B/quad (`log/` 19.9, `derived/` 67.1) |
| Varve, `MemoryBytes` 1 GiB, no cap | 66.2 s | 151,085 | the same |

- **Speed.** Varve is about 0.8 times pyoxigraph's speed.
- **Disk.** Varve uses about 0.3 times the disk, including a log that
  pyoxigraph does not keep.
- **Memory.** Varve's memory is bounded and asserted: peak heap 374–377 MB
  under the cap. pyoxigraph's bulk loader bounds nothing that it states.
- **Where the time goes.** In Varve's 71 s, parsing, resolving and sorting
  the input took 26.5 s and the commit's passes 44.6 s. The parse alone runs
  at 1.07M quads a second. What is left is single-threaded resolution on the
  parser's thread and six order sorts written one after another.
- **Without a cap.** The uncapped run's peak heap (3.3 GB) counts garbage the
  collector had no reason to collect. The capped runs are the measure of the
  bound.

### Defects found

| # | Defect | Found by | Fixed in | Kept as |
|---|---|---|---|---|
| 1 | A term key compared language tags byte for byte, so `en-GB` and `en-gb` were two terms to the on-disk dictionary | the model property, seed `0TyDyZl1BdDa` (16 property tests failed) | `24e2cf6` | `regression_a_language_tag_is_found_in_either_case` |
| 2 | A multiply-only 64-bit hash gave the bulk loader a false collision at 3M quads (`s256610`, `s286696`) | the bulk gate at 3M | `c9a65a5`: references are 120 bits of SHA-256; the term-key hash is SplitMix64 | the collision check refuses a load rather than merging two terms |
| 3 | A bulk load kept every spill until it ended: at 100M it would fill the disk | the 100M gate | `03eceb9` | each pass deletes its inputs |
| 4 | Out of memory under the 1.5 GiB cap in the commit phase: the passes' buffers overlapped | the 100M gate | `617398c` | ADR 0081's carve-up |
| 5 | Recovery after a crash during a bulk load was not bounded: the open scan held every record of an unclosed commit, and a crash after the close replayed the load into a memtable | the 100M gate's crash check, out of memory at cut 700 | `6213757`: the scan holds at most 64 MiB of a commit; open adopts a delta run that continues the index | `the_open_scan_holds_a_bounded_part_of_any_commit` (100 iterations), `a_load_whose_state_was_lost_reopens_from_its_delta_run` |
| 6 | Fences and block starts rebuilt as dataset-sized arrays on the large object heap at every merge and checkpoint, fragmenting it: the working set grew with the live heap flat | the one-hour soak with the policy, then ablations (no as-of reads; `GCConserveMemory`) | `7f4b846` | `chunked_fences_are_searched_as_one_array` (500 iterations) |
| 7 | Twelve kept checkpoints each held the dataset's whole fences in memory, twelve of every thirteen fences held, with the collector committing about twice that | the second one-hour soak | `87b692e`: a checkpoint's sections held sparsely | `a_sparse_section_answers_as_a_dense_one` (60 iterations, sections of up to 235 blocks) |
| 8 | A property's torn half assumed the log had a segment; a load that changes nothing commits nothing (test defect) | seed `0006_MyMQdJ1` | `7f4b846` | the property itself |

Two performance faults:

- The first 100M commit took 963 s because the ranks fell to disk lookups.
  `7c3c5ad` holds them in memory until the allocations are written.
- Point lookups were 2.6 times slower than 6a's after compression: each
  seek decoded the same block up to three times. `6ff6a94` decodes it once.

### Property iteration counts

New in 6c:

| Property | Iterations |
|---|---|
| A bulk load equals the same operations committed the ordinary way (canonical N-Quads and the dictionary counters, before and after reopening) | 150 |
| The open scan holds a bounded part of any commit, whole and torn | 100 |
| A replica equals the source at the shipped position | 150 |
| An as-of read reads exactly the log distance to its checkpoint | 60 |
| A term section on a blob finds every key and no other (interpolation) | 40 |
| A key block decodes to the keys it encoded | 5,000 |
| Chunked fences are searched as one array | 500 |
| A sparse (checkpoint) section answers as a dense one | 60 |
| A block cut short or left long does not decode | 1,000 |

6a's properties are unchanged and pass on derived format 2:

- the model property, 1,000 on files;
- many records, 333;
- disk runs, 333;
- records cut at every boundary, 40 each;
- determinism, 250;
- I6, 40 each.

The fault-injection suites pass too.

**Tests run on the final code:**
- store: 151, none failed, one skipped (the existing skip);
- SPARQL store: 13;
- evaluation: 28;
- conformance: 6,018, with the W3C submodules checked out;
- the native AOT smoke, publishing with no IL warnings;
- the browser contract pages in headless Chromium 141: 39 on the OPFS
  worker and 21 on the IndexedDB main thread. `log/` from the same
  deterministic script was byte-identical to `FileStorage`'s across 12
  files.

### Checkpoint policy, as-of latency, deferred deletion

- **Pins across merges.** `a_pin_held_across_a_merge_keeps_its_runs_until_it_is_disposed`
  holds a pin across a merge on the memory and file backends. It reads the
  replaced runs, and they are deleted only after the pin is disposed.
- **The policy.** `the_policy_writes_a_checkpoint_every_so_many_commits_and_keeps_the_newest`
  and `…every_so_many_bytes_of_log_in_the_background` cover both triggers.
- **R2.** `an_as_of_read_reads_exactly_the_log_distance_to_its_checkpoint`
  (60 iterations) asserts it exactly: the log bytes an as-of read reads
  equal the bytes between its checkpoint and its position.

**As-of latency, measured** (`--asof-latency`): an as-of read at a log
distance from its checkpoint, in commits of 50 quads; the median of 21 reads (5 at 10,000):

| Dataset | 0 | 10 | 100 | 1,000 | 10,000 |
|---|---:|---:|---:|---:|---:|
| 100,000 quads | 0.01 ms | 2.15 ms | 22.1 ms | 195 ms | 2,464 ms |
| 2,000,000 quads | 0.01 ms | 3.32 ms | 26.9 ms | 277 ms | 3,006 ms |

The cost is proportional to the distance, about 0.2–0.3 ms per commit, and
nearly independent of the dataset's size: twenty times the quads cost 1.2 to
1.5 times as much at each distance. That is R2. The policy is what bounds
the distance; the soak's 1,000 commits bound an as-of read near 280 ms.

### Replica bootstrap

`Dataset.ShipAsync(target, position)` copies the manifest, the segments up to
the commit's end and the newest checkpoint at or below it, through the
storage contract, into empty storage. Opened, the replica is at the shipped
position and equals the source as of it (150 iterations, across memory and
file backends). The AOT smoke ships a bulk-loaded dataset and opens it
natively.

### Benchmarks

Machine: the same as 6a's.

- Intel Xeon @ 2.80 GHz, 4 cores, 15 GiB.
- Ubuntu 24.04, kernel 6.18, ext4 on a virtio disk, a cloud container.
- .NET 10.0.12; pyoxigraph 0.5.11.

The commands and the tables are in
[`tests/Varve.Benchmarks/README.md`](../../tests/Varve.Benchmarks/README.md),
milestone 6c.

**Sizes on disk** (`--file-sizes`, milestone 4's 1,000,000 quads), against
6a's:

| | 6a | 6c |
|---|---:|---:|
| `log/` per quad | 36.8 B | 36.8 B (format version 1, unchanged) |
| Disk runs per quad | 193.5 B | 86.4 B |
| A checkpoint per quad, keys and dictionary | 221.3 B | 86.4 B |
| Held in memory per quad, a run's directory | 1.5 B (fences) | 1.88 B (fences and block starts) |
| Held in memory per quad, a checkpoint's directory | 1.5 B | 0.09 B (sparse) |

The keys are stored at 5.40–7.28 bytes each across the six orders. That is
within 0.2 bytes of the ideal delta coding of the same keys (5.19–7.09). With
the ids scattered as content-derived ids would be, it would be 17.29–33.90:
ADR 0012's locality, now spent.

**Scans** (`FileScanBenchmarks`, a million quads in disk runs, pinned):

| | 6a | 6c |
|---|---:|---:|
| Every quad | 44.1 ms | 49.1 ms |
| One predicate | 2.94 ms | 2.63 ms |
| The default graph | 8.01 ms | 7.69 ms |
| 10,000 subject lookups | 28.4 ms | 36.9 ms |

Every scan allocates 512 bytes and nothing per quad, as in 6a.

The first 6c measurement of the lookups was 75.2 ms. Each seek decoded
three blocks: the lower bound's, the upper bound's (for a point lookup the
same block), and the first block read (the lower bound's again).
`KeySection.Range` decodes it once (`6ff6a94`). What remains is the price of
compression on a seek: one block decoded rather than read raw, against a
disk footprint 2.2 times smaller.

### Proposed specification changes

- **Language tags in a bulk load.** RDF 1.1 compares language tags ignoring
  case. The spec's term identity is silent on case. A bulk load writes new
  tags lowercased and finds existing ones in either case (ADR 0081).
  Proposed: §1's term identity says tags are compared ignoring case and that
  a store may normalise the case of a new tag.
- **R2 as an exact property.** "Cost is proportional to the log distance"
  is tested as: the bytes read equal the log bytes from the checkpoint to the
  position. Proposed: §10's R2 row says so.
- **I3 and allocation order.** A bulk load allocates new canonical ids in
  reference order, not first-occurrence order. Both satisfy I3, and the
  spec does not fix the order. Proposed: say that it does not, so that no
  test or client relies on first-occurrence order.

### What milestone 7 needs from the maintainer

- **Acceptance of ADRs 0078–0084**, which turns this pull request green.
- **The soak gate's drift criterion** (ADR 0082). The ±25% band holds. The
  10% drift does not: +20.6%, the collector's headroom over a commit table
  of 144 bytes a commit. Choose one:
  - a compact commit table, a few dozen bytes a commit in chunks: a
    milestone 7 change to the store's state, which readers share without
    locks;
  - an archive horizon (T3) that bounds the table;
  - restating the criterion to count the headroom over the dataset's own.

  The last needs no code, and is the one this session would not choose by
  itself.
- **The archive horizon (T3).** Checkpoints are kept by count. Pruning the
  log below a horizon is the next storage decision.
- **A multi-threaded bulk resolve.** The parser's thread resolves and spills
  the input: 207K–351K operations a second at 100M (284.8–390.4 s empty,
  484.4 s populated, where more terms already exist and are looked up),
  against a parse rate of 1.07M quads a second. The load itself (sort, merge-join,
  commit) is already parallel per pass. Whether to spend a second buffer to
  overlap parse and spill is ADR 0081's last alternative.
- **Per-run filters.** A lookup by term on a dataset with many runs reads one
  hash index per run. A filter per run (bloom or similar) is a derived
  format version; whether to spend it is the question.
- **A browser content security policy.** The OPFS worker loads a JavaScript
  module. A host with a strict CSP needs to allow it, and the package should
  say how (ADR 0084).

## After the pull request opened

### The maintainer's decisions on the pull request

> Accepting 0078–0084 on the branch; merge follows. Yes, watch #62.
>
> Soak drift: the commit table becomes derived, paged state (position-to-offset index in derived/, read through the blob contract, bounded recent-entry cache). Own slice after #62 merges, before milestone 7, with the one-hour soak re-run. #61 stays open and the gate is not restated. Report the working set less collector headroom alongside, but the criterion stays the working set.

### What was done

- **The acceptances** are the maintainer's, in `4d9f800`: all 27 rulings of
  the seven sets, and the seven ADRs' status lines. Without the `CS0618`
  override the solution then builds with no warning and no error, and
  `eng/decision-sets.cs` counts 579 decisions, all accepted.
- **The index** records 0078–0084 as accepted.
- **The soak's drift** is left to its own slice, as decided. ADR 0082 is not
  restated, its criterion stays the working set, and #61 stays open.

## Addendum, 2026-10-06 — the commit index, derived and paged

> **Recorded contemporaneously** by the same session, in the same record,
> under [ADR 0033](../adr/0033-commit-traceability.md). Tool, model and
> session identifier are as above: Claude Code 2.1.289, `claude-opus-5-5`,
> `session_01JZjxTGanMjuGR2KWbPjWRi`. Developed with AI assistance under human
> review.

### The prompt

> Slice: paged commit index (Refs #61, #10). Milestone 6c is merged. The commit table held in memory (144 bytes per commit, growing without bound) becomes derived, paged state:
>
> - A position-to-record-offset index under derived/, written in the same block-and-fence form as runs, versioned under the derived rule (unknown version is a cache miss), rebuilt from the log on open when missing or stale, and extended incrementally by the default projection as commits close.
> - Read through the synchronous blob contract; a bounded in-memory cache of the newest entries (size an option; state the default and the reason); every store path that consulted the table (as-of resolution, Diff, subscriptions resuming from a position, replay) reads through the index.
> - The lock-free shared-state change gets an ADR: what readers see during an index extension, and the test that a pin opened before an extension still resolves every position it could before.
> - Properties re-run: I5 (as-of by timestamp), R2, R3, subscriptions from arbitrary positions, recovery with the index missing, stale, or from another dataset; the determinism test unchanged since log/ does not change.
> - The one-hour soak re-run on the final code: working set, handles, derived/ counts, and the working set less collector headroom alongside. The pass criterion is the working set within the band and under the drift limit. If it still fails, report the remaining cause with its ablation rather than restating the gate.
> - Allocation and lookup benchmarks before and after; point lookup must not regress from 6c's 36.9 ms.
>
> One PR, red only on CS0618; report as a dated addendum to the 6c traceability record and close #61 only if the gate passes.

When the prompt arrived, #62 had not yet merged; the work began on a local
branch off its head and was moved onto `main` when it merged, an hour later.

### The decision

[ADR 0089](../adr/0089-the-commit-index-is-derived-and-paged.md), filed
unaccepted with its decision set (four rulings).

- **An entry per commit, 80 bytes.** Each commit's entry holds:
  - its timestamp;
  - its header hash;
  - its location;
  - the two dictionary counters after it;
  - the log's bytes to its end.

  Settings are not in it. They change only at settings commits, and the
  index keeps those positions apart.
- **Blobs.** The entries live in blobs under `derived/index/commits/`, kind 4
  of the derived header (storage-format.md §7.4). Each blob is organised in
  blocks of 128, with each block's first timestamp as its fence: 8 bytes in
  memory per 128 commits. A timestamp is found by the fences, then at most
  seven entries read.
- **The cache.** `DatasetOptions.CommitCache` sets how many of the newest
  entries stay in memory, **4,096 by default**, holding between that and
  twice that. That is 320 to 640 KiB whatever the length of the log, and
  what reads near the head reads memory. Maintenance writes the oldest half
  out at twice the cache, and merges the newest two blobs while the newer is
  as large as the older, so the blob count stays logarithmic.
- **Readers keep the version they captured.** Entries in memory are written
  before the version that reaches them is published. A blob is taken for the
  length of a read. A blob merged away is deleted only once no reader holds
  it. A reader that finds one closed reads the current version, capped at its
  own head.
- **Open.** The log walk hands each commit to the index instead of keeping
  them all. Blobs continuing the chain are checked entry by entry against the
  log. From the first that disagrees, the index is rebuilt and written out as
  it goes.

Every reader of the old table reads through the index: `AsOfAsync`,
`PositionAt` and `AsOfTimestampAsync`, `DiffAsync`, subscriptions,
`CatchUpAsync` and `RebuildAsync`, replay on open, checkpoints and their
checks against the log, the projection's run checks, shipping a replica, and
the bulk loader.

`log/` is unchanged, and the determinism test passes unchanged: browser and
desktop `log/` are byte-identical across 12 files.

### Defects found

| # | Defect | Found by | Fixed in |
|---|---|---|---|
| 1 | A version whose blob had been merged away fell back to the current version to resolve a timestamp, and could answer a position past its own head | the new model property, seed `bRQn6DRNs2j4` | `3240132`: capped at the version's head |
| 2 | Two datasets open on one storage — the model harness reopens a live dataset — deleted each other's index blobs: the new one's open deleted every blob its chain did not use, including one the live one had just written and not yet opened again | the file-backed model properties at a cache of 2, under background maintenance | `8a1b378`: an open deletes only blobs it listed and could not use, never one reaching past its head |
| 3 | The same two datasets number their blobs from the same sequence, so one retiring a blob deletes the other's just written under the same name | the same, once 2 was fixed | `8a1b378`: a page or merge that does not read back is done again on the next round, not failed |
| 4 | The contract suite's naive backend was not safe for the store's own concurrency; nothing in its dataset case had run maintenance in the background before (test defect) | one full run, once | `2d32835`: a lock in `ListStorage` |

The collisions in 2 and 3 cost nothing but a retry:
- both instances read the same log, so colliding blobs hold the same entries;
- the storage contract keeps a deleted blob readable to whoever has it open.

### Tests and iteration counts

- **New:**
  - the index answers as the list of its entries, for random appends, pages
    and merges, through versions captured before their blobs were merged away
    and deleted: 200 iterations;
  - a version held across an extension resolves every position it could
    before. Its blobs survive 180 more commits of paging and merging and are
    deleted only once it lets go. A version captured without holding resolves
    every position too, through the current one;
  - an index missing, damaged, stale beside a log that diverged after 30
    commits, or another dataset's is rebuilt from the log. Each case checks
    every entry, `PositionAt` at every timestamp, `DiffAsync` and a
    subscription from position 41.
- **Re-run with a cache of 2,** so that every one pages out, merges and
  reads back: every suite of `Varve.Store.Tests`, at 6a's and 6c's
  iteration counts:
  - the model property (I5 resolved at every timestamp, R2, R3, I7/I8 on
    reopen, subscriptions from arbitrary positions), 1,000 on files;
  - many records, 333;
  - disk runs, 333;
  - the log cut at every byte, and at every boundary on files, 40 each;
  - determinism, 250;
  - the fault-injection suites, crashing now inside index writes, merges and
    deletes too;
  - the storage contract cases, in Chromium as well.
- **Store suite:** 157 tests, three full runs, none failed (one skipped, the
  existing skip). Ten runs of the file-backed properties also passed after
  defects 2 and 3.

One storage-contract case,
`a_dataset_commits_checkpoints_reopens_and_reads_as_of_over_the_backend`,
failed once, in a full run after defects 2 and 3 were fixed. **Defect 4**
was in the test, not the store. `ListStorage`, the contract suite's
deliberately naive backend, had no lock. Nothing in that case had run
maintenance in the background before, and the commit index paging at a cache
of 2 now did, writing blobs while the test thread wrote a checkpoint. The
store calls a backend from its sequencer and its maintenance at once (ADR
0070), so a backend must allow it. `ListStorage` now takes one lock around
everything (`2d32835`). The case has not failed since: 20 runs of the
contract suites, and every full run.

### Benchmarks, before and after

The same machine as 6c, the same session. `--commit-index 100000` opens
100,000 single-quad commits on files, then reads through the public members
that use the table:

| | Before (6c) | After |
|---|---:|---:|
| Managed heap held by the open dataset | 15.5 MB, 154.6 B a commit | 1.4 MB, 14.1 B a commit |
| Open | 0.58 s | 0.62 s |
| `PositionAt` | 1.37 µs, 0 B | 3.07 µs, 0 B |
| `SettingsAtAsync` | 0.76 µs, 0 B | 0.69 µs, 0 B |
| `DiffAsync`, 10 commits | 80.3 µs, 29,213 B | 88.9 µs, 29,772 B |
| A subscription from a position, 10 commits | 75.1 µs, 33,636 B | 97.6 µs, 34,196 B |

An old position's entry is now a read of 80 bytes through the blob read,
where it was an array access:
- a timestamp reads up to seven entries;
- a commit read reads its own entry and the one before, for its predecessor's
  hash;
- each read hash is a 32-byte array, about 56 bytes allocated.

| `FileScanBenchmarks` | 6c (record) | Before, this session | After |
|---|---:|---:|---:|
| Every quad | 49.1 ms | 36.7 ms | 43.6 ± 9.8 ms |
| One predicate | 2.63 ms | 2.55 ms | 2.29 ms |
| The default graph | 7.69 ms | 6.45 ms | 7.19 ms |
| **10,000 subject lookups** | **36.9 ms** | 33.8 ms | **29.5 ms** |

The point lookup does not regress from 6c's 36.9 ms. Scans do not read the
commit index; the full scan's spread in the after run is its error bar.

### The soak, on the final code

`--soak 60 --policy`, as ADR 0082 states it: 125,498 commits and 3.1 million
quads in the hour, reopened at its last commit in 0.99 s. Samples every
30 seconds after a full collection.

| Minutes | Commits | Live heap | Fragmented | LOH | Working set, median (max) | The dataset's own | Handles | `derived/` files |
|---|---:|---:|---:|---:|---|---:|---|---|
| 0–10 | 20,941 | 10 MB | 18 MB | 22 MB | 162 MB (246) | 0.8 MB | 60–77 | 6–21 |
| 10–20 | 41,873 | 11 MB | 13 MB | 17 MB | 140 MB (203) | 2.8 MB | 74–80 | 18–22 |
| 20–30 | 62,839 | 13 MB | 9 MB | 14 MB | 138 MB (219) | 4.5 MB | 77–80 | 20–22 |
| 30–40 | 83,757 | 14 MB | 11 MB | 14 MB | 138 MB (213) | 6.1 MB | 77–82 | 19–24 |
| 40–50 | 104,670 | 16 MB | 13 MB | 17 MB | 142 MB (192) | 7.6 MB | 79–81 | 21–23 |
| 50–60 | 125,498 | 17 MB | 12 MB | 15 MB | 142 MB (212) | 9.0 MB | 78–83 | 20–25 |

The criterion is the working set's 30-second samples over the last 50
minutes. Beside it, as the maintainer asked, the working set less the
collector's headroom: less committed memory, plus the heap.

| | Median | Band (±25%) | Drift (≤10%), the last 10 minutes against 10–20 |
|---|---:|---|---|
| **Working set** | 141 MB | **no**: −13.3%/**+27.3%** | **yes**: +1.1% |
| Working set less the dataset's own | 133 MB | no: −12.0%/+31.0% | yes: −3.0% |
| Working set less the collector's headroom | 100 MB | yes: −13.4%/+16.3% | yes: +7.7% |

**The gate is not met, by two samples of a hundred.**
- **Drift holds now.** Before this slice it failed at +20.6%. The commit
  table was the dataset's own growth, and it is gone. The dataset's own at
  the hour is 9.4 MB, 3.0 B a quad, against 27.6 MB in 6c: 6.0 MB of run
  directories and 3.4 MB of sparse checkpoints, with the commit index's
  fences in the first.
- **The band fails at two samples:** minute 28 (180 MB, +27.3%) and minute
  55 (179 MB, +27.0%). At both, the live heap is 12 and 24 MB, as
  everywhere. The
  collector's committed memory, at 64–67 MB in most samples, was 111 and
  105 MB. Working set less committed memory is 73–75 MB in every ten-minute
  window.

**The remaining cause, with its ablation.** The one-hour run again, as-of
reads off (`--no-asof`):

| One hour, the policy on | With as-of reads | Without |
|---|---:|---:|
| Allocation | 9–16 GB/min | 0.3 GB/min |
| Working set median | 141 MB | 194 MB |
| Band | −13.3%/+27.3% | −24.5%/+31.1% |
| Drift | +1.1% | +25.8% |
| GC committed memory, minutes 10–20 → 50–60 | 68 → 67 MB | 101 → 143 MB |
| Live heap, minutes 10–20 → 50–60 | 11 → 17 MB | 9 → 16 MB |
| Working set less committed memory | 73–75 MB | 72–80 MB |

The as-of reads are not the cause: without them the working set is worse.
What moves the working set in both runs is memory the collector holds
committed and not live, and it moves against the allocation rate:
- allocating 10 GB a minute, the collector runs often and keeps its
  commitment near 65 MB, with an occasional excursion — the two samples;
- allocating 0.3 GB a minute, it seldom collects, and what it holds
  committed rises 42 MB in the hour over a heap of 10–20 MB.

The store's live memory is small and close to flat in both, and what grows
of it is the dataset's own. The confirming run is the gate's workload unchanged, under
`DOTNET_GCHeapHardLimit=0x8000000`: a bound of 128 MB on what the collector
may hold, about four times the live heap. It completed 125,048 commits, with
no out-of-memory and no exception:

| One hour, the policy on | Default runtime | Heap hard limit 128 MB |
|---|---:|---:|
| Working set median | 141 MB | 116 MB |
| **Band (±25%)** | **−13.3%/+27.3%** | **−14.6%/+12.0%** |
| **Drift (≤10%)** | **+1.1%** | **+7.6%** |
| GC committed memory, per ten minutes | 63–89 MB | 37–50 MB |
| Working set less committed memory | 73–75 MB | 70–73 MB |
| Handles; `derived/` files | 74–83; 18–25 | 74–83; 18–26 |

Bounded by the runtime, the same store holds the band and the drift with
room to spare. So the last two samples are the collector's choice of how
much to keep committed, not memory the store holds.

The gate as ADR 0082 states it is the default runtime's working set, and it
is not met. Three ways to meet it, for the maintainer:
- the store could declare a recommended host configuration (a heap limit,
  or `GCConserveMemory`);
- it could allocate less in as-of reads, so that the collector's excursions
  are smaller;
- the gate could be judged on the working set a host is expected to run
  with.

This session did not pick one: the last is a restatement, and the first two
are decisions.

#61 stays open. The gate is not restated.
