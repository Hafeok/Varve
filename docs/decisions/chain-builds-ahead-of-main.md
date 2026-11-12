---
set: chain-builds-ahead-of-main
namespace: varve
adr: 0243
decisions:
  - key: FeatureTreeRootedAtMain
    statement: "Unlanded features are chain/<feature> branches, each declaring exactly one parent, main at a release or a chain branch at one of its stable tags, and branches never join"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: ChainBranchesAreOptIn
    statement: "Chain branches are opt-in; independent work stays on its session branch with a pull request to main, and nothing in flight is migrated"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: AcceptedDecisionsGateStacking
    statement: "A feature may have children only once its whole decision set is accepted at the stable tag they fork from, a stable point implies its own decisions are accepted, and depth is within a backstop of 3 changed only by dated amendment"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: StableTagsCutByWorkflowOnRequest
    statement: "A point is stable when every gate is green on its head, and its immutable tag is cut by a workflow on a request naming that head, after verifying every gate on that exact sha, never by hand and never on every green head"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: ThreeBuildClasses
    statement: "A release is built from main through its descriptor to nuget.org and is read forever; a chain build is built from a chain stable tag to a GitHub Release marked prerelease and is not; a CI artifact is built from any commit; chain builds never go to nuget.org"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: ChainBuildsPublishPackagesAndSbomsOnly
    statement: "A chain build publishes .nupkg files and their SBOMs only, with no container image and no WASM bundle under a chain version until a dated amendment adds one"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: UnstableBuildMarkerIsAReservedHeaderByte
    statement: "A chain build sets a reserved header byte in every file header it writes, which every release refuses, and writes only into a store that already carries it or that it creates; it is a format change under the format inventory"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: PathEncodedChainVersions
    statement: "A chain build's version is <base>.chain.<feature>.<build>[.<feature>.<build>]..., read from the tag chain/<version>, sorting above its parent and base preview and below the next preview, with feature names in [0-9a-z-]"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: ChainBranchesAreNeverRewritten
    statement: "A chain branch is never rebased, squashed or force-pushed, its tags never move, it merges main forward, a child never lands before its parent, and it lands by fast-forward or merge commit, never by squash"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: InterferenceByBoundedTrialMerge
    statement: "Every non-ancestor pair of chain branches gets a git merge-tree check, a build and test runs one at a time only where declared touches overlap or the merge changes the same project, and recorded interference blocks landing of the later of the two"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: TheChainGraphIsCheckedByShacl
    statement: "The chain graph is derived on every run from .chain/feature.ttl and git, never a source of truth, and validated by a SHACL shapes graph on a pinned released Varve, in the namespace https://w3id.org/varve/chain# until the gate moves to how-we-work"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: LandingTrains
    statement: "Two or more approved, individually green pull request heads may land as one train: reproducible Landing-only merge commits onto main in a stated order, gated once on the train's head, at most one release descriptor as the last member, and a red train landing nothing"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
  - key: AllocatorsDesignedOut
    statement: "ADR numbers are assigned at landing by land.cs in a reproducible Landing-only renumber commit, the spec version and changelog cut belong to the release descriptor, and *.Unshipped.txt merge by union"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-12T00:00:00Z
---

The rulings of [ADR 0243](../adr/0243-chain-builds-ahead-of-main.md),
filed unaccepted by the session for #230 (ADR 0066) and accepted on #280 on
2026-11-12, at the maintainer's word, by his delegate (ADR 0239). The maintainer decided
them on #230 (2026-11-06) and in its review (2026-11-11); acceptance is his
act on the pull request, or his delegate's for a decision he has read
(ADR 0239).
