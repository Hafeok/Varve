---
set: property-based-testing
namespace: varve
adr: 0025
decisions:
  - key: CsCheckForProperties
    statement: "Property-based tests use CsCheck, a test-only package chosen for having no dependencies"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: GeneratorsReviewedAsTests
    statement: "A generator is reviewed as carefully as its property, and term generators produce escapes, surrogate pairs, directional language tags, ucschar IRIs and nested triple terms"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0025](../adr/0025-property-based-testing.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
