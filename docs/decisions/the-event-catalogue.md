---
set: the-event-catalogue
namespace: varve
adr: 0144
decisions:
  - key: CatalogueGraphPerSourceGraph
    statement: "Event types are declared in https://w3id.org/varve/ns#events-catalogue/{enc(G)}, one graph per source graph, enc the percent-encoded IRI or default, on the varve:setup scheme"
  - key: DeclarationVocabularyInVarve
    statement: "A declaration is varve:EventType and varve:EventField nodes, every node an IRI, with the terms docs/vocabulary.md lists; #105 takes the same terms over unchanged"
  - key: CatalogueGovernedBySourceGraph
    statement: "A caller reads a catalogue graph exactly when it reads its source graph and writes it exactly when it writes the source graph, through GraphScope's governing-graph seam composed in IAccessScopes"
  - key: ThreeViewsAgree
    statement: "SPARQL over the catalogue graphs, the dataset's types list and the AsyncAPI 3.0 document are computed from the same declarations as of the read's position and agree"
  - key: ServerWideListIsTheUnion
    statement: "GET /events/types is the union of the dataset lists over every dataset the caller reads, each entry naming its dataset"
  - key: NoCloudEventsBindingExists
    statement: "The AsyncAPI document describes the structured CloudEvent as the message payload with a trait naming its attributes, and lists the problem event on every channel; no CloudEvents or Event Hubs binding exists"
  - key: NameTakenLeaksNothing
    statement: "A short name is unique within the dataset across catalogue graphs, and its refusal names only the declaration being written, never the other one, its graph, class or version"
  - key: WhatMovesToManagement
    statement: "Declarations move to the Management plane (#105) with the same terms and checks; rendered events and the catalogue graphs' as-of history stay"
---

The rulings of [ADR 0144](../adr/0144-the-event-catalogue.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration, part 2,
of #93 (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
