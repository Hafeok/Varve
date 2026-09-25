# 0057 — SPARQL Update over the store: one request, one commit

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the milestone 5c plan.
Answers the brief's tension "SPARQL Update semantics (DELETE/INSERT WHERE
evaluated against which position) and how one update request maps to one
atomic commit", which ADR 0005 placed in a layer 5 package without deciding.
Specification: [`docs/spec/sparql-update-store.md`](../spec/sparql-update-store.md).

> **Amended 2026-09-25**, by the maintainer's decision on the milestone 5c
> report. **Execution's steps 3 and 4 are swapped: the pin is released, then
> the composed delta is submitted.** That is the order the specification
> (§3) and `SparqlUpdate.ExecuteAsync` already had; only this record listed
> the release last. The reasons:
> - Nothing after the request is built reads the pin. The request ties itself
>   to `P` by `expectedPosition`, not by holding a read.
> - A term the pin holds goes in as `RequestTerm.Existing(handle)`, and the
>   dictionary is append-only (ADR 0012), so the handle stays valid without
>   the pin. A staged term goes in as a request term the sequencer resolves
>   (ADR 0058).
> - The sequencer normalises against `G_head`, which is `G_P` or a
>   `Conflict`, so the outcome is the same in either order.
> - Holding the pin across the submit would keep a pinned read alive while
>   the request waits in the sequencer's queue. ADR 0052 has a pin live for
>   one execution and no longer, and that wait is unbounded under load.
>
> A failing operation still throws before the submit, and the pin is
> released on that path too. The steps below are left as written; read 3
> and 4 in the swapped order. No code or specification changes.

## Context

ADR 0005 put SPARQL Update in a layer 5 integration package that "evaluates
the `WHERE` clause against a pinned position and submits the resulting delta
as one commit". Three things it did not settle:

- **A request is a sequence.** SPARQL 1.1 Update §2.2: operations execute
  with "the same effects as executing them sequentially in the order they
  appear", a request "SHOULD be treated atomically", and one failure "MUST
  abort the sequence". Operation 2 must see operation 1's effect, yet nothing
  is committed until the end — so what does operation 2 read?
- **What happens to a concurrent writer.** The pin is taken before
  evaluation and the commit made after; another commit can land in between.
- **Graphs.** SPARQL has graph management — `CREATE`, `DROP`, `COPY` — over
  graphs that may exist while empty. The store's log records quads (ADR 0010)
  and its indexes hold quads (ADR 0041): a graph with no quads is not
  represented anywhere.

## Decision

### Execution

1. **Pin** the readable head `P` and open a staging view over it (ADR 0058).
2. **Evaluate each operation, in order, against the overlay of the deltas
   before it**: operation `k` reads `Overlay(staging, δ₁ ; … ; δₖ₋₁)` — the
   overlay of ADR 0017, which is what it exists for — and its delta `δₖ` is
   computed exact against that source. The deltas form a chain of exact
   deltas, so their composition is exact against `G_P` (ADR 0047).
3. **Submit the composed delta as one commit with `expectedPosition = P`.**
   The sequencer normalises against `G_head`; with the expected position
   met, that is `G_P`, so what commits is the composed delta, and the
   pre-commit validators — the dataset's and the request's (ADR 0058) — run
   on it as part of the commit (T1 step 5).
4. **Release the pin.** A failing operation throws before step 3, and nothing
   reaches the log or the dictionary.

A request with an empty net effect makes no commit and returns `NoChange`
(I4, ADR 0010). One request is therefore **at most one commit**, and exactly
one when it changes anything.

### Concurrency

**A `Conflict` is returned, not retried.** Whether a request evaluated
against `P` still means the same against a later head is the caller's
question — a `DELETE … WHERE` that removed a stale value may, re-run, remove a
fresh one. `UpdateOptions.ConflictRetries` (default 0) lets the caller say
yes; each retry re-pins and re-evaluates from the start.

### Graphs

**A named graph exists if and only if it holds a quad.** SPARQL 1.1 Update
allows it: "Stores that do not record empty graphs will always return
success" (§3.1.5, §3.2.2), and creating a non-existing graph always succeeds
in such a store (§3.2.1). So `CREATE` has no effect — and fails without
`SILENT` when the graph holds a quad, because then it exists and §3.2.1 says a
failure SHOULD be returned; `DROP` and `CLEAR` retract the graph's quads;
`ADD`, `COPY` and `MOVE` are operations on quads; and a source graph that
does not exist is an empty one, so `SILENT` changes nothing for them.

### `LOAD`

A contract, `ILoadSource`, resolves an IRI to a document and its syntax;
parsing is `Varve.Turtle`'s. The default refuses every IRI. The HTTP source is
the server's (milestone 7), as ADR 0055 does for `SERVICE`. `LOAD SILENT`
makes a failure an operation with no effect (§3.1.4).

### Public surface

One entry point — `SparqlUpdate.ExecuteAsync(Dataset, Update, UpdateOptions,
CancellationToken)` returning the `CommitResult` — with `UpdateOptions`,
`ILoadSource`, `LoadedDocument` and `SparqlUpdateException`. Nothing else.

## Alternatives considered

- **Execute each operation as its own commit.** Needs no overlay and no
  staging, and it is what a naive loop over `CommitAsync` gives. Rejected by
  ADR 0005 and by §2.2's atomicity: a failure in operation 3 would leave 1 and
  2 committed, and a reader between commits would see a state no request
  produced.
- **Evaluate every operation against the pin**, and compose the deltas.
  Simpler still: no overlay. Rejected: it breaks §2.2's sequential effect —
  `INSERT DATA { :a :p 1 } ; DELETE WHERE { :a :p ?o }` must delete what the
  first inserted.
- **Apply operations to a materialised copy** of the dataset. Correct, and
  proportional to the dataset rather than the change, which on a million-quad
  store is the wrong cost for a one-quad update. The overlay's cost is the
  delta's.
- **Retry on `Conflict` by default.** What most callers of a database expect.
  Rejected: it turns optimistic concurrency (ADR 0011) into last-writer-wins
  for exactly the requests — read, decide, write — that asked for the
  guarantee, and it does so silently. A caller that wants it sets one option.
- **Record empty graphs**, as Oxigraph does: a named graph can exist with no
  triples, and `CREATE`/`DROP` are observable. Faithful to the letter of
  §3.2, and it makes every graph-management case of the suite behave as
  written. Rejected: an empty graph has no quad to carry it, so the log would
  need an event kind of its own — a graph creation and deletion outside the
  quad delta — every projection would have to understand it, and as-of reads,
  diff and subscriptions would gain a second kind of change, all for a state
  no query can see except through `GRAPH ?g {}` over an empty pattern. §3.2
  names this store's choice as a permitted one.
- **Retry inside the store**, by letting the sequencer re-run the request.
  Impossible by ADR 0005: the sequencer cannot evaluate SPARQL.

## Consequences

- **One request is one position** in the log, or none. The conformance
  harness asserts it on every update case.
- **A request's `WHERE` clauses cost an overlay each**, which grows with the
  request's own delta; a request of many large operations pays for the
  earlier ones' deltas in every later scan. Bulk loading is not this path
  (ADR 0013).
- **Suite cases that depend on an empty graph existing** would need an
  exemption citing §3.2's allowance. The report of the milestone names each.
- **`Conflict` is visible to callers** of the update API, as it is to callers
  of the store.

## Checks

- **Checked against the accepted ADRs** (0001–0056) and the specification.
  Touches **0005** (this is the package it anticipated, and the pin-evaluate-
  commit shape is its), **0010** (effective deltas; `NoChange` for an empty
  request; I4), **0011** (the expected position, and `Conflict` returned),
  **0015** and **0052** (a pin lives for the request, released in every path),
  **0017** (the overlay between operations, and validators on the commit),
  **0041** (graphs have no existence apart from their quads), **0044** (blank
  nodes in a request are fresh; an existing one is addressed by handle),
  **0047** (composition over a chain), **0055** (the refusing-default contract
  pattern) and **0056** (clock and randomness from options). No conflict with
  any.
- **Layer ownership.** `Varve.Sparql.Store`, **layer 5**; it references
  `Varve.Store` (4), `Varve.Sparql.Evaluation` (3), `Varve.Sparql` and
  `Varve.Turtle` (2), `Varve.Rdf` (1) and `Varve.Iri` (0).
- **Analyzer rule.** None. `System.Uri` stays banned in this package: `file:`
  IRIs go through `Varve.Iri` (ADR 0004); the narrowing ADR 0004 anticipates
  for layer 5 is the server's, at milestone 7.
- **Open questions owned.** None of the specification's. Q1's protocol half
  (skolem IRIs) stays with milestone 7.
