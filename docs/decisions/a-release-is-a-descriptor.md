---
set: a-release-is-a-descriptor
namespace: varve
adr: 0102
decisions:
  - key: EveryMilestoneEndsInARelease
    statement: "Every milestone ends in a release: the pull request that closes a milestone issue carries the release descriptor whose basis names it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ReleaseProposedByItsDescriptor
    statement: "A release is proposed by adding releases/<version>.yaml to a pull request, format 1 in docs/releases.md, and cut by landing that pull request at its approved head; no other step, credential or person is in the path"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: BasisResolvesAtTheCommitUnderRelease
    statement: "A descriptor's basis resolves at the commit under release: each milestone issue it names closed by a trailer in the range, exactly the ADRs first shipped and each Accepted, and the storage format equal to Varve.Store's"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: VersionsAreNeverReused
    statement: "A version comes after every v* tag by precedence and is never reused; a cut descriptor is immutable, and a correction is a new version"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: CutOnlyAtAnApprovedHead
    statement: "A release is cut only at a commit carrying the gates App's successful agent review check run; a descriptor landed by a merge commit is recovered by a retro-cut naming the approved head"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: EveryGateRunsAtTheCommitUnderRelease
    statement: "Every gate of ci.yml runs at the commit under release before it is tagged, and a valid descriptor whose gates fail is not cut and leaves no tag"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: TheGatesAppTags
    statement: "The tag and the GitHub Release are created by the gates App with an installation token, never by GITHUB_TOKEN and never under a person's or a composite identity"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ChangelogIsTheProjectionOfReleases
    statement: "CHANGELOG.md is rendered from releases/ alone, a descriptor's date, title and summary per version, and is checked byte for byte; it is never edited by hand"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ReleaseNotesAreTheDescriptorSummary
    statement: "The GitHub Release's notes are the descriptor's summary and its title is the tag message, read by publish.yml before anything is pushed and published after the NuGet push"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: HumanReviewGatesReleases
    statement: "Human review gates a release through the approval, on its head, of the pull request that adds its descriptor, and a pull request with an agent's commits; it is not otherwise required for a merge, and the release environment has no reviewer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: AMilestoneIsCompleteWhenReleased
    statement: "A milestone is complete when a release descriptor names its issue in basis; the cut closes the issues its basis names, with the gates App's token"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ReleasesBeforeDescriptorsAreRecorded
    statement: "A version tagged before descriptors existed is recorded by a descriptor pinned to its tagged commit, which is checked for its shape and storage format and never cut"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ClosingKeywordsOnlyAsTrailers
    statement: "A closing keyword with an issue number stands only as a trailer line of its own in a commit message, and eng/issue-refs.cs refuses it anywhere else"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: StatusIsACheckedProjection
    statement: "README.md's Status section and docs/roadmap.md's milestone headings are checked against the release descriptors, the conformance baseline and the projects, in CI and at the commit under release"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
---

The rulings of [ADR 0102](../adr/0102-a-release-is-a-descriptor.md), filed without
`accepted-by` by the release-descriptor session of #73 (ADR 0066).

It supersedes [ADR 0085](../adr/0085-a-release-per-milestone.md) whole. `EveryMilestoneEndsInARelease`
moves here under its key, with its statement changed: the milestone ends in a release
proposed in the pull request that closes it. 0085's other four rulings are not carried:
`ReleaseCutByTheChangelogTool` is replaced by `ReleaseProposedByItsDescriptor`,
`ReleasedSectionsAreFixed` by `ChangelogIsTheProjectionOfReleases`,
`ReleaseNotesAreTheChangelogSection` by `ReleaseNotesAreTheDescriptorSummary`, and
`NextMilestoneWaitsForTheRelease` by the restated `EveryMilestoneEndsInARelease`. 0085 has no
ruling in force, so it has no set file.

`HumanReviewGatesReleases` moves here from [ADR 0088](../adr/0088-only-checked-commits-reach-main.md)'s
set with its statement changed: the review is the descriptor's approval, not the `release`
environment's reviewer.
