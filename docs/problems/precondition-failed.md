# `precondition-failed`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/precondition-failed` |
| **Title** | The precondition does not name the current version. |
| **Status** | `412` |
| **Members** | `position`, `expectedPosition`, `headPosition`, `graphs` |

A precondition that does not name the current version, checked before anything is read or parsed. `headPosition` and `position` are the head.

- An `If-Match` on an update or the settings resource names the head; `expectedPosition` is what it named (ADR 0094).
- An `If-Match` on a Graph Store write names the graph's version, and a graph with no quads matches no tag; `If-None-Match: *` on a `PUT` fails when the graph holds a quad. `graphs` gives the graph and the version it has (ADR 0126).
- `Varve-If-Graphs-Match` on an update names graphs and the versions read; `graphs` lists each graph that is at another version, `{"graph": "<iri>" | "default", "version": n}` (ADR 0126).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
