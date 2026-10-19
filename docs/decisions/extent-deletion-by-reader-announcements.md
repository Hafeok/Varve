---
set: extent-deletion-by-reader-announcements
namespace: varve
adr: 0160
decisions:
  - key: DeletionWaitsForExtentMaintenance
    statement: "An extent set no catalog names is deleted from an object store only by the deletion rule of reader announcements; a directory, with one process, deletes it once no view holds it"
  - key: CatalogVersionIsTheSetSequence
    statement: "A catalog's version is its next set sequence, which every swap advances, so a set with sequence s is named only by versions after s"
  - key: EveryInstanceAnnouncesAnInterval
    statement: "Every instance that reads extents from the store, the writer included, keeps a reader record announcing the oldest and the newest catalog versions it holds, replaced only when that interval changes"
  - key: AnnounceThenConfirm
    statement: "A reader announces a catalog version, then reads the catalog again, and reads no extent of it until the catalog it reads is the one it announced; a writer announces a version before swapping to it"
  - key: SwapThenReadRecordsThenDelete
    statement: "The writer deletes a set only after the swap that removed it, and only after reading the reader records after that swap"
  - key: DeleteWhenNoRecordOverlaps
    statement: "The writer deletes set s, removed at version r, only when no present record's interval [oldest, newest] meets the open interval (s, r)"
  - key: ADeadRecordHoldsUntilAnEvent
    statement: "A dead reader's record holds deletion back until an event removes it: the same instance restarting and replacing it, or the operator's forget; never a timer"
  - key: MaintenanceMayPoll
    statement: "The writer may read the records in a maintenance round, because when it reads them decides only how soon a set is deleted, never whether deleting it is safe"
  - key: AForgottenReaderFailsAndReattaches
    statement: "A reader whose record was forgotten fails the reads that find an extent gone with reader-forgotten and attaches again; it never returns a wrong result"
  - key: AnUnannouncedReadFailsOnAGoneExtent
    statement: "A read opened explicitly unannounced that finds an extent gone fails with read-unannounced-extent-gone, never with the damage error and never with a wrong result"
  - key: AnnouncingIsRequired
    statement: "An open whose reader record cannot be written fails and names the role it needs, unless the caller asked explicitly for an unannounced read"
  - key: UnknownRecordVersionHoldsEverything
    statement: "A reader record of a format version this build does not know holds every catalog version; it is never ignored"
  - key: ReaderRecordIsCoordinationState
    statement: "The reader record and the writer record are coordination state, read forever from the release that writes them; derived formats are rebuilt, not read forever"
---

The rulings of [ADR 0160](../adr/0160-extent-deletion-by-reader-announcements.md),
filed unaccepted by milestone Extent maintenance of #110 (ADR 0066).
`DeletionWaitsForExtentMaintenance` moved here from ADR 0139's set, which this
ADR supersedes in that part.
