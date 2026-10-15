---
set: domain-events-as-cloudevents
namespace: varve
adr: 0143
decisions:
  - key: EventsAreQuadsInTheCommit
    statement: "A domain event is a node typed with a declared class in a source graph, written in the same commit as the change it describes; it occurs at the position whose effective delta asserts that type"
  - key: MatchingIsExact
    statement: "An event occurs on an asserted rdf:type of the declared class exactly; no subclass is followed and nothing is inferred, simple entailment until Inference (#95) states otherwise"
  - key: RenderedFromTheClosureAsOf
    statement: "The declaration's SELECT runs over the event node's closure in its source graph as of the commit's position, with ?event bound by VALUES under the projection, never over the commit's delta"
  - key: NumbersRoundTripExactly
    statement: "A JSON number is emitted only when the shortest ECMAScript form of the nearest double equals the literal's exact decimal value; otherwise the event is a problem unless the field is declared a string"
  - key: DatesWithoutTimezoneAreProblems
    statement: "A dateTime or time without a timezone, or any date or time value whose lexical form is not RFC 3339, is a problem unless the field is declared a string; it is never rewritten"
  - key: EnvelopeIsCanonicalCloudEvents
    statement: "An event is a CloudEvents 1.0 structured JSON event in RFC 8785 canonical form, id the position and the node's IRI, sequence the padded position and ordinal, time the commit's timestamp, dataschema the revision in force"
  - key: SourceIsConfiguredOrTheRequestBase
    statement: "source is built on Varve:Events:Source when configured and on the request's base otherwise; byte identity holds per base"
  - key: ProblemsAreInBandCloudEvents
    statement: "An event that cannot be rendered is a CloudEvent of the reserved type event-not-rendered with a problem as its data, in the event's place, never skipped and never guessed"
  - key: EventsRangeIsTheCommitsRange
    statement: "GET /datasets/{name}/events serves a range with the commits resource's position semantics, a canonical CloudEvents batch when bounded and one server-sent event per commit when open"
  - key: VarveEventsAtLayerFour
    statement: "Varve.Events renders, generates and checks over quads at layer 4 by dependency, referencing no store and no protocol"
---

The rulings of [ADR 0143](../adr/0143-domain-events-as-cloudevents.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration, part 2,
of #93 (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
