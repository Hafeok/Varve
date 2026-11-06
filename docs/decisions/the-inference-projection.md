---
set: the-inference-projection
namespace: varve
adr: 0195
decisions:
  - key: OneProjectionPerMaterialisingBinding
    statement: "InferenceProjection in Varve.Inference.Store is an IProjection over one binding whose profile materialises, fed the dataset's closed commits in order, rebuildable from the log, writing nothing to it"
  - key: RowsAreImplicitQuadsOverHandles
    statement: "A row is one implicit quad over store handles with a validity interval [asserted, retracted), and the closure at p is the explicit quads at p with the rows valid at p, a vocabulary constant the dictionary lacks staged per read"
  - key: PlacedByPremiseSets
    statement: "Each row sits in its data graph, the binding's TBox graph or a premise-set graph of each minimal premise set, as ADR 0190 places it, and the count of premise sets is reported per binding"
  - key: GenerationsPublishedWhole
    statement: "A TBox or rules change, a failed load or a failed incremental pass starts a generation that recompiles the program, and the position moves only once a commit is applied, generation included"
  - key: CheckedStateInTheLocalCache
    statement: "The state is one checked blob per binding in the local derived store, identified by its binding, and a miss, a mismatch or a decoder's exception is a rebuild from the head, never a wrong entailment"
  - key: FromScratchBelowTheFloor
    statement: "A cold start puts the floor at the head, and a closure read below it is the binding's from-scratch closure as of that position, computed when read, under the read's memory limit"
  - key: InferenceBehind
    statement: "Readiness reports each binding's lag and never turns unready for it, and a read that needs the closure at an unreached position waits two seconds, then answers 503 inference-behind; a lookup never denies a term while the closure is behind"
  - key: OneSeamForTierTwo
    statement: "Every tier-2 surface resolves the closure through IInferenceClosures, and a refusal of tier 2 sits there and nowhere else"
  - key: JoinsByIdWhileErasureIsOff
    statement: "Rows are over store handles and joins are by id, exact while erasure mode is off; before erasure mode and a materialising binding coexist, joins are by value, an Erasure commit starts a generation from scratch and stops serving the old one at once, every tier-2 surface unavailable and readiness saying why"
  - key: IncrementalEqualsFromScratch
    statement: "Over generated histories with retractions, TBox and rules changes, the closure at every position equals the from-scratch closure, byte for byte in canonical N-Quads, for each retraction candidate, and as-of reads equal it above and below the floor"
---

The rulings of [ADR 0195](../adr/0195-the-inference-projection.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 2 (#171) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
