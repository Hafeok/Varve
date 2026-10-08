---
set: varve-protocol-and-varve-server
namespace: varve
adr: 0091
decisions:
  - key: ProtocolIsALayer5Library
    statement: "Varve.Protocol, layer 5, holds the SPARQL Protocol, the Graph Store Protocol, the service description, the change feed and the diff as IEndpointRouteBuilder extensions with no host assumption and no authentication type"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ServerIsTheHost
    statement: "Varve.Server, layer 6, is the executable and composition root: configuration, hosting, authentication and the wiring of datasets, clocks and validators"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: UpdateExecutorSeam
    statement: "Varve.Protocol declares ISparqlUpdateExecutor and the host binds it to Varve.Sparql.Store; the query choreography of ADR 0052 is written once in Varve.Protocol"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: DatasetResolverSeam
    statement: "Varve.Protocol asks an IDatasetResolver the host implements for the dataset a request names"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: PermissionsArePolicyNames
    statement: "Every endpoint authorises imperatively and first, through the host's IAuthorizationService given in ProtocolOptions, by the policy names varve:read, varve:write and varve:admin with the DatasetName as resource; a refusal is a challenge or a forbid with no detail"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: CallerIdentitySeam
    statement: "The caller's identity reaches the protocol through ICallerIdentity, which the host implements from the token"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ProtocolContractVocabulary
    statement: "Varve.Protocol's contract vocabulary adds Varve.Store and Varve.Sparql in its own project file, and the global value is unchanged"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ProtocolModelNamespace
    statement: "Varve.Protocol.Model holds the protocol's public data: dataset names, as-of selectors, limits, problem types and the change feed's records; the root namespace holds the endpoints, the options, the seams and the reader"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
---

The rulings of [ADR 0091](../adr/0091-varve-protocol-and-varve-server.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
