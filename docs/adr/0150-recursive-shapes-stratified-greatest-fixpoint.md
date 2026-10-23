# 0150 — Recursive shapes: stratified, the greatest fixpoint in each stratum, refused statically otherwise

## Status

**Accepted — filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13), 2026-10-17** (ADR 0066). Decided by the maintainer on the part 1 plan.
**It departs from the brief**, which says SHACL's "fixpoint treatment of
recursion … carr[ies] over from the shacl-rs specification". It cannot: that
specification defines none. The departure and its reason are recorded below.

## Context

### What SHACL says

SHACL 2017 §3.4.3 and SHACL 1.2 Core §6.6.3 (Working Draft, 14 October 2026)
say the same thing: a shape is *recursive* when it is related to itself by the
transitive closure of *refers to*, and "the validation with recursive shapes is
not defined in SHACL and is left to SHACL processor implementations. For
example, SHACL processors may support recursion scenarios or produce a failure
when they detect recursion." 1.2 widens *refers to* with `sh:memberShape`,
`sh:reifierShape`, `sh:someValue` and `sh:targetWhere`.

### What shacl-rs says

The shacl-rs specification (`shacl-rs-functional-spec.md`, Draft v0.8, at
`Hafeok/rust-shacl` commit `c0c4873`) was read in full for this ADR:

- **ADR-002 and §9.1**: version 1 **rejects every recursive shapes graph**. A
  cycle search over the shape-reference graph, with an edge for each
  shape-expecting parameter and no polarity, detects it at ingestion.
- **§9.2**: a deferred sketch for a later level. It names the "supported
  (Y-stratified)" semantics and cites arXiv 2108.13063, and describes it as
  "the least fixpoint of the 'one validation round' operator". It defines no
  polarity, no strata and no treatment of negation; with `sh:not` in a cycle
  the operator it names is not monotone, so the least fixpoint it appeals to
  need not exist.
- **arXiv 2108.13063** is Pareti, Konstantinidis and Mogavero, *Satisfiability
  and containment of recursive SHACL*, Journal of Web Semantics (2022),
  DOI 10.1016/j.websem.2022.100721. It studies satisfiability and containment
  over several recursive semantics; it is not a source for one. Varve does not
  cite it for its semantics.
- **The only rigorous fixpoint in shacl-rs is for paths** (§4.1: `+` and `*`
  as the least fixpoint of `R ↦ ⟦p⟧ ∪ (R ∘ ⟦p⟧)`), which ADR 0152 takes.

So there is no semantics for recursive shapes, with or without negation, to
carry over. Rejecting every recursive graph — what shacl-rs v1 specifies — is
one option below; the maintainer chose a semantics instead.

### The literature this restates

- Corman, Reutter and Savković, *Semantics and Validation of Recursive SHACL*,
  ISWC 2018, LNCS 11136, pp. 318–336, DOI 10.1007/978-3-030-00671-6_19:
  validation as an assignment of shapes to nodes, over supported assignments.
- Andreşel, Corman, Ortiz, Reutter, Savković and Šimkus, *Stable Model
  Semantics for Recursive SHACL*, The Web Conference 2020, pp. 1570–1580,
  DOI 10.1145/3366423.3380229.
- Bogaerts and Jakubowski, *Fixpoint Semantics for Recursive SHACL*, EPTCS
  345, pp. 41–47, DOI 10.4204/EPTCS.345.14.
- ShEx 2.1 (`https://shex.io/shex-semantics/`): `completeTyping` is defined on a
  stratification of the schema, each stratum's typing being "the union of all
  correct typings" given the strata below it, and §5.7.4, the *negation
  requirement*: "the dependency graph of a schema MUST NOT have a cycle that
  traverses some negated reference". The construction below is that one,
  restated for SHACL's components.

## Decision

### Definitions

Let **S** be the shapes of a shapes graph and **N** the RDF terms. An
**assignment** is a function σ: S × N → {false, true}; assignments are ordered
pointwise with false < true, and form a complete lattice.

**The dependency graph** D has the shapes as vertices and an edge s → s′ for
each pair where s *refers to* s′ (SHACL 1.2 Core §6.6.3), labelled by the
parameter through which it does. Each label has a **polarity**:

| Parameter | Polarity |
|---|---|
| `sh:node`, `sh:property`, `sh:and` (each member), `sh:or` (each member), `sh:someValue`, `sh:memberShape`, `sh:reifierShape` | positive |
| `sh:qualifiedValueShape`, in a shape with `sh:qualifiedMinCount` and no `sh:qualifiedMaxCount` | positive |
| `sh:not` | negative |
| `sh:xone` (each member) | both |
| `sh:qualifiedValueShape`, in a shape with `sh:qualifiedMaxCount` | both |
| a sibling shape, under `sh:qualifiedValueShapesDisjoint true` | both |
| `sh:targetWhere` | target |

An edge of polarity *both* counts as one positive and one negative edge. A
SPARQL-based constraint (ADR 0148) refers to no shape.

**A shapes graph is stratifiable** when no cycle of D contains a negative
edge or a target edge. Equivalently, inside each strongly connected component
of D every edge is positive, and every target edge joins two components.

**A stratification** is a function str: S → ℕ such that for every edge
s → s′, str(s′) ≤ str(s) if the edge is positive and str(s′) < str(s)
otherwise. A stratifiable graph has one: number the strongly connected
components of D in a topological order of the condensation.

### The semantics

Given a stratification with strata 1 … k, define σ₁ … σₖ in turn.

For stratum i, let Tᵢ map an assignment τ of the shapes of stratum i to the
assignment Tᵢ(τ) where Tᵢ(τ)(s, n) is **true** exactly when the validation of
n against s (SHACL 1.2 Core §6.6) produces no result whose severity is
disallowed (§6.7, §6.8.1.2) and reports no failure, with every conformance
check that validation performs against a shape s′ answered by

- σⱼ(s′, ·) for the stratum j = str(s′) < i, and
- τ(s′, ·) for str(s′) = i.

**σᵢ is the greatest fixpoint of Tᵢ.** It exists and is unique: every
reference inside a stratum is positive, and every positive parameter's
validator produces no more results when more nodes conform to the shape it
references — `sh:node`, `sh:property`, `sh:and` and `sh:memberShape` ask that
values conform, `sh:or`, `sh:someValue` and `sh:reifierShape` that some
alternative or some value does, and `sh:qualifiedMinCount` that at least so
many do — so Tᵢ is monotone, and by Knaster–Tarski its fixpoints form a
complete lattice with a greatest element.

**σ = σ₁ ∪ … ∪ σₖ.** A node n conforms to a shape s when σ(s, n) is true. The
validation results of the shapes graph are those §6.6 defines, with every
conformance check answered by σ, and the targets of a shape with
`sh:targetWhere t` are the nodes n of the data graph with σ(t, n) true.

Three properties follow:

1. **It does not depend on the stratification chosen.** Two stratifications
   order the strongly connected components consistently with D, so each
   component's fixpoint is taken over the same assignment of the components
   below it. (ShEx states the same of `completeTyping`.)
2. **It is SHACL's own semantics wherever SHACL defines one.** In a shapes
   graph with no recursive shape, every component is one shape with no edge to
   itself, Tᵢ does not depend on τ, and its one fixpoint is the value SHACL
   defines.
3. **Every fixpoint is a supported assignment** in the sense of Corman et al.,
   since it satisfies σ = T(σ); the greatest fixpoint is the maximal one.

### The cost, shown

The greatest fixpoint accepts a cycle that supports itself:

```turtle
ex:PersonShape a sh:NodeShape ;
    sh:targetClass ex:Person ;
    sh:property [ sh:path ex:knows ; sh:someValue ex:PersonShape ] .

ex:a a ex:Person ; ex:knows ex:b .
ex:b a ex:Person ; ex:knows ex:a .
```

"A person knows some person who conforms." Under the greatest fixpoint both
`ex:a` and `ex:b` conform: each is supported by the other, and nothing outside
the cycle grounds either. Under the least fixpoint neither conforms.

**Varve prefers the greatest fixpoint anyway.** Recursion in a shapes graph is
far more often universal than existential — "every person a person knows
conforms" (`sh:node` on all values) — and data with cycles is ordinary RDF.
Over the same two nodes with `sh:node ex:PersonShape` on `ex:knows`, the
least fixpoint makes **neither** conform, though nothing is wrong with either:
every cycle in the data would be a violation. The greatest fixpoint answers
that case correctly, and pays with the self-supporting existential case
above. It is the maximal-typing reading ShEx uses, so a shapes graph ported
from ShEx keeps its meaning. A caller who needs a grounded existential states
the ground explicitly (a `sh:minCount` on a path, a class, a value).

### What is refused

**A shapes graph that is not stratifiable is refused before validation**,
whatever the data. The problem (ADR 0151) names the cycle: the shapes in
order, each with the parameter that leads to the next, starting and ending at
the same shape, and the negative or target edge marked. The decision is
static — it reads the shapes graph alone — so no data, and in part 2 no
commit, can make a shapes graph that was accepted become refused.

A failure in any validation inside the computation of σ (ADR 0151) fails the
whole validation; no assignment is guessed around it.

### How it is computed (not normative)

At load, the strongly connected components of D are found and the
stratifiability check runs over them. At validation, a conformance check on a
shape whose component has no cycle is answered by memoised recursion. One on a
shape in a cyclic component starts a local greatest-fixpoint iteration over
the pairs (s′, n′) of that component reachable from it through value nodes:
every pair starts true, each is re-evaluated under the current assignment, a
pair that fails is set false, and the iteration stops when nothing changes.
The pairs reachable from a pair are closed under the operator, so the
fixpoint over them agrees with σ on them, and they are memoised as final.

### The property

A property test (`Varve.Shacl.Tests`) generates small shapes graphs with
recursion through every polarity, and small data graphs, and checks:

- every non-stratifiable graph is refused, and the cycle in the problem is a
  cycle of D through a negative or target edge;
- on every stratifiable graph, the validator's report equals the one computed
  by an independent oracle: σ by naive iteration, stratum by stratum, over all
  pairs of shapes and terms of the data graph, starting from all-true;
- the oracle's σ is a fixpoint of every Tᵢ, and no assignment above it is.

## Alternatives considered

- **Refuse every recursive shapes graph**, what shacl-rs v1 specifies and
  §6.6.3 permits. Simplest and uncontroversial. Rejected by the maintainer:
  recursion is how a shapes graph says "every person known conforms", which
  is ordinary.
- **The least fixpoint.** Grounded, so the self-supporting cycle fails.
  Rejected: it makes every cycle in the data a violation of every universal
  recursive constraint, above.
- **Well-founded semantics** (three-valued; Bogaerts and Jakubowski discuss
  recursive SHACL by fixpoint theory). Gives a meaning to more shapes graphs,
  including some negation through recursion. Rejected: whether a graph has a
  two-valued meaning would depend on the data, so a commit could turn a valid
  shapes graph into a refused one, and part 2's gate would refuse writes for a
  reason the writer did not cause.
- **Supported or stable model semantics** (Corman et al.; Andreşel et al.).
  Several models per graph, so validation is brave or cautious over a set of
  them, and deciding it is hard in general. The greatest fixpoint on a
  stratified graph is one supported assignment, chosen by a rule that is
  cheap to compute and to explain.
- **A depth cut-off** that assumes conformance past some depth, which is what
  shacl-rs's code does. Not a semantics: its answer depends on the cut-off.

## Consequences

- A self-supporting existential cycle conforms, by the example above; the
  operator's guide says so.
- Negation through recursion is refused, with the cycle named. A shapes graph
  that needs it must be restructured.
- Part 2's incremental validation inherits a static, data-independent
  stratification, which is what an affected-node computation needs.

## Checks

- **Checked against the accepted ADRs** (0001–0133, 0143–0147). Touches
  **0017** (a refusal is static, so a validator's verdict never depends on a
  refusal the commit caused). No conflict. **Departs from `docs/brief.md`**,
  point 8's "carry over", as recorded above.
- **Layer ownership.** `Varve.Shacl`, layer 3.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-20 — the construction moves to `Varve.Derivation`

Filed by SHACL, part 2a (#138, under milestone 8, #13), unaccepted until the
maintainer accepts it (ADR 0066).

The strongly connected components, the stratification and the refused cycle
are computed by `Varve.Derivation`'s `DependencyGraph` (ADR
[0172](0172-varve-derivation.md)), so Inference stratifies by the same code.
The polarity table above stays `Varve.Shacl`'s, and so does the greatest
fixpoint's iteration. The semantics is unchanged, and the property above holds
it. ADR 0171 adds that incremental validation finds the pairs a delta affects
by propagating along the same graph, polarity ignored.

**The ledger.** The ruling is ADR 0172's `StratificationIsDerivations`.
