# 0164 — The tier-1 follower: a dataset that tails its writer's log on the same store

## Status

**Accepted — filed unaccepted by milestone Management plane of #105,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the plan of #105, 2026-10-19:
- a follower refreshes by probing the next slot, with no LIST polling;
- it follows on demand, except the system dataset, which it follows on an
  interval.

**Refines** ADR [0136](0136-one-writer-by-an-epoch-and-a-log-slot-fence.md)
point 5's "a follower attaching the writer's catalog": the follower
attaches the catalog, then keeps tailing the log.

## Context

ADR 0136 lets any number of read-only opens of a dataset sit beside its
writer, but a read-only open is a snapshot. It lists `log/` once and never
learns of a later commit.

The management plane (ADR 0163) needs every instance of a fleet to read:
- the system dataset, for its grants;
- every data dataset it does not write, since any instance serves reads and
  forwards writes (ADR 0168).

Replication (#97) builds handover on the same thing. So the follower is built
once, here, with no handover and no promotion.

An object store bills per request, and an idle fleet should cost nothing.
A follower that lists `log/` on a timer would pay a LIST per dataset per
interval, forever.

## Decision

1. **The contract is in `Varve.Store`.**
   - `ITailingStorage : IStorage` adds `ValueTask<bool> RefreshAsync(CancellationToken)`.
   - A refresh learns what the writer has appended since the last one, adopts
     it, and returns whether there was anything.
   - **Adopting is the writer's own check** (ADR 0136 point 2): each chunk's
     self-hash, slot, offset, predecessor hash, content hash, and an epoch no
     lower than the chunk before. A chunk that does not verify is an error,
     never skipped.
2. **`Dataset.OpenFollowerAsync(ITailingStorage, DatasetOptions)`** opens a
   dataset as a follower. It is an ordinary `Dataset`:
   - pinned reads, as-of reads, the commits resource and `Subscribe` work as
     on any open;
   - **`AdvanceAsync`** refreshes the storage, reads the commits closed since
     the head, verifies them as an open does (I6), and applies them to the
     commit index, the default projection and the graph versions. A torn or
     unclosed tail is ignored until a later advance finds it closed;
   - `Head` is the applied position;
   - **every write is refused** with `CommitOutcome.Unavailable` and the reason
     "follower", and a checkpoint is refused, as on any read-only open (ADR
     0139). A follower runs no maintenance.
3. **`Varve.Store.ObjectStorage` implements it** for `ObjectStorageAccess.ReadOnly`.
   A refresh **probes the next slot with a GET**: chunk *k*+1 of the newest
   segment it holds, or chunk 0 of the next segment once it holds a seal.
   - **A found chunk** is adopted, and the probe goes on to the slot after it.
   - **A chunk carrying a seal moves the probe to the next segment.** Adopting
     across the seal is the one case that **lists** `log/`, because the next
     segment may already be consolidated.
   - **A 404 is nothing new, after one check.** A refresh that has not seen
     its segment's seal also probes for that segment's consolidated object.
     - **Why the check.** Consolidation deletes every chunk of a sealed
       segment but its fences (ADR 0136 point 4). So a seal the follower has
       not seen can disappear, together with the chunks before it, and then
       the slot probe answers 404 for ever.
     - **What it costs.** An idle refresh is two GETs that both answer 404.
       This is the one deviation from "a 404 means nothing new", and the
       operator guide states the cost.
   - **The writer's catalog is re-read only when a probe found commits,** to
     attach its new extents (ADR 0139).
   - **Like every read-only open, a refresh never probes conditional writes,
     claims, or writes** (ADR 0136 point 5).
4. **A follower advances on demand.** The host advances a follower in three
   cases:
   - **A read arrives** and its last advance is older than
     `Varve:Fleet:FollowInterval` (default one second).
   - **A request names a minimum.** It carries `Varve-Min-Position` or
     `Varve-Min-Management-Position` above the applied position, and the
     follower advances once before answering.
   - **The system dataset,** the one exception, is advanced on the interval,
     because grants must move without data reads.

   An idle dataset makes no requests at all. The interval decides how soon a
   follower learns, never whether what it learns is right: there is no
   timeout in any decision (ADR 0136 point 9).
5. **Bounded memory.** A follower's projection grows in memory from the
   writer's extents.
   - When it has applied more than `DatasetOptions.MemtableLimit` quads since
     its open, the host re-opens it from the writer's newest catalog.
   - Reads already pinned keep the old dataset until they end.
6. **The reader contract of Extent maintenance (#110) is a stated hook.**
   - On each catalog re-read, the follower holds that catalog version. It
     reports this through one internal call, which does nothing until #110
     lands.
   - It is safe meanwhile because nothing deletes a shared extent yet (ADR
     0139, `DeletionWaitsForExtentMaintenance`).
   - Whichever of #110 and #105 lands second turns the call into #110's
     announcement under `readers/`.
7. **The file backend has no follower.** ADR 0075 never built a read-only
   open of a file dataset, and a fleet of one has no other reader.

## Alternatives considered

- **List `log/` on every refresh.** A LIST per dataset per interval, the cost
  the maintainer refused. The slot is deterministic, so a GET answers the same
  question.
- **One GET per refresh, trusting a 404.** It is wrong across a seal that
  consolidation removed before the follower saw it.
- **Follow every dataset on a timer.** An idle fleet would pay requests for
  ever.
- **Store change notifications** (Event Grid, S3 events). Faster, but they
  depend on a provider. The probe is the same everywhere, and a notification
  may later shorten the interval without changing a decision.

## Consequences

- **What Replication (#97) reuses unchanged:** `ITailingStorage`,
  `OpenFollowerAsync`, `AdvanceAsync`, the probe and lag on readiness. It adds
  warmness, the takeover claim and promotion.
- **`Varve.Store`'s baseline** gains `ITailingStorage`,
  `Dataset.OpenFollowerAsync`, `Dataset.AdvanceAsync` and `Dataset.IsFollower`.
- **The operator guide** states the cost:
  - per data dataset: two GETs per advance of an idle follower, at most one
    advance per interval, and only while reads arrive;
  - for the system dataset: the same, on every interval.
- **The fleet test** asserts that an idle dataset made no request over its run.

## Checks

- **Checked against the accepted ADRs** (0001–0147). It touches:
  - **0018** (the backend still does not parse the log; the store's reader
    does);
  - **0072** (the follower verifies the chain as an open does);
  - **0134** (GETs and the one LIST);
  - **0135** (consolidation);
  - **0136** (refined as above);
  - **0139** (the catalog attached, then re-read).

  No conflict.
- **Layer ownership.**
  - `Varve.Store` (4): the contract and the advance.
  - `Varve.Store.ObjectStorage` (5): the probe.
  - `Varve.Server` (6): when to advance.
- **Analyzer rule.** None.
- **Open questions owned.** None.
