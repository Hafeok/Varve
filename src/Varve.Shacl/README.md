# Varve.Shacl

A SHACL 1.2 Core validator over any `IQuadSource` — an `InMemoryDataset`, a
store's view, an overlay — with the W3C SHACL test suites, 2017 and 1.2, as
its gate (ADR 0149).

- **`ShapesGraph.Load`** reads a shapes graph once: its shapes found, checked
  against SHACL's syntax rules, and stratified. An ill-formed shapes graph, or
  one whose recursion runs through a negation, is refused with problems that
  name the shape and the rule (ADRs 0150, 0151). A loaded shapes graph is
  immutable and validates any number of data sources.
- **`ShaclValidator.Validate`** validates a data graph against it. The data is
  read through the quad source contract and never changed; a shape constant
  the data does not hold matches nothing and is never added to it.
- **`ValidationReport`** is a value: `Conforms` and a `Summary` by severity
  without building any RDF, the `Results` in a stated order, and the report
  graph as RDF whose bytes are the same for the same shapes, data and labels.
  A blank node of the data keeps its source's label (ADR 0154).
- **Recursive shapes** mean the greatest fixpoint of each stratum: a person
  who knows only persons conforms on cyclic data (ADR 0150).
- **`sh:pattern`** uses `Varve.Xsd`'s XPath regular expressions under a
  timeout (ADR 0153).

SHACL-SPARQL is `Varve.Shacl.Sparql`, passed as an extension in
`ShaclOptions.Extensions` (ADR 0148). This package references `Varve.Rdf`,
`Varve.Iri` and `Varve.Xsd` only, so SHACL Core ships no query engine.
