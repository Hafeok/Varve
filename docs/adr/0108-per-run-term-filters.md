# 0108 — Per-run term filters: derived format 3

## Status

**Accepted — filed unaccepted by milestone 7b of #11, 2026-10-08** (ADR 0066).
Decided by the maintainer in the 7b brief, item 6. Discharges ADR
[0079](0079-the-term-dictionary-on-disk.md)'s revisit condition by the
maintainer's decision rather than by a workload: "a per-run filter if cold
term lookups ever dominate commits" was 6c's open item, and 7b builds it.
**Changes the derived format** from version 2 (ADR
[0080](0080-derived-blocks-compressed.md)) to **3**, which ADR
[0072](0072-format-version-1.md) allows: `derived/` is not read forever.
Acceptance is the maintainer's act on the pull request.

## Context

A term is looked up by content in each run's hash index until it is found
(ADR 0079). A commit of new terms therefore reads one index window per run
for every term, and a dataset with many runs — before maintenance merges
them — pays that per term. ADR 0079 declined a filter in memory for its
memory, "about a byte per term", and named it the revisit condition.

## Decision

1. **Each run carries a filter over the content hashes of its terms**: a
   blocked Bloom filter with 512-bit blocks, eight bits a term, three probes
   in one block, so a lookup touches one cache line. The false-positive rate
   is about two percent; a false positive costs what every lookup cost
   before, one index read.
2. **The filter is a section of the run file**, after the term section,
   named in the run's directory with its offset and length, and **the
   derived format version becomes 3** (`storage-format.md` §7). A version-2
   run has no filter section and is read as before: a reader that finds no
   filter looks up every run. Maintenance rewrites runs it merges in format
   3, so a dataset migrates as it runs; nothing rewrites a run for the
   filter alone.
3. **A filter is loaded with the run's directory** (ADR 0080's chunked
   directory), held for as long as the reader holds the run, and counted in
   the soak's "dataset's own" figure (ADR 0082) as eight bits a term beside
   the directory's 40 bytes per 128 keys.
4. **A lookup by term consults the filter first** and skips the run on a
   miss; the cache in front of the runs (ADR 0079) is unchanged.
5. **Measured**: the commit benchmark's cold-term rows before and after, on
   the same machine, and the soak re-run on the final code, in the 7b
   record and the benchmark README.

## Alternatives considered

- **A filter over all runs**, rebuilt at every merge. One filter to read,
  and a rebuild proportional to the dataset at every flush.
- **Fences of the hash index in memory** (ADR 0079's second alternative).
  Finds the window without reading it; still reads a window per run for an
  absent term.
- **A cuckoo or xor filter.** Fewer bits per term at the same rate; the
  blocked Bloom filter is forty lines, builds in one pass over the run's
  hashes, and needs no construction retries.
- **Leave it to the cache.** The cache absorbs repeats; a commit's new terms
  are by definition not repeats.

## Consequences

- A commit's new term costs one filter probe per run and an index read
  only where the filter says maybe.
- `derived/` grows by a byte a term per run; the soak reports it.
- Opening a dataset reads one more section per run's directory.
- A format-2 dataset is read without change and migrates by maintenance.

## Checks

- **Checked against the accepted ADRs** (0001–0101) and specification 1.6.
  Touches **0072** (derived data may change format), **0079** (the revisit
  condition discharged; the id scheme unchanged), **0080** (the directory
  gains a section), **0082** (the soak counts it). No conflict.
- **Layer ownership.** `Varve.Store`, layer 4.
- **Analyzer rule.** None. The hot path is `[HotPath]` already.
- **Open questions owned.** None.
