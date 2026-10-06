---
set: a-release-per-milestone
namespace: varve
adr: 0085
decisions:
  - key: EveryMilestoneEndsInARelease
    statement: "A milestone is a section of eng/changelog-sections.txt, started when its line is added, and the milestone before it is finished by a v* tag on main published by publish.yml"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: ReleaseCutByTheChangelogTool
    statement: "A release is cut by eng/changelog.cs --release, which moves Unreleased into a dated section with compare links; the cut is committed and that commit is tagged"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: ReleasedSectionsAreFixed
    statement: "A released section of CHANGELOG.md is copied as it stands by every later run, and only Unreleased is regenerated, from the commits after the newest release"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: ReleaseNotesAreTheChangelogSection
    statement: "publish.yml checks that the tagged CHANGELOG.md has the version's section and, after the push, creates the GitHub release with that section as its notes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: NextMilestoneWaitsForTheRelease
    statement: "The release pending gate fails a change that adds a section while the base's newest section has no v* tag at or after its first commit; a change adding no section passes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
---

The rulings of [ADR 0085](../adr/0085-a-release-per-milestone.md), filed unaccepted by the
pre-release session of #63 (ADR 0066).
