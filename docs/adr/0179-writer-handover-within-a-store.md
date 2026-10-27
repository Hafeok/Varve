# 0179 — Writer handover within a store: two triggers, an epoch claimed in the writer record, a log slot as the fence

## Status

**Accepted — filed unaccepted by Replication part 1 (#165), 2026-10-27**
(ADR 0066). Acceptance is the maintainer's act on the pull request.

- **Drafted** for Replication (#97) as "Writer handover within a store",
  amended by the maintainer on 2026-10-15 (the epoch in a writer record
  beside `log/MANIFEST`) and 2026-10-19 (the takeover through the management
  plane; RustFS for MinIO).
- **Decided** on #97's plan, approved on 2026-10-27, with these answers:
  - the writer set is `varve:placedOn` ∪ `varve:standbyOn` (ADR
    [0180](0180-warm-standbys-and-the-writer-set.md));
  - the system dataset hands over too;
  - abdication is for a storage error, and for a fault shown to be local to
    the instance, never for one that is deterministic over the log;
  - a claim in place is held equal to a re-open from the catalog by a
    property.

**Refines** the Replication decision of 2026-10-11, "promotion is an
operator's act". Within one store, a change of writer is a *handover* and
may be automatic. Promotion of a different copy stays an operator's act
(#97 part 2, #166). No accepted ADR records that sentence, so nothing is
superseded.

**Records** that #97's original deliverable, "writes refused with `409`
naming the primary", is superseded by forwarding (#97's amendment of
2026-10-19, ADR [0168](0168-write-routing-by-forwarding.md)). A follower
never refuses a write; it forwards it.

**Amends**, each by a dated block:
- [ADR 0136](0136-one-writer-by-an-epoch-and-a-log-slot-fence.md):
  - the triggers `abdication` and `probe`;
  - the release reasons `storage-error` and `cache-error`;
  - a write-open that takes a claim already made;
  - point 5 per dataset;
- [ADR 0168](0168-write-routing-by-forwarding.md):
  - the directory invalidated on an epoch seen while following;
  - no cached `NoWriter`;
  - a forward refused by a superseded or stepped-down writer answered as
    `writer-moved`.

**Meets** what ADR 0136 already guarantees: log slots by create-if-absent,
the epoch in every chunk header, the header chain a new writer verifies,
fence-slot tombstones kept for ever, and a writer record that can hold a
released epoch.

Records decision set
[`writer-handover-within-a-store`](../decisions/writer-handover-within-a-store.md).

## Context

Replication has three tiers:

1. followers on the same store;
2. a copy a region away, made by the store's own geo-replication;
3. log shipping to any other store or a directory.

Today any change of writer is an operator's act, or the orchestrator's
restart of the one writer. An operator's act is right whenever the new
writer would start from a different copy of the log, as in tiers 2 and 3.
That copy may lack a tail, so the change is a decision about data loss, and
it belongs to a person.

A tier-1 follower is a different case: it reads the same log objects as the
writer (ADR [0164](0164-the-tier-1-follower.md)). If it becomes the writer,
no data is lost and nothing can diverge. It continues the one log that
exists. Requiring an operator here adds recovery time and buys no safety.

**No consensus protocol.** Varve uses no Raft or other replica-level
consensus. The object store's conditional writes (create-if-absent,
replace-if-unchanged) are linearisable (ADR 0134), and they are the only
agreement Varve relies on.

**No decision from elapsed time.** A dead process emits nothing, so its
absence can be noticed only by something that waits. That waiting belongs to
the orchestrator's probe, outside Varve. The probe decides only that a
handover is *requested*, never whether one is safe.

## Decision

**1. Two acts, two rules.**
- A **handover** changes the writer of a dataset while the log stays the same
  objects on the same store. A handover may be automatic.
- A **promotion** makes a different copy of the log authoritative: a tier-2
  secondary, a tier-3 target, or a restored backup. It is always an
  operator's act (#166).

**2. The epoch is claimed in the writer record.** A candidate claims writership
by replacing the writer record, against the version that shows epoch *n*, with
epoch *n*+1 and its own holder (ADR 0136 point 2). Several candidates may race;
exactly one wins, and a loser does not retry.

**3. The fence is a log slot, not the writer record.** The writer record is
not on the commit path: a replace per commit would cost group commit a round
trip. After claiming epoch *n*+1, the new writer adopts and fences:
- it adopts every chunk that exists above what it already holds, in order,
  verifying the header chain;
- it creates the next slot by create-if-absent, as a fence chunk carrying
  epoch *n*+1;
- if that create fails, it adopts what is now in the slot and tries the next.

Once the new writer owns a slot, the old writer's next create fails, so it
can append nothing more. Commits the old writer made durable before the
fence are valid: they are adopted, not discarded. ADR 0136 point 3 proves the
old writer can attempt no slot but the fence slot.

**4. An acknowledgement follows durability.** A commit is acknowledged only
after the create that holds it succeeded (ADRs 0124 and 0136). With point 3,
no acknowledged commit is lost in a handover, and no position is written
twice.

**5. A superseded writer stops, and on a handover dataset rejoins as a
follower.**
- **It stops.** When a create fails because a higher epoch owns the slot, the
  writer marks itself superseded and refuses every later write. The batch in
  flight fails with `503 writer-superseded`.
- **A lost response is not new.** When the response to a create was lost, the
  outcome of that batch is unknown to its clients. They resolve it as they do
  today, by reading the commits resource for their request id. The handover
  adds nothing to this case.
- **What happens next depends on the dataset.**
  - On a dataset with automatic handover (ADR 0180), supersession is an
    expected event. The instance closes its write-open and re-opens as a
    follower of the same store, serving reads and forwarding writes. It is
    not eligible to claim again until it is warm (point 9).
  - On any other dataset, ADR 0136 point 5 stands: the instance stays
    read-only, reports `superseded` on `/health/ready`, which is then false,
    and never claims again by itself. That is the "two instances configured
    to write one dataset" signal.
- **An idle writer** learns of its supersession at its next create. Before
  that, a read that carries `Varve-Min-Position` above its head makes it read
  its writer record once. If the record shows a newer epoch, it is
  superseded now. The read is the event, so no timer is involved.

**6. A handover has exactly two triggers, both events.**

*Abdication.* The writer knows it cannot continue. It replaces the writer
record to release epoch *n*, recording the reason. It answers every request
still in flight with `503 writer-stepped-down`, a new problem type, and
every later direct request the same way until it has re-opened as a follower.
After that it forwards them. The error responses and the release are one
event seen from two sides. The reasons:
- **`shutdown`**: graceful shutdown (ADR 0136 point 6), as today.
- **`storage-error`**: a batch failed against the store, with every retry
  spent or the store's answer an error, and the record replace then
  succeeded. Today such a writer refuses writes until it is re-opened (ADR
  0124). With a standby, the dataset need not wait for the re-open.
- **`cache-error`**: a fault local to the instance. That is an I/O error from
  the instance's local cache directory (ADR 0139): its disk full, read-only
  or failing. It is known by where it was raised, the cache's own writes,
  never by its message. A standby has its own cache, so its claim does not
  meet the same fault.
- **`placement`**: ADR 0163 point 12 and ADR 0180's placement event, as
  today.

**Never on a fault that is deterministic over the log.** A projection or
validator fault on the commit path fails the commit with `500`, and the
writer stays. Such a fault is a function of the log, so every standby would
meet it at the same position, and handing over would only move it: a
handover storm with no timer to stop it.

**The test that tells the two apart.** `HandoverAbdicationTests`:
- **A projection fault**, injected as a function of the commit, fails the
  commit with `500` on the writer and again on a standby given the same
  commit. The epoch history is unchanged, so there was no handover.
- **A cache fault**, injected into the writer's cache directory alone,
  releases the epoch with `cache-error`. The standby then claims, and
  commits the same request.

If the release cannot be written, nothing is released. Readiness turns false
(the dataset `failed`), and the probe trigger applies.

*Probe failure.* The orchestrator's liveness probe declares the writer failed.
The automation observing it sends a **takeover request** to a standby:
- `PUT /manage/datasets/{d}/state` with `{"takeover":{"epoch":n}}`, under a
  management token holding the operator role, so it is an operator's act
  carried out by automation (ADR 0181);
- answered by **the instance that receives it**, authorised at that
  instance's applied management position. It is not forwarded to the system
  writer: it acts on the store, not on a declaration, and it must work when
  the system writer is the instance that failed;
- the standby claims *n*+1, with trigger `probe`, only if the writer record
  still shows epoch *n*;
- success is `200` with the new epoch.

A refusal is `409 takeover-refused`, a new problem type, with a `reason`:
- `handover-off`: the dataset does not declare `varve:automaticHandover true`;
- `not-in-writer-set`: this instance is neither its `placedOn` nor a
  `standbyOn`;
- `cold`: this instance is not warm (ADR 0180);
- `epoch-moved`: the record no longer shows *n*. The refusal carries the
  current epoch, so a late or repeated trigger is a visible no-op.

Varve does not judge liveness. **Nothing else triggers a handover**:
- `503 server-busy` and `429` are load signals, and never trigger one;
- a client's failure to reach the writer is not a trigger, because a
  partition between a client and the writer says nothing about the writer.

**7. No timers in Varve's decisions.**
- A standby notices a release by reading the writer record with a
  conditional GET on its follow interval (ADR 0180). The interval affects
  how soon a takeover happens, never whether one is safe. Store change
  notifications could later shorten it without changing any decision.
- A false probe failure, for example a writer stalled in a garbage collection,
  causes an unnecessary handover. It cannot cause divergence, because points
  2–4 hold regardless of clocks. The stalled writer finds out at its next
  create, as point 5 says.

So "no leases" stands with no exception.

**8. The claim happens in place.** A warm standby already holds the dataset:
its projection, its graph versions, its commit index and its cache.
- It claims, then opens the store for write with the claim it made, through
  `ObjectStorage`'s new write-open that takes a claim already made and is
  guarded by the epoch named.
- The new storage adopts the slots above the standby's head and fences.
- The follower `Dataset` applies what was adopted, as an advance does, swaps
  its storage for the write-open, and starts its sequencer and maintenance.

A re-open from the catalog would discard the warmth that point 9 exists for.
`ClaimInPlaceTests` holds the two equal: over generated histories with a claim
at every position, the dataset claimed in place and the dataset re-opened from
the catalog answer every read equally, as of every position after the claim
(canonical N-Quads compared).

**9. Only warm standbys take over automatically.**
- Automatic handover is opt-in per dataset (`varve:automaticHandover`, off by
  default), and only for a member of its writer set (ADR 0180).
- Only a **warm** standby qualifies (ADR 0180). A cold instance refuses a
  takeover with `409 takeover-refused`, `cold`.
- Without a warm standby, the orchestrator's restart of the one writer
  remains the recovery. Opening for write claims the next epoch (ADR 0136).
- Only writer-mode opens claim. Read-only opens never change the writer
  record.

**10. Flapping is bounded by events, not by time.**
- Every trigger names the epoch it is about, and a claim succeeds only
  against that epoch. A late or repeated trigger for an old epoch is a no-op.
- A superseded or abdicated instance is eligible again only once it is warm.
- So each handover needs a fresh failure of the current writer.
- A `storage-error` abdication needs a failed batch, which needs a write.
  So a store that fails every writer alike hands over at most once per failed
  write, and an idle dataset never does.
- A restart claim names no epoch, so it is bounded differently. The start
  rule of ADR 0180 lets a starting member claim only a released or empty
  record, one it holds itself, or one held by a non-member. A returning
  writer never takes the epoch from a healthy successor.

**11. Every handover is visible.**
- Each chunk carries its epoch (ADR 0135).
- The writer record's epoch history records each claim:
  - the epoch, the holder and the address;
  - the trigger: `restart`, `operator`, `abdication` or `probe`;
  - the fence slot, and the release reason.
- The record stays `format: 1`, since its members are read by name.
- `GET /manage/datasets/{d}/state` reports `role` (`writer`, `standby` or
  `follower`), `epoch`, `warm` and the epoch history.
- No commit is written to mark a handover. The log stays the user's data
  plus commit metadata.

**12. Forwarding across a handover.** ADR 0168 rules the forwarder; this point
adds what a handover needs.
- **The directory learns the new writer by events.** It re-reads a record
  on `writer-moved`, on a failed connect and on any `503` from the writer, as
  today. It now also re-reads one when a follower's advance adopts a chunk of
  an epoch above the route's. `ObjectStorage.AdoptedEpoch` reports the
  highest epoch in a chunk header the follower adopted. It is the backend's,
  as the epoch is (ADR 0135), so `ITailingStorage` is unchanged.
- **`NoWriter` is not cached.** Each write while there is no writer reads the
  record once. The request is the event.
- **A forward refused across the handover was not committed.** A writer
  answers `writer-superseded` or `writer-stepped-down` only when the create
  that would hold the batch failed or never ran. The forwarder answers such a
  forward with `503 writer-moved`, with the new epoch when it knows it and
  the original problem type as the member `reason`, and marks the route
  stale. Under the hop rule the body is never sent again, so the client
  retries.
- **A forward whose outcome is unknown** is today's case, unchanged:
  `503 unavailable`, naming the writer's request id when it returned one. The
  commits resource resolves it.

**13. The system dataset hands over too.**
- `Varve:Fleet:Writer` names one instance, or with
  `Varve:Fleet:AutomaticHandover` set to `true`, the system dataset's writer
  set: its preferred writer first, then its standbys.
- The writer set, the warm rule, the start rule and the takeover route are
  the same, with `{d}` = `_fleet`.
- A data takeover is authorised at the standby's applied management
  position, so it never needs the system writer.

**14. The file backend** keeps its OS-level lock (ADR 0075). A standby process
on the same machine takes over when the lock is released; process death is
the event. This ADR does not extend the file backend across machines.

## Alternatives considered

- **Operator-only, as today.** Rejected for tier 1. It adds operator latency to
  every machine loss and gains no safety, since the store already guarantees
  a single writer.
- **Leases.** Rejected by the standing constraint. Correctness would rest on
  bounded clock drift and pause times, which fencing avoids.
- **A heartbeat object watched by standbys.** Rejected. It puts a timeout
  inside Varve's own decision, and it duplicates the orchestrator's probe.
- **Any 5xx from the writer as a trigger.** Rejected. Load shedding (`503
  server-busy`) would cause handovers under load, and a handover under load
  moves the load and drops the warm projection.
- **Abdication on a projection or validator fault.** Rejected by the
  maintainer. Such a fault is deterministic over the log, so every standby
  would fail the same commit, and the handovers would never end.
- **Raft, or another quorum among replicas.** Rejected. It would be a second
  agreement protocol under one the store already provides.
  - RPO 0 within a store comes from the store's synchronous replication (ZRS,
    or an erasure-coded store), not from follower acknowledgements.
  - Across regions, Raft could give RPO 0 only by paying a cross-region round
    trip on every commit.
- **The writer record as the fence, replaced on every commit.** Rejected:
  simpler, but a second round trip on every batch.
- **The takeover forwarded to the system writer**, as other management `PUT`s
  are. Rejected: it acts on the dataset's store, and the system writer may be
  the instance that failed.
- **A standby that re-opens from the catalog to claim.** Rejected as the
  default by point 8. It is the oracle of `ClaimInPlaceTests`.

## Consequences

- **New problem types:** `writer-stepped-down` (503) and `takeover-refused`
  (409, with `reason` and `epoch`). `writer-moved` gains the member `reason`.
- **`Varve.Store.ObjectStorage`:**
  - `ClaimTrigger` gains `Abdication` and `Probe`;
  - `ReleaseReason` gains `StorageError` and `CacheError`;
  - a write-open takes a claim already made, guarded by the epoch named.
- **`Varve.Store.ObjectStorage`** also reports `ObjectStorage.AdoptedEpoch`,
  the highest epoch a follower adopted.
- **`Varve.Store`:** a follower `Dataset` can take a write-open in place.
- **The gate (#97, #165)** adds `HandoverScheduleTests`, over generated
  schedules of:
  - abdications;
  - probe-triggered takeovers, including false ones against a stalled writer;
  - partitions, as delayed and lost responses;
  - concurrent candidates;
  - late and duplicated triggers;
  - `server-busy` at the writer;
  - forwarded writes.

  Each schedule shows that:
  - no acknowledged commit is lost;
  - positions are contiguous, and each is written once;
  - the header chain verifies across epochs;
  - the superseded writer appends nothing after the fence;
  - no trigger for an old epoch changes the writer;
  - `server-busy` never causes a handover;
  - a write forwarded across a handover commits exactly once, or fails with
    `writer-moved` and no commit.

  `ClaimInPlaceTests` and `HandoverAbdicationTests` are as stated above. The
  fleet job measures takeover time on Azurite and RustFS for both triggers.
- **Deployment and automation** are ADR 0181's.
- **What changes with it:**
  - the roadmap's Replication text: "promotion across copies is an
    operator's act; writer handover within a store may be automatic,
    triggered by abdication or probe failure";
  - the problem catalogue;
  - the operator guide: standbys, the takeover automation, and reading the
    epoch history;
  - #97's scope, through #165 and #166.

## Checks

- **Checked against the accepted ADRs** (0001–0178). It touches:
  - **0075** (the file backend unchanged);
  - **0113** (readiness);
  - **0119** (problem types);
  - **0124** (a failed batch now may abdicate);
  - **0134** (conditional writes the only agreement);
  - **0135** (the epoch in the chunk header);
  - **0136** (amended, as listed);
  - **0139** (the cache's fault is the instance's);
  - **0163** (the takeover route, and point 12's placement release);
  - **0164** (the follower turned writer);
  - **0168** (amended, as listed).

  No conflict.
- **Layer ownership.**
  - `Varve.Store` (4): the in-place write.
  - `Varve.Store.ObjectStorage` (5): the claim and the release reasons.
  - `Varve.Protocol` (5): the problem types and the forwarder's answer.
  - `Varve.Server` (6): the triggers, the route, and when to claim.
- **Analyzer rule.** None.
- **Open questions owned.** None.
