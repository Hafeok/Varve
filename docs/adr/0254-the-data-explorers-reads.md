# 0254 — The data explorer's reads: one position per view, streamed lines, a read-ahead to a ceiling

## Status

**Accepted — filed unaccepted by the session for Web UI part 2 (#355, under
#96), 2026-11-19** (ADR 0066), and accepted by the maintainer on
2026-11-24, written by his delegate (ADR 0239). The plan was approved on
[#96](https://github.com/Hafeok/Varve/issues/96#issuecomment-6979264625) with
the maintainer's rulings of 2026-11-18 (D2, D4, D5, D7), recorded there.

Filed as 0249, it became 0254 on 2026-11-20 by the maintainer's ruling on #387:
0249 is taken on #385's branch.

It records the decision set
[`the-data-explorers-reads`](../decisions/the-data-explorers-reads.md), and
amends [0203](0203-typed-reads-in-the-protocol-client.md) by a dated
amendment in that file, for the client's new reads.

## Context

The data explorer (#96, part 2) reads; it does not write (ruling 1). Every
screen reads a route `main` already serves, through `Varve.Protocol.Client`
(ADR 0203). Three things about those reads need deciding once, for every
screen: which position a view shows, how a result arrives in a browser, and
how much of one the browser holds.

- A time selector (`Varve-As-Of: time:`) resolves on the server, per request.
  Two reads of one view sent the same time could resolve to two positions if
  a commit lands between them.
- `QueryAsync` returns the raw response. `Varve.Sparql.Results` reads a whole
  buffer only; `Varve.Turtle`'s N-Triples reader takes a whole span or a
  stream to its end.
- A response the reader has paused holds a pinned read and a server worker,
  and is cut at `QueryTimeout` (30 s; ADR 0095).

## Decision

1. **Every answer comes from the server** (ruling 6), under the caller's
   grants and row rules, regime and position. The browser evaluates nothing,
   and never says a thing does not exist: an empty answer is "returned to
   you", and a refusal is the server's problem as sent (ADR 0242).
2. **One view, one position.** A view's first read resolves its selector,
   and every later read of that view is pinned to `position:p` from the first
   answer's `Varve-Position`. A time is sent at most once per view. Two
   panels that read different positions say so, and a pair of reads that is
   meant to share one (point 3) is shown only if it did.
3. **What a regime added is two reads at one position** (ruling 8, D4):
   read A under the regime, read B under `simple` at A's position. A − B,
   as statements, is marked; of it, a triple the readable TBox graphs assert
   is "from the ontology", the rest "inferred", both dashed. SELECT rows are
   never marked; the table names the regime that answered, from the echoed
   `Varve-Entailment`. `RegimeComparison` in the explorer's Parts makes the
   marks; two cases decide whether a statement can be compared at all.
   - **Blank nodes.** A store's blank node is written with the label its
     identity gives it, `b` and a number, the same in every response at every
     position (ADR 0098 point 1). That is what makes its label the same in
     read A and read B, so a statement whose blank nodes are all the store's
     is compared by its terms, labels included. A blank node the query makes
     (a `CONSTRUCT` template's, `BNODE()`) is labelled `q` and a number,
     unique in one execution (`sparql-evaluation.md` §7.9): nothing makes
     `q3` in A the node `q3` in B. A statement that holds one, inside a triple
     term too, is not compared. It is drawn plain, neither inferred nor
     asserted, and the view says under the graph how many: "2 statements hold
     a blank node this query made, which two reads cannot match, so they are
     not marked." Any label that is not a store's is treated so. Entailment
     makes no blank node of its own: the regimes rewrite the query over
     stored terms.
   - **Cut reads.** Marks are shown only when both reads ran to their end,
     at one position. If either stopped at the ceiling, was cancelled, or
     broke before the server finished it, or the two positions differ, no
     statement is marked, and the view says why in one line, for example
     "Nothing is marked as added: the read under the regime stopped before
     its end, at the ceiling or by a cancel, so what the regime added cannot
     be told."
   - Both are held by `RegimeComparisonTests`.
4. **SELECT is read as TSV and a graph as N-Triples, a line at a time**
   (D5). Both are line formats in which a line is a solution or a statement:
   SPARQL 1.1 Query Results CSV and TSV Formats §4 writes a term in Turtle
   syntax with `ECHAR` escapes for tab, LF and CR inside strings, so a raw
   line feed only ends a line (`docs/spec/sparql-results.md` §5.4); N-Triples
   escapes LF and CR likewise. The body is cut into whole lines and each
   batch parsed alone, with the TSV header in front. The cutting is held to
   the chunk-boundary oracle.
5. **Read ahead to a ceiling, then close** (D2). The client reads up to
   5,000 rows or statements into the browser and then closes the response;
   the view shows them in steps of 500, and "Show more" never goes back to
   the server. At the ceiling the view says "Stopped at 5,000 rows:
   download the result to read the rest". Closing is the client ending the
   request, which the server logs (ADR 0112, amended). A download is a fresh
   read pinned to the view's position, copied as it arrives and never held
   whole.
6. **A body that breaks before its end is a cut, never an exception.** A
   limit reached after the first flush travels in a trailer a browser cannot
   read (#351); the reader shows what it has, says the response ended before
   the server finished it, and names the request id.
7. **Query history is per tab** (D7): the last 20 query texts with their
   dataset, regime and selector, in `sessionStorage`, cleared at sign-out. No
   result, token, position read or request id is stored, and nothing on the
   server.

## Alternatives considered

- **Pause the stream and resume on "Show more".** Refused by D2: a paused
  read holds a pinned read and a worker, and comes back cut after 30 s.
- **A resumable reader in `Varve.Sparql.Results`** (D5's other option). New
  public API with its own oracle in a layer-2 package, for one format; the
  line formats need none.
- **Re-running the query with OFFSET for each page.** An order without
  ORDER BY is not promised, and each page would be a new read.
- **A time sent with every read of a view.** Two reads could show two
  positions as one view.

## Consequences

- `Varve.Protocol.Client` gains the streamed answer and the explorer's
  other reads (ADR 0203, amended 2026-11-19).
- The explorer never holds more than the ceiling: about 5,000 solutions as
  owned terms.
- **Checks:**
  - Against the accepted ADRs: 0095 (the read limits, unchanged), 0096 (the
    selector), 0107 and 0235–0238 (the server filters; the client shows what
    it got), 0192 (the regime header), 0203 (amended). No conflict.
  - Layer ownership: `Varve.Protocol.Client` (layer 5) and `Varve.WebUi`
    (layer 6).
  - Analyzer rule: none new.
