# Milestone 7a — the protocols, the server, authentication, the change feed

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#11](https://github.com/Hafeok/Varve/issues/11) |
| **Date** | 2026-10-07 |
| **Tool** | Claude Code 2.1.292, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served for every turn, from the session's own metadata (`get_session`: `configured_model`, `session_context.model` and `last_served_model` all `claude-opus-5-5`) |
| **Session identifier** | `session_018Wbaz3NpFgYStJn7zDS1Da` |
| **Branch** | `claude/clever-sagan-gviqln`, from `main` at e2b9ca8 |
| **Commits** | 510ea64, 79fb0da, 84f50a5, a75d41e, a8a4475, 60d77ed, 5d4eeed, c56a9f4, 7b29bb0, and the closing commit carrying this record |

The session ran out of context once and continued from a summary; the work
and this record are continuous across it.

## The prompts

### The brief

> Session 7a: protocols, service description, authentication, change feed
> Milestone 7a builds `Varve.Server` (layer 6, the first host) and `Varve.Protocol` (layer 5, the protocol implementations over the integration packages, usable from any ASP.NET Core host). Read `docs/brief.md`, the specification, the SPARQL and update specs, ADRs 0005, 0010–0016, 0032/0088, 0034, 0042, 0052, 0057–0060, the authentication ADR, and the 5c and 6c traceability records before planning. `AGENTS.md` applies: every public type under the DD rules, `[Contract]` and `[DomainModel]` citing decisions, wrappers not primitives, `[HotPath]` where it applies. Plan first, wait for approval; one PR, red only on `CS0618`; the API sketch and the endpoint table are in the plan. This is a milestone, so it ends in a release.
> A. Decisions (ADRs, Accepted unless stated)
>
> 1. Package split. `Varve.Protocol` (layer 5): SPARQL 1.1 Protocol, Graph Store Protocol, service description, the change-feed protocol, and the request-to-store mapping, as ASP.NET Core endpoint groups with no host assumptions (`IEndpointRouteBuilder` extensions). `Varve.Server` (layer 6): the executable; configuration, hosting, authentication wiring, the composition root. Nothing in `Varve.Protocol` references authentication types; it exposes the authorisation requirements (`read`, `write`, `admin` per dataset) as policy names the host binds. ADR 0060's layer table unchanged.
> 2. Protocol scope. SPARQL 1.1 Protocol (query via GET and both POST forms; update via both POST forms; `default-graph-uri` and `named-graph-uri` per §2.1.4 and §2.2.3; content negotiation over every result format `Varve.Sparql.Results` writes; CONSTRUCT and DESCRIBE in every RDF syntax Varve writes). Graph Store Protocol: GET, PUT, POST, DELETE, HEAD on direct and indirect graph identification, `?default`, every RDF syntax Varve reads and writes; a PUT or DELETE is one commit; a POST is one commit. SPARQL 1.2 Protocol differences adopted where the draft is stable; report what you find. Service description (SPARQL 1.1 Service Description) at the endpoint root for `GET` with RDF `Accept`, generated from the dataset's actual features (supported languages, result formats, input formats, named graphs by SPARQL-only enumeration never by store internals, and the event-sourced extensions declared under a Varve vocabulary IRI, with a stated namespace).
> 3. Datasets as the routing unit. One server hosts many datasets: `/datasets/{name}/sparql`, `/datasets/{name}/graphs`, `/datasets/{name}/`, with `{name}` validated as a path segment and mapped to a dataset directory under a configured root. Datasets are created through configuration or the admin API (session 7b), never by a query. `DatasetId` is the store's, `name` is the host's; the mapping is in the host.
> 4. Every write is one commit, with the caller as agent. Update requests go through `Varve.Sparql.Store`; GSP writes compose one delta and commit once; the commit metadata's `agent` is the caller's subject identifier from the token per the authentication ADR (`oid` for Entra, `sub` otherwise), `cause` is the request id, and the HTTP response carries the resulting `Position` in a header (`Varve-Position`) and, on `Conflict`, `409` with the head position. Pre-commit validators bound to the dataset run as always.
> 5. Reads are pinned per request (ADR 0052): the pin is taken when the request starts and released when the response body finishes streaming, with the server's maximum lifetime and cancellation as the safety net; a result stream that outlives the limit is cut with a documented error trailer where the format allows and a connection close otherwise.
> 6. Time travel over HTTP. A read request may carry `Varve-As-Of: position:<n>` or `Varve-As-Of: time:<RFC 3339>` (UTC, offsets normalised); a timestamp resolves to the latest closed position at or before it (spec I5); the response carries the resolved position. Below the archive horizon it is `404` with a problem body; a timestamp before the first commit is `404` too. Every response that touched a dataset carries `Varve-Position` and an `ETag` equal to the head position (or the resolved as-of position); `If-None-Match` on reads returns `304` when the head has not moved, which is how a client polls cheaply. On GSP writes, `If-Match: <position>` is the HTTP form of the sequencer's expected position: a mismatch is `412` with the head position, and a `Conflict` from the sequencer is `409`. The service description advertises all of it. These semantics follow Delta Sharing's (version and timestamp selection, version header on every response), which has run time travel over HTTP in production; cite it in the ADR.
> 7. Change feed. `GET /datasets/{name}/feed?from=<position>|fromTime=<RFC 3339>&to=<position>|toTime=<RFC 3339>&graph=<iri>&pattern=…` returns closed commits in the range as a stream. Resolution is asymmetric, as in Delta Sharing's change data feed: a start timestamp resolves to the earliest closed commit at or after it, an end timestamp to the latest at or before it; `from` is exclusive, `to` inclusive; no `to` means tail live. A bounded range returns a finite body and closes; an unbounded one stays open. The response carries the start position as `Varve-Position`. The stream is newline-delimited records ( one per commit, each with position, timestamp, agent, cause, kind, and the delta in N-Quads-per-line with an `+`/`-` prefix; `Settings` and `Erasure` commits always present), at-least-once, resumable by position, with long-polling or server-sent events for live tailing (choose SSE, record the alternative). The feed is a projection of the log (spec §8) and reads through the subscription contract, nothing else. The format gets its own spec page and a reader in `Varve.Protocol` so a client can consume it with Varve types.
> 8. Diff over HTTP. `GET /datasets/{name}/diff?from=<p1>&to=<p2>` (positions or timestamps, resolved as in 7) returns `Diff(p1, p2)` (spec R3) in the same delta format.
> 9. Authentication and authorisation exactly per the accepted authentication ADR: `JwtBearer` from the shared framework, authority and audiences from configuration, claim-to-permission mapping per dataset, anonymous mode explicit with a startup warning, no `Microsoft.Identity.Web`, no API keys, no other credential. Testing in three layers, each with its own ADR line: (a) an in-process OIDC issuer in the test project (discovery document, JWKS, a signing key per run, `Mint(claims)`), used by every permission and token-validation test, running everywhere the suite runs including the cloud sandbox; (b) `ghcr.io/navikt/mock-oauth2-server` as a container on Linux CI with a committed JSON config, exercising client credentials and device code end to end against the real middleware; (c) Zitadel as the real-provider leg on Linux CI (Zitadel plus PostgreSQL via `docker run` or compose in the workflow, seeded by a script through its management API: one project, one API application, one service user with client credentials, one human user for device code, roles mapped to `read`, `write`, `admin`), one end-to-end test per flow. Entra and Google: one end-to-end test each, skipped unless their secrets exist in a main-restricted environment. Containers are started by the workflow, not by a test package (no Testcontainers; say so in the register ADR). The devcontainer runs (b) and (c) locally through its Docker socket.
> 10. Errors as RFC 9457 problem details everywhere, with the SPARQL parse error's position in the body, and `Varve-Position` on every response that touched the log.
> 11. No Oxigraph-specific endpoints are imitated. Where the SPARQL Protocol is silent, Oxigraph's server behaviour is the tie-breaker per the brief, and the ADR lists each such choice.
>
> B. What is built
>
> * `Varve.Protocol` and `Varve.Server`, with the smoke apps unchanged and the benchmarks gaining a protocol workload.
> * Configuration: datasets root, per-dataset settings, auth, limits (query timeout, result size cap, max request body, pinned-read lifetime), all through `IConfiguration` with a documented `appsettings` shape and environment-variable mapping; validated at startup; the server refuses to start with an invalid configuration rather than defaulting.
> * The server runs under Native AOT: no reflection-based JSON (source-generated contexts for problem details and the feed), no reflection-based configuration binding (the configuration source generator), no MVC; minimal APIs only. The AOT publish is a CI gate for `Varve.Server` from this session.
> * Graceful shutdown: SIGTERM drains the sequencer, finishes in-flight commits, releases pins, seals the segment. Readiness is false until the default projection is at head. Both are operability concerns that belong here because the server cannot be tested without them; the full health model is the operability milestone.
>
> C. Definition of done
>
> * W3C SPARQL 1.1 Protocol test suite (`sparql11/protocol`) and the Graph Store Protocol suite (`sparql11/http-rdf-update`) wired into the conformance harness with guard counts, under the ratchet, run against an in-process server (test host) over the in-memory store and the file store. No exemptions expected; each one cites the protocol section and Oxigraph's behaviour.
> * Service description validated against the SPARQL 1.1 Service Description vocabulary with SHACL shapes written in the test project (the SHACL validator is milestone 8; a small shape set checked by a hand-written validator in test code is acceptable and must say so).
> * Properties: a request sequence of updates through the protocol equals the same sequence through `Varve.Sparql.Store` directly, commit for commit; a feed consumed from position 0 replays to the same state as an as-of read at the head; a feed resumed from an arbitrary position delivers exactly the commits after it; a bounded feed `(from, to]` concatenated with `(to, head]` equals the unbounded feed from `from`; timestamp resolution on the feed and on `Varve-As-Of` agrees with the in-process rules for generated histories with repeated timestamps; `If-Match` with a stale position is always `412` and never commits; `Varve-As-Of` reads equal in-process as-of reads; concurrent writers get exactly one `Conflict` per contended position.
> * Auth tests: every endpoint refused without a token in OIDC mode; `read` cannot write; `write` cannot administer; token validation failures are `401` with no information leak; anonymous mode warns at startup and is refused in a configuration marked production.
> * Allocation: the query path from request to response allocates nothing per solution beyond the writer's documented per-row cost; measured as before.
> * Benchmarks, last and not gating: queries per second and update commits per second over HTTP against Oxigraph's server on the same machine and dataset, honest row first, with the pinned-read and AOT configuration stated.
>
> D. Gates that come due
> Public API baselines for two new packages; the `System.Uri` ban narrowed per ADR 0004's milestone 2 note, now that layer 5 and 6 code talks HTTP: allowed in `Varve.Protocol` and `Varve.Server` only, by dated amendment, and still banned below; no new runtime packages (ASP.NET Core is the shared framework; say so in the register ADR); the ambient clock ban holds (the server injects `TimeProvider.System` at the composition root, which is what the evaluator and the store already require).
> Recorded for later, not built
> Replica bootstrap over HTTP follows Delta Sharing's control-plane/data-plane split: a `GET /datasets/{name}/snapshot?at=<position>` manifest listing the checkpoint and the log segments after it, each with size and hash and a direct or presigned URL, so the client fetches files and the server streams nothing. Record this in the roadmap under the replication milestone with the reason; do not build it in 7.
> Non-goals for 7a
> `SERVICE` and `LOAD` over HTTP, the CLI, the admin API, the container image, telemetry, resource governance beyond the limits above, federation, the Aspire integration. Nothing in `tools/`.
> Report
> The traceability record and the PR body: the endpoint table as shipped; the ADRs with numbers; protocol suite counts with the ratchet total and every exemption; the change-feed format spec; property iteration counts and counterexamples; the AOT publish size and startup time; benchmark numbers with hardware; proposed spec changes; what 7b needs from the maintainer.

### The approval of the plan

> All four conflicts: your recommendations. JwtBearer in Varve.Server only with a register ADR and a 0037 amendment, conditional on the server's AOT publish staying green. ISparqlUpdateExecutor in Varve.Protocol bound by the host; note in 0060 the revisit condition (a third integration needing the same). Commit.TryExternalise as a 0042 amendment with the append-only-dictionary invariant stated. Fix the drain; no seal on shutdown.
> Scope findings accepted as proposed; Q1 closed with stable labels out, fresh in, no skolem, spec 1.6. Required job names go in repo-standard.yaml in the PR.
> Section 8: agent IRI <issuer>#<sub> (sub percent-encoded); domain https://w3id.org/varve/ for vocabulary and problem types; ship GET /datasets/{name}/status as the one admin endpoint; application/vnd.varve.delta; version=1; tie-breakers as listed, reviewed in the PR; release after 7b as 0.1.0-preview.2.
> Plan approved. Proceed.

### During the work

> Pin the test images to registries without anonymous pull limits: ghcr.io/navikt/mock-oauth2-server, ghcr.io/zitadel/zitadel, public.ecr.aws/docker/library/postgres, by digest. No Docker Hub references in the compose file.

## The report

### What was built

- **`Varve.Protocol`** (layer 5, packable, AOT-compatible): the endpoints as
  `IEndpointRouteBuilder` extensions, the request-to-store mapping, the
  change-feed reader, 167 lines of public API baseline. No authentication type
  is referenced: every endpoint authorises imperatively, first, through the
  host's `IAuthorizationService` under the policy names `varve:read`,
  `varve:write` and `varve:admin` (ADR 0091).
- **`Varve.Server`** (layer 6, the first host): configuration bound by the
  source generator and validated at start, OIDC through `JwtBearer`, anonymous
  mode, readiness, a draining shutdown, Native AOT.
- **`Varve.Store`**: `Commit.TryExternalise` (ADR 0042, amended), and
  `Dataset.DisposeAsync` now waits for the sequencer (ADR 0101), with a
  regression test; the drain exposed a bulk-load cleanup path that skipped
  `EndBulkLoad`, fixed in the same commit.
- **`Varve.Sparql.Store`**: `UpdateOptions.ExpectedPosition`, `If-Match` for
  SPARQL Update.

### The endpoints, as shipped

Mounted by the server under `/datasets/{name}`; a host mounts the group where
it likes.

| Path | Methods | Permission | What |
|---|---|---|---|
| `/` | `GET`, `HEAD` | read | the service description, from the dataset's actual features |
| `/sparql` | `GET` (`query`), `POST` form or `application/sparql-query` | read | query; a `GET` with neither parameter is the service description |
| `/sparql` | `POST` form or `application/sparql-update` | write | update, one commit |
| `/graphs?default`, `/graphs?graph=<iri>` | `GET`, `HEAD` | read | Graph Store, indirect identification |
| `/graphs?…`, `/graphs` | `PUT`, `POST`, `DELETE` | write | Graph Store writes, one commit each; `POST /graphs` mints a graph, `201` and `Location` |
| `/graphs/{**path}` | as the two rows above | as above | direct identification |
| `/feed` | `GET` | read | the change feed: `from`/`fromTime`, `to`/`toTime`, `graph`, `pattern`; `application/vnd.varve.delta; version=1` or `text/event-stream` |
| `/diff` | `GET` | read | `Diff(from, to)` in the same format |
| `/status` | `GET` | admin | id, head, head time, durability, settings, checkpoints, failure |

Plus `/live` and `/ready` on the server, anonymous. Headers: `Varve-As-Of`
(`position:<n>` or `time:<RFC 3339>`), `Varve-Position`, `ETag` (a position),
`If-None-Match` → `304`, `If-Match` → `412`, `Varve-Request-Id`, and the
trailer `Varve-Error` on a cut stream. Every error is an RFC 9457 problem
under `https://w3id.org/varve/problems/`.

### The decisions

All filed unaccepted (ADR 0066), so every citation is `CS0618` until the
maintainer accepts them — the one red this pull request carries.

| ADR | Decides |
|---|---|
| [0091](../adr/0091-varve-protocol-and-varve-server.md) | two packages, the update seam `ISparqlUpdateExecutor`, three policy names, the revisit condition |
| [0092](../adr/0092-protocol-scope-problem-details-and-tie-breakers.md) | protocol scope, SPARQL 1.2's `version`, problem details, the Oxigraph tie-breakers, the suites and their exemptions |
| [0093](../adr/0093-datasets-are-the-routing-unit.md) | datasets as the routing unit, the paths |
| [0094](../adr/0094-a-write-over-http-is-one-commit.md) | a write is one commit; agent `<issuer>#<percent-encoded subject>`; cause the request id; outcomes to status codes; `If-Match` |
| [0095](../adr/0095-a-read-over-http-is-pinned-for-its-response.md) | pinned reads, their lifetime, the cut stream |
| [0096](../adr/0096-time-travel-over-http.md) | `Varve-As-Of`, `ETag`, `304`, after Delta Sharing |
| [0097](../adr/0097-the-change-feed-and-the-diff.md) | the change feed and the diff, SSE for the live tail |
| [0098](../adr/0098-blank-nodes-at-the-protocol-boundary.md) | Q1 closed: stable labels out, fresh nodes in, no skolem IRIs; spec 1.6 |
| [0099](../adr/0099-register-jwtbearer-and-the-workflows-containers.md) | the register: `JwtBearer` in the server alone, conditional on AOT; no Testcontainers; the images |
| [0100](../adr/0100-authentication-tested-in-three-layers.md) | authentication tested in three layers |
| [0101](../adr/0101-the-server-configuration-aot-shutdown-readiness.md) | configuration, AOT, shutdown, readiness, status |

Dated amendments, each with a new key in its decision set: 0004
(`System.Uri` allowed in `Varve.Protocol` and `Varve.Server`, by
`eng/BannedSymbols.Uri.txt`), 0037 (`JwtBearer` is a package), 0042 (a
delivered commit externalises its handles; the append-only dictionary is the
invariant), 0060 (the protocol seam's revisit condition).

### The suites

Against an in-process server over the memory store and the file store, under
the ratchet:

| Suite | Cases | Passing, each store | Exempt, each store |
|---|---:|---:|---:|
| `sparql11/protocol` | 34 | 34 | 0 |
| `sparql11/graph-store-protocol` | 13 | 13 | 0 |
| `sparql11/http-rdf-update` (all `dawg:Deprecated`) | 18 | 12 | 6 |
| `sparql11/service-description` (three checks we wrote) | 3 | 3 | 0 |

Guard counts 34, 13 and 18. **The ratchet holds 2,927 lines**, up from 2,803
by exactly the 124 new passes, with no regression. The twelve exemptions, all
in the deprecated suite and each in `baseline/exemptions.txt` with its
section: two bodies in Turtle with no final `.` (Turtle 1.1 §2.4), the two
`GET`s that depend on them, a `DELETE` of a graph no step creates (GSP §5.4),
and a `HEAD` without `Accept` that expects Turtle (GSP §5.2 allows N-Triples).
The service description is checked against shapes in
`tests/fixtures/service-description/shapes.ttl` by a validator written in the
test project, which says so; the SHACL validator is milestone 8's.

**SPARQL 1.2 Protocol** (Working Draft, 23 July 2026): its only normative
addition is an optional `version`, as a parameter or a media-type parameter.
Adopted: `1.1`, `1.2-basic` and `1.2`, the parameter winning over the text's
`VERSION`, an unknown value `400`.

### The change-feed format

`docs/spec/change-feed.md`, format version 1,
`application/vnd.varve.delta; version=1`: records of lines ended by an empty
line — `commit <position> <kind> <timestamp>`, then optional `agent`,
`cause`, `scope` and `attachment` lines with canonical N-Triples terms, then
`+`/`-` change lines in canonical N-Quads; `diff <from> <to>` records; and an
`error <problem type>` record ending a cut stream. `#` comment lines, `#`
alone as the heartbeat. SSE frames each record as one event with its position
as `id`, so `Last-Event-ID` resumes. `ChangeFeedReader` parses it with
`Varve` types and passes the chunk-boundary oracle at every split.

During the server work the spec's sentence on SSE's `shutdown` event was found
to disagree with the endpoint. The endpoint's form was kept — every event's
data is a §2 record, and `id` always carries the position — and the spec now
says so.

### Properties

Eight properties in `Varve.Protocol.Tests`, **1,000 iterations each**, all
passing:

1. a sequence of updates over HTTP equals the same sequence in process, commit
   for commit;
2. the feed from 0 replays to the head's state;
3. resumption from any position delivers exactly the commits after it;
4. `(a, b]` then `(b, head]` equals `(a, head]`;
5. timestamp resolution on the feed and on `Varve-As-Of` agrees with I5 and
   Delta Sharing's asymmetry, over histories with repeated timestamps;
6. a stale `If-Match` is always `412` and never commits;
7. as-of over HTTP equals as-of in process;
8. concurrent writers at one position commit exactly once.

**Counterexamples.** One, and it was the generator's: it reused a blank-node
label across the operations of one update request, which SPARQL forbids, so
the parser rightly refused the request. Labels are now unique per operation.
None in the code under test.

### Allocation

The query path from request to response allocates **56 bytes a solution**,
which is the evaluator's row (8 × (3 + 1) + 24) and nothing
of the protocol's: the result writer's per-row cost is zero for terms the
dictionary already holds, and every term here was committed in process.

The test measures the whole process, because a request crosses Kestrel's
threads. Run alone it read 56 every time; **in the full pipeline it read 1,780
and 2,894**, because other test classes allocate in parallel and
least-of-six readings cannot exclude a neighbour that allocates during every
one. It now runs in a collection with parallelisation disabled, after every
parallel test, and read 56 in three consecutive full runs of the project.

### Authentication

| Layer | Where | Tests |
|---|---|---|
| (a) issuer in the test | everywhere, the sandbox included | every endpoint × no token, seven token flaws (expired, not yet valid, wrong audience, wrong issuer, bad signature, unknown key, unsigned) and each of read, write, admin; a permission on one dataset is not on another; the agent IRI with a percent-encoded subject; Zitadel's object-shaped role claim |
| (b) mock-oauth2-server 6.0.4 | `auth (mock-oauth2)`, Linux | client credentials writes and names its agent; authorization code with PKCE through the login form reads and is refused a write |
| (c) Zitadel v4.19.4 and PostgreSQL 18.6 | `auth (zitadel)`, Linux | client credentials writes with its project role; device code, approved through the session and OIDC APIs, reads and is refused a write |
| Entra ID, Google | `auth (providers)`, `main` only | one test each; skipped without secrets |

A refusal is `401` with a bare `Bearer` challenge and an empty body for every
flaw — `IncludeErrorDetails` is off — and `403` with no body naming the
dataset or permission. `Varve.Server.Tests` has 35 tests: 29 run everywhere,
and the six provider tests skip without their provider. Here, with Docker
started in the sandbox, the four container tests passed against the real
containers; `.devcontainer/auth-legs.sh` runs both legs in 27 s.

**Found while doing it.**
- **mock-oauth2-server has no device-code grant**: no
  `device_authorization_endpoint` in discovery, `405` from the path. The brief
  asked for device code there. It is tested in (c) instead, against a provider
  that has it, and (b) tests the authorization code with PKCE, a real
  interactive flow the mock does have. ADR 0100 says so.
- **A test issuer's "unknown key" was first written wrong**: the issuer's own
  key under an unknown `kid`, which the middleware rightly accepts, since it
  tries every published key. An unknown key is a key the JWKS does not
  publish; the test now signs with one.
- **Zitadel's API answers 503 for a few seconds after `zitadel ready`
  passes.** The seed waits for an authenticated call to succeed first.
- **Zitadel puts roles in the access token only with
  `accessTokenRoleAssertion`** on the application; the seed sets it.
- **Docker Hub rate-limited the sandbox's shared address** on the first
  PostgreSQL pull. The maintainer then asked for registries without anonymous
  limits; every image is now on ghcr.io or ECR Public, pinned by a digest
  resolved from the registry (the PostgreSQL index digest is the same on both).

### The local pipeline

`dotnet run eng/ci.cs` with `WarningsNotAsErrors=CS0618`, the one red this
pull request is allowed: every job passes, after the allocation test's
isolation above. The ratchet: **2,927 passing, 12 exempt, 0 newly passing,
0 regressed, 0 missing.** Without that property, the build fails on
`CS0618` alone.

### Native AOT

`Varve.Server` publishes for linux-x64 with **zero trim and AOT warnings**.

| | |
|---|---|
| Binary | **19,925,752 bytes** (19.0 MiB) |
| Process start to `/ready` 200, a memory dataset | 61 ms |
| Process start to `/ready` 200, a file dataset, via `eng/server-smoke.cs` | 114 ms |
| `SIGTERM` | exit 0; the next start reopens the file dataset with its commit |
| An invalid configuration | exit 2, every error listed |

Measured in the sandbox (4-core Xeon @ 2.10 GHz). CI's `native aot` job now
publishes the server on ubuntu and windows and runs the smoke there; its step
summary records each runner's size and time. `JwtBearer`'s condition in ADR
0099 — the AOT publish stays green — holds.

### Benchmarks

Not a gate. 4-core Xeon @ 2.10 GHz, 15 GiB, Ubuntu 24.04 cloud container;
client and server share the cores. Varve: the AOT binary. Oxigraph: its 0.5.11
server image, pinned by digest. 100,000 triples; ten seconds a row after two
of warm-up; `tests/Varve.Benchmarks/README.md` has the commands.

| Server | Store | Workload | Clients | Operations/s | p50 | p99 |
|---|---|---|---:|---:|---:|---:|
| Oxigraph | RocksDB | point query | 1 | 3,020 | 0.29 ms | 0.79 ms |
| Varve | memory | point query | 1 | 4,969 | 0.17 ms | 0.62 ms |
| Varve | file | point query | 1 | 4,412 | 0.19 ms | 0.82 ms |
| Oxigraph | RocksDB | point query | 8 | 13,612 | 0.49 ms | 2.03 ms |
| Varve | memory | point query | 8 | 23,910 | 0.25 ms | 1.99 ms |
| Varve | file | point query | 8 | 23,685 | 0.25 ms | 2.02 ms |
| Oxigraph | RocksDB, unsynced | `INSERT DATA` | 1 | 3,845 | 0.24 ms | 0.62 ms |
| Varve | memory | `INSERT DATA` | 1 | 3,102 | 0.27 ms | 0.95 ms |
| Varve | file, flushed | `INSERT DATA` | 1 | 1,337 | 0.70 ms | 1.91 ms |
| Oxigraph | RocksDB, unsynced | `INSERT DATA` | 8 | 11,382 | 0.64 ms | 2.00 ms |
| Varve | memory | `INSERT DATA` | 8 | 6,863 | 0.73 ms | 5.88 ms |
| Varve | file, flushed | `INSERT DATA` | 8 | 1,221 | 1.08 ms | 53.83 ms |

**The honest rows.** Varve reads faster. It writes slower: its memory row,
the one with Oxigraph's guarantee or less, is 19% behind with one writer and
40% behind with eight. With eight writers most of Varve's work is `409`s
(below). Pinned reads were on, with the default lifetime of two minutes.

### Proposed spec changes

1. **An update of `INSERT DATA` and `DELETE DATA` alone, sent without
   `If-Match`, commits with no expected position.** Today every update commits
   expecting its pin, with `ConflictRetries` 0 (ADRs 0057, 0094), so
   concurrent unconditional writers get `409`: in the eight-writer runs, about
   two per commit in memory and seven on files. Neither operation reads, so
   its effect cannot depend on the head it lands on, which is the reasoning
   ADR 0094 already applies to a Graph Store `POST`. An amendment to ADR 0057
   and to `sparql-update-store.md`; not made here, because 0057 is accepted.
2. **A feed record for `Settings` and `Erasure` content.** Format version 1
   names them and carries no content (it is read from `/status`). A version 2
   could carry it; recorded, not proposed for 7b.

Made in this pull request: specification 1.6 (Q1 closed, ADR 0098);
`sparql-update-store.md`'s `ExpectedPosition`; the change-feed spec's SSE
`shutdown` sentence.

### What 7b needs from the maintainer

1. **Accept ADRs 0091–0101** and the four amendments, which turns `CS0618`
   green. ADR 0092's tie-breaker table is the part the plan asked to be
   reviewed on the pull request.
2. **Apply `.github/repo-standard.yaml`**: the required checks
   `auth (mock-oauth2)` and `auth (zitadel)` (the server's AOT steps are in the
   already-required `native aot` jobs), and the `auth-providers` environment.
3. **The provider secrets**, in `auth-providers`, if those legs should run:
   an Entra test tenant with an API registration issuing v2 tokens and a
   client granted the `Varve.Write` app role (`VARVE_ENTRA_TENANT`,
   `_CLIENT_ID`, `_CLIENT_SECRET`, `_AUDIENCE`), and a Google service account
   key (`VARVE_GOOGLE_SERVICE_ACCOUNT`). Until then they skip.
4. **A decision on the `INSERT DATA` proposal** above.
5. **An upstream issue for DecisionDriven.Analyzers**: an
   `ArchContractTypeAssemblies` value separated by `;` is truncated at the
   first separator in the generated `.editorconfig`, so `DD0010` honours only
   the first assembly. Worked around by nothing: the repository now separates
   with commas, which the analyzer reads correctly. To be filed (the session's
   GitHub access is scoped to this repository).
6. **The release**: after 7b, as `0.1.0-preview.2`, as decided. Not cut here.

### Recorded for later

Replica bootstrap over HTTP — a snapshot manifest of the checkpoint and the
segments after it, with sizes, hashes and direct or presigned URLs, after
Delta Sharing's control-plane/data-plane split — is in `docs/roadmap.md`
under replication. Not built.

### Not built, as the brief says

`SERVICE` and `LOAD` over HTTP, the CLI, the admin API beyond `/status`, the
container image, telemetry, federation, Aspire, anything in `tools/`.
