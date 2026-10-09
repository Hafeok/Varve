# `rejected`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/rejected` |
| **Title** | A validator rejected the commit. |
| **Status** | `422` |
| **Members** | `report` |

A pre-commit validator rejected the commit; `report` is the validator's report as term lines (ADRs 0017, 0058, 0094).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
