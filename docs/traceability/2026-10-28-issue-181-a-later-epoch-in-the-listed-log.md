# A writer that lists a later epoch's fence is superseded (#181)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The maintainer's
> instruction is verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#181](https://github.com/Hafeok/Varve/issues/181) |
| **Date** | 2026-10-28 |
| **Tool** | Claude Code 2.1.311, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYTiuFLu3ZNqXGRKLQ3`, the session of Replication part 1 (#165) |
| **Branch** | `claude/fence-later-epoch-181`, from `main` at b8ac54b |
| **Machine** | a cloud container, Intel Xeon @ 2.10 GHz, 4 cores, 15 GiB, Ubuntu 24.04.5 LTS, .NET SDK 10.0.403 |

## The prompt

The fix was found on #165's branch (commit 0119abc there), and the maintainer
asked for it apart:

> The fence race is the important finding. Split 0119abc, with its deterministic test and the new exact-equality assertion, into its own small PR from main, closing #181 with a Closes #181 trailer on its commit. It should land ahead of the rest of #175, because every dataset’s writer fence depends on it. Then merge main back into #175.

## What was found

`MemoryWriterFenceTests`' concurrent-claims case failed about 1 run in 20 on
`main`. The issue asked whether that was a race in the claim or a wrong
expectation in the test. It was both.

- **The race.** ADR 0136 decision 2 has an open claim epoch *n*+1, adopt the
  log, then fence it. The fence is superseded "if what is there carries an
  epoch higher than *n*+1". The code checked that only for a chunk found in
  a slot it raced for. Take a writer that claimed *n* and listed the log only
  after the writer of *n*+1 had fenced it. It found that fence among the
  chunks it listed, compared nothing, and fenced the next slot at *n*. It
  then wrote on, and the writer of *n*+1, which the writer record names, was
  the one superseded at its next create. Nothing was lost or written twice,
  because each slot is one create, but the lower epoch held the log.
- **The fix.** The fence is refused when the log the open holds already ends
  in a chunk of a later epoch: a later writer holds it, as decision 2 says.
  ADR 0136 needs no amendment, since the code now does what it states.
- **The wrong expectation.** The case's first half asserted that the history
  is exactly the epochs still held. A claimant can win its epoch in the
  record and be fenced by the next claim before its own fence lands. It is
  then rightly superseded, and its claim stays in the history. With the race
  fixed, that assertion still fails 12 runs in 200.
- **The new assertion** is an equality, no weaker than the old:
  - the history is exactly the claims that succeeded in the record, each
    one claimant's, in ascending epochs;
  - the held epochs are among them;
  - every claimant that won an epoch but holds none is superseded;
  - the record's epoch is the highest held.

  Its order is the property ADR 0136 decision 2 guarantees: a claim that
  wins the record holds the log, or is superseded by a later one.

## The gate

- **The deterministic reproduction.** Writer a claims and is held just
  before it lists the log; b claims the next epoch and fences; then a lists.
  On `main`'s fence it fails, with a holding epoch 2 under b's 3. With the
  fix it passes.
- **1,000 runs under contention:** `MemoryWriterFenceTests` ran 1,035 times
  with the fix, as up to six processes at once on the 4 cores: 135 runs, then
  6 × 150. All passed. On `main` the same case failed about 1 run in 20.
- **`HandoverScheduleTests`** is on #175's branch, not on `main`. It runs with
  the same seeds once `main` is merged back into #175.
