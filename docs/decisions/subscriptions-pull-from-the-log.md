---
set: subscriptions-pull-from-the-log
namespace: varve
adr: 0042
decisions:
  - key: SubscriptionIsALogReader
    statement: "A subscription is a reader of the log that yields closed commits and, at the readable head, awaits a head-advanced signal the sequencer completes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: NoBufferPerSubscriber
    statement: "There is no buffer per subscriber: the log is the buffer, and a slow subscriber costs reads, not memory"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: FilterRunsInTheReader
    statement: "A subscription's filter runs in the reader, before delivery"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: DeliveredAllocationsFiltered
    statement: "A delivered commit carries only the allocations its delivered delta and metadata refer to"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: SubscriptionIsAnAsyncEnumerable
    statement: "A subscription is an IAsyncEnumerable of commits, cancelled by the consumer's token"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: CallerRunsProjections
    statement: "Asynchronous projections catch up and rebuild through the same reader when the caller asks, with no registry and no background task the store owns"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0042](../adr/0042-subscriptions-pull-from-the-log.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
