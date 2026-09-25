# Varve.Sparql

SPARQL 1.1 Query and Update as an immutable algebra, with SPARQL 1.2's
additions present and marked, a parser from text to the algebra, and a
serialiser back.

- **One tree.** The parser produces the algebra of SPARQL 1.1 §18.2 directly:
  `Join`, `LeftJoin`, `Filter`, `Extend`, `Group`, `Project`, … as sealed
  records with value equality and a source span on every node.
- **SPARQL 1.2 from the start**: triple terms, reified triples and annotation
  syntax expanded as §4.3 prescribes, `VERSION`, and the new functions. A
  caller asks for `1.1`, `1.2-basic` or `1.2`; a `VERSION` declaration in the
  text narrows and never widens.
- **Round trip.** `SparqlWriter` writes text that parses back to the identical
  tree, and a property test over generated algebra holds it to that.
- **Errors carry byte offset, line and column**, and name what was expected.
  The first error ends the parse: a query is one unit.
- **Rewriting without reflection.** `AlgebraRewriter` has one virtual method
  per node type; an optimiser is a rewriter, and so is a linter.

```csharp
Query query = SparqlParser.ParseQuery("SELECT ?s WHERE { ?s a ?type }"u8);
string text = SparqlWriter.ToText(query);
```

Layer 2 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
