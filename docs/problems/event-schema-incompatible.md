# `event-schema-incompatible`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/event-schema-incompatible` |
| **Title** | The declaration change is incompatible with its version. |
| **Status** | `422` |
| **Members** | `declaration`, `fields` |

A commit changed an event type's declaration and kept its version, and the new
schema would accept an instance the old one refuses (ADR 0145,
`docs/spec/events.md` §7.2). Nothing was committed. Declare a higher version
to make the change.

- `declaration` is the type node.
- `fields` is an array of `{"change": kind, "field": name}`, in field-name
  order. The kind is one of:
  - `removed`;
  - `type`: the field's JSON type or format changed, or `fieldAs` or `fieldKeepLanguage` changed;
  - `required`: an optional field made required, or a required field added;
  - `optional`: a required field made optional.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
