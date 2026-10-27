# 0190 — Entailments under graph-level grants

## Status

**Accepted — filed unaccepted by Inference (#95), 2026-10-27** (ADR 0066).
Decided by the maintainer on the plan in #95 (2026-10-27), decisions D2 and
the plan's question 1. Acceptance is the maintainer's act on the pull request.
It is filed in its own pull request, ahead of the milestone's three parts
(#170, #171 and #95's last part), which cite it as a decision set. **Amends
ADR [0107](0107-graph-level-authorisation.md)** by a dated amendment in that
file: the TBox and rules graphs of a binding are readable by every reader of
the dataset, and a graph may be governed by every graph of a set.

## Context

Inference adds quads that nobody wrote. Each one follows from premises, and
the premises live in graphs. ADR 0107 decides visibility per graph, by a
lookup of the graph handle (point 3), and it has one property: a caller
without read on G observes nothing of G through any endpoint. An entailment
can break that property in either direction:
- **It can leak.** If a conclusion is drawn from a graph the caller cannot
  read, the conclusion tells the caller something about that graph.
- **It can vanish.** If a conclusion is hidden because one of its premises is
  unreadable, a caller who could derive it from what it does read no longer
  sees it.

The ontology makes this acute. Almost every entailment has a TBox premise,
such as `rdfs:subClassOf`, `rdfs:domain` or a rule. If the TBox graph is
governed like any other graph, then every caller without read on it loses
every entailment, or learns the ontology through them.

The roadmap fixed the shape on 2026-10-12:
- inference within a graph by default;
- the ontology readable by all;
- cross-graph inference opt-in, visible only to callers who can read every
  premise graph.

This ADR states the rule exactly, says where each conclusion lives so that the
filter stays a lookup per graph handle, and names the property that tests it.

The three tiers meet it differently:
- **Tier 1** rewrites the query and evaluates it over the caller's
  `GraphScopedQuadSource`. It cannot reach what the caller cannot read.
- **Tier 2** materialises conclusions in a projection, so it needs placement.
- **Tier 3** reads the closure on the writer and answers with witnesses, so it
  needs the same placement.

## Decision

### 1. The rule

**An entailment is visible to a caller only if the caller can read every
graph its premises came from.** The rest of this section states the rule
exactly.
- **A derivation's premise set** is the set of graphs its premises came from,
  excluding the TBox and rules graphs of the binding, which every reader of
  the dataset reads (point 3).
- **A conclusion may have several derivations.** Its minimal premise sets are
  the premise sets of its derivations with no proper subset among them.
- **A conclusion is visible to a caller** exactly when one of its minimal
  premise sets is contained in the caller's readable set.

That gives both halves of the property (point 8): a caller sees no conclusion
it could not derive from what it reads, and it sees every conclusion it
could.

### 2. Intra-graph inference is the default

- **A conclusion is drawn from one data graph G's quads, plus the binding's
  TBox and rules graphs.** It lands in G.
  - Its only minimal premise set is {G}, so it is visible exactly to the
    callers who read G.
  - The read filter is ADR 0107's lookup of G's handle, unchanged.
- **The default graph** is a data graph like any other when the binding names
  it.
- **A conclusion drawn only from TBox and rules graphs** lands in a reserved
  graph of the binding, `https://w3id.org/varve/ns#inferred/{enc(binding)}/tbox`.
  - The schema's own closure is the typical case, such as transitive
    `rdfs:subClassOf`.
  - `enc` percent-encodes every byte outside RFC 3986's unreserved set, as the
    catalogue and report graphs do.
  - The graph is read under point 3's rule.
- **A TBox axiom asserted in a data graph is data of that graph.** It acts on
  G's closure and nowhere else, because G is its only premise graph.

### 3. A binding's TBox and rules graphs are readable by every reader of the dataset

- **Who reads them.** A graph that a TBox binding in force names as a TBox
  graph (`varve:boundGraph`) or as its rules graph (`varve:rulesGraph`) is
  readable by every caller whose readable set in the dataset is not empty.
  - A grant naming it adds nothing.
  - Writing it still needs a write grant on it.
  - A caller who reads nothing in the dataset reads none of them.
- **Why the rules graph too.** A conclusion shows the rule that drew it, just
  as it shows the axiom. Without this, every rule-drawn entailment would leak
  the rules graph or vanish, which is the reason the TBox needs the rule.
- **The bindings in force** are those at the management position the read is
  served under (ADR 0165), as for grants. A graph stops being dataset-public
  at the first management position where no binding names it.
- **Every surface follows.** That includes the query, the Graph Store `GET`,
  `GRAPH ?g` enumeration, the service description's graph list, the change
  feed and the diff (ADR 0107 point 5). A graph that one of these graphs
  governs (ADR 0107's governing seam, ADR 0144's catalogue graphs) follows it,
  by the governor rule as it stands.

### 4. The mechanism: a set of dataset-public graphs in the scope

- **`GraphScope` gains a set of dataset-public graphs.** A graph in the set
  is allowed exactly when the caller's readable set is not empty.
- **The host composes the set** through `IAccessScopes`, from the bindings at
  the management position, as it composes governors (ADR 0107's amendment of
  2026-10-15). `Varve.Rdf` knows only that such a set exists. The rule that
  fills it lives above it.
- **Its cost.** The decision is made once per distinct graph handle and
  memoised, like a prefix or a governor decision, so it is never per quad.
- **The `all` scope** is unchanged and still not wrapped (ADR 0107 point 4).

### 5. The widening is shown at apply

`varve apply --plan`, and the management API's plan, name every graph that
each binding in the desired declarations makes dataset-public: its TBox
graphs and its rules graph. The plan marks the graphs that are dataset-public
under the desired declarations and were not under the current ones.

A plan that widens who reads a graph therefore says so before it is applied.
A property holds it (point 8).

### 6. Cross-graph inference is opt-in, per binding

- **The switch.** A binding that declares `varve:crossGraph true` draws
  conclusions from the union of its data graphs, plus its TBox and rules
  graphs. Without it, point 2 holds.
- **Where a cross-graph conclusion lives.**
  - A conclusion whose minimal premise set S has two or more graphs lands in
    a reserved graph of the binding,
    `https://w3id.org/varve/ns#inferred/{enc(binding)}/{d}`.
  - `d` is the lower-case hex SHA-256 of the IRIs of S, sorted, each written
    in canonical N-Triples, one per line. The default graph is written as
    `varve:defaultGraph`.
  - There is one such graph per distinct premise set, not one per quad.
  - A conclusion with several minimal premise sets is in each of their
    graphs.
  - A conclusion with a minimal premise set of one graph is intra-graph, and
    lands in that graph by point 2.
- **How the premise set is recorded.** The projection keeps a table from each
  such graph to its set S. **A graph may be governed by every graph of a
  set**: ADR 0107's governing seam is generalised from one governor to a set.
  A governed graph is allowed exactly when every governor in its set is
  allowed, by the scope's own rules, prefixes and templates included.
  - The decision is made once per graph handle and memoised.
  - So the read filter stays a lookup per graph handle (ADR 0107 point 3),
    with no check of premises per quad.
  - Nothing per entailment is recorded except the graph it sits in.
- **How minimal premise sets are computed.** The closure is computed over
  pairs of a quad and a premise set.
  - A rule firing's premise set is the union of its premises' sets.
  - A pair is kept only when no kept pair for the same quad has a subset of
    its set. That is absorption, the positive Boolean provenance semiring
    over graphs.
  - The sets are subsets of the binding's data graphs, so the computation
    ends.
  - How many distinct premise sets a real binding produces is measured in
    #171 (REC M, a graph per building) and reported per binding on
    readiness. If the count grows faster than linearly in the bound graphs,
    #171's projection ADR says so before choosing how to store them.
- **Negation is refused across graphs.** A cross-graph binding whose rules
  have a negative dependency is refused at apply time, because negation
  breaks monotonicity: a caller who reads less could then derive more, so a
  conclusion drawn over every graph is not one a caller could derive from its
  part. Intra-graph bindings may use stratified negation, since each graph's
  closure is a function of that graph, the TBox and the rules alone. OWL 2
  RL/RDF has no negation. Its rules with `false` heads are constraints, not
  derivations.

### 7. What each tier needs

- **Tier 1 (rewriting) needs nothing more.**
  - It evaluates over the caller's `GraphScopedQuadSource`.
  - Its TBox is read through that same source, where point 3 makes it
    readable.
  - A `FROM`-merged active graph is entailment over graphs the caller reads,
    which point 1 allows (SPARQL 1.1 Entailment Regimes §9).
- **Tier 2 (the projection)** places each conclusion by points 2 and 6. The
  views, `varve:explicit` and `varve:implicit`, and explain read through the
  same scoped source.
  - Explain answers only for a quad the caller sees.
  - Every premise it names is then in a graph the caller reads, or in a TBox
    or rules graph.
- **Tier 3 (inconsistency at commit)** runs on the writer, under the writing
  caller's request. A data commit's outcome — refused or accepted, the
  problem body, and the attachment — depends only on graphs the writer reads.
  Its problem body names one witness per violation, as quads, and every
  witness is inside what the writer reads, in both modes:
  - **Intra-graph,** that holds already. A commit changes only the closures
    of the graphs it writes, and a write grant reads (ADR 0107 point 1). So a
    witness is inside a written graph plus the TBox and rules, which are
    dataset-public.
  - **Cross-graph, it holds by a precondition checked at apply time.** A
    cross-graph binding whose tier-3 mode is `reject` or `report` requires
    every write grant on any of its data graphs to carry read on all of its
    data graphs:
    - **What "carry read" means.** The grantee of that write grant must read
      every graph of the binding's graph set through its own grants, a write
      grant reading as ADR 0107 point 1 has it. The check is set inclusion
      over explicit graphs, prefixes and `allGraphs`. A dataset admin reads
      every graph.
    - **Templates.** A write grant by `varve:graphTemplate` counts as being on
      the binding's graphs whenever its expansion could fall among them,
      judged from the template's literal prefix. A read grant by template
      covers nothing, since what it covers depends on the caller.
    - **When it is checked:** when the binding is applied, and whenever
      grants change, by the management validator (ADR 0166 point 4) over the
      desired declarations of the fleet graph and the dataset's graph
      together.
    - **The refusal.** A declaration that would break the precondition is
      refused with `declaration-invalid`. Its finding names the binding and
      the grant.
  - **TBox and rules commits are not gated** (ADR 0169's R5, as Inference
    rules it). Writing them reveals nothing through a refusal, so the
    precondition is over the data graphs.
  - **`report`'s attachment** is the digest of the commit's new witnesses, so
    it is a statement about the binding's graphs. On the commits resource,
    the change feed and the diff, a cross-graph binding's attachment is
    delivered only to callers who read every graph of the binding, as ADR
    0174 rules for a report graph. To anyone else the commit carries no such
    attachment.

### 8. The property

Generate:
- datasets;
- grant sets: `all`, explicit lists, prefixes and templates, including a
  caller who reads nothing and one who reads only the default graph;
- TBoxes, with schema triples also inside data graphs;
- rule sets;
- bindings, intra-graph and cross-graph.

For every caller with readable set R, and for every surface:

| Surface | |
|---|---|
| the query | under each regime |
| `varve:implicit` | |
| the Graph Store | `GET` |
| `GRAPH ?g` | enumeration |
| the service description | |
| explain | |
| the views | as of a position |

the entailments the caller sees equal:
- **intra-graph:** the union over G ∈ R ∩ bound of closure(G ∪ TBox ∪
  rules);
- **cross-graph:** closure((R ∩ bound) ∪ TBox ∪ rules).

Two oracles hold the two halves:
- **No more:** ADR 0107 point 7's second property, extended, says that no IRI
  that occurs only in an unreadable graph reaches any response.
- **No less:** set equality with the union above, computed by a from-scratch
  closure over the sub-dataset of R's graphs.

A third property holds point 5: for generated desired and current
declarations, the plan names exactly the graphs whose dataset-public status
the apply would change.

A fourth holds point 7's tier 3, **no write's outcome changes with the
content of a graph the writer can't read**:
- **The pairs.** For generated pairs of datasets that differ only in graphs
  the writer does not read, under generated grants and bindings, intra-graph
  and cross-graph, in `reject` and in `report` mode.
- **What must be equal.** The same write has the same outcome over both:
  - accepted or refused;
  - the problem body, byte for byte;
  - the attachment the writer can see.
- **The generator also produces declarations that break the precondition.**
  Each is refused at apply with `declaration-invalid` naming the binding and
  the grant, so the pairs are never tested with a declaration the validator
  would not admit.

## Alternatives considered

- **Hide every entailment that has an unreadable premise, the TBox
  included.** Simple, and wrong in the vanishing direction: with an ontology
  graph readable by few, almost nothing is visible. The maintainer's ruling
  is that the TBox is readable by all.
- **Show every entailment to every reader of its conclusion's graph,
  whatever its premises.** This leaks: a conclusion drawn across graphs shows
  a reader of one what the other holds.
- **Put a cross-graph conclusion in its subject's graph, and check its
  premises per quad on read.** The filter would no longer be a lookup per
  graph handle. That is the cost ADR 0107 point 3 refuses, and it would put a
  provenance record beside every quad.
- **One reserved graph per binding for all cross-graph conclusions, filtered
  per quad.** The same per-quad check, in one place.
- **Cross-graph by default.** Every binding over several graphs would pay the
  premise-set computation, and its reserved graphs would appear without being
  asked for. The roadmap made it opt-in.
- **The TBox readable by every authenticated caller, not by readers of the
  dataset.** It would widen past the dataset. A caller with no grant in the
  dataset sees nothing in it, and that stays so.
- **Leave the rules graph under ordinary grants.** Rule-drawn conclusions
  would leak it or vanish, which is the TBox's argument again.
- **Refuse and withhold**, the plan's first wording. Under a cross-graph
  binding, a witness reaching a graph the writer cannot read would be named
  by its rule and a count of withheld quads, and the refusal would still
  stand. It leaks: a writer can probe a graph it cannot read by committing
  and watching whether the commit is refused, then varying the commit and
  repeating. The withheld count adds a measure of how much of the witness is
  hidden. The outcome of a write would then depend on content the writer
  cannot read, which is exactly what point 8's fourth property forbids. The
  precondition removes the case instead of masking it.
- **Allow negation across graphs and compute a closure per readable set.**
  Correct, but there is one closure per distinct readable set, a number the
  grants bound only by their combinations. Refused until a use needs it.

## Consequences

- **A cross-graph binding gated by tier 3 constrains the grants.** Every
  writer of its data graphs must read all of them. An operator who wants a
  writer confined to one graph either keeps the binding intra-graph or gives
  it no tier-3 mode. The refusal names the binding and the grant, so the
  conflict is visible at apply, and `docs/operator/inference.md` says so.
- **Readers of the dataset see its ontology and its rules.** An operator who
  binds a graph as a TBox makes it readable by every reader of the dataset.
  The plan says so at apply (point 5), and `docs/operator/inference.md` says
  so.
- **Two seams in `Varve.Rdf` grow:** the dataset-public set, and the governor
  generalised to a set. Both are additive to `GraphScope`'s construction, and
  `Varve.Rdf`'s baseline records them. They are built in #170 (the
  dataset-public set, which tier 1 needs) and #171 (the governed-by-a-set
  graphs, which only the projection makes).
- **Reserved graphs.** `varve:inferred/…` joins the reserved computed graphs.
  ADR 0198 (#171) records it in `docs/vocabulary.md`, and amends ADR 0130
  point 5 for the guard that refuses writing them.
- **The decision is per graph handle**, as ADR 0107's are, so a scoped read's
  cost is unchanged per quad. #171's benchmark adds a row for a scope with
  premise-set graphs.
- **#151** reworks `GraphScopedQuadSource`'s memo (`_decided`). The two new
  decisions join the same memo, and whichever lands second reconciles it.

## Checks

- **Checked against the accepted ADRs** (0001–0178) and specification 1.8.
  Touches:
  - **0107**: amended. Point 3's filter is unchanged in cost, gains two
    decisions, and the governor becomes a set;
  - **0144**: catalogue graphs governed as before;
  - **0163** and **0165**: the bindings at the management position decide;
  - **0166** and **0170**: the binding's graph-set terms;
  - **0169**: apply-time refusals that need the data dataset run above the
    store;
  - **0174**: a report graph's read rule is unchanged, and a TBox graph it
    validates is now readable by more callers;
  - **0017**: tier 3's witnesses are what the writer's validator reads;
  - **0166**: point 4's validator gains the precondition's refusal, by a
    dated amendment in the part that builds tier 3;
  - **0173**: a cross-graph binding's attachment is delivered as a report
    graph is read.

  No conflict.
- **Layer ownership.**
  - `Varve.Rdf` (1): the scope's two decisions.
  - `Varve.Management` (4): the plan's report of dataset-public graphs, and
    the cross-graph tier-3 precondition over grants and bindings.
  - `Varve.Inference` (4): premise sets and placement, through #171.
  - `Varve.Inference.Store` (5): the reserved graphs' table.
  - The server (6): the composition in `IAccessScopes`.
- **Analyzer rule.** None new. `VARVE0003` checks the two decisions' hot path,
  answered as ADR 0107's prefix decision is.
- **Open questions owned.** None. How many premise sets a cross-graph binding
  makes is a measurement owned by #171.
