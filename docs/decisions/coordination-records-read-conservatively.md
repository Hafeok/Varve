---
set: coordination-records-read-conservatively
namespace: varve
adr: 0217
decisions:
  - key: AnUnknownWriterRecordFormatIsNeverWritten
    statement: "A writer record of a format this version does not read is never written on: an open for write and a takeover refuse with WriterRecordFormatUnknownException, and a read-only open goes on as a follower that knows no writer and forwards to nobody, while the log reads"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: AnUnknownValueIsHeldNeverActedOn
    statement: "A trigger or a release reason a writer record of a known format names and this version does not know is held as Unknown with its text kept, written back as it was read, and never acted on: an unknown release is a release, and no placement or other reason is inferred from it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: AReaderRecordOfAnUnknownFormatHoldsEverything
    statement: "A reader record of a format this version does not read, or one that does not read, holds every catalog version and derived format 4, so the deletion rule keeps everything it could need"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: TheLineageRecordFollowsTheSameRule
    statement: "Replication's lineage record, when it lands, is read by the same rule: a format this version does not read is never written on, and an unknown value is held and never acted on"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
---

The rulings of [ADR 0217](../adr/0217-coordination-records-read-conservatively.md),
filed unaccepted by 1.0 readiness A for #129 (ADR 0066).
