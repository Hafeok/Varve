# `unavailable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/unavailable` |
| **Title** | The dataset cannot answer now. |
| **Status** | `503` |
| **Members** | `reason` |

The dataset cannot answer now: its projection failed, it is closing, or a dataset asked to open did not (ADRs 0094, 0106).

A write this instance would forward (ADR 0168) is answered with `reason`
`no-writer` when no instance holds the dataset's writer epoch. A forward
whose writer could not be reached, or whose answer was lost, is
[`commit-outcome-unknown`](commit-outcome-unknown.md) (ADR 0214): it is
sent once and never again, and whether the writer received any of it is
unknown here, so the client decides.

A move of event declarations forwards its own writes to the system writer
(ADR 0167). When that forward never reached the system writer, because no
connection to it was made, nothing was committed, and the move is answered
with `reason` `writer-unreachable`. When it was sent and the answer lost,
the move is `commit-outcome-unknown` too. Either way, run the move again: it
resumes.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
