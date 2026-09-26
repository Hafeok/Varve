---
set: service-through-a-handler-the-default-refuses
namespace: varve
adr: 0055
decisions:
  - key: ServiceHandlerContract
    statement: "IServiceHandler in Varve.Sparql.Evaluation executes a ServiceRequest carrying the Service node, the endpoint IRI and the incoming solutions as terms, and returns solutions or a failure"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ServiceResultsJoined
    statement: "The evaluator joins a handler's solutions with the incoming ones, so a handler may use them to narrow its request or ignore them"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ServiceVariablePerDistinctIri
    statement: "SERVICE ?v invokes the handler once per distinct IRI ?v takes, and an unbound or non-IRI ?v fails that invocation"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: DefaultServiceHandlerRefuses
    statement: "The default service handler refuses every endpoint, so the default configuration opens no connection"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: UnsilencedServiceFailureFailsQuery
    statement: "A failed SERVICE without SILENT fails the query with an exception naming the endpoint"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: SilentServiceFailureIsOmegaZero
    statement: "A failed SERVICE SILENT evaluates to one empty solution mapping"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: HttpServiceHandlerIsTheServers
    statement: "The HTTP service handler is the server's, at milestone 7"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0055](../adr/0055-service-through-a-handler-the-default-refuses.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
