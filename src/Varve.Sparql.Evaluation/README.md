# Varve.Sparql.Evaluation

The SPARQL optimiser and evaluator: a `Query` from `Varve.Sparql`, evaluated
over any `IQuadSource` — a `Varve.Store` view, pinned or as of a position, or
an `InMemoryDataset`. Update execution follows at milestone 5c.

- **Every operator and the whole function library** of SPARQL 1.1, with the
  1.2 additions: aggregates, property paths, subqueries, `VALUES`, `MINUS`,
  `EXISTS`, `SELECT`, `ASK`, `CONSTRUCT` and a minimal `DESCRIBE`.
- **An optimiser that changes no answer**: filter placement, triple and join
  order by the source's estimates, constant folding, trivial joins — each
  held to an equivalence property. `Optimise = false` skips it.
- **Nothing ambient.** `NOW()` reads the clock in the options, the random
  functions the random source; without one they fail by the option's name.
  Extension functions and aggregates arrive in the options too.
- **`SERVICE` through a handler you supply.** The default refuses; `SILENT`
  turns a failure into the empty solution.
- **Handles, not terms.** Solutions hold the source's handles and compare
  inline integers without externalising them; a `SELECT` over a basic graph
  pattern allocates its row per solution and nothing per quad.
- **Cancellation** is checked at every operator and inside scans, sorts and
  path searches.

```csharp
var evaluator = new SparqlEvaluator(new EvaluationOptions { Clock = TimeProvider.System });
using DatasetView view = dataset.Pin();
using QueryResults results = evaluator.Evaluate(query, view, cancellationToken);
var solutions = (SolutionResults)results;
while (solutions.MoveNext())
{
    if (solutions.TryGetTerm(0, out RdfTerm? term)) { /* … */ }
}
```

The source is read as it is for the whole evaluation and is not the
evaluator's to dispose: pin it, evaluate, then release it.

Layer 3 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
