# 0040 — The storage contract's members, and where the memory backend lives

## Status

**Accepted.** 2026-09-23.

Fixes the members ADR 0018 left to "when the first backend is written, at
milestone 4", and places that backend. Does not supersede 0018: its shape, its
durability levels and its five impossibilities stand as written.

## Context

ADR 0018 decided the *shape* — an append-only segment store plus a derived blob
store, asynchronous throughout, durability declared — and said the exact members
would be fixed by the first implementation. Milestone 4 writes it: the
in-memory backend, whose durability is `None` and which is what §10's property
tests run against.

Two questions came with it.

**Which members.** 0018's table lists `CreateSegment`, `Append`, `Flush`, `Seal`,
`ReadRange`, `ListSegments`, `Detach` for the log and `Put`, `GetRange`, `Delete`,
`List` for derived data. The one that does not belong yet is `Detach`: it is the
archive's primitive (T3), and archive is later work. A member whose only caller
does not exist is scaffolding, and a memory backend's "drop" would be a stub.

**Where the backend lives.** The milestone brief named a `Varve.Store.Memory`
package. It cannot be one at layer 4, because it references `Varve.Store` and a
same-layer reference is a violation (ADR 0003). It could be layer 5. 0018 says
the memory and file backends "may live in `Varve.Store` itself, since neither
needs anything above layer 4", and puts only the browser backend in a layer 5
package, because it carries JavaScript interop that no other consumer should pay
for.

The argument *for* a separate package was that it would prove the contract is
implementable from outside `Varve.Store` with public members alone — which is
exactly what the browser backend will need, and exactly what 0018's revisit
condition is about. That proof does not need a package. It needs a second
implementation, anywhere outside the assembly.

## Decision

### The members

```csharp
public enum Durability { None, Committed, Synchronised }

public interface IStorage { ISegmentStore Log { get; } IDerivedStore Derived { get; } }

public interface ISegmentStore
{
    Durability Durability { get; }
    ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken);
    ValueTask<int> CreateSegmentAsync(CancellationToken);
    ValueTask AppendAsync(int segment, ReadOnlyMemory<byte> bytes, CancellationToken);
    ValueTask FlushAsync(int segment, CancellationToken);
    ValueTask SealAsync(int segment, CancellationToken);
    ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(int segment, long offset, int length, CancellationToken);
}

public interface IDerivedStore
{
    ValueTask PutAsync(string name, ReadOnlyMemory<byte> bytes, CancellationToken);
    ValueTask<ReadOnlyMemory<byte>> GetRangeAsync(string name, long offset, int length, CancellationToken);
    ValueTask<bool> DeleteAsync(string name, CancellationToken);
    ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken);
}
```

- **Segments are numbered by the backend, ascending, and only the newest may be
  unsealed.** `AppendAsync` to anything else fails. There is no truncate, no
  positional write, no delete of a segment — 0018's first impossibility, as a
  property of the type.
- **Bytes returned by a read are immutable and may be held.** A sealed segment
  never changes, and appending to the active one never changes a byte already
  written, so a backend may hand out a slice of its own storage — the memory
  backend does, which is what lets a checkpoint be scanned in place (ADR 0041).
  A backend that cannot promise this copies.
- **`Detach` is absent** until archive exists. Adding it then is an additive
  change to an interface that has, by then, three implementers to update — a
  cost stated now rather than a stub carried until then.
- **Segment size is an input**, `DatasetOptions.SegmentBytes`, as 0018 requires.
  The store seals the active segment and creates the next when an append would
  exceed it. A record never spans two segments.

### The memory backend lives in `Varve.Store`

`MemoryStorage` is a public type in `Varve.Store`, layer 4, with durability
`None`. It is the embedded case when nothing is persisted, and it is a real
backend rather than a test double (0018, *Consequences*).

### The external proof is a second backend, in the tests

`Varve.Store.Tests` carries a deliberately trivial second implementation of
`IStorage`, written against public members only, and runs the same contract
tests and a full commit–reopen–read cycle against it. If the contract ever needs
an internal member to be implementable, that test stops compiling. **The file
backend at milestone 6 is the real external proof**; the test backend is the
standing guard until then and after.

## Alternatives considered

- **`Varve.Store.Memory` at layer 5.** Legal under the layering rule, and it
  would have proved external implementability as a side effect of existing.
  Rejected: a package whose only content is the one backend that needs nothing
  above layer 4 is a package with no reason to change except that `Varve.Store`
  did, and every user who wanted a working store without a disk would need two
  packages. 0018 placed it in `Varve.Store` for the second reason already.
- **`Varve.Store.Memory` at layer 4.** Not an option: a same-layer reference.
- **Keep `Detach` and have memory implement it as "drop".** Rejected: it is a
  member no caller uses, implemented by one backend as a no-op-shaped deletion.
  That is the scaffolding the working method forbids.
- **Return bytes by copying into a caller buffer** (`ReadAsync(segment, offset,
  Memory<byte> destination)`). What a file API looks like, and it makes
  ownership trivially clear. Rejected for the log and for derived data alike:
  it forces a copy on the memory backend, where the slice is free, and it makes
  "a checkpoint is directly queryable" untrue for exactly the backend the
  property tests run on.

## Consequences

- **`Varve.Store` has a working store with no second package.** Open a
  `Dataset` over a `MemoryStorage` and it runs.
- **The contract is public surface in `Varve.Store`'s baseline**, and every
  member above is a line in `PublicAPI.Unshipped.txt`. The browser backend at
  milestone 6 implements it from layer 5.
- **The memory backend is bound by the ambient-clock and randomness ban** that
  applies to `Varve.Store` (ADR 0011), because it is in that assembly. 0018's
  fourth impossibility is therefore enforced for it, not merely intended.
- **"Held bytes are immutable" is a promise every backend makes.** A file
  backend that reads into a pooled buffer and returns it would break it; the
  contract documentation says so.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0039).
  Touches **0003** (the same-layer rule decides against a layer 4 package),
  **0013** (records never span segments; a torn tail is representable because
  sealing is an operation of its own), **0015** (checkpoints are derived blobs),
  **0018** (members fixed, `Detach` deferred, the memory backend where 0018 put
  it), and **0011** (the ban covers the backend). No conflict with any.
- **Layer ownership.** The contract and `MemoryStorage` are `Varve.Store`,
  **layer 4**. The browser backend remains a **layer 5** package (milestone 6).
- **Analyzer rule.** None new. The banned-symbols list scoped to `Varve.Store`
  (ADR 0011) is what enforces determinism in the backend.
- **Open questions owned.** None.
