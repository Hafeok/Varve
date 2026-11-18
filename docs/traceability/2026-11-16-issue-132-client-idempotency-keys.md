# Client idempotency keys, scoped to the agent: a retried write commits once

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#132](https://github.com/Hafeok/Varve/issues/132), under 1.0 readiness [#129](https://github.com/Hafeok/Varve/issues/129); follow-ups [#309](https://github.com/Hafeok/Varve/issues/309), [#310](https://github.com/Hafeok/Varve/issues/310), [#311](https://github.com/Hafeok/Varve/issues/311) |
| **Date** | 2026-11-16 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYSRPmb4drpszHBjBHp` |
| **Branch** | `claude/admiring-thompson-wskdjl`, from `main` at 48a1fd5 |
| **ADRs** | [0246](../adr/0246-client-idempotency-keys.md) (new, filed unaccepted); [0094](../adr/0094-a-write-over-http-is-one-commit.md) and [0168](../adr/0168-write-routing-by-forwarding.md) (dated amendments, filed unaccepted) |

## The prompt

> Read AGENTS.md, then #132 and step 5 of the project doc claude/varve-claude-code-readiness-store.md, which I'll paste below. Build client idempotency keys scoped to the authenticated agent: a retried write commits once. Post your plan on #132 first: the header, how long a key is kept and where, and what a reused key with a different body answers. The key is part of the commit's metadata, so a retry after a writer handover finds it, and a forwarded write keeps its key (ADR 0168). Replication part 2 is starting in parallel; say on #132 what it must know.
>
> You are a cloud session, not the delegate: file decisions unaccepted and never land. Never write the approval line; report a head as "head <sha>". Completes: #132, with a Closes #132 trailer. The co-author trailer is exactly Co-Authored-By: Claude <noreply@anthropic.com>. Never force-push.

The project document's step 5 was not pasted into the session, and the file
is not in the repository. The session worked from #132's text, and says so on
the issue.

## The report

- **The plan** is posted on #132 before any code, and covers what the prompt
  asked for:
  - the header;
  - where a key is kept and for how long;
  - the answer to a key reused with a different body;
  - what replication part 2 (#166) must know.
- **ADR 0246**, filed unaccepted, with its decision set
  `client-idempotency-keys`. It also files dated amendments to ADRs 0094 and
  0168, and their rulings `KeyFindsALostResponsesCommit` and
  `ForwardedWriteKeepsItsKey`, unaccepted.
- **`Varve.Protocol`**:
  - `Idempotency-Key` on SPARQL Update, the Graph Store writes, the settings
    resource and the management writes that are one commit;
  - the key looked up per agent before any precondition;
  - the key recorded as a host attachment, `varve:idempotencyKey`;
  - a per-dataset index read back from the log over a window of commits;
  - a replayed answer with `Varve-Replayed`;
  - two new problem types, `idempotency-key-reused` (`422`) and
    `idempotency-key-in-flight` (`409`);
  - `Idempotency-Key` forwarded.
- **`Varve.Protocol.Client`**:
  - `ClientLimits.IdempotencyKeys`, the host's source of keys. The library
    draws no randomness, because the key is in the log (ADR 0011);
  - `WriteRetries`, with a retry on a lost response, any `503` (narrowed in the third review to the refusals the server states as retryable), and `409`
    with `idempotency-key-in-flight`.
- **`Varve.Server`**: `Varve:Limits:IdempotencyWindow`, and the CLI's key
  source.
- **The gate**, in `tests/Varve.Protocol.Tests/IdempotencyKeyTests.cs`. Two
  instances over one log, the client sending to either. Over generated
  schedules, a response is lost after the writer answered or a request is
  lost before it left, and at the loss the writer moves, reopening the log on
  the other instance. Every logical write commits exactly once. Each write
  increments a counter and leaves a marker in one commit, so a double commit
  shows as the counter above the markers.
  - With the server's key check disabled, the gate fails, at counter 4
    against 2 writes.
  - Endpoint tests cover a different body (`422`), another agent's key, a
    replay before `If-Match`, the Graph Store's `201` and `Location`, a
    malformed key, a write that committed nothing, a reopened dataset, the
    window, and a key in flight (`409`).
- **Run here:** `dotnet build Varve.slnx` with `CS0618` as a warning (the
  unaccepted citations), and the Protocol, Protocol client, web UI and Server
  test projects: Protocol 180, Protocol client 117, web UI 234 and Server 237 (58 skipped for services absent here), all passing.
- **Not run here:** `eng/decision-sets.cs`, which needs the full history, and
  this clone is shallow. The fleet suite on Azurite and RustFS. The W3C
  suites.

## The review, and what it changed

The maintainer reviewed the first head on #132 and asked for four changes
before acceptance:

1. **A hash, not the key, in the commit.** The commit records SHA-256 over
   the agent and the key. The index holds a 16-byte prefix with the
   position, and a hit is checked against the whole hash in the commit's
   header.
2. **A retry past the window is refused.** A retry carries
   `Varve-Key-Position`. A key not found more than the window past it is
   `422 idempotency-key-expired` and commits nothing. The gate now has
   commits that fill a window of 4 while a write is lost.
   - With the refusal disabled, the gate fails: the counter is 3 against 2
     writes.
3. **Keys by default.** The client's default source draws 128 random bits
   once per logical write, behind the one `RS0030` suppression, which cites
   ADR 0246. ADR 0246 point 10 says why ADR 0011 holds.
4. **The rebuild measured.** `Varve.Benchmarks -- --idempotency-index M`
   loads REC M (25,000 commits, about 5M quads) into an in-memory object
   store and fills the 100,000-commit window with keyed commits. It then
   measures `PrepareAsync` after a restart (a cold open by another
   instance) and after a takeover (a follower claiming in place).
   - Reading whole commits, at REC S, cost 0.7 to 0.9 s and 526 to
     615 MiB allocated, so #310 came into this change.
   - `Dataset.ReadMetadataAsync` reads only the header at the closing
     record's tail and checks it against the commit index's hash.
   - The index reads four headers at a time.

| REC M, window 100,000 | Restart | Takeover | Index held |
|---|---:|---:|---:|
| Window spans all 25,000 REC commits, 75,000 keyed | 468 ms | 311 ms | 2.6 MiB |
| Window all keyed, 100,000 | 412 ms | 342 ms | 5.4 MiB |

Transient allocation is 204 to 246 MiB of short-lived reads. The time is on a
4-core container, against an object store in memory.

**Also asked: whether the term appears in the change feed or in
CloudEvents.** It would have appeared in the change feed, which writes every
host attachment as an `attachment` line (ADR 0165). It is now left out for
every caller, in `DeltaLines`, with a test. CloudEvents rendering never
renders an attachment.

**Tests run here, all passing:**
- Protocol 183;
- Protocol client 117;
- web UI 234;
- Server 237, with 58 skipped for services absent here;
- Store 470, with 93 skipped.

## The second review

The maintainer's answers on #132:

- **The server reads the keys ahead.** It calls `PrepareAsync` whenever it
  becomes a dataset's writer, in the background, and a keyed write that comes
  first waits for it. Two tests cover it:
  - `A_takeover_does_not_wait_for_the_key_index_and_a_keyed_retry_straight_after_finds_its_key`
    runs locally. It does a real claim in place over an in-memory object
    store, with the warm-up held open by a test gate. The takeover completes
    while the warm-up is pending. A keyed retry sent straight after waits
    for the warm-up and is then answered from the old writer's commit.
  - `A_keyed_retry_straight_after_a_takeover_is_answered_from_the_old_writer_s_commit`
    is in the fleet suite, which runs only in CI, on Azurite and RustFS. It
    uses separate server processes, with 201 keyed commits in the window, and
    reports the takeover's time.
- **ADR 0246 states both guarantees.** With `Varve-Key-Position`, a write
  commits once or is refused. Without it, it commits once within the window.
- **The small-machine guide** gives the index's memory per written dataset:
  about 56 bytes per keyed commit, so 5.4 MiB for a full window.

**Tests run here, all passing:** Protocol 184; Protocol client 117; Server
239, with 60 skipped for services absent here, the new fleet test among them.

## The third review

The maintainer's two changes on #320, 2026-11-18, before acceptance:

- **ADR 0246's status line** reads "Accepted — filed unaccepted by …", the
  form `eng/release.cs` reads as accepted once the decisions are.
- **The client retries only the refusals the server states as retryable,**
  named by problem type:
  - `writer-moved`, `writer-stepped-down`, `writer-superseded`,
    `shutting-down`, `management-behind` and `commit-outcome-unknown`;
  - `unavailable`, but only with the reason `no-writer` or
    `writer-unreachable`;
  - `409 idempotency-key-in-flight`.

  Every other `503` is final. `Retry-After` is not the test, because a
  write's generic `unavailable` carries it. That includes a sequencer broken
  by an `OutOfMemoryException`, which stays broken until the dataset is
  reopened. A memory limit is never retried. `WriteRetryTests` holds each
  case: 9 retried and 12 final.

**Tests run here, all passing:** Protocol client 139, Protocol 184, and
Server 255 (60 skipped). The docs site's links have 0 broken.

## Deferred

- #309: keys kept for ever, in `derived/`. After 1.0.
- #311: the web UI's own check of keyed writes in the browser.
  `AddVarveClient` is settled by the default source.
