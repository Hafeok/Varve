---
set: the-learning-tracks-pages-quote-what-ci-ran
namespace: varve
adr: 0249
decisions:
  - key: LearnBlocksCarryAdr0248Markers
    statement: "Every fenced block in docs/learn/*.md carries ADR 0248's marker on the line before it, and a page with a none has a section Not run in CI; eng/learn.cs holds docs/learn to that grammar"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: QuotedBlocksEqualTheirFiles
    statement: "A marker whose note is output <path> quotes that whole file, and one whose note is source <path>#<region> quotes that region, dedented; the block equals it byte for byte after line endings and trailing whitespace"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: EverySampleRunsAndPrintsItsExpected
    statement: "eng/learn.cs --run builds every samples/learn/NN-* against src/, runs it, and fails on an output section or a final state (canonical N-Quads) that differs from expected/; --trimmed <rid> does so with the trimmed executable"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: NoRecTermBeforeItsChapter
    statement: "No rec: or brick: term, prefixed or as a full IRI, appears in a page or a sample file before the chapter docs/learn/terms.md introduces it in, and every such term has a row"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: UnshippedNamesItsIssue
    statement: "What has not shipped is an unshipped marker naming its issue, followed by a Not in this release quote naming it; the gate fails when that issue is in a released descriptor's basis"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: SamplesReferenceOnlyPackableProjects
    statement: "A sample names Varve only by VarveReference items, each a packable project under src/, resolved to it by default and to the published package with VarveFrom=package; nothing in src/ admits a sample by InternalsVisibleTo"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: SamplesHoldTheConstraints
    statement: "The samples build with the trim, AOT and single-file analyzers and warnings as errors, publish trimmed, read the System.Uri ban list, and are layer-6 composition roots under every DD and VARVE rule"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: SampleDataConformsToPinnedRec
    statement: "The slice, chapter by chapter, and every sample's final state conform to the pinned REC 4 shapes composed as ADR 0176 composes them, and samples/learn/rec.pin's tag and checksum are verified first"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: StateLeavesOutThePinnedVocabulary
    statement: "Amended 2026-11-25: a sample that holds the pinned REC files in a graph of their own names that graph, and its final state is the head without it; the graph is held to the release by rec.pin, not by expected/state.nq"
---

The rulings of [ADR 0249](../adr/0249-the-learning-tracks-pages-quote-what-ci-ran.md),
filed unaccepted by #364 (ADR 0066) and accepted by the maintainer on
2026-11-24, written by his delegate (ADR 0239).
