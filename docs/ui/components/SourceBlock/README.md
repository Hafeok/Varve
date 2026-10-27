# SourceBlock

A declaration as Turtle or TriG, read-only: what `/manage/datasets/{name}` and `/manage/graphs` return. Part 1 never edits declarations.

- **Consumer provides:** the graph IRI, the graph's version (the `ETag`), the management position it was read at, and the source text exactly as served.
- **Unhighlighted in part 1.** No highlighting classes ship until cases cover them; part 2 may add highlighting with its cases. Never a highlighting library.
- **The source scrolls inside itself,** so its `<pre>` takes focus (`tabindex="0"`) for the keyboard, and is a named region (`role="region"`, `aria-label` the graph IRI), so a screen reader says what it is on.
- A Copy button copies the source as served, never the rendered text.
