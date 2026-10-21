# `read-unannounced-extent-gone`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/read-unannounced-extent-gone` |
| **Title** | An unannounced read found an extent gone. |
| **Status** | `503` |
| **Members** | none |

The read was opened with `--unannounced` (ADR 0160): its identity may only
read the store, so it wrote no reader record, and the writer may delete what
it reads. It found an extent its catalog version names gone — merged and
deleted since it attached. The read is failed, never answered wrong, and
this is not damage. Read again: a fresh attach reads the newest catalog. A
read that must not be cut this way needs an identity that can write its
record under `readers/`.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
