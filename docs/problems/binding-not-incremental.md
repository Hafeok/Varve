# `binding-not-incremental`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/binding-not-incremental` |
| **Title** | The binding's shapes cannot be validated incrementally. |
| **Status** | `422` |
| **Members** | `binding`, `shapes` |

A `reject` shapes binding's shapes include a shape ADR 0171 classes *full*:
its affected focus nodes cannot be bounded, so every commit would validate
every target of it. The binding does not declare
`varve:allowFullEvaluation true`, which accepts that cost (ADR 0169).

It is answered when the binding is applied, and when a commit is decided
under a binding whose shapes became full after it was applied — including the
shapes commit that makes them so. Under it, every data commit is refused until
the binding or its shapes are fixed.

- `binding` is the binding's IRI, in canonical N-Triples.
- `shapes` lists each full construct by `shape`, `construct` and `section`.

## What to do

- An operator or the dataset's admin: change the shapes so no shape is full,
  declare `varve:allowFullEvaluation true` on the binding to accept the cost,
  or set its `varve:validationMode` to `varve:Report`. `GET
  /manage/datasets/{name}/bindings/{enc(binding)}` shows each shape's class
  and the construct that decided it ([Shapes](../operator/shapes.md)).
- A client: data commits are refused until that is done; sending one again
  does not help.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
