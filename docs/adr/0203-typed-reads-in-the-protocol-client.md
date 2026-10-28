# 0203 — Typed reads in `Varve.Protocol.Client`: health, problems, response facts and the management plane

## Status

**Accepted — filed unaccepted by Web UI part 1 (#167, milestone #96),
2026-10-27** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 1 plan (#96, 2026-10-27):
- every screen of the Web UI reads the server through `Varve.Protocol.Client`
  only, and what a screen needs that the client does not expose, the client
  gains as public API, in the baseline diff;
- the convention a typed read follows is checked against ADR 0118 and against
  how `SparqlHttpClient` reports a problem today, and this ADR says which it
  aligns to and why (point 2).

Records decision set [`typed-reads-in-the-protocol-client`](../decisions/typed-reads-in-the-protocol-client.md).

## Context

The Web UI (ADR 0200) shows what the server serves: readiness, the management
plane's resources, the commits, and every problem. `Varve.Protocol.Client`
(ADR 0103) is the typed client any .NET caller uses. As it stands, it has
these gaps:

- **Raw responses.** Its request methods answer `HttpResponseMessage`, leaving
  the caller to read the headers and the body.
- **No health, diff or service description.** It has no method for any of
  them.
- **Only the dataset-scoped management routes.** It reaches
  `/manage/datasets/{d}/…` and nothing else under `/manage/`: not the list,
  `/manage/system`, `/manage/fleet`, `/manage/graphs`, `…/grants` or
  `…/bindings/{b}`.
- **No problem type.** A problem reaches a caller only as the string on
  `FollowRefusedException.Problem` or `SetupException.Problem`.
- **No `Varve-Request-Id`.** It reads `Varve-Position` and `ETag`, but never the
  request id.
- **One token for both planes.** A `SparqlHttpClient` takes one
  `BearerTokenProvider`, and so one token. A token that names both planes'
  audiences is refused by both (ADR 0163 point 1), so a caller of both planes
  holds two tokens.

`varve apply` and `varve manage` call `/manage/` with their own `HttpClient`
code.

**How the client reports an answer today:**
- **The request methods** of `SparqlHttpClient` answer the response whatever
  its status. They throw only when there is no answer to give: the headers
  did not arrive within the limits (`HttpRequestException`), or the token is
  known to have expired (`BearerTokenExpiredException`, ADR 0127).
- **The operations built on them throw** a typed exception carrying the
  status and the problem's text when the server refuses: `FollowAsync`
  (`FollowRefusedException`) and `ApplyAsync` (`SetupException`). They are
  loops and multi-step procedures with no single response to hand back.

ADR 0118 is about the server's routes and resources: state, settings and
commits as resources, and paths removed rather than aliased. It sets no
convention for how a client reports a refusal, so this ADR has nothing there
to align with or depart from.

## Decision

1. **A typed read answers a `VarveResult<T>`.** It holds:
   - the value, when the response held the resource;
   - the problem, when the response was one;
   - both, when a problem's body is itself the resource: `503 not-ready` is a
     readiness;
   - always, the response's facts.

2. **An answer from the server is a value, never an exception.** This aligns
   with the request methods of `SparqlHttpClient`, which answer every status,
   and not with `FollowAsync` and `ApplyAsync`.
   - A typed read is one request with one answer. For a caller that shows
     what the server said, a `403` or a `409` is content: the Web UI renders
     it in the screen's place.
   - An exception would make every caller write the same catch to get the
     problem back.
   - Exceptions stay for no answer: the timeout, an expired token, and a
     body over the limits' cap.
   - `FollowAsync` and `ApplyAsync` keep their exceptions, for the reason
     above.

3. **A problem is read whole.** `Model.Problem` holds:
   - `Type`, a `ProblemType` wrapping the IRI, with `about:blank` when the
     body names none;
   - `Title` and `Detail`;
   - `Status` as `HttpStatusCode`, the body's or else the response's;
   - `Instance` as a `RequestId`;
   - every other member as a `JsonElement`, by name.

   The client fixes no catalogue of its own. A type or a member a newer
   server adds is carried, not dropped, and `ProblemType.Name` gives the
   name of its page in `docs/problems/`.

4. **A problem's title and detail are display text.** They are strings, as
   are readiness's detail and a dataset's reason: text a person reads, which
   nothing compares, parses or routes on. The type is the half a program
   reads, and it is a wrapper. This is the same ruling as
   `FailureTextIsDisplayText` (ADR 0069), for the protocol's problems.

5. **The response facts.** `Model.ResponseFacts` holds the status and:
   - `Varve-Position` and `Varve-Management-Position`, as `Varve.Delta`'s
     `FeedPosition`. That is the position the delta format writes, and the
     client's contract types already include `Varve.Delta`.
   - `Varve-Request-Id`, as a `RequestId`, or else the problem's `instance`.
     A problem the host writes before an endpoint runs names its request only
     there.

   A response that names no position has a null position, and a caller says
   so rather than inventing one.

6. **The server's vocabulary is a wrapper over the string it writes.**
   `InstanceRole`, `ReadinessState` and `BootstrapState` each give the values
   known today as static members. A value the client does not know is carried
   as written, so a state Replication adds (#97) is shown before it is
   named.

   Epochs are `WriterEpoch` and lags are `PositionLag`, a count of positions
   and not a position. Each is a readonly record struct over one primitive,
   per `WrappersAreReadonlyRecordStructs` (ADR 0065).

7. **Health.** `VarveHealthClient(HttpClient, Uri server, ClientLimits)`
   reads `/health/live` and `/health/ready` with no token, since neither
   authorises. `ReadyAsync` answers a `Readiness` both ready and not ready.
   - **Ready:** the body lists every dataset.
   - **Not ready:** it lists only those neither ready nor closed.
   - **Either way:** members the client does not know are kept, on the
     readiness and on each dataset.

8. **The management plane has its own client and its own token.**
   `VarveManagementClient(HttpClient, Uri server, ClientLimits,
   BearerTokenProvider?, TimeProvider?)` reads the routes under `/manage/` as
   typed reads. Its provider answers a token for the management audience
   only; a data-plane `SparqlHttpClient` keeps its own. One provider is one
   audience, so no caller of this client ever needs a token that names both
   planes.
   - **Its reads:**
     - `DatasetsAsync`, `LifecycleAsync`, `StatusAsync` and `SystemAsync`
       (JSON);
     - `DeclarationAsync`, `FleetAsync` and `GraphsAsync`, TriG as served,
       with its version;
     - `PlacementsAsync`: each dataset's `varve:placedOn`, by one query on
       `/manage/sparql`, so placement is read without parsing TriG in the
       client's caller.
   - **Unknown members are kept,** in each value's `Members`, as readiness
     keeps them.
   - **`SparqlHttpClient` reads commits whole:** `ReadCommitsAsync(from, to)`
     and `ReadCommitAsync(p)` return the line format's records through
     `Varve.Delta`'s reader, with the next page's start when the server pages
     the range.

9. **Nothing is removed.** The existing dataset-scoped management methods of
   `SparqlHttpClient` stay as they are. The baseline diff adds lines and
   removes none. Moving `varve apply` and `varve manage` onto the new client
   is a follow-up, not this ADR's.

10. **Sending stays one path.** The send under the limits' timeout is one
    internal helper, which `SparqlHttpClient` and the new clients share.

## Alternatives considered

- **Throw a typed exception for every problem**, as `FollowAsync` does. One
  convention for the whole client. Refused: every Web UI screen would catch
  to render what it caught, and a `503 not-ready` whose body is the resource
  would be read out of an exception.
- **Answer `HttpResponseMessage` and let the caller read it**, as the request
  methods do. That reopens the gaps the Context lists, in every caller.
- **Model problems by a catalogue in the client**, one type per problem.
  Refused: the client would be out of date the day the server adds a type, and
  the catalogue already lives in `Varve.Protocol`, which the client must not
  reference (both are layer 5).
- **Enums for roles and states.** A value a newer server writes would have no
  member, and would have to be carried beside the enum anyway.
- **`long` positions, with a decision exempting them.** The client already
  takes `FeedPosition` from `Varve.Delta`, which is the format's own position.
  The rule's answer is the design change.
- **A source-generated `JsonSerializerContext`.** It is AOT-clean too, but it
  binds a fixed shape. The client's existing idiom is `JsonDocument` read
  leniently, which is what keeping unknown members needs.

## Consequences

- `Varve.Protocol.Client`'s baseline gains:
  - the health client and the management client;
  - `VarveResult<T>`, `ResponseFacts` and `Problem`;
  - the readiness model;
  - the wrappers.

  No line is removed, so it is not breaking.
- The Web UI's screens read through these, and its component tests render
  fixtures captured from a running server through them, so a change of a
  member the client reads fails there.
- **Checks:**
  - Against the accepted ADRs: 0103 (the client), 0118, 0119 (problems,
    `Varve-Request-Id`), 0127 (the token provider), 0128 (`FeedPosition`),
    0163 (two audiences) and 0065 (wrappers). No conflict.
  - Layer ownership: `Varve.Protocol.Client` (5) only.
  - Analyzer rule: none new.
