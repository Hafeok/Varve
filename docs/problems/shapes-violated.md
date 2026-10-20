# `shapes-violated`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/shapes-violated` |
| **Title** | The commit introduces a violation of a shapes binding. |
| **Status** | `422` |
| **Members** | `binding`, `shapesGraph`, `findings`, `report`, `shapesChanged` |

A commit to a dataset with a `reject` shapes binding left one of the focus
nodes it affects with a `sh:Violation` result the dataset did not have before
it (ADR 0169). Nothing was committed. A violation that was already there does
not refuse a commit: under `reject` the set of violations never grows.

- `binding` and `shapesGraph` are the binding's IRI and its shapes graph, in
  canonical N-Triples.
- `findings` lists each new violation by `focusNode` and `path`, with
  `value`, `sourceShape`, `component`, `severity` and `messages`, every term
  in canonical N-Triples.
- `report` is the validation report restricted to the commit's affected focus
  nodes after it, as canonical N-Quads (ADR 0154, amended).
- `shapesChanged` is true when the commit changed the shapes graph too: its
  data was then checked against the new shapes, and `detail` says that what
  the new shapes make non-conforming elsewhere is the validation projection's
  to report, not a reason to refuse.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
