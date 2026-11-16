# `inference-unavailable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/inference-unavailable` |
| **Title** | The binding's materialised closure is not available. |
| **Status** | `503` |
| **Members** | `binding`, `reason`, `failure`, `limit`, `reached` |

A read needed a materialising binding's closure, and the binding is stopped:
its closure would hold more than `Varve:Inference:ClosureBytes` allows (ADR
0195 point 6). `reason` is `memory-limit`. `failure` is `configuration`, ADR
0242 ruling 2's class: the instance's configuration cannot hold the closure,
and no retry helps, so the response carries **no `Retry-After`**. `limit` is
the setting, in bytes, and `reached` what the closure held when it stopped,
a lower bound on what it needs.

The read is answered at once, without waiting. Tier-1 regimes (`rdf`, `rdfs`,
`d`) on the same graphs are still served. A caller whose grant does not let
it read the closure is answered by its grant first, so this problem is
never shown to a caller who could not otherwise learn that the binding
exists.

On `/health/ready` the binding's `inference` line says `stopped
(memory-limit)`, with `limit` and `reached`, and the dataset stays ready.

## What to do

- **Raise `Varve:Inference:ClosureBytes`**, or the instance's memory, and
  restart the instance: the setting is read at start. Unset, it is a quarter
  of the collector's heap limit. `docs/operator/small-machine.md` states a
  closure's bytes per quad under each profile.
- Or narrow the binding: fewer data graphs, or the `varve:RDFS` profile in
  place of `varve:OWL2RL`.
- Or read under a tier-1 regime, which needs no closure.

A commit that changes one of the binding's TBox graphs tries the closure
again under the same limit; nothing tries it on a timer.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
