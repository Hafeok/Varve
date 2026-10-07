# 0101 — The server: configuration, Native AOT, shutdown, readiness, status

## Status

**Proposed — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Decided by the maintainer on the 7a plan:
- "Fix the drain; no seal on shutdown";
- "ship GET /datasets/{name}/status as the one admin endpoint".

Acceptance is the maintainer's act on the pull request.

## Context

A server cannot be tested without configuration it validates, a way to stop
that loses nothing, and a signal that it is ready. The full operability
milestone (`docs/roadmap.md`, *Operability*) comes later. These three come now
because the conformance host and the auth tests need them. Constraint 2
requires Native AOT. ASP.NET Core supports it for minimal APIs with source
generation, and not for MVC.

`Dataset.DisposeAsync` stops maintenance and disposes the sequencer's
semaphore, but does not wait for a commit already inside it. A commit racing a
shutdown fails with `ObjectDisposedException` after its records may already be
durable.

## Decision

1. **Configuration through `IConfiguration`, bound by the source generator**
   (`EnableConfigurationBindingGenerator`), validated at start. The shape:

   ```json
   {
     "Varve": {
       "DatasetsRoot": "/var/lib/varve",
       "Datasets": {
         "people": { "Storage": "File" },
         "scratch": { "Storage": "Memory" }
       },
       "Auth": {
         "Mode": "Oidc",
         "Authority": "https://login.microsoftonline.com/{tenant}/v2.0",
         "Audiences": [ "api://varve" ],
         "SubjectClaim": "oid",
         "RoleClaimType": "roles",
         "Production": true,
         "Datasets": { "people": { "Read": [ "Varve.Read" ], "Write": [ "Varve.Write" ], "Admin": [ "Varve.Admin" ] } }
       },
       "Limits": {
         "QueryTimeout": "00:00:30",
         "ResultSizeCap": 1073741824,
         "MaxRequestBody": 104857600,
         "PinnedReadLifetime": "00:02:00",
         "FeedHeartbeat": "00:00:15"
       },
       "ForwardedHeaders": { "Enabled": false, "KnownProxies": [ ] }
     }
   }
   ```

   - A dataset's durability is its backend's (ADR 0073): `File` is
     `Synchronised` and `Memory` is `Volatile`. There is no per-dataset
     durability setting.
   - **Permissions are cumulative.** A claim value listed under `Write` grants
     `read` too, and one under `Admin` grants all three. A grant names a
     configured dataset, or the server does not start.
   - `RequireHttpsMetadata` defaults to `true`; it is turned off only for an
     issuer on loopback, as the tests' is.
   - Environment variables map with `__`, so `VARVE__AUTH__MODE=Anonymous`.
   - **An invalid configuration refuses to start.** The server lists every
     error and exits with code 2. It never falls back to a default for a value
     that was given and is wrong.
   - A missing `Limits` value takes the default shown, which the documentation
     states.
   - `Auth:Mode` has no default: it must be stated.
   - `Auth:Mode=Anonymous` with `Auth:Production=true` refuses to start.
     Anonymous mode otherwise logs a warning at every start (ADR 0037).
2. **Native AOT is a gate.**
   - `Varve.Server` publishes with `PublishAot` and builds with
     `WebApplication.CreateSlimBuilder`.
   - It uses minimal APIs only, with no MVC.
   - Problem details and the status body are JSON through a
     `System.Text.Json` source-generated context. There is no reflection-based
     serialisation.
   - The `native aot` CI job publishes it on Linux and Windows, the job's
     existing matrix, with zero trim and AOT warnings. `eng/server-smoke.cs`
     then runs it with a file dataset, waits for readiness, makes an update, a
     query and a feed read, stops it (`SIGTERM` and exit 0 where there is
     one), and records the size and the time to ready.
3. **The composition root wires the clock and the random source**:
   `TimeProvider.System` and the store's and evaluator's injected randomness,
   as ADR 0056 requires. Nothing below it reads an ambient clock.
4. **Graceful shutdown** on `SIGTERM` (`ApplicationStopping`):
   1. New writes are refused with `503`.
   2. Live feeds end with an SSE `shutdown` event, or an `error` record in the
      plain format.
   3. In-flight requests drain within the host's shutdown timeout, and their
      pins are released.
   4. Each dataset is disposed. **`Dataset.DisposeAsync` now waits for the
      sequencer**, so a commit in progress finishes and is durable before the
      dataset closes. Then it releases the lease.
   - **The active segment is not sealed on shutdown.** Sealing at every stop
     would leave an undersized sealed segment per restart in `log/`, the
     directory people copy and check in. An unsealed active segment is already
     recovered to its last closed commit (spec §4, the 6a failure suite).
5. **Readiness and liveness**, both anonymous:
   - `GET /ready` is `200` when every configured dataset is open, not failed
     (spec §7), and its default projection is at the head. Otherwise it is
     `503`, with a JSON body naming each dataset's state.
   - `GET /live` is `200` while the process serves requests.

   The full health model is the operability milestone's.
6. **`GET /datasets/{name}/status`** is the one `admin` endpoint of 7a. Its
   JSON body reports:
   - the dataset's `DatasetId`, head and head timestamp;
   - its durability;
   - its settings at the head;
   - its checkpoints;
   - whether it has failed.

   It lets the permission matrix show that `write` cannot administer. The
   admin API proper is milestone 7b.
7. **Forwarded headers** are honoured only when configured, and only from
   configured proxies. Direct graph identification (ADR 0093) depends on the
   request's address.

## Alternatives considered

- **Seal the active segment on shutdown** (the prompt's first wording).
  Rejected by the maintainer, for the small-segment cost above.
- **Default to anonymous when `Auth` is absent.** The convenient development
  default, and the configuration mistake that exposes a production dataset. ADR
  0037 requires it to be explicit.
- **Reflection-based configuration binding.** It works under JIT and warns
  under AOT. The source generator gives the same binding without the warnings.
- **Readiness as soon as the process listens.** A dataset still replaying its
  projection would then accept writes the sequencer refuses with `Unavailable`
  (spec T1).

## Consequences

- An operator learns about a mistaken configuration at start, all at once.
- `Dataset.DisposeAsync`'s drain is a store fix with its own regression test,
  and it changes no format.
- The AOT job runs on its two runners. Its size and startup time are reported
  in the 7a record.

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0037**: configuration surface, anonymous mode;
  - **0042**: the dataset's maintenance stops before its sequencer;
  - **0056**: the clock and randomness;
  - **0073**: durability per dataset;
  - **0075**: the lease, released after the drain.

  No conflict.
- **Layer ownership.** `Varve.Server` (6). The drain is `Varve.Store` (4).
- **Analyzer rule.** None.
- **Open questions owned.** None.
