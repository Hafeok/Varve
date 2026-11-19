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
    statement: "A generation recompiles the program and rebuilds the closures for the causes GenerationsForStatedCausesOnly names, never for a failed pass, and the position moves only once a commit is applied, generation included"
  - key: CheckedStateInTheLocalCache
    statement: "The state is one checked blob per binding in the local derived store, identified by its binding, and a miss, a mismatch or a decoder's exception is a rebuild from the head, never a wrong entailment"
  - key: FromScratchBelowTheFloor
    statement: "A cold start puts the floor at the head, and a closure read below it is the binding's from-scratch closure as of that position, computed when read, under the read's memory limit"
  - key: InferenceBehind
    statement: "Readiness reports each binding's lag, heldFrom, clashes, held, peak and state, following, regenerating after erasure, stopped (memory-limit) or stopped (failed), and never turns unready for them, and a read that needs the closure at an unreached position waits two seconds, then answers 503 inference-behind; a lookup never denies a term while the closure is behind"
  - key: ClosedRowsUnderACeiling
    statement: "Closed rows are held in memory up to HistoryBytes, oldest positions out first, the held window contiguous to the projection's position, with a setting and a gauge; an as-of read below it is from scratch, and no blob holds history"
  - key: ClosureMemoryLimit
    statement: "A binding's closure is bounded by ClosureBytes, by default a quarter of the heap limit or the pool if that is less; a materialisation past it stops, serves no partial closure, and every tier-2 read answers 503 inference-unavailable at once, reason memory-limit, failure configuration, no Retry-After"
  - key: OneSeamForTierTwo
    statement: "Every tier-2 surface, the views varve:explicit and varve:implicit included, resolves the closure through IInferenceClosures, and a refusal of tier 2 sits there and nowhere else, the row-level refusal first"
  - key: JoinsByIdWhileErasureIsOff
    statement: "Rows are over store handles and joins are by id, exact while erasure mode is off; before erasure mode and a materialising binding coexist, joins are by value, an Erasure commit starts a generation from scratch and stops serving the old one at once, every tier-2 surface unavailable and readiness saying why"
  - key: IncrementalEqualsFromScratch
    statement: "Over generated histories with retractions, TBox and rules changes, the closure at every position equals the from-scratch closure, byte for byte in canonical N-Quads, for each retraction candidate, and as-of reads equal it above and below the floor"
  - key: BudgetAskedBeforeEveryAllocation
    statement: "a binding's budget counts bytes, and every structure a closure grows asks it before allocating, facts, arrays at their new size, explicit facts, a pass's record, a build's working sets and premise sets; a build is admitted only if its given facts fit, and a refusal is thrown on the build's thread before the allocation, never by catching OutOfMemoryException"
  - key: OnePoolPerProcess
    statement: "every binding's budget also takes from one pool for the process, and a ClosureBytes above the pool is refused at start"
  - key: CostsMeasuredPerProfile
    statement: "the bytes a budget charges per structure are measured per profile under a hard heap limit, and a test fails when a build needs more than its budget counts"
  - key: GenerationsForStatedCausesOnly
    statement: "a generation is made only for a commit that changes a bound TBox graph, a commit that allocates a term a rule names, and a cold start or a saved state that does not check; a failed pass is never a generation"
  - key: StoppedStaysRefused
    statement: "A budget refusal or a memory failure stops the binding at its memory limit, answered with failure configuration; a failure no repeat can cure stops it as failed, a defect answered with failure defect (ADR 0242 ruling 2, amended 2026-11-19); either answers 503 inference-unavailable with no Retry-After, is shown on readiness, and is never tried again automatically"
  - key: StopsAreRecoveredByAnAct
    statement: "an operator brings a stopped binding back by restarting the instance, whose build reads the limit afresh and never loads a saved stop, or by changing the binding's declaration, which builds a new projection at once; there is no retry under the same limit"
  - key: StorageFailuresHoldOff
    statement: "a pass reads the store before it changes a closure, and a store read or write that fails holds the feed off and resumes it from the projection's position with no generation; only a budget refusal, a memory failure, a defect, or a store failure after a closure began changing stops a binding"
  - key: PoolIsWhatTheHeapLeaves
    statement: "The pool is the heap limit less the memtable's worst case and a reserve for queries, caches and requests, 2 GiB by default as measured at the small-machine settings; the closures and the memtable never pass the heap limit less the reserve, and the reserve is not a bound on queries, which MaxConcurrentReads times MaxQueryMemory can exceed"
  - key: MarginAboveTheMeasuredCosts
    statement: "the budget counts at least a ninth more than a build needs, so a build finishes under the heap before it and 0.9 of its budget's peak; the nightly holds REC S and REC M to that, under both profiles, per graph and cross-graph"
---

The rulings of [ADR 0195](../adr/0195-the-inference-projection.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 2 (#171) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
