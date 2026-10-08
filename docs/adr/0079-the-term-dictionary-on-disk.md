# 0079 — The term dictionary on disk, carried by the runs

## Status

**Accepted — filed unaccepted by session 6c of #10, 2026-10-05** (ADR 0066).
Acceptance is the maintainer's act on the pull request.

Takes up what ADR [0070](0070-the-storage-engine-is-our-own.md)'s
consequences and the 6a record deferred ("the term dictionary stays in memory
in 6a … a dictionary on disk is on 6c's list"). It changes the derived format
(version 2, storage format §7), which ADR
[0072](0072-format-version-1.md) allows: `derived/` is not read forever. It
supersedes nothing; ADR [0012](0012-term-dictionary-and-id-scheme.md)'s ids,
classes and counters are unchanged.

**Revisit condition:** a workload where lookups by term on a cold cache
dominate commit latency — the per-run hash index searched run by run would
then need a filter in memory, which this decision declined. **Discharged
2026-10-08** by ADR [0108](0108-per-run-term-filters.md) (ADR 0068's dated
note; milestone 7b of #11): each run carries a blocked Bloom filter over its
term hashes, eight bits a term, in derived format 3. The ids, classes and
counters here are unchanged.

## Context

In 6a the dictionary was a hash table of every term in memory, rebuilt on
open from the newest checkpoint's dictionary section and the log after it.
Open was therefore O(terms) in memory and O(terms) in time, whatever the log
distance, and a dataset could not have more terms than memory. The prompt for
6c asks for the dictionary as derived state in the same run and checkpoint
machinery, so that open is O(tail since the last checkpoint) in time and
bounded in memory.

## Decision

### A run carries the entries of the ids its commits allocated

Every run — of the memtable, on disk, or a checkpoint — has a **term
section**: the dictionary entries of the canonical ids allocated by the
commits it covers, the counters `(From, To]`. A checkpoint's section is every
entry from 1 to its position's counter. Entries are the log's own encoding
(storage format §4.4); with them, a table of their offsets, one `u64` per entry
and one for the end; and a **hash index**: `(hash, id)` pairs sorted by a
64-bit hash of the term's key — its entry without the id, its language tag
lowercased, because tags compare ignoring case. Blank nodes have no entry;
their counter is in the commit headers.

Merging runs concatenates their sections and merges their indexes, streamed
like the keys (ADR 0078). The runs a version holds cover every counter from 1
to its position, so a view finds every term it can see in the runs it already
holds open; an as-of view holds its checkpoint and a run of the tail's
allocations.

### Lookups read through the synchronous blob read

- **By id**: the run whose section holds the counter; two offsets, then the
  entry, through `IReadableBlob.Read` (ADR 0071).
- **By term**: the key is built in a stack buffer, hashed, and each section is
  searched newest first, by interpolation on the hash — a hash spreads evenly,
  so a lookup reads one 512-byte window of the index in the common case — then
  each entry with the hash is read and its key compared.
- **Two caches**, 65,536 slots each, term to id and id to term. A canonical id
  is never reused and never changes its term, so a slot is never stale,
  whichever version filled it; a view checks the id against its own counter.

A lookup by term or by id into a caller's buffer **allocates nothing**; a
lookup that misses and finds the term allocates its cache slot's entry, once.
The allocation tests assert both on the memory and the file backends.

### Opening loads no dictionary

Opening loads the projection's persisted state, or the newest checkpoint, and
replays the log after it. Its memory is the memtable it rebuilds and the
fences of the runs it opens; its time is that tail plus one read of every
record and commit header, which the chain's verification (I6) needs. Nothing
is per term.

## Alternatives considered

- **A dictionary of its own beside the runs**, with its own levels and merges.
  A second engine of the same shape, with its own persisted state to keep
  consistent with the runs'. Carrying it in the runs makes "the projection's
  state at a position" include the dictionary at that position for free.
- **A B-tree or a hash table on disk, updated in place.** Fights immutable
  runs (ADR 0041), and a torn page is a corrupt dictionary rather than a
  missing file.
- **Fences of the hash index in memory**, as keys have. A few bytes per
  hundred terms, and O(terms); interpolation finds the window without them.
- **A Bloom filter per run.** Saves reads for absent terms at about a byte per
  term in memory. The revisit condition.

## Consequences

- A term is looked up in each run until found: a commit's new term is
  searched in every run. The cache absorbs the repeats; the revisit condition
  watches the rest.
- Externalising a term decodes it from its entry: an `RdfTerm` per call, which
  is the contract's result, and once cached, per id.
- Defect 1 of 6c — keys compared tags byte by byte — was found by the model
  property on the first run (seed `0TyDyZl1BdDa`) and is a named regression.

## Checks

- **Checked against the accepted ADRs** (0001–0077) and specification 1.5.
  Touches **0012** (ids unchanged; where entries live), **0041** and **0070**
  (runs carry a section; merges stream it), **0071** (lookups through the
  synchronous read), **0072** (the derived format's version, not `log/`'s).
  No conflict.
- **Layer ownership.** `Varve.Store`, layer 4. Nothing public.
- **Analyzer rule.** None new. The cache's one allocation cites
  `DictionaryCachesAreBounded` on the hot path (`VARVE0003`).
- **Open questions owned.** None.
