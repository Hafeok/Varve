---
set: telemetry-in-two-layers
namespace: varve
adr: 0112
decisions:
  - key: InstrumentationIsBclOnly
    statement: "Varve.Protocol owns the ActivitySource Varve.Protocol, one activity per request named by its operation with the database semantic-convention attributes and varve.-prefixed ones where they have none; Varve.Store owns the Meter Varve.Store with the dataset as the dimension; neither references a package"
  - key: QueryTextOffByDefault
    statement: "db.query.text is recorded only when the host opts in; it is off by default because a query can carry data"
  - key: InstrumentationPerRequestNeverPerRow
    statement: "Every span is per request and every measurement per commit, per checkpoint or on observation, never per quad or per solution; an instrumented request allocates nothing per solution beyond 7a's figure"
  - key: ExporterInTheServerOnly
    statement: "OpenTelemetry, OpenTelemetry.Extensions.Hosting and OpenTelemetry.Exporter.OpenTelemetryProtocol are referenced by Varve.Server alone, configured by the standard OTEL_* variables, and off when no endpoint is configured; admitted on the condition that the Native AOT publish stays green"
  - key: TraceContextPropagatedNeverLogged
    statement: "W3C Trace Context is honoured and propagated and never written into the log; the join between a trace and its commits is the request span's varve.request_id and varve.position attributes"
  - key: LogsThroughILoggerWithScopes
    statement: "Logs go through ILogger with the request id and the position as scopes, exported by the server through the SDK"
---

The rulings of [ADR 0112](../adr/0112-telemetry-in-two-layers.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
