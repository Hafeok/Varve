# Inference

A **TBox binding** gives a dataset's queries an ontology. You write the
ontology (the TBox: `rdfs:subClassOf`, `rdfs:subPropertyOf`, `rdfs:domain`,
`rdfs:range`) into one or more graphs of the dataset, then declare a binding
in the management plane. The binding names those graphs and the data graphs
they apply to. A query asks for a regime with the `Varve-Entailment` header,
or gets the dataset's default, and is answered as if every entailment of its
graph and the TBox were asserted.

This guide covers tier 1, the rewriting (#170): RDF, RDFS and D entailment,
answered without materialising anything. The closure (OWL 2 RL, custom rules)
and the commit-time validator are later parts of #95, and their terms are
refused until then.

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
- **Reserved.** The materialising profiles `varve:RDFS`, `varve:OWL2RL` and
  `varve:RulesOnly`, and `varve:rulesGraph`, `varve:crossGraph`,
  `varve:sameAs` and `varve:inconsistencyMode`, are refused, each with the
  part of #95 that gives it effect. A declaration written for a later part is
  refused, not silently ignored.

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
`datasetPublic`.

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
a query under RDFS take 3.6 ms with no such triple, 815 ms with one in the
data graph and 793 ms with one in the TBox (the benchmarks' README).

**Finding them.** A TBox binding's description,
`GET /manage/datasets/{name}/bindings/{binding}`, lists under `fullReads`
the graphs among its TBox and covered graphs that hold such a triple, the
default graph as `null`. `/health/ready` counts them per dataset as
`fullReads` without naming them, since it answers without credentials; the
count never makes the server unready.

A query under `GRAPH ?g` enumerates the dataset's named graphs once, to
group them by the TBox each meets.
