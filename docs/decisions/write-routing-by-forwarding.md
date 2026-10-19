---
set: write-routing-by-forwarding
namespace: varve
adr: 0168
decisions:
  - key: AnyInstanceAcceptsAWrite
    statement: "An instance that is not the writer of a dataset, or of the system dataset for a management write, forwards the write to the writer's internal address; reads never forward"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: ForwarderAuthenticatesFirst
    statement: "The forwarding instance validates the bearer token locally and refuses an unauthenticated request; authorisation, attribution and evaluation are the writer's, which authenticates the same token again"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: ForwardedUnchanged
    statement: "The method, path, query, body and listed headers are forwarded unchanged with the hop header Varve-Forwarded, the body streamed and read to completion, and the writer's response streamed back unchanged"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: HopRule
    statement: "A request carrying Varve-Forwarded that reaches a non-writer is 503 writer-moved with the current epoch and is never forwarded again; a forward that cannot connect is 503 unavailable with reason writer-unreachable and its body is never sent twice"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: WriterDirectorySeam
    statement: "IWriterDirectory answers a dataset's route, local, remote with address and epoch, or no writer; the server's implementation reads the writer records again on writer-moved, a failed connect or a newer epoch, and never expires a route by time"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: WriterRecordNamesTheAddress
    statement: "Each claim in the writer record carries the claiming instance's internal address as an optional member, the record staying format 1"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: CauseIsTheRequestId
    statement: "The commit cause is the request id, the instance name, a colon and the request's TraceIdentifier, as an xsd:string literal, echoed as Varve-Request-Id; a forwarded write's id is the writer's"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: MinPositionOnDataReads
    statement: "A data read with Varve-Min-Position above the follower's applied position advances it once and is 503 dataset-behind if it is still behind"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: OneReplicaAtMost
    statement: "Container Apps runs two apps from one image, varve-writer with one replica that claims and varve with N replicas that never claim and forward writes to varve-writer's internal FQDN"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
---

The rulings of [ADR 0168](../adr/0168-write-routing-by-forwarding.md), one line each. Filed
unaccepted by milestone Management plane of #105 (ADR 0066); every citation is `CS0618` until the
maintainer accepts them.

Moved here, superseded with their keys: `CauseIsTheRequestId` (0094) and `OneReplicaAtMost` (0142).
