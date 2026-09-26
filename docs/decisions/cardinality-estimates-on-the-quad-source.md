---
set: cardinality-estimates-on-the-quad-source
namespace: varve
adr: 0049
decisions:
  - key: EstimateOnTheQuadSource
    statement: "IQuadSource in Varve.Rdf gains Estimate over the pattern Match takes, returning a CardinalityEstimate that is exact, unknown, or a count the source has reason to believe"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: ExactEstimateIsExact
    statement: "An estimate marked exact equals the number of quads Match would yield for the pattern at the source's current state"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: UnknownEstimateIsHonest
    statement: "A source that cannot estimate says unknown rather than guessing, and the consumer falls back as if the member did not exist"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: EstimatedCountIsDocumented
    statement: "An estimate that is neither exact nor unknown is a count the source's documentation says how it derived"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: EstimateCostDocumented
    statement: "An estimate's cost is bounded by the source's documentation, and Match is not a permitted implementation in a source that claims to be an index"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: EstimateIsNeverACount
    statement: "An estimate counts a source's quads and is never used as the answer to a SPARQL COUNT"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
---

The rulings of [ADR 0049](../adr/0049-cardinality-estimates-on-the-quad-source.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Moved to a later set: the type of `Count` is superseded by ADR 0065's `QuadCount`, in its set.
