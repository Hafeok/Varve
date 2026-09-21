# 0018 — Storage abstraction: memory, file, browser

## Status

**Proposed.** 2026-09-21.

The specification fixes the *requirements*
([spec](../spec/log-and-projection-model.md) §2) and says explicitly that
byte-level layout belongs here. This ADR brings the **contract shape** options
and does not pick one. **No byte-level format is decided**; that is milestone 6.

## Context

`docs/brief.md`, constraint 3: the same core runs as an embedded library, a
standalone server, and in the browser, so the storage backend is an abstraction
from day one with in-memory, file and browser implementations.

Two things make this harder than "define an interface over `Stream`".

**The browser is not a file system.** Blazor WebAssembly documentation states
that file system access "may throw a `PlatformNotSupportedException`", and that
user state lives in browser memory and is lost when the page reloads. There is
no BCL API for the Origin Private File System or IndexedDB; both are reached
through JavaScript interop, and both are **asynchronous**. A contract shaped
like `FileStream` is a contract the browser backend has to fake. Sources:
[Blazor hosting models](https://learn.microsoft.com/dotnet/architecture/blazor-for-web-forms-developers/hosting-models#blazor-webassembly-apps),
[Blazor WebAssembly state management](https://learn.microsoft.com/aspnet/core/blazor/state-management/webassembly).

**§2 is a list of requirements on this contract, not on its implementations.**
It says the log is never rewritten, that sealed segments are immutable, that
`log/` and `derived/` are separate, that bytes are deterministic, that nothing
secret or deletable lives in the dataset directory, and that a default segment
stays below common single-file hosting limits. Several of those are things a
contract can make *unrepresentable* rather than merely forbidden, and choosing
which is the substance of this decision.

## Options

### Contract shape

**S1 — Virtual file system.** `OpenAppend(name)`, `OpenRead(name)`, `Delete`,
`List`, over `Stream`.

- Maps one-to-one onto the file backend; every .NET developer already knows it.
- Fights the browser hardest. OPFS synchronous access handles exist only inside
  a Web Worker, and IndexedDB has no stream at all, so the browser backend would
  implement a synchronous streaming abstraction over an asynchronous chunk
  store — which is where deadlocks and silent buffering live.
- `Stream` offers `SetLength` and `Seek`+`Write`. The log must never be
  rewritten, and a contract that hands every backend the ability to do it is
  relying on nobody calling it.

**S2 — Append-only segment store plus derived blob store.** Two capabilities,
matching §2's own split:

| Segment store (`log/`) | Derived blob store (`derived/`) |
|---|---|
| `CreateSegment`, `Append`, `Flush`, `Seal` | `Put`, `GetRange`, `Delete` |
| `ReadRange(segment, offset, length)` | `List` |
| `ListSegments`, `Detach` (archive) | — |

- **The forbidden operations are absent, not documented.** No truncate, no write
  at an offset, no delete of a sealed segment. §2's "the log is never rewritten"
  becomes a property of the type rather than a rule someone has to remember.
- Splits exactly where §2 splits, so "derived data is reproducible and excluded
  from version control" is a statement about *which store* something is in.
- Asynchronous throughout (`ValueTask`, `ReadOnlyMemory<byte>`), which the
  browser needs and which costs the file and memory backends nothing they cannot
  complete synchronously.
- Costs two contracts where one would do, and costs a vocabulary — segment,
  seal, detach — that has to be learned.

**S3 — One key-value blob store; emulate append.** `Get(key)`, `Put(key, bytes)`,
`Delete(key)`, `List(prefix)`. Fewest primitives, and the closest fit to
IndexedDB and to object storage.

- Append becomes read-modify-write of the active segment, which is quadratic in
  the segment and fatal for an active segment of any size. Chunking works around
  it — and chunking is S2's segment store wearing a disguise, with the
  invariants left to the caller.

**S4 — Backend-specific contracts over a shared core.** Honest about the
differences; each host gets what it can do well. Rejected on cost before it is
evaluated on merit: every consumer of the store then has three code paths, and
the brief's "one core, three hosts" becomes three cores.

### How the backends map, under S2

| Capability | Memory | File | Browser |
|---|---|---|---|
| Append to active segment | list of buffers | append + explicit flush | OPFS sync access handle in a Worker, or one IndexedDB record per chunk |
| Durability | none (and says so) | flush to disk | OPFS flush, or IndexedDB transaction commit — **weaker, and not the same thing** |
| Sealed segment immutable | frozen buffer | read-only | read-only by convention |
| Read range | slice | positional read | read at offset, or fetch chunk |
| Detach for archive | drop | move out of `log/` | out of scope for a browser |
| Ignore file at creation | n/a | write `.gitignore` for `derived/` | n/a |

**The durability row is the one that does not reconcile**, and it is an open
choice the contract has to make explicitly: either `Flush` means the same thing
everywhere and the browser cannot honour it, or the contract carries a
durability *level* and a caller can ask what it got. Pretending they are the
same is the option that produces a bug report about lost commits.

### Segment sizing

§2 requires a default segment size below common single-file hosting limits,
because the directory will be checked into version control. The binding case is
a host that refuses large files outright. The exact number is a milestone 6
decision and should be taken against the limits in force then rather than
guessed now; what this ADR fixes is that the size is a **policy input to the
contract**, not a constant compiled into a backend.

## What the contract must make impossible, not merely forbid

Whichever shape wins, these are the tests to apply to it:

1. **No rewrite of a sealed segment.** Not by truncate, not by positional write.
2. **No key material, and nothing that must be deletable, in the dataset
   directory.** The key store (ADR 0019) is not a client of this contract at
   all, and the file backend refuses a key store path inside the dataset.
3. **`log/` and `derived/` cannot be confused.** Dropping everything derived
   must be an operation the type makes safe.
4. **Deterministic bytes.** Nothing in the contract may admit ambient time,
   ambient randomness or iteration-order dependence — §10's determinism test
   compares two machines byte for byte.
5. **A discarded unclosed tail is representable** (ADR 0013). Recovery has to be
   able to find the last closed commit and drop what follows.

## Consequences

**Async all the way through, or the browser is a second-class host.** That
decision propagates upward: if the storage contract is asynchronous, then so is
everything in `Varve.Store` that touches it, up to and including the commit
path. Constraint 3 says the browser is a first-class host; this is where that
costs something.

**The memory backend is a real backend, not a test double.** It is what the
embedded case uses when nothing is persisted, and it is what the property tests
in §10 run against. Its durability answer is "none", stated rather than implied.

**A browser backend is not merely an implementation.** It needs JavaScript
interop, which means a host-specific dependency, and it is the reason the
roadmap should carry a WASM smoke build from milestone 3 rather than discovering
at milestone 6 that the contract cannot be implemented there.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0017). Touches
  **0013** (records, sealing and the discarded tail are storage-level
  operations), **0014** (the chain is in the bytes this contract writes),
  **0015** (checkpoints are derived blobs; `Detach` is the archive's primitive),
  and **0019** (the key store must *not* be a client of this contract). No
  conflict with any.
- **Layer ownership.** The storage contract is owned by `Varve.Store` at
  **layer 4**. The **memory** and **file** backends may live in `Varve.Store`
  itself, since neither needs anything above layer 4. The **browser** backend is
  proposed as a separate **layer 5** package, because it carries a host-specific
  interop surface that every other consumer would otherwise pay for. The
  alternative — all three inside `Varve.Store`, selected at runtime — is
  simpler to wire and makes the package carry browser code for server users;
  that choice is part of what this ADR leaves open.
- **Analyzer rule.** None new. Two existing reservations bear on it:
  **VARVE0007** (contracts use `Varve.Rdf` types or BCL primitives only), and
  the banned-symbols entry for ambient clock and randomness noted in ADR 0011,
  which requirement 4 above depends on.
- **Open questions owned.** None of Q1–Q8. The durability-level question raised
  above is new, is this ADR's to settle when it is decided rather than proposed,
  and is recorded in `docs/roadmap.md`.
