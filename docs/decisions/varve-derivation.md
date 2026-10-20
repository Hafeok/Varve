---
set: varve-derivation
namespace: varve
adr: 0172
decisions:
  - key: DerivationAtLayerTwo
    statement: "Varve.Derivation, layer 2, referencing Varve.Rdf only, holds the dependency graph with its stratification, regular paths with their anchors, and propagation to a least fixpoint, generic over the caller's node and label types"
  - key: StratificationIsDerivations
    statement: "Strongly connected components, the stratification and the refused cycle through a negative or target edge are computed by Varve.Derivation's dependency graph; the polarity of each SHACL parameter stays Varve.Shacl's"
  - key: PathsAreRegularPaths
    statement: "A SHACL path is compiled to a regular path automaton, evaluated forwards and backwards with set semantics, a zero-length path reaching its start whatever the graph holds"
  - key: SemiNaivePropagation
    statement: "Propagation transfers each node of a set's frontier once, component by component in topological order, to the least fixpoint"
  - key: DerivationModelNamespace
    statement: "The values Varve.Derivation's algorithms take and answer, a polarity, an edge and a refused cycle, are its model namespace Varve.Derivation.Model; the algorithms and the graph a path walks are not"
  - key: SearchesAllocatePerCall
    statement: "A path search's or a propagation's visited sets and frontiers are allocated per call, never per triple or per edge"
---

The rulings of [ADR 0172](../adr/0172-varve-derivation.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 2a (#138, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
