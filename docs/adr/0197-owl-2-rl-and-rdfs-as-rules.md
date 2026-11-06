# 0197 — OWL 2 RL/RDF and RDFS as rules: the materialising profiles

## Status

**Filed unaccepted by Inference, part 2 (#171, under #95), 2026-11-06**
(ADR 0066). Decided by the maintainer on the plan in #95 (2026-10-27),
questions 3b, 3c and 5, with `sameAs` off in this part. Acceptance is the
maintainer's act on the pull request.

Amends **0192** (`owl2-rl` served) and **0194** (the `RDFS` and `OWL2RL`
profiles, `crossGraph` and `SameAsOff` in effect), each by a dated amendment.

## Context

OWL 2 Profiles §4.3 gives OWL 2 RL/RDF as first-order implications over RDF
triples (tables 4–9), and Theorem PR1 states what they are complete for. RDF
1.1 Semantics §9.2 gives the RDFS entailment rules. Both are Datalog once
their infinite parts are bounded: the axiomatic triples over `rdf:_n` and the
blank nodes some rules would introduce. ADR 0195 maintains a closure; this ADR
says which closure.

## Decision

### 1. Two profiles

| `varve:profile` | Rules | Serves |
|---|---|---|
| `varve:RDFS` | point 2 | the views and reserved graphs (ADR 0198), explain, the clash checks |
| `varve:OWL2RL` | point 3 | the same, and `Varve-Entailment: owl2-rl` |

Under both, `rdf`, `rdfs` and `d` are still answered by tier 1's rewriting
(ADR 0193): its answer conditions (C1, C2, multiplicity) are the regime's, and
a closure read would answer them differently.

### 2. `varve:RDFS`: the RDFS entailment rules, bounded

- **The rules:** `rdf1`, `rdf2`, `rdfs2`–`rdfs13`, as RDF 1.1 Semantics §9.2.1
  gives them, with the recognised datatypes `xsd:string` and `rdf:langString`.
- **Axiomatic triples** are RDF and RDFS's, with `rdf:_n` restricted to the
  container membership properties that occur in the graph, as ADR 0193 does.
- **No rule invents a term.** The rules that introduce a blank node for a
  literal (RDF 1.1 Semantics' generalised `rdfD1`, `rdfs1`) are replaced by
  their generalised form over the literal itself, held for the joins and
  never served (ADR 0195 point 2).
- **The clash checks** are the RDFS and D inconsistencies RDF 1.1 adds: a
  literal ill-typed for a recognised datatype, and a literal in the range of a
  datatype whose value space does not hold it (for example `rdfs:range
  xsd:string` meeting a language-tagged object). They are rules whose head is
  `false`. The 10 `rdf-mt` cases that expect "false" are decided by them.

### 3. `varve:OWL2RL`: OWL 2 RL/RDF, with `sameAs` off

- **The rules:** every rule of OWL 2 Profiles §4.3 tables 4–9, each named
  `https://w3id.org/varve/ns#rule/owl2rl/{name}` (`cax-sco`, `prp-trp`, …),
  except the equality rules of table 4 under point 4.
- **The RDFS rules beneath them** are point 2's, so an `OWL2RL` closure
  contains the `RDFS` closure.
- **Datatype rules** (table 8): `dt-type1` and `dt-type2` for the datatypes
  Varve.Xsd recognises, and `dt-not-type` as a check. **`dt-eq` and `dt-diff`
  are not materialised.** Each concludes only facts whose subject is a
  literal, which are never served; with `sameAs` off, no rule substitutes
  through them, and the clash they could meet (`eq-diff1`) needs both on one
  pair of literals, which one value cannot give. `dt-diff` would also hold a
  fact for every pair of distinct literals, quadratic in the graph.
- **Rules with `false` heads** (`eq-diff1`–`3`, `prp-irp`, `prp-asyp`,
  `prp-pdw`, `prp-adp`, `prp-npa1`–`2`, `cls-nothing2`, `cls-com`,
  `cls-maxc1`, `cls-maxqc1`–`2`, `cax-dw`, `cax-adc`, `dt-not-type`) are
  checks, not derivations.
- **The compiled program.** The rules are specialised by the binding's TBox
  into join tables (for example `cax-sco` per class, `prp-spo1` per property),
  recompiled by a generation (ADR 0195 point 4), so a data commit joins
  against tables, not against the TBox.

### 4. `sameAs` off

- `varve:SameAsOff`, the default and the only value in effect in this part:
  `owl:sameAs` is an ordinary triple. The rules that conclude it
  (`prp-fp`, `prp-ifp`, `prp-key`, `cls-maxc2`, `cls-maxqc3`–`4`) fire and their
  conclusions are in the closure. The rules that substitute through it
  (`eq-ref`, `eq-sym`, `eq-trans`, `eq-rep-s`, `eq-rep-p`, `eq-rep-o`) do not.
- `varve:SameAsClasses` stays reserved, refused with a message naming part 3.
- The cases of `sparql11/entailment` and `rdf-mt` that need the eq-* rules are
  exempted by name, "until part 3's `sameAs` classes".

### 5. A clash is reported, never served as everything

An inconsistent graph entails every triple in OWL's semantics. The closure is
the rules' fixpoint all the same, as OWL 2 RL/RDF's partial axiomatisation
reads it (Theorem PR1 requires consistency for completeness, not for
soundness).
- **Each check that fires is a clash**: the rule, and the premises it matched.
- **The binding's description** names the graphs that hold clashes, behind
  admin (ADR 0194 point 8's resource), and **readiness** counts them per
  binding as `clashes`, naming no graph. Neither makes the dataset unready.
- **A query under `owl2-rl`** over a graph with a clash is answered from the
  fixpoint, and the response carries `Varve-Inconsistent: true`. Refusing
  commits that cause one is tier 3, in part 3.

### 6. `Varve-Entailment: owl2-rl`

- Maps to `ent:OWL-RDF-Based` with the profile `pr:RL`, and is answered by the
  closure (SPARQL 1.1 Entailment Regimes §6.4.5 allows the RL rules): each
  basic graph pattern is evaluated simply over the explicit quads and the rows
  valid at the read's position, with ADR 0193's C1 (no generalised triple, no
  invented blank node) applied.
- **A merged active graph** is answered by the union of its graphs' closures,
  plus whatever cross-graph closure the caller may read (ADR 0190). That is
  not the closure of the merge unless the binding is cross-graph; the service
  description's claim is per named graph, and the operator guide says so.
- **`entailment-unsupported`** answers `owl2-rl` on a graph no `OWL2RL`
  binding covers.
- **The service description** gives each named graph an `OWL2RL` binding
  covers `sd:entailmentRegime ent:OWL-RDF-Based` and
  `sd:supportedEntailmentProfile pr:RL`. `varve:defaultEntailment
  ent:OWL-RDF-Based` is in effect on an `OWL2RL` binding, and refused on any
  other.

### 7. The binding's terms in effect (ADR 0194, amended)

- `varve:profile varve:RDFS` and `varve:OWL2RL`: in effect.
- `varve:crossGraph`: in effect, with ADR 0190 point 6's placement. A
  cross-graph binding's rules are monotone here, since neither profile has
  negation.
- `varve:sameAs varve:SameAsOff`: in effect. `SameAsClasses`: reserved
  (part 3).
- `varve:rulesGraph`, `varve:RulesOnly`, `varve:inconsistencyMode`: still
  reserved, refused naming part 3.
- **Two materialising bindings over one graph** are refused, as ADR 0194
  point 2 already refuses two bindings over one graph.

### 8. The suites

- `sparql11/entailment`, OWL-RDF-Based with profile RL: the **28** cases on
  the closure subject, under the ratchet, the eq-* cases exempted by name.
- `rdf-mt`: the 10 "false" cases on the `RDFS` profile's clash checks.
- **The W3C OWL 2 RL test cases** are not vendored: their licence (D1) is not
  settled. They are part 3's `owl2-conformance` job, under ADR 0212, if it is.

## Alternatives considered

- **The `rdfs` regime from the closure** where an `RDFS` binding exists.
  Rejected: the closure has no C2 and counts no σ, so the same query would
  answer differently by whether a binding materialises.
- **Serve everything on a clash.** Correct in OWL's semantics and useless to a
  reader; the clash is reported instead.
- **`sameAs` on from the start.** The eq-rep rules multiply the closure by the
  size of each equivalence class; part 3's classes avoid that, and part 2
  does not pre-empt them.

## Consequences

- An `OWL2RL` binding's closure holds the `RDFS` closure and the RL
  conclusions; its size is measured on REC M (ADR 0196's workload).
- `Varve-Inconsistent` is a new response header, catalogued with ADR 0119's
  headers by amendment of 0192.

## Checks

- **Checked against the accepted ADRs** (0001–0194, 0206, 0213–0217, 0224,
  0225). Touches **0050**/**0051** (datatypes by value), **0192** and **0194**
  (amended), **0193** (C1 reused), **0195**. No conflict.
- **Layer ownership.** `Varve.Inference` (4): the rules, the checks, the
  compiled program. `Varve.Protocol` (5): the header and the service
  description.
- **Analyzer rule.** None new.
- **Open questions owned.** None.
