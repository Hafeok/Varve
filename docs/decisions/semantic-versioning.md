---
set: semantic-versioning
namespace: varve
adr: 0035
decisions:
  - key: StrictSemVer
    statement: "Every published package follows SemVer 2.0.0: major for a break a conforming consumer could observe, minor for compatible new surface, patch for fixes with no surface change"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: ShippedBaselineIsBreakEvidence
    statement: "A removed or altered line in PublicAPI.Shipped.txt is a breaking change, and surface in PublicAPI.Unshipped.txt carries no promise until it ships"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: RatchetIsBehaviourEvidence
    statement: "The conformance ratchet is the behavioural half of the evidence, and an exemption added to make a suite green is an accepted behaviour change"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: SemVerKeptDuringZeroX
    statement: "During 0.x the versioning rules are followed anyway, and a breaking change moves the minor while the major is zero"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: OnePointZeroIsAnApiFreeze
    statement: "1.0 is a public API freeze, reached by the roadmap's definition and not by the number looking ready"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: PackagesVersionTogether
    statement: "Every package versions together from one tag and ships as a set"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0035](../adr/0035-semantic-versioning.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
