# Inference

A **TBox binding** gives a dataset's queries an ontology. You write the
ontology (the TBox: `rdfs:subClassOf`, `rdfs:subPropertyOf`, `rdfs:domain`,
`rdfs:range`) into one or more graphs of the dataset, then declare a binding
in the management plane. The binding names those graphs and the data graphs
they apply to. A query asks for a regime with the `Varve-Entailment` header,
or gets the dataset's default, and is answered as if every entailment of its
graph and the TBox were asserted.

This guide covers tier 1, the rewriting (#170): RDF, RDFS and D entailment,
answered without materialising anything; and tier 2, the materialised
closure of a binding whose profile is `varve:RDFS` or `varve:OWL2RL` (#171,
[below](#the-materialised-closure)). Custom rules and the commit-time
validator are #95's last part, and their terms are refused until then.

The decisions are ADRs 0190 (entailments under graph-level grants), 0191 (the
packages), 0192 (the regimes over HTTP), 0193 (RDFS, RDF and D by rewriting)
and 0194 (the binding's vocabulary). The rewriting is `Varve.Sparql.Entailment`.

## Declaring a binding

A binding is a node in the dataset's management graph,
`varve:manage/dataset/{name}`, beside the dataset's own declaration:

```trig
@prefix varve: <https://w3id.org/varve/ns#> .

<https://w3id.org/varve/ns#manage/dataset/zoo> {
  <https://example.org/manage/zoo> a varve:Dataset ;
      varve:datasetName "zoo" .

  <https://example.org/bindings/zoo> a varve:TBoxBinding ;
      varve:boundGraph <https://example.org/ontology> ;
      varve:profile varve:Rewriting ;
      varve:graphPrefix "https://example.org/sites/" ;
      varve:defaultEntailment <http://www.w3.org/ns/entailment/RDFS> .
}
```

Apply it with `varve apply`, or `PUT` it to `/manage/datasets/zoo`.

| Term | What it says | |
|---|---|---|
| `varve:boundGraph` | a TBox graph: a named graph of the same dataset, never `varve:defaultGraph` or a `varve:` graph | one or more |
| `varve:allGraphs true`, `varve:graph`, `varve:graphPrefix` | the data graphs, as a grant names them; `varve:defaultGraph` is the default graph | at least one |
| `varve:profile` | `varve:Rewriting` | exactly one |
| `varve:defaultEntailment` | the dataset's default regime: `ent:Simple`, `ent:RDF`, `ent:RDFS` or `ent:D` | `ent:Simple` |

- **The data graphs** never include the binding's own TBox graphs or any
  graph under `https://w3id.org/varve/ns#`.
- **One binding per graph.** Two bindings of a dataset whose graphs overlap
  are refused: by an explicit graph, by one prefix inside another, or by
  `varve:allGraphs`. A graph's TBox is never ambiguous.
- **One default per dataset.** The bindings of a dataset that state
  `varve:defaultEntailment` state the same one.
- **No templates.** A binding names no `varve:graphTemplate`: a template
  expands per caller, and a binding has no caller.
- **The materialising profiles.** `varve:profile varve:RDFS` and
  `varve:OWL2RL` are read (ADR 0197), with `varve:crossGraph true` and
  `varve:sameAs varve:SameAsOff` on them, and `varve:defaultEntailment
  ent:OWL-RDF-Based` on an `OWL2RL` binding only. Tier 1 still answers `rdf`,
  `rdfs` and `d` over a materialising binding's graphs. A cross-graph binding
  whose data graphs meet a classifier binding's is refused while a grant with
  subject terms reads them (ADR 0238 point 3).
- **Reserved.** `varve:RulesOnly`, `varve:rulesGraph`, `varve:SameAsClasses`
  and `varve:inconsistencyMode` are refused, each with the part of #95 that
  gives it effect. A declaration written for a later part is refused, not
  silently ignored.

## Who reads the TBox

**A TBox graph is read by every caller who reads anything in the dataset**
(ADR 0190). An answer under RDFS shows the axiom that drew it, so a TBox that
only some readers could see would either leak or make entailments vanish.
- A grant naming the TBox graph adds nothing to a reader's reach.
- Writing it still needs a write grant on it.
- A caller who reads nothing in the dataset reads none of it.

Applying a binding can therefore widen who reads a graph. `varve apply --plan`
says so before anything is applied: it lists, per dataset, every graph the
bindings make dataset-public, marking those the apply **widens** (public after
it, not before) and **narrows**. The saved plan's JSON carries the list as
`datasetPublic`, written only when the apply names one: a plan without is
format 1 as earlier releases wrote it.

```text
System position 41
Dataset-public graphs, read by every reader of their dataset:
  zoo <https://example.org/ontology> (widened: dataset-public after this apply)
<https://w3id.org/varve/ns#manage/dataset/zoo> at version 12:
+ …
```

## Asking for a regime

| `Varve-Entailment` | Regime |
|---|---|
| `simple` | the query as written |
| `rdf` | RDF entailment |
| `rdfs` | RDFS entailment |
| `d` | D entailment, with Varve.Xsd's datatypes: literals compared by value |

The value may also be the regime's IRI in angle brackets,
`<http://www.w3.org/ns/entailment/RDFS>`. Tokens are case-sensitive.
- **Without the header**, a query is answered under the dataset's
  `varve:defaultEntailment`, at the management position the read is served
  under; under simple entailment when no binding states one.
- **The response** names the regime applied in `Varve-Entailment`, varies on
  it, and is tagged per position and regime: `ETag: "41.rdfs"`. Under simple
  entailment the tag is the position alone, as before.
- **Anything else** (an unknown value, `owl2-rl` or `owl2-ql` before their
  parts, more than one value) is `400`
  [`entailment-unsupported`](../problems/entailment-unsupported.md), which
  lists the tokens served.
- **Updates** are evaluated under simple entailment. An update sent with any
  other value is `entailment-unsupported`; without the header, the dataset's
  default does not apply to it.
- **The Graph Store** ignores the header: a graph's representation is its
  asserted quads.

From the command line, `varve query <url> --entailment rdfs`; from
`Varve.Protocol.Client`, `QueryAsync(query, accept, asOf, "rdfs", …)`. A
local `varve query` over a dataset directory answers under simple entailment
only, since the bindings are a server's declarations.

## What a query sees

- **Each graph is read merged with its binding's TBox.** A basic graph
  pattern in a graph a binding covers is answered over that graph and the
  binding's TBox graphs together; the default graph likewise when the binding
  names it. Under `FROM`, the merged default graph is the `FROM` graphs and
  their bindings' TBoxes. Merging into a named graph under `GRAPH` is Varve's
  extension of SPARQL 1.1 Entailment Regimes §9.
- **A graph no binding covers** is read over its own triples: its TBox is
  empty, and schema triples it holds still act on it.
- **Schema triples in a data graph** act on that graph alone.
- **Answers are restricted** as the regimes' conditions require: no
  `rdf:_n` or literal binding a variable that the graph and the query do not
  mention, and blank nodes only from the graph. Multiplicity is the graph's.
- **Property paths** and `SERVICE` are evaluated without entailment (§10).
- **What the caller reads** bounds what it sees: the rewriting reads through
  the caller's scope, so no conclusion drawn from a graph the caller cannot
  read appears.

## Service description

The description names the dataset's default as `sd:defaultEntailmentRegime`
and says `sd:feature varve:EntailmentHeader`. Each `sd:NamedGraph` a binding
covers carries one `sd:entailmentRegime` per regime served for it.

## The materialised closure

A binding whose profile is `varve:RDFS` or `varve:OWL2RL` is **materialised**
(ADRs 0195–0198): each instance holding the dataset keeps the binding's
closure, every quad its rules entail, commit by commit, in memory. Tier 1
still answers `rdf`, `rdfs` and `d` over the same graphs.

**Where a conclusion is.** A conclusion drawn from the TBox graphs alone is in
the binding's reserved graph,
`https://w3id.org/varve/ns#inferred/{enc(binding IRI)}/tbox`, which every
reader of the dataset reads. Any other is in the data graph it was drawn in,
read by that graph's readers.

**Across graphs.** A binding with `varve:crossGraph true` closes the merge
of its data graphs (ADR 0190 point 6). A conclusion is placed once for each
smallest set of data graphs it can be drawn from: one graph's conclusion in
that graph, and one drawn from two or more graphs in the set's own reserved
graph, `https://w3id.org/varve/ns#inferred/{enc(binding IRI)}/{d}`, `d` the
hex SHA-256 of the set's graph IRIs. A caller reads such a graph only when it
reads every graph of its set, and only by naming it. Each data commit
recomputes the sets over the whole closure, so a cross-graph binding's
commits cost more than an intra-graph one's; readiness counts its reserved
graphs as `premiseSets`.

**Reading it.**
- `FROM <https://w3id.org/varve/ns#implicit>` reads the implicit part of the
  query's other `FROM` graphs, or of the default graph when there are none;
  `varve:explicit` the explicit part; both, the union. In `FROM NAMED`, each
  named graph is read in that part, and `GRAPH ?g` binds the real graph. A
  view counts as a `FROM`, and combines with `simple` and `owl2-rl` only.
- `GRAPH <…/inferred/…/tbox>`, `FROM NAMED` of it, or a Graph Store `GET`
  reads a binding's TBox conclusions.
- `Varve-Entailment: owl2-rl` reads every graph as explicit ∪ implicit, on a
  dataset with an `OWL2RL` binding. What it serves is OWL 2 RL **without the
  equality rules** (`varve:SameAsOff`, the only setting in this part), and
  the answer says where it is narrower than the request (ADR 0197
  points 4, 6 and 9):
  - **`Varve-Equality: not-applied`** on every `owl2-rl` answer: no
    `owl:sameAs` was substituted through, so an answer that needs it is
    missing.
  - **`Varve-Closure: per-graph`** when the default graph merges two or more
    graphs (`FROM g1 FROM g2`, or two `default-graph-uri`s) and the answer is
    each graph's closure, merged, not the closure of the merge: a conclusion
    drawn from facts of two graphs together is missing. When one binding
    with `varve:crossGraph true` covers every merged graph, the merge also
    reads that binding's premise-set graphs within it, is the closure of the
    merge, and carries no header; a merge that names a view, and an as-of
    read, are always per-graph.
  - **`Varve-Inconsistent: true`** when a graph it reads holds a clash.
  - **`Varve-Without-Closure`** when `GRAPH ?g` ranged over named graphs no
    `OWL2RL` binding covers: it lists them (at most twenty, then `+N more`),
    and their rows are explicit quads only.

  `SparqlHttpClient.EntailedAnswerOf(response)` reads all four. **A graph
  no `OWL2RL` binding covers**, named by `FROM`, `FROM NAMED`, the protocol
  or `GRAPH <g>`, is refused
  [`400 entailment-unsupported`](../problems/entailment-unsupported.md),
  naming it; so is a view naming a graph no materialising binding covers.
- **The service description** describes each graph an `OWL2RL` binding
  covers with `sd:entailmentRegime ent:OWL-RDF-Based`,
  `sd:supportedEntailmentProfile varve:OWL2RLWithoutEquality` and
  `varve:sameAs varve:SameAsOff`. It never claims `pr:RL`, the W3C profile,
  which includes the equality rules; part 3's `varve:SameAsClasses` will.
- A read that names none of these reads nothing of the closure and never
  waits for it. None of them is ever written (`graph-not-writable`).

**When it cannot answer.**
- A projection not at the read's position after a two-second wait:
  [`503 inference-behind`](../problems/inference-behind.md).
- A binding stopped by `Varve:Inference:ClosureBytes`, or because its
  projection failed:
  [`503 inference-unavailable`](../problems/inference-unavailable.md), with
  no `Retry-After`: `failure` `configuration` at the memory limit, `defect`
  when the projection failed. Nothing tries it again automatically: a
  restart, with the limit raised, or a change to the binding's declaration
  builds it again; a defect needs an upgrade first, and a report (the
  problem page says which act does what). A store that fails to
  answer stops nothing: the binding holds off and resumes where it was.
- A caller whose grant restricts rows over the binding's graphs:
  [`403 entailment-not-available`](../problems/entailment-not-available.md).

**History.** A read as of an older position is answered from the rows the
projection holds, up to `Varve:Inference:HistoryBytes`, and below them, or
below the position the instance started at, by computing the closure as of
that position, on every instance and after every restart. That costs a
closure from scratch; the [configuration](configure.md#inference) page has
the settings.

**On readiness**, each binding's `inference` line beside its dataset's gives
its `state` (`following`, `stopped (memory-limit)`, `stopped (failed)`), its
`lag`, `heldFrom`, `inconsistent` and `clashes`, `held` and `peak` (the bytes
its closures hold now and the most they have held, by the budget's count),
and for a cross-graph binding `premiseSets`. None of them makes the dataset unready.

## Cost

The rewriting runs once per query: each basic graph pattern becomes a union
of the asserted patterns that entail it, its tables built from the schema
the pattern meets. The schema is read by a few bound probes, never by a scan
of the data, and kept across queries until a graph it reads changes, so a
later query over the same graphs skips it. A pattern with a variable class
or predicate seeks the index once per distinct class or predicate. Neither
grows with the data: a query whose TBox is small costs about what its simple
form does (ADR 0193).

Two patterns are answered by every term of the graph and cost what their
answers do: `?s ?p ?o` and `?x a rdfs:Resource`.

### A triple about the vocabulary itself

A triple that says something of the RDF or RDFS vocabulary itself —
`rdf:type rdfs:subPropertyOf ex:p`, a domain on `rdfs:member` — makes any
triple a possible schema triple, so the schema of every graph it is part of
is read whole, and again after every commit to that graph (ADR 0193).

Where it is held decides what it costs, because a graph's schema is read
merged with its binding's TBox:
- **In a data graph** it acts in that graph alone, and only that graph pays.
- **In a TBox graph** it acts in every graph the binding covers, and each of
  them pays, after every commit to it.

So keep such triples out of a binding's TBox, and, where one graph needs
them, in that graph. Measured on a data graph of 10,000 people, a commit and
a query under RDFS take 7 ms with no such triple, 711 ms with one in the data
graph and 694 ms with one in the TBox; at 100,000 people, 7 ms, 8.9 s and
9.9 s (the benchmarks' README).

**Finding them.** A TBox binding's description,
`GET /manage/datasets/{name}/bindings/{binding}`, lists under `fullReads`
the graphs among its TBox and covered graphs that hold such a triple, the
default graph as `null`. `/health/ready` counts them per dataset as
`fullReads` without naming them, since it answers without credentials; the
count never makes the server unready.

A query under `GRAPH ?g` enumerates the dataset's named graphs once, to
group them by the TBox each meets.

### A cross-graph binding with property chains

A binding with `varve:crossGraph true` finds each fact's premise sets, so it
can place a fact two graphs derive together. That is paid on every build
from scratch: a cold start, a commit to a TBox graph, a restart after a stop.
Under `varve:OWL2RL`, a TBox with an `owl:propertyChainAxiom` or a transitive
property whose facts cross graphs currently costs **about four times as long
each time the number of graphs doubles** (ADR 0195 point 3, #354). Measured
on REC M with links between buildings, 200 per thousand commits, 4 cores:

| Graphs | Closure | Premise sets |
|---|---|---|
| 7 | 49 s | 163 s |
| 15 | 198 s | 686 s |
| 31 | 1,216 s | 3,239 s |

Without such links the same graphs take 38 s, 91 s and 236 s to close and 55
s, 141 s and 383 s to find the sets. Keep such a binding to **about 15
graphs**, a build of a quarter of an hour on this machine, or bind the
graphs the chain crosses in one binding and the rest per graph. A binding
under `varve:RDFS` is unaffected: no RDFS rule joins two data graphs.
