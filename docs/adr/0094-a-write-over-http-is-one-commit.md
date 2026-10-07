# 0094 — A write over HTTP is one commit: agent, cause, `Varve-Position`, `409`, `412`

## Status

**Proposed — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Decided by the maintainer on the 7a plan: "agent IRI `<issuer>#<sub>` (sub
percent-encoded)". Acceptance is the maintainer's act on the pull request.

## Context

The store's write contract is one commit per request, an optional expected
position, and five outcomes: `Committed`, `NoChange`, `Conflict`, `Rejected`
and `Unavailable` (spec T1, ADRs 0010, 0011). SPARQL Update is one commit per
request (ADR 0057). The commit's metadata carries an agent and a cause, both
terms (spec §1). ADR 0037 makes the agent "the caller's stable subject
identifier from the token, `oid` for Entra and `sub` otherwise, recorded as a
term". It does not say which term.

HTTP has its own vocabulary for the same things: `If-Match` and `412` for an
optimistic precondition, `409` for a conflict with the resource's state, and
`ETag` for the version a response describes.

## Decision

1. **Every write request is at most one commit.**
   - A SPARQL Update request goes through `ISparqlUpdateExecutor`, which the
     server binds to `SparqlUpdate.ExecuteAsync` with `ConflictRetries` 0.
     Whether the request still means the same against a newer head is the
     client's decision (ADR 0057).
   - A Graph Store `PUT` pins the head, reads the target graph, and commits the
     retraction of its quads and the assertion of the body as one commit
     expecting the pinned position.
   - A `DELETE` does the same with no assertion.
   - A `POST` commits the assertion of the body, with no expected position
     unless `If-Match` gives one, since its net effect cannot depend on what it
     did not read.
   - A body that fails to parse commits nothing and is `400`.
2. **The agent is an IRI: the token's issuer, `#`, and the subject,
   percent-encoded.**
   - The subject is `oid` when the configured issuer is Entra, and `sub`
     otherwise (ADR 0037).
   - Percent-encoding uses UTF-8 and leaves only RFC 3986's unreserved
     characters, so the result is an IRI for any subject and two subjects never
     collide.
   - For example, `https://login.microsoftonline.com/{tenant}/v2.0#6f8c…`.
   - An IRI and not a literal, so that two issuers on one server cannot
     produce one agent, and so that provenance names who vouched for the caller.
   - **In anonymous mode there is no agent** (`RequestTerm.None`). The log
     records that nobody was authenticated, rather than inventing a name.
3. **The cause is the request id**: ASP.NET Core's `TraceIdentifier`, as an
   `xsd:string` literal. Every response carries it as `Varve-Request-Id`, so a
   client can find its commit in the feed.
4. **Every response that touched the log carries `Varve-Position`.** That is
   the commit's position after `Committed`, and the head after `NoChange`,
   `Conflict`, `Rejected` or `412`. It also carries `ETag: "<position>"`
   (ADR 0096).
5. **Outcomes map to status codes**:

   | Outcome | Status | Body |
   |---|---|---|
   | `Committed`, update | `204` | none |
   | `Committed`, GSP `PUT` creating a graph, or `POST` to the store root | `201` | none, `Location` for a minted graph |
   | `Committed`, other GSP writes | `204` | none |
   | `NoChange` | as `Committed`, `201` never | none; `Varve-Position` is the unchanged head |
   | `Conflict` | `409` | problem `conflict`, with `head` |
   | `Rejected` | `422` | problem `rejected`, with the validator report as N-Triples text |
   | `Unavailable` | `503`, `Retry-After: 1` | problem `unavailable` |

6. **`If-Match` is the expected position.**
   - Its entity tag is a position, `"<n>"`, the form every `ETag` takes.
   - A write whose `If-Match` does not equal the head is `412` with the head in
     `Varve-Position` and commits nothing. The check happens before anything
     is read or parsed.
   - A matching `If-Match` becomes the commit's expected position, so a writer
     that loses the race inside the sequencer gets `Conflict`, and `409`.
   - `If-Match: *` matches any head.
   - It applies to SPARQL Update too. That needs `UpdateOptions.ExpectedPosition`
     in `Varve.Sparql.Store`: the pin must be at that position or the result is
     `Conflict` without evaluation.
   - `If-None-Match` on a write is `400`.
7. **The dataset's pre-commit validators run as always**, because every path
   is a `CommitAsync`. Nothing in the protocol bypasses or adds to them.

## Alternatives considered

- **The agent as an `xsd:string` literal of the subject.** What the prompt
  first proposed. A literal of `sub` alone collides across issuers, and `sub`
  is unique only per issuer (OIDC Core §2).
- **A `urn:` agent with a hash of the issuer.** Opaque, and it hides who issued
  the identity, which is the point of recording it.
- **Retrying conflicts on the server.** A GSP `PUT` that retries replaces a
  graph its client never saw. The client holds the `ETag` and decides.
- **`409` for a stale `If-Match`.** RFC 9110 §13.1.1 makes it `412`, and `412`
  tells the client its precondition, not the state, was wrong.

## Consequences

- A client can do optimistic concurrency end to end: read, take the `ETag`,
  write with `If-Match`, and get `412` or `409` instead of a lost update.
- The feed shows who made each commit as an IRI any client can compare.
- `Varve.Sparql.Store` gains one option, `ExpectedPosition`, on its baseline.

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0011**: the expected position, over HTTP;
  - **0037**: the agent's form, a refinement of "recorded as a term";
  - **0057**: one request, one commit; retries are 0;
  - **0058**: validators unchanged.

  No conflict.
- **Layer ownership.** The mapping is `Varve.Protocol` (5); the agent's
  construction from a token is `Varve.Server` (6); `ExpectedPosition` is
  `Varve.Sparql.Store` (5).
- **Analyzer rule.** None.
- **Open questions owned.** None.
