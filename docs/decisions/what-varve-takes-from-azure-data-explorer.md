---
set: what-varve-takes-from-azure-data-explorer
namespace: varve
adr: 0140
decisions:
  - key: MaterialisedPartPlusDelta
    statement: "A derived result read at position p uses its projection at an applied position q no later than p and applies the log delta (q, p]: lag costs read latency, never correctness"
  - key: DerivationDeclaredOnce
    statement: "Standing queries are management declarations, maintained as projections from exact deltas"
  - key: AlertsAreResultDeltas
    statement: "A standing query's result delta at a position is the event Varve emits; acting on it stays outside Varve"
  - key: BackfillOnCreation
    statement: "A new standing query builds in the background from the log, and readiness reports its lag"
  - key: ProjectionsAsExtentsInTheStore
    statement: "The default projection's permutations and the dictionary's lookups are immutable, sorted, compressed extents in the object store, each over a position range with per-quad asserting and retracting positions and statistics; a catalog the writer alone swaps names the live ones; local disk caches them"
  - key: NoColumnarTelemetryStore
    statement: "Varve adds no columnar telemetry store and no second query language; high-rate telemetry belongs in an Eventhouse or another columnar store, fed by Varve's CloudEvents"
  - key: NoSealingOnElapsedTime
    statement: "Group commit seals a batch on nothing but what arrived during the previous write; there is one commit path and no timer"
  - key: NoRetentionByAge
    statement: "The horizon moves only by an explicit act, an applied declaration or an operator's request naming a position; nothing moves because time passed"
  - key: NoWallClockFreshness
    statement: "Freshness is a position, never an age, and standing queries may not use NOW()"
  - key: NoDerivedDataInTheLog
    statement: "Derived results are projections, never written to the log, droppable and readable as of any position"
  - key: NoCorrectnessFromTuning
    statement: "A knob may trade cost for speed only if every setting leaves the result equal to a from-scratch evaluation at the same position"
  - key: EveryDerivedResultStatesItsPosition
    statement: "Every derived result states the position it reflects and is exact there or refused"
  - key: NoPurgeByRewriting
    statement: "Data is removed for compliance by crypto-erasure, never by rewriting storage"
  - key: NoQueryFunctionAccessControl
    statement: "Access control is graph-level grants at a management position, not a function applied to a query"
  - key: OneQueryOneNode
    statement: "One query runs on one node; followers add capacity across queries, and distributed execution would be a milestone of its own"
---

The rulings of [ADR 0140](../adr/0140-what-varve-takes-from-azure-data-explorer.md), filed unaccepted by
milestone Object storage of #92 (ADR 0066).
