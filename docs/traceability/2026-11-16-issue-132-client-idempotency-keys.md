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
  - `WriteRetries`, with a retry on a lost response, any `503`, and `409`
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
  test projects, all passing (counts in the head's comment on #132).
- **Not run here:** `eng/decision-sets.cs`, which needs the full history, and
  this clone is shallow. The fleet suite on Azurite and RustFS. The W3C
  suites.

## Questions for the maintainer

- **The window.** Is 100 000 commits the right default, or should keys be
  kept for ever now, with #309 as part of 1.0?
- **The default key source.** #132 says the client sends a key by default.
  ADR 0011's ban on ambient randomness keeps the library from drawing one,
  so the host supplies it: the CLI does, and the web UI and `AddVarveClient`
  are #311. Is that the reading you want, or should the ban admit the client's
  keys by a decision?

## Deferred

- #309: keys kept for ever, in `derived/`.
- #310: a header-only commit read, for the index's catch-up.
- #311: key sources for the web UI and `AddVarveClient`.
