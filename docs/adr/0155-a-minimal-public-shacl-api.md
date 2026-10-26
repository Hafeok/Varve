# 0155 — A minimal public SHACL API, and the shapes model kept internal

## Status

**Accepted — filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13), 2026-10-17** (ADR 0066). Decided by the maintainer on the part 1 plan.

## Context

The brief keeps public API minimal and tracked: every public member is a
visible decision. A SHACL validator has an obvious large surface — the parsed
shapes, their targets, paths and constraints — and an obvious consumer for
it, Graphsite, which will drive a milestone after 1.0 ("SHACL shapes model").
Until that consumer exists, a public model would be frozen at 1.0 with no one
using it.

Part 2 needs more than "validate once": a pre-commit validator loads its
shapes once and validates every commit, and an incremental projection
validates only the focus nodes a delta affects. Part 2 must be able to add
what it needs without changing what this part made public.

## Decision

1. **Public in `Varve.Shacl`**, and nothing else:
   - **`ShapesGraph`** — opaque, immutable and safe to share across threads:
     `ShapesGraph.Load(IQuadSource, GraphPattern, ShaclOptions?,
     CancellationToken)` returns a `ShapesLoad`, which holds the shapes or the
     problems that refused them (ADR 0151). It exposes no shape, target, path
     or constraint.
   - **`ShaclValidator.Validate(ShapesGraph, IQuadSource, GraphPattern,
     ShaclOptions?, CancellationToken)`** returns a `ValidationOutcome`: a
     `ValidationReport` (ADR 0154) or the problems of a failure.
   - **`ShaclOptions`** — `Extensions` (ADR 0148), `RegexTimeout`,
     `SubClassOfInShapesGraph`, `ConformanceDisallows`, `Canonicalisation`.
   - **`ShaclProblem`** and **`ShaclProblemKind`**.
   - **The extension contract** (ADR 0148): `IShaclExtension`,
     `IShaclConstraint`, and the sealed context and sink types they are given.
   The values a validation answers — `ValidationReport`, `ShaclResult`,
   `ValidationSummary`, `ShaclProblem` — are the package's model namespace,
   `Varve.Shacl.Model` (ADR 0069's pattern); the rest is not
   (`ShaclModelNamespace`). Its counts are `int` and a problem's message a
   `string`: a count and text for a person (`ShaclModelPrimitives`).
2. **The shapes model is internal**, designed knowing it will become public:
   - Its types are immutable and hold `RdfTerm`s, never handles of any source,
     so a shapes graph is independent of every data source it validates.
   - It holds nothing from a parser or an evaluator: no algebra node, no query
     string compiled for an engine, no `Regex` — a pattern is held as its
     translation (ADR 0153) and compiled per validation.
   - No `InternalsVisibleTo` but the test assemblies (ADR 0003).
3. **What part 2 adds is additive.** A focus-node restriction on
   `Validate`, and the dependency information an affected-node computation
   needs, are new members beside these; none of these changes for them. The
   pull request that adds them shows that as a baseline diff with no removed
   line.

## Alternatives considered

- **A public shapes model now.** Rejected by the maintainer: it would be the
  1.0 surface with no consumer, and its first consumer would want it shaped
  differently.
- **Validate-only, no `ShapesGraph`.** Smaller still. Rejected: part 2 would
  re-parse a shapes graph on every commit, or add the type later and change
  `Validate`'s signature.
- **Exceptions for refusals and failures.** Rejected: a refused shapes graph
  is an expected outcome with several problems to report, not a programming
  error; cancellation stays an exception, as everywhere.

## Consequences

- `Varve.Shacl`'s baseline is small, and the pull request shows it in full.
- Graphsite's milestone makes the model public by its own ADR.

## Checks

- **Checked against the accepted ADRs** (0001–0133, 0143–0147). Touches
  **0118** (API alignment while everything is preview) and **0003**
  (`InternalsVisibleTo` toward tests only). No conflict.
- **Layer ownership.** `Varve.Shacl`, layer 3.
- **Analyzer rule.** None; the public API analyzer records the baseline.
- **Open questions owned.** None. The public model is owned by the "SHACL
  shapes model" milestone after 1.0.

## Amendment, 2026-10-20 — what part 2 adds

Filed by SHACL, part 2a (#138, under milestone 8, #13), unaccepted until the
maintainer accepts it (ADR 0066).

As point 3 foresaw, the additions are additive, and the pull request shows the
baseline diff with no removed line:

- `ShapesGraph.Classify()` and the classification it returns (ADR 0171);
- **one entry point for incremental validation**,
  `IncrementalValidation.Compare(shapes, after, dataGraph, delta, options,
  cancellationToken)`, returning an `IncrementalOutcome`: either the
  problems that stopped it, or an `IncrementalComparison` with the affected
  focus nodes, the number of affected pairs, the reports restricted to them
  before and after the delta, and the results introduced (ADRs 0171, 0173).
  The part 2 plan named two members instead,
  `ShaclValidator.Validate(…, FocusSelection)` and `ShapesGraph.AffectedBy`;
  the maintainer accepted the single call on #140, because a gate needs the
  affected set, both restricted reports and the introduced results together,
  and each is cheap only when computed with the others;
- `IShaclFootprint`, an optional extension member through which an extension
  constraint states what it reads, with `FootprintContext` for the anchors
  it adds (`Varve.Shacl.Sparql` implements it);
- `ShaclOptions.RefuseNonDeterministicFunctions`, which ADR 0169's binding
  sets, and `ShapesGraphContext.RefuseNonDeterministicFunctions`, which an
  extension reads.

The shapes model stays internal.

**The ledger.** No new ruling here; ADR 0155's set stands.

## Amendment, 2026-10-23 — what part 2b adds

Filed by SHACL, part 2b (milestone 8, #13), unaccepted until the maintainer
accepts it (ADR 0066).

Additive, for the validation projection (ADR
[0174](0174-the-validation-projection.md)); the pull request shows the
baseline diff with no removed line:

- `IncrementalComparison.Pairs`: every affected (shape, focus node) pair, as
  a `FocusPair` with its results after the delta, or none when it is no
  longer a target;
- `ShaclValidator.ValidateByPair`, returning a `PairedOutcome`: the whole
  report and every targeted pair with a result;
- `ValidationReport.Compose(header, parts)`: one report from parts, under a
  header's imports and conformance set;
- `ValidationReport.WriteTo` and `ValidationReport.ReadFrom`: a report as
  bytes and back, paths included, the same canonical N-Quads after;
- `ValidationOutcome.Reported` and `ValidationOutcome.Failed`, for a store
  that serves a report it keeps.

The shapes model stays internal. The bytes of `WriteTo` are derived and
droppable, versioned by their first four bytes, and refused when not this
version's.

**The ledger.** No new ruling here; ADR 0155's set stands, and the
projection's rulings are ADR 0174's.
