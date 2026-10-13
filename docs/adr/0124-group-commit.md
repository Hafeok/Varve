# 0124 — Group commit: one storage write and one flush for the requests that wait

## Status

**Accepted — filed unaccepted by milestone Commit path of #91, 2026-10-13**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the Commit path plan, with the conditions on timestamps,
projection failure and cancellation written into the decision below.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
version 1.7, §5 T1.

## Context

T1 step 7 appends a commit's records and makes them durable before the
commit returns (ADR 0013), and the file backend makes them durable with one
device flush (ADR 0073). Every commit therefore pays one flush, and a writer
that arrives while another's flush is in progress waits for it and then pays
its own. Milestone 7a measured the consequence over HTTP: with eight writers
on the file backend the p50 rose from 0.76 ms to 3.67 ms, "because each
commit now waits its turn in the sequencer for the flush of the one before"
(`tests/Varve.Benchmarks/README.md`, addendum of 2026-10-08).

The flush is the whole cost on a device and most of it on an object store,
where a write is milliseconds. The object-storage backend (#92) cannot afford
one write per commit. Every database with a write-ahead log answers this the
same way: the requests that queue behind a write in flight are written
together when it returns.

What has to survive the change is everything T1 promises per commit. I1–I8
are stated per commit, and nothing in the specification, the protocol or the
feed may depend on how commits were grouped for writing.

## Decision

**Group commit, on every backend, on by default.**

1. **Steps 1–6 per request, in arrival order; step 7 per batch.** Requests
   that arrive while a write is in flight queue. When the write returns, the
   next leader takes the queue in arrival order, up to the bounds, and runs
   T1 steps 1 to 6 for each request in turn **against the state as that
   request would see it**: the head after every earlier request of the batch
   that committed. Normalisation, term resolution, the expected-position
   check, the validators and their overlay are exactly the unbatched ones,
   because the state they read is the unbatched state. A request answered
   `NoChange`, `Conflict` or `Rejected` takes no place in the batch and
   leaves no trace, as before.
2. **Each request keeps its commit.** Its own records, closing flag,
   position, header and header-chain link (`prev` is the hash of the batch
   member before it, or of the head for the first). The records of the batch
   are appended in position order and **made durable by one flush**, after
   the last. A batch that fills the active segment seals it exactly as a
   lone commit would (flush, trailer, flush, seal, next segment), so the
   bytes of `log/` do not depend on where a batch began or ended.
3. **Acknowledgement after durability, all or nothing.** No request of a
   batch is answered `Committed` before the flush that makes the batch
   durable has returned. A failed write fails every request in the batch,
   none is acknowledged, and the dataset is broken until reopened, as a
   failed append is today. Recovery then finds the log at whatever closed
   commit reached the device: a member that was never acknowledged may be
   found durable after reopening. That is the same uncertainty a lone
   commit's failed flush carries today (its records may have reached the
   device before the error), and a caller that must know resolves it the
   same way, by reading the commit's cause in the feed.
4. **Timestamps.** Each member's timestamp is taken when it is staged:
   `max(clock, ts(previous member))`, the previous member being the head for
   the first. Timestamps are therefore monotone within a batch and across
   batches (I5), and a member's timestamp is the one it would have had
   unbatched under the same clock readings. Equality of `log/` bytes between
   a batched and an unbatched history is stated, and tested, under an
   injected `TimeProvider`.
5. **A default projection failure at member k.** The default projection is
   applied to each member as it is staged, so the next member can read it.
   If applying it fails at member *k*, members 1 to *k* are written (their
   records are a durable fact, and *k*'s failure is the projection's, not the
   log's), the dataset enters the failed state at *k* exactly as it would
   unbatched (§7), and **members k+1 onwards are not in the write**: they are
   answered `Unavailable` because they never became durable and never will,
   which is what the unbatched sequencer answers every request after a
   projection failure.
6. **Cancellation.** A request cancelled while it is still queued, before a
   leader has taken it, is withdrawn: it is never staged, never written, and
   its caller sees the cancellation. Once a leader has taken it into a batch
   it is not withdrawn: the caller may stop waiting, and the commit is
   written with the rest of the batch. The batch's write is not cancellable
   by any one member's token.
7. **Bounds.** A batch holds at most `GroupCommit.MaxRecords` records
   (default 1,024) and `GroupCommit.MaxBytes` bytes of records (default
   4 MiB); a single commit larger than either is a batch of its own. There
   is no timer and no wait for company: a request that finds the sequencer
   idle is a batch of one and is written at once, so an idle dataset's commit
   latency is what it was. `GroupCommitOptions.Off` writes every commit
   alone, which is the unbatched sequencer exactly; it is the reference the
   equivalence property runs against.
8. **What a caller can observe is latency, and nothing else.** The batch
   boundary is part of no contract: not of the commit result, the feed, a
   subscription, the commit index, the header chain or the files under
   `log/`. Derived data may differ in bytes, because maintenance is
   considered once per batch rather than once per commit, which §2 already
   allows (`derived/` is reproducible in content, not in bytes).
9. **Telemetry** stays per commit (ADR 0112): `varve.store.commit.duration`
   and the queue depth are recorded per request. One instrument is added,
   `varve.store.commit.batch_size`, a histogram recorded once per write.

## Alternatives considered

- **A delay window** (wait up to *t* for company before writing). What
  several databases offer as `commit_delay`. Rejected as the default: it adds
  latency to every commit on an idle dataset to save flushes on a busy one,
  and the queue that forms behind a write in flight already batches exactly
  the requests that would have waited. It can be added later as an option
  without changing anything here.
- **A background writer thread** owning the log, with requests posted to it.
  The common shape in server databases. Rejected: the browser host has one
  thread, and the store has no background thread of its own today
  (maintenance is a task the sequencer starts). The leader pattern needs no
  thread: the request that finds the sequencer free does the work.
- **One commit per batch.** Cheaper still: one header, one position. Rejected
  outright by ADR 0013 and I1: a position is a request's, and two requests
  merged into one commit cannot be told apart in the feed or undone by
  `Diff`.
- **Normalise the whole batch against the head before it, and the members'
  deltas against each other afterwards.** Equivalent in result and harder to
  prove; staging each member against the state the previous one produced is
  the unbatched computation, run earlier.
- **Acknowledge each member when its own records are appended**, before the
  batch's flush. Faster on paper, and a lie: an acknowledged commit could be
  lost at power loss while a later one survived. The flush is the commit
  point (ADR 0013).

## Consequences

- **Every backend pays one flush per batch, not per commit.** On the file
  backend the device's flush is amortised over the queue; on the object
  store (#92) a batch is one object write. The write benchmark before and
  after is in the milestone's record.
- **One write failure fails up to a batch of requests**, rather than one.
  They fail together and none is acknowledged, so no caller is told a
  falsehood; more callers learn about a failure that was always fatal to the
  dataset.
- **A request may wait for a batch it is not in.** A request arriving just
  after a leader took the queue waits for that batch's flush, then is
  written in the next. Its latency is at most two writes, against at most
  the length of the queue ahead of it unbatched.
- **Replication's tier-1 writer handover and object storage rely on point 3**:
  nothing acknowledged is ever not durable.

## Checks

- **Checked against the accepted ADRs** (0001–0123) and specification 1.6.
  Touches **0010** (normalisation per request, I2 per commit), **0011**
  (`OneSequencerPerDataset` stands: the sequencer still takes requests one at
  a time for steps 1–6 and assigns positions in order; what it batches is
  the write), **0013** (records and the closing flag per commit; the flush
  after the last record of the batch orders every closing record before the
  commit point of each), **0016** (the synchronous default projection is
  applied before each request returns; a failure is the failed state),
  **0018** and **0073** (one `FlushAsync` per batch; the trailer rule on
  sealing unchanged), **0072** (no byte of format 1 changes), **0112** (one
  new instrument), **0114** (point 1 amended: a write waits in the
  sequencer's queue and is written in a batch). No conflict.
- **Layer ownership.** `Varve.Store`, layer 4. The server's configuration
  keys (`Varve:Store:GroupCommit:MaxRecords`, `:MaxBytes`) are `Varve.Server`,
  layer 6, under ADR 0115.
- **Analyzer rule.** None.
- **Open questions owned.** None.
