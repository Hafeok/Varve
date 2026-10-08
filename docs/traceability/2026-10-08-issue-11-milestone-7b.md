# Milestone 7b — federation, `LOAD`, the CLI, the admin API, graph-level authorisation, the loader on workers, term filters

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#11](https://github.com/Hafeok/Varve/issues/11) |
| **Date** | 2026-10-08 |
| **Tool** | Claude Code 2.1.294, a cloud session started from the desktop app |
| **Model** | `claude-fable-5-1`, configured and served for every turn, from the session's own metadata (`get_session`: `configured_model`, `session_context.model` and `last_served_model` all `claude-fable-5-1`) |
| **Session identifier** | `session_01BTAMTgvpiWwJZKFGbXdmto` |
| **Branch** | `claude/relaxed-newton-9rvd9m`, from `main` at 510ea64 |
| **Commits** | 68d3569, 48e91ca, b3325cf, 53b4a73, b68ab19, aa75ade, 84813d7, fa38aac, 3242096, 79cfbc5, and the closing commits carrying this record and the release |

The session ran out of context once and continued from a summary; the work
and this record are continuous across it. **The ADRs were filed as 0102–0108
and renumbered 0103–0109 at close-out**: the release-descriptor session (#73)
landed on `main` while this one ran and took 0102. The brief, the plan and
the approval below quote the original numbers, as do the commit messages up
to the renumbering commit; everything else in the tree uses the final ones. No .NET SDK was on the machine; the
session installed 10.0.401 under its home directory and worked from there.

## The prompts

### The brief

> Session 7b: federation, LOAD, the CLI, performance items, release
> 7a is merged. 7b completes milestone 7 and ends in the milestone's release. Read the 7a traceability record and ADRs 0091–0101 before planning; plan first, wait for approval; one PR, red only on `CS0618`.
> This session runs in parallel with a separate releases-as-code PR (release descriptors under `releases/`, a superseding ADR for 0085, changes to `eng/changelog.cs`, `eng/release-pending.cs`, the release workflow and AGENTS.md's release section). Do not touch any of those files, or CHANGELOG.md, before close-out. At close-out, merge `main` first; if that PR has merged, write `releases/v0.1.0-preview.2.yaml` under its ADR; if not, use the ADR 0085 fallback in item 7 and say so in the report. Once that PR is on `main`, `release-pending` is a gate: a PR that closes the milestone issue goes red until it carries a descriptor. That is the intended signal; add the descriptor, do not touch the gate.
>
> 0. Two items carried from the 7a report, done first and each its own commit.
>    * (a) ADR 0057, dated amendment. A request that evaluates no pattern (`INSERT DATA`, `DELETE DATA`, Graph Store `PUT`/`POST`/`DELETE`) submits with no expected position unless the client sends `If-Match`, which is always honoured; a request that evaluates a pattern keeps the check unchanged. Reason, in the amendment: the expected position protects a delta computed against a pinned state, and a data-only request read no state; the sequencer normalises its delta against the head it meets (I2), so the commit is the same at any head. Property: a data-only request submitted against two histories that differ only in the contended tail yields the same effective delta. Re-run the eight-writer rows of the 7a write benchmark after it and report the 409 count before and after.
>    * (b) DecisionDriven.Analyzers preview.7 bump. The `;`-separated `ArchContractTypeAssemblies` was read up to the first separator, so DD0010 has checked the layer-3 and layer-5 projects against `Varve.Rdf` only. Bump, build, and sort every new finding the usual way: fix the signature or cite a decision. No suppressions, no downgrades. Report the count of new findings per package.
>    * (c) Allocation meter. The shared meter every allocation test uses becomes the minimum of N measurements for both the baseline and the candidate (a pool miss only adds bytes, so the minimum is the figure that repeats), and the "two rounds agree" loop is removed. N is a constant in the meter with the reason beside it. Re-run every allocation test on all three CI platforms; `Varve.Sparql.Tests.AllocationTests.parsing_allocates_the_tree_and_nothing_else` is the case that motivated it (7d43295 and the 7a Windows flake).
> 1. `SERVICE` over HTTP: the `IServiceHandler` implementation in `Varve.Protocol.Client` (layer 5) using `HttpClient`, SPARQL Protocol compliant, result parsing through `Varve.Sparql.Results` readers, `SILENT` per Federated Query, timeouts and size caps from configuration, an allow-list of endpoints (default: none; the operator opts in), and the W3C `sparql11/service` evaluation suite run end to end against an in-process second server. Record the SSRF considerations in the ADR: no loopback or private ranges unless explicitly allowed.
> 2. `LOAD` over HTTP: the `ILoadSource` implementation with the same client, content negotiation, size cap, allow-list; `update` suite cases that need it, if any, now pass without the test handler.
> 3. The CLI as a dotnet tool, `varve`: `create`, `info`, `load` (the bulk loader from 6c), `query`, `update`, `export`, `checkpoint`, `feed` (tail a local or remote dataset), `serve` (the server from an embedded host). Embedded mode opens the dataset directory directly with no authentication (the lease prevents clashes with a running server); remote mode authenticates with OIDC device code or client credentials per the authentication ADR, storing only the refresh token in the platform credential store. `System.CommandLine` or hand-written parsing: evaluate against constraint 4 and AOT, decide, ADR, register. AOT single-file publish is a gate.
> 4. Admin API in `Varve.Protocol`: dataset create, delete (refused while open), list, settings commits (`Settings` kind), checkpoint on demand, projection status (position, lag, failed state); all `admin` permission.
> 5. Graph-level authorisation. Decided; write the ADR and build it.
>    * A permission grant is scoped: `(dataset, permission, graph-set)` where the graph set is `all`, an explicit list of graph IRIs, or an IRI prefix set; `default` names the default graph. The 7a `read`/`write`/`admin` per dataset becomes the `all` case, so existing configuration and claim mappings are unchanged in meaning. Mapping from claims to scoped grants lives in the host configuration (`Varve.Server`), exactly where 7a put the unscoped mapping; `Varve.Protocol` receives the resolved grant set per request and knows nothing about tokens.
>    * Reads: the request's quad source is `GraphScopedQuadSource(pinned, readable-graphs)`, a layer-1 wrapper over the quad source contract (same family as the overlay, ADR 0036) that filters by graph on every pattern and on graph enumeration, so a user without `read` on graph `G` cannot observe `G`'s existence through `GRAPH ?g`, the service description, GSP `GET`, or the feed. `FROM`/`FROM NAMED` naming an unreadable graph is treated as naming an empty graph, not an error (no leak through error text). The feed and diff endpoints filter commit deltas per graph and drop commits that become empty; `Settings` and `Erasure` commits are visible only to `admin`.
>    * Writes: the effective delta is computed as today, then every quad in it is checked against the writable set before submission; one unwritable quad fails the whole request with `403` and a problem body naming the graph, and nothing is committed. A `DELETE WHERE`/`DELETE … INSERT … WHERE` evaluates its pattern over the readable scope, so a user cannot delete what they cannot read. GSP `PUT`/`DELETE` on an unwritable graph is `403`; on an unreadable one it is `404`, same as a missing graph.
>    * Admin is dataset-wide; there is no graph-scoped admin.
>    * `[HotPath]` on the filter; measured: a request with `all` scope must allocate and cost the same as before (the wrapper is skipped, not a no-op layer), and a scoped request's overhead is reported per pattern evaluation.
>    * Tests: every W3C query and update case re-run through the protocol with an `all` grant equals the unscoped run; a property that for any dataset, scope `S`, and query `Q`, the scoped result equals `Q` evaluated over the sub-dataset containing only `S`'s graphs; a property that no response (body, headers, problem details, service description, feed) to a user without `read` on `G` contains any IRI that occurs only in `G`; the 7a auth tests extended with a graph-scoped user.
>    * Record the boundary in the ADR: graph-level is what the store's model gives for free (graphs are in the index key); row-level (per-subject or per-classifier) access is milestone 9 alongside erasure, where the classifier already assigns quads to classes, and is not approximated here.
> 6. Performance items from 6c: the multi-threaded bulk resolve and per-run term filters, each with a before-and-after benchmark and the one-hour soak re-run on the final code (the gate is still judged on the operability milestone's host configuration; report under the default runtime for comparison).
> 7. Close-out and release: service description updated for 7b's features; the operator guide started (`docs/operator/`: run, configure, authenticate, back up and restore, upgrade), since the server now exists; AGENTS.md and the roadmap updated; `releases/v0.1.0-preview.2.yaml` added to the PR per the releases-as-code ADR (basis: the milestone issue, ADRs 0091 onward, the 0057 amendment, `storage-format: 1`); merging cuts the release and `changelog.cs` folds the notes. If that ADR is not yet merged, fall back to ADR 0085: changelog cut and the maintainer tags.
>
> Report as 7a, plus what the operability milestone needs from the maintainer: the container registries (GHCR and Docker Hub are decided), the GC and host configuration that judges #61, and the Aspire integration's package placement.

### The plan, as submitted

> The context is read and the toolchain is in place: no .NET SDK was on this machine, so I installed 10.0.401 under the home directory, initialised the W3C submodules, restored, and a baseline Release build is running in the background. Here is the plan for approval.
>
> ## Conflicts and decisions needed before I build
>
> 1. **`LOAD` over HTTP crosses a same-layer line.** `ILoadSource` lives in `Varve.Sparql.Store` (layer 5) and the brief puts its implementation in `Varve.Protocol.Client` (layer 5), which `DD0001` forbids. Recommendation: the client exposes `RdfDocumentClient.FetchAsync(iri)` (content negotiation, size cap, allow-list, the document's syntax and base), and each host binds `ILoadSource` to it in one line, as the hosts already bind `ISparqlUpdateExecutor`. `IServiceHandler` is layer 3, so `HttpServiceHandler` implements it directly. This is not ADR 0091's "third integration reached from protocol code" (the direction is reversed), and I will say so in the ADR.
>
> 2. **`serve` from the CLI, and the server, cannot be two layer-6 projects.** A reference between them is a same-layer violation. Recommendation: one executable. `Varve.Server` gains the commands and is the tool `varve`; `serve` runs the 7a host unchanged, and a bare invocation with no command still serves, so `eng/server-smoke.cs` and the future container image need no change. Alternative: a separate `Varve.Cli` project compiling the server's sources by `<Compile Include>`, two composition roots of one text. Please pick the package id if you take the recommendation; I would keep `Varve.Server` with the tool command `varve`.
>
> 3. **The tool package is framework-dependent; the AOT publish is the gate artefact.** A Native AOT tool package is per runtime identifier and cannot be cross-compiled on the publish runner, so `dotnet tool install` gets the ordinary package, and the `native aot` job publishes the single-file binary on both runners and runs the CLI smoke against it. The ADR records both.
>
> 4. **Argument parsing: `System.CommandLine` 2.0.12** (stable, MIT, Microsoft, AOT-tested, no native asset) in `Varve.Server` only, registered under the CLI ADR with the same AOT condition as `JwtBearer`. Hand-written parsing for nine commands with shared options, help and completion is a few hundred lines that exist only to avoid a dependency a host may take. I will state the alternative in the ADR.
>
> 5. **ADR 0037 point 8, "the platform credential store", has no managed path.** Credential Manager, Keychain and libsecret all need P/Invoke or their command-line tools. Recommendation: the refresh token in one file under the user profile, mode 0600 on Unix and DPAPI-protected on Windows through `System.Security.Cryptography.ProtectedData` (Microsoft, managed, registered in the CLI only), with the keychain tools recorded as the alternative. That changes 0037's wording, so the CLI ADR supersedes point 8's storage clause in part. The other reading, shelling out to `security` and `secret-tool`, is fragile and absent on most servers. Your call.
>
> 6. **No new changelog section line is possible.** `v0.1.0-preview.1` is tagged at a9f0c24, before 7a's first commit, so a "Milestone 7b" line is red on `release pending` until the release this pull request cuts. Recommendation: 7b adds no line, and at close-out I retitle the 7a line to "Milestone 7 — the server, the protocols, authentication, the CLI, federation" so the released section reads as one milestone. Retitling is not an addition, so the gate passes.
>
> 7. **Admin API shape.** `Varve:Auth:Server:Admin` (claim values) is a server-wide grant for create, delete, open and close; a server admin also administers every dataset. `GET /datasets` lists the datasets the caller may administer. A created dataset is a directory under `DatasetsRoot`, and at start the server serves every directory under the root that holds a dataset plus the configured ones, so a created dataset survives a restart without a configuration edit (a dated amendment to 0093 point 4). `DELETE` is `409 dataset-open` until `POST /datasets/{name}/close` has drained and released it; `open` is the inverse, so an operator can attach a copied directory without a restart. The "at least one dataset" start check relaxes to "at least one dataset or a root".
>
> 8. **Graph scope reaches the update seam.** `DELETE WHERE` over the readable scope and the pre-submit write check both happen inside `SparqlUpdate`, so `UpdateOptions` gains `ReadScope` and `WriteScope`, `ISparqlUpdateExecutor.ExecuteAsync` gains an `AccessScope` parameter (dated amendment to 0091), and `Varve.Protocol` gets a fourth seam, `IAccessScopes`, that the host resolves from claims per request. A refused quad throws `GraphNotWritableException` naming the graph; the protocol answers `403`.
>
> 9. **Item 6's two items change the store.** Worker-thread resolve is ADR 0081's last alternative built: the parser fills buffers, a bounded pool resolves and spills in sequence order, `BulkLoadOptions.Workers` defaults to the processor count less one. Per-run term filters are a derived format change: format 3, a blocked Bloom filter per run at about a byte a term, loaded with the run's directory and counted in the soak's "dataset's own" figure. ADR 0079's revisit condition fires by your decision, not by a measurement; the ADR says so.
>
> 10. **The evaluation suites over the protocol** become a third subject of the conformance harness (query and update cases sent over HTTP to an in-process server with an `all` grant, results read back with the results readers), roughly 640 more ratchet lines. The `sparql11/service` suite runs its seven cases with `HttpServiceHandler` against one in-process server per `qt:serviceData` endpoint; a test-side delegating handler maps the manifests' example hosts to loopback, and the allow-list names the example IRIs.
>
> ## The decisions and the surface
>
> | ADR | Decides |
> |---|---|
> | 0102 | `Varve.Protocol.Client`, layer 5: `SparqlHttpClient` (query, update, Graph Store, feed, admin), the endpoint policy (allow-list by IRI prefix, default none; loopback and private ranges refused unless `AllowPrivateAddresses`; redirects not followed; the SSRF considerations), timeouts and byte caps |
> | 0103 | `SERVICE` and `LOAD` over HTTP: `HttpServiceHandler` sends `SELECT * WHERE { P }` by `SparqlWriter`, negotiates results JSON then XML, parses with `Varve.Sparql.Results`, `SILENT` per Federated Query §2.3; `RdfDocumentClient` and the one-line `ILoadSource` adapter; configuration `Varve:Federation` and `Varve:Load`; the suites |
> | 0104 | The CLI: one executable, the nine commands, embedded mode (directory, no auth, the lease), remote mode (device code and client credentials, the credential file), `System.CommandLine`, the tool package, the AOT gate |
> | 0105 | The admin API, as in item 7 |
> | 0106 | Graph-level authorisation: scoped grants, `GraphScope` and `GraphScopedQuadSource` in `Varve.Rdf`, reads, writes, feed and diff, the skipped wrapper for `all`, the row-level boundary left to milestone 9 |
> | 0107 | The bulk load resolves on worker threads |
> | 0108 | Per-run term filters, derived format 3 |
>
> Dated amendments: 0057 (data-only requests, the property, a new key), 0004 (`System.Uri` in `Varve.Protocol.Client`), 0091 (the seam's scope parameter and `IAccessScopes`), 0093 (datasets under the root), and 0037 if you take item 5. All filed unaccepted, the one red.
>
> New endpoints, all `admin` unless noted:
>
> | Path | Methods | What |
> |---|---|---|
> | `/datasets` | `GET` | the datasets the caller administers |
> | `/datasets/{name}` | `PUT`, `DELETE` | create (`201`, body `{ "storage": "File" }`), delete a closed one; server admin |
> | `/datasets/{name}/open`, `/close` | `POST` | attach, drain and release; server admin |
> | `/datasets/{name}/settings` | `POST` | a `Settings` commit with `If-Match`, `Varve-Position` back |
> | `/datasets/{name}/checkpoints` | `POST` | checkpoint the head or `?at=`, `201` |
> | `/datasets/{name}/status` | `GET` | gains `projection: { position, lag, failed }` |
>
> Configuration added under `Varve`: `Auth:Server:Admin`, `Auth:Datasets:{name}:Grants` (claim, `read` or `write`, graphs as IRIs, `default`, or prefixes; the 7a `Read`/`Write` lists stay as the `all` case), `Federation` and `Load` (`AllowedEndpoints`/`AllowedSources`, `AllowPrivateAddresses`, `Timeout`, `MaxResponseBytes`).
>
> CLI: `varve create|info|load|query|update|export|checkpoint|feed|serve`, each taking a directory or a URL where both make sense (`load` is embedded only, the bulk loader; `serve` embeds the 7a host). The delta writer becomes public in `Varve.Protocol` as `ChangeFeedWriter` so `feed` on a local dataset writes the specified format.
>
> Tests beyond the suites: the three properties of item 5 plus the 7a auth matrix with a graph-scoped user; the allocation test that `all` scope costs what 7a measured and a benchmark row for a scoped request; the 0057 property; the CLI's commands against a temporary dataset and against the in-process server with the test issuer, which gains a device-authorization endpoint; the client credentials and device-code flows on the Linux legs.
>
> ## Order of work and close-out
>
> Commits, each `Refs #11` and signed off: 0(a) with the eight-writer re-run and the 409 counts; 0(b) with the per-package finding counts in the body; 0(c) with the meter's constant and its reason; the ADRs and sets; then items 1, 2, 3, 4, 5 and 6 in that order, each a vertical slice ending in passing tests; the long runs (bulk gate before and after, the one-hour soak under the ADR 0082 configuration and under the default runtime) in the background while later items proceed. Close-out: merge `main`; if the releases-as-code pull request has landed, write `releases/v0.1.0-preview.2.yaml` under its ADR, otherwise `eng/changelog.cs --release 0.1.0-preview.2` per 0085 and say so; service description, `docs/operator/`, AGENTS.md, roadmap, the traceability record; the closing commit carries `Closes #11`; then the pull request, red only on `CS0618`. Today no such pull request exists, only two Dependabot ones.
>
> Two environment notes. The allocation meter re-run on Windows and macOS happens on the pull request's CI, which I will report from. The Oxigraph rows of the 7a benchmark are not re-run, only Varve's eight-writer rows, which is what item 0(a) asks.
>
> Please confirm items 2, 5, 6 and 7, or redirect them, and I will proceed.

### The approval of the plan

> Approved.
> 2: one executable, package id Varve.Server, command varve. ADR revisit condition: a second executable moves host wiring into a layer-5 Varve.Hosting package.
> 5: credential file as proposed, superseding 0037 point 8's storage clause in part. Add --no-store for CI use; document that macOS file storage is weaker than Keychain and the token is revocable.
> 6: retitle the 7a line; moot if the releases PR lands first.
> 7: as proposed. DELETE on a closed dataset removes the directory, stated in the ADR. A directory under the root that fails to open is listed with failed and a reason, not skipped.
> All else as planned.

### During the work

No further prompts. Every decision below that the brief and the approval do
not settle was the session's, and is marked as such.

## The report

### What was built

1. **ADR 0057 amended** (commit 68d3569): a request of `INSERT DATA` and
   `DELETE DATA` alone submits with no expected position unless the caller
   gave one; the chain is composed without the pin and normalised by the
   sequencer against the head it meets. Four unit tests and a 1,000-iteration
   property (two histories differing in the contended tail, the same
   effective delta). The eight-writer rows re-run: **409s before and after,
   memory 147,760 → 0 and file 86,222 → 0**, throughput 5,980 → 10,794 and
   1,081 → 2,106 ops/s, file p99 70.5 → 6.6 ms (`tests/Varve.Benchmarks/README.md`,
   the 7a addendum).
2. **DecisionDriven.Analyzers 0.1.0-preview.7** (48e91ca): the
   `;`-separated `ArchContractTypeAssemblies` is read whole; the separators
   went back to `;`. **New findings per package: 0 in all 14** — proved to
   be a real check, not a silent one, by narrowing `Varve.Protocol`'s list to
   one assembly and seeing five `DD0010` findings appear.
3. **The allocation meter** (b3325cf) is the minimum of `Readings = 10`
   interleaved readings for the baseline and the candidate; the two-rounds
   loop is gone; `docs/testing.md` §4 says why. The three-platform re-run is
   the pull request's CI.
4. **ADRs 0103–0109** and the dated amendments (53b4a73), all filed
   unaccepted; the ledger's only red is `CS0618`.
5. **`Varve.Protocol.Client`** (b68ab19): `SparqlHttpClient`,
   `HttpServiceHandler` (`SERVICE` over HTTP: `SELECT vars WHERE { P }` by
   `SparqlWriter`, JSON then XML, failures returned for `SILENT`),
   `RdfDocumentClient` (the fetch `LOAD` needs), `EndpointPolicy` (allow-list
   by IRI prefix checked first, then `http`/`https` only, no userinfo, no
   redirects, loopback and private ranges refused unless allowed),
   `ClientLimits`. The server binds `Varve:Federation` and `Varve:Load`
   (ADR 0104). The W3C `sparql11/service` suite runs its seven cases end to
   end over HTTP against in-process servers that share one handler, seven new
   `@http` ratchet lines.
6. **The admin API** (aa75ade, ADR 0106): list, create, open, close, delete;
   settings commits; checkpoints on demand; `/status` with the projection;
   `Varve:Auth:Server:Admin`; datasets discovered under the root, a failed
   one listed with its reason; `DELETE` of a closed dataset removes its
   directory.
7. **The `varve` command line** (84813d7, ADR 0105): one executable, the
   nine commands, embedded over a directory and remote over a URL;
   `--token`/`VARVE_TOKEN`, client credentials, the device code flow; the
   credential file (0600, DPAPI, `--no-store`); `System.CommandLine` 2.0.12
   and `ProtectedData` registered in `Varve.Server` alone; the tool package
   (`PackAsTool`, command `varve`) and the AOT single file; `eng/server-smoke.cs`
   runs the commands against the AOT binary before serving.
8. **Graph-level authorisation** (fa38aac, ADR 0107): `GraphScope`,
   `CallerScope`, `GraphScopedQuadSource` and `GraphNotWritableException` in
   `Varve.Rdf`; `ReadScope` and `WriteScope` in `UpdateOptions`; the
   `IAccessScopes` seam; reads through the scoped view, `FROM` of an
   unreadable graph an empty graph, Graph Store `404`/`403`, the feed and the
   diff cut, `Vary: Authorization`; `Varve:Auth:Datasets:{name}:Grants`.
9. **The loader on worker threads and per-run term filters** (3242096,
   ADRs 0108 and 0109): `BulkLoadOptions.Workers`, a ring of operation
   buffers the parser fills and workers resolve, sort and spill as runs named
   by their sequence; `TermFilter`, derived format **3** read beside 2, the
   soak counting it.
10. **Close-out**: the service description advertises `sd:BasicFederatedQuery`
    when the host answers `SERVICE` and names the admin endpoints;
    `docs/operator/` (run, configure, authenticate, back up and restore,
    upgrade); AGENTS.md, the roadmap, the README; `main` merged and the ADRs
    renumbered; `releases/v0.1.0-preview.2.yaml` (ADR 0102), `CHANGELOG.md`
    folded from it, the roadmap's heading *(complete)*.

### The endpoints, as shipped

Added to 7a's table (`docs/traceability/2026-10-07-issue-11-milestone-7a.md`):

| Path | Methods | Permission | What |
|---|---|---|---|
| `/datasets` | `GET` | any dataset permission | the datasets the caller administers, each with `state`, `storage`, `origin`, `id`, `head` |
| `/datasets/{name}` | `PUT` | server admin | create under the root: `201`, body `{ "storage": "File" }`; `409 dataset-exists` |
| `/datasets/{name}` | `DELETE` | server admin | delete a closed dataset and its directory; `409 dataset-open` while open |
| `/datasets/{name}/open`, `/close` | `POST` | server admin | attach; drain and release |
| `/datasets/{name}/settings` | `POST` | admin | a `Settings` commit, `If-Match` honoured, `204` with `Varve-Position`; `403 agent-required` in anonymous mode |
| `/datasets/{name}/checkpoints` | `POST` | admin | a checkpoint at the head or `?at=`, `201`; `400` for an empty dataset |
| `/datasets/{name}/status` | `GET` | admin | gains `state` and `projection { position, lag, failed }` |

New problem types: `dataset-exists`, `dataset-open`, `agent-required`,
`graph-not-writable` (with the graph as an extension member).

### The decisions

| ADR | Title | Status |
|---|---|---|
| 0103 | `Varve.Protocol.Client`: the HTTP client, the endpoint policy, the limits | filed unaccepted |
| 0104 | `SERVICE` and `LOAD` over HTTP, under an allow-list the host sets | filed unaccepted |
| 0105 | The CLI `varve`: one executable, the credential file, `System.CommandLine` | filed unaccepted; supersedes 0037 point 8's storage clause in part |
| 0106 | The admin API | filed unaccepted |
| 0107 | Graph-level authorisation | filed unaccepted; refines 0037 point 3 |
| 0108 | The bulk load resolves and spills on worker threads | filed unaccepted |
| 0109 | Per-run term filters: derived format 3 | filed unaccepted; discharges 0079's revisit condition |

Dated amendments (ADR 0068): **0057** (data-only requests), **0004**
(`System.Uri` admitted in `Varve.Protocol.Client` for transport addresses),
**0091** (the seam carries the scope, a fourth seam, a fourth policy name,
the resolver's write side), **0093** (datasets under the root), **0037**
(status), **0079** and **0081** (their revisit condition and alternative
taken by 0109 and 0108).

Decisions the session made within the approved plan:

- `CallerScope` is the name of what ADR 0107 calls the access scope, because
  `Varve.Store.AccessScope` already names a dataset's history setting.
- `GraphNotWritableException` is `Varve.Rdf`'s, not `Varve.Sparql.Store`'s:
  the executor that throws it and the protocol that answers it are both
  layer 5 (ADR 0091). ADR 0107's consequences say so.
- A `POST` to the Graph Store's root with a minted graph name outside the
  writable set is `403`, not `404`: a name the server just made reveals
  nothing.
- `CLEAR ALL` and a `DELETE WHERE` over every graph clear what the caller
  reads; the rest is untouched and unmentioned — the same rule as `FROM`.
- The conformance harness gained **two** subjects, not one: `@protocol`
  (the brief's) and `@scoped` (the store through a scope naming every graph
  of the case), because the second costs nothing and exercises the wrapper
  across the whole suite.
- A worker's cache keeps a new term it met after the shared table spilled;
  the merge of the term runs takes a term that two spills hold once, which
  the 6c code already relied on.

### The suites

| Suite | Passing |
|---|---:|
| query evaluation, `@dataset`, `@store`, `@scoped`, `@protocol` | 1,132 + 968 + 88 = 2,188 (547 cases × 4) |
| `sparql11/service` over HTTP, `@http` | 7 |
| update evaluation, in process and `@protocol` | 188 (94 × 2) |
| protocol, graph store, service description (7a) | 124, the 12 `http-rdf-update` exemptions unchanged |
| everything else (7a) | 1,615 |
| **the ratchet** | **4,122** lines, up from 2,927 at 7a |

Protocol tests 54, server tests 55 (49 pass, 6 provider legs skip without
secrets), client tests 51, store tests 163, Rdf tests +5, every other suite
unchanged. The `@protocol` run found a defect the in-process runs could not:
the evaluator minted blank nodes as `.b<n>`, which no RDF syntax carries, so
a `CONSTRUCT` answer with one did not parse as N-Triples. Minted labels are
`q<n>` now; `sparql-evaluation.md` §7.9 says so.

### Properties

| Property | Iterations | Where |
|---|---:|---|
| A data-only request against two histories differing in the contended tail commits the same effective delta (ADR 0057) | 1,000 | `Varve.Sparql.Store.Tests` |
| The scoped source equals the sub-dataset of the scope's graphs for every pattern (ADR 0107, layer 1) | 300 | `Varve.Rdf.Tests` |
| The scoped answer over HTTP equals the answer over the sub-dataset of the readable graphs | 1,000 | `Varve.Protocol.Tests` |
| No response to a caller without `read` on `G` — query, graph store, service description, feed, diff, problem bodies, headers — carries an IRI only `G` holds | 200 | `Varve.Protocol.Tests` |
| A bulk load leaves the same dataset and the same ids at any worker count (ADR 0108) | 60 | `Varve.Store.Tests` |
| The 7a properties, unchanged | | |

### Allocation

A solution through the protocol costs the evaluator's row, 56 bytes, and
nothing else — with a caller whose scope is every graph, which is served by
the unwrapped view, and with a scope that names the default graph, which is
served through `GraphScopedQuadSource`. The meter is the minimum of ten
readings now; the Windows and macOS re-run is the pull request's.

### Native AOT

The one executable with the command line: **21,447,656 bytes** (20.5 MiB),
up from 19,925,752 at 7a; process start to `/ready` 118 ms on the
development machine. `eng/server-smoke.cs` runs `create`, `update`, `query`,
`export`, `checkpoint`, `feed` and `info` against the binary, serves, and
runs one `query` against the served URL. The tool package packs with the
`varve` command and `eng/package-metadata.cs` accepts it.

### Benchmarks

In `tests/Varve.Benchmarks/README.md`, the 7b section:

- a scoped scan of a million quads costs 13.6 ns a quad over the unwrapped
  scan, allocating per request and never per quad;
- the bulk gate at 10M: 82.7 s → 68.2 s (120,863 → 146,643 quads/s), the
  input stage 31.6 s → 15.4 s with three workers; at 100M, 1 GiB:
  1,047 s → 907 s (95,478 → 110,268 quads/s), the input stage 377 s → 173 s,
  the commit 670 s → 734 s (the serial part, unchanged in code; the
  difference is the machine's), peak working set 2,272 → 1,978 MB;
- the 10M gate again under 6c's 512 MiB `DOTNET_GCHeapHardLimit`: 73.6 s
  (135,801 quads/s), peak managed heap 490 MB, within 4% of 6c's 71 s on the
  same cap;
- 10,000 lookups of absent terms over three disk runs: 48.8 ms → 2.9 ms;
- the soak: running on the final code as this is written; its figures are in the closing commit's revision of this record and of the README.

### What was found

- **The `@protocol` subject found the evaluator's minted labels.** Fresh
  blank nodes were labelled `.b<n>`, which no RDF syntax carries; in process
  nothing parsed them, over HTTP a `CONSTRUCT` answer with one was not
  N-Triples. They are `q<n>` now (§7.9).
- **A worker's blank-node key was the whole scratch array**, not the slice
  it had written, so two workers resolving the same label could disagree.
  The worker-count property (the same dataset and ids at 1, 2 and 3 workers)
  found it on its first run; the fix is the slice.
- **`AccessScope` already existed**: `Varve.Store.AccessScope` is a
  dataset's history setting, so ADR 0107's type is `CallerScope`.
- **`GraphNotWritableException` could not live at layer 5**: the executor
  that throws it (`Varve.Sparql.Store`) and the protocol that answers it are
  both layer 5, and a 5→5 reference is refused, so it is `Varve.Rdf`'s.
- **BenchmarkDotNet rebuilds the benchmark project itself** and that build
  has warnings as errors, so every row here ran `--inProcess` while the 7b
  ADRs are `CS0618`; the README's commands say so.
- **The releases-as-code session took ADR number 0102** while this one
  ran with 0102–0108 filed; the seven ADRs, their sets, citations and
  cross-references were renumbered 0103–0109 in one commit (8731edb) after
  `main` was merged, and `eng/decision-sets.cs` and the build confirm no
  citation was lost.
- **The 100M commit stage varied by 10% between two runs** (670 → 734 s)
  with no change to its code; the before and after rows are one session on
  one machine, and the input stage's halving is the signal, not the
  commit's drift.
- **The 7a eight-writer `409`s were the pin, not contention**: with data-only
  requests submitting against the head they meet, the counts went from
  147,760 and 86,222 to 0 and throughput roughly doubled, with the same
  one-commit-per-request guarantee.

### Proposed spec changes

- `sparql-evaluation.md` §7.9: minted blank nodes are `q<n>` (made).
- `sparql-update-store.md` §2, §3, §6.2: `ReadScope`, `WriteScope`, the
  check before the submit (made).
- `change-feed.md` §4: the caller's scope as a filter (made).
- `storage-format.md` §7: derived format 3, the filter section (made).

### What the operability milestone needs from the maintainer

1. **Accept ADRs 0103–0109** and the seven dated amendments, which turns
   `CS0618` green.
2. **The container registries.** GHCR and Docker Hub are decided; the image
   is not built here. What it needs: the AOT single file as the entry point
   (`varve serve`), a base image choice (`mcr.microsoft.com/dotnet/runtime-deps`
   for the AOT binary, or distroless), the `Varve:DatasetsRoot` volume, and
   the two registries' credentials as repository secrets with a publish
   workflow that tags by the release descriptor. The 7b smoke script already
   runs the binary the way an image would.
3. **The GC and host configuration that judges #61.** The soak ran under
   the default runtime (the figures are in the benchmark README); the 6c addendum showed a
   128 MB `DOTNET_GCHeapHardLimit` holding the band. The decision that the
   operability milestone needs: the configuration the gate is judged on —
   workstation or server GC, a heap hard limit or none, container memory
   limits — stated once in `docs/operator/configure.md` and ADR 0082.
4. **The Aspire integration's package placement.** An Aspire resource for a
   Varve server would be a hosting-side package referencing
   `Aspire.Hosting`; it is a composition-root concern and belongs at layer 6
   beside `Varve.Server`, as `Varve.Aspire.Hosting` in `src/`, or in `tools/`
   if it is not shipped on NuGet. It must not live in `Varve.Protocol.Client`
   (layer 5), which stays free of hosting packages. The maintainer's call.
5. **The release.** The releases-as-code pull request (#73, ADR 0102) had
   merged, so `releases/v0.1.0-preview.2.yaml` proposes the release: basis
   #11, `storage-format: 1`, ADRs 0090–0109 (every ADR since the
   `v0.1.0-preview.1` tag, as `eng/release.cs --draft` lists them);
   `CHANGELOG.md` is its projection; README's Status and the roadmap's
   heading pass `eng/status.cs`. `eng/release.cs --check` is red until the
   seven 7b ADRs are accepted and until the `Closes #11` trailer is in the
   range, which the descriptor's commit carries. It lands at its approved
   head through `land/`, never by the merge button (`docs/releases.md` §3);
   nothing is tagged by hand.

### Recorded for later

- `varve ship`: a command over `Dataset.ShipAsync` for an exact-position
  replica; the operator guide points at the API for now.
- The checkpoint policy is not configurable through the server yet; an
  operator takes checkpoints through the admin API or the command line.
- The `@protocol` evaluation subject starts one in-process server per case,
  about 50 ms each; a shared server with per-case `SERVICE` routing would
  halve the suite's time.

### Not built, as the brief says

The container image, telemetry, the Aspire integration, archive, erasure
mode, SHACL.
