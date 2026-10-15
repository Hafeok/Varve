# Operating Varve

The operator's guide to `Varve.Server`: one executable that is the server and
the `varve` command line (ADR 0105). Eight pages, each a task:

| Page | What it covers |
|---|---|
| [Run](run.md) | installing the executable, starting the server, what it listens on, liveness and readiness, stopping it |
| [Configure](configure.md) | every setting under `Varve:`, with its default and what refuses to start |
| [Authenticate](authenticate.md) | OIDC bearer tokens, claims to permissions, graph-scoped grants, on-behalf-of and the attribution rule, anonymous mode, and how the command line gets a token |
| [Datasets](datasets.md) | where a dataset's boundary goes, and a setup an application applies on every start: `AddVarve`, `varve setup apply`, `WithDataset`, the setup file |
| [Integrate](integrate.md) | the commits resource as the stream other systems follow, `FollowAsync` and its checkpoint, at least once |
| [Observe](observe.md) | the OTLP exporter and the `OTEL_*` variables, every span and metric by name, the log events, what it costs |
| [Back up and restore](backup-and-restore.md) | what the files are, taking a copy, checkpoints, restoring, and the change feed as an incremental copy |
| [Upgrade](upgrade.md) | what a new build reads, what it rewrites, and the order of a rolling upgrade |

Every refusal the server answers is an RFC 9457 problem of a type listed in
[`docs/problems/`](../problems/README.md), one page per type with its status
and its members (ADR 0119). Every term the service description coins under
`https://w3id.org/varve/ns#` is in [`docs/vocabulary.md`](../vocabulary.md).

The decisions behind the pages are ADRs 0101 (the host), 0105 (the command
line), 0106 (the admin API), 0107 (graph-level authorisation), 0104 (`SERVICE`
and `LOAD` over HTTP), 0112 (telemetry), 0037, 0100 and 0127 (authentication
and on-behalf-of), 0128 and 0129 (the feed's format and following it), 0130
and 0131 (setup), and 0132 (CORS). The protocol a
client speaks is in `src/Varve.Protocol/README.md` and
`docs/spec/change-feed.md`.
