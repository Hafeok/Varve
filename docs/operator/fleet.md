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
- **The system dataset hands over too**, when `Varve:Fleet:AutomaticHandover`
  is `true`: `Varve:Fleet:Writer` is then its writer set, the same on every
  instance, for example `a,b`, with the preferred writer first. Readiness's
  `management` object then carries `standby` and `warm` on a standby. The
  system dataset is released last when its writer shuts down, after the
  datasets it holds, so its takeover follows theirs.

### Taking over from a failed writer

A writer that crashes or hangs releases nothing, and Varve never judges
liveness itself. The orchestrator's probe does, and automation turns its
failure into a takeover request (ADRs 0179, 0181):

```sh
varve manage takeover --from a --server https://b.internal --server https://c.internal \
  --authority https://login.microsoftonline.com/{tenant}/v2.0 --client-id {operatorClientId} \
  --client-assertion /var/run/secrets/azure/tokens/azure-identity-token --scope api://varve-manage/.default
```

- **`--from` names the failed instance**, and only an epoch it still holds
  is taken over. With no datasets named, that is every dataset it holds,
  and the system dataset (`_fleet`).
- **Each `--server` is an instance that may become the writer**, in order of
  preference. Each one answers for itself, at the epoch it reads, so the
  command asks the first that is a warm standby of the dataset. It skips one
  that does not answer, the failed instance among them.
- **A stale trigger changes nothing.** The probe's verdict can arrive after
  the failed instance recovered by restart and reclaimed. So before it sends
  anything, the command asks that instance itself, at the address its last
  claim recorded, within five seconds. If it answers ready and holds the
  dataset, the command says the trigger is stale and exits 0. If it does
  not answer, or answers not ready, the takeover goes on.
- **It is safe to run twice.** A dataset another instance already holds, or
  whose epoch moved meanwhile, is reported as needing nothing, and so is
  one that declares no automatic handover; that one keeps restart as its
  recovery. Exit code 0 means every dataset is handled; 1 means some
  dataset was refused by every server, and standard error says why.
- **The token is the operator role's** on the management audience. Prefer a
  workload identity's federated token to a secret.

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
- A forward whose writer cannot be reached, or whose answer is lost, is
  [`503 commit-outcome-unknown`](../problems/commit-outcome-unknown.md),
  with `requestId` and no `position` (ADR 0214): the writer may have
  committed it, so send it again only conditionally. No instance holding the
  epoch is `503 unavailable`, reason `no-writer`. A forward is never sent
  twice.

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
  part of it;
- `spill`, `{"state": "failing", "kind": …}`, while the memtable's spills to
  the local disk fail. The writer goes on committing. With a standby, it
  releases the epoch for `local-disk-error` at the first commit where the
  memtable passes four times `Varve:Store:MemtableLimit` or the process's
  memory load reaches the GC's high-load threshold, which respects the
  container's memory limit. A small container so hands over before the
  out-of-memory killer acts;
- `cache`, the local caches turned off because a write to them failed:
  `segment-copies`, `extent-pages`. What they would hold is read from the
  store, and nothing else changes: a failing cache never moves the writer.

A writer whose batch fails against the store, with every retry spent,
releases the epoch for `storage-error` when the dataset has a standby, so the
standby takes over instead of the dataset waiting for a re-open. Without one
it refuses writes until it is opened again, as before.

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

### Restart or handover: choosing the layout

What a writer's failure costs in write downtime decides between them
(ADR 0181). These were measured by the handover (services) job's cases, on
a 4-core cloud container, with `Varve:Fleet:FollowInterval` at 200 ms. Each
time runs from the event to the first write the fleet commits again:

| Recovery | Layout | Azurite | RustFS |
|---|---|---:|---:|
| The writer restarts, with no standby | two apps, or one pod per instance without standbys | 2,568–3,221 ms | 1,998–2,264 ms |
| A warm standby claims on the writer's shutdown | a writer set, any layout | 908 ms | 573 ms |
| A warm standby takes over on a takeover request | a writer set, with automation | 504 ms | 312 ms |

- **The restart's figure includes the process start alone.** A platform's
  own restart adds its time to it: scheduling, pulling the image, the
  startup probe. #164 measures that on real Container Apps.
- **A takeover's figure starts at the request.** The time the probe takes
  to declare the writer failed comes first: its period times its failure
  threshold, plus the automation's own latency, which #164 measures on
  Azure.
- **A crash is recovered by the platform's own restart**, in seconds, in
  either layout: the restarted instance reclaims at once. Handover
  automation is for what a restart does not recover: a replica that is
  gone, and one that runs but stays not ready. On Azure those are its two
  alerts (ADR 0181, amendment of 2026-10-28).
- **Restart is free.** The two-app layout's followers scale to zero, and
  it needs no automation. **A writer set costs** a warm standby per
  dataset that stays up, and on Azure N always-on apps and Front Door's
  base fee.

**With standbys, a rolling deploy moves the writer to a standby on each
restart** (ADR 0180 point 4). The restarting holder shuts down, releasing
its epoch with `shutdown`. A warm standby wins the race to claim it, and the
restarted instance comes back as a standby. So after a rolling deploy, the
writer is whichever member won the last race, not `placedOn`. Nothing moves
it back: `placedOn` is the preferred writer, claimed at a cold start, and
changing it is how you move the writer on purpose. `varve lease` shows each
move as an `abdication` claim following a `shutdown` release.

## Active-active by dataset

One fleet on one store can write in two places at once, **by dataset,
never two writers on one dataset** (#97 point 9, ADR 0180). Give each
dataset the writer set of the region, or the group of instances, it lives
in:

```turtle
<urn:fleet:orders> varve:placedOn "r1a" ; varve:standbyOn "r1b" ;
    varve:automaticHandover true .
<urn:fleet:invoices> varve:placedOn "r2a" ; varve:standbyOn "r2b" ;
    varve:automaticHandover true .
```

- **Every instance serves every dataset.** It reads what it follows, and
  forwards a write to the dataset's writer, in whichever region that is
  (ADR 0168). A write to the other region's dataset costs the hop between
  regions.
- **A handover stays in its writer set.** Only a member claims, so a
  crash, a takeover or a rolling restart moves `orders` between `r1a` and
  `r1b`, and never to region 2.
- **Each epoch has one writer.** Every write appends under the epoch the
  writer record names, and a superseded writer appends nothing after the
  next epoch's fence (ADR 0136). So two instances never append to one
  dataset, whatever the regions do.
- **The system dataset has one writer set too**, for the whole fleet.
  Management writes from the other region are forwarded to it.

A property runs four processes on one store as two regions, over generated
schedules of writes through any instance, crashes, takeovers and rolling
restarts (`docs/testing.md`). After every step each dataset has one writer,
in its own region. At the end, every acknowledged write is present, each
write took exactly one position, and every claim in each dataset's epoch
history is its own region's. Active-active across copies of a store, with
shipping between them, is part 2's (#166).

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
