---
set: the-shapes-binding-vocabulary
namespace: varve
adr: 0170
decisions:
  - key: BindingTerms
    statement: "A varve:ShapesBinding names its shapes graph by varve:boundGraph, its data graphs in the grant vocabulary's graph-set terms on the binding node, its mode by varve:validationMode, and may set varve:allowFullEvaluation and varve:subClassOfInShapesGraph"
  - key: ShapesAreDataInTheDataset
    statement: "The shapes graph is a named graph of the dataset it gates, versioned and readable as of any position like any graph"
  - key: DataGraphIsTheUnionOfBoundGraphs
    statement: "The data graph is the union of the bound graphs, without the shapes graph and without any graph under the varve: namespace"
  - key: BindingDeclarationChecks
    statement: "The declaration validator refuses a shapes binding without exactly one bound graph and mode or without a graph set, with a graph template, or naming its own shapes graph as a data graph"
---

The rulings of [ADR 0170](../adr/0170-the-shapes-binding-vocabulary.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 2a (#138, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
