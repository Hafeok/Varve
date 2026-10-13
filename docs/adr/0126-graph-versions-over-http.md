# 0126 — Graph versions over HTTP: `ETag` per graph, `If-Match` per graph, `Varve-If-Graphs-Match`

## Status

**Accepted — filed unaccepted by milestone Commit path of #91, 2026-10-13**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the Commit path plan: an as-of graph read is tagged with
the graph's version at that position; the `409`'s `graphs` member is limited
to the request's scope and capped; empty graphs as point 5 states.

**Amends** ADR [0094](0094-a-write-over-http-is-one-commit.md) (the `ETag`
and `If-Match` of a Graph Store request name the graph's version), ADR
[0096](0096-time-travel-over-http.md) (point 4's tag, for a Graph Store
read) and ADR [0119](0119-headers-and-the-problem-catalogue.md) (point 9's
members of `409` and the title of `412`), each by a dated block of its own
(ADR 0068).

## Context

ADR 0125 gives every graph a version and narrows the expected-position check
to a scope. HTTP already carries the dataset's version: every response that
touched a dataset has `Varve-Position` and `ETag: "<position>"` (ADRs 0094,
0096), and `If-Match` is the expected position. Over the Graph Store Protocol
the resource is a graph, so the natural tag is the graph's version. SPARQL
Update addresses no one resource, so its precondition needs a header that
can name several.

## Decision

1. **A Graph Store `GET` or `HEAD` is tagged with the graph's version.**
   `ETag: "<v>"`, `v` the position of the last commit at or before the read's
   position that touched the graph (ADR 0125), at the head or as of any
   position. `Varve-Position` stays the read's position, so a client still
   knows where it read. The tag is strong: the graph's content, and so every
   representation of it, is fixed from `v` until the next commit that
   touches it, and two reads at positions with the same version of the graph
   return the same bytes for the same `Accept`. As in ADR 0094, one tag
   names the graph state across serialisations: the server compares it only
   against the graph's version, and every response carries `Vary: Accept,
   Varve-As-Of, Authorization`, so a cache keys each representation apart.
   `If-None-Match` compares with the graph's version and is answered before
   any pin is taken, from the head's index or, as of a position, from the
   nearest checkpoint's map and the log after it.
2. **Dataset-level reads keep the head.** A query, the service description
   and the feed are tagged with the position they describe, as before.
3. **`If-Match` on a Graph Store write names the graph's version.** The
   comparison is strong (RFC 9110 §13.1.1): a tag other than the graph's
   current version is `412`, before anything is read or parsed. A matching
   tag becomes the commit's expected position with scope `{g}`, so a writer
   that loses the race inside the sequencer gets `409`. A `PUT` or `DELETE`
   without `If-Match` expects the position it pinned, with scope `{g}`: it
   read only `g`.
4. **`Varve-If-Graphs-Match` on a SPARQL Update.** `Varve-If-Graphs-Match:
   <iri>=<n>, default=<n>, …`: each named graph, or the default graph as
   `default`, with the version the client read. Every graph listed must be
   at the version given, or the request is `412` before evaluation; the
   request is evaluated over the head `h` at which the versions were
   checked, and commits expecting `h` with the listed graphs as its scope, so
   a change to any of them while it evaluates is `409`. The header replaces
   the inferred scope of point 6: the client has stated what it read. A malformed header is `400`. It is not combined with
   `If-Match`: both on one request is `400`.
5. **Empty graphs.** A graph with no quads has no current representation (a
   graph exists when it holds a quad, ADR 0057): `GET` is `404`, and
   `If-Match` against it is `412` whatever tag it names, since there is
   nothing for the tag to match. `If-None-Match: *` on a `PUT` is a
   create-only write: it succeeds when the graph is empty and is `412` when
   it holds a quad. An empty graph's **version still counts in a scope**:
   emptying it was a commit that touched it, and a writer that read it
   before that commit conflicts.
6. **The scope of an Update without the header** is ADR 0057's, as amended:
   the graphs the request's patterns read and its delta writes, or every
   graph; a data-only request stays unconditional.
7. **`409 conflict` gains `graphs`**: an array of the intervening commits
   that touched the request's scope, newest first, each
   `{"position": n, "graphs": ["<iri>" | "default", …]}`, listing only graphs
   **in the request's own scope**, so that it says nothing about a graph the
   caller did not name or read (ADR 0107). At most 100 commits are listed,
   read at most `Varve:Limits:MaxAsOfDistance` commits back from the head
   (ADR 0114 point 3); `graphsTruncated: true` says the list stopped short.
   A request with no scope (`S` = every graph) lists the commits after its
   expected position with the graphs they touched that the caller may read.
8. **`412 precondition-failed`** is titled "The precondition does not name
   the current version." and gains `graphs`, the graphs whose version
   differed, each `{"graph": …, "version": n}`.
9. **The service description** advertises `varve:conditionalRequest
   "Varve-If-Graphs-Match"` beside `If-Match` and `If-None-Match`, and
   `varve:graphVersionETag true`.
10. **`Varve.Protocol.Client`** exposes a graph's version from a Graph Store
    read (the `ETag`, parsed), and takes `ifMatch` on its Graph Store writes
    and a graph-to-version map for `Varve-If-Graphs-Match` on an update, so
    the client milestone (#93) builds on them.

## Alternatives considered

- **Keep the dataset position as every tag.** Simplest, and it makes every
  conditional Graph Store write conflict with every other graph's writer,
  which is what this milestone exists to stop.
- **A weak tag per graph.** Weak tags cannot be used with `If-Match`.
- **An as-of graph read tagged with the resolved position** rather than the
  graph's version. Correct, and it splits one representation across many
  tags: two as-of reads of an unchanged graph would not share a cache entry.
  The maintainer chose the graph's version.
- **Several `If-Match` tags for an update.** `If-Match` lists alternatives
  any of which may match (RFC 9110 §13.1.1), not a conjunction over
  resources; a request naming several graphs needs a header of its own.
- **Retry a conflicted request on the server when the client gave no
  precondition.** Proposed on the plan for a request whose only
  precondition is the server's own inferred scope; ADRs 0057 and 0094 reject
  server-side retry, and changing that is the maintainer's decision, recorded
  in the milestone's report, not here.

## Consequences

- A client of one graph does optimistic concurrency on that graph alone;
  eight writers on eight graphs do not conflict.
- A cache holds one entry per graph version and representation, across
  as-of positions.
- `If-None-Match` on a graph read is answered from the index, without a pin.

## Checks

- **Checked against the accepted ADRs** (0001–0125) and specification 1.7.
  Touches **0057** (a graph exists when it holds a quad), **0092** (problems
  everywhere; no protocol-silent tie-breaker is involved), **0094**,
  **0096**, **0119** (amended), **0107** (the `graphs` member respects grants),
  **0114** (the as-of bound), **0125** (the versions). No conflict.
- **Layer ownership.** `Varve.Protocol` and `Varve.Protocol.Client`, layer 5;
  the header's binding to an update is `Varve.Sparql.Store`'s option, layer 5.
- **Analyzer rule.** None.
- **Open questions owned.** None.
