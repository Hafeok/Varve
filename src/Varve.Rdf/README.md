# Varve.Rdf

The RDF 1.1 and 1.2 term model, and the abstract quad source contract.

- **Two representations.** `RdfTermView` is a zero-allocation window over a
  parser's buffer, valid for the callback that receives it. `RdfTerm` is the
  owned term, produced only when someone asks — because allocation per quad is
  a defect, not a trade-off.
- **RDF 1.2 from the start**: triple terms, and language-tagged strings with a
  base direction.
- **Term equality, permanently.** Character by character over lexical form,
  datatype IRI and language tag (Concepts §3.3). `"1"^^xsd:integer` and
  `"01"^^xsd:integer` are different terms and stay different terms; value
  comparison belongs to a SPARQL evaluator, not to a graph.
- **`IQuadSource`** is the contract everything reads through: an opaque 64-bit
  handle, internalise and externalise, an equality comparer supplied by the
  source, and four explicit graph modes — the default graph, one named graph,
  every named graph (SPARQL's `GRAPH ?g`), or the union. Two members for an
  optimiser: a **cardinality estimate** per pattern that is exact, estimated
  or honestly unknown, and an **inline-value accessor** that hands over the
  integer or boolean a handle encodes in its own bits without materialising
  the term.

- **`QuadOverlay` and `QuadDelta`**: a source with a change applied,
  `(B \ R) ∪ A`, merged at scan time. One implementation serves a store's
  as-of reads and the view a pre-commit validator gets.

```csharp
InMemoryDataset dataset = new();
dataset.Add(RdfTerm.Iri("http://example.org/s"u8), RdfTerm.Iri("http://example.org/p"u8),
            RdfTerm.Literal("chat"u8, "en-GB"u8));

using IQuadCursor cursor = dataset.Match(
    TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph);
```

Layer 1 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
