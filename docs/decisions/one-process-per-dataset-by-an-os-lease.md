---
set: one-process-per-dataset-by-an-os-lease
namespace: varve
adr: 0075
decisions:
  - key: OneProcessPerDirectory
    statement: "A dataset directory is opened by one FileStorage at a time, across processes and within one"
  - key: LeaseIsAnExclusiveHandle
    statement: "The lease is derived/LOCK held open with FileShare.None until the storage is disposed, released by the operating system when the process ends, with no renewal and no takeover interval"
  - key: LeaseContentIsDiagnostic
    statement: "The lease's process id, machine and time taken are written beside it in derived/LOCK.owner, for the refusal's message only"
---

The rulings of [ADR 0075](../adr/0075-one-process-per-dataset-by-an-os-lease.md), filed unaccepted by
session 6a of #10 (ADR 0066).
