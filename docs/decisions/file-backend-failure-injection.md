---
set: file-backend-failure-injection
namespace: varve
adr: 0214
decisions:
  - key: TornAtEveryByteOfEveryWrite
    statement: "Under the process model every write the file backend issues, to the log, to derived/ and to its small files, is cut at every byte, and the store recovers each image to a closed commit no earlier than the last acknowledged"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: EverySectorCutEnumerated
    statement: "Under the power-loss model every unflushed write at every operation is cut at every 512-byte sector boundary in turn, the others once kept whole and once dropped, the metadata once to the last flush and once whole, enumerated rather than sampled"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: FullDiskFailsTheCommitAndTheNextWriteRecovers
    statement: "A full disk or exhausted quota at any write or flush of the log fails the commit that met it, which is not acknowledged, and leaves the dataset open for reads at its head; the next write, with no timer and no reopen by hand, recovers the writer as an open would, fencing off everything after the last commit closed and flushed with an abandoned trailer, and proceeds when the space is back or is refused storage-full when it is not; any other failed append, or a recovery whose log does not verify, waits for a reopen"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: AcknowledgedOnlyWhenTheBatchIsDurable
    statement: "Committed means closed and flushed: no member of a batch is acknowledged before a flush that returned covers its closing record; when the batch's write fails, the writer flushes once unless a flush is what failed, and each member is answered by the log, committed when closed and flushed, not known when closed and not confirmed flushed, and refused when never closed, so that the committed members are a prefix of the batch in order; moved from ADR 0124 by ADR 0214's supersession in part"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ClosedAndUnflushedIsNotKnown
    statement: "A member closed in the log whose flush did not return, or returned after an earlier flush failed, is answered outcome unknown with its position, never refused, since its record may survive; it is settled through the commits resource once the head reaches that position, by whether the commit there carries the request's cause; on a full disk the writer's recovery fences it off before the head moves"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: AnOpenFlushesWhatItAdopts
    statement: "An open on a backend that flushes with fsync flushes the log's newest open segment before it builds anything on what it found there, so that a closed commit whose flush failed in an earlier process is durable before a commit index, a checkpoint or an answer depends on it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: TermsReadBackAfterAFailedAppend
    statement: "After a failed append, full disk or not, followed by the next write's recovery or by a reopen, every term committed before the failure and after it reads back as itself: the term dictionary forgets the ids a failed batch staged before the next staging"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: StorageFullIs507
    statement: "A write refused because the storage had no space, never closed in the log, is answered 507 with the problem type storage-full, and readiness reports the dataset's writes as refused for lack of space under writesRefused without becoming unready"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: OutcomeUnknownIs503WithTheRequestId
    statement: "A write whose outcome is not known is answered 503 with the problem type commit-outcome-unknown, never 507: when the storage did not confirm its flush, carrying the request id, which is the commit's cause, and the position to look at in the commits resource; and the same type, without a position, for a forwarded write whose answer from the writer was lost"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: KilledAtEveryFlushOnRealFiles
    statement: "A real process running the workload on the real file system is killed just before and just after every flush, and the store opened on what it left recovers every commit it acknowledged and nothing past a closed commit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: PowerCutOnRealFilesUnderTheModels
    statement: "A power cut on real files is the image a mirrored simulated file system gives under ADR 0073's power-loss and lost-entries models, written to the disk and opened through DiskFileSystem"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: RunInTheReleaseGateAndNightly
    statement: "The faults run at every point in release-gate.yml, which release.yml calls at the commit under release and which a release must pass, and in the nightly's store job; a pull request runs a seeded share of each"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
---

The rulings of [ADR 0214](../adr/0214-file-backend-failure-injection.md),
filed unaccepted by 1.0 readiness A for #129 (ADR 0066).
`AcknowledgedOnlyWhenTheBatchIsDurable` moves here from ADR 0124's set under
its key, restated (ADR 0068 point 4).
