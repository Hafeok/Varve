---
set: the-extent-merge-policy
namespace: varve
adr: 0159
decisions:
  - key: SizeTieredWithAFanOut
    statement: "The oldest run of F adjacent sets below half the cap whose largest is at most F times their smallest merges into one; past the bound, the adjacent run of up to F with the fewest bytes merges"
  - key: TheSetIsTheUnitOfMerge
    statement: "A set's six order extents share one window in the catalog and merge together, so the bound on sets per permutation is the bound on sets"
  - key: BoundOnSetsPerPermutation
    statement: "The policy keeps at most (F-1)(T+1) sets below half the cap, T the tier of half the cap, plus the sets past half the cap, plus one merge in flight"
  - key: MaxSetBytesCapsAMerge
    statement: "A set past half of MaxSetBytes is never merged again, and no merge produces a set past MaxSetBytes, so an as-of read inside a set costs at most a capped set"
  - key: MergesTriggeredByTheCatalog
    statement: "A merge is considered only when the catalog changes, at a checkpoint, a drop or another merge; never from elapsed time"
  - key: PolicyConfigurableWithinLimits
    statement: "The fan-out is 2 to 16, 4 by default; MaxSetBytes is 1 MiB or more; merges can be turned off; the default holds the read latency gate"
---

The rulings of [ADR 0159](../adr/0159-the-extent-merge-policy.md), filed
unaccepted by milestone Extent maintenance of #110 (ADR 0066).
