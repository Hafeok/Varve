# Operating Varve

The operator's guide to `Varve.Server`: one executable that is the server and
the `varve` command line (ADR 0104). Five pages, each a task:

| Page | What it covers |
|---|---|
| [Run](run.md) | installing the executable, starting the server, what it listens on, liveness and readiness, stopping it |
| [Configure](configure.md) | every setting under `Varve:`, with its default and what refuses to start |
| [Authenticate](authenticate.md) | OIDC bearer tokens, claims to permissions, graph-scoped grants, anonymous mode, and how the command line gets a token |
| [Back up and restore](backup-and-restore.md) | what the files are, taking a copy, checkpoints, restoring, and the change feed as an incremental copy |
| [Upgrade](upgrade.md) | what a new build reads, what it rewrites, and the order of a rolling upgrade |

The decisions behind the pages are ADRs 0101 (the host), 0104 (the command
line), 0105 (the admin API), 0106 (graph-level authorisation), 0103 (`SERVICE`
and `LOAD` over HTTP), and 0037 and 0100 (authentication). The protocol a
client speaks is in `src/Varve.Protocol/README.md` and
`docs/spec/change-feed.md`.
