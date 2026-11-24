# Varve.Inference.Store

The inference projection over `Varve.Store`, at layer 5 (ADRs 0191 and 0195):
a materialising TBox binding's implicit quads, kept commit by commit.

- **`InferenceProjection`**: an `IProjection` over one binding. Each row is
  one implicit quad with a validity interval `[asserted, retracted)`, placed
  as ADR 0190 places it: a conclusion drawn from the TBox graphs alone in
  `varve:inferred/{enc(binding)}/tbox`, any other in the data graph it was
  drawn in.
- **Generations**: a commit that allocates a term a rule names, or whose
  incremental pass fails, rebuilds the closures; only the rows that differ
  open or close.
- **History** (ADR 0195 point 5a): closed rows are held up to
  `InferenceOptions.HistoryBytes`, oldest positions out first. A read below
  `HeldFrom`, or below the floor, is from scratch.
- **Memory** (point 6): the closures hold at most
  `InferenceOptions.ClosureBytes` together. Past it the binding stops, serves
  nothing at or above the commit that stopped it, and is tried again by a
  commit that changes a TBox graph.

**Not yet:** cross-graph bindings (placement by premise set), the checked
state in the local cache, and the surfaces that read the projection (the
views, the reserved graphs, `owl2-rl`, explain).
