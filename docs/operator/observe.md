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

```sh
docker run --rm -p 4318:4318 -v "$PWD/otel-collector.yaml:/etc/otelcol/config.yaml" otel/opentelemetry-collector:latest
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318 OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf \
  varve serve --anonymous --dataset scratch=Memory
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
| `diff`, `status`, `service description`, `settings`, `checkpoints` | the dataset's other resources |
| `datasets`, `create dataset`, `delete dataset`, `state` | the admin API |
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
| `http.response.status_code` | the status; the span's status is an error at `5xx` |

W3C Trace Context is honoured: a `traceparent` on the request makes the span
part of the caller's trace, and the server's own outbound requests
(`SERVICE`, `LOAD`) carry it on. The trace id is never written into the log:
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
| `varve.store.pinned_reads` | up-down counter | {read} | views pinned and not yet released: open reads, live tails' bases |
| `varve.store.projection.lag` | gauge | {position} | how far the default projection is behind the head; readiness tolerates `Varve:Health:ReadyLag` of it |
| `varve.store.checkpoint.duration` | histogram | s | a checkpoint, written and read back |
| `varve.store.log.bytes` | gauge | By | the bytes of every commit's records in the log |
| `varve.store.derived.bytes` | gauge | By | the bytes of the runs and checkpoints held open |

The server also exports ASP.NET Core's and the runtime's meters:
`Microsoft.AspNetCore.Hosting` (request duration and active requests),
`Microsoft.AspNetCore.Server.Kestrel` (connections),
`Microsoft.AspNetCore.RateLimiting` (the `reads` and `health` policies'
queue and rejections, ADR 0114), `System.Net.Http` (outbound `SERVICE` and
`LOAD`) and `System.Runtime` (GC, thread pool, working set).

## Logs

`ILogger`, as any .NET host: the console by default, with
`Logging:LogLevel:*` as the configuration, and the OTLP log exporter beside
it when the exporter is on. The protocol logs two events, category
`Varve.Protocol`, at Information:

- a commit: `{Operation} on '{Dataset}' committed position {Position} for request {RequestId}`;
- a refusal: `{Method} {Path} answered {Status} for request {RequestId}`.

ASP.NET Core's hosting scope puts `RequestId` and `RequestPath` on every
line a request writes. The server's own category, `Varve.Server`, logs the
anonymous-mode warning at start, a lease it is waiting for (ADR 0116), and a
dataset it could not open.

## What it costs

Nothing until something listens. Without an endpoint the SDK is not built;
the `ActivitySource` answers no listener in a flag test and the request runs
as it would; a `Meter` instrument with no listener is a flag test per
commit. With a listener a request allocates its span and its attributes,
once, and nothing per solution: the allocation test's traced reading is the
same 56 bytes a solution as the untraced one.
