---
set: the-views-and-the-reserved-graphs
namespace: varve
adr: 0198
decisions:
  - key: ExplicitAndImplicitViews
    statement: "varve:explicit and varve:implicit in FROM or FROM NAMED read the explicit or implicit part of the graphs they cover, a view counting as a FROM, and combine with simple and owl2-rl only"
  - key: GraphBindsTheRealGraph
    statement: "Under a view in FROM NAMED, GRAPH ?g binds the real graph's IRI, never the view's"
  - key: ReservedInferredGraphs
    statement: "A binding's TBox-only conclusions are read as varve:inferred/{binding}/tbox and its cross-graph conclusions as varve:inferred/{binding}/{d}, under ADR 0190's read rule, tagged with the position they were read at"
  - key: ClosureSourcesHoldTheContract
    statement: "The implicit view, the reserved graphs and the staged vocabulary constants never deny a term they will yield, answering with a staged handle while the closure is behind"
  - key: InferenceGraphsNotWritable
    statement: "InferenceGraphGuard refuses a quad in varve:explicit, varve:implicit or under varve:inferred/ as graph-not-writable on every write path"
---

The rulings of [ADR 0198](../adr/0198-the-views-and-the-reserved-graphs.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 2 (#171) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
