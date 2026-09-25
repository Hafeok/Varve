# 0052 — A pinned read lives for one query execution

## Status

**Accepted.** 2026-09-24. Refines ADR 0015's "a pinned read has the lifetime
of one operation" for the operation that milestone 5 introduces — a query —
and fixes who owns the pin, who disposes it, and what bounds it. 0015 is
unchanged.

## Context

ADR 0015 and specification R1 define a pinned read as an engine snapshot with
the lifetime of one operation, and say plainly that a held pin is a resource:
while it is open the engine cannot release what it is reading, and a
long-held pin is a leak with visible consequences. Milestone 4 built it as
`Dataset.Pin()` returning a `DatasetView` the caller disposes, and its report
raised the question this ADR answers: the evaluator's API should make the
operation's boundary explicit rather than hold a view for a session.

A query execution has three parties. The **caller** wants results; it may be
a server handling one HTTP request, or a library user iterating in a loop.
The **evaluator** at layer 3 runs over `IQuadSource` and, by ADR 0005, knows
nothing about pins, positions or stores. The **store** at layer 4 hands out
the pin. Only the caller can see all three, and only the caller knows when
the last result has been consumed.

Two failure modes are being designed against. A pin disposed while the result
stream is still being read makes every later result an
`ObjectDisposedException`. A pin never disposed — because the caller stopped
iterating early, or because a client went away — holds a checkpoint from
being dropped for as long as the process lives.

## Decision

1. **A pinned read lives for one query execution.** It is taken before
   evaluation begins and released after the last result has been consumed or
   the consumer has stopped, whichever comes first. It is not held across
   queries, not cached, and not shared between requests.
2. **The evaluator receives a pinned source it does not own.** Its entry point
   takes an `IQuadSource`; it never calls `Pin()`, never disposes what it was
   given, and never learns that the source was a pin. That keeps ADR 0005's
   boundary exactly where it is: an evaluator that could pin would be an
   evaluator that knew about a store.
3. **The caller disposes the pin when the result stream ends.** The result
   stream the evaluator returns is disposable, and disposing it is the signal
   the caller has finished; the caller — or the layer 5 integration acting
   for it — disposes the `DatasetView` at that moment. Disposing the pin
   while results are still being pulled is a caller error, and the view's
   `ObjectDisposedException` is the honest report of it.
4. **A configurable maximum lifetime with cancellation is the safety net.**
   The evaluator's entry point takes a `CancellationToken`, and a query that
   is still running when the token fires stops with a cancellation the caller
   sees. The bound is a setting of the host that runs queries for others —
   **the server, at milestone 7**, which enforces it per request and disposes
   the pin on cancellation. An embedded caller may set no bound, because it
   is the party that would be bounding itself.
5. **The contract is documented in two places**: on `Dataset.Pin()` now, and
   on the evaluator's entry point when 5b writes it. A reader of either sees
   the whole arrangement.

## Alternatives considered

- **The evaluator pins.** It would take a `Dataset` and manage the lifetime
  itself, which is the ergonomic shape. Rejected by ADR 0005: the evaluator
  at layer 3 cannot reference the store at layer 4, and an evaluator that
  took a "pinnable" contract would be a layer 1 contract shaped around a
  store concept.
- **The pin's lifetime is the caller's session.** Query after query over one
  snapshot, which is what a REPL wants. Rejected by ADR 0015: it is a leak
  with a name, and the same result is available honestly as an as-of read at
  the position the session started, which holds nothing.
- **The store times pins out itself.** The store would carry a clock and a
  policy for every pin it hands out. Rejected: the store has no ambient
  clock by design (ADR 0011), the bound is a host policy and differs between
  a server and an embedded process, and a pin that vanished under a running
  query would turn a slow query into a wrong answer rather than a cancelled
  one.
- **Results are materialised before the pin is released**, so the stream
  never outlives the pin. Safe, and rejected for the reason streaming exists:
  a `SELECT` over a million solutions must not need a million solutions of
  memory before the first is returned.
- **Reference counting the view**, so the last consumer's dispose releases
  it. Rejected as machinery for a problem one owner already solves; a pin has
  one caller.

## Consequences

- **The layer 5 integration is where the pattern is written once**: pin,
  evaluate, hand the stream out wrapped so that disposing it disposes the
  pin. Every host — server, CLI, SPARQL Update's `WHERE` evaluation (ADR
  0005) — uses that wrapper rather than repeating the choreography.
- **A caller that forgets to dispose leaks a pin**, and in memory that costs
  a held index version and a checkpoint that cannot be dropped. The server
  makes it impossible by construction at milestone 7; an embedded caller is
  trusted, as it is with every other `IDisposable`.
- **Cancellation is a first-class outcome of a query**, distinct from an
  error, from the first evaluator on.
- **Nothing in `Varve.Store` changes** except documentation. The store
  already returns a disposable view and already refuses a disposed one.

## Checks

- **Checked against the accepted ADRs** (0001–0051) and the specification.
  Touches **0015** and R1 (refined for one operation, not changed), **0005**
  (the evaluator never pins), **0011** (no clock in the store, so no timeout
  there), **0041** (what a held pin holds: an index version), and **0048**
  (the evaluator's entry point is where the second half of the contract is
  documented). No conflict with any.
- **Layer ownership.** The pin is `Varve.Store`, **layer 4**. The evaluator's
  cancellation is `Varve.Sparql.Evaluation`, **layer 3**. The choreography is
  **layer 5**. The maximum lifetime is a server setting, **layer 5**, at
  milestone 7.
- **Analyzer rule.** None.
- **Open questions owned.** None.
