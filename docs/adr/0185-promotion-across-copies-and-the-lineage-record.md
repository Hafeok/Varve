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
- **`promoted`,** once it is: the epoch, the position the source's fleet
  wrote up to with its header hash, the fence slot, when, and by whom.
- **`earlier`: the chain.** A copy of a promoted copy carries, oldest
  first, every earlier source with that copy's `promoted`. So which fleet
  wrote which range of positions is read from the lineage alone:
  - positions up to the first link's promoted position were written by the
    first link's source fleet, the next range by the next link's, and so
    on;
  - positions up to this copy's own promoted position, or every other
    position while it is a replica, by `source`'s fleet;
  - positions above this copy's promoted position by the fleet that serves
    it.

  A shipper writes the chain when it creates the lineage: its source's own
  `earlier`, and then its source's `source` and `promoted` where the
  source is a promoted copy. A chain that is not in position order is a
  record this build does not understand. Format 1 has not shipped, so the
  chain is in format 1.
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
  1. replaces the lineage with `promoted`, recording the tail it verified
     and no fence;
  2. claims in place with trigger `promotion`, at **one above the highest
     of the record's epoch, the highest epoch in any adopted chunk, and
     the epoch in the catalog's wrapper**;
  3. fences, and becomes the writer, with its projection warm;
  4. **replaces the lineage once more, its last write:** the position and
     the header hash as they are at the fence, and the fence slot. A
     promotion is finished when its lineage names a fence, and not before.
- **A shipper that is mid-round** read the lineage as `replica` before
  step 1, and may create chunks after it. What happens to them is decided
  by the fence slot alone:
  - **Chunks it created before the fence are kept.** The fence adopts
    every chunk below its slot, as any claim in place does (ADR 0136). They
    are commits the source acknowledged, so the copy holds them and step 4
    records the position after them: one or more above what step 1
    recorded.
  - **They are the source fleet's.** Their management positions are read
    in the source fleet's system dataset, which was shipped ahead of them
    (ADR 0184 decision 13). Decision 8's boundary is the position step 4
    records.
  - **If the shipper's create lands on the fence slot first,** the
    promoter's fence takes the next slot, as a fence does when it loses a
    slot. The chunk is below the fence and is kept, as above.
  - **The shipper's next create, at or above the fence slot, finds other
    bytes** and it stops with `shipping-diverged`, having overwritten
    nothing. At its next round it reads the lineage and stops as
    `replica-promoted` (decision 7).
  - **A commit of the old source that was not on the target before the
    fence is in no copy.** Decision 9 says what a client's key does then.
- **A second promote request** is answered from the lineage:
  - a finished promotion answers `not-a-replica`, whichever position the
    request names;
  - a promotion that has not finished (decision 6) is completed by a
    request that names the position step 1 recorded, and a request that
    names another is refused `position-mismatch`, with the recorded one.
- **`source` and `position` are what the operator expects,** and the
  request is refused when the lineage or the verified tail says otherwise.
  The operator states how much they mean to keep.
- **Refusals** are `409 takeover-refused` with a reason:
  `not-in-writer-set`, `cold`, `not-a-replica`, `source-mismatch`,
  `position-mismatch`.
- **`varve promote`** is that route's client.
- **It is answered whatever the hold-off,** as a takeover is.

**5. Where no fleet serves the target,** `varve promote --embedded
<target>` acts on the store itself, through all four steps, and then
**releases as any writer that closes** (the maintainer's ruling of
2026-11-23).
- A finished promotion leaves nothing for the next writer to know: the
  lineage says `promoted` with its fence, and the claim is fenced.
- The target's placed member, or its writer set, then starts as after any
  ordinary release: by the start rule's "the record is released", taking
  one epoch more. A standby stands by, and takes over as it would from
  any writer.
- The reason `promotion` is only for a promotion that did not finish
  (decision 6).

**6. A promotion that fails part way is completed only by promoting
again.**
- **Only a promotion that failed part way releases with `promotion`,**
  never `storage-error` and never as an ordinary release. No standby races
  for a `promotion` release, so no standby finishes a promotion by itself.
  This holds for a member of a fleet and for `--embedded` alike.
- **Not finished is: the lineage says `promoted` and names no fence.**
  That is so from step 1 until step 4 of decision 4, wherever between them
  it stopped. Such a copy refuses a write-open with
  `409 dataset-is-replica`, reason `promotion-incomplete`.
- **Promoting again is idempotent on the same lineage.** It names the
  position step 1 recorded, claims above whatever epoch the first attempt
  reached, fences again, and writes step 4. Chunks a shipper created
  before the new fence are kept, as in decision 4.

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
  under a fleet the lineage names: the source's, or for a position in an
  earlier range of the chain (decision 2), that link's.
- **After two promotions** the answer is per position. Take a dataset
  written by fleet A, shipped to fleet B and promoted there at 40, written
  on by B, shipped to fleet C and promoted there at 90. In C: positions 1
  to 40 are A's, 41 to 90 are B's, and 91 on are C's own. The name the
  dataset had in each fleet is the prefix that link's source names.
- **Authorisation never reads a foreign position.** The watermark, the
  write path and what a commit is checked against use only positions above
  the promoted one.
- **The source fleet's system dataset is shipped too** (the maintainer's
  answer D3), as one more shipping target, to
  `fleets/<source-fleet-id>/` on the target's store. It is followed
  read-only.
  - **Every earlier fleet's goes onward with the copy.** A fleet that
    ships a promoted copy also ships, from `fleets/<id>/` on its own
    store to the same place on the target, the system dataset of each
    fleet in the copy's chain, before the commits that refer to it. The
    copy there says what the one it was read from says: a replica of that
    fleet's system dataset. What the old commits refer to was on the
    shipping fleet's store before the copy was promoted, so it is shipped
    until a round finds nothing new.
  - **Where a store holds no copy of an earlier fleet's system dataset,**
    a position of that fleet cannot be read there, and is answered as a
    position not yet applied. Nothing is refused for it.
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
- **A dataset written in this fleet is not declared a copy** (the
  maintainer's ruling of 2026-11-23).
  - **`varve apply` refuses it** where the answering instance can see it:
    its store holds a dataset under that name whose lineage does not name
    the declared source, or that has no lineage. The answer is
    `422 declaration-invalid` with one finding, at the dataset's graph and
    `varve:copyOf`. Nothing is committed.
  - **Where the apply cannot see it,** the store unread that moment, the
    declaration is applied. A running writer keeps writing: a copy is
    found before a claim, never under one. Readiness reports the
    contradiction at once, on the dataset, as
    `"declaration": "copy-declared-on-a-written-dataset"` with the
    reason, and the instance stays ready. It has
    [a page](../operator/shipping/copy-declared-on-a-written-dataset.md).
  - **At a start, with the dataset not open,** the member does not open
    it for write: the bullet above this one.
  - A copy that is not there yet, and a promoted copy whose lineage names
    the declared source, are not refused.
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
what the first promotion's lineage recorded until its own last write.

**14. What the promotion page tells the operator, where they promote**
(the architect's review, for pull request 3). The page for `varve promote`
says, at the step where the operator runs it and not in a note elsewhere:
- **the old primary keeps accepting writes.** Promotion acts on the copy
  only. Nothing tells the old primary, and nothing stops it;
- **its later commits are in no copy.** Every commit it acknowledges after
  the position promoted, and every one not yet shipped at that moment, is
  on the old primary alone;
- **there is no planned switchover.** Varve has no operation that stops
  writes on a primary, ships the rest and promotes. An operator who wants
  one stops the writers themselves, waits until readiness reports the
  position shipped, and promotes at that position.

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
  - A shipper that wins a slot before the fence has its commits kept and
    recorded as the source's; its next create stops it, and a second
    promote request is `not-a-replica`.
  - A copy of a promoted copy carries the chain; after two promotions each
    position names the fleet that wrote it, and that fleet's system
    dataset is on the store.
  - A copy promoted on the store is taken by a writer set as after any
    ordinary release.
  - An apply that declares a written dataset a copy is refused, and a
    writer that meets the declaration keeps writing while readiness says
    so.
  - A claim below the catalog's wrapper epoch is never made.
  - A grant present only in the shipped `_fleet` grants nothing.
  - A write after a promotion is authorised at the target fleet's
    position, and an event of a commit from before it renders from the
    shipped `_fleet`, byte for byte as the source rendered it.
  - A cold first open after a promotion stays within the memtable's
    bound.
- **Open questions owned.** None. Restoring a lost fleet is #315, and a
  check that two fleets' declarations agree is #314.
