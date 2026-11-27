---
set: the-graph-view
namespace: varve
adr: 0251
decisions:
  - key: SvgWrittenByRazor
    statement: "The graph view is an SVG written by Razor, every coordinate an attribute and never a style, with no script library and no foreignObject; the view draws a laid-out model and lays out nothing"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: DeterministicRadialLayout
    statement: "The layout is radial and deterministic, with no simulation: the resource at the centre, its neighbours on ring 1 in the order given, an expanded neighbour's on an arc of ring 2 within its sector; the same rows always give the same picture"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: LabelsNeverMeetTextNeverScaled
    statement: "No label meets another label, a node or an expand target; a crowded sector keeps one label per predicate, and where that does not fit the rings widen and the layout is made again, up to twelve times; after the twelfth a label that still fits nowhere is left out, its edges drawn without it, and the view names it in a status line; text is never scaled"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: ADiffLaysOutTheUnion
    statement: "A diff lays out the union of both positions, so an unchanged node stays where it was"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: TheSpikeIsNeverShipped
    statement: "The graph view's spike is built in only with -p:VarveGraphSpike=true, and eng/browser-size.cs fails on the publish the release and the image carry when a file of it holds the spike's script, page type or route"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: NodeBudgetFromTheSpike
    statement: "The node budget is 50: the largest size on the spike's grid whose first graph draws, cold median, in under 100 ms; it is raised only from a re-measurement on the perf environment with the explorer lazy, by the same rule"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
---

The rulings of [ADR 0251](../adr/0251-the-graph-view.md), filed
unaccepted by the session for #355 (ADR 0066) and accepted by the maintainer
on 2026-11-24, written by his delegate (ADR 0239).
