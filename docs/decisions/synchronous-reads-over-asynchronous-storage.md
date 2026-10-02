---
set: synchronous-reads-over-asynchronous-storage
namespace: varve
adr: 0071
decisions:
  - key: SegmentStoreAndDerivedStore
    statement: "Storage is an append-only segment store for log/ and a derived blob store for derived/; derived blobs are read synchronously and everything else is asynchronous"
  - key: StorageContractMembers
    statement: "IStorage exposes an ISegmentStore that lists, creates, appends to, flushes, seals and reads segments and reads and writes once its manifest, and an IDerivedStore that creates a blob writer, opens a readable blob, deletes and lists blobs"
  - key: ReadBytesAreImmutable
    statement: "Bytes returned by a segment read are immutable and may be held, and a backend that cannot promise it copies; derived bytes are copied into the reader's buffer"
  - key: DerivedReadsAreSynchronous
    statement: "A derived blob is read synchronously through IReadableBlob's Length and Read, and runs and checkpoints are scanned through it by one scan code on every backend"
  - key: BlobsArePublishedAtomically
    statement: "A derived blob is written through an IBlobWriter and becomes visible only when published, durable as the backend declares and atomically replacing any blob of that name; an unpublished writer leaves nothing"
  - key: ReadPathByBenchmark
    statement: "The file backend reads derived blobs with RandomAccess or a memory-mapped view, whichever milestone 6a's benchmark favours, recorded in ADR 0071"
---

The rulings of [ADR 0071](../adr/0071-synchronous-reads-over-asynchronous-storage.md), filed unaccepted
by session 6a of #10 (ADR 0066). `SegmentStoreAndDerivedStore` moved here from ADR 0018's set, and
`StorageContractMembers` and `ReadBytesAreImmutable` from ADR 0040's, each restated where this ADR
supersedes it.
