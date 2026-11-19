# A cancelled event move stays cancelled

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim
> where it is quoted; the full session prompt is summarised. The transcript
> itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#254](https://github.com/Hafeok/Varve/issues/254); closed [#217](https://github.com/Hafeok/Varve/issues/217) as its duplicate; opened [#367](https://github.com/Hafeok/Varve/issues/367) |
| **Pull request** | [#374](https://github.com/Hafeok/Varve/pull/374) |
| **Date** | 2026-11-19 |
| **Tool** | Claude Code, a cloud session (claude.ai/code), not the maintainer's delegate (ADR 0239) |
| **Model** | not written by the tool: its environment does not put a model identifier in a file pushed to a repository. The session gave it in its chat reply, and the maintainer fills it in here |
| **Session identifier** | `session_01NbWSYShh3XyxMHBQtApVnR` |
| **Branch** | `claude/event-move-race-254`, from `main` at c754bcba |
| **ADRs** | [0167](../adr/0167-event-declarations-move-to-the-management-plane.md) amended 2026-11-19, filed unaccepted |

## The prompt

The session prompt, *a write refused just after a cancelled event-catalogue
move (#254)*, set out what was known, the maintainer's ruling of 2026-11-19
("a user must never expect one thing and get another. If the server says the
move is cancelled, a write must not then be refused as moved"), six rulings
(find the cause first; no waiting in the test; which fix of two; the move's
other transitions; a test that fails without timing; the idle fleet), six
plan questions, the gate, and how the work lands.

The maintainer's answer to the plan, recorded on #254:

> The cause and the plan are accepted. Fixes (a) and (b) as proposed. Answers:
>
> A. 409, as a named problem type with a "What to do" section (ADR 0242): the
> move was cancelled by another request; say what the caller can do. It is
> not added to the client's retry list.
> B. Fix (c) is in this pull request, as its own commit with its own test.
> State what it adds to a write while a dataset is Moving, and what a write
> gets when the management plane cannot be reached then; that must be a
> refusal the client already treats correctly.
> C. Add a permanent dispatch-only job, not a temporary one: it runs a named
> test filter N times on a chosen OS, under load if asked. It never runs on a
> push or a pull request and is not a required check; make sure the
> required-checks gate agrees. Document it in docs/testing.md. Then run the
> 500 on windows-latest and ubuntu-latest and post both results.
>
> Added to the gate: a model-based test of the move. Random interleavings of
> loop ticks, move requests, cancels and failed writes against a small model
> of the move's states, asserting at least:
> - a move never reaches Moved after a cancel was acknowledged;
> - the catalogue is never frozen while no move is in progress;
> - a cancel is answered cancelled only if it took effect.
> It fails on the old code and passes on the new. Say which interleavings it
> found beyond the two you already hold.
>
> Also:
> - Close #217 as a duplicate of #254, with your reason.
> - #367 stays its own issue.
> - IdleFleetTests must still pass: the fix adds no commit on a timer.
> - Push the branch with the red tests now, and say on the pull request that
>   they are red until the fix; open it as a draft.
>
> File the ADR 0167 amendment unaccepted. Report "head <sha>" with the hosted
> result, every failed job read.

## The report

See the session's close-out,
[`docs/sessions/2026-11-event-move-race.md`](../sessions/2026-11-event-move-race.md),
for the cause, the reproduction rates before and after, the table of the
move's transitions, the interleavings the model-based test found, the
durations per OS, and what is deferred.
