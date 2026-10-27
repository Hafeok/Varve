# 0136 — One writer by an epoch, fenced by a log slot; no lease, no timer

## Status

**Accepted — filed unaccepted by milestone Object storage of #92, 2026-10-15**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the Object storage plan, 2026-10-15: an open for write
claims the next epoch, with the guards of decision 5; the epoch in a writer
record of its own rather than in `log/MANIFEST`; fence chunks the only
tombstones, kept for ever; graceful shutdown abdicates.

**Refines** ADRs [0075](0075-one-process-per-dataset-by-an-os-lease.md) and
[0116](0116-the-lease-policy-and-varve-lease.md) for
the object-storage backend; both stand unchanged for the file backend, whose
lock the operating system releases.

Meets the "Object storage must guarantee" list of the writer handover
decision Replication (#97) files: log slots claimed by create-if-absent, no
two epochs in one slot; the epoch in every chunk's header; a header chain a
new writer verifies; consolidation that never reopens a slot a fence could
need; a writer record that can hold a released epoch.

## Context

The file backend has one writer because the operating system gives one
process an exclusive handle and takes it back when the process ends (ADR
0075). Nothing ever forces a held lock, because "an interval after which the
lock is broken automatically would be a timer that decides the holder is
dead when it is slow" (ADR 0116).

An object store has no such handle. What it has is linearisable conditional
writes (ADR 0134). A lease built on them — an object holding a holder and an
expiry, renewed in the background — is exactly the timer ADR 0116 refuses:
correctness would rest on clocks and pause times, and a writer stalled past
its expiry in a garbage collection would come back writing beside its
successor.

Fencing needs no clock. If every write a writer makes is a create of a name
that can be created once, then a successor that takes the next name stops
the predecessor at its very next write, whenever that comes, without either
knowing anything about time.

## Decision

**1. The writer record.** The object `writer`, beside `log/` and `derived/`
under the dataset's prefix, holds the current epoch (a counter from 1), its
holder (a name the host gives itself), whether it was released and why, and
the epoch history: each claim's epoch, holder, trigger, timestamp, fence slot
and release reason. It is JSON an operator can read. It is created once by
create-if-absent and afterwards only replaced by replace-if-unchanged.
`log/MANIFEST` stays what ADR 0072 made it, the format's manifest, written
once: the epoch is not log data, and keeping it out of `log/` keeps `log/`
byte-identical to the file backend's.

The history records the fence slot (`NNNNNNNN.chunks/CCCCCCCC`), not a
position: the backend never parses the log (ADR 0018), and the slot is where
the epoch's first commit begins.

**2. An open for write claims the next epoch, then fences.**

- *Claim.* Read the writer record; replace it, against the version that shows
  epoch *n*, with epoch *n*+1 and this holder (or create it with epoch 1). A
  claim that loses the race is **not retried**: the open becomes read-only
  and reports itself superseded. Retrying would claim *n*+2 and supersede the
  winner, and two instances would supersede each other for ever.
- *Adopt.* List `log/` and read every chunk of every segment not yet
  consolidated, in order, checking each header — its self-hash, its slot, its
  offset, the previous header's hash, the hash of its bytes, an epoch no
  lower than the chunk before. A chunk that does not verify refuses the open.
- *Fence.* Create the next slot by create-if-absent as an empty chunk
  marked *fence*, carrying epoch *n*+1. If the create finds the slot taken,
  adopt what is there and try the slot after; if what is there carries an
  epoch higher than *n*+1, a later writer holds the log already and this open
  is superseded.

The fence is then recorded in the writer record.

**3. The fence is the next slot in log order.** The slot after the last that
exists: the next chunk of the newest segment, or chunk 0 of the next segment
when the newest is sealed. A segment that holds only fence chunks has no
bytes and was never opened; it is not listed to the store, which sees what
it would after a crash just before opening a segment, and the store's own
`CreateSegmentAsync` then opens it without a create.

*Why a superseded writer can append nothing after the fence.* A writer
creates one chunk at a time, and only ever the slot after its own last
successful create, or the slot it is retrying. Let a writer of epoch *e* have
last succeeded at slot *k*−1 when epoch *e*+1 claims. Either its attempt at
*k* landed before the new writer listed, and was adopted, and the fence is at
*k*+1 or beyond by the same argument; or it did not, and the fence is at *k*.
In every schedule, the only new slot the old writer can attempt is the fence
slot, which exists, so its create fails. A retry of a slot it already wrote
and that consolidation since deleted is a write of the same bytes the new
writer adopted: it acknowledges a commit that is, truly, in the log. The
argument holds across a segment boundary, because the seal is in the log's
bytes: the slot after a seal chunk is chunk 0 of the next segment, which is
exactly where the old writer's next create would go.

**4. Fence chunks are kept for ever.** Consolidation (ADR 0135) deletes every
chunk of a sealed segment except its fence chunks. A fence chunk is 136 bytes,
one per epoch, and the epoch history already lists each. Nothing else is
kept: an empty tombstone per chunk would make listing and cold start grow
with the log's history.

**5. A superseded writer stops, stays readable, and never claims again.**

- A create that finds its slot holding other bytes means a newer epoch holds
  the log. The writer marks itself superseded, fails the batch with
  `WriterSupersededException`, and refuses every later write the same way.
  The store's sequencer refuses writes after a failed batch until the dataset
  is opened again (ADR 0124); reads continue.
- **It never claims again on its own.** Only a fresh process start claims.
  The server reports it as `superseded` on `/health/ready`, which is then
  false, while `/health/live` stays true: an orchestrator that restarts on
  liveness never restarts it to win the epoch back.
- **Only opens for write claim.** Every read-only open — the command line's
  read commands, `varve info`, `varve lease` without `--take`, a follower
  attaching the writer's catalog (ADR 0139) — claims nothing and writes
  nothing.

Over HTTP the refused write is `503 writer-superseded`, a new problem type in
the catalogue (ADR 0119).

**6. Graceful shutdown abdicates.** Closing a writer that holds its epoch
replaces the record, against the version it last wrote, marking the epoch
released with the reason `shutdown`. The next writer's claim then follows a
release rather than superseding a writer that might still run, and a
revision rollover on Container Apps is clean. A release that cannot be
written is left undone: the next claim supersedes instead, which is also
safe.

**7. Every claim is visible, and a run of `restart` claims means a
misconfiguration.** Each claim is recorded with its trigger: `restart` for an
open for write, `operator` for a takeover. Restarts of one writer show as
`restart` claims each following a `shutdown` release, or following nothing
after a crash. Claims by **different holders**, alternating, each superseding
the last, mean two instances are configured to write one dataset; the
operator guide says so, and how to read it. No commit is written for a claim:
the log stays the user's data plus commit metadata.

**8. The operator's takeover names the epoch.** `varve lease --take <url>
--epoch <n>` claims *n*+1 only if the record still shows *n*, fences the log,
and releases the epoch with the reason `operator-takeover`, so that the next
writer to open claims cleanly. There is no time-based check: the operator is
the one who decided the holder is gone. `varve lease <url>` reports the
epoch, its holder, whether it is released, and the history.

**9. No lease, no renewal, no timer.** Nothing is renewed in the background,
and nothing is decided from elapsed time. The writer record's timestamps are
read from the host's `TimeProvider` for the operator to read, and never
compared. A retry waits before trying again; how long it waits decides how
soon, never whether.

## Alternatives considered

- **A lease object with an expiry.** Correctness from clocks; refused by ADR
  0116's reasoning and the standing constraint.
- **The epoch in `log/MANIFEST`.** As the session prompt first put it. The
  manifest is written once (ADRs 0071, 0072), and putting a replaceable
  object under `log/` would make `log/` differ from the file backend's.
- **The manifest as the fence: check it before every chunk.** A read or a
  replace per batch, on the commit path, and still racy without the slot.
- **Claim only an unclaimed or released epoch.** Then every crash whose
  replacement has another name — every replica restart on Container Apps —
  would wait for an operator. A restart is a takeover, guarded by decision 5.
- **A loser retries its claim.** Two instances configured to write would
  supersede each other on every restart.

## Consequences

- `Varve.Store` gains `WriterSupersededException`; `Varve.Store.ObjectStorage`
  the writer record, `ObjectStorage.TakeOverAsync` and `ReadWriterAsync`.
- `varve lease` reads the record for an object-storage dataset and takes over
  by epoch; for a directory it is ADR 0116's.
- `/health/ready` gains the state `superseded`; the catalogue gains
  `writer-superseded`.
- Replication (#97) builds automatic handover on the same record: its
  release by abdication needs no format change, and its probe-triggered
  takeover is this ADR's claim with an epoch named, as the operator's is.

## Checks

- **Checked against the accepted ADRs** (0001–0133). Touches **0018** (the
  backend never interprets log bytes; the record and the chunk header are
  the backend's), **0071** and **0072** (the manifest stays written once),
  **0075** and **0116** (refined for this backend: an open for write takes a
  held epoch over, because nothing in an object store releases it when its
  holder dies; the file backend's rulings stand), **0113** (a new readiness
  state), **0119** (a new problem type), **0124** (a failed batch refuses
  later writes). No conflict.
- **Layer ownership.** `WriterSupersededException`: `Varve.Store`, layer 4.
  The record, the claim and the fence: `Varve.Store.ObjectStorage`, layer 5.
  `varve lease` and the readiness state: `Varve.Server`, layer 6; the problem
  type, `Varve.Protocol`, layer 5.
- **Analyzer rule.** None.
- **Open questions owned.** None. Automatic handover is Replication's (#97).

## Amendment, 2026-10-19 — the claim records an address; the reason placement

Filed by milestone Management plane of #105, unaccepted until the maintainer
accepts it (ADR 0066).

Each claim in the epoch history may carry `address`, the claiming instance's
internal URL (ADR [0168](0168-write-routing-by-forwarding.md) point 6). The record
stays `format: 1`, since a reader of format 1 reads members by name. A release may
carry the reason `placement` (ADR [0163](0163-the-management-plane.md) point 12).
This adds; no ruling of this ADR changes.

**The ledger.** The rulings are ADR 0168's `WriterRecordNamesTheAddress` and ADR
0163's `PlacementByEvents`.

## Amendment, 2026-10-27 — handover's triggers and release reasons, a claim made before the open, and supersession per dataset

Filed by Replication part 1 (#165), unaccepted until the maintainer accepts
it (ADR 0066). Ruled in [ADR 0179](0179-writer-handover-within-a-store.md).

- **Two more triggers.** The epoch history's `trigger` may be `abdication`, a
  standby's claim after a release, or `probe`, a standby's claim on a
  takeover request. `restart` and `operator` are unchanged.
- **Two more release reasons.**
  - `storage-error`: a batch failed against the store and the release could
    still be written.
  - `cache-error`: a fault of the instance's own cache directory.

  A fault deterministic over the log is never a release reason.
- **A write-open may take a claim already made.** It is guarded by the epoch
  named, then adopts and fences as point 2 says. This is how a warm standby
  claims in place.
- **Point 5 per dataset.** On a dataset with automatic handover (ADR 0180), a
  superseded writer re-opens as a follower and is eligible again once warm.
  On every other dataset point 5 stands.
- **The record stays `format: 1`.** A reader of format 1 reads members by
  name. An unknown trigger or reason was refused before this amendment, so a
  build before it refuses a record written after a handover. The release
  notes say so.
