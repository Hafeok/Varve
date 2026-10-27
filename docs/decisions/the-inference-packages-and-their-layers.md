---
set: the-inference-packages-and-their-layers
namespace: varve
adr: 0191
decisions:
  - key: EntailmentAtLayerThree
    statement: "Varve.Sparql.Entailment, layer 3, references Varve.Sparql, Varve.Derivation, Varve.Rdf, Varve.Xsd and Varve.Iri, holds the regimes, the TBox merge, the schema's saturation and the rewriting, evaluates nothing, and is usable over any quad source"
  - key: InferenceAtLayerFour
    statement: "Varve.Inference, layer 4, holds the rule engine, OWL 2 RL/RDF as rules, custom rules and deletion, over the evaluator and Varve.Shacl, and receives SHACL-SPARQL only through Varve.Shacl's extension contract"
  - key: InferenceStoreAtLayerFive
    statement: "Varve.Inference.Store, layer 5, holds the projection, the tier-3 validator and the TBox binding as a value the host maps, and references neither Varve.Management nor Varve.Shacl.Store"
  - key: TablesBuiltPerQuery
    statement: "A rewriting's tables are built once per query and its patterns once per basic graph pattern; what runs per quad, per rewritten pattern or per rule firing is a hot path"
---

The rulings of [ADR 0191](../adr/0191-the-inference-packages-and-their-layers.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 1 (#170) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
