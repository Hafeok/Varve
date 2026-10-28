# TermChip

One RDF term, its kind shown by form: an IRI in the link colour with no frame, a literal quoted on a fill with its datatype or language as a caption, a blank node in a dashed outline, an RDF 1.2 triple term as a frame `<<( … )>>` holding three chips.

- **Consumer provides:** the term as the API serialises it (from SPARQL results, a delta or a source graph), and, for an IRI, a compact form when a prefix applies, with the full IRI as the accessible name and `title`.
- Used wherever a term appears: results, deltas, diffs, the graph view's nodes. Never render a term as bare text.
- Modifiers `vw-term--added` and `vw-term--removed` mark a term inside a diff; a removed term is also struck through.
- Literals show `xsd:string` only when the API states it explicitly; language-tagged literals show the tag (`@en`), with the direction (`--ltr`) in RDF 1.2.
