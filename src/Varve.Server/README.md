# Varve.Server

The Varve executable: the server, and the `varve` command line (ADR 0104).
Install it as a .NET tool, or take the native single file from a release.

```sh
dotnet tool install --global Varve.Server
varve create ./people
varve load ./people data.nq
varve query ./people -q 'SELECT (COUNT(*) AS ?n) WHERE { ?s ?p ?o }'
varve serve --Varve:DatasetsRoot=/var/lib/varve
```

A dataset is a directory on this machine, opened directly with no
authentication (the lease keeps a server and a command from sharing it), or
the URL of a dataset on a server, reached over HTTP with a bearer token.

| Command | Directory | URL | Does |
| --- | --- | --- | --- |
| `create` | yes | | A new dataset (file storage, the format of ADR 0073). |
| `info` | yes | yes | Head, projection, checkpoints, settings. |
| `load` | yes | | The bulk loader (ADR 0083) over N-Triples, N-Quads, Turtle or TriG. |
| `query` | yes | yes | A query; results as JSON, XML, CSV, TSV or N-Triples. |
| `update` | yes | yes | One update request, one commit. |
| `export` | yes | yes | The dataset as N-Quads or TriG, or one graph. |
| `checkpoint` | yes | yes | A checkpoint at the head or a position (ADR 0078). |
| `feed` | yes | yes | The change feed (ADR 0097), bounded or `--follow`. |
| `serve` | | | The server, with the configuration of `docs/operator/`. |

Against a server, a token comes from `--token` or `VARVE_TOKEN`, or from
OpenID Connect: `--authority` with `--client-id` and `--client-secret` (or
`VARVE_CLIENT_SECRET`) for client credentials, or `--authority` with
`--client-id` alone for the device code flow, which prints a URL and a code
to enter. A refresh token is kept in `~/.config/varve/credentials.json`
(mode 0600; DPAPI on Windows; `--no-store` keeps none, for CI).

Exit codes: 0 done, 1 the command failed and said why on standard error,
2 a usage error.
