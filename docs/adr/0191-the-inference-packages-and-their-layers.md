# 0191 — The inference packages and their layers

## Status

**Accepted — filed unaccepted by Inference, part 1 (#170, under #95),
2026-10-27** (ADR 0066). Decided by the maintainer on the plan in #95
(2026-10-27), question 2. Acceptance is the maintainer's act on the pull
request. **Amends ADR [0003](0003-package-layering.md)** by a dated amendment:
three packages join the layer table.

## Context

Inference has three tiers (#95). Each tier needs something different, and
each need sits at a different layer:
- **Tier 1** rewrites a query's algebra against an ontology read from a quad
  source. It needs the algebra (`Varve.Sparql`, layer 2) and the dependency
  graphs and paths of `Varve.Derivation` (layer 2). It needs no evaluator.
- **Tier 2** materialises a closure by rules.
  - Some of the rules are SPARQL `CONSTRUCT`, which needs the evaluator
    (`Varve.Sparql.Evaluation`, layer 3).
  - Some are `sh:rule`, which needs `Varve.Shacl` (layer 3).
  - The projection that keeps the closure needs `Varve.Store` (layer 4).
- **Tier 3** is a pre-commit validator (ADR 0017). The contract it implements
  is `Varve.Store`'s.

`Varve.Store` must learn nothing about inference, as it learned nothing about
SPARQL (ADR 0005) or SHACL (ADR 0169). Same-layer references are refused (ADR
0003), so the three needs make three packages.

## Decision

1. **`Varve.Sparql.Entailment`, layer 3.**
   - **References:** `Varve.Sparql` (2), `Varve.Derivation` (2), `Varve.Rdf`
     (1), `Varve.Xsd` (0) and `Varve.Iri` (0).
   - **What it holds:**
     - the entailment regimes as a value;
     - the TBox merge, a view of a quad source in which every graph is read
       with the TBox graphs (ADR 0193);
     - the saturation of the schema;
     - the rewriting of a query's basic graph patterns.
   - **What it does not hold:** an evaluator. It reads through `IQuadSource`
     and is usable over any dataset, without the store.
   - It sits beside `Varve.Sparql.Evaluation` and `Varve.Shacl` and references
     neither.
2. **`Varve.Inference`, layer 4.** It holds the rule engine, OWL 2 RL/RDF as
   rules, custom rules and the deletion pass.
   - **References:** `Varve.Sparql.Evaluation` (3), `Varve.Shacl` (3),
     `Varve.Sparql.Entailment` (3, for the schema model and vocabulary),
     `Varve.Derivation` and `Varve.Sparql` (2), and `Varve.Rdf` (1).
   - **Why layer 4:** `CONSTRUCT` rules need the evaluator and `sh:rule`
     needs `Varve.Shacl`, both at layer 3.
   - **Not `Varve.Shacl.Sparql`,** also at layer 4. An `sh:condition` that
     uses SHACL-SPARQL receives that extension through `Varve.Shacl`'s
     extension contract (ADR 0148), passed in by the host.
   - It is usable over any `IQuadSource`. It is built in #171.
3. **`Varve.Inference.Store`, layer 5.** It holds the inference projection,
   the tier-3 pre-commit validator, and the TBox binding as a value the host
   maps from its declaration, as `ShapesBinding` is mapped (ADR 0170 point 5).
   - **References:** `Varve.Inference` (4), `Varve.Store` (4) and `Varve.Rdf`
     (1).
   - **Not `Varve.Management` and not `Varve.Shacl.Store`,** both of which it
     does not need.
   - It is built in #171.
4. **What the other layers gain.**
   - `Varve.Protocol` (5) references `Varve.Sparql.Entailment` (3) to rewrite
     a query. For the closure (#171) it defines a seam the server implements,
     as it does for validation reports (ADR 0174 point 8).
   - `Varve.Management` (4) gains the binding's terms (ADR 0194).
   - `Varve.Server` (6) composes all of them.
5. **Hot paths.**
   - What runs per rewritten pattern, per changed quad and per rule firing is
     `[HotPath]`.
   - A rewriting's tables are built once per query, not per solution. That is
     answered by this set's `TablesBuiltPerQuery`.

## Alternatives considered

- **One package for all three tiers,** at layer 5. The browser would carry
  the store for a rewriting it does not need (#95, decision 8), and the
  rewriting could not be used without the store.
- **The rewriting inside `Varve.Sparql.Evaluation`.** The evaluator would
  gain a second reason to change, and a layer-3 rewriting that needs only the
  algebra would grow the evaluator's package for every host.
- **`Varve.Inference` at layer 3.** It needs the evaluator for `CONSTRUCT`
  rules, and a same-layer reference is refused.
- **The projection in `Varve.Store`.** ADR 0005's reason, again: the store
  stays free of query languages and rules.

## Consequences

- **Three new packages.** The README's package table and `AGENTS.md`'s layer
  table gain them as each is built. None takes a new dependency.
- **The browser host** gets tier 1 by referencing `Varve.Sparql.Entailment`
  alone. Its size cost is measured in #170.

## Checks

- **Checked against the accepted ADRs** (0001–0178, and 0190). Touches:
  - **0003**: three packages, amended;
  - **0005**: the store stays rule-free;
  - **0048**: the evaluator is unchanged;
  - **0148**: the SHACL extension contract, reused;
  - **0169** and **0170**: the binding-as-a-value pattern;
  - **0172**: `Varve.Derivation` reused;
  - **0174**: the protocol's seam pattern.

  No conflict.
- **Layer ownership.** As above. `VARVE0005` checks each declaration; `DD0001`
  checks the direction.
- **Analyzer rule.** None new.
- **Open questions owned.** None.
