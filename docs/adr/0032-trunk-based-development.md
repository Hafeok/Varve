# 0032 — Trunk-based development and non-blocking review

## Status

Accepted. 2026-09-22. **Amended by
[0066](0066-expected-red-pull-requests.md)** (2026-09-25): a change citing a
decision filed without acceptance reaches `main` only through a pull request,
which may arrive red on `CS0618` alone and is merged only green.

Supersedes the pull-request rule written at the milestone 3a close-out. That
rule lived in `CONTRIBUTING.md` and in the never list rather than in an ADR,
which is itself the defect this decision corrects: a rule strong enough to be a
"never" should have had a recorded decision behind it.

## Context

Two of the Mind Over Machine stewardship items are about how work reaches the
trunk:

> **Trunk-Based Development:** Full support for trunk-based development (Pull
> Requests are supported and welcomed, but optional).
>
> **Non-Blocking Reviews:** Support for non-blocking reviews (reviews are
> enabled and required for formal releases, but not for merging to the trunk).

Varve's existing rule was the opposite: "Nothing reaches `main` except through a
pull request. Never commit to `main`, and never push it." with branches named
`milestone/<id>` or `fix/<topic>` and one pull request per milestone part.

That rule was not unreasonable, and it produced three good pull requests. It
also produced exactly the pathology trunk-based development exists to avoid.
`milestone/3b` was a long-lived branch carrying a whole milestone; when it
merged it brought 883 conformance cases, a new reader, a new writer, a
benchmark suite and nine defect classes' worth of fixes in one movement. No
review of that branch could have been a review in any useful sense — the pull
request body was a report to be believed, not a diff to be checked. The work was
sound because the gates said so, which is the honest description of what was
actually reviewing it.

That is the observation underneath the standard's position. **The blocking
review here has always been the automated one.** Ruleset 1 on `main` requires
every gate to pass and permits no bypass: build on both platforms, analyzer
tests, the conformance ratchet, the dependency register, the native-asset gate,
DCO. A human cannot merge past a red ratchet and would not catch what the
ratchet catches. Pretending the human review was the gate, while the gates did
the gating, cost the project long-lived branches and bought nothing.

The place a human decision genuinely belongs is different, and it is not on the
way to the trunk. A version pushed to nuget.org cannot be edited, replaced or
deleted (ADR 0029). *That* is irreversible, and that is where a person should
have to say yes.

## Decision

1. **`main` is the trunk.** Anyone with write access — the maintainer, any
   future maintainer, and any AI session working under this repository's rules —
   commits to `main` directly or through a pull request, **their choice**.
   Neither route is privileged and neither is a lesser form of the other.
2. **The blocking review is the automated one.** Ruleset 1 requires every
   required status check to pass, forbids force pushes and deletion, and has no
   bypass list. That is what a change has to satisfy, and it satisfies it
   identically whether it arrived by push or by pull request.
3. **Human review is required for a release, not for a merge.** `publish.yml`
   runs in the `release` GitHub environment, which has the maintainer as a
   required reviewer. Publishing therefore stops and waits for a person, every
   time, on the one action that cannot be undone.
4. **Pull requests stay welcome.** They are the right tool for a change that
   wants discussion, for anything from outside, and for work an author wants
   read before it lands. What changes is that they are no longer the only door,
   and that an unreviewed pull request does not block on a reviewer who may
   never come.
5. **Small, frequent commits to `main`.** A change that is not ready for the
   trunk lives **behind a feature flag or stays local** — not on a long-lived
   branch. `milestone/3b` is the shape this rule exists to prevent a repeat of.
6. **The branch-naming table is withdrawn.** `milestone/<id>` and `fix/<topic>`
   described a world where every change had a branch. A branch that exists
   today is short-lived and its name is the author's business.
7. **Only the maintainer merges** a pull request, and only the maintainer
   releases. That part of the old rule stands.

This ADR does not change the rulesets, which are GitHub settings held by the
maintainer. It records what they are so that a contributor can predict what will
happen to a push, and so that a later change to them is visibly a change.

### Amendment, 2026-09-24 — the gate runs after the push; ruleset 1 blocks nothing

Point 2 says ruleset 1 "requires every required status check to pass … and it
satisfies it identically whether it arrived by push or by pull request". **The
list of required checks is empty, and has been.** The first export of this
repository's settings (#23, [ADR 0039](0039-repo-standard.md)) showed it: the
ruleset, named `trunk` on GitHub, forbids deletion and force pushes and requires
no check. That is why direct pushes to `main` have been landing.

It cannot be otherwise while point 1 stands. GitHub checks a required status
against the pushed commit itself, and a commit pushed straight to `main` has not
been built yet, so requiring the CI jobs would refuse every direct push — for
everyone, since the ruleset has no bypass. The maintainer weighed that on
2026-09-24 (#27) and **kept point 1**: the route to `main` stays a choice, and
the checks stay off the ruleset.

So the automated review is real, but it is not a lock. **Every gate runs in CI
on every push to `main` and on every pull request; it does not stop a push. A red
trunk is fixed forward before anything else lands.** A pull request shows its
checks before it merges and is the route to take when a change should be seen
green first. The ruleset's strict mode (a branch must be up to date with `main`
before it merges) is switched off in the same change, since with no required
checks it guards nothing.

Revisit if a red trunk is ever left standing: that is the failure this
arrangement trusts people not to cause, and the fix would be required checks
with a bypass for direct pushes, which would supersede this ADR.

## Alternatives considered

- **Keep pull-request-only.** The status quo, and the option with the strongest
  emotional case: a pull request is a checkpoint, and checkpoints feel safe.
  Rejected because the checkpoint was not doing the work. Three merged pull
  requests, three long-lived branches, and in every case the evidence a reviewer
  would have relied on was the gate output quoted in the body. Keeping the rule
  would keep the branches and the illusion, and the standard calls for neither.
- **Pull-request-only with auto-merge once checks pass.** A middle position:
  every change gets a pull request, but nothing waits on a human. Rejected as
  ceremony — it is trunk-based development with an extra API call and a branch
  that exists for ninety seconds. If no human is required to look, the pull
  request is a formality, and a formality that every change must perform is a
  tax on small commits, which are exactly the commits this decision wants more
  of.
- **Required review with a short timeout, falling back to auto-merge.**
  Rejected: on a single-maintainer project the timeout is the only path that
  ever fires, so the rule would describe a review that never happens. It also
  makes the project's behaviour depend on the maintainer's availability, which
  is the coupling trunk-based development removes.
- **Release branches.** A `release/x.y` branch cut from the trunk and stabilised
  separately. Rejected as premature and, on present evidence, permanently
  unnecessary: MinVer versions from the tag (ADR 0029) and the trunk is required
  to be releasable at every commit. A release branch would exist to hold work
  the trunk is not ready for, which is the thing being abolished.

## Consequences

- **`CONTRIBUTING.md` and the never list change.** "A commit on `main`, or a
  push to it. Every change arrives by pull request." leaves the never list. What
  replaces it is narrower and truer: never push a commit that has not passed the
  gates locally, and never leave the trunk un-releasable.
- **The gates carry more weight, and they were already carrying it.** Every
  future rule ships as an analyzer or an `eng/` gate before it is written down,
  which `CONTRIBUTING.md` already required and which now has no human backstop
  behind it. A rule that cannot be automated is advice.
- **An AI session's commits reach `main` the same way a human's do.** There is
  no separate, weaker path for them and no separate, stricter one. What
  distinguishes them is the traceability record (ADR 0033) and the signing
  exception (ADR 0034), neither of which is about review.
- **This makes `main` the only branch anyone needs to look at.** The stale
  `milestone/3a`, `milestone/3b` and `fix/3a-closeout` refs on the remote are
  artefacts of the superseded rule and can be deleted whenever the maintainer
  wants; nothing depends on them.
- **The one irreversible action now blocks on a person**, which it did not
  before this decision made the `release` environment load-bearing. That is a
  net increase in human review, applied where it can still change the outcome.
