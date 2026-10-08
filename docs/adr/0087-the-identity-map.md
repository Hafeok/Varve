# 0087 — The identity map: who signs off, accepts and approves for an agent

## Status

**Accepted — decided by the maintainer, 2026-10-06, and written by the
pre-release session of #63.** The decision set was filed without
`accepted-by`, which a session never writes (ADR 0066), and the maintainer
accepted it on the pull request on 2026-10-06.

**Enforces ADR [0034](0034-commit-signing-and-the-sandbox-exception.md) point
3**, which until this ADR nothing enforced (0034's amendment of 2026-10-06).
Complements [0062](0062-adopting-decisiondriven-analyzers.md) (the ledger) and
[0066](0066-expected-red-pull-requests.md) (a session never accepts).

**Revisit condition, and planned end:** the map is the **interim form of the
ledger's authority model**. When the `DecisionDriven.Analyzers` generator reads
the ledger itself, the ledger's grants (an identity holding a role with
`accept-decision`) replace this file, and a superseding ADR moves these rules
onto them.

## Context

ADR 0034 requires a DCO sign-off on every commit, "human and AI session
alike", and says whose: for a session, "the directing human's statement". A
session's commits are authored by the session (`Claude
<noreply@anthropic.com>`) and signed off by the human directing it. So a rule
"the sign-off matches the author" would refuse every session commit, and a
rule that only asks for *a* sign-off would accept anybody's.

Two other places have the same question in a different form. A decision a
session files is accepted by a human (ADR 0066), and nothing checked which
human. A pull request of a session's work is reviewed by nobody in particular,
because review was non-blocking (ADR 0032).

All three are the same fact: **for an agent, some human is responsible**, and
possibly others may act for that human. That fact was not written down
anywhere that a gate could read.

## Decision

### The map

`eng/identities.json` lists three kinds of identity.

- **Humans**: an id, the name and the emails they commit and sign off with,
  their GitHub login, and **delegates**, the ids of other humans allowed to act
  for them.
- **Agents**: an id, a name, their author emails, and their **responsible**
  human.
- **Exempt**: automation whose messages cannot be configured, by author name.
  This list is shared by `eng/issue-refs.cs` (until now a hard-coded list) and
  `eng/dco.cs`, and is closed: adding to it is a decision.

An author the map does not know is held to the rules for humans. A
human's delegates change **only in that human's own pull request**.

### Three rules on it

1. **DCO** (`eng/dco.cs`, the `dco` job). Every non-merge commit in the range
   carries a `Signed-off-by` trailer:
   - a human's commit, signed off with the author's own name and email;
   - an agent's commit, signed off with the name and one of the emails of its
     responsible human or a delegate;
   - an exempt author's commit, skipped.

   The failure path is a fixture, `tests/fixtures/dco/`, and the `dco-fixture`
   job in `eng/ci.cs` requires it to fail.
2. **Acceptance authority** (`eng/decision-sets.cs`). A decision first filed by
   a commit an agent authored is accepted only by that agent's responsible
   human or a delegate: its `accepted-by` names one of their emails.
3. **Agent review** (`eng/agent-review.cs`, the `agent review` workflow). A
   pull request containing an agent's commits needs an approving review **on
   its current head** from the agent's responsible human or a delegate. The
   check runs again on every review event. The same check refuses a delegate
   change in anyone's pull request but that human's.
   - The base's map decides both who may approve and whose delegates changed,
     so a change cannot authorise itself.
   - On a branch push there is no pull request, so a head with an agent's
     commits or a delegate change fails there and lands through a pull request.

`dco` and `agent review` are required checks on ruleset 1 (ADR 0088). Rule 2
runs in the `decision-sets` job, which is already a gate.

### Amendment, 2026-10-06 — approval by comment on one's own pull request, and the exit to the ledger

**The Consequences' first point came true at once.** GitHub refuses an
approving review from a pull request's author. #64 was opened under the
responsible human's account, so nobody could approve it, and it was merged
with `agent review` red, before ruleset 1 required the check.

**So rule 3 gains one form of approval, for one case.**
- For a pull request **opened under the responsible human's own GitHub
  login**, a review in the comment state by that human whose body contains
  `approve <head sha>` counts as that human's approval.
- The sha is the current head's, in full or at least seven characters of it.
- A comment naming an earlier head counts for nothing, and so does the same
  comment by anyone else, delegates included.
- A pull request opened by anyone else still needs an approving review from
  the responsible human or a delegate, as before.

The comment is the approval GitHub would not let that human give, made on the
same head and recorded on the same pull request.

**The exit.** When the ledger gates ship (ADR 0086,
[decision-driven-analyzers#84](https://github.com/Hafeok/decision-driven-analyzers/issues/84)),
an approval becomes a **signed ledger `Review` over the head SHA by an identity
holding `Approve`**, and both GitHub forms, the approving review and the
comment, are retired with this map. A signature over the head is what both
forms stand in for: evidence that a particular person approved exactly what
merges. Neither form can give that, because GitHub records who pressed a
button, not what they signed.

### Amendment, 2026-10-06 — the judging script is always main's; approval by comment is not a valid workflow

**The hazard the first amendment left open.** `agent review` ran the pull
request's own `eng/agent-review.cs`, so a pull request that edited it to pass
judged itself. Running main's script under `pull_request` is not enough
either:
- `pull_request` and `pull_request_review` take their workflow file from the
  pull request, so a pull request can rewrite the job instead of the script;
- a required check is matched by name, so any pull request can add a job
  called `agent review` that passes.

**The rule, from this amendment:**

1. **The judging script is always main's.** The job that judges checks out
   main by name and runs main's `eng/agent-review.cs`. It runs only on
   triggers whose workflow is main's: `pull_request_target`, `issue_comment`
   (a new comment), and a push to `land/**`, which only the maintainer may
   push (ruleset `land`). It reads the pull request (head, commits, identity
   map, comments) through the API, as data. Nothing from the pull request is
   checked out, built or run.
2. **Check names are spoofable; only App-pinned checks count.** The verdict
   is a check run `agent review` that the gates GitHub App (checks: write
   only) creates or updates on the head. The App's key is a secret of the
   `gates` environment, which only `main` and `land/**` can reach. Ruleset 1
   requires `agent review` from that App by its integration id.
   `eng/required-checks.cs` holds the pin: a pinned name must be posted by the
   App from a `pull_request_target` workflow, and a name the App posts must be
   pinned. The repo-standard App's key moved to the same environment, so the
   workflow that applies `.github/repo-standard.yaml` mints its token there
   too, and the repository has no Actions secrets of its own.
   **The other required checks are still matched by name only**, so
   a pull request could satisfy any of them with a job of the same name.
   Pinning them is the same work again, gate by gate.
3. **The approval is a conversation comment** containing
   `approve <head sha>`, at least **12** characters of the current head, by
   a holder of Approve for every agent in the pull request: its responsible
   human or a delegate, read from main's map. Review submissions are not
   consulted, and `pull_request_review` is not a trigger. This replaces the
   first amendment's review-comment form and its own-pull-request
   restriction.
4. **Approval by pull-request comment is not a valid workflow.** It is
   tolerated only until the ledger's review gate exists: a signed ledger
   `Review` over the head SHA by an `Approve` holder, as the first amendment
   says. The pull request that adopts that gate removes the comment form.

The test is `eng/agent-review-tamper.cs`, a job of `eng/ci.cs`:
- a pull request that edits `eng/agent-review.cs` to always pass would pass
  under its own copy;
- under main's copy, the same pull request fails;
- the workflow's judging job cannot run anything but main's copy.

Until the pull request after this one, a transitional `pull_request` job
reports for the pull request that introduces this rule, running the base's
script. Once this is on main, the pin means that job cannot satisfy ruleset 1.

### Amendment, 2026-10-07 — the transitional job is gone

The pull request after the second amendment removes the `pull_request`
trigger and the transitional job from `.github/workflows/agent-review.yml`,
with the base-script mode of `eng/agent-review.cs` that served it. `agent
review` now comes only from the gates App's check run, posted by main's
script on `pull_request_target`, `issue_comment` and `land/**` pushes. A
failing verdict is a posted failure, not a failing job. The tamper test
gains two parts: the workflow has no `pull_request` trigger, and no job in it
is named `agent review`.

This amendment also restores the heading `Alternatives considered`, which
the second amendment's commit dropped by mistake. The list below it is
unchanged.

### Amendment, 2026-10-08 — an approved head lands through land/

**Why.** ADR [0102](0102-a-release-is-a-descriptor.md) cuts a release at the
commit that lands its descriptor, and only at an approved head: the merged
head must be the reviewed head. A session's pull request could reach `main`
only by the merge button, whose merge commit is not the approved head,
because a `land/` push with an agent's commits failed rule 3 outright.

**The rule, from this amendment:** on a push to `land/**`, an agent's
commits are admitted **when the pushed head is a pull request's head that
carries the gates App's successful `agent review` check run**. That run is
the pull request's own verdict on that exact sha, posted only by the trusted
paths (`pull_request_target`, `issue_comment`) after the approval comment;
nothing pushed to `land/` can add one, and a push of any other sha, a rebased
or merged one included, is not admitted. The maintainer pushes the approved
head to `land/<name>`, and fast-forwards `main` to it (ADR 0088).

- The delegate rule is unchanged: a delegate change on a push still fails.
- Every other pull request lands as before; using `land/` for one is the
  maintainer's choice.
- The judge reads the head's check runs and pull requests, so its job holds
  `checks: read`.

`eng/agent-review-tamper.cs` gains three cases: an approved pull request's
head passes; a `success` from another App fails; an approved sha that is no
pull request's head fails.

## Alternatives considered

- **Exempt agents from the author match.** Leaves every session commit
  unchecked, against 0034's "no exception".
- **Author session commits as the human**, with the session as co-author.
  Makes the match trivial and the author line false: the author line is the one
  place git records that a session wrote the change.
- **Encode the rules in the ledger now.** The generator does not read grants
  yet; a map now, replaced by grants then, keeps the rules enforceable in the
  meantime.

## Consequences

- **A pull request opened under the responsible human's own account cannot be
  approved by that human.** GitHub refuses a review by a pull request's
  author. Session pull requests are opened under the maintainer's account
  today, so **they need a delegate**, or must be opened by an identity other
  than the maintainer's, before `agent review` can pass. Until one exists, a
  session's pull request cannot merge.
- An approval is on a head. A push after the approval asks for it again, which
  is the point: what was approved is what merges.
- Every commit in a pull request, merged or pushed, now carries a correct
  sign-off. Commits before this ADR are not re-checked; ADR 0034 point 2's
  reasoning about history applies.

## Checks

- **Checked against the accepted ADRs** (0001–0086). Enforces **0034** point 3;
  consistent with **0033** (the exempt list's reasoning), **0062** and
  **0066** (acceptance is a human act); depends on **0088** for the required
  checks. No conflict.
- **Layer ownership.** None; build and process.
- **Analyzer rule.** None: the rules read git history, the map and GitHub's
  reviews, which no compilation sees.
- **Open questions owned.** None.
