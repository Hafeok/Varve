# `inference-unavailable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/inference-unavailable` |
| **Title** | The binding's materialised closure is not available. |
| **Status** | `503` |
| **Members** | `binding`, `reason`, `failure`, `limit`, `reached` |

A read needed a materialising binding's closure, and the binding is stopped
(ADR 0195 point 6). `reason` says why:

- **`memory-limit`**: its closure would hold more than
  `Varve:Inference:ClosureBytes` allows, or more than the instance's share
  for all its bindings together: the heap limit less the memtable's worst
  case and `Varve:Inference:ReserveBytes`. The
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

`failure` is ADR 0242 ruling 2's class, and neither carries a
**`Retry-After`**, since waiting helps neither:

- **`configuration`** with `reason` `memory-limit`: the instance's settings
  or memory cannot hold the closure. A setting or more memory fixes it.
- **`defect`** with `reason` `failed` (ADR 0242, amended 2026-11-19): the
  code failed, and only a version that fixes it does. No `limit` or
  `reached`.

The read is answered at once, without waiting. Tier-1 regimes (`rdf`, `rdfs`,
`d`) on the same graphs are still served. A caller whose grant does not let
it read the closure is answered by its grant first, so this problem is
never shown to a caller who could not otherwise learn that the binding
exists.

On `/health/ready` the binding's `inference` line says `stopped
(memory-limit)`, with `limit` and `reached`, or `stopped (failed)`, and the
dataset stays ready.

## What to do

**`failure` `defect` (`reason` `failed`): report it and upgrade.**
- **Report it**, with the instance's log around the stop: the binding's
  projection logs the exception that stopped it. Include the binding's
  declaration and the dataset's position from readiness.
- **Upgrade** to a version that fixes it, and restart: the restarted
  instance builds the binding from the head. Restarted on the same version,
  it fails again at the same commit.
- **Meanwhile**, read under a tier-1 regime (`rdf`, `rdfs`, `d`), which
  needs no closure, or remove the binding's declaration.

**`failure` `configuration` (`reason` `memory-limit`): give the closure the
memory it needs, or need less.** A stopped binding comes back by one of two
acts, and only these:

- **Restart the instance** with `Varve:Inference:ClosureBytes` or the
  instance's memory raised. A restarted instance never loads a saved
  stopped state: it builds the binding from the head, once, under the limit
  it reads when it starts. Restarted with nothing changed, it stops again
  at the same place. Unset, `ClosureBytes` is a quarter of the collector's
  heap limit, or the pool if that is less; it can be at most the pool, the
  heap limit less the memtable's worst case and
  `Varve:Inference:ReserveBytes`, which the instance checks when it starts.
  Readiness's `peak` for the binding, when it last ran, says what it asked
  for. `docs/operator/small-machine.md` sizes a closure under each profile,
  and says what a small container with no pool does.
- **Change the binding's declaration** with `varve apply`, or a `PUT` to
  `/manage/datasets/{name}`: fewer data graphs, or the `varve:RDFS` profile
  in place of `varve:OWL2RL`. The changed binding is built at once from the
  head, with no restart, under the limit in force. Applying the same
  declaration again builds nothing.
- **Or read under a tier-1 regime**, which needs no closure.

There is no retry on a running instance: under the same limit, the same
data and the same version, a retry would stop the same way. **Nothing tries
a stopped binding again automatically**: not a data commit, not a commit to
one of its TBox graphs, not a timer.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
