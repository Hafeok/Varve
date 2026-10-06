---
set: home-of-the-eng-gates
namespace: varve
adr: 0086
decisions:
  - key: EngHoldsVarveSpecificGates
    statement: "eng/ is the home of the Varve-specific gates (the conformance ratchet and exemptions, the guard counts and harnesses, the benchmarks, the decision report, and ci.cs as the orchestrator) and hosts generic gates only until they are ported"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: ProcessGatesPortToHowWeWork
    statement: "The process gates (changelog with the release cut, licence headers, issue references, DCO, package metadata, native assets, release pending) are ported to mindovermachine-dev/how-we-work beside repo-standard"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: LedgerGatesPortToTheAnalyzerRepository
    statement: "The ledger gates (register citations, and the decision-set checks the generator does not make) are ported to the analyzer repository beside DecisionDriven.Report, citing decision keys rather than ADR numbers"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: ScriptLeavesWhenItsPortIsConsumed
    statement: "A script leaves eng/ when its port is released and Varve consumes it from there; one issue in each destination, with the provenance on Varve's side, is the exit criterion"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
---

The rulings of [ADR 0086](../adr/0086-home-of-the-eng-gates.md), written by the pre-release
session of #63 at the maintainer's decision, filed without `accepted-by` (ADR 0066).
