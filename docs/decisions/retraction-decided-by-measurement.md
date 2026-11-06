---
set: retraction-decided-by-measurement
namespace: varve
adr: 0196
decisions:
  - key: DeletionIsASecondPropagation
    statement: "Each retraction candidate is a deletion pass with its own transfer over the binding's dependency graph, Propagation unchanged, rule joins anchored at each changed fact"
  - key: ThreeCandidatesMeasured
    statement: "DRed, recursive counting and Backward/Forward are built behind one internal strategy, each passing the incremental-equals-from-scratch property, and measured on REC M histories at the stated retraction rates"
  - key: ChosenOnTheMeasurement
    statement: "The candidate is chosen by p99 time per commit on the owl2rl profile, with the trade stated where another is within 20% and lower on the maximum or memory, and the losers leave src for the benchmarks"
---

The rulings of [ADR 0196](../adr/0196-retraction-decided-by-measurement.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 2 (#171) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
