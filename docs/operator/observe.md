# Observe

What the server tells you about itself, and how to get it out (ADR 0112).
Two layers: the libraries instrument through the BCL's `ActivitySource` and
`Meter`, which cost nothing until something listens; the server carries the
OpenTelemetry SDK and exports over OTLP when, and only when, you give it an
endpoint. Logs go through `ILogger` as in any .NET host.

## Turning the exporter on

The exporter is configured by the standard `OTEL_*` environment variables,
read by the SDK itself. Nothing is built, and nothing is sent, until one of
the endpoint variables is set.

| Variable | What it does |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | the collector, e.g. `http://otel-collector:4318`; also `_TRACES_ENDPOINT`, `_METRICS_ENDPOINT`, `_LOGS_ENDPOINT` per signal |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (the default, port 4317) or `http/protobuf` (port 4318) |
| `OTEL_EXPORTER_OTLP_HEADERS` | `key=value,…` sent with every export, for a collector that wants a token |
| `OTEL_SERVICE_NAME` | `service.name`; `varve` when unset |
| `OTEL_RESOURCE_ATTRIBUTES` | `key=value,…` on every signal: `deployment.environment=prod,service.instance.id=…` |
| `OTEL_METRIC_EXPORT_INTERVAL` | milliseconds between metric exports; 60000 by default |
| `OTEL_TRACES_SAMPLER` | `parentbased_always_on` by default; `parentbased_traceidratio` with `OTEL_TRACES_SAMPLER_ARG=0.1` keeps a tenth |

One setting of Varve's own: `Varve:Telemetry:QueryText` (`false`). When
`true`, every request span carries `db.query.text`, the query or update as
sent. It is off because a query can carry data, and a trace store is rarely
governed like the dataset.

A collector without a backend, to see what arrives:

<!-- ci: none — the collector image is Docker Hub's, which CI does not pull (#118); TelemetryTests (ci.yml build) runs the exporter against an in-process OTLP receiver -->
```yaml
# otel-collector.yaml — the debug exporter prints every signal to stdout.
receivers:
  otlp:
    protocols:
      http:
        endpoint: 0.0.0.0:4318
exporters:
  debug:
    verbosity: detailed
service:
  pipelines:
    traces:  { receivers: [otlp], exporters: [debug] }
    metrics: { receivers: [otlp], exporters: [debug] }
    logs:    { receivers: [otlp], exporters: [debug] }
```

<!-- ci: none — the collector image is Docker Hub's, which CI does not pull (#118); TelemetryTests (ci.yml build) sets these variables against an in-process receiver -->
```sh
docker run --rm -p 4318:4318 -v "$PWD/otel-collector.yaml:/etc/otelcol/config.yaml" otel/opentelemetry-collector:latest
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318 OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf \
  varve serve --anonymous
```

## Traces

One span per request from the source `Varve.Protocol`, a child of ASP.NET
Core's request span (source `Microsoft.AspNetCore`, exported too). The span
is named by the operation once it is known, and carries the OpenTelemetry
database conventions where they have an attribute and `varve.`-prefixed ones
where they do not.

| Span name | The request |
|---|---|
| `SELECT`, `CONSTRUCT`, `ASK`, `DESCRIBE` | a query, by its form |
| `UPDATE` | an update |
| `GET graph`, `HEAD graph`, `PUT graph`, `POST graph`, `DELETE graph` | the Graph Store Protocol |
| `commits`, `commit` | the commits resource, a range or one position |
| `diff`, `service description`, `access` | the dataset's other resources |
| `events`, `event types`, `event schema`, `asyncapi` | a dataset's domain events (ADRs 0143–0145) |
| `server event types` | `GET /events/types`, every dataset's event types |
| `datasets`, `dataset declaration`, `settings`, `state`, `status`, `checkpoints`, `grants`, `binding`, `event declarations`, `event declarations move`, `fleet declaration`, `management graphs`, `apply`, `system` | the management plane (ADR 0163) |
| `sparql`, `graph` | the name before the operation was known: a request refused before parsing |

| Attribute | Value |
|---|---|
| `db.system.name` | `varve` |
| `db.namespace` | the dataset's name |
| `db.operation.name` | the span's name |
| `db.query.text` | the query or update, only with `Varve:Telemetry:QueryText` |
| `db.response.returned_rows` | solutions written, for a `SELECT` |
| `varve.request_id` | the request id: the `Varve-Request-Id` header, a problem's `instance`, and the `cause` of a commit the request made |
| `varve.position` | the position the response describes: the view's for a read, the committed one for a write |
| `varve.as_of` | the `Varve-As-Of` selector as sent, when there was one |
| `varve.azp` | the application that acted for the caller: the token's `Auth:ApplicationClaim` (`azp`), for a token exchanged on a user's behalf as for an application's own (ADR 0127); never in the log or the logger |
| `varve.request.aborted` | `true` on a read the client ended before the server finished it: a cancel, a closed tab, or the explorer's 5,000-row ceiling (ADR 0112, amended 2026-11-20); absent otherwise |
| `http.response.status_code` | the status; the span's status is an error at `5xx` |

W3C Trace Context is honoured: a `traceparent` on the request makes the span
part of the caller's trace. A write forwarded to its writer (ADR 0168) and an
event move's call to the dataset's writer carry `traceparent` and
`tracestate` on explicitly. `SERVICE` and `LOAD` go through .NET's
`HttpClient`, whose default propagator carries the current trace; no test
covers that. The trace id is never written into the log:
a commit's `cause` is the request id, which the server mints, and the join
between a trace and the commits it caused is `varve.request_id` and
`varve.position` on the span (ADR 0094, amended).

## Metrics

The meter `Varve.Store`, every measurement tagged `db.namespace` with the
dataset's name. Nothing is measured per quad or per solution: a measurement
is made per commit, per checkpoint or per maintenance step, and the gauges
are the value as of the last of those.

| Instrument | Kind | Unit | What it measures |
|---|---|---|---|
| `varve.store.commit.duration` | histogram | s | a commit, from entering the sequencer's queue to its outcome; `varve.outcome` is `committed`, `no_change`, `conflict`, `rejected` or `unavailable` |
| `varve.store.sequencer.queue_depth` | up-down counter | {commit} | commits waiting for the sequencer |
| `varve.store.commit.batch_size` | histogram | {commit} | the commits one write of the log made durable, with one flush (ADR 0124) |
| `varve.store.pinned_reads` | up-down counter | {read} | views pinned and not yet released: open reads, live tails' bases |
| `varve.store.projection.lag` | gauge | {position} | how far the default projection is behind the head; readiness tolerates `Varve:Health:ReadyLag` of it |
| `varve.store.checkpoint.duration` | histogram | s | a checkpoint, written and read back |
| `varve.store.log.bytes` | gauge | By | the bytes of every commit's records in the log |
| `varve.store.derived.bytes` | gauge | By | the bytes of the runs and checkpoints held open |
| `varve.store.seek_directories.bytes` | gauge | By | the bytes of extent sets' seek directories kept in memory for as-of reads, at most `Varve:Store:SeekDirectoryCacheBytes` (ADR 0240). At the ceiling while as-of reads are slow, the cache is too small for the history they read |
| `varve.store.derived.damaged` | counter | {blob} | derived blobs found damaged as they were read: a block that did not match its check (ADR 0213). Each was put aside and its part of the answer re-run from the log, so no read failed or answered differently; the writer's next round writes a damaged set again. A count that keeps rising is the disk or the store under `derived/` failing |

The server also exports ASP.NET Core's and the runtime's meters:
`Microsoft.AspNetCore.Hosting` (request duration and active requests),
`Microsoft.AspNetCore.Server.Kestrel` (connections),
`Microsoft.AspNetCore.RateLimiting` (the `reads` and `health` policies'
queue and rejections, ADR 0114), `System.Net.Http` (outbound `SERVICE` and
`LOAD`) and `System.Runtime` (GC, thread pool, working set).

## Logs

`ILogger`, as any .NET host: the console by default, with
`Logging:LogLevel:*` as the configuration, and the OTLP log exporter beside
it when the exporter is on. The protocol logs three events, category
`Varve.Protocol`, at Information:

- a commit: `{Operation} on '{Dataset}' committed position {Position} for request {RequestId}`;
- a refusal: `{Method} {Path} answered {Status} for request {RequestId}`;
- a read the client ended: `{Method} {Path} ended by the client before its response did, for request {RequestId}`.

ASP.NET Core's hosting scope carries `RequestId` and `RequestPath` for each
line a request writes, but neither the console formatter nor the OTLP log
exporter writes scopes by default; for the console,
`Logging:Console:FormatterOptions:IncludeScopes` turns them on.

The server's own category, `Varve.Server`, logs these events, by id:

| Id | Level | What |
|---|---|---|
| 1 | Warning | the server starts in anonymous mode |
| 2 | Warning | a dataset is leased elsewhere, and the server waits for it (ADR 0116) |
| 3 | Information | a dataset is placed on another instance, and this one released its epoch |
| 4 | Warning | a dataset is no longer declared, and its writer deleted its storage |
| 5, 6 | Warning | a dataset did not take its declared settings, or did not open in its new role; tried again |
| 7 | Information | a follower reopens, having applied more than its memtable limit |
| 8, 9 | Information | a standby claimed a dataset in place, or another member claimed it first (ADR 0180) |
| 10 | Warning | neither `DatasetsRoot` nor `ObjectStorage` is set: the system dataset is in memory |
| 11 | Information | bootstrap operators recorded |
| 12 | Error | this instance is superseded as the system dataset's writer |
| 13 | Warning | advancing a dataset failed; it serves what it applied |
| 14 | Information | an event move resumed |
| 15 | Warning | the system dataset's newer writer holds it, and this instance rejoins as a standby; **also** a dataset's standby did not claim it in place (#300) |
| 16 | Information / Warning | a standby claimed the system dataset in place; **also** a dataset's standby did not advance (#300) |
| 17 | Warning | a newer writer holds a dataset, and this instance rejoins its writer set |
| 18 | Warning | a batch failed against the store, and the writer released its epoch (`storage-error`) |
| 19 | Error | a memtable's spills to the local disk fail (`local-disk-error`) |
| 20, 21 | Warning, Debug | a dataset did not open as its writer, and is tried again |
| 22 | Warning | a dataset's idempotency keys were not read ahead (ADR 0246) |

Ids 15 and 16 each name two events until #300 is fixed: tell them apart by
the message.

## What it costs

Nothing until something listens. Without an endpoint the SDK is not built;
the `ActivitySource` answers no listener in a flag test and the request runs
as it would; a `Meter` instrument with no listener is a flag test per
commit. With a listener a request allocates its span and its attributes,
once, and nothing per solution: the allocation test's traced reading is the
same 56 bytes a solution as the untraced one.

## Not run in CI

What on this page no CI job runs, and why (ADR 0248):

- **Turning the exporter on.** The collector image is Docker Hub's, which CI does not pull (#118); TelemetryTests (ci.yml build) runs the exporter against an in-process OTLP receiver.
- **Turning the exporter on.** The collector image is Docker Hub's, which CI does not pull (#118); TelemetryTests (ci.yml build) sets these variables against an in-process receiver.
