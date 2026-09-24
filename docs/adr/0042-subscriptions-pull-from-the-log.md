# 0042 — Subscriptions pull from the log

## Status

**Accepted.** 2026-09-23.

Decides the delivery mechanism behind §8's `Subscribe(from, filter)` and §7's
asynchronous projections. ADR 0016 fixed the promises — closed commits, in
order, at-least-once, a consumer-owned position — and left how they are kept to
the implementation.

## Context

There are three ordinary ways to feed a subscriber.

1. **Push**: the sequencer calls each subscriber after closing a commit.
2. **A queue per subscriber**: the sequencer writes each commit into a buffer
   the subscriber drains.
3. **Pull**: the subscriber reads the log from its own position and waits when
   it reaches the head.

The first two put the subscriber on the sequencer's path. ADR 0011's single
sequencer is the dataset's write throughput; ADR 0016 rejected making every
projection synchronous precisely because one slow consumer would stall it. A
queue moves the stall to a bound — a full buffer either blocks the sequencer or
drops commits — and duplicates, per subscriber, what the log already is.

## Decision

**A subscription is a reader of the log.** It holds the position it last
delivered, reads the next closed commit, filters its delta, and yields it. When
it reaches the readable head it awaits a **head-advanced signal**: one
completion source, replaced on each commit and completed when the readable head
moves. The sequencer's only work for subscribers is completing that signal.

- **In order and closed-only**, because the reader never reads past the readable
  head (ADR 0013).
- **At-least-once**, because the consumer owns its position: resuming from the
  last position it *persisted* re-delivers anything it had not.
- **No buffer per subscriber.** The log is the buffer. A subscriber that falls
  behind costs reads, not memory, and cannot slow a writer.
- **The filter runs in the reader**, before delivery (ADR 0016 rejected
  consumer-side filtering). A `Data` commit whose filtered delta is empty is
  skipped and the next delivered one carries its true position; `Settings` and
  `Erasure` commits are delivered whatever the filter (ADR 0046).
- **The delivered commit carries only the allocations its delivered delta and
  metadata refer to**, so a filter restricts terms as well as quads — the shape
  I9 will need when private terms exist.
- **The shape is `IAsyncEnumerable<Commit>`**, cancelled by the consumer's
  token. Async iterators are compiler-generated state machines: no reflection,
  no runtime code generation, and they run on the single thread of a browser.

### Projections use the same reader

An asynchronous projection is fed by `CatchUpAsync(projection)`: read from
`projection.Position + 1` to the head and `ApplyAsync` each commit, unfiltered.
`RebuildAsync(projection, fromCheckpoint)` resets it — to empty at 0, or to a
checkpoint's view at its position — and catches up. There is no registry and no
background task the store owns; the caller decides when a projection runs,
which in a browser is the only honest answer.

## Alternatives considered

- **Push from the sequencer.** Lowest latency. Rejected: it puts every
  subscriber's cost into every commit, which ADR 0016 already refused for
  projections, and it makes the sequencer responsible for a subscriber that
  throws.
- **`System.Threading.Channels`, one channel per subscriber.** In the BCL and
  well understood. Rejected: a bounded channel either blocks the writer or drops
  commits, and an unbounded one is a memory leak with a slow consumer on the
  other end. It also duplicates the log in memory.
- **A registry of projections the store drives in the background.** What ADR
  0016's "registry" could be read as. Rejected for now: a background task per
  projection needs a scheduler the browser host does not have, and a store-owned
  registry is the shape the reserved VARVE0005 exists to watch. The caller-driven
  `CatchUpAsync` is the primitive a later host-level runner can be built from.
- **A callback-based `Subscribe(handler)`.** Familiar. Rejected: it inverts
  control of the position back to the store, and at-least-once with a
  consumer-owned position is simplest when the consumer is the one looping.

## Consequences

- **Latency is one signal and one log read**, which in memory is a slice. On the
  file backend it is a positional read; milestone 6 decides whether hot tail
  commits are cached.
- **Reading a commit decodes it from the log each time.** Two subscribers at the
  same position decode twice. That is the price of no shared buffer, and it is a
  cache to add behind the reader if it is ever measured to matter.
- **"At-least-once" is exactly-once within a process** that does not restart.
  The guarantee is stated as at-least-once because that is what survives a
  consumer that crashes between applying and persisting.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0041).
  Touches **0011** (nothing is added to the sequencer's path but completing a
  signal), **0013** (closed commits only), **0016** (the promises this keeps,
  and the "registry" it declines to build yet), and **0046** (which commit
  kinds bypass the filter). No conflict with any.
- **Layer ownership.** Subscriptions, `CatchUpAsync` and `RebuildAsync` are
  `Varve.Store`, **layer 4**.
- **Analyzer rule.** None.
- **Open questions owned.** None.
