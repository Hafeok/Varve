---
set: the-tbox-binding-vocabulary
namespace: varve
adr: 0194
decisions:
  - key: TBoxBindingTerms
    statement: "A varve:TBoxBinding is an IRI node of its dataset's declaration graph with one or more varve:boundGraph TBox graphs, a graph set in the grant terms, one varve:profile and at most one varve:defaultEntailment, and the reserved terms varve:rulesGraph, varve:crossGraph, varve:sameAs and varve:inconsistencyMode"
  - key: RewritingOnlyInPartOne
    statement: "Until #171 the only profile with an effect is varve:Rewriting and the only defaults simple, RDF, RDFS and D; every other profile, value and reserved term is refused with the part that gives it effect named"
  - key: OneTBoxBindingPerGraph
    statement: "Each graph of a dataset is covered by at most one TBox binding, overlap judged on graphs, prefixes and allGraphs, and the data graphs never include the binding's TBox graphs or a varve: graph"
  - key: DefaultIsTheDatasets
    statement: "The bindings of a dataset agree on varve:defaultEntailment, which is the dataset's default regime"
  - key: TBoxBindingRefusals
    statement: "The validator refuses a TBox binding without a TBox graph or with one that is the default graph or a varve: graph, without exactly one profile or a graph set, with a graph template, or naming its own TBox graph among its data graphs"
  - key: TBoxShapesFirst
    statement: "The binding's checks are shapes in shapes/management.ttl at runtime, the overlap, the dataset's default and the reserved terms a residual code check, and the extended code validator the tests' oracle"
  - key: FullReadsSurfaced
    statement: "A TBox binding's description, behind admin, names the graphs among its TBox and covered graphs that force a schema's full read, the default graph as null, and readiness counts them per dataset without naming them and never turns unready for them"
  - key: PlanListsDatasetPublicGraphs
    statement: "A management plan lists per dataset the graphs its TBox bindings make dataset-public, marking those widened and narrowed by the apply, and varve apply --plan prints the list before the graph diffs"
---

The rulings of [ADR 0194](../adr/0194-the-tbox-binding-vocabulary.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 1 (#170) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
