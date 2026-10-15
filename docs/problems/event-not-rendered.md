# `event-not-rendered`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/event-not-rendered` |
| **Title** | An event could not be rendered. |
| **Status** | `422` |
| **Members** | `event`, `eventType`, `reason` |

Never a response. It is the `data` of a CloudEvent in the events stream,
which stands in the place of an event that could not be rendered (ADR 0143,
`docs/spec/events.md` §4.3). The CloudEvent's `type` is this type's IRI, and its
`datacontenttype` is `application/problem+json`. A replay yields it again, in
the same place, with the same bytes.

- `event` is the event node's IRI, or `_:` for a blank node.
- `eventType` is the declared short name.
- `reason` is one of:
  - `blank-event`: the event node is a blank node;
  - `types`: the node takes two declared classes in one commit;
  - `no-row` or `rows`: the query yields no row, or more than one;
  - `unbound-required`: a required field is unbound;
  - `datatype`: a value is not of its field's datatype, or has no JSON form, such as a date or time without a timezone;
  - `number`: a number fails the number rule.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
