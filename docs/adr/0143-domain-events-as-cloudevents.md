# 0143 — Domain events as quads, rendered as CloudEvents in canonical JSON

## Status

**Accepted — filed unaccepted by milestone Client and integration, part 2, of
#93, 2026-10-15** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer in the part 2 brief, points 1, 2, 7 and 8,
and on its plan:
- a problem is an in-band CloudEvent of a reserved type;
- a `dateTime` or `time` without a timezone is a problem unless the field is a
  string;
- the number rule is the sentence of point 3 below;
- matching is exact, with no entailment;
- `source` takes `Varve:Events:Source`, or the request's base.

The specification is `docs/spec/events.md` §4–§5.

## Context

Integrating systems want named domain events: *an order was placed*, with a
total and a time. Two shapes are usual. One keeps a separate event store
beside the data, so the two can disagree. The other derives events from row
changes after the fact, so their names are guesses. Varve has a third shape for
free. A commit is atomic, so a publisher can write the event as quads in the
same commit as the change it describes. The commits resource filtered to that
graph is then the domain stream (ADR 0129), atomic with the data and replayable
for ever.

That serves consumers that speak RDF. Most do not. They want JSON with a
fixed shape, a type name and a schema, in an envelope their tooling knows.
CloudEvents 1.0 is that envelope: it is vendor-neutral, and Event Hubs, Kafka,
Knative and the Azure and AWS event routers all carry it.

## Decision

1. **Events are quads in a source graph, written in the same commit as the
   change.**
   - An event occurs at p for each node e that p's effective delta asserts
     `rdf:type C` in G, where C is the `varve:eventClass` of a type declared
     in G's catalogue graph as of p and not retired (ADR 0144).
   - **Matching is exact.** No subclass is followed and nothing is inferred.
     The entailment regime is simple entailment until Inference (#95) states
     otherwise.
   - A retraction renders nothing. An assertion that I2 empties is no event.
2. **The rendering is a pure function of the position and the declaration.**
   - The declaration's `SELECT` runs over the closure of e in G as of p: e's
     quads, and the quads of every blank node reached from them,
     recursively. `?event` is bound to e by a join with `VALUES` under the
     projection.
   - It does not run over the commit's delta, so it stays correct when I2
     empties part of the delta because a quad already held.
   - Each projected variable becomes a `data` member through a fixed
     XSD-to-JSON mapping (spec §5.2):
     - integer and decimal types become numbers under point 3;
     - finite doubles and floats become numbers;
     - booleans become booleans;
     - `dateTime`, `date` and `time` become RFC 3339 strings, only when their
       lexical form is one;
     - IRIs and every other literal become strings;
     - a language and a direction are dropped unless the field declares
       `varve:fieldKeepLanguage`.
3. **Numbers.** A JSON number is emitted only when the shortest ECMAScript
   form of the nearest double equals the literal's exact decimal value. The
   JSON text is then exact, and every IEEE consumer parses it to the same
   double. Anything else is a problem, unless the declaration maps the field
   to `string`, which is then always used.
4. **The envelope** is CloudEvents 1.0, structured JSON mode, written in RFC
   8785 canonical form by `Varve.Json.Canonical` (ADR 0147). So the same event
   is always the same bytes.
   - `id` is the position, a space, and the event node's IRI. An IRI holds no
     space, so the split is unambiguous.
   - `source` is the dataset's URL, built on `Varve:Events:Source` when it is
     configured and on the request's base otherwise. Byte identity holds per
     base, and the operator guide tells a deployment with more than one host
     name to set the option.
   - `type` is the declared short name.
   - `time` is the commit's timestamp, in the delta format's form.
   - `subject` is the declared subject variable's value.
   - `dataschema` is the schema revision in force at p (ADR 0145).
   - `sequence` (the CloudEvents extension) is the position zero-padded to 20
     digits, `.`, and the event's ordinal within its commit, padded to 6.
     That orders every event of a dataset totally as a string.
   - Within a commit, events are ordered by the node's IRI, then by the type's
     short name.
5. **An event that cannot be rendered is a problem in the stream, in its
   place.**
   - It is a CloudEvent whose `type` is the problem type
     `https://w3id.org/varve/problems/event-not-rendered`, whose
     `datacontenttype` is `application/problem+json`, and whose `data` names
     the event, its type and a reason:
     - `no-row`;
     - `rows`;
     - `unbound-required`;
     - `datatype`;
     - `number`;
     - `types`;
     - `blank-event`.
   - The batch stays a valid CloudEvents batch. Replay stays byte-identical. A
     consumer that resumes sees the problem again, at the same place.
   - Nothing is skipped and nothing is guessed.
6. **The endpoints.**
   - `GET /datasets/{name}/events` serves a range with the commits resource's
     position semantics (`from`, `to`, `fromTime`, `toTime`,
     `Last-Event-ID`, paging by `Link`), so resume and replay are the same
     act:
     - as `application/cloudevents-batch+json` for a bounded range;
     - as server-sent events on an open range: one SSE event per commit, its
       `id` the position and its data that commit's events as one canonical
       array, so a reconnect never splits a commit.
   - `type` restricts the range to one short name.
7. **`Varve.Events` is a package at layer 4, by dependency.**
   - It references:
     - `Varve.Rdf`;
     - `Varve.Xsd`;
     - `Varve.Sparql`;
     - `Varve.Sparql.Evaluation`, for the `SELECT`;
     - `Varve.Json.Canonical`.
   - It does not reference `Varve.Store` or `Varve.Protocol`, and nothing on
     layer 4 references it.
   - It holds the declaration model and reader, the mapping, the renderer,
     the schema and AsyncAPI generators and the compatibility check, all
     functions of quads. An embedded or browser host can render events
     without a server (constraint 3).
   - `Varve.Protocol` maps a store's commits and as-of views to it, as it does
     for `Varve.Delta`.

## Alternatives considered

- **JSON-LD compaction with a frame per type.**
  - A frame is not a schema. Compaction's output shape depends on the data: a
    property with one value is a scalar, and with two it is an array. Its
    types follow the context's coercions, not a per-field declaration.
  - A consumer could not generate a client from it, and the number rule
    could not be stated over it.
  - A declared `SELECT` fixes one row with named, typed columns, which is
    what an event is.
- **Render from the commit's delta.** The delta is empty for a quad that
  already held (I2), so an event re-asserting a known fact would lose fields.
  The closure as of p is the state the event describes.
- **An event store beside the log.** It is two sources of truth that
  disagree on a crash. The events graph is in the same commit as the change.
- **Render numbers as strings always.** That is safe, but it is the wrong
  default for the common case, and the declaration can ask for it per field.
- **Skip an unrenderable event, or render it partly.** Either is a guess a
  consumer cannot detect. A problem in place is detectable and replays the
  same.
- **Subclass matching.** It needs an entailment regime, which is Inference's
  (#95) to state, and it would make an event's occurrence depend on the
  ontology as well as the data.
- **`Varve.Events` inside `Varve.Protocol`.** Then only a server could render,
  and constraint 3 asks for three hosts from one core.

## Consequences

- A dataset's domain stream is available to consumers that do not speak RDF,
  and is atomic with the data.
- The rendering's bytes are under the read-forever rule. They change only when
  the log or the declarations do, never with a release. The replay property
  (spec §8) and the schema golden files (ADR 0145) hold it.
- Each commit that holds an event costs one as-of read for the closures,
  measured in the traceability record. `MaxAsOfDistance` (ADR 0114) bounds it.
  Object storage's validity-interval extents (its ADR 0138) answer as-of
  reads from the index, which removes the distance cost. This ADR does not
  optimise around it.
- Three problem types join the catalogue (ADR 0119, amended).

## Checks

- **Checked against the accepted ADRs** (0001–0133) and the specification.
  It touches:
  - **I2** (the effective delta, which is why the closure and not the
    delta);
  - **0022** (handles, externalised at the boundary);
  - **0057** (the `SELECT`'s evaluator);
  - **0095** (an as-of read's lifetime is one commit's rendering);
  - **0097** and **0129** (the range, the paging and `Last-Event-ID`,
    reused);
  - **0107** (the catalogue's grants, ADR 0144);
  - **0114** (the as-of distance);
  - **0119** (the problem catalogue);
  - **0128** (the timestamp's form).

  No conflict.
- **Layer ownership.**
  - `Varve.Events`: layer 4, by dependency.
  - `Varve.Protocol`: layer 5, the endpoints.
  - `Varve.Server`: layer 6, `Varve:Events:Source`.
- **Analyzer rule.** None new. `VARVE0003` applies to nothing here: the
  rendering is per event, not per quad.
- **Open questions owned.** None. Inference (#95) owns subclass matching.
