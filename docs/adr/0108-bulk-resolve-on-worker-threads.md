# 0108 — The bulk load resolves and spills on worker threads

## Status

**Accepted — filed unaccepted by milestone 7b of #11, 2026-10-08** (ADR 0066).
Decided by the maintainer in the 7b brief, item 6. Builds the last
alternative of ADR [0081](0081-the-bulk-loader.md) ("a background thread
that spills while the parser fills a second buffer"), which that ADR left
"for when the throughput row says it is needed"; the 6c record's throughput
row did. Acceptance is the maintainer's act on the pull request.

## Context

The 6c bulk gate measured the parser's thread resolving and spilling the
input at 207K–351K operations a second at a hundred million quads, against a
parse rate of 1.07M quads a second: the parser waits for the disk and the
hash lookups between every buffer. The sort, the merge-join and the commit
are already parallel per pass (ADR 0081). The input stage is the serial
part.

A host that cannot block a thread — the browser — cannot bulk-load (ADR 0081),
so threads are available wherever a bulk load runs.

## Decision

1. **The parser fills buffers; workers resolve and spill them.** `BulkLoad`
   keeps a small ring of operation buffers. `Assert` and `Retract` write into
   the current one; when it fills, the buffer is handed to a worker and the
   parser continues into the next. A worker resolves the buffer's terms
   (hash, dictionary lookup, the new-term table) and spills it as a sorted
   run. The parser blocks only when every buffer is busy.
2. **`BulkLoadOptions.Workers`** is the number of workers, default the
   processor count less one, at least one. One worker is the 6c pipeline
   with one buffer of overlap; the parser's own thread is never a worker.
3. **The spill order is preserved.** Buffers carry a sequence number; runs
   are named by it, and the merge of the runs is order-independent (a sort),
   so the load's result is the same at any worker count, which the gate's
   crash tests and the determinism property already assert.
4. **The memory bound holds**: the buffers are carved from
   `BulkLoadOptions.MemoryBytes` as the sort buffer was — the ring's buffers
   together take what the one buffer took — and the new-term table is shared
   under a lock, since it is the one structure two buffers write.
5. **Measured**: the bulk gate's throughput row before and after, on the same
   machine, in the 7b record and the benchmark README; the 100-million row
   is the gate and stays so.

## Alternatives considered

- **Resolve inside the parser's thread, spill on a worker.** Only the disk
  write overlaps; the lookups were the larger half.
- **Parse on several threads.** A parser is a stream; splitting one input is
  a different loader, and several inputs can already be several loads of
  one dataset in sequence.
- **Leave it.** The gate passes; the row is the reason it was on the 6c list.

## Consequences

- A load on a four-core machine overlaps parsing with resolution; the
  record gives the number.
- A load on one core is the 6c loader with one buffer more in flight.
- `BulkLoadOptions` gains one member on the baseline.

## Checks

- **Checked against the accepted ADRs** (0001–0101) and specification 1.6.
  Touches **0076**, **0077** and **0081** (the loader, its bound and its
  validators, all unchanged in what they decide), **0073** (durability
  unchanged: the commit is the same commit). No conflict.
- **Layer ownership.** `Varve.Store`, layer 4.
- **Analyzer rule.** None.
- **Open questions owned.** None.
