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

**Amended 2026-10-12** (ADR 0068), by milestone 6b of #10 under ADRs
[0122](0122-rdf-xml-over-system-xml.md) and
[0123](0123-json-ld-over-utf8-json.md): RDF/XML and JSON-LD join the
negotiation, as the Consequences said they would; see the end.

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

## Amendment, 2026-10-12 — RDF/XML and JSON-LD are negotiated

Filed by milestone 6b of #10, unaccepted until the maintainer accepts it
(ADR 0066). It replaces one row of point 8's table and adds to point 1;
nothing else above changes, and it adds no key. The Consequences said this
table would change when the packages existed; `Varve.RdfXml` (ADR 0122) and
`Varve.JsonLd` (ADR 0123) exist.

**Point 1.** `CONSTRUCT` and `DESCRIBE`, a Graph Store `GET` and the
service description are negotiated over N-Triples, Turtle, N-Quads, TriG,
`application/rdf+xml` and `application/ld+json`, in that order of the
server's preference; the default is still N-Triples, so an `Accept` that
leaves the choice open answers as before. A Graph Store `PUT` or `POST`, and
each part of a multipart `POST`, reads all six. The service description's
`sd:resultFormat` and `sd:inputFormat` name `formats:RDF_XML` and
`formats:JSON-LD` beside the four.

**RDF 1.2.** The four line syntaxes write a triple term and a directional
literal in their RDF 1.2 forms, as they did. RDF/XML writes them as
`rdf:parseType="Triple"` and `its:dir`, and declares `rdf:version="1.2"` when
it does (ADR 0122); it reads them where a body's `rdf:version` announces
1.2. JSON-LD writes a direction natively, as `@direction` (ADR 0123,
DirectionDefaultIsNative), and has no triple term.

**What a format cannot spell.** Three graphs are refused by the format the
client chose, by that format's own writer, and the refusal is
`not-acceptable` with a `detail` naming the cause, not a server fault:

| The graph holds | Refused by | Why |
|---|---|---|
| a triple term | JSON-LD | JSON-LD 1.1 has none |
| a predicate with no NCName suffix, such as `<http://ex/p/>` | RDF/XML | a property element is a qualified name (RDF/XML §2.7) |
| a character XML 1.0 cannot carry, such as U+0008 | RDF/XML | XML 1.0 §2.2 |

The answer is not renegotiated to another format in `Accept`: the server
cannot know a graph is unspellable before it reaches the triple that makes
it so, and a silent change of format is the kind of guess point 8 rules
out. JSON-LD meets its refusal before the first byte, always, because the
writer holds the graph until the last triple (ADR 0123,
FromRdfBuffersTheDataset): the answer is a `406` problem. RDF/XML streams,
so a refusal before the first byte is a `406` problem and one after it is
the cut every bounded read takes (ADR 0095): a `Varve-Error: not-acceptable`
trailer where the response carries trailers, an abort where it does not,
and never a whole-looking document.

**What JSON-LD holds.** The graph JSON-LD buffers is charged against
`Limits:MaxQueryMemory` (ADR 0114) at its terms' bytes and sixteen bytes a
triple, and a graph over it is `422 memory-limit-exceeded` before the first
byte, as a materialising query is. The streaming syntaxes hold nothing per
triple and are not charged.

**Point 8's table.** The row

| RDF/XML or JSON-LD requested or sent | `406`, `415` | no such package yet |

is replaced by

| A graph the chosen format cannot spell (above) | `406` before the first byte; the cut after it | point 8: no guessed format |

The deprecated suite's two `HEAD` and `GET` exemptions without `Accept`
stand: N-Triples is still the default, which GSP §5.2 allows.

**Tests.** `GraphFormatTests` in `Varve.Protocol.Tests`, over a real socket:
a graph written in each of the six formats reads back the same in each of
the six; a `CONSTRUCT` and the service description in each, with `Vary:
Accept, Varve-As-Of, Authorization` on every one; the empty default graph is
an empty document in RDF/XML and JSON-LD; a triple term travels in five and
is a `406` in JSON-LD; a predicate with no qualified name is a `406` in
RDF/XML; JSON-LD over the memory limit is a `422`; a JSON-LD body naming a
graph or a remote context, and an unclosed RDF/XML body, are `rdf-syntax`
and commit nothing. `GraphFormatConformanceTests` runs the eleven protocol
and Graph Store cases that ask for a graph with every graph asked for in
RDF/XML and then in JSON-LD, and the nineteen `CONSTRUCT` and `DESCRIBE`
evaluation cases through the protocol likewise: each passes, or is a `406`
for a graph that format's writer refuses, which seven are (six triple terms
in JSON-LD, one U+0008 in RDF/XML).

**Not changed.** `Varve.Protocol.Client`'s document fetch for `LOAD` still
reads the four line syntaxes; it gains RDF/XML and JSON-LD when a milestone
wants `LOAD` of either, by amendment of ADR 0104. The `varve` command line's
`load` and `export` are unchanged. The `version` media-type parameter of
RDF 1.2 is neither read nor written.
