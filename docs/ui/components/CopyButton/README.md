# CopyButton

A small quiet `Button` that copies a value exactly as given: a request id beside a problem, a declaration's source as served, an IRI too long for its cell.

- **Consumer provides:** the text to copy. It copies the value the API gave, never the rendered text.
- Its label is "Copy" and becomes "Copied" once the value is on the clipboard, so the effect is stated in words. **The state is transient:** after two seconds the label is "Copy" again.
- A visually hidden live region beside it (`aria-live="polite"`) announces "Copied", since a label that changes under focus is not reliably read out.
- No case states "Copied": it follows a click, so a component test clicks and checks the label and the announcement.
- A molecule: it composes the `Button` atom (small, quiet). `ProblemPanel` and `SourceBlock` use it.
