---
set: realestatecore-as-the-reference-domain
namespace: varve
adr: 0141
decisions:
  - key: RecVendoredAsTestData
    statement: "REC 4's SHACL release is vendored into testdata/rec/ at a pinned tag with checksums and its BSD-3-Clause notice in THIRD-PARTY-NOTICES; no package references it and no shipped assembly contains it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: DeterministicPortfolioGenerator
    statement: "A seeded, test-only generator produces REC portfolios with a history of renovations, moved assets, added and retired points and tenants entering and leaving, the same bytes for the same seed and size on every platform"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: StatedSizes
    statement: "Sizes S, M and L are the stated datasets for benchmarks: about 200 thousand, 5 million and 50 million quads at the head over 1, 25 and 100 thousand commits"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: TelemetryStaysOutside
    statement: "Sensor-rate ObservationEvents are telemetry and belong in an Eventhouse joined on the point IRI; the samples show that split"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: RecIsATestCaseNotAFeature
    statement: "Varve stays ontology-agnostic: no REC-specific code ships, a Varve defect a REC test finds is fixed in Varve, and a REC defect is reported upstream with a written exemption"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
---

The rulings of [ADR 0141](../adr/0141-realestatecore-as-the-reference-domain.md), filed unaccepted by
milestone Object storage of #92 (ADR 0066).
