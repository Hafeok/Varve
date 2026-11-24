# A follower opens at the last whole record and never appends (#384)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim
> below. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#384](https://github.com/Hafeok/Varve/issues/384), completed. Found by, and blocking, [#166](https://github.com/Hafeok/Varve/issues/166) |
| **Date** | 2026-11-20 |
| **Tool** | Claude Code 2.1.325, a cloud session. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* This tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYSB3pCh3SLBkJVgjCv` |
| **Branch** | `claude/follower-torn-tail-384`, from `main` at bd5eedd8 |
| **ADRs** | [0164](../adr/0164-the-tier-1-follower.md) amended, unaccepted |

## The prompt

The session is the one that does Replication part 2 (#166). Its own
failure-injection gate found the defect, and it filed #384. The maintainer
then wrote, on 2026-11-20, among rulings on #166:

> #384 comes before #338. A shipping target is in that state at every
> segment roll, so the warm standby meets it routinely.
> - A pull request of its own, branch `claude/follower-torn-tail-384`,
>   with a dated amendment to ADR 0164, filed unaccepted: a follower opens
>   at the last whole record and never appends.
> - The issue's reproduction as the test, failing before the fix.
> - After it lands, #338 merges `main` and its failure-injection gate
>   stops skipping the follower check.

## The report

**The defect.** `Dataset.OpenFollowerAsync` ran the writer's open of the
log. Where the newest segment was not whole, that open repairs it by
appending an abandoned trailer or sealing the segment, and a follower's
storage refuses both. The follower did not open.

**The two states,** each between two creates of the writer:
- the newest segment holds only its opening chunk;
- the newest segment has its trailer chunk and no seal chunk.

**The change.** One method and one branch in `Varve.Store`:
- `LogWriter.Following` makes a follower's log writer at the head, with no
  active segment, without reading or writing the store;
- `Dataset.OpenAsync` uses it when it opens a follower.

A follower's log writer is never used to append. A follower that takes the
writing over (`TakeWriteAsync`) already opens the log again as a writer
after its claim, and makes the repair then. So nothing else changed.

**The tests.** `FollowerTornTailTests`, one per state:
- the writer is stopped between the two creates, by a fault on the second;
- a follower opens at the last acknowledged commit and reads every
  position;
- it creates, replaces and deletes nothing under `log/`, and the log's
  objects are the same before and after;
- a writer that opens afterwards repairs the segment and commits, and the
  follower's advance reaches its head.

Both failed before the change with the exception the issue quotes. For the
second state the seal chunk's name is taken from a run that nothing stops.

**What was not done.**
- No change to a follower that is already open. It was not affected.
- No new ruling. `FollowerIsAnOrdinaryDataset` already says every write is
  refused; the open now keeps it. The amendment says so.
- The file backend has no follower, so it has no such open.

**After this lands,** #338 merges `main` and removes the catch in its
failure-injection gate that stepped over this refusal.

Developed with AI assistance under human review.
