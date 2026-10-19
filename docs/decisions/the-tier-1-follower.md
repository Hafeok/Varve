---
set: the-tier-1-follower
namespace: varve
adr: 0164
decisions:
  - key: TailingStorageContract
    statement: "ITailingStorage.RefreshAsync learns and adopts what the writer appended since the last refresh, with the writer's own chunk checks, and a chunk that does not verify is an error, never skipped"
  - key: FollowerIsAnOrdinaryDataset
    statement: "Dataset.OpenFollowerAsync opens a dataset whose AdvanceAsync applies the commits closed since its head, verified as an open verifies them; reads work as on any open, and every write and checkpoint is refused"
  - key: ProbeTheNextSlot
    statement: "A refresh GETs the next deterministic slot, and a seal moves the probe to the next segment; a 404 is nothing new once the probed segment's consolidated object is also absent, so an idle refresh is two GETs and a refresh never lists"
  - key: FollowOnDemand
    statement: "A follower advances when a read arrives after Varve:Fleet:FollowInterval since its last advance, or on a minimum-position header above it; only the system dataset is advanced on the interval, so an idle dataset makes no requests"
  - key: FollowerReopensFromTheCatalog
    statement: "A follower that has applied more than MemtableLimit quads since its open is re-opened from the writer's newest catalog, reads pinned to the old one ending on it"
  - key: ReaderContractHook
    statement: "A follower reports each catalog version it holds through one internal call, a no-op until Extent maintenance's reader contract lands"
---

The rulings of [ADR 0164](../adr/0164-the-tier-1-follower.md), one line each. Filed unaccepted
by milestone Management plane of #105 (ADR 0066); every citation is `CS0618` until the
maintainer accepts them.
