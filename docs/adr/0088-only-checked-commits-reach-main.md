# 0088 — Only checked commits reach main

## Status

**Accepted — decided by the maintainer, 2026-10-06, and written by the
pre-release session of #63.** The decision set was filed without
`accepted-by`, which a session never writes (ADR 0066), and the maintainer
accepted it on the pull request on 2026-10-06.

**Supersedes [0032](0032-trunk-based-development.md)**, with its amendment of
2026-09-24. 0032's rulings that still stand move here (below). ADR
[0066](0066-expected-red-pull-requests.md)'s amendment of 0032 applies to this
ADR unchanged: a pull request citing an unaccepted decision may arrive red on
`CS0618` alone, and merges only green.

**Revisit condition:** GitHub refusing a fast-forward of `main` to a commit
whose required checks have passed on a `land/**` branch. That is how GitHub
documents required checks (they are read off the commit, wherever they ran),
and this ADR has not yet seen it happen. The first human landing is the test,
and if it fails this ADR is revisited before anything else lands.

## Context

### What ruleset 1 actually did

**Ruleset 1 never refused a direct push for a failing check.** Its required
check list has been empty since its first export (#23, ADR 0039), and the
2026-09-24 amendment of 0032 recorded that and kept it so. The earlier
description was wrong, not the ruleset: 0032 point 2 said it "requires every
required status check to pass", and 0032's Context listed what it required —
"build on both platforms, analyzer tests, the conformance ratchet, the
dependency register, the native-asset gate, DCO". **None of these was
required, and DCO was not even a check**: nothing in the repository read a
`Signed-off-by` trailer until ADR 0087.

So the automated review was real but ran after the fact. A red trunk was fixed
forward, and a commit that broke a gate was on `main` before any gate saw it.

### What changed

ADR 0087 adds two checks about who stands behind a change, `dco` and
`agent review`. A check about authority that runs after the push protects
nothing: the commit has already landed. And 0032's reason for keeping checks
off the ruleset ("a pushed commit has not been built yet") turns out to be a
reason to build it first, not a reason to skip the checks.

## Decision

1. **`main` accepts only a commit whose SHA already has passing required
   checks.** Ruleset 1 (`trunk`) requires every gate by its job name, `dco` and
   `agent review` included, and keeps **no bypass**. The list is in
   `.github/repo-standard.yaml`, so merging a change to it applies it. Not
   required: the durability jobs, which measure and gate nothing, and the
   decision report, which never gates (ADR 0062).
2. **A human lands a change in one of two ways.**
   - Push it to a short-lived branch named `land/<anything>`, let CI run there
     (`ci.yml` and the agent-review workflow run on `land/**` pushes and check
     the whole range from `main`), then fast-forward `main` to that SHA:
     `git push origin <sha>:main`.
   - Or open a pull request and merge it, with no review needed when every
     commit is the human's own.

   Both routes are the human's choice, as in 0032.
3. **Cloud sessions are unchanged.** A session lands through a pull request
   from its branch, as 0034's amendment of 2026-09-23 already says. That pull
   request carries agent commits, so `agent review` requires an approval from
   the session's responsible human or a delegate (ADR 0087).
4. **Kept from 0032**:
   - `main` is the trunk;
   - there are no long-lived branches;
   - human review gates a release;
   - only the maintainer merges a pull request and only the maintainer
     releases.

   Changed from 0032:
   - **review is non-blocking for a human's own commits and blocking for an
     agent's**;
   - a branch's name is its author's business, except that `land/` marks a
     branch whose head is meant for `main`.

## Alternatives considered

- **Keep 0032**: checks after the push, fixed forward. Rejected: it cannot
  protect `main` from a commit nobody stands behind.
- **Required checks with a bypass for the maintainer.** Rejected: a bypass is
  how the one person most likely to push straight to `main` skips the checks.
  `land/**` costs one CI run and no exceptions.
- **Pull requests for everyone.** Rejected: a review-free pull request is
  allowed by point 2, but not required. A checked SHA, however it got its
  checks, is what this ADR asks for.

## Consequences

- **A required check's name is part of the ruleset.** Renaming a job, or a
  matrix entry, without renaming it in `repo-standard.yaml` in the same change
  leaves `main` unable to accept anything. The same holds for a job that stops
  reporting.
- **The release commit (ADR 0085) lands the same way**: through a pull request
  or a `land/` branch, then the tag.
- `eng/issue-refs.cs`, `eng/dco.cs` and `eng/release-pending.cs` check a push
  to any branch but `main` from `main`, not from the previous push. What a
  required check on a branch's head vouches for is everything that head would
  bring to `main`.
- CI runs twice for a branch pushed and then fast-forwarded: once on the branch
  and once on `main`. The second run is a record, not a gate.

## Checks

- **Checked against the accepted ADRs** (0001–0087). **Supersedes 0032.**
  Consistent with **0034** (sessions land through pull requests), **0039**
  (the ruleset is declared, and a merge applies it), **0066** (red on
  `CS0618` only, merged green) and **0087** (the checks it requires). No
  conflict.
- **Layer ownership.** None; build and process.
- **Analyzer rule.** None.
- **Open questions owned.** None.
