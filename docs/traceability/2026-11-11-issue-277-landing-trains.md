# Landing trains (step 1 of the chain-builds work)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim in
> the step 0 record. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#277](https://github.com/Hafeok/Varve/issues/277), opened by this session for step 1; [#230](https://github.com/Hafeok/Varve/issues/230) and [#209](https://github.com/Hafeok/Varve/issues/209) referenced |
| **Date** | 2026-11-11 |
| **Tool** | Claude Code 2.1.322, a cloud session. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* This tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYNzDrbT3ENstGnmH3N` |
| **Branch** | `claude/nice-mendel-495j4y-step1`, on step 0's branch at 5a4850b |
| **ADRs** | none decided; [0243](../adr/0243-chain-builds-ahead-of-main.md) point 10 (`LandingTrains`) built; [0087](../adr/0087-the-identity-map.md) and [0088](../adr/0088-only-checked-commits-reach-main.md) applied |

## The prompt

Step 1 of the session prompt, in
[the step 0 record](2026-11-11-issue-230-chain-builds-ahead-of-main.md),
under the message "Start with step 0 (the ADR) and step 1 (landing trains),
and post your plan on each issue before building."

## The report

- **The plan went on #277 before the code.** It named the part of the step
  that is not `land.cs`: `agent review` admitted a `land/` push only when its
  head *is* an approved pull request's head, and a train's head is a merge,
  so every train would have been red there. `dco`, `issue-refs` and
  `release-pending` already skip merges, and the release's head guard reads
  `agent review`'s run, so neither needed a change.
- **`eng/land.cs -- --train N1 N2 …`:**
  - checks each member before building anything:
    - an `approve <sha>` naming its exact head is now a refusal, where a
      single landing only warns;
    - every check ruleset 1 requires must be concluded on the member's head;
    - its `Completes:` line is checked against its own commits;
    - at most one descriptor, which goes last, dated within three days;
  - merges by `git merge-tree --write-tree` and `git commit-tree`, so no
    working tree is touched and a conflict refuses the train. The merges are
    dated at the newest commit they join, so building twice gives the same
    head, and a fixture checks this;
  - `--main` verifies the branch rather than rebuilding it: the first-parent
    line down to `main`, each merge's trailer and tree, and the second
    parents equal to the members' current heads;
  - prints the train without its last member when the push to `main` is
    refused, and without the member at fault when a merge conflicts;
  - `--delegated-by <who>` adds ADR 0239 point 4's trailer to the merges.
- **`eng/agent-review.cs`** admits a train by the same rule, in its own copy.
  The judge is main's, so a train cannot change how it is judged. On a push,
  the workflow now checks main out with its whole history so that git can
  read the pushed merges. This takes effect only once it is on `main`.
- **Measured:**
  - `land-fixtures`: 12 cases → 22. Ten train cases are built for real in a
    scratch bare repository: 2 pass, 8 are refused, 0 judged wrong. The
    failure path was proven by copying the `pass-` case as `fail-`, which
    gave "23 cases, 2 judged wrong", exit 1;
  - `agent-review-tamper`: 4 new train cases, all hold;
  - `licence-headers`, `land`: pass.
- **A real dry run against GitHub**, `land.cs -- --train 266 265 --print`,
  refused both members, each for having no approval of its head, checks
  still running, and no `Completes:` line.
- **Left for later steps or other issues:**
  - consulting the trial-merge record when both members are chain branches
    is step 5's;
  - who may land a train is today's rule (ADR 0239). #246 would change it.
