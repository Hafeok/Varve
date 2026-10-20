# 0172 — `Varve.Derivation`: delta-driven maintenance at layer 2

## Status

**Accepted — filed unaccepted by SHACL, part 2a (#138, under milestone 8,
#13), 2026-10-20** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 2 plan (#13, 2026-10-20), with
the amendment of ADR 0003 it needs. Amends **0003** (layer 2), **0150** (the
stratification moves here) and **0152** (path evaluation moves here), by dated
amendments in those files.

## Context

#13 requires that the incremental engine "allows reuse by the Inference
milestone (#95): semi-naive maintenance from exact deltas and the
affected-node computation are not SHACL-private". Continuous queries, after
1.0, will want the same. Three pieces of ADR 0171's engine are not about
SHACL:

- a **dependency graph** whose edges carry a polarity, its strongly connected
  components, a stratification, and the refusal of a cycle through a negative
  edge — ADR 0150's construction, and stratified Datalog's;
- **regular paths** over a graph — SHACL's paths (§4), SPARQL's property
  paths, and the closures Inference maintains (`rdfs:subClassOf*`, transitive
  properties) — evaluated forwards and backwards, and the **anchors** of a
  changed triple: the start nodes whose path image may change;
- **propagation** of affected keys along a dependency graph to a least
  fixpoint, which is semi-naive evaluation's frontier.

Inference (#95) sits at layer 3 or above, beside `Varve.Shacl`, and a
same-layer reference is a violation (ADR 0003), so the shared part must be
below layer 3.

## Decision

1. **`Varve.Derivation`, layer 2**, referencing `Varve.Rdf` (1) only. Its one
   reason to change is how derived facts over a graph follow the graph's exact
   deltas. The name is not "Incremental" (an adjective, a grab-bag's name) nor
   "Maintenance" (Varve's word for format migration, ADR 0109).
2. **What it holds**, public, generic over the caller's node and label types so
   that it knows neither handles nor terms:
   - **`DependencyGraph`**: vertices `0 … n−1`, edges labelled with a
     `Polarity` (`Positive`, `Negative`, `Both`, `Target`) and an opaque label;
     `Stratify()` returns the strongly connected components (Tarjan), their
     topological order, and either a stratification or the first cycle through
     a negative or target edge, as vertices and the edges between them.
   - **`RegularPath<TLabel>`**: predicate, inverse, sequence, alternative,
     zero-or-more, one-or-more and zero-or-one, compiled once to an automaton
     (Thompson's construction, ε-closures precomputed). Over an
     `IPathGraph<TNode, TLabel>` — the caller's `Objects(s, p)` and
     `Subjects(p, o)` — it gives `Evaluate(start)` (the set of nodes reached,
     SHACL's set semantics, ADR 0152: a zero-length path reaches its start
     whatever the graph holds), `Reverse(end)` (the nodes that reach `end`),
     `Anchors(x, label, y)` (the start nodes from which a walk uses the edge
     x → y, by reverse reachability over the product of the graph and the
     automaton), and `IsBounded` (the automaton has no cycle).
   - **`Propagation<TNode>`**: affected-key sets per vertex of a
     `DependencyGraph`, seeded by the caller, propagated along edges by a
     caller-supplied transfer, component by component in topological order, to
     the least fixpoint, each node of a set's frontier transferred once
     (semi-naive).
3. **What stays SHACL's.** The polarity of each SHACL parameter (ADR 0150's
   table), the footprint of each component, the classification (ADR 0171), and
   validation itself stay in `Varve.Shacl`. It builds its dependency graph and
   compiles each `ShapePath` into a `RegularPath<RdfTerm>`; its SHACL syntax
   and SPARQL rendering stay its own.
4. **What Inference reuses unchanged**: `DependencyGraph` for rule strata,
   `RegularPath` with its anchors for tier 1's closures, `Propagation` for
   insertion. **What it adds**: deletion for least fixpoints (delete and
   re-derive, or counting) and anchors for join-shaped rule bodies. Neither is
   built here: nothing in this milestone uses them, and code nothing uses is
   scaffolding. `Propagation` takes its transfer as a function so that a
   deletion pass is a second propagation, not a change to this one.
5. **What runs per edge, per node and per changed triple is `[HotPath]`.** A
   search's visited set and frontier are allocated per call, not per triple,
   answered by a decision of this set, as ADR 0152's search is.

## Alternatives considered

- **Keep it in `Varve.Shacl`** and let Inference copy it. Two implementations
  of a stratification drift, which is ADR 0152's lesson.
- **In `Varve.Rdf`**, beside the overlay. ADR 0017 put the overlay there
  because it is part of the quad source contract's vocabulary. A fixpoint
  engine is not, and `Varve.Rdf` would gain a second reason to change.
- **Over `IQuadSource` and handles** instead of generic nodes. Simpler
  signatures, but SHACL's nodes include terms the source does not hold (ADR
  0151), and Inference may run over a rewritten view: the caller's node type
  is what both have.
- **A Datalog engine** now, with SHACL compiled into it. The general answer,
  and the wrong order: Inference decides its rule language, and SHACL would
  pay an interpretation layer per focus node.

## Consequences

- **A new package at layer 2.** ADR 0003's description of layer 2 is amended
  ("what depends on the model alone"), and `AGENTS.md`'s table gains it. It
  takes no dependency.
- **`Varve.Shacl` references it**, and its `Stratification` and path search
  become calls into it. ADR 0152's differential property runs unchanged and
  now holds `Varve.Derivation`'s paths to the evaluator's.

## Checks

- **Checked against the accepted ADRs** (0001–0157, and #134's 0163–0168).
  Touches **0003** (layer 2, amended), **0048** (the evaluator's own paths
  are not moved), **0150** (the construction moves, unchanged), **0152** (the
  search moves, the semantics unchanged). No conflict.
- **Layer ownership.** `Varve.Derivation`, layer 2.
- **Analyzer rule.** None new. `VARVE0005` takes the declaration, `DD0001`
  the direction.
- **Open questions owned.** None. Deletion for least fixpoints is Inference's
  (#95), recorded in its issue.
