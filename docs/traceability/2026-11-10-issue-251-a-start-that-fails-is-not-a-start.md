# A start that fails is not a start

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#251](https://github.com/Hafeok/Varve/issues/251); [#216](https://github.com/Hafeok/Varve/issues/216) and [#202](https://github.com/Hafeok/Varve/issues/202) referenced. #216 was worked in the same session, as its own pull request and record |
| **Date** | 2026-11-10 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app |
| **Model** | `claude-fable-5-2`, the session's model and the one last served, from the session's own metadata; the session was configured for `claude-opus-5-5` and switched by the user |
| **Session identifier** | `session_01NbWSYPrQLZBSTrPr18UMBJ` |
| **Branch** | `claude/optimistic-euler-0mmun9-251`, from `main` at 81fba3d |
| **ADRs** | [0163](../adr/0163-the-management-plane.md) (amended) |

## The prompt

> Read AGENTS.md. Work #216 and #251 as one session, with one PR per issue. #216's two points are rulings already made on 2026-11-02, written on the issue. Point 1 is a dated amendment to ClaimAfterAReleaseByItsReason. #251's expected behaviour and gate are on the issue. Post your plan on each issue before building.

## The plan

Posted on #251 before building
([comment](https://github.com/Hafeok/Varve/issues/251#issuecomment-6763763469)).

## The report

- **The fix.** `ConvergeMemberAsync` marks the entry started once the dataset
  is open after the step, where it marked it at the decision. A failed open
  leaves it unstarted, and the next round applies the start rule again.
  `MemberDecision` is unchanged.
- **The log.** A writer's open that fails is a warning, once, naming the
  dataset, the epoch the open had taken or that it took none, and the
  failure with its causes. Later failing rounds are at debug until the
  dataset has been open again.
  - **A change from the plan.** The plan logged again when the reason
    changed. The reason changes every round, because a probe's key is
    random, so the first run logged every round. It is once per run of
    failures now.
  - The epoch is the storage's when the open got that far. Otherwise it is
    the writer record's, read after the failure, when the record names this
    instance unreleased.
- **The reason.** An entry's reason is the failure's message and its causes,
  so readiness and `GET /manage/datasets` name the store request and its
  status.
- **The choice.** Readiness reports a declared dataset that did not open,
  and the declaration's answer stays the system commit's. ADR 0163 is
  amended at points 12 and 13 with the reasons. No ruling of the ledger
  changes.
- **The tests.** A fleet theory, through the forced-failure proxy, with two
  failures: every `PUT` under `/d/`, where the open fails at the probe with
  no epoch, and the log's `PUT`s alone, where it fails holding epoch 1. In
  each: the declaration is answered success; readiness reports `failed`
  with the request and `500`; the log has one warning with the epoch, after
  five seconds of failing rounds; and a write is committed within ten
  seconds of the proxy recovering, with no restart.
- **The log test without a store service was not written.** The server's
  tests have no seam for a failing store in process: a fleet's own system
  dataset lives in the store, so the server needs a real one to start. The
  log is asserted in the fleet theory.
- **`FleetTests.cs:465`.** The assertion is `AssertWrittenAsync`, which
  prints the response's status and body, the dataset's readiness entry with
  its state and reason, and the server's log.
- **Mutant.** With the entry marked started whatever the outcome, both
  cases fail, with `503 no-writer` and the readiness reason "Its writer has
  not created it yet", which is the issue's failure.
- **Stated in the amendment.** A writer whose open fails after its claim
  claims again each round, so a store that fails a dataset's log for long
  leaves a run of claims in the epoch history.
- **What ran where.** The fleet tests ran on Azurite, started in the
  session's container from the npm package at 3.37.0, the version CI pins.
  The container has no Docker daemon, so the RustFS leg, the one that failed
  in the release run, did not run here: CI's `fleet (services)` job runs it.
