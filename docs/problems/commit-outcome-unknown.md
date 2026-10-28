# `commit-outcome-unknown`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/commit-outcome-unknown` |
| **Title** | The write may or may not have been committed. |
| **Status** | `503` |
| **Members** | `position`, `requestId` |

The server cannot say whether the write was committed. It is never answered
as refused, since the commit may exist. There are two causes.

**The storage did not confirm the flush** (ADR 0214). The write's commit was
written to the log and closed, and the flush that would make it durable did
not return, on a full disk or another failure of the storage. A commit is
committed when it is closed and flushed, so this one is not known. `position`
is where it was written, and `requestId` names the request; it is the cause
the commit carries (ADR 0094). On a full disk the dataset's next write fences
the commit off before anything else is committed, so the commit at `position`
is then another write's; after a crash or a reopen first, the commit is there
when its record survived.

**A forwarded write's answer was lost** (ADR 0168). This instance forwarded
the write to the dataset's writer, and the writer's answer did not come back.
The writer may have committed it. There is no `position`, and `requestId` is
this instance's id for the request: the writer mints its own id for a
forwarded write, which is the commit's cause, so the commits resource cannot
be searched by it.

## Settling it

With a `position`, the commits resource settles it once the dataset's head
reaches `position`: `GET /datasets/{name}/commits/{position}` is this write's
commit when its cause is `requestId`, and another's when it is not.

## Sending it again

Never send the write again unconditionally: it may be committed twice.

- **Conditional on the position before.** Send it again with `If-Match`
  naming the position the first attempt was based on: `position − 1` when
  the problem has a `position`, else the `Varve-Position` of the read the
  write was made from (ADR 0011). If the first was not committed, the retry
  commits. If the first was committed, the retry is a `409 conflict`, since
  it touches what the first touched; read the graphs `conflict` names before
  deciding, since another write may also have come first.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
