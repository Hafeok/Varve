# A live feed cut at shutdown

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#81](https://github.com/Hafeok/Varve/issues/81), opened by this session |
| **Date** | 2026-10-09 |
| **Tool** | Claude Code 2.1.294, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01YbhFvg7wVgX3FdRbofvTt8` |
| **Branch** | `claude/gracious-planck-l5ugrc`, from `main` at c54fcec |

## The prompt

The maintainer pasted the refusal of `git push origin e07eabf…:refs/heads/main`:
`Required status check "build (ubuntu-latest)" is failing.`

## The report

The failing check was `HostTests.Shutdown_ends_a_live_feed_and_keeps_every_commit_for_the_next_start`.
It failed on #80's pull request run, while the same commit passed on
`land/pr-80`. It had failed before on #75's `land/` push and on `main`'s push
run for c54fcec. The server logged `NotSupportedException` from the
subscription's `DisposeAsync`, called from `FeedEndpoint.StreamAsync`.

At shutdown, the heartbeat delay and the pending `MoveNextAsync` both wait on
the linked token. When the delay wins, the heartbeat's flush throws, and
`await using` disposes the subscription with the read still in flight. The
store refuses that. Its exception replaces the cancellation, so the
`shutdown` event is never written and the response is aborted.

The fix is in `src/Varve.Protocol/Endpoints/FeedEndpoint.cs`. Before the
enumerator is disposed, the read still in flight is cancelled through the
linked source and awaited, with its exception suppressed. The outer catches
then see the exception that actually ended the stream.

**Not reproduced locally.** Fifteen isolated runs of the test and ten runs of
the whole project under CPU load all passed before the fix. The order of
cancellation callbacks against the continuation is the scheduler's, and no
deterministic test of it was found without a hook into the store. The
existing host test is the regression test. After the fix, Varve.Protocol.Tests
(47) and Varve.Server.Tests (35), the latter ten times, pass.

Developed with AI assistance under human review.
