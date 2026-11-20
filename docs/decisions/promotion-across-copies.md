---
set: promotion-across-copies
namespace: varve
adr: 0185
decisions:
  - key: PromotionIsAnOperatorsAct
    statement: "Nothing in Varve promotes a replica on its own: a promotion is a request with the operator role, naming the source and the position the operator expects"
  - key: TheLineageRecordSaysWhatACopyIs
    statement: "The lineage record beside the writer record holds the state replica or promoted, the source's store, prefix, dataset and fleet, and once promoted the epoch, position, header hash and fence slot; a format this build does not read, or an unknown state, is a replica and is never written on"
  - key: AReplicaIsNeverOpenedForWrite
    statement: "An open for write of a replica is dataset-is-replica; a placed member follows it without claiming and without entering the hold-off; a write sent to it is 409 dataset-is-replica, decided after authorisation and naming only the dataset the caller sent"
  - key: PromotionIsAnsweredByAWarmMemberOfTheTargetFleet
    statement: "PUT …/state with promote is answered by the instance that receives it, a member of the writer set following the replica, whatever the hold-off: it marks the lineage promoted, claims in place with trigger promotion one above the highest of the record's epoch, every adopted chunk's and the catalog wrapper's, and fences; where no fleet serves the target, varve promote --embedded does the same and releases with the reason promotion"
  - key: AnIncompletePromotionIsCompletedOnlyByPromoting
    statement: "A promotion that fails after winning the record releases with promotion, never storage-error, and a promoted lineage with no fenced promotion claim is promotion-incomplete: it refuses a write-open, and a write sent to it, until the promotion is run again, which is idempotent and keeps the first promotion's lineage"
  - key: ADatasetIsDeclaredACopy
    statement: "A dataset's graph may say varve:copyOf a varve:ShippingSource, a store and a prefix; the fleet never creates a dataset under such a declaration, its placed member writes nothing and readiness says it waits for the first shipping round, both sides may be declared in one apply, and a copy is not shipped on"
  - key: AReplicaIsFollowedAsAStandby
    statement: "The instance that would write a replica, a member of its writer set or its placed instance, follows it as a standby tailing on the interval, with or without automatic handover; any other instance follows it on demand"
  - key: ALostPromotionIsEpochMoved
    statement: "A promotion that finds the lineage or the writer record changed between its read and its write is refused as takeover-refused with the reason epoch-moved, and is not retried"
  - key: TheReturningPrimaryIsRefused
    statement: "The old primary's shipper stops with replica-promoted when it reads the lineage, and with shipping-diverged when it raced the promotion at the fence slot; a promoted copy takes no write from its old source"
  - key: AManagementPositionBelongsToItsFleet
    statement: "The management position a commit records is a position in the system dataset of the fleet that wrote it: positions of a replica's commits and of commits at or below a promoted position are foreign, and authorisation never reads them; the source fleet's system dataset is shipped, followed read-only, never consulted for authorisation, and answers the events and audit of foreign commits"
---

The rulings of [ADR 0185](../adr/0185-promotion-across-copies-and-the-lineage-record.md),
filed unaccepted by Replication part 2 (#166) (ADR 0066).
