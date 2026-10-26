# Varve.Shacl.Store

Commit-time SHACL gating for `Varve.Store` (ADR 0169). A `ShapesBinding`
names a shapes graph of the dataset, the graphs it validates as one data
graph, and a mode, and `ShapesGate` is the dataset validator that enforces it:

```csharp
ShapesBinding binding = new(bindingIri, shapesGraphIri, GraphScope.All, ValidationMode.Reject);
DatasetOptions options = new() { Validators = [new ShapesGate([binding])] };
```

- **`Reject`** refuses a commit whose affected focus nodes have a
  `sh:Violation` that was not there before it, with the problem
  `shapes-violated`. The set of violations never grows.
- **`Report`** accepts the commit and attaches
  `<<( binding varve:affectedReport "sha256:…" )>>`, the digest of the report
  restricted to the focus nodes the commit affected (ADR 0173).
- **`Off`** runs nothing.

Only the focus nodes a commit affects are validated (ADR 0171), in the state
before and after it. A commit that changes the shapes is never refused for the
data already there. Shapes whose classification is full are refused under
`Reject` unless the binding allows full evaluation (`binding-not-incremental`),
and a shapes graph that does not load refuses the commit (`shapes-invalid`).

On a server the bindings are management declarations (`varve:ShapesBinding`,
ADR 0170), applied at the management position a write is authorised under.
