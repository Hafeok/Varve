# The fleet

Servers that share one object store share one management plane: they are a
**fleet** (ADR 0163). This page is how a fleet holds together, who writes
what, where a write goes, and what each part costs in store requests.

## The system dataset

Management state — the datasets, their placement, settings and grants — is
one ordinary Varve dataset per fleet, the **system dataset**, under the store
prefix `_fleet/` (or `{DatasetsRoot}/_fleet/` on the file backend). No dataset
name starts with `_`, so no user dataset can take it, and it has no
data-plane address: it is read and written through `/manage/`.

- **One instance writes it.** `Varve:Fleet:Writer` decides: `true`, `false`,
  or an instance name compared with `Varve:ObjectStorage:Instance`, so that
  identical pods share one configuration. The writing instance claims the
  system dataset's writer epoch (ADR 0136).
- **Every other instance follows it** as a tier-1 follower (ADR 0164): a
  read-only open that never claims, advanced by probing the next log slot.
- **A fleet of one** — a server with no object store — is its own writer
  whatever the key says.

Each instance enforces the grants at the management position it has
**applied** (ADR 0163 point 3). Every response names it in
`Varve-Management-Position`.

## Who writes a dataset: placement

A dataset is written by the instance its declaration places it on:
`varve:placedOn "<instance>"`, or, without one, the system writer's
instance. An instance claims a dataset placed on it when its `Writer` key
lets it claim at all (anything but `false`). Every other instance follows an
object-store dataset and forwards its writes; a file or memory dataset placed
elsewhere is not served there.

**A placement change works by events, never by time** (ADR 0163 point 12):

1. The old writer applies the management position that shows the change. It
   opens the dataset as a follower, then releases its epoch with the reason
   `placement`.
2. The newly placed instance follows until it observes that release in the
   writer record, then claims.
3. A write in flight across the change is answered, or refused with `503`,
   by the old writer. A forward made after the release reaches the new one.

An instance that starts claims its placement whatever the record says,
fencing any predecessor: a restart is the recovery from a writer that died
without releasing. `varve lease <container>/<dataset>` shows every claim and
release, `placement` included.

### Standbys: a writer set

A dataset declared with `varve:automaticHandover true` may name standbys,
`varve:standbyOn "<instance>"`, any number of them, none equal to its
`placedOn` (ADR 0180). The `placedOn` instance is the **preferred writer**,
and the two together are the **writer set**: only a member ever claims.

- **At start**, a member claims only when the writer record is empty or
  released and it is the preferred writer, when the record names it (a
  restart after its own crash), or when the record names an instance that is
  not a member. Otherwise it starts as a standby. So a returning writer never
  takes the dataset from a healthy successor.
- **A standby tails on the interval**, whether or not reads arrive, and reads
  the writer record on the same interval. That is how it sees a release.
- **After a release, its reason decides who claims.** For `placement`, only
  the `placedOn` instance. For `shutdown`, `storage-error` or
  `local-disk-error`, every warm member races, and the store's conditional
  write picks one. The winner claims in place: it keeps what it has applied
  and cached, and commits at once.
- **A placement change still works by events**: changing `placedOn` makes the
  holder release for `placement`, whoever it is, and the new `placedOn`
  claims. Taking the holder out of `standbyOn` does the same. A holder that
  differs from `placedOn` after a race is a state, and nothing reacts to it.
- **A superseded holder** rejoins as a standby, and may claim again once it
  is warm.

## Where a write goes: forwarding

Any instance accepts a write (ADR 0168). One that does not write the dataset
— or, for a management write, the system dataset — forwards it to the
writer's address in the writer record, `Varve:Fleet:InternalUrl`:

- **The forwarding instance authenticates first.** An anonymous request is
  refused there; authorisation, attribution and evaluation are the writer's.
- **The body is read whole, then sent once**, with the caller's token and the
  headers a write's meaning depends on. SPARQL Update is forwarded whole, so
  its `WHERE` is evaluated at the writer.
- **The answer comes back unchanged**: status, `Varve-Position`, `ETag`, the
  writer's `Varve-Request-Id`, any problem.
- **The hop rule.** A forwarded request carries `Varve-Forwarded`. An
  instance that receives one and does not write the dataset answers
  [`503 writer-moved`](../problems/writer-moved.md), with the epoch, and never
  forwards it again: a request crosses at most one hop. Retry, and the route
  is read again.
- A writer that cannot be reached is `503 unavailable` with the reason
  `writer-unreachable`; no instance holding the epoch is `no-writer`. A
  forward is never sent twice.

**`Varve:Fleet:InternalUrl` carries the caller's bearer token.** It is
`https`, or stays inside a private network: a pod's headless-service name, an
app's internal FQDN.

Request ids are `<instance>:<id>`, unique across the fleet.

## Reading your writes

- **A data read** may send `Varve-Min-Position: p`, the `Varve-Position` a
  write answered. A follower below `p` advances once, and answers
  [`503 dataset-behind`](../problems/dataset-behind.md) only if it is still
  behind.
- **Any request** may send `Varve-Min-Management-Position: p`, the position
  a management write answered: the instance advances its system follower
  once, and answers
  [`503 management-behind`](../problems/management-behind.md) only if it is
  still behind. Neither waits.

## Readiness

`/health/ready` carries a `management` object: the instance's `role` in the
system dataset (`writer`, `follower` or `superseded`), the `epoch`, the
applied `position`, the `lag` behind the system tail, and the `bootstrap`
state (`recorded`, `absent` or `mismatch`). Each dataset carries:
- its role: `writer`, `standby`, `follower` or `elsewhere`;
- its `position`: what this instance has applied, a follower's included;
- its `lag`, the projection lag: the commits it has applied to its log but
  not yet to its default projection;
- for a standby, `tailing` and `warm`. A standby is **warm** when it tails on
  the interval, its last advance succeeded, its projection lag is zero, it is
  not re-opening, and it has advanced since it opened. Only a warm standby
  races for a release. A standby that is not warm is still ready to serve
  reads;
- `validation`: each shapes binding's projection lag, beside `warm` and never
  part of it.

A follower's position is how far it has read the writer's log, not a
distance from the writer's head: no instance knows the head without asking
the writer, so readiness reports none. To read your own write on any
instance, send the position its response named as `Varve-Min-Position`
(above). An instance is not ready when
its system role is `superseded`, its bootstrap is `mismatch`, its management
lag exceeds `Varve:Fleet:AllowedLag`, or a dataset fails ADR 0113's rule.
A dataset whose event declarations are moving to the management plane, and
whose move has not finished, carries `"events": {"state": "moving", "since": m₁}`, as information: it
never makes the instance unready ([Moving event schemas](moving-event-schemas.md)).

**A mistaken second system writer** — two instances with `Writer` `true` —
shows as a superseded instance in readiness, and as a run of `restart`
claims in `varve lease <container>/_fleet`.

**Without a system writer**, management writes are unavailable. The data
plane keeps serving under the grants at the last applied position: data
reads on every instance, data writes on the instances that write their
datasets.

## Request costs

What a fleet asks of the store, with `Varve:Fleet:FollowInterval` at its
default of one second:

| What | Requests |
|---|---|
| **An idle followed dataset** | **none.** A data follower is advanced only when a read arrives |
| A read on a follower whose last advance is older than the interval | one advance: a probe of the next log slot. Idle, that is **2 GETs** — the next chunk slot, then the segment's consolidated object — and **never a LIST** |
| A read on a follower advanced within the interval | none |
| A read with `Varve-Min-Position` above the follower's head | one advance, whatever its age |
| The system dataset, on every follower | one advance per interval: 2 GETs when idle |
| The system writer | one GET of its writer record per interval, to learn it was superseded |
| **An idle standby dataset** | **3 GETs per interval**: its writer record, read whole (a few hundred bytes), and the 2 GETs of an idle advance |
| A forwarded write | the writer's own requests, and one extra HTTP hop between instances; the body is read whole before it is sent |

So an idle fleet of N instances asks the store for about `2(N − 1) + 1`
GETs per interval, whatever the number of datasets, and 3 more per interval
for each standby of each dataset. A longer interval lowers
that and lengthens how soon a grant is enforced everywhere;
`Varve-Min-Management-Position` gives the guarantee to the client that needs
it.

## Deployment

The code is the same everywhere; only the address differs.

- **Kubernetes**: one StatefulSet with `Writer: varve-0` and a headless
  service ([Kubernetes](kubernetes.md)).
- **Azure Container Apps**: two apps, `varve-writer` (one replica, `Writer`
  `true`) and `varve` (N replicas, `Writer` `false`); the `varve` app's
  identity reads the container and writes only under `readers/`
  ([Azure Container Apps](azure-container-apps.md)).
- **Handover by hand**: N single-replica apps behind a front door, each with
  its own instance name, and placement changed by declaration.

**With standbys, a rolling deploy moves the writer to a standby on each
restart** (ADR 0180 point 4). The restarting holder shuts down, releasing
its epoch with `shutdown`. A warm standby wins the race to claim it, and the
restarted instance comes back as a standby. So after a rolling deploy, the
writer is whichever member won the last race, not `placedOn`. Nothing moves
it back: `placedOn` is the preferred writer, claimed at a cold start, and
changing it is how you move the writer on purpose. `varve lease` shows each
move as an `abdication` claim following a `shutdown` release.

## Event schemas in a fleet

A dataset's event declarations can live on the management plane (ADR 0167),
and [Moving event schemas](moving-event-schemas.md) moves them there. In a
fleet, the move may be sent to any instance. It is forwarded to the dataset's
writer, which makes the freeze and the copy as management writes under the
caller's token, forwarded once to the system writer. Every instance then
renders a moved dataset's events from its own follower of the system dataset,
at the management position each commit records. An instance that has not
applied that position yet answers the events request `503
management-behind`. A writer that also writes the system dataset finishes a
stopped move by itself; any other instance finishes it on the next request.
