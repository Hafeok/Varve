# 0180 — Warm standbys and the writer set: `placedOn` ∪ `standbyOn`, placement by events, and what warm means

## Status

**Accepted — filed unaccepted by Replication part 1 (#165), 2026-10-27**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on #97's plan, 2026-10-27:
- **the writer set** is a separate `varve:standbyOn`. `varve:placedOn` keeps
  `maxCount 1` and means the preferred writer;
- **claims:**
  - a fresh dataset, and one released while no member is warm (at a start),
    is claimed by `placedOn` alone; otherwise the release's reason decides
    (point 4, settled below);
  - a change of `placedOn` is a placement event, and only the new `placedOn`
    claims;
  - adding or removing a `standbyOn` is an event of its own;
  - after a takeover the holder may differ from `placedOn`, and nothing
    reacts to that state;
- **warm** means the default projection has caught up with what was applied,
  with the other conditions of point 6. Validation projections are not part
  of it, and their lag is reported beside it.

**Settled on #165, 2026-10-27:** who claims after a release is decided by its
reason (point 4). A rolling deploy moves the writer to a standby on each
restart, and the operator guide says so.

**Adds to** [ADR 0164](0164-the-tier-1-follower.md): its ruling
`FollowOnDemand` stands for a follower, and this ADR's
`StandbysFollowOnTheInterval` rules for a standby.

**Amends**, each by a dated block:
- [ADR 0163](0163-the-management-plane.md) point 12 (the start rule and
  placement events, for a dataset with a writer set) and point 13 (readiness);
- [ADR 0166](0166-varve-management-and-the-vocabulary.md): the term
  `varve:standbyOn` and its shapes.

Records decision set
[`warm-standbys-and-the-writer-set`](../decisions/warm-standbys-and-the-writer-set.md).

**Amended 2026-11-10:** a release for `storage-error` or `local-disk-error`
starts the releaser's hold-off (point 4; see the end, "a release for a
storage or local-disk error starts the releaser's hold-off";
[#216](https://github.com/Hafeok/Varve/issues/216)).
`ClaimAfterAReleaseByItsReason` is restated.

## Context

ADR [0179](0179-writer-handover-within-a-store.md) lets a warm standby take a
dataset's epoch over. It needs three things stated that the management plane
does not yet say:
- **which instances may write a dataset at all**;
- **how a placement change is told apart from a takeover**;
- **what warm is.**

Today placement is compared as state: an instance holds the epoch if
`placedOn` names it (`Placement.RoleOf`). After a takeover the holder is a
standby, so the old rule would make it abdicate at once, back to the
instance `placedOn` names. That is exactly the flapping ADR 0179 point 10
bounds.

A follower advances only when a read arrives (ADR 0164). A standby that waits
for reads may be arbitrarily far behind when the probe fails, and it would
never see a release.

## Decision

**1. The writer set.**
- The dataset's **preferred writer** is `varve:placedOn`: one literal instance
  name, at most once, or the system writer's instance when absent (ADR 0163
  point 12, unchanged).
- Its **standbys** are `varve:standbyOn`: literal instance names, any number.
- The **writer set** is the preferred writer and the standbys. Only a member
  ever claims the dataset.
- **The shapes** (`shapes/management.ttl`, ADR 0175):
  - `standbyOn` is an `xsd:string`;
  - a dataset with a `standbyOn` declares `varve:automaticHandover true`;
  - `standbyOn` and `placedOn` are disjoint (`sh:disjoint`), because an
    instance is the preferred writer or a standby, not both.

  A declaration that breaks one is `declaration-invalid`, as for any shape.
- **Without automatic handover there is no standby.** Every rule of ADR 0163
  point 12 stands unchanged, so a dataset without `varve:automaticHandover
  true` behaves as before this ADR.

**2. Placement is a sequence of events.**
- An instance compares the dataset's declaration at each management position
  it applies with the one at the position it applied before.
- What it acts on is a change, never a state:
  - **`placedOn` changed.** The current holder, whoever it is, abdicates with
    the reason `placement` once its applied position shows the change. Only
    the new `placedOn` claims, after it observes that release (ADR 0163
    point 12).
  - **The holder was removed from `standbyOn`.** It abdicates with
    `placement`, and the preferred writer claims.
  - **A standby was added or removed.** An added member starts tailing (point
    5). A removed one that does not hold the epoch becomes an ordinary
    follower. Nothing is claimed.
- After a takeover, the holder may differ from `placedOn`, and nothing reacts
  to that state.

**3. The start rule, for a dataset with a writer set.** ADR 0163 point 12's
"at start, an instance claims what is placed on it whatever the record says"
is narrowed. At start, a member claims only when one of these holds:
- **the record is empty, or released, and the instance is the preferred
  writer.** There is no race;
- **the record is held by this instance.** It is a restart after this
  instance's own crash, so the orchestrator's restart stays the recovery;
- **the record is held by an instance that is not a member.** Placement
  changed while this instance was down.

Otherwise it starts as a standby. So a returning writer never takes the epoch
from a healthy successor, and a run of `restart` claims in the epoch history
still means a deployment error (ADR 0136 point 7).

**4. Who claims after a release is settled by the release's reason.**
- **`placement`:** only the new `placedOn` claims (point 2).
- **`shutdown`, `storage-error` or `local-disk-error`** (ADR 0179 point 6):
  every warm member races.
  - That is the standbys, and the preferred writer when it is a standby.
  - The instance that released is not warm, so it does not race.
  - The store's replace-if-unchanged picks exactly one (ADR 0179 point 2).
- **A returning `placedOn`** starts as a standby under the start rule (point
  3). It claims again only by a release it wins, a takeover, or a placement
  event.
- **A rolling deploy moves the writer to a standby on each restart.** The
  restarting holder shuts down, releasing with `shutdown`. A warm standby
  wins the race, and the restarted instance comes back as a standby. The
  operator guide says so: over a rolling deploy the writer ends on whichever
  member won the last race, not on `placedOn`. That is a state nothing reacts
  to (Status).

**5. Standbys follow on the interval.** ADR 0164's `FollowOnDemand` stands
for a follower; this point adds the standby.
- **A follower** advances when a read arrives after
  `Varve:Fleet:FollowInterval` since its last advance, or on a minimum-position
  header above its head.
- **A standby, and the system dataset,** advance on the interval. A standby
  also reads the writer record on the same interval, which is how it observes
  a release (ADR 0179 point 7). The read is whole, a few hundred bytes, and
  answers `200` each time, not `304`. `IObjectStore` keeps its five
  operations (ADR 0134), none of them a conditional read, and a record this
  small is not worth a sixth. Decided by the maintainer on #165, 2026-10-28.
- **The cost**, stated in the operator guide: an idle standby dataset is three
  GETs per interval, the writer record and the two probes of ADR 0164 point
  3. An idle dataset with no standby still makes no requests.

**6. Warm.** A standby is warm when all of these hold:
- it is **tailing**: it follows on the interval (point 5);
- its **last advance succeeded**;
- its **default projection has caught up** with what it applied:
  `ProjectionPosition == Head`;
- it is **not re-opening**: neither the re-open after `MemtableLimit` (ADR
  0164 point 5) nor an open after supersession;
- it has **completed an advance since its last open.** A superseded or
  abdicated instance is eligible again only once it is warm (ADR 0179 point
  10).

Validation projections are not part of warm: commit-time gating does not read
them (ADR 0169), and ADR 0174 keeps their lag off readiness. Warm is not
"zero lag": the claim adopts whatever the standby has not applied (ADR 0179
point 3), so warm bounds that tail to about one interval's commits.

**7. Readiness and the state resource report it.**
- Each dataset entry on `/health/ready` carries:
  - `role`: `writer`, `standby`, `follower` or `elsewhere`;
  - `position`: the applied position;
  - `lag`: closed commits applied to the log but not yet to the default
    projection, as today, now named projection lag in the guide;
  - `tailing` and `warm`, for a standby;
  - `validation`: each binding's lag, as ADR 0174 gives it, beside `warm`.
- **A standby that is not warm is still ready** to serve reads. Warm decides
  only whether it may take a dataset over.
- **No "behind the writer" number.** A follower cannot know the writer's head
  without asking the writer, so readiness reports none. Read-your-writes is
  `Varve-Min-Position` (ADR 0168 point 8).
- `GET /manage/datasets/{d}/state` reports `role`, `epoch`, `warm`, the writer
  set and the epoch history (ADR 0179 point 11).

**8. A long-lived follower stays bounded.** ADR 0164 point 5, ruled and not
built until now, is built:
- a follower or standby that has applied more than
  `DatasetOptions.MemtableLimit` quads since its open is re-opened from the
  writer's newest catalog;
- reads already pinned keep the old dataset until they end;
- while it re-opens, a standby is not warm.

## Alternatives considered

- **`placedOn` repeated under `automaticHandover`.** Proposed on the plan.
  The maintainer chose a separate term: `placedOn` keeps one meaning, the
  preferred writer, and one cardinality.
- **Placement as state, as today.** The holder after a takeover would abdicate
  back at once.
- **Every member races for a fresh dataset.** Refused by the maintainer: the
  preferred writer claims a fresh one, with no race.
- **Only `placedOn` claims every release.** Refused by the maintainer on
  #165: the preferred writer's own shutdown would hand over to nobody, so a
  release for shutdown, storage-error or local-disk-error is raced for.
- **Standbys follow on demand, like any follower.** A standby would be as far
  behind as the time since the last read, and would never see a release.
- **Warm including the validation projections.** Refused by the maintainer:
  gating does not read them.

## Consequences

- **`Varve.Management`:** the term, its shapes, `DatasetDeclaration.StandbyOn`,
  and the reader.
- **`Varve.Server`:**
  - placement compares consecutive declarations;
  - the start rule;
  - standbys tail on the interval;
  - the warm flag, the readiness fields and the state resource;
  - the re-open after `MemtableLimit`.
- **`docs/vocabulary.md`** and the operator guide: standbys, their cost, warm,
  and reading readiness.
- **Tests:**
  - **the declaration shapes:** a `standbyOn` without automatic handover, and
    one equal to `placedOn`, are refused;
  - **placement events over generated declaration histories:** no instance
    reacts to a holder that differs from `placedOn`; a `placedOn` change
    moves the epoch to the new `placedOn` only;
  - **the start rule:** a returning writer starts as a standby beside a
    healthy successor;
  - **warm:** a standby is warm only after an advance with its projection
    caught up, and not while re-opening;
  - **a follower** re-opened after `MemtableLimit` keeps answering pinned
    reads.

## Checks

- **Checked against the accepted ADRs** (0001–0178). It touches:
  - **0113** (readiness fields; a standby not warm stays ready);
  - **0163** (amended, as listed);
  - **0164** (`FollowOnDemand` stands, and a standby's interval is added
    beside it; point 5 built);
  - **0166** (amended: a term);
  - **0169** and **0174** (validation projections are not part of warm);
  - **0175** (the shapes validate the new term).

  No other conflict.
- **Layer ownership.**
  - `Varve.Management` (4): the term and its shapes.
  - `Varve.Server` (6): placement events, the start rule, tailing, warm and
    readiness.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-11-10 — a release for a storage or local-disk error starts the releaser's hold-off

Ruled by the maintainer on 2026-11-10, in the session for
[#216](https://github.com/Hafeok/Varve/issues/216). The restated key is
unaccepted until the maintainer accepts it (ADR 0066).

**What point 4 said, and why it did not hold.** Point 4 has every warm member
race for a release for `shutdown`, `storage-error` or `local-disk-error`, and
adds: "The instance that released is not warm, so it does not race." For a
shutdown that is true, because the instance is gone. For the other two it is
true only for a while. The writer that releases for `storage-error` or
`local-disk-error` stands by in the same process (ADR 0179 point 6), and its
standby is warm after one advance. On
[#175](https://github.com/Hafeok/Varve/pull/175) it was warm in time: it
claimed the epoch it had just released, failed at the fence, and held the
record while the standby that could write lost the race (7453a21's account).
Since that fix a failed claim releases again, so the race settles, but only
after the releaser has claimed and failed.

**The rule.** A release for `storage-error` or `local-disk-error` starts the
releaser's hold-off for that dataset: the hold-off of
[ADR 0163](0163-the-management-plane.md)'s amendment of 2026-11-10
(`ClaimsHoldOffAfterAStoreFailure`), with its figures. The release counts as
a failure against the store.
- **Every warm member still races,** the releaser among them. It races once
  its hold-off has passed.
- **So another warm member wins at once.** The releaser sits out the round
  after its release whole, and then the rest of its wait. A member that
  reaches the store reads the release within one round and claims.
- **And the releaser, alone, takes the dataset back.** With every other
  member down or cold nothing else claims, and the releaser claims its own
  release two rounds after it, or later when it has failed before. A writer
  whose store failed once and recovered answers writes again with no
  restart.
- **While its store still fails,** its claim fails at the fence, releases
  what it won (ADR 0179 point 6) and lengthens the hold-off, to a claim
  every 15 to 30 seconds at the cap.
- **`shutdown` and `placement` are unchanged,** and so is the start rule
  (point 3). The system dataset's writer does not release for these two
  reasons, so nothing changes for it here.

**The ruling of 2026-11-02 that this replaces.** On #175 the maintainer first
ruled that the releaser does not race for its own release at all, and #216
was written so. Built that way, it left a writer set whose standbys were all
down or cold with no writer after one failed batch, until a takeover request
or a restart. The session reported that cost, and the maintainer replaced
the exclusion with the hold-off. The exclusion never landed.

**A consequence for `local-disk-error`.** A releaser whose disk still fails,
and which is alone, claims in place after its hold-off, and that claim
succeeds, because the store answers. A successful open as the writer resets
the hold-off. It then commits until its memtable passes the bound again and
releases again (ADR 0179 point 6). So alone, it cycles at the rate its
memtable fills, and the hold-off does not lengthen. With another warm
member, that member has the dataset after the first release.

**The ledger.** `ClaimAfterAReleaseByItsReason` is restated with the
hold-off, in this ADR's set (ADR 0068 point 4).

**Code and tests.**
- `ConvergeMemberAsync` starts the entry's hold-off where it releases for
  either reason. `MemberDecision` is unchanged: the hold-off gates the claim
  it decides, as it gates every claim.
- The fleet suite:
  - **b wins at once:** the storage-error abdication's epoch history holds
    one claim after the release, the other member's at epoch 2, and none by
    the releaser.
  - **The releaser alone:** a, with its standby not running, fails a batch
    and releases. The store recovers. a claims epoch 2 itself and answers
    writes, in the same process. With one-second rounds the claim comes more
    than 1.5 s after the release; without the hold-off it came after 1.2 s.

**Checked against the accepted ADRs.** ADR 0179 point 6 (the releases, their
reasons, and a failed claim's release) and point 8 (the claim in place) are
unchanged. ADR 0163's hold-off is applied as it stands. No other conflict.
