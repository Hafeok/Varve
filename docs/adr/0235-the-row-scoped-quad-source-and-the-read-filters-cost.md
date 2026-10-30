# 0235 — The row-scoped quad source and the read filter's cost

## Status

**Filed unaccepted by milestone 9, part 1 (#202, under #14), 2026-10-30**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
on the plan in #14 (2026-10-30), question 4.

Records decision set
[`the-row-scoped-quad-source`](../decisions/the-row-scoped-quad-source.md).

## Context

ADR 0107 point 3 keeps the read filter a lookup per graph handle, never a
check per quad. Row-level access is per quad by definition, so its cost has
to be stated honestly and kept bounded. A caller without a row restriction
must pay nothing, as ADR 0107 point 4 has for `all` graphs.

## Decision

1. **`SubjectScope` and `RowScope`**, in `Varve.Rdf` (layer 1).
   - **A `SubjectScope`** is ADR 0234's subject set as a value: `All`, or
     explicit subjects, prefixes, classes and links (predicate, target IRI),
     with its `createUnder` prefixes. It has a `Union`.
   - **A `RowScope`** maps a binding's IRI to a `SubjectScope`. A binding it
     does not name is `All`. **`RowScope.All` restricts nothing.**
   - **`CallerScope`** gains `ReadableRows` and `WritableRows`. An admin's
     are `All`.
2. **`RowScopedQuadSource`**, in `Varve.Rdf`, beside `GraphScopedQuadSource`.
   It is built over an inner source, an `IRowClassification` of that source
   at its position (ADR 0231), and a `RowScope`. It shows a quad exactly
   when ADR 0230 point 4 allows it, given that the inner source has already
   applied the graph scope.
   - **`Wrap` returns the inner source itself when the scope is `All`.** The
     wrapper is skipped, not a no-op layer.
   - **`TryInternalise`, `TryExternalise` and the term members pass
     through.** A term's existence is not a row's. So the source never
     denies a term (ADR 0022, as #200 states it), and it passes
     `tests/QuadSourceContract.cs`.
   - **`Contains` and `Match`** filter. A `Match` whose graph is not in a Γ_B
     goes to the inner source unwrapped.
   - **`Estimate`** answers the inner source's bound, downgraded to an
     estimate (ADR 0049).
3. **The cost.** For each quad of a classified graph:
   - **`BindingOf(graph)`**: memoised per graph handle, as
     `PrefixDecisionMemoisedPerGraph` is;
   - **one probe** of `SubjectsOf` on the subject handle;
   - **one probe** on the object handle, when the object is not a literal and
     the predicate is not a public link. Whether a predicate is public is
     memoised per predicate handle;
   - **the decision "set ⊆ S_B"**, memoised per subject-set id. Sets are
     interned by the classification, so their number is the number of
     distinct ownership combinations, never the number of quads;
   - **each member subject**, decided once per request: an explicit subject
     by set lookup; a prefix by externalising once; a class or link by one
     `Contains` on the inner source per graph of Γ_B.

   None of it allocates per quad. The memos are concurrent maps filled only
   through `GetOrAdd`, which `VARVE0003` would report and which this ADR's
   decisions answer:
   - `RowDecisionIsTwoProbesPerQuad`, for the probes;
   - `SubjectSetDecisionMemoisedPerSet`, for the set decisions.
4. **Composition.** A row-restricted read is
   `RowScopedQuadSource(GraphScopedQuadSource(view, readable), classification, rows)`.
   The graph filter runs first, so a quad of an unreadable graph never costs
   a probe.
5. **The benchmark.** BenchmarkDotNet over REC M, with its tenants as data
   subjects and leases and spaces linked to them. Rows are ordered slower
   first:
   - a full scan;
   - a selective join;
   - a bound-subject lookup;
   - `GRAPH ?g` enumeration.

   Each is run unscoped, graph-scoped, and row-scoped with S = `all`, one
   tenant, 10% and 50% of the tenants. A row-scoped `all` must equal the
   unscoped row within noise, and an allocation test holds it to zero bytes
   per quad. `tests/Varve.Benchmarks/README.md` records the numbers per
   machine, and this ADR is held to them: a scan worse than 2× graph-scoped
   is reported, not hidden.

## Alternatives considered

- **A per-request memo of node decisions.** It would cost memory in the
  distinct nodes a request touches, which is the request's size. The
  classification's map is immutable for the position, so the probe needs no
  memo.
- **Filtering in the evaluator.** A layer-3 package would then know grants,
  which ADR 0107 refused.
- **Rewriting the query.** ADR 0107's reasons, unchanged: it is fragile
  across paths, `MINUS`, `EXISTS` and `FROM`.

## Consequences

- **`Varve.Rdf`'s baseline gains** `SubjectScope`, `RowScope`,
  `RowScopedQuadSource`, `IRowClassification` (ADR 0231),
  `RowNotWritableException` (ADR 0236) and `CallerScope`'s two members and
  constructor. They are stable 1.0 API.
- **A row-restricted scan costs the probes above.** The benchmark says how
  much.

## Checks

- **Checked against the accepted ADRs** (0001–0190). Touches:
  - **0022** and **#200**: terms pass through;
  - **0049**: estimates;
  - **0107**: points 3 and 4, kept;
  - **0026**: the hot path.

  No conflict.
- **Layer ownership.** `Varve.Rdf` (1).
- **Analyzer rule.** None new. `VARVE0003` checks the hot path, answered
  above.
- **Open questions owned.** None.
