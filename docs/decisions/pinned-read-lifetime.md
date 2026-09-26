---
set: pinned-read-lifetime
namespace: varve
adr: 0052
decisions:
  - key: PinPerQueryExecution
    statement: "A pinned read lives for one query execution, from before evaluation until the last result is consumed or the consumer stops, and is never cached or shared"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: EvaluatorNeverPins
    statement: "The evaluator receives a quad source it does not own, never calls Pin() and never disposes what it is given"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: CallerDisposesThePin
    statement: "The caller disposes the pin when the disposable result stream ends, and disposing it earlier is a caller error"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: LifetimeBoundedByCancellation
    statement: "A query's maximum lifetime is a host setting enforced through the evaluator's CancellationToken, set per request by the server and optional for an embedded caller"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: PinContractDocumentedTwice
    statement: "The pin contract is documented on Dataset.Pin() and on the evaluator's entry point"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
---

The rulings of [ADR 0052](../adr/0052-pinned-read-lifetime.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
