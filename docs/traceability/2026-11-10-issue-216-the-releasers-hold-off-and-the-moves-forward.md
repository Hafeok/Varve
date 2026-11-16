# The releaser stands out of its own release's race, and the move's forward answers by what happened

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#216](https://github.com/Hafeok/Varve/issues/216); [#165](https://github.com/Hafeok/Varve/issues/165) referenced. [#251](https://github.com/Hafeok/Varve/issues/251) was worked in the same session, as its own pull request and record |
| **Date** | 2026-11-10 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app |
| **Model** | `claude-fable-5-2`, the session's model and the one last served, from the session's own metadata; the session was configured for `claude-opus-5-5` and switched by the user |
| **Session identifier** | `session_01NbWSYPrQLZBSTrPr18UMBJ` |
| **Branch** | `claude/optimistic-euler-0mmun9`, rebased onto #251's branch at 9f1f1ff, which is on `main` at 2f7573d |
| **ADRs** | [0180](../adr/0180-warm-standbys-and-the-writer-set.md) (amended), [0167](../adr/0167-event-declarations-move-to-the-management-plane.md) (amended); [0163](../adr/0163-the-management-plane.md)'s hold-off, amended by #251's branch, is applied |

## The prompt

> Read AGENTS.md. Work #216 and #251 as one session, with one PR per issue. #216's two points are rulings already made on 2026-11-02, written on the issue. Point 1 is a dated amendment to ClaimAfterAReleaseByItsReason. #251's expected behaviour and gate are on the issue. Post your plan on each issue before building.

## The plan

Posted on #216 before building
([comment](https://github.com/Hafeok/Varve/issues/216#issuecomment-6763759048)).

## The first report: the exclusion, which did not land

The session first built point 1 as #216 wrote it: the member that released
for `storage-error` or `local-disk-error` did not race for its own release.
It reported one cost the ruling did not name. When the releaser is the only
member that could claim, because every other member is down or cold, nothing
claims: a writer whose store failed once and recovered no longer took the
dataset back, and writes were `no-writer` until a takeover request or a
restart. A fleet test showed it.

Point 2 was built as it stands below.

## The second prompt, the same day

> Both costs you flagged are real, and one mechanism fixes both. Change the design as follows.
> Hold-off: a member whose open or write against the store fails for a dataset holds off before claiming that dataset again. It starts at one claim round, doubles per consecutive failure to a cap of 30 seconds, carries jitter, and resets on a successful open.
> #253 (#251) carries the mechanism, with the ADR 0163 amendment stating the figures. Add a test that a store failing continuously sees claims at the capped rate.
> #252 (#216) restates ClaimAfterAReleaseByItsReason as: a release for a storage or local-disk error starts the releaser's hold-off. Keep the test that b wins at once, and add one where the releaser is alone and reclaims after its hold-off, with writes answered again and no restart. Update the operator guide to match.
> Order: #253 lands first and #252 rebases onto it. Post both new heads.
> The one-off failure of EventMoveTests.A_catalogue_that_does_not_translate_one_to_one_cancels_the_move_and_writes_reopen: open an issue with the output you have and what ran beside it, linked from #216.
> Yes, watch both pull requests and respond to CI and review comments.

## The report

- **Point 1, the rule.** A release for `storage-error` or `local-disk-error`
  starts the releaser's hold-off, ADR 0163's, which #251's branch carries.
  `ConvergeMemberAsync` starts it where it releases for either reason.
  `MemberDecision` is as it is on `main`: the releaser races like any warm
  member, once its hold-off has passed.
- **Point 1, the ruling.** ADR 0180 is amended, dated 2026-11-10, the day of
  this ruling, and says which ruling of 2026-11-02 it replaces.
  `ClaimAfterAReleaseByItsReason` is restated in the ledger with no
  `accepted-by`: a cloud session never writes it. The build is red on
  `CS0618` at `MemberDecision.cs` until the maintainer accepts it, and at
  `ClaimHoldOff.cs` until #251's key is accepted.
- **Why b wins at once.** The hold-off's first wait is half a round to one
  round, which could end before another member's round came. So the
  hold-off always sits out the round after a failure whole. That is #251's
  mechanism, and this rule rests on it.
- **One consequence stated in the amendment.** A releaser for
  `local-disk-error` that is alone claims in place after its hold-off, and
  the claim succeeds, because the store answers. Success resets the
  hold-off, so it cycles at the rate its memtable fills.
- **Point 1, the tests.**
  - The storage-error abdication's fleet test also asserts that the one
    claim after epoch 1 is b's, at epoch 2. Eight runs in eight.
  - A new fleet test for the releaser alone, with one-second rounds: the
    store recovers, a claims epoch 2 itself and answers writes in the same
    process, and the claim comes at least 1.5 s after the release.
  - The failed claim's fleet test is as it is on `main`: a member claiming
    its own release is again what happens.
- **Point 2.** `EventMoves`' forward answers `503 unavailable`, reason
  `writer-unreachable`, when no connection to the system writer was made, and
  `503 commit-outcome-unknown` when the write was sent and its answer lost.
  `Forwarding.NeverSent` reads `HttpRequestException.HttpRequestError`. I
  read "one problem type per situation" as these two existing types, and
  said so on the plan: no catalogue entry is added. A client's forwarded
  write keeps ADR 0214's one answer. ADR 0167 is amended; no ruling changes.
- **Point 2, the tests.** `ForwardingTests` holds `NeverSent` to the
  transport, a refused connection and one closed after the request. A fleet
  theory sends a move through an instance whose system writer's address
  refuses, and one whose address takes the request and never answers.
- **Mutants.**
  - With the release no longer starting the hold-off, the releaser-alone
    test fails: the claim came 1.2 s after the release.
  - With `NeverSent` widened to a closed connection, the move's sent case
    fails.
- **The flake.** `EventMoveTests.A_catalogue_that_does_not_translate_one_to_one_cancels_the_move_and_writes_reopen`
  failed once in a full server run beside a build, and is #254.
- **What ran where.** The fleet tests ran on Azurite, started in the
  session's container from the npm package at 3.37.0, the version CI pins.
  The container has no Docker daemon, so the RustFS leg did not run here:
  CI's `fleet (services)` job runs it.
