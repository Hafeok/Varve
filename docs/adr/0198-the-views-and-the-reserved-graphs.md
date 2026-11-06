# 0198 — The explicit and implicit views, and the reserved graphs of the closure

## Status

**Filed unaccepted by Inference, part 2 (#171, under #95), 2026-11-06**
(ADR 0066). Decided by the maintainer on the plan in #95 (2026-10-27),
question 6, and the answer to D5 that moved the views into this part.
Acceptance is the maintainer's act on the pull request.

Amends **0130** point 5 (the closure's computed graphs are not writable), by a
dated amendment there.

## Context

A reader of a materialised closure needs to ask three questions: what was
asserted, what was entailed, and both. GraphDB answers them with pseudo-graphs
in `FROM`: it puts every implicit statement in the database's default graph,
and a `FROM NAMED` pseudo-graph binds `GRAPH ?g` to the pseudo-graph's IRI.
Varve's closure is placed per graph (ADR 0190), so the real graph is the
answer a `GRAPH ?g` should give.

## Decision

### 1. Two view IRIs

`varve:explicit` and `varve:implicit` are
`https://w3id.org/varve/ns#explicit` and `https://w3id.org/varve/ns#implicit`.

- **No view IRI in the query:** the dataset is as today. Under `simple`, the
  explicit part is read. Under `owl2-rl`, each graph is read as explicit ∪
  implicit (ADR 0197 point 6).
- **`FROM varve:explicit` or `FROM varve:implicit`:** the default graph is the
  merge of the explicit or implicit quads of the query's other `FROM` graphs,
  or of the dataset's default graph when there are none. Both together give
  the union.
- **`FROM NAMED varve:explicit` or `FROM NAMED varve:implicit`:** each named
  graph, the query's other `FROM NAMED` graphs or every named graph when there
  are none, is read in that part. **`GRAPH ?g` binds the real graph's IRI,
  never the view's.** That differs from GraphDB on purpose.
- **A view IRI counts as a `FROM`.** A query with `FROM` and no `FROM NAMED`
  has no named graphs, as SPARQL 1.1 §13.2 has it.
- **A graph with no materialising binding** has an empty implicit part.
- **A view with `rdf`, `rdfs`, `d` or `owl2-ql`** is
  `entailment-unsupported`: a view is a part of the closure, which tier 1 does
  not have. A view combines with `simple` and `owl2-rl`.
- **Protocol `default-graph-uri` and `named-graph-uri`** take the view IRIs as
  `FROM` and `FROM NAMED` do.

### 2. The reserved graphs

- **`varve:inferred/{enc(binding)}/tbox`**: the conclusions drawn only from a
  binding's TBox graphs (ADR 0190 point 2).
- **`varve:inferred/{enc(binding)}/{d}`**: a cross-graph binding's conclusions
  whose minimal premise set has digest `d` (ADR 0190 point 6).

Both are under `https://w3id.org/varve/ns#inferred/`. They are read like a
named graph by `GRAPH`, `FROM NAMED` and the Graph Store `GET`, through the
`IInferenceClosures` seam (ADR 0195 point 7), under ADR 0190's read rule: the
TBox graph by every reader of the dataset, a premise-set graph by a caller who
reads every graph of its set. Neither has a graph version in the log; the
Graph Store tags it with the position it was read at, as ADR 0174 point 8 does
for a report graph.

### 3. Sources held to the quad source contract

The implicit view, the reserved graphs and the staged vocabulary constants are
quad sources over the projection. Each is built against
`tests/QuadSourceContract.cs` (ADR 0022, amended 2026-10-30): it never denies
a term it will yield, including one it has not loaded because the closure is
behind; it answers with a staged handle instead, as `ReportGraphs` does since
#186.

### 4. Writing them is refused

`InferenceGraphGuard`, a dataset validator in `Varve.Protocol` and the twin of
`ReportGraphGuard`, refuses a commit with a quad in `varve:explicit`,
`varve:implicit` or any graph under `varve:inferred/` as
`graph-not-writable`, on every write path, an administrator's included
(ADR 0130 point 5, amended). The server puts it on every dataset.

### 5. Tier 2 refused at one point

Every read of a view or a reserved graph resolves the closure through ADR
0195 point 7's seam, so ADR 0195 point 8's refusal after erasure, and the
row-level refusal #14 adds, apply to them without a check of their own.

## Alternatives considered

- **GraphDB's model**, every implicit statement in the default graph and
  `?g` bound to the pseudo-graph. Loses the graph a conclusion is placed in,
  which ADR 0190's grants rest on.
- **A view by header** instead of `FROM`. A view is a choice of dataset, which
  SPARQL already expresses with `FROM`; a header would make the same query
  text mean two datasets.

## Consequences

- A query that never names a view or a reserved graph reads nothing from the
  closure under `simple`, and never waits for it.
- The views are readable over SPARQL alone, by any client.

## Checks

- **Checked against the accepted ADRs** (0001–0194, 0206, 0213–0217, 0224,
  0225). Touches **0022** (the contract), **0107**/**0190** (the read rule),
  **0130** (amended), **0174** (the twin guard and graph tagging), **0195**.
  No conflict.
- **Layer ownership.** `Varve.Protocol` (5): the guard, the dataset's
  composition; `Varve.Inference.Store` (5): the sources over the projection.
- **Analyzer rule.** None new.
- **Open questions owned.** None.
