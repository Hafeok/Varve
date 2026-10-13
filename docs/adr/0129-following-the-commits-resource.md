# 0129 — Following the commits resource: `FollowAsync`, a checkpoint the caller owns, at least once

## Status

**Accepted — filed unaccepted by milestone Client and integration of #93,
2026-10-13** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Client and integration plan.

## Context

The commits resource (ADRs 0097, 0118) already has every property integration
needs:

- a total order per dataset;
- resumption by position, through `from` or `Last-Event-ID`;
- at-least-once delivery from the store's subscription;
- byte-deterministic records;
- retention for ever;
- filtering by graph.

What it lacks is a consumer. Today an application that wants "every commit,
once, surviving restarts" has to write the same loop each time:

- read a range page by page through `Link: rel="next"` (ADR 0114);
- switch to the live tail;
- reconnect after `shutdown` or a dropped connection;
- back off on `429 too-many-live-tails`;
- store a position somewhere, and decide whether it stores it before or after
  doing the work, which is the whole question.

## Decision

1. **`SparqlHttpClient.FollowAsync(from, handler, checkpoint, options, ct)`**
   follows the dataset the client addresses.
   - **The starting point.** It starts from `checkpoint.LoadAsync()` when that
     has a position, and from `from` otherwise. `from` is exclusive, as on the
     resource.
   - **History first.** It reads the history up to the head the status
     endpoint reports, as `application/vnd.varve.delta` ranges followed
     through `Link: rel="next"`.
   - **Then the live tail.** It opens the live tail as `text/event-stream`
     from the last position it handled, or from that head, with
     `Last-Event-ID` on every reconnect.
   - **The handler and the checkpoint.** It hands each commit record
     (`Varve.Delta`'s `FeedRecord`, ADR 0128) to `handler` and **calls
     `checkpoint.SaveAsync(position)` only after the handler returns**.
   - **Filters.** `FollowOptions` carries the feed's `graph` filter (ADR 0097).
2. **The contract is at least once, and the method says so.**
   - A crash between the handler's return and the checkpoint's save delivers
     that commit again on restart.
   - A handler must be idempotent by position. A consumer that stores the
     position in the same transaction as its effect gets exactly once in
     effect, and the documentation shows how.
   - Within one run, without a crash, each commit is handed over exactly once
     per checkpoint advance, in position order.
3. **`IFollowCheckpoint`** has two members: `LoadAsync` (the last position
   saved, or none) and `SaveAsync(position)`.
   - The caller implements it: a file, a table, or a quad in the consumer's
     own Varve dataset.
   - The package ships one implementation, `FileFollowCheckpoint`, which
     replaces the file atomically (write a temporary file, then rename), for
     samples and the CLI.
4. **What it rides out.**
   - It retries the following, with backoff, and never treats them as the end
     of the stream:
     - a dropped connection;
     - an SSE `shutdown` or `error` event (ADR 0095);
     - `429` and `503` (ADR 0114);
     - a `5xx` before any byte arrived.
   - It surfaces the following as an exception carrying the problem body,
     because waiting will not fix them: `401`, `403`, `404`, any other `4xx`,
     and a body that is not the format (`ChangeFeedFormatException`).
   - The backoff is exponential with jitter from `FollowOptions`, measured on
     the client's `TimeProvider`.
5. **Grants are the server's.** A commit the caller cannot read is not
   delivered (ADR 0107), so the positions a follower sees may have gaps. The
   checkpoint stores the last position handled, and resuming re-reads at most
   the filtered commits after it, which the server drops again.
6. **No new server surface.** `FollowAsync` uses only the commits resource and
   the status endpoint as they are.

## Alternatives considered

- **Checkpoint before the handler.** At most once: a crash loses a commit.
  The log keeps everything for ever, so losing a commit is a choice nobody
  wants made for them.
- **The client stores the checkpoint itself.** A file would be wrong for a
  web farm and a table wrong for a CLI. One interface with two members is
  cheaper than every policy.
- **Only the live tail, from the checkpoint.** A live tail is a slot counted
  against `MaxLiveTailsPerClient` and is not paged. Replaying months of
  history through one open stream holds the slot for the whole replay. Ranges
  are reads, bounded and paged, as history should be.

## Consequences

- Integration is one call. The Event Hubs bridge sample (the milestone's
  second part) is built on it.
- The property tests resume a follower from every position of generated
  histories and inject a crash between handler and checkpoint at every
  commit.

## Checks

- **Checked against the accepted ADRs** (0001–0128). Touches **0095** (a cut
  is visible, and the follower resumes past it), **0097** and **0118** (the
  resource, unchanged), **0107** (gaps are grants), **0114** (paging and the
  tail limit). No conflict.
- **Layer ownership.** `Varve.Protocol.Client`, layer 5.
- **Analyzer rule.** None.
- **Open questions owned.** None.
