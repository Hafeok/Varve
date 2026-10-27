# SourceBlock

A declaration as Turtle or TriG, read-only: what `/manage/datasets/{name}` and `/manage/graphs` return. Part 1 never edits declarations.

- **Consumer provides:** the graph IRI, the graph's version (the `ETag`), the management position it was read at, and the source text exactly as served.
- Highlighting is three classes only (`vw-tok-iri`, `vw-tok-lit`, `vw-tok-kw`); no highlighting library. Unhighlighted source is acceptable.
- A Copy button copies the source as served, never the rendered text.
