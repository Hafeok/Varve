# SourceBlock

A declaration as Turtle or TriG, read-only: what `/manage/datasets/{name}` and `/manage/graphs` return. Part 1 never edits declarations.

- **Consumer provides:** the graph IRI, compact only with a prefix the source itself defines, and then its full IRI too, the graph's version (the `ETag`), the management position it was read at, and the source text exactly as served: for a declaration, the whole document, `@prefix` lines and the graph's wrapper included.
- **Unhighlighted in part 1.** No highlighting classes ship until cases cover them; part 2 may add highlighting with its cases. Never a highlighting library.
- **The source scrolls inside itself,** so its `<pre>` takes focus (`tabindex="0"`) for the keyboard, and is a named region (`role="region"`, `aria-label` the graph's full IRI), so a screen reader says what it is on.
- A Copy button copies the source as served, never the rendered text, so it parses as served (review F24); the browser tests parse what it copies as TriG on every screen with a source.
