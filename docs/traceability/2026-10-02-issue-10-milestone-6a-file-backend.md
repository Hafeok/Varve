# Milestone 6a — the file backend, format version 1, failure injection

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#10](https://github.com/Hafeok/Varve/issues/10) |
| **Date** | 2026-10-02 |
| **Tool** | Claude Code 2.1.287, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5` (Claude Opus 5.5), configured and served — confirmed from the session's own metadata, not from memory |
| **Session identifier** | `session_01DeGpEKupBcx5yx69ZJz63h` |
| **Branch** | `claude/sleepy-shannon-8wh26z`, landing on `main` as one pull request |
| **Commits** | 14, from `6ad54c2`; the last two add this record and regenerate `CHANGELOG.md`, where the rest are listed under milestone 6a |

## The prompt

### First message

> Session 6a: file backend, format version 1, failure injection
> Milestone 6a makes Varve durable: the file implementation of the storage abstraction (ADR 0018, 0040), the first frozen on-disk format, and the failure-injection suite that proves the recovery guarantees the specification states. Read `docs/brief.md`, the specification (`log-and-projection-model.md`, version 1.3), ADRs 0010–0018, 0040–0047, 0058, 0065, 0069 and the storage research note before planning. `AGENTS.md` applies in full: the storage contracts and every new public type are written under the DD rules from the start, `[Contract]` and `[DomainModel]` citing decisions, wrappers not primitives, `[HotPath]` on the read loops. Plan first, wait for approval; one PR, red only on `CS0618`; the public API sketch is in the plan.
> A. Decisions, to be written as ADRs (Accepted, with revisit conditions where stated)
>
> 1. Storage engine: our own, and it is two different things. `log/` is an append-only segment writer: no keys, no compaction, no index. `derived/` holds immutable sorted runs in the six key orders ADR 0041 already uses in memory, with a memtable (the newest runs), tiered background merges, and recovery by replay from the projection's position; the Varve log is its write-ahead log, so no second WAL exists. No managed engine is adopted; the research note's candidates are the rejected alternatives, with ZoneTree's lack of a storage seam and FASTER's hash model named. Revisit: a workload at milestone 7 where tier merges cannot keep up with commit rate.
> 2. Synchronous reads over asynchronous storage. The storage contract's read side is synchronous: a readable blob exposes its length and `Read(ByteOffset, Span<byte>)`, and runs and checkpoints are scanned through it with the same code as in memory. On desktop the file backend serves reads through a memory-mapped view (`MemoryMappedFile`, BCL) or `RandomAccess.Read`, chosen by benchmark and recorded. Writes, flushes, opens and sealing are asynchronous. The evaluator's cursor stays synchronous, so ADR 0052's pinned-read lifetime holds. Revisit: a host where no synchronous read exists (6b decides this for the browser).
> 3. Format version 1, frozen at the first prerelease that writes it. ADR 0045's in-memory layout was provisional and may change in this session; what this session ships becomes version 1, and from the tag that publishes it Varve commits to reading version 1 forever (the 1.0 definition's read-forever rule starts now, not at 1.0). Layout: a dataset manifest `log/MANIFEST` (magic, format version, dataset id, creation settings hash); segment files `log/NNNNNNNN.seg` with a header (magic, format version, dataset id, segment id, first position) and records; a record is a fixed header (length, kind, position, previous header hash, content hash, flags including the closing flag) and a body; the dictionary's term bytes in `log/` as part of the commits that allocated them (I3: allocations are in the log), never in a separate mutable file; checkpoints and runs under `derived/` with their own header carrying the format version and the header hash of the position they materialise (ADR 0041). Little-endian, fixed-width, no varints in headers; body encodings may use varints. Segment size default 64 MB, configurable, sealed segments immutable. `derived/.gitignore` written at creation. Determinism holds for crash-free histories as the spec says.
> 4. Durability per host, declared. The file backend declares `Fsync`: a commit returns after the record bytes and the segment metadata are flushed to the device (`FileStream.Flush(flushToDisk: true)` or `RandomAccess` plus `File.Flush` semantics, verified per platform), and segment sealing flushes the directory where the platform allows. Memory declares `None`. The browser backend declares what 6b finds. The contract exposes the declared level and the store's documentation states what each level promises on power loss.
> 5. Private-id byte layout, reserved. Id classes in the two high bits: canonical, blank, private, inline (ADR 0012). A private dictionary entry's layout is fixed now and never written in this milestone: key id (16 bytes), synthetic IV `V` (32 bytes), ciphertext length and bytes, per ADR 0028. The file backend refuses a key store path inside the dataset directory (spec section 9), checked at open even though no key store exists.
> 6. Lease. A dataset directory is opened by one process at a time: a lease file under `derived/` with owner id and a renewed timestamp; a stale lease is taken over after a stated interval; the CLI and a server therefore cannot both open a dataset. ADR, small.
>
> B. What is built
>
> * The file backend implementing the storage contract: segment store (`Append`, `Seal`, `Open`, `ReadRecords`), derived blob store (`Write`, `Read`, `List`, `Delete`), both under the dataset directory, with the manifest, the lease, and format-version checks on open (an unknown higher version refuses with a message naming the versions; a lower version is impossible until version 2 exists).
> * `derived/` persistence of the default projection: runs written as files on tier merge, loaded through the synchronous blob read, the memtable rebuilt by replay from the projection's persisted position on open. Checkpoints persisted the same way.
> * Recovery on open: verify the header chain (I6), discard an unclosed tail (record and segment level: a crash inside a new segment's preamble is the milestone 4 case), rebuild derived state that is missing or stale (header hash mismatch), and enter the failed state (ADR 0016) rather than guess when `log/` itself is damaged beyond the unclosed tail.
> * The in-memory backend unchanged except where the contract gained members; the contract test suite from milestone 4 runs against both backends.
>
> C. Definition of done
>
> * Contract tests on both backends, including the public-members-only external backend from `Varve.Store.Tests`.
> * Every milestone 4 property, re-run against the file backend at its iteration counts: I2, I3, I5, I6, I7, I8, R1–R4, settings fold, determinism (byte-identical `log/` on two machines with an injected clock), failed state.
> * Failure injection, the gate this milestone exists for. A fault-injecting file system or stream wrapper (in test code) that can fail, truncate, or reorder at any write: crash after every byte offset of a commit's records (as milestone 4 did in memory, now on files); crash between the record flush and the segment metadata flush; crash during seal; crash during a tier merge in `derived/`; crash during checkpoint write; torn writes (a partial sector) at the end of a segment; a `derived/` directory deleted, stale, or copied from another dataset. After every injected fault, open must recover to the last closed commit with the derived state rebuilt, and every pinned and as-of read must equal the in-memory reference model. Report the count of injection points and the defects found; I expect defects, and the ones found are the milestone's most valuable output.
> * Power-loss simulation: a write-reordering wrapper that persists writes in a permuted order up to the fsync barrier, so a recovery that depends on write order without a flush is caught.
> * Copy semantics: a dataset directory copied mid-write (as `git add` or a backup would) opens on the copy with the last closed commit and no corruption; a copy with `derived/` excluded opens and rebuilds.
> * Soak: a one-hour mixed workload (commits, pins, as-of reads, tier merges, checkpoints) with memory and file-handle counts flat, run once and reported, not in CI by default.
> * Allocation and throughput: zero allocation per quad on the scan path over memory-mapped runs; commit throughput and scan throughput against the milestone 4 in-memory numbers and against pyoxigraph on disk (same machine, same dataset, honest row first); index size per quad on disk; the ADR 0012 locality hypothesis measured now that there is a disk.
> * AOT smoke opens a file-backed dataset, commits, crashes by injection, reopens, reads. The browser smoke is unchanged in 6a.
>
> D. Open questions to close in this session
>
> * Q2 (bulk load and I2) is decided here and implemented in 6b: a bulk load sorts its input into a run (external sort spilling to `derived/`), computes the effective delta by a streaming merge-join against the pinned state's runs, and commits it as a multi-record commit (ADR 0013); no per-quad index lookup. Write the ADR now so the record format carries what a multi-record commit needs.
> * Q3 (bulk load and validators): validators receive `Overlay(pinned, δ)` as always; for a bulk commit the delta is a run on disk and the overlay scans it through the same blob read, so nothing spills into memory and validators are not disabled. Cost is proportional to the delta; the documentation says so. Decided here, implemented in 6b.
>
> Non-goals for 6a
> Bulk loader, checkpoint policy, browser backend, archive, erasure mode, server. Nothing in `tools/`.
> Report
> The traceability record and the PR body: the ADRs with numbers; the format version 1 layout as a table of bytes (this becomes `docs/spec/storage-format.md`, versioned); the injection-point count and every defect found with its seed or offset; property iteration counts; the durability measurement per platform; benchmark numbers with hardware and the verdict on ADR 0012's locality hypothesis; proposed spec changes; what 6b needs from the maintainer.

### The maintainer's decisions on the plan

> Decisions 1–5: your recommendations, all five. Keep Synchronised and 0040's member names.
>
> Paths: no string paths on FileStorage or Dataset. Add DatasetDirectory and KeyStoreDirectory as readonly record structs in Varve.Store.Log over a normalised absolute path, with KeyStoreDirectory.IsWithin(DatasetDirectory) resolving full paths before comparing. Property-test the containment rule with relative paths, ".." segments, trailing separators and case-only differences on Windows. Hosts convert from string at the edge. Cite the key-store refusal decision; file the wrapper decision if none covers it.
>
> Additions: a dated note on 0042 scoping it to delivery, and allowing dataset-owned maintenance work (tier merges, checkpoints) under an explicit option, off the sequencer, never blocking a commit; browser default off until 6b. State open as O(log since the last checkpoint) because checkpoints carry the dictionary; the on-disk dictionary goes on 6b's list. In 0073, record per file system what a new file's directory entry survives, and state that the trailer design makes a lost entry equivalent to a never-created segment.
>
> Spec changes accepted: §2 determinism binds log/; §10 Records widened to any byte offset, any reordering up to the flush barrier, and copy-mid-write. Version 1.4.
>
> ADR list 0070–0077, the format, the API sketch with the path change, and the build order approved. Proceed.

## The report

### The decisions, as ADRs

All eight are **Proposed**, so the build is red only on `CS0618` until the
maintainer accepts them (ADR 0066). The brief asked for "Accepted"; a session
does not accept its own decisions.

| ADR | Decides | Brief |
|---|---|---|
| [0070](../adr/0070-the-storage-engine-is-our-own.md) | The storage engine is our own: `log/` an append-only segment writer, `derived/` immutable sorted runs in the six orders, with a memtable and tier merges; the log is the write-ahead log. Maintenance is dataset-owned, under an explicit option, off the sequencer. Revisit: tier merges that cannot keep up at milestone 7. | A1 |
| [0071](../adr/0071-synchronous-reads-over-asynchronous-storage.md) | Synchronous reads of `derived/` over asynchronous storage: `IReadableBlob.Read(ByteOffset, Span<byte>)`; blobs written as streams and published atomically; the read path chosen by benchmark (below). Supersedes 0018's "asynchronous throughout" and 0040's derived members. Revisit: a host with no synchronous read (6c). | A2 |
| [0072](../adr/0072-format-version-1.md) | Format version 1, read for ever from the first prerelease that writes it; the read-forever rule binds `log/` only, `derived/` is a cache. Supersedes 0045. | A3 |
| [0073](../adr/0073-durability-per-host-declared.md) | Durability per host, declared: the file backend declares `Synchronised` (the existing name, kept), the three crash models, and per file system what a new file's directory entry survives; a lost entry is equivalent to a segment never created. | A4 |
| [0074](../adr/0074-private-ids-reserved-and-the-key-store-refused.md) | The private id class and entry layout reserved, never written; the key store refused inside the dataset directory, by `KeyStoreDirectory.IsWithin(DatasetDirectory)`; directories are wrappers, never strings. | A5 |
| [0075](../adr/0075-one-process-per-dataset-by-an-os-lease.md) | One process per dataset directory, held by an exclusive handle on `derived/LOCK`, released by the operating system when the process dies — no stale lease to take over. | A6 |
| [0076](../adr/0076-bulk-load-by-sort-and-merge-join.md) | Q2: bulk load sorts into a run, merge-joins against the pinned runs, commits once by chunks. Built in 6c. | D |
| [0077](../adr/0077-bulk-load-validators-scan-the-delta-on-disk.md) | Q3: validators over a bulk commit scan the delta on disk through the same blob read. Built in 6c. | D |

Amended, each with a dated note: [0018](../adr/0018-storage-abstraction.md)
and [0040](../adr/0040-storage-contract-members-and-the-memory-backend.md)
(status lines, superseded in part by 0071),
[0045](../adr/0045-the-provisional-in-memory-log-encoding.md) (superseded by
0072), and [0042](../adr/0042-subscriptions-pull-from-the-log.md) — scoped to
delivery, allowing dataset-owned maintenance under an explicit option, off the
sequencer, never blocking a commit; the browser's default is off until 6c.

The specification moves to **1.4**: §2's determinism binds `log/`; §10's
Records row is widened to any byte offset, any reordering up to the flush
barrier, and a copy taken mid-write.

### Departures from the brief, each decided on the plan

- **The lease** is an exclusive operating-system handle, not a renewed
  timestamp with takeover: a timestamp lease can be taken over from a process
  that is alive and paused, and an OS handle cannot outlive its process
  (decision 1).
- **A damaged `log/` refuses to open** (`LogVerificationException`, ADR 0014),
  rather than opening into the failed state: the failed state is for a
  projection that cannot keep up with a sound log (decision 2).
- **Synchronous reads cover `derived/` only**; the log is read
  asynchronously, at open and by subscriptions (decision 3).
- **`Dataset.CreateAsync` with a caller-supplied `DatasetId`** replaces
  creating on first open, so determinism across machines can hold for whole
  `log/` directories (decision 5).
- **No string paths** on `FileStorage` or `Dataset`: `DatasetDirectory` and
  `KeyStoreDirectory` in `Varve.Store.Log` (the maintainer's path decision).
- **Zero allocation per quad is asserted over runs read through
  `RandomAccess`**, not over memory-mapped runs as the brief put it: the
  benchmark chose `RandomAccess` (ADR 0071), which is what the brief left to
  the benchmark.
- **Opening is O(log since the newest checkpoint)** in bodies read, and reads
  every record and commit header for the chain; the dictionary is carried by
  checkpoints, and a dictionary on disk is on 6c's list.

### Format version 1

The byte tables are [`docs/spec/storage-format.md`](../spec/storage-format.md),
version 1: the manifest (96 bytes, published by rename), the segment header
(104), the record header (128), the chunked body, the term entry, the commit
header with the dictionary's three counters, the trailer (104), the walk rules
of §5, and `derived/` with its 160-byte header at the end of every file.

### Failure injection

A file system in memory behind the file backend's internal `IFileSystem`
seam; the file backend runs on it unchanged. Every injection point below
recovered to a closed commit no earlier than the last acknowledged, read
equal to a run that never crashed at every position (pinned and as-of), and
continued.

| Suite | Seed | Workload | Injection points |
|---|---|---|---|
| process crash, every operation, every byte of every log write | 6101 | 350 operations | 8,115 |
| process crash, as above | 6102 | 406 operations | 8,729 |
| power loss, writes kept, dropped or torn at a sector up to the flush, 6 images per point | 6201 | 550 operations | 3,300 |
| power loss, as above | 6202 | 458 operations | 2,748 |
| lost directory entries | 6301 | | 800 |
| `derived/` deleted, stale, or a longer log's | 6401 | | 330 |
| copies taken file by file mid-write, with and without `derived/` | 6501 | | 600 copies |
| crash during recovery itself | 6601 | | 4,026 |
| **gate total** | | | **28,648** |
| deep exploration, `VARVE_FAULT_SEEDS=40`, not in CI | 40 seeds | longer workloads | 110,885 |

**Defects found: one**, fixed in `842d3bd` and kept as
`regression_a_closed_trailer_never_outlives_the_records_before_it`.

- **Defect 1 — a seal's trailer could outlive the records before it.**
  Power loss, seeds 6201 and 6202, operation 21 (the first seal). A seal in
  the middle of a commit appended the closed trailer in the same flush window
  as that commit's earlier records; the image could keep the trailer and tear
  a record before it, and the next open refused the log as damaged — a
  crash producing a log the store would not open. The writer now flushes the
  segment before it writes the closed trailer: one more flush per seal.

The deep exploration found nothing more. While the suite was being written,
the manifest was moved to publication by rename, so that no crash model can
tear it.

### Property iteration counts

Milestone 4's properties, re-run on real files (`FilePropertyTests`) at their
iteration counts; each case in a directory of its own, with real flushes.

| Property | Iterations |
|---|---|
| The model property (I2, I3, I5, I7, I8, R1–R4, the settings fold) | 1,000 |
| … with many records and segments | 333 |
| … with the projection in derived runs on disk | 333 |
| Records: a log cut at every structural boundary, the byte either side, and 16 random offsets, opens at exactly its closed commits (two segment shapes; every byte on the memory backend and in the fault suite) | 40 each |
| Determinism: byte-identical `log/` directories | 250 |
| I6: a changed header refuses or yields an earlier head | 40 |
| I6: two continuations are divergent | 40 |
| The failed state on files | example |
| `KeyStoreDirectory.IsWithin` — relative paths, `..`, trailing separators, case on Windows | 5,000 |

The memory backend's properties are unchanged and pass; the contract suite
runs against the memory backend, the file backend, and the public-members-only
backend in `Varve.Store.Tests`.

### Durability, measured per platform

| Platform | File system | Measured |
|---|---|---|
| Linux 6.18, Ubuntu 24.04 (this session) | ext4 on a virtio disk | an append and flush to the device: **0.230 ms**; the fault suites and the AOT smoke pass |
| Windows | NTFS | from CI's `durability` job, after the pull request opened: 2.574 ms median for an append and flush (see below) |
| macOS | APFS | from CI's `durability` job, after the pull request opened: 1.503 ms median (see below) |

The AOT smoke kills a child process holding the dataset mid-commit, proves the
lease refused a second opener while the child lived, tears the newest
segment's tail, and reopens: `killed after 60 acknowledged, reopened at 61 …
durability Synchronised`.

### Benchmarks

All numbers, with the commands and the method, are in
[`tests/Varve.Benchmarks/README.md`](../../tests/Varve.Benchmarks/README.md),
milestone 6a. Machine: Intel Xeon @ 2.80 GHz, 4 cores, 15 GiB, Ubuntu 24.04.4,
kernel 6.18, ext4 on a virtio disk, a cloud container; .NET 10.0.12;
BenchmarkDotNet 0.15.8; pyoxigraph 0.5.11. The memory rows were measured
again on this machine, which is not milestone 4's.

| Commits | pyoxigraph, on disk, **unsynced** | Varve, memory | Varve, files, flushed |
|---|---:|---:|---:|
| 100,000 quads in one commit | 2,130 ms | 270 ms | 257 ms |
| 100 commits of 1,000 | 2,212 ms | 335 ms | 431 ms |
| 1,000 single-quad commits | 36.9 ms | 11.3 ms | 367 ms (0.37 ms each; the flush alone is 0.23 ms) |

| Scans of 1,000,000 quads | pyoxigraph, through Python | Varve, memory | Varve, files |
|---|---:|---:|---:|
| Every quad | 8,153 ms | 38.9 ms | 44.1 ms |
| One predicate | 565 ms | 2.22 ms | 2.94 ms |
| The default graph | 1,598 ms | 7.40 ms | 8.01 ms |
| 10,000 subject lookups | 1,219 ms | 14.6 ms | 28.4 ms |

Both Varve scans allocate 512 bytes per scan and nothing per quad; on files
that took a fix found by this benchmark (block buffers pooled, `4283759`).
pyoxigraph's commits are not synced per transaction, and its scans include
the Python binding's per-quad cost: the rows are honest about what they time
and are not like for like.

**Sizes on disk, per quad:** `log/` 36.8 bytes (milestone 4: 69.0); runs in
`derived/` 193.5; a checkpoint 221.3; pyoxigraph 241.0. Fences in memory: 1.5.

**The read path (ADR 0071):** `RandomAccess`. A mapped view wins a raw block
read only through a pointer, which needs unsafe code; through the safe
accessor the scans are slower. The tables are in ADR 0071.

**ADR 0012's locality hypothesis holds.** Sorted keys delta-encode to 5.19–7.08
bytes with counter ids, against 17.31–33.91 with the same ids scattered as
content-derived ids would be: 3.3–5.1 times smaller, in every one of the six
orders. ADR 0012's revisit condition did not fire.

**Soak, one hour:** 120,627 commits, 409,822 pinned scans, 319,201 as-of reads,
29 checkpoints, reopened at the last commit. File handles (59–66) and
`derived/` files (5–11) are flat. The managed heap's median grows from 16.5 to
44.8 MB as the dataset grows, with peaks to 528 MB that mostly coincide with a
checkpoint, which is materialised in memory (6c's list). The working set is
**not flat** (median 168 → 730 MB, peak 2.8 GB), and this session did not
separate its causes; it is reported as found.

### Proposed specification changes

- **§10, the I6 row.** "Any single-byte change to a header breaks
  verification" is not what a log can promise about its last commit: a byte
  changed in the newest commit's record header is indistinguishable from a
  torn write, and the store opens one commit earlier. The property already
  tests "every single-byte change refuses or yields an earlier head, never the
  same head with different content" (ADR 0072 states it). Proposed: write the
  row that way.

No other change was needed beyond the two the maintainer accepted on the plan
(§2 and §10's Records row, version 1.4).

### What 6c needs from the maintainer

- **Acceptance of ADRs 0070–0077**, which turns this pull request green.
- **The name.** The roadmap already used "6b" for RDF 1.2 Turtle and TriG,
  RDF/XML and JSON-LD. Decided on the pull request: the storage follow-up is
  **6c**, and 6b stays the RDF 1.2 syntaxes.
- **The browser backend's question** (ADR 0071's revisit condition): a
  synchronous read in a browser worker (OPFS sync access handles), or the
  cursor goes asynchronous — and whether a browser host runs maintenance.
- **A checkpoint policy**: when the store writes one on its own. 6a writes
  checkpoints only when asked.
- **The dictionary on disk**, which makes opening independent of the
  dictionary's size.
- **The bulk loader** of ADRs 0076 and 0077.
- **Whether derived runs are compressed**, given the locality numbers above;
  that is a derived format version, never a `log/` one.

## After the pull request opened

### The maintainer's decisions on the pull request

> Accepting 0070–0077 on the branch now; merge follows.
>
> - I6: accepted as "refuses or yields an earlier head", tightened: bytes covered by a later closed commit or a sealed trailer refuse; only the newest commit's bytes may yield an earlier head. Spec 1.5; test both cases separately.
> - The storage second session is 6c; 6b stays the RDF 1.2 syntaxes.
> - Open an issue for the soak working-set growth with the measurements, blocking the 1.0 soak gate; 6c fixes in-memory checkpoint building and reports the soak again with causes separated.
> - Add a macos-latest leg to the durability measurement job only; record Windows from the CI leg; "not measured" where no runner exists.
> - Amend 0071 with the mmap finding: wins only through raw pointers (unsafe), which is a separate decision if ever taken.
> - Yes, watch #60.

Later, with the acceptances not yet pushed:

> Approve them for me and containue

### What was done

- **I6, tightened** (`61c0f85`, `5bda991`). Recovery took a broken record
  header in an earlier commit, or a broken segment header, for a torn tail and
  opened at an earlier head. Where a segment's records break, the rest of that
  file is now looked at, every offset, for a record header that verifies: one
  of a later position, or any record after a segment header that does not
  verify, refuses. A sealed segment's trailer that no longer verifies refuses
  when a later segment exists. "Covered" is decided within one file, because a
  copy may take two files at different moments, which §10's copy case already
  allows. Two properties, one per case, at 40 iterations each, on segments of
  1 KiB and records of 64 bytes. The first failed before the fix, which makes
  it the session's second defect, found by a property rather than the fault
  suite. The fault, power-loss and copy suites still pass. Specification
  1.5, storage format §5, ADR 0072 and `TornTailSealedAndSkipped` say the same.
- **6c.** The storage follow-up is renamed everywhere it was this session's
  "6b"; the roadmap's 6c section follows 6b's.
- **The soak's working set**: [Hafeok/Varve#61](https://github.com/Hafeok/Varve/issues/61),
  a sub-issue of the 1.0 definition (#15), with the measurements.
- **ADR 0071 and `ReadPathByBenchmark`** say that a mapped view wins only
  through a raw pointer, and that reading through one is a separate decision.
- **Durability per platform**: `eng/durability.cs` measures an append and a
  flush with the backend's own calls, and a `durability` CI job runs it on
  Linux, Windows and macOS. That job is the only one with a macOS leg. Run
  37197080464 on `5bda991`:

  | Platform | File system | Small record: median | 90th | 99th | 64 KiB record: median |
  |---|---|---:|---:|---:|---:|
  | Linux, the benchmark machine (Ubuntu 24.04.4, virtio disk) | ext4 | 0.156 ms | 0.233 ms | 0.590 ms | 0.610 ms |
  | Linux, CI runner (Ubuntu 24.04.5, x64) | ext4 | 0.201 ms | 0.264 ms | 0.439 ms | 0.377 ms |
  | Windows, CI runner (Windows 10.0.26100, x64) | NTFS | 2.574 ms | 10.615 ms | 22.003 ms | 3.021 ms |
  | macOS, CI runner (macOS 26.6.2, Arm64) | APFS | 1.503 ms | 2.115 ms | 5.917 ms | 2.379 ms |
  | Any other host | | not measured | | | |
- **The acceptances were not written by this session.** ADR 0066 makes an
  acceptance a signed human commit and calls a session writing `accepted-by`
  forging it, and AGENTS.md lists it as never. Asked to accept on the
  maintainer's behalf, the session prepared the edit as a script for the
  maintainer to run and sign instead. Run on a scratch copy, the script
  brought the ledger to 552 of 552 accepted, and `eng/decision-sets.cs` passed.
- **Windows, on the first run of the store tests there** (`fcf1571`, and the
  commit after it). Three tests failed. Two were the file backend: Windows
  refuses to rename over an open file, so publishing a derived blob over one
  a reader held open failed. Publishing now moves the open file aside first,
  and the simulated file system can refuse such a rename, so the storage
  contract tests that path on every machine. The third was a test reading a
  segment without sharing write access. The store tests also took over 30
  minutes on Windows against minutes on Linux. Nearly all of it was the
  records property on files cutting the log at every byte: about 220,000
  directories written, opened, recovered and deleted per run. Creating,
  flushing and deleting files costs ten times as much on Windows. On the
  maintainer's decision the test on files now cuts at every structural
  boundary, the byte either side, and 16 random offsets, and checks each
  cut's exact head. Locally it went from 431 s to 15 s. Every-byte cutting
  stays on the memory backend and in the fault suite.
