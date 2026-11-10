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

## The second prompt, the same day

After the report above, the maintainer ruled on the two costs the session
had flagged, here and on #216:

> Both costs you flagged are real, and one mechanism fixes both. Change the design as follows.
> Hold-off: a member whose open or write against the store fails for a dataset holds off before claiming that dataset again. It starts at one claim round, doubles per consecutive failure to a cap of 30 seconds, carries jitter, and resets on a successful open.
> #253 (#251) carries the mechanism, with the ADR 0163 amendment stating the figures. Add a test that a store failing continuously sees claims at the capped rate.
> #252 (#216) restates ClaimAfterAReleaseByItsReason as: a release for a storage or local-disk error starts the releaser's hold-off. Keep the test that b wins at once, and add one where the releaser is alone and reclaims after its hold-off, with writes answered again and no restart. Update the operator guide to match.
> Order: #253 lands first and #252 rebases onto it. Post both new heads.
> The one-off failure of EventMoveTests.A_catalogue_that_does_not_translate_one_to_one_cancels_the_move_and_writes_reopen: open an issue with the output you have and what ran beside it, linked from #216.
> Yes, watch both pull requests and respond to CI and review comments.

## The second report

- **The mechanism** is `ClaimHoldOff`, one per dataset on each instance. It
  is asked once a round, and gates a claim at start, the preferred writer's
  open after a placement release, and a claim in place. It also gates a
  dataset without a writer set, and a standby's claim of the system dataset.
  A takeover request is not gated.
- **Three choices the prompt left open, stated in the amendment.**
  - **Jitter** is a wait drawn evenly between half the step and all of it,
    so the cap means a claim every 15 to 30 seconds.
  - **One whole round is always sat out.** A wait of half a round could end
    before another member's round came, so the round after a failure never
    claims. #216's "b wins at once" rests on this.
  - **"A successful open" is an open as the writer.** A releaser opens as a
    standby at once, and that must not end its hold-off.
- **The ruling** is filed as `TheManagementPlane.ClaimsHoldOffAfterAStoreFailure`,
  unaccepted, so the build is red on `CS0618` at `ClaimHoldOff.cs` until the
  maintainer accepts it.
- **The capped rate** is tested over a clock the test moves, for rounds of
  50 ms to 5 s: an hour of failed claims, the waits doubling to the cap, and
  a claim every 15 to 30 seconds in the last half hour. It is not shown on a
  real store, where reaching the cap takes a minute of failures.
- **The fleet theory** now fails the log's writes for twenty seconds and
  counts the claims in the record: at most eight. With the hold-off removed
  it counted thirteen.
- **The earlier report's last cost** is what the hold-off bounds.
- **The flake** is #254.

## The third prompt, the same day

> Agreed on all three points and on the local-disk cycle. One check: while a lone releaser cycles on local-disk-error, does /health/ready report the dataset as degraded with that reason? If not, add it to #253 and post the new head. Otherwise nothing more is needed. The delegate will accept both rulings and land #253, then #252. Rebase when main moves.

## The third report

- **It did not.** Readiness reported `spill: failing` only while the open
  writer's last spill had failed. After the release the entry is a fresh
  standby, and after the claim nothing is failing until the next spill, so
  for most of each cycle readiness said nothing.
- **Added:** `"degraded": {"reason", "releases", "since"}` on a dataset's
  readiness entry, from this instance's release for `storage-error` or
  `local-disk-error` until it has written the dataset for 30 seconds with no
  spill failing. `OwnRelease` keeps it, beside the hold-off. It never makes
  the instance unready.
- **No ruling is added.** It is detail of readiness, in ADR 0163's amendment
  at point 13.
- **Tests.** `OwnReleaseTests` drives twenty cycles over a moved clock. The
  fleet's storage-error abdication asserts the entry on the releaser and its
  absence on the member that took over. `local-disk-error` itself is not
  forced on a real store.
- **One limit, stated in the amendment.** A standby that released and never
  writes the dataset again stays `degraded` until it restarts.
