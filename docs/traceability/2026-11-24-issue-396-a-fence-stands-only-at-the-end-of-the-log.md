# A fence stands only at the end of the log (#396)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim
> below. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#396](https://github.com/Hafeok/Varve/issues/396), completed. Found by [#166](https://github.com/Hafeok/Varve/issues/166) |
| **Date** | 2026-11-24 |
| **Tool** | Claude Code, a cloud session. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* This tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYSB3pCh3SLBkJVgjCv` |
| **Branch** | `claude/fence-not-at-the-end-396`, from `main` at 693c328a |
| **ADRs** | [0136](../adr/0136-one-writer-by-an-epoch-and-a-log-slot-fence.md) amended, unaccepted |

## The prompt

The session is the one that does Replication part 2 (#166). It found the
defect while answering the architect's review, and filed #396. The
maintainer then wrote, on 2026-11-24, among rulings on #338:

> #396 comes first, in a pull request of its own, as #393 did:
> - branch `claude/fence-not-at-the-end-396`, from `main`;
> - the issue's reproduction as the test, failing before the fix;
> - a dated amendment to ADR 0136, filed unaccepted, with its ruling
>   cited from the code;
> - say on #396 whether any released preview can lose or fork data by
>   this order, and what an operator would see.
> Post the plan on #396 before building.

## The report

**The defect.** A writer's fence create can succeed in a slot that is not
the end of the log: the slot a chunk held until the writer it supersedes
sealed the segment, consolidated it and deleted its chunks. ADR 0136
decision 3 did not have this order.

**What an unfixed build does,** both reproduced:
- Where the new writer's open reads a chunk below the tail, it is refused
  as superseded at its first commit, and the old writer commits on.
- Where a checkpoint is at the head, both writers acknowledge commits at
  the same positions. The new writer's are behind the consolidated object
  and are lost.

**The change.** In `ObjectSegmentStore.FenceAsync`, which the open for
write and the claim in place both run:
- after a fence create succeeds, one read for the consolidated object of
  the fence's segment;
- absent, the fence stands;
- present, `FenceAgainPastAsync` adopts the object, probes the log after it
  as a follower's refresh does, and the loop fences again.

The probe and the tail listing were in `RefreshAsync` and
`ClaimInPlaceAsync`; they are now methods both use. Nothing else changed.

**The ruling** is `AFenceStandsOnlyAtTheEndOfTheLog`, filed unaccepted in
the decision set and cited on `FenceAgainPastAsync`. The build is red on
`CS0618` until it is accepted.

**The tests.** `FenceNotAtTheEndTests`, four cases:
- the open for write, the issue's reproduction;
- the same with a checkpoint at the head, the order that forks;
- the claim in place;
- a follower that had read up to the slot before the vacated one.

Each asserts that the new writer reads the old writer's last commit, that
its fence is recorded in the next segment, that it commits and the old
writer is refused, and that a reader opened afterwards holds every
acknowledged commit. All four fail with the check turned off.

**What was not done.**
- The chunk left in the vacated slot is not deleted. The amendment says why.
- `docs/spec/storage-format.md` is unchanged: it gives the writer record's
  `fence` member and not the fence procedure, and no byte of any format
  changed.
- No timing was measured against a real store. The cost is one read per
  open for write.

Developed with AI assistance under human review.
