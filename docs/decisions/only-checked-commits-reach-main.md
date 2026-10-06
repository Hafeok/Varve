---
set: only-checked-commits-reach-main
namespace: varve
adr: 0088
decisions:
  - key: MainIsTheTrunk
    statement: "main is the trunk, and anyone with write access lands on it through a pull request or by fast-forwarding it to a checked commit, their choice"
  - key: MainAcceptsOnlyCheckedCommits
    statement: "main accepts only a commit whose SHA already has passing required checks; ruleset 1 requires every gate by job name and has no bypass"
  - key: HumansLandThroughALandBranchOrAPullRequest
    statement: "A human lands a change by pushing it to a land/ branch and fast-forwarding main to its checked head, or through a pull request, which needs no review when every commit is the human's own"
  - key: HumanReviewGatesReleases
    statement: "Human review is required for a release, through the release environment, and for a pull request with an agent's commits, and not otherwise for a merge"
  - key: NoLongLivedBranches
    statement: "Work not ready for the trunk lives behind a feature flag or stays local, never on a long-lived branch"
  - key: BranchNamesAreTheAuthors
    statement: "A short-lived branch's name is its author's business, except that land/ marks a branch whose head is meant for main"
  - key: OnlyTheMaintainerMergesAndReleases
    statement: "Only the maintainer merges a pull request and only the maintainer releases"
---

The rulings of [ADR 0088](../adr/0088-only-checked-commits-reach-main.md), which supersedes ADR 0032,
written by the pre-release session of #63 at the maintainer's decision and filed without
`accepted-by` (ADR 0066). Five keys move here from 0032's set under their own names, three of
them with their statements changed by the supersession. `GatesRunAfterThePush` is not carried:
`MainAcceptsOnlyCheckedCommits` replaces it, and 0032 now has no ruling in force, so it has no
set file.
