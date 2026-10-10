# 0112 — Telemetry in two layers: the BCL instruments, the server exports

## Status

**Accepted — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Admits
three packages to the register, in `Varve.Server` alone, on the condition
that the server's Native AOT publish stays green (as ADR 0099 admitted
`JwtBearer`).

## Context

The roadmap asks for traces, metrics and logs to the OpenTelemetry semantic
conventions for a database server. Constraint 4 says every package needs an
ADR, and the libraries at layers 4 and 5 are referenced by hosts that may
have no exporter at all: an embedded store does not want the OpenTelemetry
SDK in its closure. The BCL has had the instrumentation half of OpenTelemetry
since .NET 6: `System.Diagnostics.ActivitySource` and
`System.Diagnostics.Metrics.Meter` cost nothing when no listener is attached
and are what the SDK listens to.

## Decision

1. **Instrumentation is BCL only, in the libraries.**
   - **`Varve.Protocol`** owns the `ActivitySource` `Varve.Protocol`: one
     activity per request, named by the operation (`SELECT`, `CONSTRUCT`,
     `ASK`, `DESCRIBE`, `UPDATE`, `GET graph`, `PUT graph`, `POST graph`,
     `DELETE graph`, `commits`, `diff`, `status`, …), a child of ASP.NET
     Core's own request activity. Its attributes follow the semantic
     conventions for database client and server spans:
     `db.system.name = varve`, `db.namespace` = the dataset name,
     `db.operation.name`, `db.response.returned_rows` for a solution
     result, and `db.query.text` **only when the host opts in**
     (`ProtocolOptions.Telemetry.QueryText`, off by default: a query can
     carry data). Where the conventions have no attribute for a log-first
     store, the attribute is `varve.`-prefixed: `varve.request_id` (the
     request id that is also the commit's cause), `varve.position` (the
     position the response describes, the committed position for a write),
     `varve.as_of` (the selector as sent). `http.response.status_code` closes
     the span, whose status is an error at `5xx`. The operator guide lists
     them.
   - **`Varve.Store`** owns the `Meter` `Varve.Store`, one per process, every
     instrument with `db.namespace` as its dimension, which the host supplies
     when it opens a dataset (`DatasetOptions.Name`; the id otherwise):
     `varve.store.commit.duration` (histogram, seconds, with
     `varve.outcome`: `committed`, `no_change`, `conflict`, `rejected`,
     `unavailable`), `varve.store.sequencer.queue_depth` (up-down counter),
     `varve.store.pinned_reads` (up-down counter),
     `varve.store.projection.lag` (gauge, positions),
     `varve.store.checkpoint.duration` (histogram, seconds),
     `varve.store.log.bytes` and `varve.store.derived.bytes` (gauges). Every
     measurement is per commit, per checkpoint or per maintenance step, never
     per quad: the hot path is untouched and the allocation tests say so. The
     gauges are recorded at those moments rather than observed on a callback,
     because a callback holds its dataset for the life of the meter, which is
     the process's, and a dataset a host abandoned without disposing would
     never be collected.
   - **Logs** go through the `ILogger` the host gives the protocol
     (`ProtocolOptions.Logger`): one line per commit with the operation, the
     dataset, the position and the request id, one per refusal with the
     status and the request id, and ASP.NET Core's hosting scope puts the
     request id on every line a request writes. Nothing is logged per
     solution.
2. **The exporter lives in `Varve.Server` alone**: `OpenTelemetry`,
   `OpenTelemetry.Extensions.Hosting` and
   `OpenTelemetry.Exporter.OpenTelemetryProtocol`, all 1.19.1 (resolved from
   nuget.org, `Adr="0112"`), configured by the standard `OTEL_*` environment
   variables (`OTEL_EXPORTER_OTLP_ENDPOINT`, `_HEADERS`, `_PROTOCOL`,
   `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`) and **off when no endpoint
   is configured**: the SDK is not even built then, and the activities and
   meters cost what an unlistened source costs. The server subscribes the
   SDK to `Varve.Protocol`, `Varve.Store`, ASP.NET Core's and the runtime's
   sources, and routes `ILogger` through the SDK's log exporter.
3. **W3C Trace Context is honoured and propagated** (ASP.NET Core does it;
   the activity inherits the incoming `traceparent`) and **never written into
   the log**: the commit's `cause` stays the server-minted request id (ADR
   0094, amended), because a trace id is client-chosen, so forgeable and
   collidable, and spans several requests. The join between a trace and its
   commits lives in telemetry: the request span carries `varve.request_id`
   and `varve.position`.
4. **Tested in process**: a test host attaches an `ActivityListener` and a
   `MeterListener` and asserts the span and metric names, the dataset
   dimension, and that `db.query.text` is absent unless opted in; an
   allocation test shows an instrumented request allocating nothing per
   solution beyond 7a's figure.

## Alternatives considered

- **The OpenTelemetry SDK in `Varve.Protocol`.** Every host would carry the
  SDK and its exporter whether it exports or not, and constraint 4 would
  have the libraries depend on a package for what the BCL already does.
- **`Microsoft.Extensions.Diagnostics.HealthChecks`-style abstractions for
  metrics.** The BCL's `Meter` is the abstraction; the SDK is one listener.
- **The trace id as the commit's cause.** Rejected in point 3; it would make
  provenance depend on a value the caller chose.
- **The host's `IMeterFactory`, or a meter per dataset.** A factory's meters
  live as long as the factory, and a meter of our own is registered
  process-wide until disposed; either keeps an abandoned dataset's
  instruments, and through them the dataset, alive. One meter per process
  with the dataset as a tag costs nothing to abandon, and is what the
  conventions expect.

## Consequences

- A `Varve.Protocol` host that wants telemetry attaches a listener; a host
  that does not pays nothing.
- `Varve.Store`'s `DatasetOptions` gains `Name`, the dimension; `Dataset`
  exposes nothing new. `Varve.Protocol`'s `ProtocolOptions` gains
  `Telemetry` (the opt-in) and `Logger`; `TelemetryOptions` names the source
  and the meter.
- `docs/operator/observe.md` lists every span and metric by name and shows
  the OTLP collector's debug exporter as the Grafana-free example.
- The register grows by three lines, each conditional on the AOT gate, and
  `eng/native-assets.cs` confirms none ships a native asset.

## Checks

- **Checked against the accepted ADRs** (0001–0109) and specification 1.6.
  Touches **0009** (the register), **0056** (the clock stays injected; a
  measurement reads `Stopwatch`), **0064** (`[HotPath]`: nothing marked is
  instrumented), **0094** (the cause). No conflict.
- **Layer ownership.** Sources in `Varve.Protocol` (5) and `Varve.Store`
  (4); the SDK in `Varve.Server` (6).
- **Analyzer rule.** None.
- **Open questions owned.** None.
