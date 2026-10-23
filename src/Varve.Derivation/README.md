# Varve.Derivation

Delta-driven maintenance over any graph, at layer 2 (ADR 0172): what
`Varve.Shacl`'s incremental validation and, later, Inference share. Generic
over the caller's node and edge label, so it knows neither handles nor terms.

- **`DependencyGraph<TLabel>`** and **`Stratification<TLabel>`**: vertices with
  edges labelled by a `Polarity`; strongly connected components numbered
  dependencies first; the cycles through a negative or target edge that refuse
  a stratification (ADR 0150's construction).
- **`RegularPath<TLabel>`** and **`PathAutomaton<TLabel>`**: SHACL's paths and
  SPARQL's property paths, compiled once by Thompson's construction and
  searched over an `IPathGraph<TNode, TLabel>`: forwards (`Evaluate`, the set
  of nodes reached), backwards (`Reverse`), and from a changed edge
  (`Anchors`, the start nodes whose image it may change). `IsBounded` says
  whether the path has a closure.
- **`Propagation<TNode, TLabel>`**: sets of nodes per vertex, closed under the
  dependencies to the least fixpoint, each new node transferred once along
  each edge (semi-naive).

Deletion for least fixpoints — delete and re-derive, or counting — is
Inference's to add (#95); nothing here uses it yet.
