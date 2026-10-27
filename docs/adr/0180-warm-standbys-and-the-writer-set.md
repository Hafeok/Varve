# 0180 — Warm standbys and the writer set: `placedOn` ∪ `standbyOn`, placement by events, and what warm means

## Status

**Accepted — filed unaccepted by Replication part 1 (#165), 2026-10-27**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on #97's plan, 2026-10-27:
- **the writer set** is a separate `varve:standbyOn`. `varve:placedOn` keeps
  `maxCount 1` and means the preferred writer;
- **claims:**
  - a fresh or released dataset is claimed by `placedOn` alone;
  - a change of `placedOn` is a placement event, and only the new `placedOn`
    claims;
  - adding or removing a `standbyOn` is an event of its own;
  - after a takeover the holder may differ from `placedOn`, and nothing
    reacts to that state;
- **warm** means the default projection has caught up with what was applied,
  with the other conditions of point 6. Validation projections are not part
  of it, and their lag is reported beside it.

**One question for the maintainer** is point 4's last bullet: who claims when
the preferred writer *itself* abdicates. The rule above, read alone, leaves
that release to `placedOn`, which is the instance that just released it.

**Supersedes in part** [ADR 0164](0164-the-tier-1-follower.md): its ruling
`FollowOnDemand` moves to this ADR's set under the same key, restated, so
that a standby follows on the interval.

**Amends**, each by a dated block:
- [ADR 0163](0163-the-management-plane.md) point 12 (the start rule and
  placement events, for a dataset with a writer set) and point 13 (readiness);
- [ADR 0166](0166-varve-management-and-the-vocabulary.md): the term
  `varve:standbyOn` and its shapes.

Records decision set
[`warm-standbys-and-the-writer-set`](../decisions/warm-standbys-and-the-writer-set.md).

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

**4. Who claims after a release.**
- **A release with the reason `placement`** is claimed by the instance the
  event names (point 2).
- **A release by a standby that held the epoch** (`shutdown`, `storage-error`
  or `cache-error`, ADR 0179 point 6) is claimed by the preferred writer
  alone.
- **A release by the preferred writer itself**, after a takeover took it back
  or on its own shutdown, is the open question:
  - read alone, the rule in Status leaves it to `placedOn`, the instance that
    just released it;
  - **proposed:** its warm standbys claim, racing, and the store's
    replace-if-unchanged decides, as ADR 0179 point 2 says;
  - without this, the preferred writer's abdication hands over to nobody
    until its restart or a probe-triggered takeover.

**5. Standbys follow on the interval.** This restates ADR 0164's
`FollowOnDemand`.
- **A follower** advances when a read arrives after
  `Varve:Fleet:FollowInterval` since its last advance, or on a minimum-position
  header above its head.
- **A standby, and the system dataset,** advance on the interval. A standby
  also reads the writer record by conditional GET on the same interval, which
  is how it observes a release (ADR 0179 point 7).
- **The cost**, stated in the operator guide: an idle standby dataset is three
  GETs per interval, the writer record answering `304` and the two probes of
  ADR 0164 point 3. An idle dataset with no standby still makes no requests.

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
- **Every member races for a fresh or released dataset.** Refused by the
  maintainer: the preferred writer claims, with no race.
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
  - **0164** (`FollowOnDemand` superseded in part, restated; point 5 built);
  - **0166** (amended: a term);
  - **0169** and **0174** (validation projections are not part of warm);
  - **0175** (the shapes validate the new term).

  No other conflict.
- **Layer ownership.**
  - `Varve.Management` (4): the term and its shapes.
  - `Varve.Server` (6): placement events, the start rule, tailing, warm and
    readiness.
- **Analyzer rule.** None.
- **Open questions owned.** Point 4's last bullet, for the maintainer on the
  pull request.
