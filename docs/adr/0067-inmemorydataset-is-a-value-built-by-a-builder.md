# 0067 — `InMemoryDataset` is an immutable value, assembled by `InMemoryDatasetBuilder`

## Status

**Accepted.** 2026-09-26. Decided by the maintainer on the adoption plan
(issue [#43](https://github.com/Hafeok/Varve/issues/43)), in place of the
plan's first proposal, which could not work (see Context). Refines ADR
[0022](0022-quad-source-term-handle.md)'s in-memory dataset without changing
anything 0022 decided: it is still a quad source with its own interning table,
at layer 1. Implemented in session 2 of #43.

## Context

`InMemoryDataset` is the quad source that holds a parsed file. It gives layer
3 something to run against without a store (ADR 0022), and it is what the
conformance harness, the evaluator tests, the smoke apps and the benchmarks
build their data in. It lives in `Varve.Rdf`, which ADR
[0064](0064-varve-configuration-and-hot-path-rules.md) declares a
`[DomainModel]` namespace. It is mutable: `Add` and `Internalise` change it
after it has been handed out.

That is the distinction the log-first model draws for the store. The log's
values are immutable, and what holds and changes them is the engine (ADR
[0065](0065-wrapper-types-and-the-store-log-namespace.md)). A mutable store of
values sitting in the model namespace breaks it. The model is where a
consumer may assume that what it was given cannot change under it, and a
pinned read or a snapshot is only one if that holds.

**The plan's first proposal was to move `InMemoryDataset` to
`Varve.Rdf.Datasets`**, "a sibling namespace outside the `[DomainModel]`
prefix in the same assembly". Checked against the package, that does not
exist.

- **The prefix covers sub-namespaces.** `DomainModelNamespaces` matches a
  prefix against its own namespace and every namespace under it, as the
  `DD0010` page states.
- **Every public type must be under the root.** `DD0006` requires every
  public type of the assembly to be under its root namespace, `Varve.Rdf`.
- **So nothing in the assembly is outside the model.** Every namespace a
  public type in `Varve.Rdf` may occupy is under the model's prefix.
  `Varve.Rdf.Datasets` would be model exactly as `Varve.Rdf` is. Reported
  upstream as
  [decision-driven-analyzers#46](https://github.com/Hafeok/decision-driven-analyzers/issues/46).

**The rule would not have said so either.** `DD0019` checks the shape of a
model type's non-private members: setters, `readonly`, collection-typed
members, `readonly` structs. It does not see mutation through a method over
private state, which is exactly `InMemoryDataset`'s shape. So it would not
report the type wherever it lived. Reported upstream as
[decision-driven-analyzers#47](https://github.com/Hafeok/decision-driven-analyzers/issues/47).
This decision therefore rests on the design reason above, not on a
diagnostic.

## Decision

**`InMemoryDataset` becomes an immutable value.** Once made, its quads and its
interning table do not change.

- It stays in `Varve.Rdf`, and it stays an `IQuadSource`.
- It keeps every contract member: `Match`, `Estimate`, `TermComparer`,
  `TryInternalise`, `TryExternalise`, `TryGetInlineValue`.
- It keeps its read-only members: `Count` and `TermCount`.
- It loses `Add` and `Internalise`.

`TryInternalise` is already a lookup that answers false for a term the source
has never seen. ADR 0022 calls that a read-only source's entitlement, so its
meaning is unchanged.

**`InMemoryDatasetBuilder` assembles it**: a `sealed` class in `Varve.Rdf`
carrying today's mutators (`Internalise`, `TryInternalise`, both `Add`
overloads), and a `ToDataset()` that returns an `InMemoryDataset` snapshot.
This is the package's documented escape hatch
(`ImmutableModel.BuildersAreTheEscapeHatch`): a sealed `*Builder` in the model
namespace is exempt from `DD0019` and may appear on no contract.

**Handles are stable across snapshots.** The builder only ever appends to its
interning table. A handle it issued means the same term in every
`InMemoryDataset` it produces, before and after later additions. A caller that
builds, snapshots, adds and snapshots again can compare handles between the
two snapshots with the snapshots' `TermComparer`, as it can today with one
mutable dataset. The snapshot copies. It does not alias the builder's
collections, because aliasing them would make the "immutable" value change
when the builder did.

## Alternatives considered

- **Move it to `Varve.Rdf.Datasets`.** The plan's proposal. Rejected as
  impossible under the package's semantics (Context), and reported upstream.
  If #46 adds an exact-namespace `[DomainModel]`, a move becomes possible. It
  would still leave a mutable store in the package's public surface beside the
  model, which this decision prefers not to have at all.
- **Leave it as it is.** No API change. `DD0019` does not report it, so the
  build stays green. Rejected: it keeps a mutable type in the model on the
  strength of a rule's blind spot. That is the shortest path to green the
  package's guard sentences exist to stop, arrived at by accident.
- **Move the model down to `Varve.Rdf.Model`**, leaving the dataset (and the
  term arena) in `Varve.Rdf` outside it. Rejected: every public type in
  `Varve.Rdf` changes namespace. The expected public API diff of the adoption
  is wrapper types, the attributes and this one change, and a whole-assembly
  rename is not that.
- **A new assembly for in-memory stores.** A package whose only reason to
  change is that `Varve.Rdf` did, at layer 2 beside the syntaxes it does not
  belong with. Rejected.
- **Make `InMemoryDataset` a `*Builder` itself**, by renaming it. The smallest
  diff. Rejected: a builder may appear on no contract, and an in-memory
  dataset is passed to the evaluator as an `IQuadSource` on every test. The
  value is the thing that gets passed; the builder is the thing that is
  filled.

## Consequences

- **The public API diff of `Varve.Rdf`** gains `InMemoryDatasetBuilder` and
  `ToDataset()`, and loses `InMemoryDataset.Add` and `.Internalise`. It is
  recorded in the baseline, and nothing is published yet (ADR 0029).
- **Every caller that fills a dataset changes** from
  `new InMemoryDataset()` plus `Add` to a builder plus `ToDataset()`. The
  callers are the conformance harness, the evaluator and Turtle tests, the
  benchmarks and the two smoke apps. The change is mechanical, and a caller
  that adds after querying takes a second snapshot.
- **The benchmarks that measure `InMemoryDataset`** measure the snapshot. The
  one-off copy at `ToDataset()` is outside every measured loop, and the
  before-and-after numbers are reported in session 2.
- **`TermArena`** is also mutable and also in `Varve.Rdf`, and `DD0019` does
  not report it for the same reason. It is the parser's per-statement scratch,
  and a hot-path type. This ADR does not decide it. Session 2 sorts it under
  the two-bucket rule: a decision that the arena is a hot-path pool, or the
  design change.
- **`QuadOverlay`** is already immutable over an immutable base and delta, and
  is unaffected.

## Checks

- **Checked against the accepted ADRs** (0001, 0003–0005, 0007–0018,
  0021–0066). Touches:
  - **0005**: the evaluator still runs over any quad source.
  - **0022**: the interning table and handles are unchanged in meaning, and
    handles are stable across snapshots.
  - **0024**: terms are unchanged.
  - **0049**: `Estimate` stays exact by scan.
  - **0050**: `TryGetInlineValue` still answers false.
  - **0058**: validators still see sources through `IQuadSource`.
  - **0064**: `Varve.Rdf` is a model namespace.
  - **0065**: the same log-first distinction.

  The specification is not touched.
- **Layer ownership.** Unchanged: `Varve.Rdf`, layer 1.
- **Analyzer rule.** None new. `DD0019` exempts the builder, and `DD0010`
  keeps it off contracts. Two upstream issues, #46 and #47, are cited above.
- **Open questions owned.** None.
