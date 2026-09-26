---
set: delta-composition-and-closure-over-triple-terms
namespace: varve
adr: 0047
decisions:
  - key: CompositionOverExactChains
    statement: "Delta composition has identity (empty, empty) and is associative over chains of exact deltas, including any run of a log, and not over arbitrary deltas"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: AllocationsReachable
    statement: "Every id in a commit's alloc is reachable from its A or its metadata, directly or as a component of an entry that is (I3)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: NonAssociativityWitnessRuns
    statement: "The specification's counterexample to associativity runs as a named test"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0047](../adr/0047-delta-composition-and-closure-over-triple-terms.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
