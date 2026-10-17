# Varve.Shacl.Sparql

SHACL-SPARQL for `Varve.Shacl` (ADR 0148): SPARQL-based constraints
(`sh:sparql`) and SPARQL-based constraint components (`sh:ConstraintComponent`
with SELECT and ASK validators), evaluated by `Varve.Sparql.Evaluation` over
the same `IQuadSource` the validation reads.

```csharp
ShaclOptions options = new() { Extensions = [SparqlConstraints.Extension()] };
ShapesLoad load = ShapesGraph.Load(shapes, GraphPattern.DefaultGraph, options);
ValidationOutcome outcome = ShaclValidator.Validate(load.Shapes!, data, GraphPattern.DefaultGraph, options);
```

Variables are pre-bound by SHACL 1.2's Values Insertion (SHACL 1.2
SPARQL-Related Features, Appendix A): every basic graph pattern, path and
`GRAPH ?g` is joined with the bindings. A query that violates its restrictions
— `MINUS`, `VALUES` or `AS` over a pre-bound variable, `SERVICE` — refuses the
shapes graph. Prefixes come from `sh:prefixes` or, failing that, from the
`sh:declare`s of the shapes graph's ontology or `sh:ShapesGraph` node;
`owl:imports` is not followed (ADR 0151).

It is a separate package so that SHACL Core ships no query engine.
