---
set: writer-handover-within-a-store
namespace: varve
adr: 0179
decisions:
  - key: HandoverAndPromotionAreTwoActs
    statement: "A handover changes a dataset's writer while its log stays the same objects on the same store, and may be automatic; a promotion makes another copy authoritative and is always an operator's act"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: TwoTriggersOnly
    statement: "A handover is triggered by the writer's abdication or by a takeover request after the orchestrator's probe failure, each naming the epoch it is about; load signals, a client's failure to reach the writer and elapsed time never trigger one"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: AbdicateOnStorageOrLocalFaults
    statement: "A writer abdicates by releasing its epoch for shutdown, placement, a failed batch against the store, or local spills failing once, at a commit, the memtable passes four times MemtableLimit or the memory load reaches the GC's high-load threshold, whichever comes first; a failed derived-cache write degrades and never abdicates, and a fault deterministic over the log fails the commit or the dataset and the writer stays"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: TakeoverAnsweredByTheStandby
    statement: "PUT /manage/datasets/{d}/state with a takeover naming epoch n, under an operator token, is answered by the instance that receives it, which claims n+1 only if the record still shows n, and is refused 409 takeover-refused with handover-off, not-in-writer-set, cold or epoch-moved otherwise"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: OnlyWarmStandbysTakeOver
    statement: "Automatic handover is opt-in per dataset, and only a warm member of its writer set takes it over; without one the orchestrator's restart of the one writer is the recovery"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ClaimInPlace
    statement: "A warm standby claims, then takes a write-open with that claim, adopts and fences above its head, and becomes the writer with its projection and cache, equal at every position after the claim to a re-open from the catalog"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: SupersededRejoinsAsFollower
    statement: "On a dataset with automatic handover a superseded writer fails its batch with 503 writer-superseded and re-opens as a follower, eligible to claim again only once warm; on any other dataset ADR 0136's superseded state stands"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: SteppedDownAnswers
    statement: "A writer that abdicated answers the requests in flight, and later direct ones until it has re-opened as a follower, with 503 writer-stepped-down"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ForwardAcrossAHandover
    statement: "A forward refused by a superseded or stepped-down writer is answered 503 writer-moved with the original as reason and the route marked stale; the directory re-reads a record when an advance adopts a higher epoch, and no NoWriter route is cached"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: EveryHandoverIsVisible
    statement: "Each claim is in the writer record's epoch history with its trigger, restart, operator, abdication or probe, its fence and its release reason; the state resource reports role, epoch and warm; no commit marks a handover"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: SystemDatasetHandsOver
    statement: "With Varve:Fleet:AutomaticHandover the system dataset has a writer set named by Varve:Fleet:Writer, under the same rules and route"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
---

The rulings of [ADR 0179](../adr/0179-writer-handover-within-a-store.md),
one line each. Filed unaccepted by Replication part 1, #165 (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
