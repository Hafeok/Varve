# Varve.Classification

Row-level access for `Varve.Store` (ADRs 0230–0233). A `ClassifierBinding`
names an annotated shapes graph of the dataset and the graphs it classifies.
The classifier reads three annotations from the shapes graph:

- `varve:dataSubject true` on a node shape: its focus nodes are data subjects.
- `varve:subjectLink true` on a property shape with a predicate path:
  ownership flows along that predicate.
- `varve:publicLink true` on a property shape: that predicate's object is not
  hidden with the row it names. `rdf:type` is always public.

A node belongs to the data subjects it is, and to those its links reach. A
quad is about its subject's subjects and, unless its predicate is a public
link, its object's.

```csharp
ClassifierBinding binding = new(bindingIri, shapesGraphIri, GraphScope.All);
ClassificationProjection projection = new(dataset, binding);
await dataset.CatchUpAsync(projection, ct);

using DatasetView view = dataset.Pin();
projection.TryAt(view.Position, view, out IRowClassification rows);
IQuadSource visible = RowScopedQuadSource.Wrap(view, rows, callerRows);
```

`ShapeClassifier` is `Varve.Store`'s `IClassifier`, from scratch over any
state. `ClassificationProjection` keeps the classification at every position,
maintained from exact deltas. It is saved to the local cache, verified in full
on read, and rebuilt when the cache is damaged. A shapes graph that does not
classify fails closed: a row-restricted caller sees nothing of the binding's
graphs.
