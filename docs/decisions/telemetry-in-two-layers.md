---
set: telemetry-in-two-layers
namespace: varve
adr: 0112
decisions:
  - key: InstrumentationIsBclOnly
    statement: "Varve.Protocol owns the ActivitySource Varve.Protocol, one activity per request named by its operation with the database semantic-convention attributes and varve.-prefixed ones where they have none; Varve.Store owns the Meter Varve.Store with the dataset as the dimension; neither references a package"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: QueryTextOffByDefault
    statement: "db.query.text is recorded only when the host opts in; it is off by default because a query can carry data"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: InstrumentationPerRequestNeverPerRow
    statement: "Every span is per request and every measurement per commit, per checkpoint or per maintenance step, never per quad or per solution and never on a callback that would hold the dataset; an instrumented request allocates nothing per solution beyond 7a's figure"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: ExporterInTheServerOnly
    statement: "OpenTelemetry, OpenTelemetry.Extensions.Hosting and OpenTelemetry.Exporter.OpenTelemetryProtocol are referenced by Varve.Server alone, configured by the standard OTEL_* variables, and off when no endpoint is configured; admitted on the condition that the Native AOT publish stays green"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: TraceContextPropagatedNeverLogged
    statement: "W3C Trace Context is honoured and propagated and never written into the log; the join between a trace and its commits is the request span's varve.request_id and varve.position attributes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: LogsThroughILoggerWithScopes
    statement: "Logs go through the ILogger the host gives the protocol: one line per commit with the dataset, the position and the request id, one per refusal with the status, and the request id in the hosting scope of every line; the server exports them through the SDK"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
---

The rulings of [ADR 0112](../adr/0112-telemetry-in-two-layers.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
