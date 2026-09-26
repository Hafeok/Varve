---
set: trunk-based-development
namespace: varve
adr: 0032
decisions:
  - key: MainIsTheTrunk
    statement: "main is the trunk, and anyone with write access commits to it directly or through a pull request, their choice"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: GatesRunAfterThePush
    statement: "Every gate runs in CI on every push to main and every pull request without stopping a push, and a red trunk is fixed forward before anything else lands"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: HumanReviewGatesReleases
    statement: "Human review is required for a release, through the release environment, and not for a merge"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: NoLongLivedBranches
    statement: "Work not ready for the trunk lives behind a feature flag or stays local, never on a long-lived branch"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: BranchNamesAreTheAuthors
    statement: "The branch-naming table is withdrawn, and a short-lived branch's name is its author's business"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: OnlyTheMaintainerMergesAndReleases
    statement: "Only the maintainer merges a pull request and only the maintainer releases"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0032](../adr/0032-trunk-based-development.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

`GatesRunAfterThePush` is point 2 as the 2026-09-24 amendment corrected it: the ruleset
requires no check, so the gates bind after a push rather than before it. ADR 0066's
amendment is in 0066's set.
