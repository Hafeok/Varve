---
set: bulk-resolve-on-worker-threads
namespace: varve
adr: 0108
decisions:
  - key: WorkersResolveAndSpillBuffers
    statement: "The parser fills a ring of operation buffers and workers resolve and spill each full buffer as a sorted run; the parser blocks only when every buffer is busy"
  - key: BulkWorkersOption
    statement: "BulkLoadOptions.Workers is the worker count, default the processor count less one and at least one; the parser's thread is never a worker"
  - key: BulkSpillOrderPreserved
    statement: "Buffers carry sequence numbers, runs are named by them, and the load's result is the same at any worker count; the buffers share MemoryBytes and the new-term table is shared under a lock"
---

The rulings of [ADR 0108](../adr/0108-bulk-resolve-on-worker-threads.md), filed unaccepted by milestone 7b of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
