---
set: log-shipping
namespace: varve
adr: 0184
decisions:
  - key: AShippingTargetIsADeclaration
    statement: "A dataset's graph in the system dataset may name shipping targets with varve:shipsTo, each a store and a prefix, and anything else is refused at apply; the source is always an object store, and a dataset on the file backend cannot be shipped"
  - key: TheShipperRunsOffTheSequencer
    statement: "The shipper is a role on the epoch holder, or on the instance varve:shippedOn names, over a read-only open of the source of its own; no commit waits for a target, no timer decides anything, and two shippers at once are harmless"
  - key: NothingOnATargetIsOverwritten
    statement: "Every log object is written to a target by create-if-absent of bytes identical to the source's; a name taken by other bytes is shipping-diverged, naming the slot, and the shipper stops for that target; a source chunk whose header or content hash fails is not shipped, and readiness says source-chunk-damaged"
  - key: ShippedInOrderTheCatalogLast
    statement: "A round ships the lineage record, the manifest, log objects in slot order with the target's consolidation after its .seg, the extent sets the catalog names, and the catalog last, only when every set it names is on the target and its cursor is at or below the shipped tail; a target advances only through its next chunk slot, so a segment the source consolidated before its end was shipped is first sealed there by the source's kept fence chunks and one chunk of the rest of its bytes, and its .seg is created only then; the writer record, reader records and probes are not shipped"
  - key: ASetIsVerifiedThenCopiedAsItIs
    statement: "The shipper verifies each blob of a set whole before it creates it, as a reader verifies the blocks it reads, ships no damaged blob and does not publish the catalog that names its set, with readiness saying waiting-on-damaged-set while the log is shipped as before; it never rewrites a set on a target: a rewrite is the source writer's and reaches the target as a new set"
  - key: ResumeIsByTheTargetsTail
    statement: "The shipper keeps no state: it lists the target's log once, verifies each segment the target still holds as chunks, and compares the tail with the source at the same slot; a match continues and anything else is shipping-diverged, a broken chunk of the target included; across a segment boundary the commit header chain is the log reader's check, run by every follower of the target and by a promotion"
  - key: TheShipperIsAReaderAndAMaintenanceWriter
    statement: "The shipper announces one reader record per target on the source, holding the catalog version it copies until the target's catalog is replaced, and on the target it consolidates chunks and deletes a retired set only when no reader record of the target holds it and the source has deleted it, without ever opening the target as a dataset"
  - key: ATargetIsAVerifiedStore
    statement: "A target store needs a verification record of an accepted suite at its root, or the shipper writes nothing to it and readiness says store-unverified with what to run; the compatibility page is a notice on an apply and never a refusal; a prefix that holds another dataset is shipping-target-not-empty"
  - key: ATargetsFailureIsNeverTheSources
    statement: "A target that fails makes the shipper back off for that target alone: it never abdicates, releases or touches the source's writer record, never makes the source checkpoint, and readiness reports the shipped log position and the shipped catalog's position without turning unready"
  - key: ACopyIsGovernedWhereItIsServed
    statement: "Shipping copies bytes, so every commit keeps its agent and attachments; what governs a read of a copy is the declarations of the fleet that serves it, none of which is shipped as governing access; a source fleet's system dataset, and each earlier fleet's where the dataset is a promoted copy, is shipped ahead of the dataset commits that refer to it; a fleet that ships has its system dataset on object storage"
  - key: ErasureAcrossCopiesIsOpen
    statement: "Before erasure mode and a shipping target may be combined, milestone 9 part 2 must either have the copies share the key store's destruction or refuse the target; a key store per copy meets neither, and the choice is #203's"
---

The rulings of [ADR 0184](../adr/0184-log-shipping.md), filed unaccepted by
Replication part 2 (#166) (ADR 0066).
