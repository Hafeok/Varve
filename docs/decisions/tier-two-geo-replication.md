---
set: tier-two-geo-replication
namespace: varve
adr: 0186
decisions:
  - key: TierTwoIsTheStoresOwnReplication
    statement: "Tier 2 is one dataset in one Azure Blob Storage account that the provider replicates to a second region; Varve writes nothing there and runs no shipper, target or second fleet for it"
  - key: ASecondaryFollowerTakesOnlyWhatIsWholeAndInOrder
    statement: "A follower at the account's secondary endpoint reads unannounced, adopts no chunk after the first absent slot, treats a consolidated segment that is not there yet as not yet, attaches a catalog only when every set it names is whole, and is never in a writer set"
  - key: GeoLagIsTheAccountsLastSyncTime
    statement: "Readiness reports the secondary's status and last sync time exactly as the account returns them, per instance; Varve computes no interval from it, compares no clock with it, reads it on no timer of its own, and decides nothing by it"
  - key: AFailedOverAccountIsNotOpenedForWrite
    statement: "A claim records whether the account was geo-redundant, read from the data plane; where the newest claim says it was and the account no longer is, an open for write, a takeover and a claim in place refuse with account-failed-over before anything is written, and a dataset whose claims never said so is not asked"
  - key: PromotionAfterAFailoverIsOnlyAnOperatorsRequest
    statement: "Nothing promotes on a failover by itself: varve promote --from-failover, a request or embedded, adopts the longest prefix of the log that verifies, says what lies beyond the first gap and asks before it goes on, and is refused not-failed-over where the account has not failed over and the log has no hole"
  - key: WhatLiesBeyondTheFirstGapIsMovedAndNeverDeleted
    statement: "A promotion after a failover moves every object beyond the first gap to discarded/{epoch}/ under the dataset by a create and then a delete, drops a catalog that is ahead of the log or names a set that is not whole, and claims above the epoch of every chunk kept and discarded"
  - key: TheLineageSaysWhatWasDiscarded
    statement: "Each promotion after a failover is an item of the lineage's promoted.failovers with its epoch, position, hash, fence and what was discarded; a dataset that was never a copy has a lineage with no source, and an item with no fence has not finished"
  - key: ThePromoteAnswerCountsWhatWasLost
    statement: "The answer of a promotion after a failover states in-band the first position not kept and how many commits the discarded chunks held from there, says the count is a lower bound, and says so when no discarded chunk could be read to a position"
  - key: AStrayWriterIsFencedByTheOperator
    statement: "A writer still running against the lost region is past every slot the store holds and is not stopped by the fence; the operator stops the first region's instances or removes their write role before promoting, the command asks them to confirm it, and what a stray writer acknowledged is discarded and counted"
  - key: TheGeoLayoutIsTwoRegionsBehindFrontDoor
    statement: "The geo layout is one RA-GRS account, a writing fleet in the first region, followers at the secondary in the second, Front Door sending every write to the first, and a custom role with only the two read-only actions the stats and the account information need"
  - key: TierTwoIsVerifiedOnARealAccountOrNotAtAll
    statement: "A geo double and an emulator's secondary endpoint gate the code, and tier 2 is called verified only by the real-account leg, restricted to main and without a key; until it has run, the release notes and the guide say that it has not"
---

The rulings of [ADR 0186](../adr/0186-tier-two-the-stores-own-geo-replication.md),
filed unaccepted by Replication part 2 (#166) (ADR 0066).
