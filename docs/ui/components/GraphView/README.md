# GraphView

A bounded neighbourhood as SVG written by Razor: nodes are links in ring order (Tab), Enter follows, Space or the + control expands, arrow keys move along a ring. Edges and their labels are `aria-hidden`; each node's accessible name carries the relation ("dct:creator → users:alice, outgoing, inferred"). Inferred edges are dashed in `edge-inferred` and labelled "inferred"; a diff draws the union of both positions so unchanged nodes stay put. The component takes a laid-out model: the radial layout is a pure class tested on its own, so these cases pin rendering, not layout. Text is never scaled: the layout's bounds set the SVG's width and height one to one, and the view scrolls when it outgrows its container. Labels sit toward the neighbour end of their edge, on a surface halo, clear of every node, target and other label; a crowded sector keeps one label per predicate. Each expand target is 24×24. Parameters: Title, Nodes (x, y, term, kind, type, diff, expandable, expanded, accessible name, href), Edges (from, to, label, kind), Labels (x, y), Bounds, Budget (a `NodeBudget`).

- **Layout:** `RadialLayout` (ADR 0251), a pure class with its own property test: no two label boxes meet, and no label box meets a node or an expand target.

Level: organism. Cases: `centre-only`, `ring`, `inferred-edges`, `expanded`, `diff`, `over-budget`.
