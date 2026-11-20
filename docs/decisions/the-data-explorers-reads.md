---
set: the-data-explorers-reads
namespace: varve
adr: 0254
decisions:
  - key: EveryAnswerFromTheServer
    statement: "The explorer evaluates nothing: every answer is the server's, under the caller's grants, rows, regime and position, and an empty one is what was returned to the caller, never that a thing does not exist"
  - key: OneViewOnePosition
    statement: "A view's first read resolves its selector and every later read of the view is pinned to that answer's position; a time is sent at most once per view"
  - key: RegimeAddedIsTwoReadsAtOnePosition
    statement: "What a regime added is the regime's answer less simple's at the same position; of it, what the readable TBox graphs assert is from the ontology and the rest inferred; SELECT rows are never marked"
  - key: LineFormatsReadALineAtATime
    statement: "SELECT is read as SPARQL 1.1 TSV and a graph as N-Triples, the body cut into whole lines and each batch parsed alone, the cutting held to the chunk-boundary oracle"
  - key: ReadAheadToACeilingThenClose
    statement: "The client reads up to 5,000 rows into the browser and closes the response; the view shows them 500 at a time and Show more never goes back to the server"
  - key: ABrokenBodyIsACut
    statement: "A body that breaks before its end ends the answer as cut, with what was read and the request id, never as an exception"
  - key: HistoryPerTab
    statement: "Query history is the last 20 query texts with their dataset, regime and selector, in sessionStorage, cleared at sign-out; no result, token, position read or request id is stored"
---

The rulings of [ADR 0254](../adr/0254-the-data-explorers-reads.md), filed
unaccepted by the session for #355 (ADR 0066).
