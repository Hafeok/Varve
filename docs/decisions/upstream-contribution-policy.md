---
set: upstream-contribution-policy
namespace: varve
adr: 0038
decisions:
  - key: SpecOverOxigraph
    statement: "Oxigraph is the tie-breaker only where the governing specification is silent or ambiguous, and where the specification decides, Varve follows it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: DifferentialTriage
    statement: "Every differential disagreement is exactly one of varve-defect, upstream-defect, spec-gap or intentional-divergence, and varve-defect is never exempted"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: UpstreamDestination
    statement: "Upstream defects go to Oxigraph's tracker one per issue, spec gaps to the W3C suites or the working group, and design findings to Oxigraph Discussions, each linked from a Varve issue"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: UpstreamLicensing
    statement: "Code crosses into Oxigraph only from its copyright holders contributing directly, tests go to the W3C suites under their terms, and shared tooling stays in Varve under MPL-2.0"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: StaleExemption
    statement: "An exempted differential disagreement that no longer reproduces fails the harness, and its entry is removed"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: DifferentialExemptionsChecked
    statement: "The differential harness fails on an exemption with no or an unknown category, missing references or the category varve-defect"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0038](../adr/0038-upstream-contribution-policy.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

The keys are the ADR's own decision ids, D1 to D5, in PascalCase. The open question on
spec-gap references is not a ruling.
