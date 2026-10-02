# 0071 — Synchronous reads of derived data over asynchronous storage

## Status

**Proposed — filed unaccepted by session 6a of #10, 2026-10-02** (ADR 0066).

It **supersedes in part**:

- **ADR [0018](0018-storage-abstraction.md)**, the words "asynchronous
  throughout" of its *Decision*: reads of derived data are synchronous. The
  split into a segment store and a derived blob store, the durability levels
  and the five impossibilities stand.
- **ADR [0040](0040-storage-contract-members-and-the-memory-backend.md)**, the
  derived store's members (`PutAsync` and `GetRangeAsync` are replaced) and the
  promise that bytes read from the derived store may be held. The segment
  store's members gain two (the manifest); the rest of 0040 stands.

**Revisit condition:** a host where no synchronous read exists — 6b decides
this for the browser — supersedes this ADR.

## Context

ADR 0018 made storage asynchronous throughout, because the browser's storage is
asynchronous. ADR 0022's quad source walks a cursor with a synchronous
`MoveNext`, and the evaluator is built on it; ADR 0052 fixes how long a pinned
read lives, which is "one query execution", not "until the next await". In
memory the two never met, because a checkpoint was a slice already in hand
(ADR 0041). The roadmap carried the collision into milestone 6 as a named risk
with three answers: page a run in before the scan starts, give storage a
synchronous read path, or make the cursor asynchronous.

- **Paging in before the scan** is a restore step, which is what "directly
  queryable" (ADR 0015) exists to forbid, and it bounds a run by memory.
- **An asynchronous cursor** is a superseding change to ADR 0022 that reaches
  every operator in the evaluator, and pays a state machine per row on every
  host, including the two whose reads complete synchronously.
- **A synchronous read path** costs one member, and it is the shape every
  desktop file API already has.

A second problem sits beside it. `IDerivedStore.PutAsync` takes the whole blob
as one buffer. A disk run of ten million quads is 1.9 GB in six orders; a
contract that needs it in one `ReadOnlyMemory<byte>` cannot write it.

## Decision

### Derived data is read synchronously

```csharp
public interface IReadableBlob : IDisposable
{
    ByteCount Length { get; }
    int Read(ByteOffset offset, Span<byte> destination);  // fewer bytes only at the end
}
```

`IDerivedStore.OpenAsync(name)` opens a blob — asynchronously, since opening is
where a backend may fetch, map or wait — and every read after that is
synchronous. **Runs and checkpoints are scanned through `Read` with the same
code on every backend:** a run's keys are read in blocks into a buffer the
cursor owns, the cursor walks the block, and the memory backend's `Read` is a
copy from its own array. The evaluator's cursor stays synchronous, so ADR
0052's lifetime holds unchanged.

A blob opened is held open by whatever holds the run that reads it, and closed
when the last index version holding that run is released; deleting a blob that
is still open is the backend's to defer or to allow (a file backend on Unix
allows it; on Windows it defers).

### Derived data is written as a stream, and published atomically

```csharp
public interface IBlobWriter : IAsyncDisposable
{
    ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct);
    ValueTask PublishAsync(CancellationToken ct);
}
```

`IDerivedStore.CreateAsync(name)` returns a writer. Nothing is visible under the
name until `PublishAsync`, which makes the bytes as durable as the backend's
`Durability` and then makes them visible, **atomically replacing** any blob of
that name. A writer disposed without publishing leaves nothing. On the file
backend that is a temporary file, a flush, and a rename (ADR 0073).

### Everything else stays asynchronous

Creating, writing, publishing, deleting and listing blobs; every member of the
segment store, including `ReadRangeAsync`. Log reads happen in contexts that
are already asynchronous — open, subscriptions, the tail of an as-of read — and
keeping them so leaves the browser one fewer synchronous read to find.

### The segment store keeps its manifest

`ISegmentStore` gains `ReadManifestAsync` (empty when there is none) and
`WriteManifestAsync` (once; a second write fails). The manifest's bytes are
the store's, encoded by ADR 0072, so that a `log/` written by any backend means
the same thing; the backend only keeps them — on the file backend as
`log/MANIFEST`.

### The file backend's read path is chosen by benchmark

`RandomAccess.Read` on a held handle, or a `MemoryMappedFile` view, whichever
the milestone 6a benchmark favours; the result is recorded in this ADR by dated
amendment, with the numbers.

## Alternatives considered

- **Make the cursor asynchronous** (`IAsyncQuadCursor`). The uniform answer,
  and the only one if a host has no synchronous read at all. Rejected now: it
  supersedes ADR 0022 and costs every operator of the evaluator an
  `async` state machine per row on the desktop, to serve a browser whose answer
  is not known yet. If 6b finds no synchronous read in a browser worker, that is
  this ADR's revisit condition, and the cursor change is made then with the
  evidence.
- **Page each run in before a scan**, keeping storage asynchronous. Rejected
  above: a restore step, bounded by memory.
- **Keep `PutAsync(ReadOnlyMemory<byte>)`** and write runs in parts under
  separate names. Rejected: a run in parts needs its own manifest of parts and
  its own atomicity, which is a writer with publish under another name.
- **Make the log's reads synchronous too**, for symmetry. Rejected for now: no
  caller needs it, and it widens what 6b must find.
- **Hand out the backend's own bytes** (a span over a mapped view) instead of
  copying into the caller's. Zero copy, and what ADR 0040's "bytes may be held"
  promised. Rejected for the contract: a mapped view's lifetime is the mapping's,
  so a held span outlives what it points at the moment the blob is closed or
  deleted — a use-after-free that `Span` cannot express. The copy into a block
  buffer is per block, not per quad.

## Consequences

- **The scan code has one source**: the memory backend's checkpoints are read
  through `Read` as the file backend's are, so the property tests exercise the
  code the disk runs. In-memory checkpoints lose ADR 0041's zero-copy
  reinterpretation; the cost is a block copy, measured in the milestone report.
- **Every backend implements a synchronous read.** The memory and file backends
  do; the browser backend is where it may not exist, and that is the revisit
  condition.
- **`PutAsync` and `GetRangeAsync` are gone** from the public surface; nothing
  is published yet (ADR 0029), so nothing breaks outside the repository.

## Checks

- **Checked against the accepted ADRs** (0001–0069). Supersedes **0018** and
  **0040** in part, as stated. Touches **0015** (a run is still scanned where it
  lies, which is what directly queryable means), **0022** and **0052** (the
  cursor and the pin's lifetime are unchanged, which is the point), **0041**
  (runs are read in blocks; their meaning is unchanged), and **0065** (the new
  members take `ByteOffset`, `ByteCount` and `BlobName`). No conflict with the
  specification.
- **Layer ownership.** `Varve.Store`, **layer 4**. `IReadableBlob` and
  `IBlobWriter` are `[Contract]` types citing this ADR.
- **Analyzer rule.** None. `IReadableBlob.Read` and its implementations are
  `[HotPath]`.
- **Open questions owned.** None. Closes the roadmap's milestone 6 risk,
  "`IQuadSource` is synchronous and the storage contract is asynchronous", for
  the desktop.
