---
set: the-management-position-on-the-commit
namespace: varve
adr: 0165
decisions:
  - key: ManagementPositionIsAnAttachment
    statement: "A commit made through a fleet's server carries its authorising management position as the attachment typed literal varve:managementPosition, with no byte of format v1 changed"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: HostAttachmentsOnTheCommit
    statement: "CommitMetadata.Attachments carries terms the host attaches, resolved as the agent and cause are and recorded before the validators' attachments"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: ManagementPositionsNeverDecrease
    statement: "A dataset's writer refuses a write whose applied management position is below the highest its log records, after advancing the system follower once, so the recorded positions never decrease along a log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: NoAuditIndex
    statement: "Whether a write was allowed is answered from the commit's attachment and the grants as of that position; there is no derived audit index"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
---

The rulings of [ADR 0165](../adr/0165-the-management-position-on-the-commit.md), one line each.
Filed unaccepted by milestone Management plane of #105 (ADR 0066); every citation is `CS0618`
until the maintainer accepts them.
