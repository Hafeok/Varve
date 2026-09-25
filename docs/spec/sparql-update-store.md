# SPARQL Update over the store

Functional specification for `Varve.Sparql.Store` (layer 5): executing a
SPARQL 1.1 Update request against a `Varve.Store` dataset, as one commit.

Status: Accepted. Changes only together with the ADR that motivates the
change. Decisions: [ADR 0057](../adr/0057-sparql-update-one-request-one-commit.md)
(the execution model) and [ADR 0058](../adr/0058-staging-view-and-dataset-validators.md)
(what the store provides for it).

## 1. Normative references

- **SPARQL 1.1 Update**, W3C Recommendation, 21 March 2013. Section numbers
  below are its own.
- `log-and-projection-model.md` — T1, R1, R4, I2, I3, I4 — which is the
  authority for what a commit is.
- `sparql-algebra.md` §5, the update operations as the parser records them;
  `sparql-evaluation.md` for how a `WHERE` is evaluated.

## 2. The entry point

```csharp
CommitResult result = await SparqlUpdate.ExecuteAsync(dataset, update, options, cancellationToken);
```

- **`dataset`** is a `Varve.Store.Dataset`; **`update`** is a parsed
  `Varve.Sparql.Algebra.Update`. The package pins, evaluates and commits; the
  caller holds nothing.
- **`UpdateOptions`** is immutable and has four members, none with an
  ambient default (ADR 0056's rule):
  - `Evaluation` — the `EvaluationOptions` every `WHERE` is evaluated with,
    and templates too: its clock and random source serve `NOW()`, `RAND()`,
    `UUID()` and `STRUUID()`, and its `SERVICE` handler serves `SERVICE`.
  - `Metadata` — the commit's agent, cause and graph scope, passed through.
  - `LoadSource` — the `ILoadSource` `LOAD` reads through (§6.3). The default
    refuses every IRI.
  - `ConflictRetries` — how many times a request that met a `Conflict` is
    executed again from a fresh pin. Default **0**: a `Conflict` is returned,
    not retried (§4).
- **Validators come from the dataset** (`DatasetOptions.Validators`, ADR
  0058), and run as part of the commit as always (T1 step 5).
- **The result is the commit's**: `Committed(P)`, `NoChange(head)`,
  `Conflict(head)`, `Rejected(report)` or `Unavailable`. An operation that
  fails — §5 lists when — throws `SparqlUpdateException` naming the operation
  by its index and kind, and nothing is committed (§3).
- **Cancellation** stops the request at the next operation boundary or inside
  an evaluation (the evaluator's contract), with nothing committed.

## 3. One request, one commit

Update §2.2: a request "SHOULD be treated atomically … either no effect or a
complete effect"; its operations are executed "in a fashion that guarantees
the same effects as executing them sequentially in the order they appear";
and "a result of failure from any operation MUST abort the sequence". ADR
0005 makes the request one commit. Execution:

1. **Pin** the readable head `P` (R1) and open a **staging view** over the pin
   (ADR 0058): a quad source that reads `G_P`, and that can give a handle to a
   term `D_P` does not hold — a new IRI, a new literal, a fresh blank node —
   without touching the dictionary.
2. **For each operation `k`, in order**, evaluate it against
   `S_k = Overlay(staging, δ₁ ; … ; δₖ₋₁)` (R4, ADR 0017's overlay) and
   compute its delta `δₖ`, exact against `S_k`: it retracts only quads in
   `S_k` and asserts only quads not in it (§5 per operation). `δ₁ ; … ; δₖ` is
   a chain of exact deltas, over which `;` is associative (§6 of the model,
   ADR 0047), so the composed delta is exact against `G_P`.
3. **Submit** the composed delta `Δ` as one commit with
   `expectedPosition = P`: each retraction and assertion of `Δ` becomes an
   operation of one `CommitRequest`, a term the pin holds as
   `RequestTerm.Existing(handle)` and a staged term as a request term the
   sequencer resolves (ADR 0058's mapping). The sequencer normalises against
   `G_head`, which is `G_P` or a `Conflict`, so the committed delta is `Δ`.
4. **Release** the pin, whatever happened.

**Consequences, which the tests assert.**

- A request whose net effect is empty — deleting absent data, clearing an
  empty graph, inserting then deleting — makes **no commit** and returns
  `NoChange` (I4, ADR 0010). A request with a non-empty net effect makes
  **exactly one**. None makes two.
- A failing operation leaves no trace: nothing was submitted, and the staging
  view's terms never reached the dictionary (I3).
- Later operations see earlier ones' effects, including terms the earlier ones
  created, because they are evaluated over the overlay of the staging view.

## 4. Concurrency

`Conflict(head)` from the sequencer means another writer committed between
the pin and the submit (ADR 0011). **The request is not retried by default**:
whether re-running it against the new head is still what the caller meant is
the caller's decision, and `ConflictRetries` is how the caller states it.
Each retry starts again at §3 step 1 — a fresh pin, a fresh evaluation — and
the result after the last attempt is returned. There is no lock; a caller
never holds anything across the decision.

## 5. The operations

`S` is the operation's source (`S_k` above), and "retract `X`" means "retract
the quads of `X` that are in `S`", "assert `X`" means "assert the quads of `X`
that are not in `S` or are retracted by this operation" — so that every
operation's delta is exact against `S`.

### 5.1 `INSERT DATA` — §3.1.1

Assert the quads. A blank node in the data is a fresh node: one per distinct
label per request (§3.1.1 with the grammar's label scoping; T1 step 2, ADR
0044), so `_:b` in two operations of one request — which the grammar forbids
anyway — would not join.

### 5.2 `DELETE DATA` — §3.1.2

Retract the quads. Blank nodes are refused by the parser (`sparql-grammar.md`
§4, `syntax-update-bad-*`), confirmed by the syntax suite: a blank node in
`DELETE DATA`, `DELETE WHERE` or a `DELETE` template never parses, so the
executor never sees one.

### 5.3 `DELETE`/`INSERT … WHERE` and `DELETE WHERE` — §3.1.3

1. **The dataset of the `WHERE`**: with `USING` or `USING NAMED`, the
   `DatasetSpec` it records, exactly as `FROM`/`FROM NAMED` for a query
   (`sparql-evaluation.md` §6.11, SPARQL 1.1 §13.2). Otherwise, with
   `WITH <g>`, the source with `<g>` as its default graph and its named
   graphs as they are — a view, not a copy. Otherwise, `S` itself. `USING`
   takes precedence over `WITH` for the `WHERE` (§3.1.3: "in the presence of
   one or more graphs referred to in USING clauses and/or USING NAMED
   clauses, the WITH clause will be ignored while evaluating the WHERE
   clause"). §3.1.3 describes `WITH <g>` as "an RDF Dataset containing a
   default graph with the specified name"; §4.5's formal mapping wraps the
   `WHERE` in `GRAPH <g>` instead. The two agree except when `<g>` does not
   exist and the pattern matches no quad — `WITH <g> INSERT { … } WHERE {}`
   — where the wrapping yields no solution and the dataset one. A graph that
   does not exist is an empty graph in this store (§5.5), so the dataset
   reading is the one followed, and the named graphs stay the store's, as
   under the wrapping.
2. **Evaluate** the `WHERE` with the evaluator, over that dataset, to
   solutions; every variable the templates mention is projected.
3. **Instantiate** each template per solution: a variable takes its binding;
   a blank node in the `INSERT` template is fresh per solution (§3.1.3, "a
   new blank node … for each solution"); a quad pattern with no `GRAPH` goes
   to `<g>` under `WITH <g>` and to the default graph otherwise. A quad with an
   unbound variable, or with a literal or a triple term as subject, a non-IRI
   predicate, or a non-IRI graph name, is skipped (§3.1.3: "if any solution
   produces a triple containing an unbound variable or an illegal RDF
   construct … then that triple is not included"). A blank node bound from
   the store is that node, by handle; a blank node the evaluator minted
   (`BNODE()`) is a fresh node per distinct node in the evaluation.
4. **The delta**: retract the instantiated `DELETE` quads, then assert the
   instantiated `INSERT` quads — §3.1.3's "the deletion … before the
   insertion" — both against `S`, both from the same solutions.

`DELETE WHERE { Q }` is `DELETE { Q } WHERE { Q }` (§3.1.3.3).

### 5.4 `LOAD` — §3.1.4

Resolve the source IRI through `LoadSource` to a document and a syntax; parse
it with `Varve.Turtle` (N-Triples, N-Quads, Turtle, TriG); assert its triples
into the default graph, or into `INTO GRAPH <g>`. A quad document's named
graphs are kept as they are without `INTO`, and with `INTO` every quad goes to
`<g>` — SPARQL 1.1 Update speaks only of graphs, and this is Oxigraph's
behaviour, the tie-breaker. Each document's blank nodes are fresh nodes, one
per label per load. **Failure** — the source refuses, the document does not
parse — fails the operation; **with `SILENT` the operation has no effect and
the request continues** (§3.1.4: "the operation will still return success").
The HTTP load source is the server's (milestone 7); the package ships only
the contract and the refusing default.

### 5.5 Graph management — §3.2

**The store represents no empty graphs: a named graph exists if and only if
it holds a quad** (ADR 0041; the log records quads, ADR 0010). §3.2 allows
this: "Stores that do not record empty graphs will always return success"
(§3.1.5, and §3.2.2 for `DROP`), and "stores that do not record empty named
graphs will always return success on creation of a non-existing graph"
(§3.2.1). So:

| Operation | Effect | Fails without `SILENT` |
|---|---|---|
| `CREATE GRAPH <g>` — §3.2.1 | none | when `<g>` holds a quad: it exists, and §3.2.1 says a failure SHOULD be returned |
| `CLEAR` — §3.1.5 — and `DROP` — §3.2.2 — of `GRAPH <g>`, `DEFAULT`, `NAMED` or `ALL` | retract every quad of the target | never: a graph that does not exist is an empty one |
| `ADD <a> TO <b>` — §3.2.5 | assert `<a>`'s triples into `<b>` | never |
| `COPY <a> TO <b>` — §3.2.3 | retract `<b>`'s triples, assert `<a>`'s | never |
| `MOVE <a> TO <b>` — §3.2.4 | `COPY`, then retract `<a>`'s | never |

`ADD`, `COPY` and `MOVE` with the same source and target do nothing (§3.2.3–
§3.2.5). "The input graph does not exist" is a *MAY* fail (§3.2.3–§3.2.5);
here a graph with no quads is an empty graph, so nothing fails, and `SILENT`
changes nothing for these operations. `CLEAR` and `DROP` differ only in
whether an empty graph remains, which this store cannot tell apart.

## 6. Contracts

### 6.1 `SparqlUpdate`

The entry point of §2, and the only way in.

### 6.2 `UpdateOptions`

As §2.

### 6.3 `ILoadSource`

```csharp
public interface ILoadSource
{
    ValueTask<LoadedDocument> LoadAsync(RdfTerm iri, CancellationToken cancellationToken);
}
```

`LoadedDocument` is the bytes, the `RdfSyntax` to parse them as, and the
base IRI to resolve against (the document's own IRI unless the source says
otherwise); a failure is a `LoadedDocument` that carries a message, or an
exception, either of which fails the operation. `file:` IRIs are handled by
no source this package ships; the conformance harness serves them from the
suite directory, converting the IRI to a path through `Varve.Iri` —
`System.Uri` stays banned (ADR 0004).

### 6.4 `SparqlUpdateException`

The operation's index in the request, its kind, and the cause.

## 7. Tests, and the gate

- **The W3C update evaluation suites** of SPARQL 1.1 — `basic-update`,
  `clear`, `delete`, `delete-data`, `delete-insert`, `delete-where`, `drop`,
  `add`, `copy`, `move`, `update-silent` — each `UpdateEvaluationTest` run
  over a fresh in-memory store: the `ut:data` and `ut:graphData` of the
  action loaded as setup commits, the request executed through
  `SparqlUpdate`, and the store's as-of read at the resulting head compared
  with the result's data by dataset isomorphism — canonical equality, with
  the backtracking check alongside (ADR 0059). Guard counts pin each suite;
  the ratchet holds each case.
- **One request, one commit**, asserted on every case: the head after the
  request is the head before plus one when the result is `Committed`, and
  unchanged when it is `NoChange`; `Diff` over the one commit is non-empty;
  no other result is accepted where the suite expects success.
- **The reference property.** For generated sequences of requests — one to
  four operations each, over `INSERT DATA` with and without blank nodes,
  `DELETE DATA`, `DELETE WHERE`, `DELETE`/`INSERT … WHERE` with and without
  `WITH`, `CLEAR`, `DROP`, `CREATE SILENT`, `ADD`, `COPY`, `MOVE` — applying
  them through `SparqlUpdate`, and applying through the store's commit API
  the deltas a term-level reference model computes by hand, give the same
  as-of state at every position and the same number of commits. A stated
  number of iterations, every counterexample reported.
- **Unit tests** for what the suites do not reach: a `Conflict` returned and
  not retried, and retried under `ConflictRetries`; a failing operation
  leaving the log and dictionary untouched; a later operation matching a term
  an earlier one created; `LOAD` through a test source, with `SILENT`; a
  dataset validator rejecting.

## 8. Open questions

1. **Skolem IRIs** for blank nodes at the protocol boundary (Q1's protocol
   half) arrive with the server, milestone 7; until then a request addresses
   an existing blank node only through a `WHERE` that binds it.
2. **A triple term around an existing blank node** cannot be built by a
   request (ADR 0044's stated limit). An `INSERT` template that would build
   one fails the operation, naming the limit.
