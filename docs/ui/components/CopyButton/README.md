# CopyButton

A small quiet `Button` that copies a value exactly as given: a request id beside a problem, a declaration's source as served, an IRI too long for its cell.

- **Consumer provides:** the text to copy. It copies the value the API gave, never the rendered text.
- Its label is "Copy" and becomes "Copied" once the value is on the clipboard, so the effect is stated in words.
- A molecule: it composes the `Button` atom (small, quiet). `ProblemPanel` and `SourceBlock` use it.
