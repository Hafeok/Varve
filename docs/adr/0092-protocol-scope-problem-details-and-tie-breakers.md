# 0092 — Protocol scope, problem details, and the Oxigraph tie-breakers

## Status

**Accepted — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Decided by the maintainer on the 7a plan:
- the vocabulary and problem-type namespace is `https://w3id.org/varve/`;
- the tie-breakers listed here are reviewed on the pull request.

Acceptance is the maintainer's act on the pull request.

**Amended 2026-10-09** (ADR 0068), by milestone Operability of #12 under ADR
[0119](0119-headers-and-the-problem-catalogue.md): point 5's problem types
are a catalogue with fixed members and a page each; see the end.

## Context

The brief makes the W3C suites the acceptance gate and Oxigraph the tie-breaker
where a specification is silent. Three documents govern this milestone:

- SPARQL 1.1 Protocol (W3C Recommendation, 2013);
- SPARQL 1.1 Graph Store HTTP Protocol (Recommendation, 2013);
- SPARQL 1.1 Service Description (Recommendation, 2013).

SPARQL 1.2 Protocol is a Working Draft (23 July 2026). Its only normative
additions are an optional `version`, carried either as a request parameter or
as a media-type parameter on `application/sparql-query` and
`application/sparql-update`, and a relaxed statement of the media types a query
response must be able to take (Appendix A of the draft).

The suites, as checked out at `rdf-tests` `369a90d`:

| Suite | Entries | Form |
|---|---:|---|
| `sparql11/protocol` | 34 | `ht:Request` lists with expected status, format and boolean |
| `sparql11/graph-store-protocol` | 13 (4 direct, 9 indirect) | `ht:Request` lists |
| `sparql11/http-rdf-update` | 18 | **all `dawg:Deprecated`** "in favor of … graph-store-protocol"; HTTP transcripts in `rdfs:comment` |
| `sparql11/service-description` | 3 | names only, no action |

Varve reads and writes N-Triples, N-Quads, Turtle and TriG (RDF 1.1 Turtle and
TriG, `turtle.md` §9). It has no RDF/XML or JSON-LD package.

## Decision

1. **Query**: `GET` with `query`, `POST` with
   `application/x-www-form-urlencoded`, and `POST` with
   `application/sparql-query`.
   - `default-graph-uri` and `named-graph-uri`, repeated, replace the query's
     `FROM` and `FROM NAMED` when either is present (§2.1.4).
   - Results are negotiated over every format `Varve.Sparql.Results` writes:
     XML, JSON, CSV and TSV.
   - `CONSTRUCT` and `DESCRIBE` are negotiated over N-Triples, N-Quads, Turtle
     and TriG.
   - With no `Accept`, or `*/*`, a solution or boolean result is JSON and a
     graph result is N-Triples. Oxigraph's choice, and the one a streaming
     writer can honour without buffering prefixes.
2. **Update**: `POST` with `application/x-www-form-urlencoded` and `update`, and
   `POST` with `application/sparql-update`. `using-graph-uri` and
   `using-named-graph-uri` apply per §2.2.3. A request that names them **and**
   has a `USING` or `WITH` clause is `400`, as the protocol requires.
3. **Graph Store**: `GET`, `HEAD`, `PUT`, `POST` and `DELETE`, by direct and
   indirect identification (§4.1, §4.2), with `?default` for the default graph.
   - Bodies may be in any of the four syntaxes, plus `multipart/form-data`
     whose parts are in them (§5.5).
   - **A graph exists when it holds a quad.** There is no empty named graph in
     the store's model (`G_P` is a set of quads), so `PUT` of an empty body
     makes the graph absent and a later `GET` is `404`. Oxigraph's store
     behaves the same.
4. **SPARQL 1.2's `version`** is accepted in both places. Values are `1.1`,
   `1.2-basic` and `1.2`, the parser's three labels (`sparql-grammar.md`).
   - The parameter wins over a `VERSION` declaration in the text, as the draft
     says.
   - An unknown value is `400`.
   - With no version given the request is parsed as `1.2`, the parser's
     default.
5. **Errors are RFC 9457 problem details** (`application/problem+json`) on
   every error response. Each problem's `type` is an IRI under
   `https://w3id.org/varve/problems/`. A SPARQL syntax error carries `line`,
   `column` and `offset` (bytes, `sparql-grammar.md`) as extension members. The
   types are listed in the protocol page of the server documentation and in
   the public `ProblemTypes` constants.
6. **The service description's own terms** are under
   `https://w3id.org/varve/ns#` (prefix `varve:`). The namespace is stated in
   the description and in this ADR.
7. **No Oxigraph endpoint is imitated.** Oxigraph serves `/query`, `/update`
   and `/store`. Varve serves the paths in ADR 0093.
8. **Where the protocol is silent, Oxigraph's server behaviour decides.** Each
   choice:

   | Situation | Varve | Note |
   |---|---|---|
   | Query and update on one URL | one endpoint, `/sparql` | the protocol permits either; Oxigraph uses two. The service description advertises one `sd:endpoint` |
   | Unsupported request media type | `415` | Oxigraph |
   | `POST` with no `Content-Type` | `400` | Oxigraph |
   | A charset other than UTF-8 | `415` | Oxigraph rejects a non-UTF-8 charset |
   | More than one `query` or `update` | `400` | |
   | `update` on `GET` | `405` with `Allow: POST` for an update; a `GET` with neither parameter is the service description | |
   | Nothing in `Accept` we can write | `406` | Oxigraph |
   | GSP `PUT` that creates a graph | `201` | the GSP permits `201` or `204`; Oxigraph answers `201` |
   | GSP `PUT`, `POST` or `DELETE` that changes an existing graph | `204` | |
   | GSP `DELETE` or `GET` of an absent graph | `404` | |
   | GSP `HEAD` | as `GET`, no body | |
   | GSP `POST` to the graph store root (§5.5, new graph) | `201` with `Location` of a minted graph under the store | Oxigraph mints a fresh IRI too |
   | An unknown `version` | `400` | the draft names none |
   | RDF/XML or JSON-LD requested or sent | `406`, `415` | no such package yet |

9. **The suites.** `protocol` and `graph-store-protocol` are gates, by their
   machine-readable requests. `http-rdf-update` runs from its transcripts under
   its own guard count, catalogued as deprecated. `service-description`'s three
   names are checks written by us, each stated as such. All four run against an
   in-process server over the memory store and the file store, under the
   ratchet.
   - **Every case of `protocol` and `graph-store-protocol`, and every
     service-description check, passes without an exemption.**
   - **Six cases of the deprecated `http-rdf-update` are exempt on both
     stores**, each a flaw of the deprecated suite, each answered as its
     successor and Oxigraph answer it. They are listed in
     `baseline/exemptions.txt`:
     - two bodies in Turtle with no final `.` (Turtle 1.1 §2.4), so `400`;
     - the two `GET`s that depend on those bodies;
     - a `DELETE` of a graph no step creates, so `404` (GSP §5.4);
     - a `HEAD` without `Accept` that expects Turtle, where GSP §5.2 allows
       RDF/XML, Turtle or N-Triples and Varve writes N-Triples.

## Alternatives considered

- **`/query` and `/update` as Oxigraph has them.** Two URLs to authorise and
  describe for no protocol reason. The single endpoint is what the protocol's
  own examples use.
- **Turtle as the default for graph results.** Prettier, but Turtle writes
  prefixes first, and the writer cannot pick them before seeing the triples.
  N-Triples streams.
- **Skip `http-rdf-update`.** The suite is deprecated and its successor covers
  it. The maintainer asked for it, and running it costs one parser of
  transcripts.
- **Problem types as `about:blank` with a `title`.** RFC 9457 allows it. A
  client could then not tell a conflict from a precondition failure without
  reading prose.

## Consequences

- A client gets the same problem shape from every endpoint, and a syntax error
  points into its own request.
- RDF/XML and JSON-LD join content negotiation when their packages exist. This
  ADR's table changes then, by amendment.
- The service description must say exactly this (ADR 0096 and the shapes test).

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0007**: the harness, gaining four suites;
  - **0057**: one update request is one commit, unchanged;
  - **0061**: canonical N-Triples is RDF 1.2's.

  No conflict.
- **Layer ownership.** `Varve.Protocol`, layer 5.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-09 — the problem types are a catalogue

Filed by milestone Operability of #12, unaccepted until the maintainer
accepts it (ADR 0066). It adds to point 5; nothing above changes.

Point 5 said the types "are listed in the protocol page of the server
documentation and in the public `ProblemTypes` constants". They are now
**catalogued** (ADR 0119): `ProblemCatalogue` in `Varve.Protocol` fixes each
type's `title`, `status` and extension members, the writer emits only from
it, `instance` is the request id, and every type has a page in
`docs/problems/` that its IRI resolves to. The parse error's members
`line`, `column` and `offset` stand; the conflict's `head` member is
renamed `headPosition`, beside `position` and `expectedPosition`, while
everything is preview. Every non-2xx is a problem, `404` for an unknown
dataset or an unserved route, `405` and `415` included, and `401` is
deliberately thin.

**The ledger.** `ErrorsAreProblemDetails` stands; the catalogue's rulings
are ADR 0119's set.