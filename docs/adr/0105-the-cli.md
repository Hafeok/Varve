# 0105 — The CLI `varve`: one executable with the server, embedded and remote modes, the credential file, `System.CommandLine`

## Status

**Accepted — filed unaccepted by milestone 7b of #11, 2026-10-08** (ADR 0066).
Decided by the maintainer on the 7b plan: "one executable, package id
`Varve.Server`, command `varve`"; "credential file as proposed, superseding
0037 point 8's storage clause in part. Add `--no-store` for CI use; document
that macOS file storage is weaker than Keychain and the token is revocable".
**Supersedes ADR [0037](0037-server-authentication.md) point 8 in part**: the
storage clause, "in the platform credential store". The flows and the
"nothing but the refresh token" stand. Acceptance is the maintainer's act on
the pull request.

**Revisit condition:** a second executable. If one is needed — a separate
server image without the commands, a tool without the server — the host
wiring both would share moves into a layer-5 `Varve.Hosting` package, and
this ADR's point 1 is superseded.

## Context

The brief names the CLI as the third host of the first milestone's three
(embedded, server, browser) and ADR 0060 puts every executable at layer 6,
expecting the CLI to "ship as a .NET tool". ADR 0037 decided its
authentication: none embedded, OIDC device code or client credentials against
a remote server, nothing stored but the refresh token, "in the platform
credential store".

Three things the plan found. A `serve` command that runs the 7a server cannot
live in a second layer-6 project, because a reference between two layer-6
projects is a same-layer violation (`DD0001`). The platform credential stores
— Windows Credential Manager, macOS Keychain, libsecret — have no managed API:
each needs P/Invoke or its command-line tool, and constraint 1 forbids the
first while the second is absent on most servers. And a Native AOT .NET tool
package is built per runtime identifier and cannot be cross-compiled on the
publish runner, so a tool package and an AOT binary are two artefacts.

## Decision

1. **One executable.** `Varve.Server` is the tool: package id `Varve.Server`,
   tool command `varve`. `varve serve` runs the host of ADR 0101 unchanged,
   and so does `varve` with no command, or with nothing but host
   configuration (`--Varve:…`, `--urls=…`, as the 7a host took), so
   `eng/server-smoke.cs`, the in-process tests and a container image's
   entry point need no change. One composition root
   (ADR 0060), one AOT publish, one package.
2. **The commands**, each taking a dataset **directory** or a dataset **URL**
   where both make sense:

   | Command | Does | Embedded | Remote |
   |---|---|:-:|:-:|
   | `create <dir>` | creates a file dataset | yes | — (ADR 0106's `PUT` is the admin call) |
   | `info <dir\|url>` | id, head, durability, settings, checkpoints, projection, as `/status`'s JSON | yes | `/status` |
   | `load <dir> <files…>` | the bulk loader (ADR 0081), `--graph` | yes | — |
   | `query <dir\|url>` | `--query`/`--file`, `--format json\|xml\|csv\|tsv\|nt\|ttl\|nq\|trig`, `--as-of`; a local graph result is N-Triples | yes | `/sparql` |
   | `update <dir\|url>` | `--update`/`--file`, `--if-match` | yes | `/sparql` |
   | `export <dir\|url>` | the dataset as N-Quads or TriG, `--graph` for one graph | yes | a query, written as quads |
   | `checkpoint <dir\|url>` | a checkpoint at the head or `--at` | yes | `/checkpoints` |
   | `feed <dir\|url>` | the change feed in the format of `change-feed.md`, `--from`, `--to`, `--graph`, `--follow` | yes | `/feed` |
   | `serve` | the server | — | — |

   - **Embedded mode** opens the directory with `FileStorage` directly, with
     no authentication (ADR 0037, point 6): the file permissions are the
     boundary and the lease (ADR 0075) refuses a directory a server holds.
     `SERVICE` and `LOAD` take the policy from `--allow-endpoint` and
     `--allow-source` flags, empty by default (ADR 0104).
   - **Remote mode** talks to a server through `Varve.Protocol.Client` (ADR
     0103) with a bearer token.
   - `load` is embedded only: the bulk loader holds the sequencer, which no
     protocol endpoint exposes. A remote load is a Graph Store `PUT` or
     `POST`, and `update` and the Graph Store calls of the client cover it.
   - **Streams and exit codes.** Results, quads and feed records go to
     standard output as bytes; a command's own words (`committed, position
     3`) go there as text; a failure is one line on standard error, `varve:
     …`. Exit `0` is done, `1` a command that ran and failed, `2` a usage
     error or, for `serve`, a configuration that does not validate (ADR
     0101).
3. **Remote authentication** is ADR 0037's: the **device code** flow for a
   person (`--authority`, `--client-id`; the CLI prints the verification URI
   and the code, polls the token endpoint, and keeps the refresh token), or
   **client credentials** for automation (`--client-id` with
   `--client-secret` or `VARVE_CLIENT_SECRET`), the OIDC discovery document
   read from the authority. The OIDC client is written over `HttpClient` and
   source-generated `System.Text.Json`: no package.
4. **The credential file supersedes "the platform credential store".** The
   refresh token, with the authority, the client id and the server it was
   obtained for, is kept in one file under the user's profile
   (`$XDG_CONFIG_HOME/varve/credentials.json`, `~/.config/varve/` by
   default; `%LOCALAPPDATA%\varve\` on Windows):
   - created with mode `0600` on Unix, and the CLI refuses to read one that
     is group- or world-readable;
   - protected with DPAPI on Windows through
     `System.Security.Cryptography.ProtectedData` (Microsoft, MIT, managed,
     `CurrentUser` scope), registered under this ADR in `Varve.Server` alone;
   - **on macOS the file is weaker than the Keychain**: it is readable by any
     process running as the user, where the Keychain prompts per
     application. The operator guide says so, and says what mitigates it: the
     token is a refresh token the issuer can revoke, it is scoped to the
     client id the operator registered, and `--no-store` keeps it out of
     the file altogether.
   - **`--no-store`** obtains a token and keeps nothing on disk: the mode for
     CI and for a shared machine. With it, every invocation authenticates
     again; client credentials make that cheap.
   - Access tokens are never written. Nothing but the refresh token and what
     identifies its issuer is written, as ADR 0037 requires.
5. **`System.CommandLine` 2.0.12** parses the command line: stable, MIT,
   Microsoft, no native asset, trimming- and AOT-annotated. Registered under
   this ADR in `Varve.Server` alone, on the same condition as `JwtBearer`
   (ADR 0099): the AOT publish stays green, or the parsing is written by hand
   over the BCL.
6. **Two artefacts.** The **tool package** is framework-dependent
   (`PackAsTool`, `ToolCommandName varve`), installed with
   `dotnet tool install -g Varve.Server`, packed by the existing pack job and
   checked by `eng/package-metadata.cs`. The **Native AOT single-file binary**
   is the `native aot` job's artefact on both runners, as 7a's was, and the
   job runs the CLI smoke against it: `create`, `update`, `query`, `export`,
   `checkpoint`, `feed`, then `serve` through `eng/server-smoke.cs`. The AOT
   publish is the gate; the package is what `dotnet tool` installs.
7. **The commands are the host's code**: `Varve.Server` gains a `Commands/`
   folder, and nothing moves out of it into a library. The embedded commands
   compose the store, the evaluator, the update executor, the bulk loader and
   the writers exactly as the smoke apps do (ADR 0060).

## Alternatives considered

- **A separate `Varve.Cli` project compiling the server's sources** by
  `<Compile Include>`: two executables from one text. Two composition roots
  to keep identical, two AOT publishes, and a package whose contents are
  another project's files. Rejected by the maintainer in favour of one
  executable with the revisit condition above.
- **A `Varve.Hosting` library at layer 5 now.** The honest shape for two
  executables; one executable does not pay for it. The revisit condition.
- **The OS keychains through their command-line tools** (`security`,
  `secret-tool`, `cmdkey`): no P/Invoke, and absent or different on every
  platform a CLI runs on, including every Linux server. Recorded as the
  alternative for a user who wants it; `--no-store` with an external token
  cache is the supported way to get it today.
- **A D-Bus Secret Service client in managed code** over the session socket.
  A package, or a protocol implementation, for one platform's keyring.
- **Hand-written parsing.** Nine commands, shared options, help, completion
  and response files are a few hundred lines that would exist to avoid a
  dependency a host may take under constraint 4.
- **A Native AOT tool package.** Per runtime identifier, so the publish
  runner would have to build every platform's binary, which AOT cannot do
  across platforms. The two-artefact decision is what the SDK supports.

## Consequences

- `Varve.Server` takes two packages: `System.CommandLine` and
  `System.Security.Cryptography.ProtectedData`, both in the register with
  this ADR, both in the native-asset gate's closure, neither in a library.
- ADR 0037's set loses `RemoteCliUsesBearerTokens` to this ADR's set, where
  it carries the new storage clause.
- The operator guide (`docs/operator/authenticate.md`) documents the flows,
  the file, the macOS caveat and `--no-store`.
- The 7a `native aot` job grows by the CLI smoke; its time is reported.

## Checks

- **Checked against the accepted ADRs** (0001–0101) and specification 1.6.
  Touches:
  - **0037**: point 8's storage clause superseded in part; the flows,
    anonymous mode and "nothing but the refresh token" unchanged; point 6's
    embedded CLI unchanged;
  - **0060**: one executable is one composition root; the revisit condition
    added;
  - **0075**: the lease is the embedded mode's guard;
  - **0081**: `load` is the bulk loader;
  - **0091** and **0101**: the server unchanged inside `serve`;
  - **0099**: the register pattern and the AOT condition reused.

  No conflict beyond the stated partial supersession.
- **Layer ownership.** `Varve.Server`, layer 6.
- **Analyzer rule.** None. The register gate and `eng/native-assets.cs`
  check the packages.
- **Open questions owned.** None.
