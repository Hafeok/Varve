# 0197 — OWL 2 RL/RDF and RDFS as rules: the materialising profiles

## Status

**Accepted — filed unaccepted by Inference, part 2 (#171, under #95),
2026-11-06**
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

The rules are RDF 1.1 Semantics' entailment patterns (W3C Recommendation, 25
February 2014), by the names it gives them:

- **`rdfD2`** (§8.1.1, *Patterns of RDF entailment*): `xxx aaa yyy` gives
  `aaa rdf:type rdf:Property`. RDF 1.1 has no `rdf1` or `rdf2`: those are
  the 2004 RDF Semantics' names, and 2004's `rdf2` (`rdf:XMLLiteral`) has no
  counterpart.
- **`rdfs1`–`rdfs13`**, `rdfs4a` and `rdfs4b` among them (§9.2.1, *Patterns
  of RDFS entailment*). `rdfs1` types each recognised datatype IRI as an
  `rdfs:Datatype`: an axiom per datatype, which invents nothing.
- **The recognised datatypes** under `varve:RDFS` are `xsd:string` and
  `rdf:langString`, which every RDF interpretation recognises (§8: an RDF
  interpretation recognising D is one where D includes both).
- **Axiomatic triples** are the RDF axiomatic triples (§8, in the body of
  *RDF Interpretations*) and the RDFS axiomatic triples (§9, in the body of
  *RDFS Interpretations*), with `rdf:_n` restricted to the container
  membership properties that occur in the graph, as ADR 0193 does.
- **No rule invents a term.** `rdfD1` (§8.1.1) concludes a blank node for a
  literal of a recognised datatype. It is replaced by Appendix A's
  generalised `GrdfD1` (*Entailment rules*, informative), `"sss"^^ddd
  rdf:type ddd` over the literal itself, held for the joins and never
  served (ADR 0195 point 2). The rule is named `rdfD1` in the closure, since
  it stands for that pattern.
- **The clash checks** are the D and RDFS unsatisfiability RDF 1.1 adds (§7.1,
  §9.2.1): a literal ill-typed for a recognised datatype (`dt-not-type`), a
  literal in the range of a datatype whose value space does not hold it (for
  example `rdfs:range xsd:string` meeting a language-tagged object), and a
  subclass between recognised datatypes whose value spaces do not nest
  (`dt-subclass`). They are rules whose head is `false`. The 10 `rdf-mt`
  cases that expect "false" are decided by them.

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
  admin (ADR 0194 point 8's resource), and **readiness reports the
  inconsistency** per binding: `inconsistent: true` and `clashes`, their
  count at the projection's position, naming no graph. Neither makes the
  dataset unready.
- **A query under `owl2-rl`** over a graph with a clash is answered from the
  fixpoint, and the response carries `Varve-Inconsistent: true`. Refusing
  commits that cause one is tier 3, in part 3.
- **Explain names the clash** (ADR 0199 point 2): an explain whose quad, or
  any premise of its proof, is in a graph with a clash the caller may see
  carries `clashes`, each the check that fired and a proof of each premise it
  matched.
- The header was accepted by the maintainer on 2026-11-16 (#171) on these two
  conditions: readiness reports the inconsistency, and explain names the
  clash.

### 6. `Varve-Entailment: owl2-rl`

- Maps to `ent:OWL-RDF-Based` with the profile `pr:RL`, and is answered by the
  closure (SPARQL 1.1 Entailment Regimes §6.4.5 allows the RL rules): each
  basic graph pattern is evaluated simply over the explicit quads and the rows
  valid at the read's position, with ADR 0193's C1 (no generalised triple, no
  invented blank node) applied.
- **A merged active graph** is answered by the union of its graphs' closures.
  Under one cross-graph binding (ADR 0190) that covers every merged graph, the
  merge also reads the premise-set graphs of every set within it, and is then
  the closure of the merge; otherwise it is not, and the answer says so with
  `Varve-Closure: per-graph` (amended 2026-11-19, §3).
- **`entailment-unsupported`** answers `owl2-rl` on a graph no `OWL2RL`
  binding covers, whether the dataset has such a binding or not, when the
  query names the graph (amended 2026-11-19, §3a).
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

## Amendment, 2026-11-19 — no name wider than what is served

Filed by Inference, part 2 (#171, under #95), unaccepted (ADR 0066), and accepted by the maintainer on 2026-11-20. The maintainer's ruling of 2026-11-19, on
the review's point 7: **a user must never expect one thing and get
another.** A name or a claim Varve exposes is never wider than what is
served, and where an answer is narrower than its request suggests, the
answer says so in-band. It amends points 4, 6 and 8, and ADR 0119 by its own
dated amendment.

### 1. The service description: `varve:OWL2RLWithoutEquality`, never `pr:RL` here

`pr:RL` (`http://www.w3.org/ns/owl-profile/RL`) names OWL 2 RL as OWL 2
Profiles §4.3 gives it, equality rules included. A binding with
`varve:sameAs varve:SameAsOff` applies every rule of tables 4–9 but
`eq-ref`, `eq-sym`, `eq-trans` and `eq-rep-s`/`-p`/`-o` (point 4), so it does
not serve `pr:RL`, and point 6's claim of it is withdrawn. Each graph an
`OWL2RL` binding covers, the default graph included when the binding does,
is described with:

- `sd:entailmentRegime ent:OWL-RDF-Based`;
- `sd:supportedEntailmentProfile varve:OWL2RLWithoutEquality`
  (`https://w3id.org/varve/ns#OWL2RLWithoutEquality`): OWL 2 RL/RDF without
  the equality rules, the rules this part applies;
- `varve:sameAs varve:SameAsOff`, the binding's own term (ADR 0194).

**Why `ent:OWL-RDF-Based` is not a wider claim.** The regime names the
semantics the answers are entailments under, and every answer the closure
gives is entailed under the OWL 2 RDF-Based semantics: the rules are sound
for it (OWL 2 Profiles, Theorem PR1 needs consistency for completeness, not
for soundness). Under that regime SPARQL 1.1 Entailment Regimes lets a
service answer by a profile's rules (§6.4.5, as point 6 cites) and say which
by the profile it names. So the regime claims what every answer is sound
for, and the profile names how much is computed. A reader who knows only W3C profiles finds none it
recognises, and assumes no completeness, which is the truth. The TBox graphs
are no binding's data graphs and carry none of these.

`pr:RL` is claimed only where the equality rules are in effect: part 3's
`varve:SameAsClasses`. The code is `ServiceDescriptionEndpoint.Owl2Rl`.

### 2. `Varve-Equality: not-applied`

Every response to a query answered under `owl2-rl` by a binding with
`varve:SameAsOff` carries **`Varve-Equality: not-applied`**, beside
`Varve-Inconsistent`. In this part every `OWL2RL` binding is one, so every
`owl2-rl` answer carries it. An answer that needs `owl:sameAs` substituted
through is missing from it; the header says so in-band, and the graph's
description says which rules apply. In part 3, a binding with
`varve:SameAsClasses` answers without it. It is catalogued in ADR 0119
(amended 2026-11-19), and `SparqlHttpClient.EntailedAnswerOf(response)` reads
it, with `Varve-Inconsistent` and `Varve-Closure`, into an `EntailedAnswer`.

### 3. A merge: the closure of the merge under one cross-graph binding, else `Varve-Closure: per-graph`

A default graph that merges two or more graphs (`FROM g1 FROM g2`, or two
`default-graph-uri`s) is answered under `owl2-rl` as follows. View IRIs are
not counted; one graph, the dataset's own default graph, and `GRAPH`
patterns are no merge.

**Included today.** Each merged graph's explicit quads and its implicit part.
And when **one** `OWL2RL` binding with `varve:crossGraph true` has every
merged graph among its data graphs (none of them a TBox graph), the merge
also reads that binding's premise-set graphs whose every graph is merged
(ADR 0190 point 6, ADR 0198 point 2). A conclusion is stored in the
premise-set graph of each of its minimal premise sets (ADR 0195 point 5), so
every conclusion whose premises lie in the merged graphs is in one of those
premise-set graphs or in a graph's own implicit part: the answer is the
closure of the merge, and carries no header. The premise-set graphs are read
under the same grant check as any graph: a caller reads one only when it
reads every graph of its set, which a caller merging those graphs does.

**Not included today, and said by `Varve-Closure: per-graph`:**
- a merge no single cross-graph binding covers: graphs of an intra-graph
  binding, graphs of two different bindings, or a graph of no binding (which
  `owl2-rl` refuses first, §3a). A conclusion that needs facts of two of the
  graphs together is missing;
- a merge that also names a view (`varve:explicit`, `varve:implicit`): the
  view decides which parts are read, and the premise-set graphs are not
  added to it;
- an as-of read (`Varve-As-Of`, ADR 0195 point 5): the closure below the
  history held is computed from scratch per graph, without premise sets.

So the header is now set exactly when the merge is not the closure of the
merge. ADR 0197 point 6's earlier "plus whatever cross-graph closure the
caller may read" is corrected to this paragraph: before this amendment the
premise-set graphs were read only when the query named them.

### 3a. A graph no binding covers: `entailment-unsupported`, or `Varve-Without-Closure`

`owl2-rl`, or a view (`varve:explicit`, `varve:implicit`, ADR 0198 point 1),
**naming** a graph that no binding gives a closure is refused `400
entailment-unsupported`, and the problem's `detail` names the graph. A graph
is named by `FROM`, `FROM NAMED`, `default-graph-uri`, `named-graph-uri` or
`GRAPH <g>`, in the query or a subquery. Under `owl2-rl` a graph is covered
when an `OWL2RL` binding has it as a data graph (not a TBox graph), or it is
that binding's reserved graph of TBox conclusions or one of its premise-set
graphs; a view needs the same of any materialising binding. Before this
amendment only a dataset with no `OWL2RL` binding at all was refused, and a
graph outside every binding was read with an empty implicit part.

`GRAPH ?g` names no graph, and is answered over the named graphs the query
reads, covered or not. When some of them have no closure, the answer says
which: **`Varve-Without-Closure`**, a comma-separated list of their IRIs in
angle brackets (`FROM NAMED`'s, or every named graph the caller reads when
the query names none), at most twenty, then `+N more`. Those graphs' rows
are their explicit quads only. The header is omitted when every graph read
has a closure. It lists only graphs the caller reads, so it shows nothing a
grant hides (ADR 0190).

### 4. Every other place where what is served is narrower than its name

For each place in ADRs 0195 to 0199 where an answer could be read as wider
than it is, this is how the user is told. Places where the narrowing is the
regime's own answer condition, and so no narrowing, are listed too, so the
list is complete.

| Where | What is served | How the user is told |
|---|---|---|
| 0197 point 4: `SameAsOff` | OWL 2 RL without `eq-*` | profile `varve:OWL2RLWithoutEquality`, `varve:sameAs varve:SameAsOff` (§1); `Varve-Equality: not-applied` (§2) |
| 0197 point 6: a merged default graph | the closure of the merge under one cross-graph binding covering it; otherwise the union of each graph's closure | `Varve-Closure: per-graph` exactly when it is not the closure of the merge (§3) |
| 0197 point 5: a graph with a clash | the rules' fixpoint, not everything an inconsistent graph entails | `Varve-Inconsistent: true`; readiness's `inconsistent` and `clashes`; explain's `clashes` |
| 0197 point 6: `owl2-rl` naming a graph no `OWL2RL` binding covers | nothing | `400 entailment-unsupported`, naming the graph (§3a) |
| 0198 point 1: a view naming a graph no materialising binding covers | nothing | `400 entailment-unsupported`, naming the graph (§3a) |
| 0197 point 6: `GRAPH ?g` over named graphs some of which no binding covers | those graphs' explicit quads | `Varve-Without-Closure` listing them (§3a) |
| 0197 point 8: the suites | `sparql11/entailment` under RL: **27 of 28** on the closure. `sparqldl-13` (its data, `data-08.ttl`, states `owl:sameAs`) waits for part 3's `sameAs` classes. `rdf-mt` needs no `eq-*` rule: its 38 rewriting cases and 10 "false" cases all run | the conformance table in `README.md` names the case pending, and the ratchet's runner prints `pending: owl:sameAs, part 3's sameAs classes (ADR 0197 point 4)` |
| 0197 point 3: `dt-eq`, `dt-diff` not materialised | no `owl:sameAs` or `owl:differentFrom` between literals | no narrowing: each would conclude a fact with a literal subject, which C1 never serves under the regime anyway (ADR 0193) |
| 0197 point 2: axiomatic triples bounded to the `rdf:_n` that occur, no invented blank node | the regime's own answer conditions | no narrowing: SPARQL 1.1 Entailment Regimes' C1 and C2 bound the answers the same way (ADR 0193) |
| 0197 point 1: `rdfs` from the rewriting even beside an `RDFS` binding | the RDFS regime's answer | no narrowing: the regime's conditions, C2 and the multiplicities, are kept |
| 0195 / ADR 0190 point 6: a cross-graph conclusion | in its premise-set graph, read only by a caller who reads every graph of its set, and only when named | the service description lists only graphs the caller reads; a caller who reads one graph of a set is not told the conclusion exists, by design, since that would show what the other graph holds (ADR 0190 point 2) |
| 0199 point 4: explain of a quad the caller does not see | `absent` | `absent` means "not entailed by the graphs you read", stated on the explain page; saying more would show what the caller cannot read (ADR 0190) |
| 0199 point 2, 0197 point 5: a clash in a graph the caller does not read | not named, `Varve-Inconsistent` not set | the same: the header speaks of the graphs the query read |
| 0195 point 6: a binding at its memory limit, failed, or with no pool | nothing | `503 inference-unavailable`, `reason`, readiness (refused, never an empty answer) |
| 0195 point 5: a read behind the projection | nothing | `503 inference-behind` with `Retry-After` (refused, never a stale answer) |
| 0195 point 5: an as-of read below the history held | the closure computed from scratch | no narrowing: the same answer, slower |
| 0196: the retraction strategy | the same closure | no narrowing: held to the from-scratch build by the property tests |

### The ledger

`NoClaimWiderThanServed`, `EqualityNotAppliedSaidInBand`,
`PerGraphClosureSaidInBand`, `MergeUnderOneCrossGraphBindingIsItsClosure`
and `UncoveredGraphRefusedOrSaid`, in this ADR's set. The tests are
`InferenceSurfaceTests`:
- `The_service_description_names_owl2_rl_without_equality_and_never_pr_RL`;
- `An_owl2_rl_answer_says_equality_was_not_applied`;
- `An_owl2_rl_answer_over_a_merge_says_it_was_answered_graph_by_graph`;
- `A_merge_under_a_cross_graph_binding_is_the_closure_of_the_merge`;
- `Owl2_rl_or_a_view_naming_a_graph_with_no_closure_is_entailment_unsupported`;
- `Graph_variable_over_a_mix_names_the_graphs_read_without_a_closure`.
