---
set: promotion-across-copies
namespace: varve
adr: 0185
decisions:
  - key: PromotionIsAnOperatorsAct
    statement: "Nothing in Varve promotes a replica on its own: a promotion is a request with the operator role, naming the source and the position the operator expects"
  - key: TheLineageRecordSaysWhatACopyIs
    statement: "The lineage record beside the writer record holds the state replica or promoted, the source's store, prefix, dataset and fleet, once promoted the epoch, position, header hash and fence slot, and for a copy of a promoted copy the chain of every earlier source with its promotion, oldest first, so that which fleet wrote which range of positions is read from the record alone; a format this build does not read, an unknown state, or a chain out of order is a replica and is never written on"
  - key: AReplicaIsNeverOpenedForWrite
    statement: "An open for write of a replica is dataset-is-replica; a placed member follows it without claiming and without entering the hold-off; a write sent to it is 409 dataset-is-replica, decided after authorisation and naming only the dataset the caller sent"
  - key: PromotionIsAnsweredByAWarmMemberOfTheTargetFleet
    statement: "PUT …/state with promote is answered by the instance that receives it, a member of the writer set following the replica, whatever the hold-off: it marks the lineage promoted, claims in place with trigger promotion one above the highest of the record's epoch, every adopted chunk's and the catalog wrapper's, fences, and as its last write records in the lineage the position and hash at the fence and the fence slot; chunks a shipper created before the fence are kept and are the source fleet's; a finished promotion answers a second request not-a-replica; where no fleet serves the target, varve promote --embedded does the same and releases as any writer that closes, so a writer set starts as after any ordinary release"
  - key: AnIncompletePromotionIsCompletedOnlyByPromoting
    statement: "A promotion is finished when its lineage names a fence; one that fails before that releases with promotion, never storage-error and never as an ordinary release, so no standby races for it, and is promotion-incomplete: it refuses a write-open, and a write sent to it, until the promotion is run again naming the position first recorded, which is idempotent; a fence that landed in a segment a shipper consolidated meanwhile is not at the end of the log and does not finish the promotion"
  - key: ADatasetIsDeclaredACopy
    statement: "A dataset's graph may say varve:copyOf a varve:ShippingSource, a store and a prefix; the fleet never creates a dataset under such a declaration, its placed member writes nothing and readiness says it waits for the first shipping round, both sides may be declared in one apply, and a copy is not shipped on; an apply that declares a copy of a dataset the answering instance can see is written in this fleet, with no lineage naming that source, is refused as declaration-invalid, and where the declaration is applied all the same the running writer keeps writing and readiness reports copy-declared-on-a-written-dataset"
  - key: AReplicaIsFollowedAsAStandby
    statement: "The instance that would write a replica, a member of its writer set or its placed instance, follows it as a standby tailing on the interval, with or without automatic handover; any other instance follows it on demand"
  - key: ALostPromotionIsEpochMoved
    statement: "A promotion that finds the lineage or the writer record changed between its read and its write is refused as takeover-refused with the reason epoch-moved, and is not retried"
  - key: TheReturningPrimaryIsRefused
    statement: "The old primary's shipper stops with replica-promoted when it reads the lineage, and with shipping-diverged when it raced the promotion: at the fence slot, which is the one slot a shipper's next create and the fence can both want, since a shipper seals a segment through that slot before it creates a consolidated segment; a promoted copy takes no write from its old source, and the old primary's later commits are in no copy"
  - key: AManagementPositionBelongsToItsFleet
    statement: "The management position a commit records is a position in the system dataset of the fleet that wrote it: positions of a replica's commits and of commits at or below a promoted position are foreign, each range of the lineage's chain to its own fleet, and authorisation never reads them; the source fleet's system dataset, and every earlier fleet's with a promoted copy shipped onward, is shipped, followed read-only, never consulted for authorisation, and answers the events and audit of foreign commits"
---

The rulings of [ADR 0185](../adr/0185-promotion-across-copies-and-the-lineage-record.md),
filed unaccepted by Replication part 2 (#166) (ADR 0066).
