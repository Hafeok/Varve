# 0152 — SHACL paths are evaluated in `Varve.Shacl`, and agree with the evaluator's

## Status

**Accepted — filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13), 2026-10-17** (ADR 0066). Decided by the maintainer on the part 1 plan.

## Context

A property shape's value nodes are the nodes its path reaches from the focus
node (SHACL 1.2 Core §4, §6.9.2; 2017 §2.3.1), and SHACL defines each path by
its SPARQL 1.2 property path equivalent. `Varve.Sparql.Evaluation` evaluates
property paths (ADR 0054), but it is at layer 3 beside `Varve.Shacl`, and a
same-layer reference is a violation (ADR 0003). Two implementations of one
definition drift unless something holds them together.

## Decision

1. **`Varve.Shacl` evaluates paths itself, over `IQuadSource`.** A path is
   parsed from the shapes graph once, at load, into an internal tree:
   predicate, inverse, sequence (a SHACL list of two or more), alternative
   (`sh:alternativePath`, a list of two or more), and the closures
   `sh:zeroOrMorePath`, `sh:oneOrMorePath` and `sh:zeroOrOnePath`.
2. **The semantics is a set.** A path from a focus node denotes the *set* of
   nodes it reaches (§6.9.2: value nodes are a set), so alternatives and
   sequences are deduplicated, unlike SPARQL's multiset for `/` and `|`
   (SPARQL 1.2 §18). The closures are the least fixpoints shacl-rs §4.1 states,
   `⟦p⁺⟧ = μR. ⟦p⟧ ∪ (R ∘ ⟦p⟧)` and `⟦p*⟧ = Δ ∪ ⟦p⁺⟧`, computed by a
   breadth-first search with a visited set per start, which terminates on
   cyclic data in at most |nodes| rounds. A zero-length path reaches the focus
   node whatever the graph holds, as SPARQL's `ALP` does (ADR 0054).
3. **Ill-formed paths are refused at load** (Appendix A, and §4's rules): a
   path node that is neither an IRI nor a blank node satisfying exactly one of
   the forms above, a blank node with more than one value for its path
   predicate, a list of fewer than two members, a member that is not itself a
   well-formed path, or a path that reaches itself through its own blank
   nodes. The problem names the shape and the rule.
4. **The two implementations agree, by property.** A differential test in
   `tests/Varve.Shacl.Sparql.Tests` — a test project, which may reference
   both — generates paths of every form up to a stated depth and data graphs
   with cycles, and checks for every node of the graph that `Varve.Shacl`'s
   value nodes equal the evaluator's `SELECT DISTINCT ?o { <n> path ?o }`.
   The pull request reports the iteration count and any counterexample found.
5. **One divergence, by construction, kept and stated.** The differential
   property found it on its first run (seed `1ApdSFS1VMn4`): from a focus node
   the data graph does not hold, along `( [ sh:zeroOrOnePath ex:p1 ]
   [ sh:zeroOrOnePath ex:p0 ] )`, SPARQL gives nothing and `Varve.Shacl` gives
   the focus node. SPARQL 1.2 §18 rewrites a sequence into a join through a
   fresh variable, and a zero-length step whose ends are both variables ranges
   over the graph's nodes only, so the node is lost at the second step. The
   SHACL value nodes are the nodes reached *from the focus node*, and a
   zero-length path reaches it whatever the graph holds (point 2), at every
   step as at the first. Only a node absent from the data graph is affected,
   since everything a path reaches from a node in the graph is in the graph.
   The property compares every node of the graph exactly, and at an absent
   node checks that the divergence is exactly this: SPARQL's answer is a
   subset of SHACL's, and SHACL's is empty or the focus node alone.
6. **The step that runs per value node is `[HotPath]`.** A closure's search
   allocates its visited set and frontier per focus node, not per quad, and is
   answered by a `[DesignDecision]` citing this ADR, as `PathOperator.Search`
   cites ADR 0054.

## Alternatives considered

- **Translate the path to SPARQL and call the evaluator.** One
  implementation. Impossible at layer 3, and would put the evaluator in SHACL
  Core's dependency graph, which ADR 0148 exists to avoid.
- **Move the evaluator's path code down to a shared layer 2 package.** It
  works over the algebra's `PropertyPath` records and the evaluator's
  execution context, so it would drag the algebra into SHACL Core; and a
  package for one algorithm is the grab-bag the brief warns against.
- **Trust the specification's equivalence** and skip the differential test.
  Two hand-written searches are exactly what drifts.

## Consequences

- Two path implementations, held together by a property, not by a reference.
  A fix to one that the property shows the other needs is one change across
  both.
- The differential property gives the evaluator's paths a second oracle too.

## Checks

- **Checked against the accepted ADRs** (0001–0133, 0143–0147). Touches
  **0003** (why the code is not shared), **0048** (the evaluator is not
  moved), and **0054** (the semantics compared against, and the zero-length
  rule both share). No conflict.
- **Layer ownership.** `Varve.Shacl`, layer 3; the differential test declares
  no layer.
- **Analyzer rule.** None new; `VARVE0003` holds the per-value-node step.
- **Open questions owned.** None.
