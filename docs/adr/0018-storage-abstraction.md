# 0018 — Storage abstraction: memory, file, browser

## Status

**Accepted.** 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§2, which fixes the requirements and says byte-level layout belongs here.

This ADR was **Proposed** earlier on 2026-09-21 and brought contract-shape
options without choosing. It was never accepted, so it is completed in place
rather than superseded; the options that lost are kept under *Alternatives
considered*.

**No byte-level format is decided here.** That is milestone 6.

**Revisit condition** (see *Consequences*): the in-memory and browser backends
cannot both implement the contract without leaking backend detail. Meeting it
supersedes this ADR.

## Context

`docs/brief.md`, constraint 3: the same core runs embedded, as a server, and in
the browser, so the storage backend is an abstraction from day one.

Two things make this harder than "define an interface over `Stream`".

**The browser is not a file system.** Blazor WebAssembly documentation states
that file system access "may throw a `PlatformNotSupportedException`", and that
user state lives in browser memory and is lost when the page reloads. There is
no BCL API for the Origin Private File System or IndexedDB; both are reached
through JavaScript interop, and both are **asynchronous**. A contract shaped
like `FileStream` is one the browser backend has to fake. Sources:
[Blazor hosting models](https://learn.microsoft.com/dotnet/architecture/blazor-for-web-forms-developers/hosting-models#blazor-webassembly-apps),
[Blazor WebAssembly state management](https://learn.microsoft.com/aspnet/core/blazor/state-management/webassembly).

**§2 is a list of requirements on this contract**, and several of them are
things a contract can make *unrepresentable* rather than merely forbidden.
Choosing which is the substance of this decision.

## Decision

**An append-only segment store plus a derived blob store**, split exactly where
§2 splits, asynchronous throughout.

| Segment store (`log/`) | Derived blob store (`derived/`) |
|---|---|
| `CreateSegment`, `Append`, `Flush`, `Seal` | `Put`, `GetRange`, `Delete` |
| `ReadRange(segment, offset, length)` | `List` |
| `ListSegments`, `Detach` (archive) | — |

**The forbidden operations are absent from the type, not documented in prose.**
No truncate, no write at an offset, no delete of a sealed segment. §2's "the log
is never rewritten" becomes a property of the contract rather than a rule
someone has to remember, and that is the whole argument for this shape over the
alternatives.

Asynchronous throughout (`ValueTask`, `ReadOnlyMemory<byte>`), which the browser
needs and which costs the file and memory backends only a completed task.

### Durability is declared, not assumed

**The backend declares the guarantee it gives**, and the contract does not
pretend the guarantees are equal:

| Level | Means |
|---|---|
| `Synchronised` | written through to durable media — `fsync` or equivalent |
| `Committed` | handed to a transactional store that reported success — an IndexedDB transaction |
| `None` | in memory only, lost on process exit |

A caller that needs to know what it got can ask. A caller that does not, does
not have to — but nothing in the contract will tell it that an IndexedDB commit
and an `fsync` are the same thing, because they are not, and the alternative to
saying so is a bug report about lost commits.

### How the backends map

| Capability | Memory | File | Browser |
|---|---|---|---|
| Append to active segment | list of buffers | append + explicit flush | OPFS sync access handle in a Worker, or one IndexedDB record per chunk |
| Durability level | `None` | `Synchronised` | `Committed` |
| Sealed segment immutable | frozen buffer | read-only | read-only by convention |
| Read range | slice | positional read | read at offset, or fetch chunk |
| Detach for archive | drop | move out of `log/` | out of scope for a browser |
| Ignore file at creation | n/a | write `.gitignore` for `derived/` | n/a |

### Segment sizing is a policy input

§2 requires a default segment size below common single-file hosting limits,
because the directory will be checked into version control. The number is a
milestone 6 decision taken against the limits in force then. What this ADR fixes
is that the size is an **input to the contract**, not a constant compiled into a
backend.

### What the contract must make impossible

1. **No rewrite of a sealed segment.** Not by truncate, not by positional write.
2. **No key material, and nothing that must be deletable, in the dataset
   directory.** The key store (ADR 0023) is not a client of this contract at
   all, and the file backend refuses a key store path inside the dataset.
3. **`log/` and `derived/` cannot be confused.** Dropping everything derived is
   an operation the type makes safe.
4. **Deterministic bytes.** Nothing in the contract may admit ambient time,
   ambient randomness or iteration-order dependence — §10 compares two machines
   byte for byte.
5. **A discarded unclosed tail is representable** (ADR 0013). Recovery finds the
   last closed commit and drops what follows.

## Alternatives considered

- **A virtual file system** — `OpenAppend`, `OpenRead`, `Delete`, `List` over
  `Stream`. Maps one-to-one onto the file backend and every .NET developer knows
  it. Lost on two counts. It fights the browser hardest: OPFS synchronous access
  handles exist only inside a Web Worker and IndexedDB has no stream at all, so
  the browser backend would implement a synchronous streaming abstraction over
  an asynchronous chunk store. And `Stream` offers `SetLength` and `Seek`+`Write`
  — it hands every backend the ability to rewrite the log and relies on nobody
  calling it.
- **One key-value blob store, append emulated** — `Get`, `Put`, `Delete`,
  `List`. Fewest primitives and the closest fit to IndexedDB and object storage.
  Lost because append becomes read-modify-write of the active segment, which is
  quadratic in the segment. Chunking works around it, and chunking is the
  segment store in disguise with the invariants left to the caller.
- **Backend-specific contracts over a shared core.** Honest about the
  differences. Lost on cost before merit: every consumer of the store grows
  three code paths, and "one core, three hosts" becomes three cores.
- **One durability level, defined as the weakest.** Would remove the declaration
  above. Lost because it makes the file backend's real guarantee invisible to a
  caller who has it and needs it.

## Consequences

**Async all the way through, or the browser is a second-class host.** That
propagates upward: if the storage contract is asynchronous then so is everything
in `Varve.Store` that touches it, up to and including the commit path.
Constraint 3 says the browser is first-class; this is where that costs
something.

**The memory backend is a real backend, not a test double.** It is the embedded
case when nothing is persisted, and it is what §10's property tests run against.
Its durability answer is `None`, stated rather than implied.

**A browser backend is not merely an implementation.** It needs JavaScript
interop and therefore a host-specific dependency, which is why the roadmap
carries a WASM smoke build from milestone 3 rather than discovering at milestone
6 that the contract cannot be implemented there.

**Revisit condition.** If the in-memory and browser backends cannot both
implement this contract without leaking backend detail into it — a
browser-shaped member that memory must stub, or the reverse — the shape is
wrong and this ADR is superseded. **The contract's exact members are fixed when
the first backend is written, at milestone 4**; what is fixed now is the shape.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0017, 0020–0023).
  Touches **0013** (records, sealing and the discarded tail are storage-level
  operations), **0014** (the chain is in the bytes this contract writes),
  **0015** (checkpoints are derived blobs; `Detach` is the archive's primitive),
  and **0023** (the key store must *not* be a client of this contract). No
  conflict with any.
- **Layer ownership.** The storage contract is owned by `Varve.Store` at
  **layer 4**. The **memory** and **file** backends may live in `Varve.Store`
  itself, since neither needs anything above layer 4. The **browser** backend is
  a separate **layer 5** package, because it carries a host-specific interop
  surface that every other consumer would otherwise pay for.
- **Analyzer rule.** None new. **VARVE0007** (contracts use `Varve.Rdf` types or
  BCL primitives only) and the ambient clock and randomness ban from ADR 0011
  both apply; requirement 4 depends on the latter.
- **Open questions owned.** None of Q1–Q9. The durability question the Proposed
  version raised is settled above by declaration.
