---
set: turtle-recovery-and-prefixes
namespace: varve
adr: 0030
decisions:
  - key: StatementIsTheRecoveryUnit
    statement: "On a syntax error the Turtle parser resumes after the next full stop at nesting depth zero outside strings and IRIs, and the failed statement yields no quads"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: StatementQuadsBuffered
    statement: "The parser buffers a statement's quads in its arena and releases them at the statement's end, and bindings made before a failed statement stand"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: PrefixesReportedAsDeclared
    statement: "Prefixes and the base are reported through OnPrefix and OnBase in document order as each directive is read, not returned as a final map"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: IsomorphismCheckInTheHarness
    statement: "Dataset isomorphism for the conformance harness is a backtracking check in Varve.Conformance.Tests, not in Varve.Rdf"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: TurtleRoundTripIsIsomorphic
    statement: "A Turtle round trip is isomorphic, not byte-identical, and byte stability belongs to canonical N-Triples"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0030](../adr/0030-turtle-recovery-and-prefixes.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Moved to a later set: the clause deleting the isomorphism check when RDFC-1.0 lands is
superseded by ADR 0059, in its set.
