# Domain events: the catalogue, the rendering and the schema registry

Functional specification for `Varve.Events` (layer 4, by dependency) and for
the events endpoints of `Varve.Protocol` (layer 5): how a dataset declares the
domain events it exposes, how each event is rendered as a CloudEvent in JSON,
how the declarations are listed, versioned and described in AsyncAPI, and how
a change to a declaration is checked.

Status: **Proposed** with ADRs [0143](../adr/0143-domain-events-as-cloudevents.md)
(rendering), [0144](../adr/0144-the-event-catalogue.md) (the catalogue),
[0145](../adr/0145-the-event-schema-registry.md) (the registry) and
[0147](../adr/0147-varve-json-canonical.md) (canonical JSON), milestone Client
and integration, part 2 (#93). It changes only together with the ADR that
motivates the change.

**What is read for ever.** The rendering of §4 and §5 and the schema of §6 are
functions of the log and nothing else, so their output for a given position
never changes: a CloudEvent replayed is the bytes it was, and a schema served
at its `$id` is the bytes it was. A change to either that alters a byte for an
existing input is not a clarification; it is refused by the golden files of
§6.4 and the replay property of §8.

## 1. Normative references

- CloudEvents 1.0.2, its JSON event format, its batch format
  (`application/cloudevents-batch+json`) and the `sequence` extension.
- RFC 8785, the JSON Canonicalization Scheme (`Varve.Json.Canonical`).
- RFC 3339, `date-time`, `full-date` and `full-time`.
- JSON Schema, draft 2020-12 (core and validation).
- AsyncAPI 3.0.0.
- `change-feed.md` §3 (server-sent events) and §4 (the commits resource's
  range, its paging and `Last-Event-ID`), whose position semantics this
  specification reuses unchanged.
- `log-and-projection-model.md` I2 (the effective delta) and §6 (as-of reads).
- XSD 1.1 Part 2 for the datatypes of §5.2.

## 2. Terms

- **Source graph** G: a graph of a dataset holding event nodes. The default
  graph may be one.
- **Catalogue graph** of G: the graph that declares G's event types (§3.1).
- **Declaration**: an event type's description in a catalogue graph (§3.2).
- **Event node**: a node of G typed, in some commit, with a declared class.
- **Event**: an event node at the position of the commit that typed it (§4.1).
- **Version** n: a positive integer a declaration states. **Revision** r of
  version n: a position at which the type's generated schema changed (§6.2).

The vocabulary prefix `varve:` is `https://w3id.org/varve/ns#`.

## 3. The catalogue

### 3.1 Catalogue graphs

The catalogue graph of G is

```
https://w3id.org/varve/ns#events-catalogue/{enc(G)}
```

where `enc(G)` is `default` for the default graph and, for a named graph, its
IRI percent-encoded as one segment: every UTF-8 byte outside the unreserved
set of RFC 3986 §2.3 (`A–Z a–z 0–9 - . _ ~`) as `%` and two upper-case hex
digits. An absolute IRI always holds a `:`, encoded `%3A`, so no named graph's
encoding is `default`. This is the scheme of `varve:setup/migration/{id}` (ADR
0130), under the same namespace of system graphs.

A graph named under `varve:events-catalogue/` that is not `enc(G)` of some
graph — not decodable, or decoding to something that is not an absolute IRI —
declares nothing, and a write of a declaration into it is refused (§7).

**Grants** (ADR 0144, ADR 0107 amended). The catalogue graph of G is
**governed** by G: a caller reads it exactly when it reads G, and writes it
exactly when it writes G. A grant naming a catalogue graph itself has no
effect on it.

### 3.2 Declarations

A declaration is the set of triples, in one catalogue graph, about one type
node and its field nodes. Every node is an IRI: a blank node in a catalogue
graph is refused (§7), so that a declaration has an identity, and so that the
setup's baselines, which refuse blank nodes (ADR 0130), can carry it.

| Term | On | Count | Value |
|---|---|---|---|
| `rdf:type varve:EventType` | type node | 1 | |
| `varve:eventName` | type node | 1 | an `xsd:string` matching `[A-Za-z][A-Za-z0-9._-]{0,127}`: the CloudEvents `type` |
| `varve:eventClass` | type node | 1 | an IRI: the class that marks an event node in G |
| `varve:eventVersion` | type node | 1 | an `xsd:integer` ≥ 1 |
| `varve:eventSelect` | type node | 1 | an `xsd:string`: a SPARQL `SELECT` query (§5.1) |
| `varve:eventSubject` | type node | 0–1 | an `xsd:string`, the name of a projected variable whose value is the CloudEvents `subject` |
| `varve:eventRetired` | type node | 0–1 | `true`: the type renders no new events (§3.4) |
| `rdfs:comment` | type node | 0–1 | an `xsd:string`, the type's description |
| `varve:eventField` | type node | 0–n | a field node |
| `rdf:type varve:EventField` | field node | 1 | |
| `varve:fieldName` | field node | 1 | an `xsd:string` matching `[A-Za-z_][A-Za-z0-9_]{0,63}`: the `data` member and the projected variable |
| `varve:fieldDatatype` | field node | 1 | an IRI of §5.2's table |
| `varve:fieldRequired` | field node | 1 | an `xsd:boolean` |
| `varve:fieldAs` | field node | 0–1 | `"string"`: the field is always a JSON string (§5.3) |
| `varve:fieldKeepLanguage` | field node | 0–1 | `true`: a language-tagged value is an object (§5.2) |
| `rdfs:comment` | field node | 0–1 | an `xsd:string`, the field's description |

Within a dataset:

- a short name (`varve:eventName`) belongs to one type node across every
  catalogue graph;
- a class belongs to one type node within one catalogue graph;
- a field name belongs to one field node within one type;
- a field node belongs to one type node.

### 3.3 Example

```trig
PREFIX varve: <https://w3id.org/varve/ns#>
PREFIX xsd:   <http://www.w3.org/2001/XMLSchema#>
PREFIX rdfs:  <http://www.w3.org/2000/01/rdf-schema#>

<https://w3id.org/varve/ns#events-catalogue/https%3A%2F%2Fexample.org%2Fevents> {
  <https://example.org/types/OrderPlaced> a varve:EventType ;
    varve:eventName "OrderPlaced" ;
    varve:eventClass <https://example.org/ns#OrderPlaced> ;
    varve:eventVersion 1 ;
    varve:eventSubject "order" ;
    rdfs:comment "An order was placed." ;
    varve:eventSelect """
      PREFIX ex: <https://example.org/ns#>
      SELECT ?order ?total ?placedAt WHERE {
        ?event ex:order ?order ; ex:total ?total ; ex:placedAt ?placedAt
      }""" ;
    varve:eventField <https://example.org/types/OrderPlaced/order> ,
                     <https://example.org/types/OrderPlaced/total> ,
                     <https://example.org/types/OrderPlaced/placedAt> .

  <https://example.org/types/OrderPlaced/order> a varve:EventField ;
    varve:fieldName "order" ; varve:fieldDatatype xsd:anyURI ; varve:fieldRequired true .
  <https://example.org/types/OrderPlaced/total> a varve:EventField ;
    varve:fieldName "total" ; varve:fieldDatatype xsd:decimal ; varve:fieldRequired true .
  <https://example.org/types/OrderPlaced/placedAt> a varve:EventField ;
    varve:fieldName "placedAt" ; varve:fieldDatatype xsd:dateTime ; varve:fieldRequired true .
}
```

### 3.4 Retirement, not deletion

A declaration is never removed: a commit that retracts a type node's
`rdf:type varve:EventType`, or any triple a declaration requires, is refused
(§7). A type is **retired** by asserting `varve:eventRetired true`: it renders
no event at a position where it is retired, and it stays listed and served,
with every version and revision it ever had. So a short name never returns
with a version it already had, and the check of §7 reads only the state before
and after the commit, never the history.

### 3.5 Where declarations come from, and where they go

Until the Management plane (#105) lands, declarations are data in the
catalogue graphs, written by the source graph's writers through any write path
— a SPARQL Update, a Graph Store write, a setup baseline (ADR 0130). #105 makes
them management declarations: **the same terms and the same checks** (§7),
applied through `/manage/`. What stays: the rendered events (§4–§5), which
depend only on the declarations in force at each position, and the as-of
history of the catalogue graphs already written.

## 4. Events

### 4.1 When an event occurs

An event occurs at position p for each node e and type T such that

- the effective delta of the commit at p (I2) asserts `(e, rdf:type, C)` in
  G, and
- T is declared in the catalogue graph of G as of p, with `varve:eventClass C`,
  and is not retired as of p.

Matching is exact: no subclass, no inference. The entailment regime is simple
entailment until Inference (#95) states otherwise. A retraction renders
nothing; a re-assertion after a retraction is a new event. An assertion that
I2 empties, because the triple already held, is no event.

**Order.** The events of one commit are ordered by the event node's IRI, in
code-point order, then by the type's short name. A blank event node sorts after
every IRI, in the order of the commit's delta.

### 4.2 The envelope

Each event is one CloudEvent 1.0 in the structured JSON format, written in
RFC 8785 canonical form (members sorted, no whitespace, numbers in ECMAScript
form), so the same event is always the same bytes.

| Attribute | Value |
|---|---|
| `specversion` | `"1.0"` |
| `id` | the position in decimal, one space, the event node's IRI |
| `source` | the dataset's URL (§4.4) |
| `type` | the type's short name |
| `time` | the commit's timestamp: RFC 3339, UTC, seven fractional digits, `Z` (the delta format's form) |
| `subject` | when the declaration names `varve:eventSubject`: that variable's value, as a string (§5.3's string form) |
| `datacontenttype` | `"application/json"` |
| `dataschema` | the URL of the schema revision in force at p (§6.2) |
| `sequence` | the position zero-padded to 20 digits, `.`, and the event's ordinal within its commit, from 0, zero-padded to 6 digits |
| `data` | the fields (§5) |

A space cannot occur in an IRI, so `id` splits at its first space into the
position and the IRI; the `sequence` attribute orders every event of a dataset
totally, as the extension requires, as a string.

### 4.3 A problem on the stream

An event that cannot be rendered is never guessed and never skipped: in its
place, at its ordinal, the stream carries a CloudEvent with

- `type` `https://w3id.org/varve/problems/event-not-rendered`;
- `datacontenttype` `application/problem+json`;
- no `dataschema` and no `subject`;
- `id`, `source`, `time` and `sequence` as §4.2;
- `data` the problem (RFC 9457): `type`, `title`, `status` 422, and the members
  `event` (the node's IRI, or `_:` for a blank node), `eventType` (the short
  name) and `reason`, one of:

| Reason | When |
|---|---|
| `blank-event` | the event node is a blank node |
| `types` | the node takes two declared classes of G in one commit |
| `no-row` | the `SELECT` yields no row |
| `rows` | the `SELECT` yields more than one row |
| `unbound-required` | a required field's variable is unbound |
| `datatype` | a value is not of the field's datatype, or is a blank node, or has no form in §5.2 (a date or time without a timezone, a year outside 0000–9999) |
| `number` | a numeric value fails §5.4 |

A problem event renders as deterministically as an event, so a replay yields
it again, in the same place, with the same bytes.

### 4.4 The source

`source` is `{base}datasets/{name}`, where `{base}` is `Varve:Events:Source`
when the host configures it (an absolute URL ending in `/`), and otherwise the
request's scheme, host and path base followed by `/`. **Byte identity holds per
base**: a deployment reached under more than one host name sets the option. The
schema URLs of §6 and the AsyncAPI document's servers use the same base.

## 5. Rendering

### 5.1 The query

The declaration's `SELECT` runs over a dataset that holds, in its default
graph, the **closure** of e in G as of p: every quad `(e, ?p, ?o)` of G, and,
for each blank node `b` reached as an object, every quad `(b, ?p, ?o)` of G,
recursively. Then `?event` is bound to e: the evaluator joins the pattern
under the projection with `VALUES ?event { e }`. So the rendering is a pure
function of the position and the declaration, and stays correct when I2 empties
part of the commit's delta because a quad already held.

The query must parse as a SPARQL 1.2 `SELECT` whose projection is exactly the
field names, plus the subject variable when one is declared; it may use
`DISTINCT`, but not `GROUP BY`, aggregates, `ORDER BY`, `LIMIT`, `OFFSET`,
`FROM`, `FROM NAMED` or `SERVICE` (§7). Exactly one row renders an event;
none, or more than one, is a problem (§4.3).

### 5.2 From a value to JSON

A field's value is the row's binding of its variable. Unbound is the member's
absence when the field is optional, and `unbound-required` when it is required.
A blank node is `datatype`. Otherwise the value must be of the field's
datatype — an IRI for `xsd:anyURI`, a literal whose datatype is the declared
one or, for the integer types, one derived from it — or the event is
`datatype`.

| `varve:fieldDatatype` | JSON | Schema |
|---|---|---|
| `xsd:integer`, `xsd:long`, `xsd:int`, `xsd:short`, `xsd:byte`, the non-negative, non-positive, positive, negative and unsigned types | number, §5.4 | `{"type":"integer"}` |
| `xsd:decimal` | number, §5.4 | `{"type":"number"}` |
| `xsd:double`, `xsd:float` | number: the value's nearest double in ECMAScript form; NaN and ±INF are `number` | `{"type":"number"}` |
| `xsd:boolean` | `true` or `false` | `{"type":"boolean"}` |
| `xsd:dateTime`, `xsd:dateTimeStamp` | string: the lexical form when it is an RFC 3339 `date-time` (a timezone, a year 0000–9999, an hour below 24) | `{"type":"string","format":"date-time"}` |
| `xsd:date` | string: the lexical form when it is an RFC 3339 `full-date` (no timezone) | `{"type":"string","format":"date"}` |
| `xsd:time` | string: the lexical form when it is an RFC 3339 `full-time` (a timezone, an hour below 24) | `{"type":"string","format":"time"}` |
| `xsd:anyURI` | string: the IRI, or the literal's lexical form | `{"type":"string","format":"iri"}` |
| `rdf:langString`, `rdf:dirLangString` | string: the lexical form; with `varve:fieldKeepLanguage`, `{"direction":…,"language":…,"value":…}`, `direction` absent when there is none | `{"type":"string"}`, or the object's schema |
| any other datatype (`xsd:string`, durations, the `g` types, …) | string: the lexical form | `{"type":"string"}` |

A date or time with no RFC 3339 form is `datatype`, never rewritten: the
lexical form is the data, and a rewrite would be a guess.

### 5.3 Fields declared as strings

`varve:fieldAs "string"` makes the member a JSON string for every value: an
IRI's text, a literal's lexical form, a language-tagged string's text. The
datatype check of §5.2 still applies, and §5.4 does not. The schema is
`{"type":"string"}`. It is the declared way to carry a number §5.4 refuses.

### 5.4 Numbers

**A JSON number is emitted only when the shortest ECMAScript form of the
nearest double equals the literal's exact decimal value.** The JSON text is
then exact, and every IEEE 754 consumer parses it to the same double. So
`"19.99"^^xsd:decimal` is `19.99`, `"9007199254740993"^^xsd:integer` (2^53 + 1)
is `number`, and an `xsd:decimal` with more significant digits than a double
holds is `number`, unless the field is declared a string. The comparison is
of values: `"1.50"` and `1.5` are equal; `-0` is `0`.

## 6. Schemas

### 6.1 The generated schema

The schema of a declaration is generated when it is read, never stored. It is
JSON Schema draft 2020-12, written in canonical form:

```json
{"$id":"…/revisions/42","$schema":"https://json-schema.org/draft/2020-12/schema",
 "additionalProperties":true,"description":"An order was placed.",
 "properties":{"order":{"format":"iri","type":"string"},…},
 "required":["order","placedAt","total"],"title":"OrderPlaced","type":"object",
 "x-varve-version":1}
```

`required` lists the required field names in code-point order; a field's
`rdfs:comment` is its `description`. `additionalProperties` is `true`, so an
instance of a later compatible revision is valid under every earlier revision
of its version (§7.2).

### 6.2 Versions, revisions and URLs

A **revision** of version n of type T is a position at which the schema
generated for T (its bytes with `$id` removed) differs from the one generated
at the position before, or at which T is first declared, while T's version is
n. Under `{base}datasets/{name}/events/types/{T}`:

| URL | Serves |
|---|---|
| `…/versions/{n}/revisions/{r}` | the schema of revision r: **immutable**, the `$id` every schema carries and `dataschema` names |
| `…/versions/{n}` | the latest revision of version n |
| `…` (no version) | the latest revision of the current version |

The two resolving URLs answer with the revision's body, its `$id` and a
`Content-Location` naming it. Every version and revision ever declared stays
served, at the head and as of any later position.

### 6.3 AsyncAPI

`GET …/events/asyncapi` is an AsyncAPI 3.0.0 document generated from the
declarations as of the read's position, in canonical JSON:

- `info`: the dataset's name and the position;
- `defaultContentType`: `application/cloudevents+json`;
- `servers`: `sse` (`protocol` `https` or `http`, `host` and `pathname` from the
  base) always; `eventhubs` (`protocol` `amqp`) when `Varve:Events:EventHubs`
  names a namespace and hub;
- `channels`: one per type, `address` `datasets/{name}/events?type={T}`, its
  messages the type's event and the problem event of §4.3;
- `operations`: one `receive` per channel;
- `components.schemas`: the CloudEvent envelope, each type's current schema,
  and the problem;
- `components.messageTraits.cloudEvent`: the CloudEvents attributes, named.

**There is no CloudEvents binding** in AsyncAPI 3.0, and no Event Hubs binding.
The envelope is described as the payload of a structured-mode message instead.

### 6.4 The read-forever rule for schemas

Because a schema is computed, the generator is under the read-forever rule:
`tests/Varve.Events.Tests/fixtures/schemas/` holds, per case, a declaration
and the schema bytes each of its versions generates. A change to the generator
that alters any of them fails the build's tests, and no switch regenerates
them in CI.

## 7. Checks on a declaration change

A commit whose effective delta touches a catalogue graph is checked by a
dataset-level validator (ADR 0058), on every write path, before it commits.
The check compares the declarations of the touched catalogue graphs before
and after the commit. A refusal is `422` with one of two problem types and
nothing committed.

### 7.1 Well-formedness: `event-declaration-invalid`

Members: `declaration` (the type node's IRI, or the graph's when no node is
in question) and `reason`:

| Reason | When |
|---|---|
| `graph` | a triple in a graph under `varve:events-catalogue/` that is not `enc(G)` of a graph |
| `blank-node` | a blank node in a catalogue graph |
| `term` | a required term missing, a term given twice, a value of the wrong kind, an unknown `varve:` term on a type or field node |
| `select` | the query does not parse, or is not of §5.1's form |
| `field` | a field name twice in a type, a field node of two types, a datatype outside §5.2 |
| `class-taken` | a class declared twice in one catalogue graph |
| `name-taken` | the short name is another type node's |
| `removed` | a declaration, or a triple it requires, is retracted (§3.4) |
| `version` | the version is lower than before |

**`name-taken` says nothing more.** The problem names the declaration being
written and the reason; it never names the other type node, its graph, its
class or its version, so a caller learns nothing of a graph it cannot read.

### 7.2 Compatibility: `event-schema-incompatible`

When a type's version is unchanged, its new declaration must be compatible
with its old one: every instance valid under the new schema is valid under the
old one. Member `fields`: an array of `{"field": name, "change": kind}`, in
field-name order. The changes, and whether they keep the version:

| Change | Kind | Compatible |
|---|---|---|
| an optional field added | | yes |
| the query's text, the subject, the class, a description | | yes |
| retirement | | yes |
| a field removed | `removed` | no |
| a field's JSON type or format changed | `type` | no |
| an optional field made required, or a required one added | `required` | no |
| a required field made optional | `optional` | no |
| `fieldAs` or `fieldKeepLanguage` changed | `type` | no |

An incompatible change is accepted when the declaration states a higher
version; the version may skip numbers, and never decreases.

## 8. The endpoints

All of them authorise `read` on the dataset, honour `Varve-As-Of` where the
read is one position, and see only the catalogue graphs the caller reads.

| Route | Answer |
|---|---|
| `GET /datasets/{name}/events?from=&to=&fromTime=&toTime=&type=` | the events of the range: `application/cloudevents-batch+json` (a JSON array, canonical), paged by the commits resource's page size with `Link: rel="next"`; `text/event-stream` for an open range |
| `GET /datasets/{name}/events/types` | the dataset's types, JSON |
| `GET /datasets/{name}/events/types/{T}[/versions/{n}[/revisions/{r}]]` | a schema, `application/schema+json` |
| `GET /datasets/{name}/events/asyncapi` | the AsyncAPI document, `application/json` |
| `GET /events/types` | the types of every dataset the caller reads, each with its dataset |

**The range.** `from`, `to`, `fromTime`, `toTime` and `Last-Event-ID` resolve
as on the commits resource (`change-feed.md` §4), and `type` restricts the
events to one short name. A commit with no event is skipped. The events of
one commit are never split across pages.

**Server-sent events.** One event per commit with at least one event:
`event: events`, `id:` the position, and `data:` the commit's events as a
canonical JSON array. A reconnect with `Last-Event-ID` resumes after that
commit, so a commit is delivered whole or not at all.

**The lists.** `{"types":[…]}`, each entry `{"class","dataset"?,"description"?,
"name","retired","schema","sourceGraph","version","versions":[{"revisions":[…],
"schema","version"}]}` in short-name order, `sourceGraph` `null` for the
default graph; the server-wide list adds `dataset` and orders by dataset, then
name.

**Cost.** Each commit with an event opens an as-of read at its position for the
closures (§5.1), under `Limits:MaxAsOfDistance` (ADR 0114); a range that
exceeds it ends with the stream's `error` event, as the commits resource's
read limits do. The declarations come from the catalogue graphs' history,
replayed once per dataset and kept current, never from an as-of read.
