# 0066 — Expected-red pull requests: citing a decision nobody has accepted yet

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the adoption plan
(issue [#43](https://github.com/Hafeok/Varve/issues/43)).

**Amends ADR [0032](0032-trunk-based-development.md)**: the trunk stays green,
and a change of this one kind reaches it only through a pull request.

**Amends ADR [0034](0034-commit-signing-and-the-sandbox-exception.md)**: the maintainer's acceptance
edit on a session's branch is the review of that session's new decisions, and
it is a signed human commit.

## Context

Under `DecisionDriven.Analyzers` (ADR [0062](0062-adopting-decisiondriven-analyzers.md)),
a decision filed without `accepted-by` is generated as `[Obsolete(error:
false)]`, so every citation of it is `CS0618`. The repository builds with
`TreatWarningsAsErrors`, so `CS0618` fails the build. That is the mechanism
working as designed (`DecisionsAsTypes.UnacceptedEmitsWarningObsolete`). Code
that rests on a decision no human has accepted cannot ship.

It collides with how work reaches the trunk.

- **The sessions that do the work cannot accept.** Sessions 2 and 3 of #43
  will find contracts, pools and boundaries with no decision behind them, and
  the honest response is to file one. An AI session cannot accept it: GOVERNANCE
  names one person who decides, and an acceptance is that person's act.
- **So the session's branch is red, by construction**, and it stays red until
  the maintainer accepts.

ADR 0032 says a red trunk is fixed forward before anything else lands, and
the gates run on every pull request. It does not say what a pull request that
*must* arrive red is. ADR 0034 says a cloud session bound to a branch lands
through a pull request, which the maintainer merges.

There are two ways to make the red go away that are worse than the red:

- a committed `WarningsNotAsErrors` for `CS0618`, which would let unaccepted
  decisions ship forever and silently;
- a session writing `accepted-by` itself, which is forging the one act the
  mechanism exists to reserve to a person.

## Decision

1. **A pull request may arrive red when, and only when, every failure is
   `CS0618` from citing a decision in `docs/decisions/` that was filed without
   acceptance.** Nothing else excuses a red pull request:
   - a `DD` or `VARVE` diagnostic;
   - an IL-prefixed diagnostic;
   - a failing test, the ratchet, the register or any other gate.

   The PR body lists the unaccepted decisions it cites, by `Set.Key`, with the
   citation sites.
2. **The maintainer's review is the acceptance.** The maintainer reads each
   filed decision. For each one accepted, the maintainer adds `accepted-by` and
   `accepted-at` to its entry, on the pull request's branch, as their own
   commit. That commit is signed like any human commit (ADR 0034). A decision
   the maintainer does not accept is not accepted. The citing code changes,
   or the pull request waits.
3. **The pull request is merged only green.** The acceptance commits turn it
   green. A pull request still carrying `CS0618` is not merged. The trunk is
   never red because of this ADR, so ADR 0032's "fixed forward" is never
   invoked for it.
4. **A change of this kind reaches `main` only through a pull request.** It
   never arrives by direct push, which ADR 0032 otherwise allows: a direct push
   of an unaccepted citation would make the trunk red with no place for the
   acceptance to happen first.
5. **No committed configuration may downgrade `CS0618`.** That means no
   `WarningsNotAsErrors`, `NoWarn`, `.editorconfig` severity or `#pragma` for
   it, anywhere in the repository.
6. **Verifying such a branch locally with a command-line override is allowed**
   and is not configuration. For example,
   `dotnet build Varve.slnx -c Release -p:WarningsNotAsErrors=CS0618` checks that
   nothing but `CS0618` is left. It lives in a terminal and in a PR body's
   evidence, never in a file. CI runs without it.
7. **A session never writes `accepted-by`.** Session 1 of #43 transcribes
   acceptances that already exist. Each Varve ADR's own status and date record
   the maintainer's acceptance, and the transcription is reviewed in its pull
   request. That is copying an existing acceptance, and it is the only form in
   which a session touches the field.

## Alternatives considered

- **`WarningsNotAsErrors=CS0618` in `Directory.Build.props`.** Everything
  green, always. Rejected: it removes the only thing that stops an unaccepted
  decision from shipping. It is also exactly the repo-wide downgrade ADR 0004
  forbids for the rules that matter.
- **File decisions only in separate pull requests, accepted before the code
  that cites them.** A green pull request every time, and the rule stays
  simple. Rejected: a decision is best judged beside the code that needed it,
  and splitting them makes the maintainer accept an abstraction without its
  first use. It also doubles the round trips for every finding a session
  sorts.
- **Let the session accept, with the maintainer's review of the PR as the
  real acceptance.** Rejected: the ledger would record an acceptance by nobody
  who could give it. `accepted-by` is an identity, and a session has no
  identity that may accept.
- **Allow a red trunk until the next acceptance.** It is what ADR 0032's
  direct push would produce. Rejected: the trunk is required to be releasable
  at every commit (ADR 0032, point 5), and a red build is not releasable.

## Consequences

- **Sessions 2 and 3 of #43 arrive red**, with the list of decisions awaiting
  the maintainer in the body. Review time is spent on decisions, not diffs.
- **The CI result of such a pull request is informative only once it is
  green.** Before that, the evidence that nothing but `CS0618` is failing is
  the local override run quoted in the body. The maintainer can reproduce it
  with the same command.
- **The first acceptances by a human commit** appear in `docs/decisions/` as
  edits on session branches, which is where the ledger will later record them
  natively.
- **A decision the maintainer rejects** leaves the pull request red until the
  code stops citing it. That is the intended pressure.

## Checks

- **Checked against the accepted ADRs** (0001, 0003–0005, 0007–0018,
  0021–0065). Touches:
  - **0004**: no repo-wide downgrade, extended to `CS0618`.
  - **0032**: amended as above. The trunk is still green at every commit.
  - **0033**: every commit still carries its issue reference and the session
    its traceability record.
  - **0034**: amended as above. The signing exception for sessions is
    unchanged, and the acceptance is a signed human commit.
  - **0062**: the mechanism this ADR accommodates.
- **Layer ownership.** None.
- **Analyzer rule.** None. The compiler's `CS0618` and
  `TreatWarningsAsErrors` are the enforcement. `DD0008` does not cover
  `CS0618`, because it is not a `DD` rule. Point 5 is kept by review, and by
  the fact that any downgrade is a one-line diff in a shared file.
- **Open questions owned.** None.
