# 0081 — The bulk loader: references by content hash, memory bounded, one commit

## Status

**Proposed — filed unaccepted by session 6c of #10, 2026-10-05** (ADR 0066).
Acceptance is the maintainer's act on the pull request.

Builds what ADRs [0076](0076-bulk-load-by-sort-and-merge-join.md) and
[0077](0077-bulk-load-validators-scan-the-delta-on-disk.md) decided, and states
what they left open: how a load resolves terms without holding them all, the
exact validator member (0077's "6c states the exact member when it builds
it"), what the load holds in memory, and who may run one. It supersedes
nothing.

**Revisit condition:** a load refused for a collision of its 120-bit term
references, or an input whose new terms do not fit a disk the size of the
dataset.

## Context

ADR 0076 says: resolve terms as quads arrive, allocating new ones
provisionally; sort into a run by an external sort; merge-join with the
pinned state; one multi-record commit. A provisional id is a counter, given
the first time a term is met — and knowing that a term was met before means
remembering every new term. A load of a hundred million quads meets tens of
millions of them; held in memory, they are the bound the load was meant not
to have. And ids given in the order met are not the order the delta's keys
sort in, so a delta sorted by provisional ids must be sorted again.

## Decision

### A new term is referred to by a hash of its key

While the input arrives, every term becomes a 128-bit **reference**: an
existing term its id (a lookup through ADR 0079's dictionary, behind a cache),
a new one the first 120 bits of the SHA-256 of its key, in a **segment** that
sorts where its final id will — existing canonical ids, new canonical terms by
the depth of their triple-term nesting (a triple term after its parts), existing
blank nodes, new blank nodes (a label is scoped to the load, ADR 0044), inline
values. Operations are 72-byte records of four references and a sequence
number, sorted outside memory in runs spilled to `derived/bulk/`; new terms'
keys are spilled the same way, sorted by reference.

New terms take their **final ids in reference order**, so a list sorted by
references is sorted by final ids: the delta is sorted once. The order is
deterministic — the same input gives the same log bytes — and is not the
order an ordinary commit allocates in (first met); both are allocations "in
ascending id order per class", which is all storage format §4.4 asks.

**Two different keys with one reference refuse the load**, detected when the
spilled tables are merged; nothing is ever merged on a hash. A real collision
of 120 bits of SHA-256 is beyond any load; defect 2 of this milestone was a
collision of the first, weaker hash, found by the gate and fixed by taking
SHA-256.

### The commit, in passes

1. The sorted operations are merged with the pinned state's quads in SPOG
   order, one sequential pass: the last operation on a quad decides, and it is
   effective when it asserts an absent quad or retracts a present one. A quad
   with a new term is never present. The new terms the effective assertions
   reach are collected — closed over the parts of triple terms reached (I3) —
   sorted, and ranked: a term's rank is its final id.
2. The merge runs again and writes the effective delta with final ids,
   already in SPOG order.
3. The delta becomes a **disk run** of the projection: the other five orders
   sorted from it one at a time, and a term section of the allocations
   (ADR 0079).
4. The dataset's validators read it there through a new member,
   `ICommitValidator.Validate(IQuadSource proposed, BulkDelta delta)`, whose
   `delta` is two quad sources over the run. Its default reads the delta into
   memory and calls the ordinary `Validate`, which costs memory in the delta's
   size — stated on the member; a validator that must not implements it.
   A validator's attachment must be a term the proposed state holds.
5. The body — allocations, then asserted, then retracted quads, in chunks — is
   streamed twice: once for its content hash, which the header needs, once
   into the log, as records of at most `MaxRecordBytes`, one flush after the
   last (`LogWriter.AppendStreamAsync`). A crash before the closing record
   leaves the dataset where it was; the spills are deleted when the load ends,
   or by the next open.

### Memory is bounded

`BulkLoadOptions.MemoryBytes`, 256 MiB by default, is carved up so that no
two parts of it are alive at once beyond it: while the input arrives, three
eighths for the operations' sort buffer, a quarter for the new terms' table
and a quarter for the term cache; in the commit, an eighth for the reached
terms' sort and for the hashes' sort, half for the reached terms' ranks —
held in memory when they fit, a search of their spill when not, and given
back once the allocations are written — and a quarter for each order's sort. Beyond it: one 64 KiB buffer per sorted run a
merge reads (at most 64 at a time; more are merged in passes first); the
directory of the run being written, which the run's readers hold anyway — 40
bytes per 128 keys per order, about two bytes per quad of the delta; and the
dataset's own state. So the bound is **`MemoryBytes` + 2 bytes per quad of
the delta + the dataset**, and the gate runs under `DOTNET_GCHeapHardLimit` —
the runtime's assertion of it — and reports the peak. The first 100-million
quad run, with the ranks and the order sort each allowed half, ran out of
memory under the cap; that is how the carve-up above was arrived at.

### Who may load

The load holds the sequencer from `BeginBulkLoadAsync` until it commits or is
disposed, with the memtable flushed first, so the delta becomes a disk run
after the projection's others. The call that fills the sort buffer writes it
before returning: a parser's handler is synchronous, and the parser's thread
does the disk work. A host that cannot block a thread — a browser — cannot
bulk-load, and `BeginBulkLoadAsync` says so.

### Language tags

A bulk load writes a language-tagged literal's tag lowercased: tags compare
ignoring case (RDF 1.1 Concepts §3.3), and a hash needs one form. An existing
term with another case is found and kept as it is.

## Alternatives considered

- **Provisional counters in memory** (0076's words). The bound the load
  exists to avoid.
- **Per-batch provisional ids, remapped after.** Bounded, and the delta must
  then be rewritten and sorted again once ids are final.
- **64-bit hashes.** A collision becomes likely around four billion terms, and
  is a refused load each time.
- **A background thread that spills while the parser fills a second buffer.**
  Faster on a machine with idle cores; the same bound with two buffers. Left
  for when the throughput row says it is needed.

## Consequences

- A load reads its operations twice and writes the delta's six orders once;
  its temporary disk is the operations' spill (72 bytes per operation) and one
  order's sort at a time.
- The log of a bulk commit is as large as an ordinary commit of the same
  delta, and its allocations are in another order.

## Checks

- **Checked against the accepted ADRs** (0001–0077) and specification 1.5.
  Implements **0076** and **0077**; touches **0013** (a multi-record commit),
  **0044** (labels scoped to the load), **0072** (chunks as format version 1
  has them), **0079** (the term section), **0070** (the delta as a disk run).
  No conflict.
- **Layer ownership.** `Varve.Store`, layer 4. Public: `BulkLoad`,
  `BulkLoadOptions`, `BulkLoadException`, `BulkDelta`,
  `Dataset.BeginBulkLoadAsync`, and the new `ICommitValidator` member.
- **Analyzer rule.** None new. The per-operation path is `[HotPath]`; SHA-256
  on a cache miss cites `NewTermsByContentHash`.
- **Open questions owned.** None; Q2 and Q3 were closed by 0076 and 0077.
