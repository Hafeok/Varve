---
set: verified-derived-data
namespace: varve
adr: 0213
decisions:
  - key: OneCheckPerBlock
    statement: "Every checked derived kind carries format 5's 64-bit block check for each of its blocks in its directory, which the header's SHA-256 covers; no other hash is used for cached derived data"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: VerifiedBeforeFirstUse
    statement: "A derived kind read whole is verified whole when it is loaded, and a kind read by range is verified per block the first time that block is read, once per process while it stays held"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: EveryBlockIsChecked
    statement: "Every block of a checked derived blob is checked when it is read, and a block that does not match is a cache miss, never a failed read and never a different answer; superseding ADR 0177 point 6 in part"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: DamageMidReadIsRerunAndResumed
    statement: "A mismatch found mid-read rebuilds the damaged run from the log for its window, in memory and in place, so a scan carries on where it was; a read that seeks a set re-runs over the runs below it and the log, resuming after the quads it yielded; the writer writes a damaged set again from the log; a second damage degrades the same way, down to the log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: Format5ForExtentsOnly
    statement: "Derived version 5 is an order extent's alone; version 6 is the checked kinds' (runs, checkpoints, the commit index, the dictionary extent and the checked envelope), and the state, the graph-version index and the catalog are still version 4; superseding ADR 0177 point 5 in part"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: GraphIndexLookupsVerifyTheDirectory
    statement: "A graph-version index is looked up only in its directory read whole and verified against the header's SHA-256 of it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ASpillDamagedFailsTheLoadUncommitted
    statement: "A spill carries a check per block, and a mismatch ends its bulk load before the commit with nothing visible, as BulkLoadSpillDamagedException and the problem type load-spill-damaged, so the caller knows a resubmit is right"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ProjectionsOutsideTheStoreWriteACheckedEnvelope
    statement: "A projection kept in the local cache by another package writes and opens its blob through Dataset.WriteCheckedAsync and Dataset.TryReadCheckedAsync, kind 9: 64 KiB blocks each checked, bound to the dataset and to the position and header hash it was built at; any failure to verify or to decode is a miss"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: CommitIndexBesideEachSet
    statement: "Each extent set carries the commit index of its window as a ninth blob, commits, of kind 4 in the newest commit-index layout every announced reader reads; a merged set's is its inputs' entries end to end; superseding ADR 0161 point 1 in part"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: NoDerivedFormatChange
    statement: "The shared commit index is written as derived version 6, with a check per block of 128 entries, when every announced reader reads 6, and as version 4 otherwise; superseding ADR 0161 point 3"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ReaderRecordsStateMaxDerived
    statement: "A reader record carries maxDerived, the newest derived format its reader reads, optional in record format 1 and read as 4 when absent; every record this build writes says 6; superseding ADR 0178 point 2 in part"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: SharedBlobsInTheNewestVersionEveryReaderReads
    statement: "A set's dictionary and commits blobs are written in the newest derived version every present reader record reads, so a follower of an older build still attaches"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: OlderReadersDecideRewrites
    statement: "While a present reader record's maxDerived is below the newest derived version, the writer rewrites no set whose blobs are older, and a merge, a drop's merge or a split writes the version every present reader reads; superseding ADR 0178 point 2 in part"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: MaintenanceRewritesFormat4Sets
    statement: "When no older reader is present and the merge policy has nothing due, the writer's maintenance rewrites the oldest set with any blob below the newest derived version alone, a merge of one input under ADR 0158's rules; with merges off nothing is rewritten; superseding ADR 0178 point 2 in part"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: OlderLocalBlobsAreAMiss
    statement: "A local-only derived blob below version 6 of a checked kind is a cache miss at open and is rebuilt; a set's older blobs are read unchecked until maintenance rewrites them, and readiness's retention reports how many sets remain"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: FlushBeforeVisible
    statement: "A derived blob is flushed before it is renamed into place unless it is written in a self-verifying format that every announced reader reads, a segment's header before its first record, and the manifest before any segment exists; the rename is always kept; superseding ADR 0073 in part"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: BlobsArePublishedAtomically
    statement: "A derived blob is written through an IBlobWriter and becomes visible only when published, atomically replacing any blob of that name and durable as the backend declares unless the blob verifies itself for every announced reader; an unpublished writer leaves nothing; superseding ADR 0071 in part"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
---

The rulings of [ADR 0213](../adr/0213-cached-derived-data-is-verified-before-use.md),
filed unaccepted by 1.0 readiness A for #146 (ADR 0066). Nine of them move
here from the sets of ADRs 0071, 0073, 0161, 0177 and 0178, under the same
keys, restated (ADR 0068 point 4).
