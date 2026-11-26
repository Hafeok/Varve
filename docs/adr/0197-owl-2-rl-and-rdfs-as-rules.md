# 0197 — OWL 2 RL/RDF and RDFS as rules: the materialising profiles

## Status

**Accepted — filed unaccepted by Inference, part 2 (#171, under #95),
2026-11-06**
(ADR 0066). Decided by the maintainer on the plan in #95 (2026-10-27),
questions 3b, 3c and 5, with `sameAs` off in this part. Acceptance is the
maintainer's act on the pull request.

**Revised 2026-11-19**, before acceptance, on the maintainer's ruling of
2026-11-19 on the review's point 7, that a user must never expect one thing
and get another, and on the maintainer's answers of the same day on a graph
no binding covers and on a merge under a cross-graph binding. What is served
where it is narrower than its name is said in-band: points 4, 6 and 8, and
point 9, which lists every such place. These were filed on 2026-11-19 as an
amendment, accepted by the maintainer on 2026-11-20 with the rest of this
ADR, and are folded into the body here at the maintainer's request; no point
is withdrawn by a later one.

**Amended 2026-11-24** (ADR 0068), by the fix for #399: the list rules and
checks read a list only from asserted statements; see the end. Filed
unaccepted, and accepted by the maintainer on 2026-11-25.

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
- **The answer says so.** Every response to a query answered under `owl2-rl`
  by a binding with `varve:SameAsOff` carries **`Varve-Equality:
  not-applied`**, beside `Varve-Inconsistent`. In this part every `OWL2RL`
  binding is one, so every `owl2-rl` answer carries it: an answer that needs
  `owl:sameAs` substituted through is missing from it. A binding with part
  3's `varve:SameAsClasses` will answer without it. The graph's service
  description names the rules that apply (point 6).
- The cases of `sparql11/entailment` and `rdf-mt` that need the eq-* rules are
  exempted by name, "until part 3's `sameAs` classes" (point 8).

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

- **The regime.** `owl2-rl` maps to `ent:OWL-RDF-Based` and is answered by the
  closure (SPARQL 1.1 Entailment Regimes §6.4.5 allows a profile's rules):
  each basic graph pattern is evaluated simply over the explicit quads and the
  rows valid at the read's position, with ADR 0193's C1 (no generalised
  triple, no invented blank node) applied.
- **The service description** describes each graph an `OWL2RL` binding
  covers, the default graph included when the binding does, with:
  - `sd:entailmentRegime ent:OWL-RDF-Based`;
  - `sd:supportedEntailmentProfile varve:OWL2RLWithoutEquality`
    (`https://w3id.org/varve/ns#OWL2RLWithoutEquality`): OWL 2 RL/RDF without
    the equality rules, the rules a binding with `varve:SameAsOff` applies
    (point 4);
  - `varve:sameAs` with the binding's own value, `varve:SameAsOff` in this
    part (ADR 0194).

  **`pr:RL`** (`http://www.w3.org/ns/owl-profile/RL`) names OWL 2 RL as OWL 2
  Profiles §4.3 gives it, equality rules included. It is claimed only for a
  graph whose binding applies them, `varve:SameAsClasses`, which is part 3's;
  in this part no graph is described with it.

  **Why `ent:OWL-RDF-Based` is no wider claim.** The regime names the
  semantics the answers are entailments under, and every answer the closure
  gives is entailed under the OWL 2 RDF-Based semantics: the rules are sound
  for it (Theorem PR1 needs consistency for completeness, not for
  soundness). The profile names how much is computed. A reader who knows only
  W3C profiles finds none it recognises, and assumes no completeness, which
  is the truth. The TBox graphs are no binding's data graphs and carry none of
  these. `varve:defaultEntailment ent:OWL-RDF-Based` is in effect on an
  `OWL2RL` binding, and refused on any other.
- **A graph no binding covers.** `owl2-rl` **naming** a graph no `OWL2RL`
  binding covers is refused `400 entailment-unsupported`, and the problem's
  `detail` names the graph; so is a dataset with no `OWL2RL` binding at all.
  A graph is named by `FROM`, `FROM NAMED`, `default-graph-uri`,
  `named-graph-uri` or `GRAPH <g>`, in the query or a subquery. An `OWL2RL`
  binding covers its data graphs (not its TBox graphs), its reserved graph of
  TBox conclusions and its premise-set graphs. A view naming a graph no
  materialising binding covers is refused the same way (ADR 0198 point 1).
- **`GRAPH ?g`** names no graph, and is answered over the named graphs the
  query reads, covered or not: `FROM NAMED`'s, or every named graph the caller
  reads when the query names none. A graph with no closure is read with its
  explicit quads only, and the answer says which: **`Varve-Without-Closure`**,
  their IRIs in angle brackets separated by `, `, at most twenty, then
  `+N more`. It lists only graphs the caller reads, so it shows nothing a
  grant hides (ADR 0190), and is omitted when every graph read has a closure.
- **A merged default graph** (`FROM g1 FROM g2`, or two
  `default-graph-uri`s; view IRIs are not counted, and one graph, the
  dataset's own default graph and `GRAPH` patterns are no merge) reads each
  merged graph's explicit quads and implicit part.
  - **The closure of the merge** when **one** `OWL2RL` binding with
    `varve:crossGraph true` has every merged graph among its data graphs: the
    merge also reads that binding's premise-set graphs whose every graph is
    merged (ADR 0190 point 6, ADR 0198 point 2). A conclusion is stored in
    the premise-set graph of each of its minimal premise sets (ADR 0195
    point 5), so every conclusion whose premises lie in the merged graphs is
    in one of them or in a graph's own implicit part. The premise-set graphs
    pass the same grant check as any graph: a caller reads one only when it
    reads every graph of its set, which a caller merging those graphs does.
    The answer carries no header.
  - **Otherwise the union of each graph's closure**, and the answer carries
    **`Varve-Closure: per-graph`**: a conclusion that needs facts of two of
    the graphs together is missing. That is a merge no single cross-graph
    binding covers (graphs of an intra-graph binding, or of two bindings), a
    merge that also names a view (the view decides which parts are read, and
    the premise-set graphs are not added to it), and an as-of read
    (`Varve-As-Of`, ADR 0195 point 5), whose closure below the history held
    is computed per graph, without premise sets.
- **The headers** `Varve-Equality`, `Varve-Closure` and
  `Varve-Without-Closure` are catalogued in ADR 0119 (amended 2026-11-19),
  beside `Varve-Inconsistent` (point 5). `SparqlHttpClient.EntailedAnswerOf`
  reads all four into an `EntailedAnswer`.

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

- `sparql11/entailment`, OWL-RDF-Based with profile RL: **27 of its 28**
  cases run on the closure, under the ratchet. `sparqldl-13` (its data,
  `data-08.ttl`, states `owl:sameAs`) is exempted by name until part 3's
  `sameAs` classes: the conformance table in `README.md` names it pending,
  and the ratchet's runner prints `pending: owl:sameAs, part 3's sameAs
  classes (ADR 0197 point 4)`.
- `rdf-mt`: the 10 "false" cases on the `RDFS` profile's clash checks. It
  needs no `eq-*` rule: its 38 rewriting cases and 10 "false" cases all run.
- **The W3C OWL 2 RL test cases** are not vendored: their licence (D1) is not
  settled. They are part 3's `owl2-conformance` job, under ADR 0212, if it is.

### 9. No name wider than what is served

The maintainer's ruling of 2026-11-19: **a user must never expect one thing
and get another.** A name or a claim Varve exposes is never wider than what
is served, and where an answer is narrower than its request suggests, the
answer says so in-band. For each place in ADRs 0195 to 0199 where an answer
could be read as wider than it is, this is how the user is told. Places where
the narrowing is the regime's own answer condition, and so no narrowing, are
listed too, so the list is complete.

| Where | What is served | How the user is told |
|---|---|---|
| 0197 point 4: `SameAsOff` | OWL 2 RL without `eq-*` | profile `varve:OWL2RLWithoutEquality`, `varve:sameAs varve:SameAsOff` (point 6); `Varve-Equality: not-applied` (point 4) |
| 0197 point 6: a merged default graph | the closure of the merge under one cross-graph binding covering it; otherwise the union of each graph's closure | `Varve-Closure: per-graph` exactly when it is not the closure of the merge (point 6) |
| 0197 point 5: a graph with a clash | the rules' fixpoint, not everything an inconsistent graph entails | `Varve-Inconsistent: true`; readiness's `inconsistent` and `clashes`; explain's `clashes` |
| 0197 point 6: `owl2-rl` naming a graph no `OWL2RL` binding covers | nothing | `400 entailment-unsupported`, naming the graph (point 6) |
| 0198 point 1: a view naming a graph no materialising binding covers | nothing | `400 entailment-unsupported`, naming the graph (point 6) |
| 0197 point 6: `GRAPH ?g` over named graphs some of which no binding covers | those graphs' explicit quads | `Varve-Without-Closure` listing them (point 6) |
| 0197 point 8: the suites | `sparql11/entailment` under RL: **27 of 28** on the closure. `sparqldl-13` (its data, `data-08.ttl`, states `owl:sameAs`) waits for part 3's `sameAs` classes. `rdf-mt` needs no `eq-*` rule: its 38 rewriting cases and 10 "false" cases all run | the conformance table in `README.md` names the case pending, and the ratchet's runner prints `pending: owl:sameAs, part 3's sameAs classes (ADR 0197 point 4)` |
| 0197 point 3: `dt-eq`, `dt-diff` not materialised | no `owl:sameAs` or `owl:differentFrom` between literals | no narrowing: each would conclude a fact with a literal subject, which C1 never serves under the regime anyway (ADR 0193) |
| 0197 point 2: axiomatic triples bounded to the `rdf:_n` that occur, no invented blank node | the regime's own answer conditions | no narrowing: SPARQL 1.1 Entailment Regimes' C1 and C2 bound the answers the same way (ADR 0193) |
| 0197 point 1: `rdfs` from the rewriting even beside an `RDFS` binding | the RDFS regime's answer | no narrowing: the regime's conditions, C2 and the multiplicities, are kept |
| 0195 / ADR 0190 point 6: a cross-graph conclusion | in its premise-set graph, read only by a caller who reads every graph of its set: when named, or within a merge one cross-graph binding covers (point 6) | the service description lists only graphs the caller reads; a caller who reads one graph of a set is not told the conclusion exists, by design, since that would show what the other graph holds (ADR 0190 point 2) |
| 0199 point 4: explain of a quad the caller does not see | `absent` | `absent` means "not entailed by the graphs you read", stated on the explain page; saying more would show what the caller cannot read (ADR 0190) |
| 0199 point 2, 0197 point 5: a clash in a graph the caller does not read | not named, `Varve-Inconsistent` not set | the same: the header speaks of the graphs the query read |
| 0195 point 6: a binding at its memory limit, failed, or with no pool | nothing | `503 inference-unavailable`, `reason`, readiness (refused, never an empty answer) |
| 0195 point 5: a read behind the projection | nothing | `503 inference-behind` with `Retry-After` (refused, never a stale answer) |
| 0195 point 5: an as-of read below the history held | the closure computed from scratch | no narrowing: the same answer, slower |
| 0196: the retraction strategy | the same closure | no narrowing: held to the from-scratch build by the property tests |

The ledger holds it as `NoClaimWiderThanServed`, `EqualityNotAppliedSaidInBand`,
`PerGraphClosureSaidInBand` and `UncoveredGraphRefusedOrSaid`. The tests are
`InferenceSurfaceTests`:
- `The_service_description_names_owl2_rl_without_equality_and_never_pr_RL`;
- `An_owl2_rl_answer_says_equality_was_not_applied`;
- `An_owl2_rl_answer_over_a_merge_says_it_was_answered_graph_by_graph`;
- `A_merge_under_a_cross_graph_binding_is_the_closure_of_the_merge`;
- `Owl2_rl_or_a_view_naming_a_graph_with_no_closure_is_entailment_unsupported`;
- `Graph_variable_over_a_mix_names_the_graphs_read_without_a_closure`.

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
  headers by amendment of 0192; `Varve-Equality`, `Varve-Closure` and
  `Varve-Without-Closure` are catalogued by ADR 0119's amendment of
  2026-11-19.
- `varve:OWL2RLWithoutEquality` is a new term in the `varve:` vocabulary,
  the profile a graph under `varve:SameAsOff` is described with.

## Checks

- **Checked against the accepted ADRs** (0001–0194, 0206, 0213–0217, 0224,
  0225). Touches **0050**/**0051** (datatypes by value), **0192** and **0194**
  (amended), **0193** (C1 reused), **0195**. No conflict.
- **Layer ownership.** `Varve.Inference` (4): the rules, the checks, the
  compiled program. `Varve.Protocol` (5): the headers, the refusal and the
  service description. `Varve.Protocol.Client`: `EntailedAnswer`.
- **Analyzer rule.** None new.
- **Open questions owned.** None.

## Amendment, 2026-11-24 — a list is read from asserted statements

Filed unaccepted by the fix for #399, on the maintainer's decision of
2026-11-24 (fix 1, with its two conditions), and accepted by the maintainer on
2026-11-25. It amends point 3: the rules over
lists of OWL 2 Profiles §4.3 (`cls-int1`, `cls-int2`, `cls-uni`, `cls-oo`,
`scm-int`, `scm-uni`, `prp-spo2`, `prp-key`) and the checks over lists
(`eq-diff2`, `eq-diff3`, `prp-adp`, `cax-adc`). The ledger holds it as
`ListsReadFromAssertedStatements`, cited by `AssertedLists`.

**What was wrong.** Point 3 named the rules over lists and did not say how a
list is read. The code read one as **every path** of cells from the axiom's
object to `rdf:nil` along `rdf:rest`, through any fact the closure held,
inferred ones included, no cell twice. Each path was an instance, and the
paths were all built before any was evaluated. A graph whose closure gives
many nodes several `rdf:rest` values makes the number of paths grow
factorially. #399's seed (`eGI5Cz8BVtI1`) is 5 TBox and 80 data triples.
They make 72 nodes cells, each with 73 `rdf:first` and 73 `rdf:rest` values,
all inferred. `cls-oo` then held 2,097,152 paths of 72 cells (about 11 GB)
before the heap ran out. None of it was charged to the closure's budget
(ADR 0195 point 3). One triple, `ex:next rdfs:subPropertyOf rdf:rest`, is
enough to do the same to any graph with `ex:next` chains.

**The ruling.**
- **A cell** is a subject the asserted statements give **exactly one**
  `rdf:first` and **exactly one** `rdf:rest`. Those statements are the
  closure's explicit ones and, for a data graph, the TBox graphs' beneath it.
  A statement both assert is one statement.
- **A data graph neither breaks nor changes a TBox list.** A cell the TBox
  graphs make is read as they make it, under every data graph, because the
  TBox closure's conclusions from it are served to every reader (ADR 0195
  point 3). A data graph's own `rdf:first` or `rdf:rest` of such a cell that
  differs is not read, and is reported as `differs-from-tbox`. A cell the TBox
  graphs leave unread, for example one with no `rdf:rest`, a data graph may
  complete.
- **A list** is the one chain of cells from a head along their `rdf:rest` to
  `rdf:nil`, no cell twice. `rdf:nil` is the empty list. A rule's axiom whose
  object is no list has no instance.
- **An inferred `rdf:first` or `rdf:rest` neither makes nor breaks a list.**
  One the rules conclude about a cell is a fact like any other: served,
  explained and joined by the other rules. It is never read as list
  structure.
- **Every other subject of an asserted `rdf:first` or `rdf:rest` is unread,
  and reported with its reason:**
  - `several-first`;
  - `several-rest`;
  - `no-first`;
  - `no-rest`;
  - `cycle`: its chain comes back to a cell before `rdf:nil`;
  - `broken`: its chain reaches a node that is no cell before `rdf:nil`;
  - `differs-from-tbox`: a data graph asserts an `rdf:first` or `rdf:rest` of
    a TBox cell other than the TBox's.
- **Under a cross-graph binding** (ADR 0190 point 6), the merge reads its
  lists from the merge's asserted statements. One graph's statement can then
  make a list another graph asserts unread in the merge, for example a second
  `rdf:rest` on its cell. It is reported under each graph that asserts an
  `rdf:first` or `rdf:rest` of the cell.
  - A premise set is drawn from the merge's instances: a minimal set of
    graphs whose facts, with the lists the merge reads, entail the fact.
  - When a commit changes the lists the merge reads, the premise sets are
    labelled again from scratch, as the merge is rebuilt. An instance can come
    or go with no change to its premises, so an incremental update of the sets
    cannot see it (`Closure.ListReadings`).
- **A list is read only while its cells' statements are held.** It always is,
  since they are explicit, except while a rebuild inserts them again. So an
  instance is never counted before its premises are all present.

**Why the rules stay monotone.** The reading depends only on the closure's
explicit statements, which are its input, not on anything it derives. With the
input fixed, the reading is fixed, and each list rule is an ordinary rule over
derived facts, with the list's cells as constants: one instance per axiom and
list. Its premises are the axiom, the cells' two statements each, and what the
rule joins. So:
- **semi-naive insertion is complete:** a derived fact never adds or removes a
  list;
- **DRed is sound and complete for a retraction that leaves the reading
  alone:** over-deletion reaches every head whose instance used a retracted
  fact, and rederivation finds every head still supported. A cell's statements
  are explicit, so they are never over-deleted;
- **the from-scratch closure of the same explicit statements reads the same
  lists**, so incremental equals from scratch (ADR 0195 point 9) by the same
  argument as for every other rule.
  - For a data graph, "from scratch" is the data's closure over the TBox's,
    each built from scratch, as the projection builds them.
  - It equals the closure of the TBox's and the data's triples together
    exactly when the data reports no `differs-from-tbox`. With one, the union
    would break the TBox's list, which the TBox closure beneath cannot
    unsay.

**What a change to an asserted list does.** A commit that asserts or retracts
an `rdf:first` or `rdf:rest` statement reads that closure's lists again.
- When the cells read are the same, the commit is applied as any other. Only
  the unread report changes.
- When they differ, the closure's input to the list rules has changed, and the
  closure is **rebuilt from its explicit statements**: dropped and inserted
  again, in the same pass. Examples are a second `rdf:rest` on a cell, a
  retracted `rdf:first`, or a chain newly reaching `rdf:nil`.
  - What the pass reports as changed is still exact: a fact dropped and
    derived again is no change.
  - The cost is a from-scratch build of that one closure: a data graph's under
    an intra-graph binding, the merge's under a cross-graph one. A TBox
    commit already rebuilds both (ADR 0195 point 4).
- Adding a statement can remove a list, so a conclusion can go away when a
  triple is asserted. That is the reading's, and the rebuild makes it exact.

**Where it is reported.**
- **`Closure.UnreadLists()`** gives the unread cells and their reasons. A data
  graph's closure reports those its own statements make unread, beyond the
  TBox closure's.
- **The projection** places each under its graph:
  - the binding's TBox graph for the TBox closure's;
  - the data graph for an intra-graph binding;
  - each data graph asserting an `rdf:first` or `rdf:rest` of the cell, for a
    cross-graph binding's merge.
- **Readiness**, under each binding, gives `unreadLists`, the count, and
  `unreadListCells`, the first twenty, each with `graph`, `cell` and
  `reason`.
- **The operator's guide**, on the materialised closure, says that only
  well-formed asserted lists are read.

**Point 9.** This narrows what `owl2-rl` serves. A conclusion OWL 2 RL/RDF
would draw from an ill-formed or inferred list is not drawn. Point 9's table
gains the row: *0197 point 3, lists: what is served is the rules over
well-formed asserted lists; the user is told on readiness (`unreadLists`) and
on the profile's page.* An answer over such a graph carries no header of its
own.

**Open question for the maintainer, on cross-graph merges:** under the merge's
reading, a graph that reads one of its own lists by itself can lose it in the
merge to another graph's statement. A conclusion that graph alone entails is
then not placed in it. Should a merge instead read each list within one graph
(with the TBox), never across graphs? That reading is monotone in the graphs,
but a list whose cells span graphs is then never read.

**Open question for the maintainer, on point 9:** point 9 asks that an answer narrower
than its request says so in-band. Is the binding's status enough, or should an
`owl2-rl` answer over a graph with unread lists carry a header as well?

**What the byte budget bounds.** The byte budget bounds what a closure
**holds**, not how long it **works**. Reading lists from asserted statements
removes #399's unbounded allocation. It does not bound the rules' joins over a
graph that uses the vocabulary as data.

#399's other seed, `eGI5Cz8BVtI1`, reads no list after this amendment, yet its
commit 42 takes the closure from 1,275 to 388,741 facts. That is within any
default budget, and it runs for more than 20 minutes. It is #408's
reproduction: a closure's work is not bounded, readiness and explain block on
the pass, and the pass cannot be cancelled. Allocations that grow with the
input outside the budget are #406's.

**The property tests** are split, so that neither stands for the other:
- **The general property** keeps its sizes (40 histories of up to 60 commits,
  400 × 200 nightly). It draws no axiom whose subject or object is an RDF,
  RDFS or OWL term.
  - The OWL declarations (`rdf:type owl:TransitiveProperty`, `owl:Thing` as a
    restriction's class, `rdf:nil` ending a list) are the language's own use
    of its vocabulary, not the vocabulary as data, and stay.
- **A second property, the vocabulary as data**, draws vocabulary terms as
  the subjects and objects of schema axioms. Its universe is one individual,
  one class, one property, one restriction and one list cell. Its histories
  have up to 16 commits.
  - **Under `RDFS`** it draws RDF and RDFS terms as data: `rdf:type`, the
    four RDFS schema properties, `rdfs:Resource`, `rdfs:Class`,
    `rdf:Property`, `rdfs:member` and `rdf:_1`.
  - **Under `OWL2RL`** it draws OWL terms as data:
    `owl:TransitiveProperty`, `owl:SymmetricProperty`,
    `owl:equivalentClass`, `owl:inverseOf` and `owl:Thing`. It draws them as
    the subjects and objects of `rdfs:subClassOf`, `rdfs:subPropertyOf`,
    `rdfs:domain`, `rdfs:range` and the OWL pairs.
  - **Why not RDF and RDFS terms under `OWL2RL`:** there they join the
    profile's whole axiomatic vocabulary into one complete relation of 48
    terms. That is 110,592 facts (48³), and its closure took 39 s from
    scratch, with a history up to 200 s. That is #408's cost, not a bound a
    property test can hold.
  - The slowest sample of 50 runs is reported on the pull request.

**The suites.** `sparql11/entailment` and `rdf-mt` are the same before and
after:
- entailment: 122 of 122 case runs;
- `rdf-mt`: 48 of 48;
- 6,311 passing and 40 exempt in all, `Regressed: 0`.

Every list in their data is well formed and asserted.

**Tests.**
- `ListsFromAssertedStatementsTests` covers:
  - the reading, and each reason;
  - an inferred `rdf:rest` that neither makes nor breaks a list;
  - an asserted change that rebuilds the closure, equal to from scratch at
    each step;
  - the one-commit case `ex:next rdfs:subPropertyOf rdf:rest`.
- `IncrementalEqualsFromScratchTests.The_seed_of_399_holds_over_a_tbox_beneath`
  fixes the seed `akRsD4X_QWj2`, for DRed and recursive counting, with the
  generator that drew it. On `main` it runs out of memory for both, and with
  this amendment it holds in seconds.
- The one-commit case runs out of memory on `main` in 55 s and passes in
  under a second.
- `with_the_vocabulary_as_data_incremental_equals_from_scratch_at_every_commit`
  is the second property.
- `InferenceBindingTests` checks the report on readiness.


## Amendment, 2026-11-26 — lists read within one graph, a header for unread lists, and no rebuild for a list no axiom reads

Filed unaccepted (ADR 0066) by #413, on the maintainer's rulings of
2026-11-25, which answer the two open questions of the amendment of
2026-11-24 above. It changes `ListsReadFromAssertedStatements` once. The
code cites it in `AssertedLists`.

**What was wrong.** Measured at REC M, OWL 2 RL, 4 cores, heap limit 10 GiB,
on `main` at 65e8142b. After the build, these commits went into one data
graph: a plain commit; a commit asserting a well-formed three-cell `rdf:List`
that no axiom names; and then its retraction.

| Binding | Plain commit | Assert the list | Retract it |
|---|---|---|---|
| per graph | 0.5–17.9 ms | 3.7–5.2 s | 3.5–4.0 s |
| cross-graph | 22–24 ms | 53.0 s, then the binding stopped at its memory | — |

Each list commit rebuilt the graph's closure, or the merge, and labelled the
merge's premise sets again from scratch. Under a cross-graph binding at REC M,
any list a user asserted took inference down.

**The rulings.**

1. **A cross-graph merge reads each data graph's lists from that graph's
   statements and the TBox's alone, never across graphs.** The graph is the
   unit of grants.
   - One graph's statement never makes another graph's list unread.
   - Graphs that read one head differently each give it a list. A list rule
     or members check has an instance for each.
   - A list whose cells span graphs is unread where it starts, with a reason
     of its own, **`spans-graphs`**. That covers a cell its graph completes
     only with another graph's `rdf:first` or `rdf:rest`, and a chain that
     reaches a cell only another graph makes.
   - Each unread cell is reported under the graph that leaves it unread.
   - An intra-graph binding already read each graph alone, with the TBox, and
     is unchanged.
2. **An answer says how many unread cells it read.** An `owl2-rl` answer
   carries **`Varve-Unread-Lists: <n>`** (ADR 0119, amended 2026-11-26) when
   the binding's rules leave asserted list cells unread, as of the answer's
   position, in a graph the caller reads or in the TBox. There is none when
   n is 0. `SparqlHttpClient.EntailedAnswerOf` reads it as
   `EntailedAnswer.UnreadLists`.
   - **As of a position.** At the projection's position, the count comes from
     the snapshot (ADR 0195 point 6, amended 2026-11-25). Below it, it comes
     from the view's own `rdf:first` and `rdf:rest` statements, read as the
     closures there would read them (`InferenceProjection.UnreadListsAt`).
     That needs no closure, so a read from the held history and a read from
     scratch report the same.
   - `Varve-Inconsistent` still reports the clashes at the projection's
     position for every read. This amendment does not change it.
3. **A list no axiom reads rebuilds nothing.**
   - **When a closure rebuilds.** A closure rebuilds for its lists only when a
     head reaching a cell whose reading changed, under the old reading or the
     new, is the object of an axiom it holds before the delta. An axiom here is
     a fact, asserted or inferred, whose predicate a list rule or check reads a
     list by: `owl:intersectionOf`, `owl:unionOf`, `owl:oneOf`,
     `owl:propertyChainAxiom`, `owl:hasKey`, `owl:members` or
     `owl:distinctMembers`.
   - **Otherwise** the new reading replaces the old, and the delta is applied
     as any other, since no instance changes.
   - **An axiom that arrives after its list**, asserted or inferred, fires the
     rule over the reading the list already has. Every well-formed list is
     read, whether an axiom names it or not, and no instance was made under
     the old reading.
   - **An axiom retracted in the delta that changes its list** counts as held,
     and that closure rebuilds.

**The premise sets under a cross-graph binding.** An instance's cells are
drawn through **one graph that reads them all**: they give `{g}`, or nothing
for the TBox's reading. They are never drawn through the sets of the cell
statements as such. Another graph may assert the same statements and not read
them as that list, for example with a second `rdf:rest` of its own; its set
gives the instance nothing. A clash is placed the same way
(`PremiseSets.SetsOfInstance`).

When a change of reading does rebuild the merge, the sets are no longer
labelled from scratch. The cells of the lists an axiom names whose reading
changed are taken as retracted and asserted again in their graphs. Only
what was drawn through them is confirmed again or relabelled, as for any
other commit (`Closure.ReadListsPerGraph`, `ListChange`).

**Measured after**, the same commits on the same machine, three rounds each:

| Binding | Plain commit | Assert the list | Retract it |
|---|---|---|---|
| per graph | 0.3–15.0 ms | 1.2–1.7 ms | 7.4–17.2 ms |
| cross-graph | 0.6–25.8 ms | 2.0–9.8 ms | 23.3–32.8 ms; the binding stays up |

**Tests.**
- `ListsFromAssertedStatementsTests`:
  - a list no axiom names is asserted, broken and retracted with no rebuild,
    under each strategy, equal to the closure from scratch at each step. This
    test fails before this amendment;
  - an axiom asserted or inferred after its list gives the closure both
    together give.
- `ProjectionPropertyTests`:
  - a property over histories that make, break and spread lists across data
    graphs and the TBox. It holds a cross-graph binding's rows, at every
    position, to the closure of each set of graphs, each graph's lists read
    alone, placed by minimal sets. It fails when the merge reads the graphs'
    lists merged;
  - a conclusion that two graphs' readings both give moves to the other
    graph's when one breaks. It fails without the relabel from the changed
    cells.
- `ListsPerGraphTests`: a second `rdf:rest` from another graph leaves a
  graph's list read and its conclusion placed. A chain into another graph's
  cell is `spans-graphs`. Each cell is under its graph.
- `InferenceSurfaceTests`: `Varve-Unread-Lists` counts only graphs the
  caller reads. As of a position below the projection's it is read from the
  view, and before the cells were asserted it is absent. The client reads it.
