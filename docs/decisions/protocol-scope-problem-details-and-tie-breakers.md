---
set: protocol-scope-problem-details-and-tie-breakers
namespace: varve
adr: 0092
decisions:
  - key: QueryOperations
    statement: "Queries are served by GET, form POST and direct POST, with default-graph-uri and named-graph-uri replacing FROM and FROM NAMED, negotiated over XML, JSON, CSV and TSV results and N-Triples, N-Quads, Turtle and TriG graphs"
  - key: DefaultResultFormats
    statement: "With no Accept or */*, a solution or boolean result is JSON and a graph result is N-Triples"
  - key: UpdateOperations
    statement: "Updates are served by form POST and direct POST, with using-graph-uri and using-named-graph-uri per section 2.2.3, and naming them beside USING or WITH is 400"
  - key: GraphStoreOperations
    statement: "The Graph Store serves GET, HEAD, PUT, POST and DELETE by direct and indirect identification and ?default, with bodies in the four syntaxes or multipart/form-data"
  - key: GraphExistsWhenItHoldsAQuad
    statement: "A named graph exists when it holds a quad, so an empty PUT leaves it absent"
  - key: VersionParameterAdopted
    statement: "SPARQL 1.2's version is accepted as a request parameter and as a media-type parameter, values 1.1, 1.2-basic and 1.2, the parameter winning over the text and an unknown value being 400"
  - key: ProblemDetailsEverywhere
    statement: "Every error response is an RFC 9457 problem whose type is an IRI under https://w3id.org/varve/problems/, and a SPARQL syntax error carries line, column and offset"
  - key: VarveVocabularyNamespace
    statement: "The service description's own terms are under https://w3id.org/varve/ns#"
  - key: NoOxigraphEndpoints
    statement: "No Oxigraph endpoint is imitated"
  - key: OxigraphTieBreakers
    statement: "Where the protocol is silent, Oxigraph's server behaviour decides, and each such choice is listed in ADR 0092's table"
  - key: ProtocolSuitesAsGates
    statement: "sparql11/protocol and sparql11/graph-store-protocol are gates, http-rdf-update runs as deprecated under its own guard, and service-description's three names are our own checks, all over the memory and file stores"
---

The rulings of [ADR 0092](../adr/0092-protocol-scope-problem-details-and-tie-breakers.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
