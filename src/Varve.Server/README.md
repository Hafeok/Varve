# Varve.Server

The Varve executable: the server, and the `varve` command line (ADR 0105).
Install it as a .NET tool, or run the container image
`ghcr.io/hafeok/varve`, which is the Native AOT build of the same executable
(`docs/operator/run.md`).

```sh
dotnet tool install --global Varve.Server --prerelease
varve create ./people
varve load ./people data.nq
varve query ./people -q 'SELECT (COUNT(*) AS ?n) WHERE { ?s ?p ?o }'
varve serve --anonymous --datasets-root "$PWD"   # development only
```

The server serves the datasets its management plane declares (ADR 0163);
declare `people`, the directory made above, with `varve apply people.trig
--server http://localhost:5000` or a `PUT` of its TriG to
`/manage/datasets/people`. It then answers at
`http://localhost:5000/datasets/people/sparql`.
`--anonymous` is for a developer's machine; a deployment takes OIDC bearer
tokens (`--authority`, `--audience`, ADR 0037).

A dataset is a directory on this machine, opened directly with no
authentication (the lease keeps a server and a command from sharing it), or
the URL of a dataset on a server, reached over HTTP with a bearer token.

| Command | Directory | URL | Does |
| --- | --- | --- | --- |
| `create` | yes | | A new dataset (file storage, the format of ADR 0073). |
| `info` | yes | yes | Head, projection, checkpoints, settings; on a server, a management call. |
| `load` | yes | | The bulk loader (ADR 0083) over N-Triples, N-Quads, Turtle or TriG. |
| `query` | yes | yes | A query; results as JSON, XML, CSV, TSV or N-Triples. |
| `update` | yes | yes | One update request, one commit. |
| `export` | yes | yes | The dataset as N-Quads or TriG, or one graph. |
| `checkpoint` | yes | yes | A checkpoint at the head or a position (ADR 0078); on a server, a management call. |
| `feed` | yes | yes | The change feed (ADR 0097), bounded or `--follow`. |
| `setup apply` | | yes | A dataset's data: its baseline and migrations (ADR 0130). |
| `apply` | | yes | Management declarations as TriG: plan, drift check, commit (ADR 0163). |
| `serve` | | | The server, with the configuration of `docs/operator/`. |

Against a server, a token comes from `--token` or `VARVE_TOKEN`, or from
OpenID Connect: `--authority` with `--client-id` and `--client-secret` (or
`VARVE_CLIENT_SECRET`) for client credentials, or `--authority` with
`--client-id` alone for the device code flow, which prints a URL and a code
to enter. A refresh token is kept in `~/.config/varve/credentials.json`
(mode 0600; DPAPI on Windows; `--no-store` keeps none, for CI). A
management call (`info` and `checkpoint` against a server, `apply`) takes a
token for the management audience; `apply` obtains one by workload identity
federation with `--client-assertion`, and never runs a device flow.

Exit codes: 0 done, 1 the command failed and said why on standard error,
2 a usage error.
