---
set: validity-intervals-in-the-default-projection
namespace: varve
adr: 0138
decisions:
  - key: IntervalsReplacePointInTimeRuns
    statement: "A checkpoint's persisted form holds each quad's intervals of assertion, its asserting and retracting positions, so a read at a position below the head is served from them and not by replaying the log"
  - key: ExtentIsAnExactDelta
    statement: "An extent's asserted section holds the quads asserted at its window's end and not at its start, its retracted section the reverse; every other interval is a row within, so a set reads as a run, an exact delta against the sets before it"
  - key: AsOfInsideAWindowNarrowsTheSet
    statement: "A read at a position inside a set's window is the sets before it and that set narrowed to the position, at a cost that is the set's size and not the distance from a checkpoint"
  - key: CheckpointIsAChainOfSets
    statement: "A checkpoint is the chain of extent sets from position 0 to it; dropping one merges its set into the next, or drops the newest, and neither changes a result at any position"
  - key: GraphIndexPerSet
    statement: "Each set carries the graph-version index at its end (ADR 0125)"
  - key: BulkLoadReachesExtentsFromTheLog
    statement: "A bulk load is unchanged: its commit reaches extents at the next checkpoint, read from the log like any other"
---

The rulings of [ADR 0138](../adr/0138-validity-intervals-in-the-default-projection.md), filed unaccepted
by milestone Object storage of #92 (ADR 0066).
