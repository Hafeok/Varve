# 0186 — Tier 2: the store's own geo-replication, read at its secondary and promoted only by an operator

## Status

**Accepted — filed unaccepted by Replication part 2 (#166), 2026-11-26**
(ADR 0066). It follows the plan approved on #97 on 2026-10-27, section 7,
the delta plan accepted on #166 on 2026-11-16, and the maintainer's rulings
of 2026-11-26 on #166.

**Nothing in it is built.** By the maintainer's order this ADR and its
decision set are filed alone, for review, before any code.

**It will amend,** by dated amendments filed with the code that makes them
true:
- [ADR 0137](0137-storage-credentials-are-workload-identity.md): the
  secondary endpoint, and a custom role for two read-only calls;
- [ADR 0142](0142-deployment-on-container-apps-and-small-machines.md): the two-region layout;
- [ADR 0160](0160-extent-deletion-by-reader-announcements.md): a follower
  at the secondary reads unannounced;
- [ADR 0164](0164-the-tier-1-follower.md): a follower at the secondary;
- [ADR 0185](0185-promotion-across-copies-and-the-lineage-record.md): the
  lineage record's `promoted.failovers`, a record with no `source`, and
  what `varve promote` answers after a failover;
- [ADR 0136](0136-one-writer-by-an-epoch-and-a-log-slot-fence.md): what a
  claim records of the account's redundancy.

Records decision set [`tier-two-geo-replication`](../decisions/tier-two-geo-replication.md).

## Context

Tier 1 (ADRs 0164, 0179 to 0181) replaces a writer within one store. Tier
3 (ADRs 0184, 0185) keeps a copy in another store, which Varve writes.
Tier 2 is between them: **one storage account that the provider replicates
to a second region**, with a read-only endpoint there. Varve writes
nothing to the second region. Azure Blob Storage calls it RA-GRS and
RA-GZRS.

What the provider documents, and this ADR relies on:

- **Replication is asynchronous.** A write to the primary is copied to the
  secondary some time later. The account reports a **last sync time**:
  every write before it is readable at the secondary, and a write after it
  may or may not be.
- **Nothing orders the copies across objects.** A later object can be at
  the secondary before an earlier one.
- **An unplanned failover makes the secondary the primary, at the same
  addresses.** The account becomes locally redundant. What had not been
  copied is lost for good. Nothing tells a client that it happened.
- **A planned failover swaps the two regions and loses nothing.** The
  primary is read-only while the rest is copied, and the account stays
  geo-redundant.
- **The last sync time is read from the secondary endpoint,** by `Get Blob
  Service Stats`, with a status `live`, `bootstrap` or `unavailable`. The
  time can be empty, and can be a value cached for a few minutes.

Four things follow for Varve.

- **A reader at the secondary sees a log with holes that close.** A chunk
  can be there before the chunk it follows, and a catalog before a set it
  names.
- **After an unplanned failover the log can have a hole that never
  closes,** with chunks after it.
- **After an unplanned failover the log can also simply be shorter,** with
  no hole. An ordinary open for write would then take the shorter log and
  write on. Acknowledged commits would be gone and nothing would have said
  so. The approved plan did not see this case.
- **A writer that is still running holds slots the store has lost.** The
  fence of ADR 0136 stops a writer at its *next* slot. Here its next slot
  is past anything the store holds, so no fence is in its way.

## Decision

**1. Tier 2 is the store's own replication. Varve copies nothing.** A
dataset in tier 2 is one dataset in one account. There is no shipper, no
target and no second fleet. What Varve adds is a way to read at the
secondary, a way to say how far behind it is, and a way to make the account
the dataset again after a failover.

- **It is Azure Blob Storage only,** in `Varve.Store.Azure`. An
  S3-compatible store's cross-region replication is a different mechanism
  and is not in this ADR.
- **It composes with tier 3.** A dataset in a geo-replicated account can
  also be shipped (ADR 0184), and a promoted copy can sit in one.

**2. A follower reads at the secondary endpoint.** An instance configured
with `Varve:ObjectStorage:SecondaryRead` reaches the container at
`https://{account}-secondary.blob.core.windows.net/…`. It is ADR 0164's
follower with three differences.

- **It reads unannounced** (ADR 0160 point 10). The endpoint refuses every
  write, so it leaves no reader record. An extent can be deleted under it;
  it then attaches the catalog again, and a read that cannot be answered is
  `read-unannounced-extent-gone`.
- **It takes only what is whole and in order.**
  - Its probe of the log stops at the first slot that is absent, and
    adopts nothing after it, whatever is there. A hole closes when the
    chunk arrives.
  - A consolidated segment whose chunks are gone and whose `.seg` is not
    there yet is "not yet", and never damage.
  - It attaches a catalog only when every set it names can be read whole.
    Until then it keeps the one it has.
- **It never writes and never claims.** It is in no writer set. A member
  configured with `SecondaryRead` that is named in `placedOn` or
  `standbyOn` is refused at apply.

Writes that reach such an instance are forwarded to the writer (ADR 0168),
which is in the first region.

**3. Lag is the account's last sync time, as the account gives it.**

- A follower at the secondary reads `Get Blob Service Stats` there, and
  readiness reports, per instance and not per dataset, a member `geo` with
  `status` and `lastSyncTime` exactly as returned. `varve status` prints
  them.
- **Varve computes nothing from it.** There is no "seconds behind", and no
  clock of ours is compared with it. An empty time is reported as absent.
- **It is read when readiness is asked,** at most once per interval of the
  follower's own tail, and never on a timer of its own.
- **Nothing decides by it.** It is for the operator, who reads it before a
  failover to see what would be lost.

**4. A claim records whether the account was geo-redundant, and an open
for write refuses an account that no longer is.** This is the rule that
keeps a failover from promoting itself.

- **At every claim** an instance reads the account's redundancy by `Get
  Account Information`, a data-plane call, when its configuration says
  `Varve:ObjectStorage:GeoRedundant`, or when the writer record's newest
  claim says the account was. The claim records `redundancy: "geo"` or
  `"local"`.
- **Where the newest claim says `geo` and the account now says `local`,**
  the account has failed over, or an operator has changed it. An open for
  write, a takeover and a claim in place then refuse with
  `AccountFailedOverException`, before anything is written. Over HTTP a
  write is `409 account-failed-over`, and readiness says
  `account-failed-over` with what to run.
- **Only `varve promote --from-failover` clears it** (decision 5). Its
  claim records `local`. When the operator makes the account geo-redundant
  again, the next claim records `geo`.
- **A dataset whose claims never said `geo` is not asked.** No request is
  added to any other deployment.
- **A planned failover needs none of this.** The account stays
  geo-redundant and loses nothing. The writer's batches fail while the
  primary is read-only, it abdicates as for any store failure (ADR 0179),
  and a writer starts as after any restart.

**5. Promotion after a failover is `varve promote --from-failover`, and
only that.** Nothing promotes on a failover by itself: no probe, no timer,
no start of a process. It is the request of ADR 0185 decision 4 with
`"fromFailover": true` and no `source`, and the same with `--embedded`.

It is refused, `not-failed-over`, where decision 4 does not say the account
failed over and the log has no hole.

In order:

1. **It reads the whole log** and finds the longest prefix that verifies:
   every slot present, each chunk's hashes good, each chained to the one
   before. The **first gap** is the first slot that is absent, or does not
   verify, with anything after it.
2. **It says what it will discard, and asks.** The command prints the last
   position kept, the slots beyond the first gap, and the highest position
   those chunks hold where one can be read. It goes on only with `--yes`.
   The request carries the position kept, as every promotion does, and is
   refused `position-mismatch` if the log says otherwise.
3. **It replaces the lineage,** or creates it, with a new item at the end
   of `promoted.failovers` (decision 6): the position kept, its hash, and
   what will be discarded. No fence yet.
4. **It moves every object beyond the first gap to `discarded/{epoch}/`,**
   under the dataset's prefix, by a create there and then a delete. Nothing
   is deleted that was not first created there, and nothing is ever
   deleted from `discarded/`.
5. **It drops a catalog that is ahead of the log or names a set that is
   not whole.** The projection is rebuilt from the log (ADR 0213).
6. **It claims,** with trigger `promotion`, one above the highest of the
   writer record's epoch, the epoch of every chunk kept *and discarded*,
   and the catalog wrapper's. The record at the secondary can be older
   than its chunks.
7. **It fences,** as any writer does (ADR 0136, amended 2026-11-24).
8. **Its last write** records the fence in the item. Until then the
   promotion has not finished: the dataset is `promotion-incomplete`, and
   only promoting again, naming the same position, completes it (ADR 0185
   decision 6).

**The answer states what was lost, in-band.** Beside `epoch`, `position`
and `asked` it carries `discardedFrom`, the first position not kept, and
`discardedCommits`, the count from there to the highest position a
discarded chunk holds. Where no discarded chunk could be read to a
position, `discardedCommits` is absent and `discardedUnknown` is `true`.
**The count is a lower bound and the answer says so:** what was in the
hole itself was never at the secondary, and cannot be counted.

**6. The lineage record says what was discarded.** Format 1 has not been
in a release, and changes.

- **`promoted.failovers`** is an array, oldest first. Each item has
  `epoch`, `position`, `hash`, `fence`, `at`, `by`, and `discarded`:
  `from`, `through` where known, `slots`, and `under`, the prefix they
  were moved to.
- **A dataset that was never a copy gets a lineage here for the first
  time:** `state` is `promoted`, there is no `source`, and `promoted`
  holds `failovers` alone. `source` becomes optional. A record with no
  `source` names no other fleet: every position is the serving fleet's.
- **A dataset that is already a promoted copy keeps `source`, `earlier`
  and `promoted`'s own position,** which is the boundary between fleets
  (ADR 0185 decision 8). A failover does not move it.
- **An item with no `fence` has not finished,** as a `promoted` with none
  has not.

**7. A writer still running against the lost region is fenced by the
operator, not by the log.**

- **Why the log cannot.** That writer's next slot is past everything the
  store holds. Its create succeeds. What it writes is beyond a hole, or
  ahead of the promoted writer in the same segment.
- **So the page for a failover has, as its first step, to stop every
  instance of the first region, or to remove its identity's write role.**
  In an outage of the region they are stopped already. `--from-failover`
  asks the operator to confirm it, in the same question as the discard.
- **What happens if they do not.**
  - Before the promotion: the stray writer's commits are beyond the first
    gap, and step 4 discards them. The answer counts them.
  - After it: the promoted writer meets a slot it did not write, and stops
    as superseded. The next open finds a chunk that does not chain, and
    refuses. `varve promote --from-failover` again discards it.
  - In both, **commits that writer acknowledged are lost,** and the answer
    of the promotion that discards them says how many.
- **A writer of the first region that starts again is not a stray.** Its
  open reads the record and the log as they are, and it claims as any
  writer does.

**8. The layout is two regions behind Front Door.**
`deploy/azure/container-apps-geo/`:

- one RA-GRS account;
- the first region's fleet, which writes;
- the second region's instances, each a follower at the secondary;
- Front Door Standard, which sends reads to the nearer region and every
  write, by request method, to the first;
- a custom role with two actions and nothing else: the one `Get Blob
  Service Stats` needs, and the one `Get Account Information` needs. Each
  instance's identity holds it beside its data role.

After a failover and its promotion the operator deploys the template again
with the regions exchanged. `docs/azure-geo-setup.md` is the step-by-step.

**9. What tier 2 promises, said plainly.**

- **RPO is the writes after the account's last sync time** at the moment
  of the failover. The provider gives no bound for it.
- **A commit acknowledged in the first region can be lost,** and a client
  holding its key retries it on the promoted dataset, where it commits
  once (ADR 0185 decision 9).
- **There is a planned switchover, and it is the account's own:** the
  provider's planned failover. Varve adds no step to it and takes none
  away.

**10. Tier 2 is verified on a real account or not at all.**

- **In CI:** a geo double over two in-memory stores, with replication
  delivered out of order and a last-sync watermark. It runs the follower
  schedules and the failover-promotion schedules. Azurite's secondary
  endpoint is also read, which proves the address and the two calls and
  nothing about replication: its last sync time is the current time.
- **On a real account:** `azure-geo.yml`, restricted to `main`, by
  workload identity federation, with no key. The failover rehearsal is
  dispatch only.
- **Until that leg has run, the release notes and the guide say that it
  has not.** A double and an emulator do not verify tier 2.

## Alternatives considered

- **Let an ordinary open take the shorter log after a failover.** It is
  what the approved plan implied. Rejected: where the log has no hole, the
  loss of acknowledged commits would be silent, and a process start would
  have promoted the secondary. The maintainer's ruling is that promotion
  is only ever an operator's act.
- **Tell a failover from the last sync time, or from the secondary
  endpoint going away.** Rejected: it makes a restart in the first region
  depend on the second being reachable, and the time is cached and can be
  empty. The account's redundancy is one fact, read where the writes go.
- **Fence a still-running writer from the log,** by checking before each
  create that the chunk before it is still there. Rejected: it is a
  request per commit for every deployment. Checking on a timer is a timer
  deciding. It stays the operator's step.
- **Delete what lies beyond the first gap.** Rejected: they are bytes a
  writer acknowledged. They are moved and listed, and an operator can read
  them.
- **A record of its own for what was discarded.** Rejected by the
  maintainer on 2026-11-26: it is a member of the lineage's `promoted`.
- **A second fleet in the second region, with its own system dataset.**
  Rejected: that is tier 3. In tier 2 the system dataset is in the same
  account and is replicated with everything else.

## Consequences

- **Lineage format 1 changes before its first release:** `source` is
  optional and `promoted.failovers` is new. `docs/spec/storage-format.md`
  section 8.3 and the `lineage-next` fixture follow with the code.
- **The writer record's claim gains `redundancy`,** optional, under format
  1. A build before this ADR ignores it.
- **Two more problem types and one more readiness state,** each with a
  page: `account-failed-over`, `not-failed-over` as a reason of
  `takeover-refused`, and the readiness member `geo`.
- **`discarded/` is a new prefix under a dataset.** Nothing reads it and
  nothing deletes it. The guide says an operator may, once they have
  looked.
- **Two instances' identities need one more role.**
- **An operator who makes a geo-redundant account locally redundant on
  purpose** meets `account-failed-over` at the next claim, and runs
  `varve promote --from-failover`, which finds no hole and discards
  nothing.

## Checks

- **Not shown, and to be shown by the real-account leg:**
  - which role action each of the two calls needs. The plan names
    `Microsoft.Storage/storageAccounts/blobServices/read` for the stats; I
    have not confirmed either against an account;
  - that `Get Account Information` answers a workload identity with a data
    role, and reports the redundancy after a failover as the documentation
    says;
  - everything about real replication order and real lag.
- **Shown before this ADR:** Azurite 3.37.0 answers `Get Blob Service
  Stats` at its `-secondary` address with `live` and the current time.
- **To be built, each failing first:**
  - the follower schedules over the geo double: a hole that closes, a
    catalog ahead of its sets, a `.seg` that is not there yet, an extent
    deleted under the reader;
  - the failover-promotion schedules: a hole with chunks after it, a
    shorter log with no hole, a writer record older than its chunks, a
    catalog ahead of the log;
  - an open for write on a failed-over account is refused until promoted,
    and is not asked on an account whose claims never said `geo`;
  - the rehearsal: failover, promote, write, a stray writer's commits
    discarded and counted;
  - a promotion stopped at each step is completed by promoting again.
- **Open questions owned.** None by this ADR. Cross-region replication of
  an S3-compatible store is not planned.
