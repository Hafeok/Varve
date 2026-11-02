# An event move's cancel is answered once the plane has applied it

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#206](https://github.com/Hafeok/Varve/issues/206) |
| **Date** | 2026-10-30 |
| **Tool** | Claude Code 2.1.314 (Claude Code), a local session in WSL |
| **Model** | `claude-fable-5-1`, from the session's own metadata |
| **Session identifier** | `99e6699f-a3f7-48ac-974c-711ade42f85c` |
| **Branch** | `fix/event-move-cancel-applied`, from `main` at ea62fc6a |

## The prompt

The session was landing #180 on the maintainer's instruction ("keep an eye on
#188 and then land it to main when all CI checks are green, then merge main
into #180 and land it"). The Windows build on `land/pr-180` failed on
`EventMoveTests.A_catalogue_that_does_not_translate_one_to_one_cancels_the_move_and_writes_reopen`,
which #180 does not touch, while the same head's pull-request run passed it.
The session diagnosed it, drafted the issue, and the maintainer said:

> just pick the next issue number

which filed #206, and the fix followed as its own pull request from `main`.

## The report

**What failed.** After the move answered `422 event-declaration-invalid`
with "cancelled", `GET manage/datasets/d/events` answered `"state":
"moving"`, not `"in-dataset"`.

**Cause.** In `EventMoves.RunAsync`, the freeze write and the copy write are
each followed by `AppliedAsync`, which advances the management plane until
its head reaches the write's position. The cancel write, m₃, was not: the
write returned after one `AdvanceAsync`, and the move answered at once. A
reader asking for the state before the plane had applied m₃ saw the graph as
it was, `varve:Moving`. The pull-request run passed by timing.

**Fix.** The cancel waits as the freeze and the copy do:
`await AppliedAsync(move, cancel.Position, …)` before the move answers, when
the write was not stale. Four lines in `src/Varve.Protocol/Endpoints/EventMoves.cs`;
no behaviour was decided, so no ADR changes.

**Verification.** `Varve.Server.Tests.EventMoveTests`, all nine, pass on
the branch locally (Linux). The failure was seen on windows-latest only, by
timing; the change makes the state read after the answer hold by the
server's own contract, not by timing.

**What else was not done.** The re-run of the failed Windows job on
`land/pr-180` was started before this fix, since #180 does not touch event
moves; this fix lands separately.
