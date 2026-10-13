# Milestone Client and integration, first part: on-behalf-of, the delta format, follow, setup, CORS

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The brief and the
> maintainer's messages are verbatim; the plan as submitted is summarised
> where it repeats the ADRs. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#93](https://github.com/Hafeok/Varve/issues/93), milestone Client and integration, its first part; the second part (events, catalogue, schema registry, the Event Hubs sample) and the close-out follow in a second pull request |
| **Date** | 2026-10-13 |
| **Tool** | Claude Code 2.1.296, a cloud session started from the Android app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata (`configured_model`, `session_context.model`, `last_served_model`) |
| **Session identifier** | `session_01VrCShTYmBH4vLxFXbaQoCh` |
| **Branch** | `ccr-6c00774e-dmy94v`, from `main` at 2472e1a (`v0.1.0-preview.4`); Commit path (#91) not yet merged |
| **Machine** | a cloud container, .NET SDK 10.0.401; no Docker daemon |

## The prompts

### The message that started the session

> Milestone Client and integration (#93), in parallel with Commit path (#91).
>
> The attached `varve-client-integration.zip` holds this session's prompt and its reference files. Extract it into an empty directory outside the repository, then:
>
> 1. Read `varve-client-integration/varve-claude-code-client-integration.md` in full. It is the session prompt and it governs this session; follow it exactly as if I had pasted it here. Its section 0 says what belongs to #91 and must not be touched.
> 2. The other two files are reference: `varve-roadmap-web-ui-milestone.md` (what item 4 checks) and `varve-roadmap-milestones.md` (the roadmap #91 is putting into `docs/roadmap.md`). Never commit the zip or the extracted folder.
> 3. Before planning, confirm the starting point: `main` is at 2472e1a and tagged `v0.1.0-preview.4` (or, if #91 has landed meanwhile, say so and start from the new `main`), #93 is open, and the session's identity hook has run and signs as the agent. If any of these fails, stop and tell me which one rather than working around it.
> 4. Then plan as the prompt says, and wait for my approval before writing code.

### The brief, `varve-claude-code-client-integration.md`

> # Session: client and integration
>
> The server is ready to be used by applications; this is what an application installs. It also makes the commits resource a contract other systems can integrate against, in RDF and in plain JSON. Start from `main` at 2472e1a (`v0.1.0-preview.4`). Read ADRs 0037, 0091–0123, the commit-path ADRs 0124–0126 on #91's branch as soon as they are pushed, `Varve.Protocol.Client`, `docs/operator/`, `docs/vocabulary.md`, `docs/problems/`, and the 7b and operability traceability records before planning. `AGENTS.md` applies in full. Plan first, wait for approval; one PR, red only on `CS0618`; the descriptor at the next free release number (expected `v0.1.0-preview.6`, unless Object storage lands first) naming the milestone issue, with `delta-format: 1` added to the descriptor schema; land through `land/`.
>
> ## 0. Running in parallel with Commit path (#91)
>
> The Commit path session is in flight and will land first. Until it does:
>
> - Do not add graph versions, `ETag`/`If-Match` handling or `Varve-If-Graphs-Match` to `Varve.Protocol.Client`, and do not touch the sequencer in `src/Varve.Store`. Those belong to #91 (its item A4 adds the client's graph version and conditional header). Where your work needs them (CORS exposing `ETag`, setup's baseline writes through the Graph Store Protocol), code against today's behaviour and name the seam in the plan.
> - The schema registry's refusal of breaking declarations is a dataset-level pre-commit validator through the existing contract (ADR 0058), not a change to the commit path.
> - Item 4's roadmap check: the Web UI text arrives with #91's docs commit. The attached `varve-roadmap-web-ui-milestone.md` is that text; if it is not on `main` when you get there, leave the check for the reconciliation.
> - When #91 lands: merge `main`, take its client API as given, reconcile the public API baselines and the problem catalogue, renumber your ADRs past #91's, and take the next free release number. Whichever of this session and Object storage (#92) lands second also reconciles `deploy/azure/` and `Varve.Aspire`.
> - CI containers (the Event Hubs emulator and the Azurite it needs) come from `mcr.microsoft.com`, never Docker Hub.
>
> ## A. Decisions (ADRs, Accepted unless stated)
>
> 1. **On-behalf-of.** A website or MCP server that holds a user's token for its own API exchanges it at Entra (OAuth 2.0 token exchange, the Entra on-behalf-of flow) for a token whose audience is Varve and calls Varve with it; Varve validates it as any bearer token and records the user as the commit's agent (`oid`, ADR 0037), not the application. What this session adds: (a) `Varve.Protocol.Client` takes a token provider called per request (a delegate returning the access token, with the token's expiry honoured), replacing the fixed token, so a caller acting for many users holds one client; (b) the operator guide's Entra page gains the chain: Varve's app registration exposing a scope and the `read`/`write`/`admin` app roles, the calling application's registration granted that scope, the exchange request, and which claims Varve reads (`oid` as the agent; `azp`, the acting application, recorded as a request-span attribute and never in the log); the two registrations are created by `deploy/azure/` through the Microsoft Graph Bicep extension; (c) `samples/` gains a minimal API that performs the exchange with MSAL (`Microsoft.Identity.Web` is allowed there, outside the shipped closure, and nowhere else) and queries Varve as the signed-in user; (d) a test against the in-process issuer: a token minted with a user's `oid` and an application's `azp` is accepted, the commit's agent is the user, and the span carries the application; the Entra leg runs the real exchange when its secrets exist. The guide states the attribution rule plainly, with an example from the commits list: a write made through on-behalf-of is recorded with the end user as its agent; a write the application makes on its own account (an import, a scheduled publish) is made with the application's client-credentials token and recorded with the application as its agent; the two are different agents in the history, and an application that routes its own writes through a user's token misattributes them to that user. This is a design choice the caller makes per write, and the guide says so.
>
> 2. **Client-side setup.** Setup in Varve is data, and data writes are idempotent by construction (I2: re-asserting what exists is an empty effective delta and no commit), so a consumer application's setup is declarative and repeatable without migration machinery for the common case. `Varve.Protocol.Client` gains `DatasetSetup`: dataset name and storage, settings, baseline sources (streams or embedded resources in any syntax Varve reads, each with its target graph), and an ordered list of named migrations for changes that are not idempotent by nature (retractions, renames). `client.ApplyAsync(setup)` does, in order: `PUT /datasets/{name}` (idempotent), `PUT …/settings` under `If-Match`, the baseline through the Graph Store Protocol (one commit the first time, none afterwards), then each migration not yet recorded; a migration that has run is recorded as data in a system graph (`varve:setup`: the migration id and the position it committed at, under the `https://w3id.org/varve/ns#` vocabulary, added to `docs/vocabulary.md`), queried before applying, so the record is time-travellable and visible in the commits list like everything else. Grants are server configuration and are out of scope by design: an application cannot grant itself permissions, and the guide says so. The guide's page on datasets gains a short section on choosing the boundary: a dataset is the unit of consistency, history, lease, throughput, lifecycle and erasure; graphs are the partition inside one history and the unit of scope; in DDD terms a dataset is a bounded context (one vocabulary, one writer, integration through its commits feed), a tenant is a shard of a context rather than a context, and a second context within a tenant is a second dataset, with the setup baseline keeping a context's vocabulary identical across its shards. Hosting: `AddVarve(options)` registers the client and a hosted service that applies the setup at startup, the same class behind `varve apply <setup.yaml>` in the CLI and behind `Varve.Aspire`'s `WithDataset`; failure to apply fails startup loudly with the problem body. Tests: applying twice yields exactly one commit per baseline source and per migration; a migration list with a new entry applies only that entry; a failed migration leaves the dataset at the previous head and the record unchanged (one request, one commit, nothing partial); the sample application uses it.
>
> 3. **The commits resource as the integration stream.** It already has the properties integration needs (total order per dataset, resumable by position, at-least-once, byte-deterministic, retained forever, filterable by graph); this item makes the contract explicit. (a) `Varve.Protocol.Client` gains `FollowAsync(dataset, from, handler, checkpoint)`: tails by range then by SSE, hands each commit to the handler, advances only after the handler returns, and stores the position through a small checkpoint interface the caller implements (a file, a table, a quad in the consumer's own dataset); the at-least-once contract is stated on the method. (b) The delta format comes under the read-forever rule like the storage format: version 1 is frozen, the release descriptor carries `delta-format: 1` checked against the writer's constant, `status.cs` reports it; the ADR says a consumer parsing version 1 must never break. (c) `samples/` gains an Event Hubs bridge: a follower that publishes each commit as one event with the position as the partition key and sequence, built on (a), with `Azure.Messaging.EventHubs` (managed) in the sample only. (d) The guide's integration page states the pattern for domain events: the events are quads; a publisher that wants named domain events writes them as quads into an events graph in the same commit as the change they describe, so the feed filtered to that graph is the domain stream and is atomic with the data; the sample does this. (e) **The same events for systems that do not speak RDF.** The events graph stays the single source of truth; a projection renders its events as CloudEvents 1.0 in structured JSON: `id` the position and the event's IRI, `source` the dataset's URL, `type` a declared short name, `time` the commit's timestamp, `subject` the event's subject, `data` plain JSON. Served by `GET /datasets/{name}/events?from=&to=` (batch JSON, `application/cloudevents-batch+json`) and as SSE on an open range, with the commits resource's position semantics, so resume and replay are the same; the Event Hubs bridge publishes these, not deltas. Each event type is declared as data in the dataset (applied by the setup baseline of item 2): its class IRI, its short name, and a SPARQL `SELECT` over the event node whose variables become the `data` fields, with a fixed XSD-to-JSON mapping (numerics to numbers, booleans, date and time types to RFC 3339 strings, IRIs and other literals to strings, language and direction dropped unless declared); a JSON Schema per type is generated from the declaration and served at `GET /datasets/{name}/events/types/{type}`. A commit renders with the declarations as of its own position, so replaying old events yields the JSON they had then, and a change of declaration is itself a commit in the history. Output is canonical JSON (RFC 8785), so the same commit always renders to identical bytes. An event whose `SELECT` yields no row or more than one is not rendered and is reported as a problem on the stream, never guessed. **The events a dataset exposes are discoverable.** The declarations live in an event catalogue graph (`varve:events-catalogue`: each type's IRI, short name, description, source graph, the `SELECT`, and the generated JSON Schema), queryable with SPARQL like any data and as of any position, so "which events did this dataset expose then" is an as-of query. The service description links the catalogue. `GET /datasets/{name}/events/types` lists the types in JSON with their schema links, and `GET /datasets/{name}/events/asyncapi` serves an AsyncAPI 3.0 document generated from the same declarations (channels per type, payload schemas, the CloudEvents binding, the SSE and Event Hubs servers when configured), so tooling that does not speak RDF can render and generate clients from it. Grants apply throughout: a caller sees only event types whose source graph it can read, in the catalogue, the list and the AsyncAPI document alike. **The server is also a small schema registry.** `GET /events/types` lists the event types across every dataset the caller can read, each with its dataset, so an integrating system finds events without knowing where they live. Every schema has a stable `$id` that includes the type's version (`…/events/types/{type}/versions/{n}`), and every version ever declared stays served, since the history keeps the declarations; the unversioned URL resolves to the current one. A change to a declaration is checked against the current version: adding an optional field is compatible and keeps the version; removing a field, changing its JSON type, or making it required is breaking, and the commit is refused (`422`, a problem type naming the incompatible fields) unless it declares a new version. Old events always render with the version in force at their position, so a consumer pinned to version 1 can replay the whole history and receive version-1 events. The guide's integration page shows one event type end to end: the quads written in the commit, the declaration, the CloudEvent a consumer receives, and its schema.
>
> 4. **CORS and the roadmap entry for the web UI.** The server gains a CORS configuration (`Varve:Cors:AllowedOrigins`, default none, `Authorization` and the `Varve-*` headers allowed, `Varve-Position` and `ETag` exposed) so a browser application can call the API; off means no `Access-Control-*` headers at all. The Web UI milestone text is already in the roadmap (placed by the Commit path session, item 0); check it is there and that it names CORS as satisfied.
>
> ## B. Definition of done
>
> - On-behalf-of: the client's per-request token provider under the 7a auth matrix; the sample passes a query and an update as the signed-in user through the in-process issuer in CI, and through Entra on the optional leg.
> - Schema registry: the server-wide list equals the union of the per-dataset lists under each caller's grants; a compatible change keeps the version; every breaking change in a generated set of declaration edits is refused unless it declares a new version; every historical version stays retrievable at its `$id`.
> - Event catalogue: the list, the AsyncAPI document and a SPARQL query over the catalogue graph agree on the declared types at every position of a generated history; the AsyncAPI document validates against the AsyncAPI 3.0 schema; a caller without read on a type's source graph sees that type in none of the three.
> - Events as JSON: for generated histories, rendering every commit twice yields identical bytes; a commit rendered after its declaration changed later still renders with the declaration in force at its position; every rendered event validates against its generated schema; a zero- or multi-row `SELECT` produces the problem and no event.
> - Integration: a follower resumed from every position of a generated history handles every commit exactly once per checkpoint advance and at least once across a crash between handler and checkpoint; the Event Hubs sample runs against the Event Hubs emulator in CI.
> - Setup: the idempotence and partial-failure tests above; `varve apply` and the hosted service exercised against the in-process server in CI.
> - CORS: a browser-origin request with and without a configured origin, in the protocol tests; no `Access-Control-*` header when unconfigured.
>
> ## Non-goals
>
> The object-storage backend, the Web UI, an Event Hubs package (the bridge stays a sample until a second consumer wants it), JSON-LD compaction for events (declared `SELECT` mappings instead, and the ADR says why).
>
> ## Report
>
> The traceability record and PR body: ADR numbers; the client's public API added, as the baseline diff; the event rendering and catalogue properties with iteration counts; the AsyncAPI document for the sample's event types; the setup tests; what the learning track can now teach.

### The plan, as submitted

The starting point held: `main` at 2472e1a tagged `v0.1.0-preview.4`, #91
unmerged with ADRs 0124–0126 pushed on its branch, #93 open, commits signed as
the agent. The plan filed ADRs from 0127 and worked in slices: ADRs; CORS; the
token provider and on-behalf-of; the delta format under the read-forever rule;
`FollowAsync`; setup; events; the catalogue and registry; the Event Hubs
sample; the guide and close-out; reconciliation with #91. It put ten decisions
to the maintainer, each with a recommendation: where the delta reader lives;
the catalogue's visibility under graph grants; where the generated JSON Schema
lives; the Event Hubs partition key; a migration record's position; blank
nodes in baselines; self-contained events; integers beyond 2^53; where
`AddVarve`, the YAML reader and Aspire's setup live; the CORS header lists. It
named two conflicts (the Web UI text's "the Azure backend session adds" CORS;
the release number) and three risks (MSAL against a test issuer, the register
not enforcing scope, `deploy/azure/` shared with #92).

### The approval of the plan

> Approved, with these answers. Where I differ from your recommendation, the reason is given.
>
> ## Decisions
>
> 1. **The delta reader and writer go in a new layer-2 package, `Varve.Delta`, not in `Varve.Store`.** If they lived in the store, every application that uses `FollowAsync` would pull `Varve.Store` into its client closure. A consumer that only reads the feed must never ship the store. The delta format is an RDF syntax with its own specification (`change-feed.md`) and its own read-forever rule, so it is the same kind of package as `Varve.Turtle`. Express it over `Varve.Rdf` types plus its own small header types (position, timestamp, agent, cause), and have the store and `Varve.Protocol` map log entries to it. `DeltaFormat.Current` lives there, and the descriptor check reads it. This is a new package, so it needs its ADR. Moving the code out of `Varve.Protocol` is a move, not a rewrite; keep the existing feed tests green across it.
>
> 2. **No filtering of one catalogue graph by its content.** That is row-level visibility, and row-level access is milestone 9's job; a second visibility mechanism must not arrive early, inside an event feature. Use one catalogue graph per source graph, `varve:events-catalogue/{source}`, and derive its grants in `IAccessScopes` (ADR 0107): reading it requires read on the source graph, writing it requires write on the source graph. Then the existing `GraphScopedQuadSource` gives "sees the type in none of the three" for SPARQL, the list and AsyncAPI alike, with no new filter. The cross-catalogue view is a union over the catalogue graphs the caller can read. Record the derived grant in the 0107 amendment.
>
> 3. **Compute the schema on read: yes,** with one condition. Since the schema isn't stored, the generator's output for a declaration is under the read-forever rule: golden files per version, and a change to the generator that alters an existing version's schema fails CI. A served `$id` must never change content.
>
> 4. **Partition by dataset: yes.** Carry the position in `id` and in the CloudEvents `sequence` extension attribute.
>
> 5. **Migration record position from the commit that asserted it: yes.**
>
> 6. **Refuse baselines that contain blank nodes: yes.** The problem names the source and points to skolem IRIs (RDF 1.1 Concepts §3.5), so the fix is obvious to the user.
>
> 7. **Self-contained events: yes, but not over the commit's delta.** Evaluate the `SELECT` over the events graph's quads whose subject is the event node, with blank-node closure, as of the commit's position. For a fresh event node that equals the delta. It also stays correct when I2 empties part of the delta because a quad already existed. It is still a pure function of position and declaration.
>
> 8. **Numbers.** Render as a JSON number only when the value round-trips through a double exactly. That covers integers beyond 2^53 and `xsd:decimal` with more digits than a double holds. Otherwise the event is not rendered and a problem is reported, unless the declaration maps that field to `string`, which is allowed and then always used.
>
> 9. **Hosting: yes to all three,** with one condition on the YAML. The YAML subset is a user-facing format, so specify it on its doc page and refuse anything outside it with a line and column. No silent acceptance.
>
> 10. **CORS: yes,** plus two things. Never send `Access-Control-Allow-Credentials`: bearer tokens travel in `Authorization` and need no credentials mode. Set a preflight `Access-Control-Max-Age`. On reconciliation with #91, add `Varve-If-Graphs-Match` to the allowed headers.
>
> ## Conflicts and risks
>
> - **Roadmap line:** change "the Azure backend session" to this milestone on reconciliation: agreed. **Release number:** the next free one: agreed.
> - **MSAL against the in-process issuer: don't build a fake exchange.** Varve's contract is only "accept the token and record `oid` as the agent". Test 1(d) proves that with a minted token. The sample performs the exchange through MSAL against Entra only, on the optional leg. A plain-POST exchange would be test code that proves nothing about Entra.
> - **Dependency register scope check: yes.** Ship it as a gate with a failing fixture: a samples-only package referenced from `src/` is red.
>
> ## Split into two PRs
>
> This is too large for one review, and the events tranche is the riskiest design in it. Land it as two releases under #93:
>
> - **First PR:** slices 1–6 and 9's CORS part, meaning ADRs, CORS, the token provider and on-behalf-of, `Varve.Delta` and read-forever, `FollowAsync`, and setup. Its descriptor does not close #93; it uses `Refs #93`.
> - **Second PR:** events rendering, the catalogue, the schema registry, the Event Hubs sample and the close-out, with `Closes #93`.
>
> If the descriptor rules (ADR 0102) don't allow a release that names an open milestone issue, say so and we'll open a sub-issue for the first half instead.

The session answered that ADR 0102 does not allow it: every `issue:` line of
a basis must be closed by a `Closes #N` trailer line in the release's range
(`docs/releases.md` §2.5, `eng/release.cs`), and a descriptor needs at least
one. The first part's descriptor therefore waits on a sub-issue of #93.

## The report

### What shipped, by ADR

All filed unaccepted (ADR 0066); every citation of a new ledger key is
`CS0618` until the maintainer accepts it, and the build is red on that alone.

- **0127, on-behalf-of.**
  - `SparqlHttpClient` takes a `BearerTokenProvider`, called for every
    request. One client serves many users. A token within thirty seconds of
    its expiry is asked for once more, and refused with
    `BearerTokenExpiredException` before a byte is sent when it is still
    stale. Expiry is measured on the client's `TimeProvider`.
  - `AccessToken` is removed.
  - The CLI renews a token a minute before its `expires_in`, one renewal at a
    time, so `feed --follow` outlives a token.
  - `Auth:ApplicationClaim` (`azp`) reaches the span as `varve.azp` through
    `ICallerIdentity.ApplicationOf`, and never the log.
  - The operator guide's Entra section gains the chain, the claims and the
    attribution rule, with a commits-list example.
  - `deploy/azure/entra/main.bicep` (Microsoft Graph extension 1.0.0) creates
    both registrations, the consent and the caller's `Varve.Write`
    application permission.
  - `samples/Varve.OnBehalfOf.Sample` exchanges with MSAL through Microsoft
    Identity Web. Its test runs against Entra only, and is skipped without
    its secrets.
- **0128, `Varve.Delta`.**
  - The reader, the record model and the writer move to a layer-3 package
    over `Varve.Rdf` and `Varve.Turtle`, with its own `FeedPosition`,
    `FeedTimestamp`, `FeedCommitKind` and `FeedProblemType`.
    `Varve.Protocol` maps a `Commit` to it, and `Varve.Store` does not
    reference it.
  - The client no longer references `Varve.Store`, and
    `ClientLimits.MaxResponseBytes` is a `long`.
  - `DeltaFormat.Current` is 1, and the media type is composed from it.
  - The descriptor's basis gains `delta-format`, required when the commit
    carries the constant. It is checked, drafted and summarised by
    `eng/release.cs` and reported by `eng/status.cs`, with two release
    fixtures.
- **0129, `FollowAsync`.**
  - It reads paged ranges, then the SSE live tail resumed by
    `Last-Event-ID`.
  - It calls the handler, then the checkpoint.
  - It waits out transient failures with backoff, and draws jitter only from
    a caller's source.
  - `IFollowCheckpoint` and `FileFollowCheckpoint`.
- **0130, setup.**
  - `DatasetSetup`, `ApplyAsync` and the system graph `varve:setup`.
  - Each migration is one commit together with its record. Concurrent
    appliers are safe.
  - Blank nodes are refused in all four syntaxes.
  - The Graph Store writes and the dataset `PUT` are in the client.
  - `DatasetSetupFile` reads the YAML subset specified in
    `docs/operator/datasets.md`, refusing at line and column.
- **0131, hosting.**
  - `AddVarve` and `DatasetSetupService` in the client.
    `Microsoft.Extensions.DependencyInjection.Abstractions` and
    `Microsoft.Extensions.Hosting.Abstractions` 10.0.12 are admitted, scoped
    to the client.
  - `varve apply`.
  - `Varve.Aspire`'s `WithDataset(…, setup:)`, `WithSetupClient` and
    `WaitForDatasetSetup`.
  - Both samples use the setup.
- **0132, CORS.**
  - `Varve:Cors:AllowedOrigins` and `MaxAge`.
  - Never credentials. The headers are listed one by one. The middleware
    runs first.
- **0133, register scopes.**
  - `Scope` on a register entry, checked by `eng/dependency-register.cs`,
    with `tests/fixtures/dependency-register/` and the `register-fixture`
    job.
  - `Microsoft.Identity.Web` 4.16.0 is admitted for `samples/`.
  - The entries that said "Varve.Server only" now carry that scope.
- **Amendments:**
  - 0009: the scope;
  - 0097: the format's home and lifetime;
  - 0102: `delta-format`;
  - 0103: the provider, and no store;
  - 0112: `varve.azp`;
  - 0117: `WithDataset`'s setup.

### Where the work departed from the decisions, and why

- **`Varve.Delta` is layer 3, not layer 2.** The reader parses terms with
  `Varve.Turtle`'s N-Quads parser and writes them with its canonical writer.
  A layer-2 package may not reference another layer-2 package, and a second
  copy of the term grammar is what constraint 6 exists to prevent. The
  maintainer's reason, that a feed consumer must not ship the store, holds at
  layer 3.
- **The client's `Varve.Store` reference is removed**, which the decision
  presupposed but the code did not have. It was there for `ByteCount` alone.
- **The setup's first step is a read, not the status endpoint.** The status
  endpoint needs `admin`, and an `ASK` needs `read`.
- **A stale `If-Match` answers `409 conflict` on this server, not `412`.**
  Setup retries on either. A Graph Store `PUT` expects the position it read
  at, so two appliers' baselines conflict as well, and a baseline write is
  retried on a conflict since it is idempotent.
- **A baseline file's UTF-8 byte order mark is dropped.** Editors write one,
  and no syntax has a place for it.
- **A setup's settings are refused in anonymous mode**, since a settings
  commit names an agent (ADR 0106). The guide says so.

### Public API, as the baseline diff

| Package | Added | Removed |
|---|---:|---:|
| `Varve.Delta` (new) | 153 | 0 |
| `Varve.Protocol.Client` | 147 | 4 |
| `Varve.Protocol` | 11 | 53 |
| `Varve.Aspire` | 3 | 1 |

- **`Varve.Protocol.Client` removed:** `AccessToken` (get and set) and the
  `ByteCount` forms of `ClientLimits`.
- **`Varve.Protocol.Client` added:**
  - `BearerTokenProvider`, `BearerToken`, `BearerTokenExpiredException`;
  - the provider constructor, and `TokenExpirySkew`;
  - `FollowAsync`, `FollowOptions`, `FollowRefusedException`,
    `IFollowCheckpoint`, `FileFollowCheckpoint`;
  - `DatasetSetup`, `BaselineSource`, `BaselineMode`, `SetupMigration`,
    `SetupStorage`, `SetupResult`, `SetupException`;
  - `ApplyAsync`, `PutDatasetAsync`, `PutGraphAsync`, `PostGraphAsync`,
    `DeleteGraphAsync`, `SetupGraph`;
  - `DatasetSetupFile` and `SetupFileException`;
  - in `Hosting`: `AddVarve`, `VarveClientOptions`, `DatasetSetupService`.
- **`Varve.Protocol`:**
  - removed: `ChangeFeedReader`, `ChangeFeedFormatException`, `FeedRecord`,
    `FeedChange` and their kinds, which moved to `Varve.Delta`;
  - added: `ActingApplication` and `ICallerIdentity.ApplicationOf`.
- **`Varve.Aspire`:** `WithDataset` gains `setup`, and `WithSetupClient` and
  `WaitForDatasetSetup` are new.

### Properties and tests

| Property | Iterations | Where |
|---|---:|---|
| a written delta document reads back to itself, whole and split at a generated offset, and writes back byte for byte | 1,000 | `tests/Varve.Delta.Tests/RoundTripTests.cs` |
| a version-1 document kept as a fixture is read and written back byte for byte | 1 (fixed) | same |
| resumed from every position of a generated history (1–9 commits, paged by three), each later commit is handled exactly once, in order, the checkpoint advancing with it | 100 histories | `tests/Varve.Protocol.Client.Tests/FollowTests.cs` |
| a crash between handler and checkpoint at a generated commit hands that commit over again on restart, and no other twice | 100 histories | same |

**Setup tests** (`SetupTests`, `SetupFileTests`, `ApplyTests`):
- applying twice commits once per baseline source and per migration, then
  nothing;
- a new migration applies alone;
- a failed migration (a refused `LOAD`) leaves the head and the record as
  they were;
- a migration that does not parse commits nothing;
- blank nodes are refused in Turtle, N-Triples, JSON-LD and RDF/XML before a
  request;
- two appliers at once apply each migration once, stable over repeated runs;
- `AddVarve` applies at start, and a broken setup fails the start;
- the setup file reads to its setup, and 21 constructs outside the subset are
  refused at their line and column;
- `varve apply` creates, then finds the dataset as declared;
- `varve apply` exits 2 for a file outside the subset and 1 for a refusal.

**Others:**
- CORS: 10 tests;
- the token provider: 5;
- on-behalf-of against the in-process issuer: 2, with the agent the user,
  `varve.azp` on every span, and an application's own token committing as the
  application;
- the release and register fixtures.

### What this environment could not do

- **No Docker daemon.** The Aspire sample's test, now waiting for
  `varve-setup-people` and reading its record, runs in CI's `aspire` job
  only, against the image CI builds from this branch.
- **No Entra secrets.** The on-behalf-of sample's exchange is skipped here.
  It needs the following in the `auth-providers` environment:
  - `VARVE_ENTRA_OBO_CALLER_ID`, `…_SECRET`, `…_AUDIENCE`;
  - a test user without MFA, `VARVE_ENTRA_OBO_USERNAME` and `…_PASSWORD`.

### What the maintainer is asked for

- **A sub-issue of #93 for this part**, which its descriptor closes. The
  descriptor and the README's Status line follow once it exists.
- **The Entra secrets above**, for the optional leg.
- **When #91 lands:**
  - merge it, and take its client API as given (graph versions, `ifMatch` on
    the Graph Store writes this part added without it);
  - add `Varve-If-Graphs-Match` to the CORS request headers;
  - scope migrations by `Varve-If-Graphs-Match` on `varve:setup`;
  - reconcile the public API baselines and the problem catalogue;
  - fix the roadmap's Web UI line ("the Azure backend session adds it");
  - take the next free release number.

### What the learning track can now teach

Each of these is a runnable sample:

- **Setup as data**, in the "schema without migrations" chapter: a
  declarative baseline that commits nothing on the second apply, and the
  rare migration recorded as data in the dataset's own history.
- **The commits feed followed at least once** with a checkpoint, in the "CDC
  and the outbox" chapter.
- **The attribution rule**, in the "commits, deletion and history" chapter:
  the user's writes and the application's own as different agents in one
  history.
