# 0168 — Write routing by forwarding: the writer directory, the hop rule and the internal address

## Status

**Accepted — filed unaccepted by milestone Management plane of #105,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on 2026-10-19:
- write routing by forwarding, which is point 11 of ADR 0163 and supersedes
  the earlier `307` design;
- on the plan of #105:
  - the forwarding instance authenticates the caller before it forwards;
  - there is no peer identity;
  - request ids are unique across the fleet;
  - client idempotency keys are a separate issue.

**Supersedes:**
- in ADR [0094](0094-a-write-over-http-is-one-commit.md): `CauseIsTheRequestId`;
- in ADR [0142](0142-deployment-on-container-apps-and-small-machines.md):
  `OneReplicaAtMost`.

**Amends** ADR [0136](0136-one-writer-by-an-epoch-and-a-log-slot-fence.md):
the record gains an address, which is additive.

## Context

A fleet is active-active by dataset (ADR 0163). Every instance serves reads,
as a writer or a follower (ADR 0164), but only one instance writes each
dataset.

A redirect assumes every instance has an address clients can reach. Behind a
load balancer of identical replicas that is not true, and on Azure Container
Apps it is not true at all, because Container Apps cannot route to a chosen
replica.

## Decision

1. **Any instance accepts a write.** An instance that is not the writer of the
   dataset, or of the system dataset for a management write, forwards the
   request to the writer's internal address. This covers:
   - SPARQL Update;
   - Graph Store `PUT`, `POST` and `DELETE`;
   - every management `PUT`, `PATCH`, `POST` and `DELETE`.

   Reads never forward.
2. **The forwarding instance authenticates first.**
   - It validates the bearer token locally, as for any request, and refuses an
     unauthenticated request at the edge.
   - It neither authorises nor parses the request.
   - Authorisation, attribution and evaluation are the writer's, which
     authenticates the same token again.
   - It keeps its rate limiter and its body limit.
3. **What is forwarded.**
   - **The request:** the method, path, query and body, unchanged. The body is
     streamed and read to completion, and a body that ends early is `400` with
     nothing forwarded as whole.
   - **Headers passed through:** `Authorization`, `Content-Type`,
     `Content-Length`, `If-Match`, `If-None-Match`, `Varve-If-Graphs-Match`,
     `Varve-Min-Management-Position` and `traceparent`.
   - **Header added:** the hop header `Varve-Forwarded: <instance>`.
   - **The response** is streamed back unchanged: status, headers and body,
     with `Varve-Position`, `ETag`, `Varve-Request-Id` and any problem.
   - **SPARQL Update is forwarded whole,** so its `WHERE` is evaluated at the
     writer, over the writer's pinned head (ADR 0057).
4. **The hop rule.**
   - A request carrying `Varve-Forwarded` that reaches an instance which is
     not the writer is answered `503 writer-moved`, a new problem type with
     members `epoch` and `requestId`. It is never forwarded again. So a
     request crosses at most one hop.
   - A forward that cannot connect is `503 unavailable` with reason
     `writer-unreachable`. A body whose bytes have started to go is never sent
     again.
5. **The writer directory.**
   - **The seam.** `Varve.Protocol` gains `IWriterDirectory`, which answers a
     `WriterRoute` for a dataset or for the system dataset. A route is
     `Local`, `Remote(address, epoch)`, or `NoWriter(epoch)`.
   - **The server's implementation** reads the writer records, keeps them per
     dataset by epoch, and learns a local claim without a read.
   - **When it reads a record again:** on `writer-moved`, on a failed connect,
     and when an advance sees a newer epoch.
   - **No route expires by time.** A stale route is caught by the hop rule.
6. **The internal address.**
   - **`Varve:Fleet:InternalUrl`** is required on an instance that may claim
     in a fleet of more than one. For example:
     - a pod's headless-service name;
     - `varve-writer`'s internal FQDN on Container Apps.
   - **It is recorded in the writer record at claim,** as the optional
     `address` of the claim's history entry. The record stays `format: 1`,
     because a reader of format 1 reads members by name and ignores one it
     does not know.
   - **The address must be HTTPS or stay inside a private network,** because
     the caller's token crosses this hop. The operator guide says so.
7. **Request ids are unique across the fleet.**
   - A request id is `<instance>:<TraceIdentifier>`, because two instances'
     connection counters can collide.
   - The writer mints its own id for a forwarded request, and the response
     carries it.
   - When a forwarded write commits and its response is lost on the way back,
     the client holds no id for it. The same is true of a direct write whose
     response is lost.
   - Client idempotency keys, scoped to the authenticated agent, are a
     separate issue. This ADR builds none.
8. **Read-your-writes.**
   - A write's response carries `Varve-Position` (ADR 0094).
   - A data read may send **`Varve-Min-Position: p`**. A follower below p
     advances once, and answers `503 dataset-behind`, with members `position`
     and `appliedPosition`, only if it is still behind.
9. **Deployment.** The code is the same everywhere; only the address
   differs.
   - **Kubernetes:** one StatefulSet of identical pods with `Writer: varve-0`,
     and a headless service that gives each pod its address.
   - **Azure Container Apps, the default** in `deploy/azure/` and
     `Varve.Aspire`:
     - two apps from one image;
     - `varve-writer` has one replica, internal ingress and `Writer: true`;
     - `varve` has N replicas, the public ingress and `Writer: false`, and
       forwards to `varve-writer`'s internal FQDN;
     - the writer recovers by the orchestrator's restart.
   - **Automatic handover:** N single-replica apps behind Front Door or
     Application Gateway, which Replication builds. The operator guide
     describes the shape.
   - **A single machine or the file backend** is a fleet of one, with nothing
     to route.

## Alternatives considered

- **`307` to the writer.** Behind identical replicas, and on Container Apps,
  no instance address is reachable by the client.
- **A peer identity, so that the writer adopts the forwarder's request id.**
  Refused by the maintainer: a forwarded id is forgeable without one, and
  idempotency keys answer the lost-response case for direct and forwarded
  writes alike.
- **The non-writer authenticates nothing.** An unauthenticated flood would be
  forwarded to the writer.
- **Raft, or the store as a request inbox.** ADR 0163's alternatives.

## Consequences

- **Problem types:** `writer-moved` and `dataset-behind` are new, and
  `unavailable` gains the reason `writer-unreachable`.
- **What Replication (#97) reuses unchanged:** `IWriterDirectory`, the
  forwarder, the hop rule and the address. It adds invalidation on a handover.
- **The tests:**
  - two Kestrel hosts on loopback, with bodies sent in small delayed chunks;
  - a property over generated schedules that change the writer during a
    forward: every acknowledged write is in the log once, and no request is
    forwarded twice;
  - the fleet suite on Azurite and RustFS, with writes sent to the non-writer.

## Checks

- **Checked against the accepted ADRs** (0001–0147). It supersedes the two
  rulings listed under Status, and touches:
  - **0037** (the token authenticated at both ends);
  - **0057** (`WHERE` at the writer);
  - **0119** (problem types);
  - **0136** (the address, additive; a dated block).

  No other conflict.
- **Layer ownership.**
  - `Varve.Protocol` (5): the seam, the forwarder, the hop rule and the
    headers.
  - `Varve.Store.ObjectStorage` (5): the record's address.
  - `Varve.Server` (6): the directory and the configuration.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-27 — the directory across a handover

Filed by Replication part 1 (#165), unaccepted until the maintainer accepts
it (ADR 0066). Ruled in [ADR 0179](0179-writer-handover-within-a-store.md)
point 12, which adds to point 5 and point 4 here.

- **Point 5's "when an advance sees a newer epoch" is built.** The
  object-storage follower reports the highest epoch it adopted
  (`ObjectStorage.AdoptedEpoch`), and the route is marked stale when that
  epoch is above the route's.
- **A `NoWriter` route is not cached.** Each write in that state reads the
  record once.
- **A forward the writer refused with `writer-superseded` or
  `writer-stepped-down` is answered `503 writer-moved`.** The original problem
  type is the member `reason`, the route is marked stale, and the body is not
  sent again. Such a batch was not committed.
- **Point 9's automatic handover layout** is ADR 0181's, filed with the
  automation.
