---
set: varve-protocol-and-varve-server
namespace: varve
adr: 0091
decisions:
  - key: ProtocolIsALayer5Library
    statement: "Varve.Protocol, layer 5, holds the SPARQL Protocol, the Graph Store Protocol, the service description, the change feed and the diff as IEndpointRouteBuilder extensions with no host assumption and no authentication type"
  - key: ServerIsTheHost
    statement: "Varve.Server, layer 6, is the executable and composition root: configuration, hosting, authentication and the wiring of datasets, clocks and validators"
  - key: UpdateExecutorSeam
    statement: "Varve.Protocol declares ISparqlUpdateExecutor and the host binds it to Varve.Sparql.Store; the query choreography of ADR 0052 is written once in Varve.Protocol"
  - key: DatasetResolverSeam
    statement: "Varve.Protocol asks an IDatasetResolver the host implements for the dataset a request names"
  - key: PermissionsArePolicyNames
    statement: "Endpoints carry the policy names varve:read, varve:write and varve:admin, which the host binds and decides per dataset on the route's dataset value"
  - key: CallerIdentitySeam
    statement: "The caller's identity reaches the protocol through ICallerIdentity, which the host implements from the token"
  - key: ProtocolContractVocabulary
    statement: "Varve.Protocol's contract vocabulary adds Varve.Store and Varve.Sparql in its own project file, and the global value is unchanged"
---

The rulings of [ADR 0091](../adr/0091-varve-protocol-and-varve-server.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
