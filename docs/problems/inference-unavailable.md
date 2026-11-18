# `inference-unavailable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/inference-unavailable` |
| **Title** | The binding's materialised closure is not available. |
| **Status** | `503` |
| **Members** | `binding`, `reason`, `failure`, `limit`, `reached` |

A read needed a materialising binding's closure, and the binding is stopped
(ADR 0195 point 6, as amended 2026-11-18). `reason` says why:

- **`memory-limit`**: its closure would hold more than
  `Varve:Inference:ClosureBytes` allows, or more than the instance's share
  for all its bindings together, three quarters of the heap limit. The
  refusal is made before the memory is asked for, so the instance stays up.
  `limit` is the setting, in bytes, and `reached` what the closure would
  have held with the request that was refused, a lower bound on what it
  needs.
- **`failed`**: its projection failed in a way no repeat can cure: a
  defect in the engine or the projection, where the same commit fails the
  same way again; or the store failing after a pass had begun changing the
  closure, which the pass's reads beforehand exist to prevent and which only
  a build repairs. The failure is in the instance's log.

A store that fails to answer is **not** a stop. A pass reads what it needs
before it changes anything; when a read or a write fails (an I/O error, a
timeout, an object store not answering), the binding logs it, holds off for
a second, doubling to thirty, and resumes from where it was, with no
rebuild. Meanwhile it reads `following` with a growing lag, and reads wait
for it as they would for any lag.

`failure` is `configuration`, ADR 0242 ruling 2's class: no retry helps, so
the response carries **no `Retry-After`**.

The read is answered at once, without waiting. Tier-1 regimes (`rdf`, `rdfs`,
`d`) on the same graphs are still served. A caller whose grant does not let
it read the closure is answered by its grant first, so this problem is
never shown to a caller who could not otherwise learn that the binding
exists.

On `/health/ready` the binding's `inference` line says `stopped
(memory-limit)`, with `limit` and `reached`, or `stopped (failed)`, and the
dataset stays ready.

## What to do

A stopped binding comes back by one of two acts, and only these:

- **Restart the instance.** A restarted instance never loads a saved
  stopped state: it builds the binding from the head, once, under the limit
  it reads when it starts. Restarted with nothing changed, a `memory-limit`
  binding stops again at the same place, so raise the limit first; a
  `failed` binding fails again unless the version started fixes the
  defect.
- **Change the binding's declaration** with `varve apply`, or a `PUT` to
  `/manage/datasets/{name}`: its profile, its TBox graphs, its data graphs
  or `varve:crossGraph`. The changed binding is built at once from the head,
  with no restart, under the limit in force. Applying the same declaration
  again builds nothing.

There is no retry on a running instance: under the same limit and the same
data, a retry would stop the same way. To make the next build fit:

- **Raise `Varve:Inference:ClosureBytes`**, or the instance's memory, and
  restart the instance: the setting is read at start. Unset, it is a quarter
  of the collector's heap limit; it can be at most three quarters of it,
  which the instance checks when it starts. Readiness's `peak` for the
  binding, when it last ran, says what it needed. `docs/operator/small-machine.md` states a
  closure's bytes per quad under each profile.
- Or narrow the binding: fewer data graphs, or the `varve:RDFS` profile in
  place of `varve:OWL2RL`.
- Or read under a tier-1 regime, which needs no closure.

**Nothing tries a stopped binding again automatically**: not a data
commit, not a commit to one of its TBox graphs, not a timer. A binding that
`failed` is a defect to report, with the log.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
