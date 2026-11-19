---
set: retraction-decided-by-measurement
namespace: varve
adr: 0196
decisions:
  - key: DeletionIsASecondPropagation
    statement: "Each retraction candidate is a deletion pass with its own transfer over the binding's dependency graph, Propagation unchanged, rule joins anchored at each changed fact"
  - key: ThreeCandidatesMeasured
    statement: "DRed, recursive counting and Backward/Forward are built behind one internal strategy, each passing the incremental-equals-from-scratch property, and measured on at least 1,000 commits of REC M under owl2rl, the sample's size, entity retractions and type changes stated"
  - key: ChosenOnTheMeasurement
    statement: "The lowest p99 per commit on owl2rl at REC M is taken, unless another candidate's p99 is within the bound, at most twice it and under 50 ms, and it builds from scratch in at most 0.6 of the time, which is then preferred, the maintainer ruling 2026-11-19; each candidate's build is stated beside its per-commit figures; DRed is chosen, and the losers leave src for the benchmarks"
---

The rulings of [ADR 0196](../adr/0196-retraction-decided-by-measurement.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 2 (#171) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
