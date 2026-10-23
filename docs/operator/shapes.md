# Shapes

A **shapes binding** puts a dataset's commits under SHACL. You write a shapes
graph into the dataset, then declare a binding in the management plane. The
binding names that graph, the graphs it validates, and what to do with a
commit that violates it. Every write path is gated: SPARQL Update, the Graph
Store and bulk loads. Each binding's report at every position is kept by a
**validation projection** and served as a graph you can query.

The decisions are ADRs 0169 (commit-time gating), 0170 (the vocabulary), 0171
(which shapes can be validated incrementally), 0173 (the report on the
commit), 0174 (the projection), 0175 (shapes for Varve's own declarations) and
0176 (the REC gate). The validator is `Varve.Shacl`: SHACL 1.2 Core and
SHACL-SPARQL (ADRs 0148–0157).

## Declaring a binding

A binding is a node in the dataset's management graph,
`varve:manage/dataset/{name}`, beside the dataset's own declaration:

```trig
@prefix varve: <https://w3id.org/varve/ns#> .

<https://w3id.org/varve/ns#manage/dataset/people> {
  <https://example.org/manage/people> a varve:Dataset ;
      varve:datasetName "people" .

  <https://example.org/bindings/people> a varve:ShapesBinding ;
      varve:boundGraph <https://example.org/shapes> ;
      varve:graphPrefix "https://example.org/people/" ;
      varve:validationMode varve:Reject .
}
```

Apply it with `varve apply`, or `PUT` it to `/manage/datasets/people`.

| Term | What it says | |
|---|---|---|
| `varve:boundGraph` | the shapes graph: one named graph of the same dataset | required |
| `varve:allGraphs true`, `varve:graph`, `varve:graphPrefix` | the graphs it validates, as a grant names them; `varve:defaultGraph` is the default graph | at least one |
| `varve:validationMode` | `varve:Reject`, `varve:Report` or `varve:Off` | required |
| `varve:allowFullEvaluation` | lets a `reject` binding have shapes classed full, at their cost | `false` |
| `varve:subClassOfInShapesGraph` | also read `rdfs:subClassOf` from the shapes graph (SHACL 1.2 Core §6.4) | `false` |

- **One data graph.** The graphs a binding validates are validated together,
  as one data graph. That union never includes the shapes graph or any graph
  under `https://w3id.org/varve/ns#`.
- **No templates.** A binding names no `varve:graphTemplate`. A template
  expands per caller, and a binding has no caller.

## What each mode does

- **`reject`** refuses a commit whose affected focus nodes gain a violation
  the position before did not have, with `422`
  [`shapes-violated`](../problems/shapes-violated.md). Violations never grow.
  Existing violators are sticky: a commit that touches one must fix it.
- **`report`** accepts the commit.
- **`off`** records the binding and does nothing.

Under `reject` and `report`, every accepted commit carries one attachment per
binding: `<<( binding varve:affectedReport "sha256:…" )>>`. It is the digest
of the report restricted to the commit's affected focus nodes. Restricting the
projection's report at the same position gives the same bytes.

A commit that changes the shapes graph is never refused because of existing
data. It is refused only when it makes the shapes themselves unacceptable:
[`shapes-invalid`](../problems/shapes-invalid.md), or
[`binding-not-incremental`](../problems/binding-not-incremental.md) under
`reject` without `varve:allowFullEvaluation`.

## Which shapes can be checked per commit

Only the focus nodes a commit's delta affects are validated (ADR 0171). Every
shape is classed from the shapes graph alone:
- **incremental**, found by a bounded rule per changed triple;
- **scoped**, found by a closure whose size depends on the data: subclass
  changes, `*` and `+` paths, lists, analysable SPARQL;
- **full**, which re-validates every target on every change.

A `reject` binding over full shapes is refused when you apply it, unless it
says `varve:allowFullEvaluation true`. **That flag's cost is a full
validation of the binding's targets on every commit.**

`GET /manage/datasets/{name}/bindings/{enc(binding)}` describes a binding:
- its declaration;
- each shape's class, with the construct and the section that decided it;
- any problem with its shapes graph.

## The report graph

Each binding's report is served as the graph
`https://w3id.org/varve/ns#validation/{enc(binding IRI)}`, as of any position.
`enc` percent-encodes every byte outside RFC 3986's unreserved set. Query it,
or `GET` it from the Graph Store:

```sparql
PREFIX sh: <http://www.w3.org/ns/shacl#>
SELECT ?focus ?path ?message WHERE {
  GRAPH <https://w3id.org/varve/ns#validation/https%3A%2F%2Fexample.org%2Fbindings%2Fpeople> {
    ?result sh:focusNode ?focus ; sh:resultMessage ?message .
    OPTIONAL { ?result sh:resultPath ?path }
  }
}
```

- **It is computed, never stored.** Nothing is in the log. A write to any
  graph under `…#validation/` is `403`
  [`graph-not-writable`](../problems/graph-not-writable.md), an
  administrator's included.
- **Who reads it:** a caller who reads every graph the binding validates. A
  report names those graphs' nodes and values. For anyone else the graph does
  not exist.
- **Its bytes are fixed:** the report at a position is the same canonical
  N-Quads as a full validation of that position (ADR 0154). The Graph Store
  tags it with the position read.
- **`503 validation-behind`.** A read that needs the report waits up to two
  seconds for the projection to reach its position, then answers `503`
  [`validation-behind`](../problems/validation-behind.md) with
  `Retry-After: 1`. A read of the data graphs alone never waits.

## The projection: lag, cold start and cost

- **Lag** is reported per binding on `/health/ready`, under the dataset's line
  as `validation`: how many commits it is behind the head. It never makes the
  dataset or the server unready.
- **Cold start.** Every instance keeps its own projections and never shares
  them. On a cold start each projection begins at the head with one full
  validation there. That full validation is the rebuild time the benchmarks
  report.
  - **Below that position** nothing is held. A read of the report there is a
    full validation as of the position, computed when read.
  - **A file dataset** saves its projections' state in its own `derived/`
    every 1,000 commits and on stop, and a warm restart reads it back.
  - **Memory and object-storage datasets** keep it in memory.
- **A shapes change** is one full validation at the commit that makes it, then
  incremental again.
- **Memory.** Rows are kept for every position the projection has seen. A
  binding whose results change often grows them.

## REC and Brick

RealEstateCore 4 (`rec.ttl`, `Brick+patches.ttl`, `recimports.ttl`) needs two
additions before it validates as SHACL says (ADR 0176). Add both to your
shapes graph, and bind with `varve:subClassOfInShapesGraph true`:

1. **`owl:Class rdfs:subClassOf rdfs:Class .`** The 1,445 Brick shapes typed
   `owl:Class` and `sh:NodeShape` are implicit class targets only when
   `owl:Class` is an `rdfs:Class`.
2. **The prefix declarations** Brick's three SPARQL constraints name by
   `sh:prefixes` and no file declares. Without them the shapes graph is
   `shapes-invalid`.

`testdata/rec/gate/composition.ttl` in the repository has both, exactly.
Brick's SHACL-AF rules (`sh:rule`) are inference, which this milestone does
not evaluate. A validation reads them as triples and nothing more.
`testdata/rec/gate/features.md` lists every SHACL feature REC uses and its
class.
