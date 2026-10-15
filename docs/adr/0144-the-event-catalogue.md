# 0144 — The event catalogue: declarations as data, one graph per source graph, governed by it

## Status

**Accepted — filed unaccepted by milestone Client and integration, part 2, of
#93, 2026-10-15** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer in the part 2 brief, points 3, 5 and 10,
and on its plan:
- one catalogue graph per source graph, its IRI on part 1's `varve:setup/…`
  precedent;
- its visibility through graph grants, never content filtering;
- the derived grant expressed in `Varve.Rdf` as a generic governing-graph
  seam that knows no catalogue, with the catalogue's rule in `Varve.Events`;
- a short name taken refused without naming the other declaration.

**Amends** ADR [0107](0107-graph-level-authorisation.md) (the seam) and ADR
[0130](0130-client-side-setup-and-system-graphs.md) (the rule), each by a dated
block (ADR 0068). The specification is `docs/spec/events.md` §3 and §6.3.

## Context

ADR 0143 renders events through declarations. Somebody must find the
declarations: "which events does this dataset expose, and which did it expose
last March?" An integrating system that does not speak RDF wants a list and
an AsyncAPI document. One that does wants SPARQL. Both answers must agree, at
every position.

A declaration must also be invisible to a caller who cannot read the events it
describes. Its class, field names and query are part of what the source graph
holds. ADR 0107 hides graphs, not rows, and row-level visibility is milestone
9's. A design that filtered one catalogue graph by its content would bring that
early, inside an event feature.

The Management plane (#105) will make event schemas a management declaration.
The vocabulary must survive the move unchanged.

## Decision

1. **Declarations are data in catalogue graphs.**
   - There is one catalogue graph per source graph G:
     `https://w3id.org/varve/ns#events-catalogue/{enc(G)}`.
     - `enc(G)` is `default` for the default graph.
     - For a named graph, it is the graph's IRI percent-encoded as one
       segment, as `varve:setup/migration/{id}` is (ADR 0130).
     - An absolute IRI always encodes `:` as `%3A`, so no encoding is
       `default`.
   - The catalogue graphs are system graphs: ordinary data, in the log,
     visible to SPARQL and time-travellable.
2. **The vocabulary** is in `varve:`, and every node is an IRI.
   - **The type node:**
     - `varve:EventType`;
     - `varve:eventName`;
     - `varve:eventClass`;
     - `varve:eventVersion`;
     - `varve:eventSelect`;
     - `varve:eventSubject`;
     - `varve:eventRetired`;
     - `varve:eventField`.
   - **The field node:**
     - `varve:EventField`;
     - `varve:fieldName`;
     - `varve:fieldDatatype`;
     - `varve:fieldRequired`;
     - `varve:fieldAs`;
     - `varve:fieldKeepLanguage`.
   - `rdfs:comment` describes either kind of node.
   - With no blank node, a declaration has an identity, and a setup baseline,
     which refuses blank nodes (ADR 0130), can carry it.
   - The terms are listed in `docs/vocabulary.md`.
3. **A catalogue graph is governed by its source graph.**
   - A caller reads the catalogue graph of G exactly when it reads G, and
     writes it exactly when it writes G. A grant naming the catalogue graph
     itself has no effect on it.
   - The mechanism is in two halves.
     - **The seam is `Varve.Rdf`'s, and knows no catalogue.** `GraphScope`
       takes a **governing-graph mapping**, a function from a named graph's IRI
       to the graph that governs it, or none. A graph with a governor is
       allowed exactly when its governor is.
       - The decision is memoised per graph handle, as a prefix decision is
         (ADR 0107's `PrefixDecisionMemoisedPerGraph`).
       - The `all` scope stays the unwrapped source.
     - **The rule is `Varve.Events`'s:**
       - `EventCatalogue.Governor` maps a catalogue graph to its source graph;
       - the server's `IAccessScopes` composes it into every caller's
         readable and writable scopes.
   - Then the existing `GraphScopedQuadSource` hides a type from a caller
     without read on its source graph, in SPARQL, the list and the AsyncAPI
     document alike. There is no new filter.
4. **Three views, one source.** Each is computed from the catalogue graphs as
   of the read's position, through the caller's scope.
   - **SPARQL**, over the catalogue graphs.
   - **The dataset's list**: `GET /datasets/{name}/events/types`, JSON, each
     type with its source graph, its class, its version, its current schema's
     URL and every version's and revision's URL.
   - **The AsyncAPI 3.0 document**: `GET /datasets/{name}/events/asyncapi`.
     - It has a channel per type.
     - Its payloads are the structured CloudEvent envelope with the type's
       schema.
     - Each channel also lists the problem event of ADR 0143, so a generated
       client expects it.
     - It lists the `sse` server always, and the `eventhubs` server when
       `Varve:Events:EventHubs` configures one.
     - **AsyncAPI 3.0 has no CloudEvents binding, and no Event Hubs binding.**
       The document says so by describing the envelope as the payload of a
       structured-mode message, with a message trait naming the attributes.
   - **The server-wide list**: `GET /events/types`. It is the union of the
     dataset lists over every dataset the caller reads, each entry with its
     dataset.
   - The service description gains `varve:eventCatalogue` (each catalogue
     graph the caller reads), `varve:events` and `varve:eventTypes`.
5. **The declarations come from the catalogue graphs' history.**
   - The history is replayed once per open dataset from the commits that touch
     a catalogue graph, and kept current as commits arrive.
   - It answers the declarations, versions and revisions in force at any
     position, without an as-of read.
6. **What moves to the Management plane, and what stays.**
   - Until #105 lands, the source graph's writers write declarations through
     any write path.
   - #105 makes them management declarations under `/manage/`: the same
     terms and the same checks (ADR 0145), in a different home.
   - **What stays:** the rendered events, which depend only on the
     declarations in force at each position, and the as-of history of the
     catalogue graphs already written.
7. **A short name is unique within the dataset, across catalogue graphs.**
   - The check (ADR 0145) sees every catalogue graph.
   - The refusal for a name in use says only that the name is taken, and
     names only the declaration being written. It never names the other
     declaration, its graph, its class or its version, so a caller learns
     nothing of a graph it cannot read.

## Alternatives considered

- **One catalogue graph, filtered by content per caller.** That is
  row-level visibility, which is milestone 9's. It is refused by the brief.
- **Teach `GraphScope` the catalogue's IRI scheme.** A layer-1 type would
  then know a layer-4 concept. The governing-graph mapping is the generic
  form: a graph may be governed by another, and the composer says which.
- **Declarations in the source graph itself.** They would be visible and
  writable with it, but they would mix with the domain's data, and a `GRAPH`
  query over the events graph would see them as data.
- **Declarations in host configuration.** They would be unversioned, invisible
  to SPARQL and not as-of. #105's reason for a management plane applies here
  too.
- **Blank-node field lists.** They are idiomatic RDF, but a baseline would
  refuse them, and a field would have no identity across edits.

## Consequences

- "Which events did this dataset expose at p" is an as-of SPARQL query, a list
  and an AsyncAPI document, and the three agree. A property over generated
  histories holds them to it at every position.
- `Varve.Rdf`'s `GraphScope` gains the mapping, and its baseline gains it too.
  `Varve.Events` gains the catalogue's IRI functions.
- System graphs get their first special treatment: a derived grant. ADR 0130's
  "no special treatment" is amended to say so.

## Checks

- **Checked against the accepted ADRs** (0001–0133). It touches:
  - **0107** (amended: the seam);
  - **0130** (amended: the rule; system graphs);
  - **0092** point 6 (the vocabulary);
  - **0106** (the dataset list the server-wide view walks);
  - **0118** (resources under the dataset).

  No conflict.
- **Layer ownership.**
  - `Varve.Rdf` (1): the seam.
  - `Varve.Events` (4): the rule, the views.
  - `Varve.Protocol` (5): the endpoints.
  - `Varve.Server` (6): the composition.
- **Analyzer rule.** None new. `VARVE0003` checks the memoised decision.
- **Open questions owned.** None.
