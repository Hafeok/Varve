# `below-archive-horizon`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/below-archive-horizon` |
| **Title** | The position is below the archive horizon. |
| **Status** | `404` |
| **Members** | `horizon` |

A position below the archive horizon (spec T3). Reserved: no horizon exists yet, and `404` stays its status because archiving is undecided (ADR 0096, as reviewed 2026-10-09). `horizon` is the lowest readable position.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
