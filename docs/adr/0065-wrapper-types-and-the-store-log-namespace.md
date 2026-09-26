# 0065 — Positions, ids and sizes as wrapper types; the log's values in `Varve.Store.Log`

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the adoption plan
(issue [#43](https://github.com/Hafeok/Varve/issues/43)).

It **supersedes in part**:

- **ADR [0011](0011-concurrency-single-sequencer.md)**, where its *Checks*
  expressed the transaction contract's results "over BCL primitives and
  `Varve.Rdf` types only". A position is a `Position`, not a `long`.
- **ADR [0040](0040-storage-contract-members-and-the-memory-backend.md)**,
  the parameter and return types of its storage members, not their meaning.
- **ADR [0049](0049-cardinality-estimates-on-the-quad-source.md)**, the type
  of `CardinalityEstimate.Count`.

With ADR [0062](0062-adopting-decisiondriven-analyzers.md), it retires what
remained of `VARVE0007`'s reservation: "BCL primitives" as an allowed contract
vocabulary.

Implemented in sessions 2 and 3 of #43.

## Context

`DD0013` bans naked primitives (`string`, the numeric types, `Guid`, the date
and time types, `object`) on the public surfaces of `[DomainModel]`
namespaces and `[Contract]` types. The package's decision is
`PrimitiveFreeSurfaces.NoNakedPrimitivesOnModelAndContract`. `DD0014`
requires the replacement to be a `readonly` struct with value equality, and
`DD0015` forbids implicit conversions to or from the primitive.

Varve's store surfaces are full of primitives, and most of them mean
something specific. The public API baseline of `Varve.Store` has:

- **Positions** as `long`: `Commit.Position`, `CommitResult.Position`,
  `CommitRequest.ExpectedPosition`, `Dataset.Head`, `Dataset.AsOfAsync`,
  `CheckpointAsync`, `DiffAsync`, `Subscribe`, `DatasetView.Position`,
  `StagingView.Position`, `IProjection.Position`, `LogChain.FindDivergenceAsync`,
  and the verification exception.
- **Timestamps** as `DateTimeOffset`: `Commit.Timestamp`,
  `AsOfTimestampAsync`, `PositionAt`.
- **Segment ids** as `int`, and **byte offsets and lengths** as `long` and
  `int`, on the storage contract (`ISegmentStore`, `IDerivedStore`,
  `SegmentInfo`) and on `DatasetOptions.SegmentBytes`.

`Varve.Rdf`'s surfaces mostly carry `TermHandle`, which is already a
`readonly struct` over a `ulong`, spans and `ReadOnlyMemory<byte>`. The
exceptions are `CardinalityEstimate.Count` (a `long`), `InlineValue.Integer`
(a `long`), and the `int` indices of `TermArena` and `TermSpan`.

A `long` position and a `long` byte offset are the same type. Passing one
where the other is meant compiles, and a test finds it only if a test
happens to cover that call. ADR 0011's invariant I1 (positions are dense and
ascending) is a property of positions, not of 64-bit integers, and nothing in
a `long` says so.

**What ADR 0011 actually says.** The adoption plan described 0011 as having
"chosen `long` for positions". On reading, it did not name a type. It said the
transaction results are "expressed over BCL primitives and `Varve.Rdf` types
only", as the principle the reserved `VARVE0007` would enforce. `long` was
milestone 4's implementation of that sentence, and ADR 0040 then wrote it
into the storage members. The supersession is of that sentence and of 0040's
signatures, stated exactly rather than as the plan put it.

**Where the model lives.** Every `Varve.Store` type is in the root namespace
`Varve.Store`. That covers the engine (`Dataset`, the views, the storage
contract, `MemoryStorage`) and the values the log is made of (commits,
their metadata and outcomes, settings, subscription filters). `[DomainModel]`
takes a namespace prefix. Declaring `Varve.Store` a model would hold the
engine to the model's immutability (`DD0019`), which is wrong for a dataset
that is a running sequencer. The values need a namespace of their own.

## Decision

### The model namespace is `Varve.Store.Log`

**The log is the model.** The specification's first sentence makes the log the
source of truth and every index a projection of it
([`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)).
What the log is made of is Varve's domain model at layer 4, and what reads,
indexes and serves it is the engine. The namespace says so.

These types move from `Varve.Store` to `Varve.Store.Log`, with the wrappers
below:

| Type | What it is |
|---|---|
| `Commit` | a closed commit, as a subscriber sees it |
| `CommitKind` | data, settings, erasure |
| `CommitMetadata` | what the caller says about a commit |
| `CommitOutcome` | `Committed`, `NoChange`, `Conflict`, `Rejected`, `Unavailable` |
| `CommitResult` | the outcome and its position |
| `CommitRequest` | a delta, metadata and an optional expected position |
| `DatasetSettings` | the settings in force at a position |
| `SettingsChange` | a settings commit's payload |
| `SubscriptionFilter` | what a subscriber asks for |
| `ValidationVerdict` | a validator's answer |
| `SegmentInfo` | a segment's id, length and seal |

They stay in `Varve.Store`, as the engine:

- `Dataset`, `DatasetOptions`, `DatasetView`, `StagingView`;
- `IStorage`, `ISegmentStore`, `IDerivedStore`, `MemoryStorage`, `Durability`;
- `IProjection`, `ICommitValidator`, `LogChain`;
- the two exceptions.

`Varve.Store.Log` is declared `[DomainModel]` in `Varve.Store`, citing this
decision (ADR [0064](0064-varve-configuration-and-hot-path-rules.md)).

Three public value types in `Varve.Store` are not in this ADR's list:
`TermAllocation`, `RequestTerm` and `AccessScope`. This ADR does not move them.
If `DD0010` reports one on a contract in session 3, it is sorted there, by the
two-bucket rule: a decision filed for it, or the design change.

### The wrappers

Each is a `readonly record struct` over one primitive. It has an explicit
constructor, a `Value` property, and no implicit conversion either way
(`DD0014`, `DD0015`).

| Wrapper | Over | Namespace | Replaces |
|---|---|---|---|
| `Position` | `long` | `Varve.Store.Log` | every log position: head, expected, as-of, checkpoint, diff bounds, subscription start, projection and view positions, divergence. Ordered: `IComparable<Position>` and the comparison operators, because I1 makes positions a total order. No public arithmetic: a position is not a count, and "the next position" is the sequencer's to assign. The sequencer does that through an `internal` member, which no baseline sees. |
| `CommitTimestamp` | `DateTimeOffset` | `Varve.Store.Log` | `Commit.Timestamp`, and the argument of `AsOfTimestampAsync` and `PositionAt`. Ordered, because I5 makes timestamps monotone. |
| `SegmentId` | `int` | `Varve.Store.Log` | segment numbers on `ISegmentStore` and `SegmentInfo`. Ordered, because ADR 0040 numbers segments ascending. |
| `ByteOffset` | `long` | `Varve.Store.Log` | the `offset` of `ReadRangeAsync` and `GetRangeAsync` |
| `ByteCount` | `long` | `Varve.Store.Log` | `SegmentInfo.Length`, `DatasetOptions.SegmentBytes`, and the `length` of `ReadRangeAsync` and `GetRangeAsync`. Those two took `int`. A length above `int.MaxValue` stays unrepresentable in the returned `ReadOnlyMemory<byte>`, and the backend rejects it as it rejects any out-of-range read today. |
| `QuadCount` | `long` | `Varve.Rdf` | `CardinalityEstimate.Count` and the argument of its factories |

`TermHandle` stays as it is.

**`Varve.Rdf` does not learn about positions.** Position, time and storage
belong to the log, and the log is layer 4. `QuadCount` is in `Varve.Rdf`
because a cardinality is a property of a quad source, which layer 1 defines
(ADR 0005, ADR 0049).

**The engine's own members take the wrappers too**, even where `DD0013` would
not require it (`Dataset` is neither a model type nor a contract). A value that
is a `Position` in one signature and a `long` in the next is the confusion
this ADR removes.

### What this ADR does not decide

These are sorted by the two-bucket rule when the rule reports them in sessions
2 and 3. This ADR does not pre-empt that.

- **The derived store's names** (`string` on `IDerivedStore`) and
  **`CommitResult.Reason`** (`string`).
- **`InlineValue.Integer`** (`long`, ADR 0050). An inline value is a union
  over several primitives, so it is not a single-primitive wrapper, and its
  `long` is the typed value the evaluator asked for.
- **`TermArena`'s and `TermSpan`'s `int` indices.** These are parser-core
  members expected to be `[HotPath]`, which `DD0013` exempts
  (`PrimitiveFreeSurfaces.BoundaryMembersExempt`). If one is not a hot path,
  that is a finding.
- **`CanonicalisationLimitException`'s `long` counts**, and the evaluator
  row's `int` column index. The latter is session 3's, by name, in the
  adoption plan.

**Expected public API diff: wrapper types only.** Six new types. The moved
types appear with a changed namespace, and every signature listed above takes
or returns a wrapper where it took a primitive. No member is added or removed
for any other reason.

## Alternatives considered

- **Keep `long` and suppress `DD0013` for positions.** Rejected: ADR 0062
  forbids suppressing a `DD` rule, and the only exception path is a
  `[DesignDecision]` citing a filed decision. The decision it would cite would
  have to argue that a position is just an integer, and I1 says it is not.
- **A `[DesignDecision(Scope = Boundary)]` on the storage contract**, treating
  it as the byte-level boundary where primitives are honest. Considered
  seriously, because the storage contract is where bytes meet the disk.
  Rejected: segment ids and offsets are still Varve's own coordinates, not the
  file system's. A backend that confuses an offset with a length is exactly
  the bug a type prevents. The actual boundary (`ReadOnlyMemory<byte>`) is
  already exempt.
- **One `LogCoordinate` type for segment and offset together.** Fewer types.
  Rejected: the two travel separately on every storage member. Pairing them
  would change the contract's shape, and ADR 0040 decided that shape. This ADR
  changes its types only.
- **`Varve.Store.Commits`**, the draft ADR-A13's name. Rejected: the moved
  types include settings, filters, verdicts and segments, which are not
  commits. The log is the thing they are all part of.
- **`Varve.Store.Model`.** Says what the namespace is for, and nothing about
  what is in it. `Log` is the specification's own word, and it tells a reader
  that everything in the namespace is a value the log carries.
- **A separate assembly for the log model.** It would make the model's
  dependencies visible in the reference graph. Rejected: a package whose only
  reason to change is that `Varve.Store` did, and two layer-4 packages would
  need a same-layer reference or a new layer. A namespace is enough for
  `[DomainModel]`, which takes a prefix.

## Consequences

- **The public API baseline of `Varve.Store` changes wholesale** in session 3,
  and `Varve.Rdf`'s by one type in session 2. Nothing is published yet (the
  first tag is `v0.1.0-preview.1`, ADR 0029), so no consumer breaks. The
  moved lines still leave `PublicAPI.Shipped.txt`, and the PR says so as ADR
  0035 asks.
- **The log bytes do not change.** A wrapper changes a type, not a value.
  Session 3 proves it with a one-off determinism assertion: the same script
  yields the same `log/` bytes before and after.
- **`Varve.Sparql.Store` and the smoke apps** take and pass `Position` and
  `CommitTimestamp` where they took `long` and `DateTimeOffset`. Where their
  contracts name store types, their `ArchContractTypeAssemblies` gains
  `Varve.Store`, per project (ADR 0064).
- **Arithmetic on positions leaves the public surface.** One site in `src/`
  computes a position: the sequencer's `head + 1` in `Dataset.cs`, which is the
  assignment I1 describes, and it keeps it through the internal member. Any
  caller outside the assembly that does arithmetic on positions becomes a
  compile error. That is a small design change each time, found by the
  compiler.

## Checks

- **Checked against the accepted ADRs and the specification** (0001, 0003–0005,
  0007–0018, 0021–0064, `log-and-projection-model.md` 1.3). Touches:
  - **0010** and **0013**: commits and records are unchanged; only the types
    of their fields change.
  - **0011**: superseded in part, above. The sequencer, the optional expected
    position and `Conflict(head)` all stand, and I1 and I5 are what the
    ordering members express.
  - **0015** and **0052**: as-of reads and pinned reads take a `Position`.
  - **0016** and **0042**: projections and subscriptions report and resume
    from a `Position`.
  - **0018** and **0040**: the storage contract's members are unchanged in
    number and meaning, and superseded in their types.
  - **0021** and **0046**: settings commits move namespace.
  - **0022**, **0024** and **0050**: `TermHandle` unchanged, `InlineValue` not
    decided here.
  - **0035**: the baseline change is recorded.
  - **0045**: the log encoding is untouched.
  - **0049**: `Count` becomes a `QuadCount`, and its three promises stand.
  - **0057** and **0058**: `Varve.Sparql.Store` and the staging view carry
    `Position`.

  No conflict with the specification: it speaks of positions as a dense total
  order and never of their representation.
- **Layer ownership.** `Position`, `CommitTimestamp`, `SegmentId`,
  `ByteOffset` and `ByteCount` are layer 4. `QuadCount` is layer 1.
- **Analyzer rule.** None new. `DD0013`–`DD0015` enforce it once sessions 2
  and 3 declare the model namespaces.
- **Open questions owned.** None.
