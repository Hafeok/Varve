# Varve.Sparql.Entailment

SPARQL 1.1 Entailment Regimes by **query rewriting**: the RDF, RDFS and D
regimes answered by simple evaluation of a rewritten query, nothing
materialised.

- **The TBox merged into every graph.** Each graph is read merged with the
  TBox graphs of the binding that covers it (`TBoxCover`), through a view of
  the caller's source, so a graph the caller cannot read lends nothing.
- **The schema by probes.** A scoping graph's schema is saturated by bound
  probes to a fixpoint, never by a scan of the data, and kept in a
  `SchemaCache` while the graphs it merges keep their versions.
- **Complete for the three regimes**, under conditions C1 and C2 and the
  multiplicity rule: each basic graph pattern becomes the union of the
  asserted patterns that entail it, the branches that add nothing pruned.
- **Two shapes cost what their answers do:** `?s ?p ?o` and
  `?x a rdfs:Resource`. A triple about the vocabulary itself makes a schema
  read its graph whole; `FullReads` names the graphs that hold one.
- **D by value**, with Varve.Xsd's datatype map, through two extension
  functions a host registers (`LiteralValues`).

```csharp
EntailedQuery entailed = EntailmentRewriter.Rewrite(
    query, source, [new TBoxCover([tbox], GraphScope.All)], EntailmentRegime.Rdfs, schemas: cache);

using QueryResults results = new SparqlEvaluator().Evaluate(entailed.Query, entailed.Source);
```

Layer 3 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
