---
set: rdfc-in-varve-rdf-and-its-work-limit
namespace: varve
adr: 0059
decisions:
  - key: RdfcInVarveRdf
    statement: "RDFC-1.0 is public API in Varve.Rdf over IQuadSource, returning the canonical N-Quads bytes and the issued identifiers, SHA-256 by default with SHA-384 and SHA-512 selectable"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: RdfcWorkLimit
    statement: "A configurable work limit counting hash calls and permutations per blank node that needs them, defaulting to 1,000 from the suite's measured maxima, throws CanonicalisationLimitException when exceeded"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: CanonicalEqualityInTheHarness
    statement: "Canonical equality is the conformance harness's comparison for CONSTRUCT, DESCRIBE and update results"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: IsomorphismCheckKeptAsCrossCheck
    statement: "The backtracking isomorphism check stays in Varve.Conformance.Tests beside RDFC-1.0 and must agree on every case, an Inconclusive being no disagreement"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: IsoIffCanonProperty
    statement: "The property that two datasets are isomorphic exactly when their canonical forms are equal is the two implementations' differential test"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: CanonicalNQuadsWriterIsInternal
    statement: "The canonical N-Quads writer is Varve.Rdf's own and internal"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: RdfcRefusesBlankInTripleTerm
    statement: "RDFC-1.0 refuses a triple term with a blank node inside it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0059](../adr/0059-rdfc-in-varve-rdf-and-its-work-limit.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

`IsomorphismCheckKeptAsCrossCheck` supersedes ADR 0030's deletion clause and moved here from
0030's set.
