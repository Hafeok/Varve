# `declaration-invalid`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/declaration-invalid` |
| **Title** | The management declarations are not valid. |
| **Status** | `422` |
| **Members** | `findings` |

A commit to the fleet's system dataset would leave a management graph it
touches with a finding of the code validator (ADR 0166 point 4): a term the
vocabulary does not know, a value of the wrong kind, a blank node, a term
reserved for a later milestone, or an event-schema term, which management
graphs hold only from part 2 on (ADR 0167). `findings` lists each, with its
`graph`, its `focus` node and `path` when it has them, and a `message`.
Nothing was committed.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
