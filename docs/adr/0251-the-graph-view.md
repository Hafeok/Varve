# 0251 — The graph view: SVG through Razor, a deterministic radial layout, a node budget of 50

## Status

**Accepted — filed unaccepted by the session for Web UI part 2 (#355, under
#96), 2026-11-19** (ADR 0066), and accepted by the maintainer on
2026-11-24, written by his delegate (ADR 0239). The plan was approved on
[#96](https://github.com/Hafeok/Varve/issues/96#issuecomment-6979264625) with
the maintainer's rulings, including ruling 4 of 2026-11-02 (the spike and its
rule). The budget of 50 is the maintainer's ruling on
[#355](https://github.com/Hafeok/Varve/issues/355) of 2026-11-19.

It records the decision set [`the-graph-view`](../decisions/the-graph-view.md).

## Context

The explorer's Graph and Resource screens draw a resource's neighbourhood
(#96). The drawing has to:

- be the same picture for the same rows, so that a diff of two positions keeps
  unchanged nodes in place;
- be reachable by keyboard and screen reader, with the table as the same rows
  in another lens;
- hold under the Web UI's CSP (`style-src 'self'`, ADR 0200) and its size
  gate (ADR 0205);
- draw fast enough in interpreted, trimmed WebAssembly that the view does not
  stall the tab.

A force simulation gives a different picture on every run and needs a script
library; neither fits.

## Decision

1. **SVG written by Razor.** `GraphView` renders an `<svg>`; a node is a
   `<g>` with a `<rect>` and `<text>`, an edge a straight line. Every
   coordinate is an attribute, never a `style`, so the CSP holds. No script
   library, no `foreignObject`. `GraphView` lays out nothing: it draws a
   `GraphLayout`.
2. **A deterministic radial layout in C#, with no simulation.**
   `RadialLayout` is a pure class:
   - the resource is at the centre;
   - its neighbours sit on ring 1 in the order the page gives them (direction,
     predicate, term, under one ordinal comparer), each with a sector in
     proportion to its expanded subtree, at least one slot;
   - an expanded neighbour's own neighbours sit on an arc of ring 2 within
     its sector;
   - a ring's radius is the smallest that keeps chips a fixed gap apart and
     leaves room for its longest label;
   - `rdf:type` is a node's caption, not a node; literals are leaf nodes and
     count against the budget.
3. **Labels never meet, and text is never scaled.** A label sits toward the
   neighbour end of its edge, at the first spot clear of every node, expand
   target and label already placed. A crowded sector keeps one label per
   predicate. Where even that does not fit, the rings widen by a quarter and
   the layout is made again, up to twelve times; the result is still a
   function of the rows. The bounds are the drawing's size in pixels, and
   the view scrolls when it outgrows its container. Property tests hold all
   of this over random neighbourhoods.

   **After the twelfth widening**, the layout is drawn at that size, and a
   label that still fits nowhere is left out, never drawn over a node, a
   target or another label, and never scaled. Its edges are drawn without
   it, and under the drawing the view says so in one status line naming
   every label left out: "The label ex:p fits nowhere at this size, so its
   edges are drawn without it.", followed by "Show the table" when the view
   has the table's link. The table still lists every edge with its predicate.
   No neighbourhood found needs even one widening, since a ring's radius
   already leaves room for its longest label: neither the property tests'
   random rings nor a probe of 1 to 40 neighbours, labels up to 500
   characters and up to 49 children in one sector. The rule is held by a
   test that lays out from a fifth of the drawing's size with no widening
   left.
4. **A diff lays out the union of both positions,** so an unchanged node is
   where it was; added and removed edges and nodes take the change tokens.
   Inferred edges are dashed in `edge-inferred` and labelled "inferred".
5. **Keyboard and screen reader.** The SVG is a named `role="group"`. Each
   node is a link to its own Resource view, in ring order, whose accessible
   name carries the relation ("dct:creator → users:alice, outgoing,
   inferred"); edges and labels are `aria-hidden`. Arrow keys move along a
   ring, Space expands or collapses. The table is the first tab.
6. **The node budget is 50.** The rule (ruling 4): the largest size on the
   spike's grid (50, 100, 150, 200, 300) whose first graph draws, cold
   median, in under 100 ms. 100 measured 110.5 ms, so the budget is 50. 75
   was measured too, but is off the grid, and does not set the budget. Over
   the budget, the view draws at most 50 nodes, and `NodeBudget` says, for
   example, "50 of 212 nodes shown, 162 left out", with "Show the table".
   The budget is `GraphBudget.Nodes`. Which 50 nodes are drawn is decided
   with the Graph screen's own slice.
7. **The budget is re-measured, and raised only from that measurement.**
   The spike ran before the explorer was lazy, in the session's container.
   It is measured again once the explorer is lazy, on the perf environment
   (#376), and the budget is raised only from that measurement, by the same
   rule.
8. **The spike is never shipped.** Its page and script live under
   `tests/Varve.WebUi.Spike/` and are built in only with
   `-p:VarveGraphSpike=true`, for a measurement. `eng/browser-size.cs`, which
   runs on the publish the release zip and the image carry (in CI's `browser
   wasm` job and the publish workflow's `web ui bundle, attested`), fails when any
   file of that publish holds the spike's script, its page's type name or its
   route. Checked on 2026-11-20: a default publish passes; one with the
   switch fails on `js/spike.js` and the explorer's assembly.

### The spike's numbers

`tests/Varve.WebUi.Spike/`, published with `-p:VarveGraphSpike=true` at
`ae852fec` and read by `run-spike.js`.

- **What ran:** Chromium 157, headless, on a 4 vCPU Intel Xeon @ 2.10 GHz
  Linux container (the session's cloud container, not a CI runner).
- **The bundle:** interpreted WebAssembly, trimmed, the published bundle,
  served over loopback.
- **The runs:** ten fresh page loads per size. Each load is one cold run (the
  first graph of the load) and ten warm runs, each on a fresh neighbourhood
  of the same size.
- **The data:** the spike page builds each neighbourhood in the browser:
  - one expanded neighbour with min(30, n/5) of its own;
  - the rest on ring 1 over 12 predicates;
  - one edge in seven inferred, one node in nine a literal.

  So the timing runs from the rows in hand to the next frame, with no server
  read.
- **The columns:**
  - "total" is the layout, Blazor's render of the SVG, and the browser's next
    frame after it;
  - the last three columns split the cold median into those parts.

All times are in milliseconds.

| Nodes | Cold median / worst | Warm median / worst | Cold layout / render / frame |
|---:|---:|---:|---:|
| **50** | **73.0** / 82.8 | 18.1 / 43.3 | 22.1 / 43.8 / 7.4 |
| 75 *(off the grid)* | 91.6 / 106.3 | 21.6 / 49.6 | 28.7 / 55.0 / 8.4 |
| 100 | 110.5 / 132.1 | 27.9 / 60.5 | 30.4 / 68.5 / 8.1 |
| 125 *(off the grid)* | 129.3 / 145.0 | 35.0 / 76.0 | 42.4 / 76.3 / 7.8 |
| 150 | 142.1 / 171.2 | 45.2 / 76.4 | 49.3 / 83.2 / 8.1 |
| 200 | 173.3 / 188.5 | 53.5 / 111.7 | 66.5 / 99.3 / 8.9 |
| 300 | 250.5 / 286.2 | 78.8 / 175.8 | 115.2 / 124.1 / 9.1 |

## Alternatives considered

- **A force-directed layout** (a script library or one in C#). It gives a
  different picture on each run, so a diff moves unchanged nodes. It needs
  iterations that cost more than the radial layout's one pass. A script
  library would also need a new package and an ADR, and work around the CSP.
- **Canvas.** It is faster to paint at a few hundred nodes. But it has no
  links, no accessible names and no focus order, and those would have to be
  rebuilt beside it.
- **Scaling the drawing to fit.** Text would shrink below legibility at the
  sizes the budget allows; the view scrolls instead.
- **A budget of 75.** It measured 91.6 ms, under the rule's 100 ms, but it is
  off the spike's grid, and the rule names the grid.

## Consequences

- `GraphView` and `RadialLayout` are organisms in `Varve.WebUi.Explorer`,
  lazy: only explorer screens draw a graph (ADR 0252 point 7). The spike's
  page is built into the explorer too, at an explorer route, under the same
  switch.
- `GraphView` is on no page until the Graph screen's own slice.
- **Checks:**
  - Against the accepted ADRs: 0200 (the CSP: attributes, no inline style),
    0205 (the size gate: no new package), 0206 (the levels and their cases).
    No conflict.
  - Layer ownership: `Varve.WebUi.Explorer` (layer 7, ADR 0252).
  - Analyzer rule: none new.
