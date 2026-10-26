# Varve.Events

Domain events as quads, for systems that do not speak RDF (ADRs 0143–0145,
`docs/spec/events.md`). A publisher writes an event as quads in an events
graph, in the same commit as the change it describes. A catalogue graph
declares each event type: its class, its short name, a SPARQL `SELECT` over
the event node, and its fields. This package renders each event as a
CloudEvents 1.0 event in RFC 8785 canonical JSON. It works over quads alone,
with no store and no HTTP.

- **`EventDeclarations`** reads the declarations out of catalogue graphs, and
  says why one is not well formed. **`EventCatalogue`** names the catalogue
  graphs and maps one back to the source graph that governs it.
- **`EventCatalogueHistory`** replays the catalogue graphs' changes. It
  answers the declarations, versions and immutable schema revisions in force
  at any position. `ContinueFrom` starts the history after a dataset's
  declarations moved to the management plane, keeping the revisions at the
  move; a revision minted after it is a `SchemaRevisionId` written `m{p}`
  (ADR 0167).
- **`EventRenderer`** renders a commit's events. The declaration's `SELECT`
  runs over the event node's closure as of the commit, and its values are
  mapped to JSON by a fixed table and the number rule. Rendering a commit
  again yields the same bytes. An event that cannot be rendered becomes a
  problem event, in its place.
- **`EventSchemas`** generates each type's JSON Schema (2020-12) from its
  declaration when read. Golden files hold the generator's output for ever.
- **`EventViews`** writes the types list, the server-wide list and the
  AsyncAPI 3.0 document.
- **`EventDeclarationCheck`** decides whether a change to a declaration may
  commit. Its checks are well-formedness, a short name taken (refused without
  naming the other declaration), retirement in place of removal, and
  compatibility at an unchanged version.
- **`EventDeclarationShapes`** is the declaration vocabulary's SHACL shapes,
  as Turtle (ADR 0157). This package does not validate by them; a caller can,
  with `Varve.Shacl`. They state what SHACL Core can, and a property holds
  them to `EventDeclarations` on all of it. The `SELECT`, field names unique
  within a type, and the checks across graphs remain the code's.

`Varve.Protocol` serves all of it over HTTP, and its dataset validator runs
the check.
