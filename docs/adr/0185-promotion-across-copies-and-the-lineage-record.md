# 0185 — Promotion across copies: an operator's act, a lineage record, and a management position that belongs to its fleet

## Status

**Accepted — filed unaccepted by Replication part 2 (#166), 2026-11-18**
(ADR 0066). Acceptance is the maintainer's act on the pull request. It
follows the plan approved on #97 on 2026-10-27, section 6, and the
maintainer's answers to its delta plan, recorded on #166 on 2026-11-16.

**Amends** by dated amendment:
- [ADR 0136](0136-one-writer-by-an-epoch-and-a-log-slot-fence.md): the
  trigger and the release reason `promotion`; an open for write refused on
  a replica; a record created above every shipped epoch;
- [ADR 0163](0163-the-management-plane.md): a promotion is answered
  whatever the hold-off; a replica is not a store failure; a replica's
  members follow;
- [ADR 0165](0165-the-management-position-on-the-commit.md): a recorded
  management position belongs to the fleet that wrote the commit;
- [ADR 0167](0167-event-declarations-move-to-the-management-plane.md):
  the events of a commit another fleet wrote;
- [ADR 0168](0168-write-routing-by-forwarding.md): a write to a replica;
- [ADR 0216](0216-the-format-inventory.md): the lineage record.

Records decision set [`promotion-across-copies`](../decisions/promotion-across-copies.md).

## Context

[ADR 0184](0184-log-shipping.md) keeps a byte-identical copy of a dataset
in another store. A copy answers the loss of a store only if one of the
copies can become the dataset. Three things stand in the way.

- **A copy can be claimed by accident.** The target's fleet declares the
  dataset, and part 1's start rule (ADR 0180) has a placed member claim a
  dataset whose record is empty. A replica's record is empty.
- **The shipped objects carry the source's epochs.** Every chunk's header
  names the epoch that wrote it, and the catalog's wrapper the epoch that
  fenced it. A writer that claims below either is marked superseded at its
  first open (ADR 0136; `ObjectSegmentStore.FenceAsync`,
  `ObjectDerivedStore.FenceCatalogAsync`).
- **A commit records a position in another fleet's system dataset.** Each
  commit carries the management position it was authorised under, as a
  bare integer (ADR 0165). A copy on another store is served by another
  fleet, whose system dataset has positions of its own. On the target
  today:
  - the write watermark is seeded from recorded positions, so writes after
    a promotion would wait for a position that means nothing there;
  - moved event declarations (ADR 0167) are resolved at the recorded
    position in the serving fleet's system dataset, which gives the wrong
    catalogue or none;
  - an audit read would answer with the wrong fleet's grants.

  The approved plan did not see this one. It was found by the delta plan.

## Decision

**1. Promotion is always an operator's act.** Nothing in Varve promotes a
replica on its own: no timer, no probe, no standby race.

**2. The lineage record says what a copy is.** The object `lineage`,
beside `writer`, format 1.
- **`state`:** `replica` or `promoted`.
- **`source`:** the source's store address, prefix and dataset id, and the
  id of the source's fleet.
- **`promoted`,** once it is: the epoch, the last position shipped with
  its header hash, the fence slot, when, and by whom.
- The shipper creates it as `replica` by create-if-absent, before the
  first object.
- **It is read conservatively** (ADR 0217). A format this build does not
  read is never written on, and the dataset is treated as a replica. An
  unknown state is a replica.

**3. A replica is never opened for write.** `409 dataset-is-replica`.
- A member of the target fleet placed on a replica reads the lineage
  before it decides, and **follows**. The start rule never runs while the
  lineage says `replica`.
- **That is not a store failure.** The member does not enter the claim
  hold-off (ADR 0163), and logs nothing as a failure.
- **A write sent to a replica** finds no writer and is answered
  `409 dataset-is-replica`, where an ordinary dataset answers `503`: a
  retry cannot help.
- **On the data plane the problem names only the dataset the caller
  sent,** and is decided after authorisation. A caller with no grant on
  the dataset gets what it gets for an absent one (ADR 0242). The source's
  address is never on the data plane.

**4. Promotion is a request to an instance of the target's fleet,** as a
takeover is (ADR 0179).
- **The request:** `PUT /manage/datasets/{d}/state` with
  `{"promote":{"source":"<address>","position":p}}` and the operator role.
  It is answered by the instance that receives it and never forwarded. It
  acts on the store and not on a declaration, so `If-Match` does not apply
  to it.
- **The instance** must be in the dataset's writer set and following the
  replica. It then:
  1. replaces the lineage with `promoted`, recording the tail it verified;
  2. claims in place with trigger `promotion`, at **one above the highest
     of the record's epoch, the highest epoch in any adopted chunk, and
     the epoch in the catalog's wrapper**;
  3. fences, and becomes the writer, with its projection warm.
- **`source` and `position` are what the operator expects,** and the
  request is refused when the lineage or the verified tail says otherwise.
  The operator states how much they mean to keep.
- **Refusals** are `409 takeover-refused` with a reason:
  `not-in-writer-set`, `cold`, `not-a-replica`, `source-mismatch`,
  `position-mismatch`.
- **`varve promote`** is that route's client.
- **It is answered whatever the hold-off,** as a takeover is.

**5. Where no fleet serves the target,** `varve promote --embedded
<target>` acts on the store itself, and then **releases with the reason
`promotion`**. The target's placed member then starts by the start rule's
"the record is released", and takes one epoch more.

**6. A promotion that fails part way is completed only by promoting
again.**
- A promotion that wins the record and then fails releases with
  `promotion`, never `storage-error`. A `storage-error` release starts a
  standby race, and a standby would then finish a promotion by itself.
- A lineage of `promoted` with no fenced `promotion` claim refuses a
  write-open with `409 dataset-is-replica`, reason
  `promotion-incomplete`.
- Promoting again is idempotent on the same lineage.

**7. The returning primary is refused twice.**
- Its shipper reads the lineage every round and stops in the state
  `replica-promoted`, which readiness reports with the promoted position.
  It is a state and not a problem type: the shipper answers no request
  (ADR 0184 decision 9).
- If it raced the promotion, its create at the fence slot finds other
  bytes of another epoch, and it stops with `shipping-diverged`.
- A promoted copy takes no write from its old source: the source has no
  path to the target but the shipper. Making the old primary a replica of
  the new one needs a fresh prefix.

**8. A recorded management position belongs to the fleet that wrote the
commit.**
- **Which positions are foreign.** Every commit of a replica, and every
  commit at or below a promoted copy's promoted position, was written
  under the fleet the lineage names.
- **Authorisation never reads a foreign position.** The watermark, the
  write path and what a commit is checked against use only positions above
  the promoted one.
- **The source fleet's system dataset is shipped too** (the maintainer's
  answer D3), as one more shipping target, to
  `fleets/<source-fleet-id>/` on the target's store. It is followed
  read-only.
  - **It is never consulted for authorisation.** A grant present only in
    it grants nothing on the target.
  - **It is shipped ahead of the dataset commits that refer to it** (ADR
    0184 decision 13), so a follower of the replica never meets a foreign
    position it cannot resolve.
  - Events and audit of a foreign commit are answered from it.
- **Restoring a lost fleet's declarations from that copy is not part of
  this** (#315).

**9. What a key outlives.** A client's idempotency key (#132) is kept in
the commit, so it is exactly as durable as its commit. If a promotion
loses an acknowledged commit that had not been shipped, a retry with its
key commits once on the promoted copy. Promotion promises no more.

**10. A dataset is declared a copy in the fleet that serves it** (the
maintainer's ruling of 2026-11-20 on #166).

```turtle
n:orders a varve:Dataset ; varve:datasetName "orders" ;
    varve:storage varve:ObjectStorage ;
    varve:copyOf n:primary .

n:primary a varve:ShippingSource ;
    varve:store "https://acct.blob.core.windows.net/primary" ;
    varve:prefix "orders" .
```

- **A fleet never creates a dataset under such a declaration.** Only the
  source's shipper makes a copy.
- **Until the first shipping round has written the lineage,** the member
  placed on it opens nothing and writes nothing. Readiness gives the
  reason: it waits for the first shipping round.
- **After it, the member follows,** as decision 3 has it.
- **Both sides may be declared in one apply.** A fleet may ship a dataset
  to another prefix of its own store and serve the copy. The order in which
  the two are declared does not matter, since neither waits on a
  declaration: the copy waits on the store.
- **A store that holds a dataset with no lineage under that name** is not a
  copy, and is not written. Readiness says so, and that a retry cannot
  help.
- **A dataset an instance already writes stays its own** when it is then
  declared a copy. A copy is found before a claim, never under one.
- **A copy is not shipped on.** `varve:copyOf` and `varve:shipsTo` on one
  dataset are refused: a replica has no writer for a shipper to run on.
  After a promotion the operator removes `varve:copyOf` before declaring a
  target.
- Without the declaration decision 3 still holds once a lineage is there.
  The declaration closes the time before it is.

**11. A replica is followed as a standby by the instance that would write
it,** whether or not the dataset declares automatic handover.
- It tails on the interval, so its projection is warm when an operator
  promotes. Decision 4's "following the replica" means this.
- An instance that would not write the dataset follows it as an ordinary
  follower, on demand.
- "Would write it" is: in the writer set where the dataset has one, and
  the placed instance where it has none.

**12. A promotion that loses to another claim is refused as
`epoch-moved`.** Decision 4 lists five reasons. A sixth is needed for a
promotion that finds the lineage or the writer record changed between its
read and its write: another promotion, or another claim, came first.
- It is the takeover's reason of the same name, with the same meaning:
  the request is a no-op, and the dataset's state says who holds it now.
- Nothing is retried. The operator reads the state and promotes again only
  if the dataset is still a replica.

**13. A promotion that did not finish has a name:
`promotion-incomplete`.** It is what decision 6's refused write-open is
called everywhere it shows:
- the reason of `409 dataset-is-replica` for a write sent to such a copy;
- the state a member reads before it claims, and follows on;
- the state the promoting member is left in when its promotion fails after
  it won the record.

It is distinct from `replica` because what an operator does differs:
promote again, with the same source and position. Promoting again keeps
the first promotion's lineage as it was written.

## Alternatives considered

- **The command line claims on the target directly,** as the approved plan
  had it. In a fleet the start rule then sees a holder outside the writer
  set and claims again: two epochs for one promotion, and the warm
  projection thrown away.
- **Promote automatically when the source is unreachable.** Unreachable
  from where, and for how long, are judgements Varve does not make (ADR
  0179). A wrong one forks the dataset across two stores, where no fence
  can refuse it.
- **No lineage record: an empty writer record means a replica.** An empty
  record also means a new dataset, and the start rule claims those.
- **Claim one above the highest chunk epoch only.** The catalog's wrapper
  can name a later epoch than any chunk the target holds, when the source
  was fenced and had not yet written.
- **Translate recorded positions into the target fleet's.** There is no
  mapping: the two system datasets have unrelated histories.
- **Answer every event of a foreign commit `503`,** and ship no `_fleet`.
  Simpler, and a region's loss then loses its declarations with it
  (declined as D3's option (b)).
- **Use the shipped `_fleet` for grants as well.** A copy would then be
  governed by a fleet that may be gone, and nobody on the target could
  change who reads it.
- **Release a failed promotion as `storage-error`.** Covered in decision
  6.

## Consequences

- **Tier 3's RTO is an operator's decision plus one claim.** The promoted
  instance is warm.
- **A target fleet with a replica runs no writer for it,** and its members
  cost what followers cost.
- **A promotion is visible** in the lineage, in the writer record's
  history as trigger `promotion`, and in the dataset's `…/state`.
- **Events of commits from before a promotion need the shipped `_fleet`.**
  Where it is missing, such an event is a problem that says so.
- **The protection of a promoted copy is its new fleet's.** Its grants and
  bindings are those declared there (ADR 0184 decision 14).

## Checks

- **Checked against the accepted ADRs.** Touches:
  - **0136, 0163, 0165, 0167, 0168, 0216**: amended.
  - **0179, 0180, 0181**: a promotion is not a handover. The takeover
    route's shape and its refusal type are reused; the start rule is
    unchanged and does not run for a replica.
  - **0217**: the lineage record follows it (point 4 foresaw it).
  - **0242**: what the data plane names.
  - **0244**: a promoted instance's first commits may wait for a flush and
    are never refused.

  No conflict.
- **Layer ownership.** The lineage record and the claim are
  `Varve.Store.ObjectStorage`, layer 5. The route is `Varve.Protocol`, its
  client `Varve.Protocol.Client`. The role is `Varve.Server`.
- **Analyzer rule.** None.
- **Tests.**
  - **The rehearsal:** promote, write, and the old primary's shipper
    refused by `replica-promoted`, and by `shipping-diverged` when it
    raced.
  - A placed member of a target fleet follows a replica, never claims it,
    and never holds off.
  - A promotion stopped at each step is completed by promoting again, and
    by nothing else.
  - A claim below the catalog's wrapper epoch is never made.
  - A grant present only in the shipped `_fleet` grants nothing.
  - A write after a promotion is authorised at the target fleet's
    position, and an event of a commit from before it renders from the
    shipped `_fleet`, byte for byte as the source rendered it.
  - A cold first open after a promotion stays within the memtable's
    bound.
- **Open questions owned.** None. Restoring a lost fleet is #315, and a
  check that two fleets' declarations agree is #314.
