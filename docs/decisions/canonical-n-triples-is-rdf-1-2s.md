---
set: canonical-n-triples-is-rdf-1-2s
namespace: varve
adr: 0061
decisions:
  - key: CanonicalFormIsRdf12s
    statement: "Canonical N-Triples is RDF 1.2 N-Triples section 3's form, and canonical N-Quads is the same form plus the graph label"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: CanonicalFormSpelling
    statement: "The canonical form puts one space after each term and one LF per line, lowercases language tags with --ltr or --rtl, writes triple terms as <<( s p o )>>, drops xsd:string, and escapes as RDF 1.2 does"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: Rdf12WinsWhereFormsDiffer
    statement: "Where the RDF 1.1 and 1.2 canonical forms differ, 1.2 wins, and everything RDF 1.1 N-Triples accepts is still read"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: CanonicalWritersByteIdentical
    statement: "Varve.Rdf's RDFC-1.0 term writer and Varve.Turtle's canonical writer stay two, and a property holds them byte-identical"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: C14nCasesUnderTheRatchet
    statement: "The 82 RDF 1.2 c14n cases are under the ratchet with guard counts of 41 per manifest"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: TurtleWriterKeepsNarrowEscapes
    statement: "Turtle has no canonical form, and its writer keeps RDF 1.1's narrower escape set"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0061](../adr/0061-canonical-n-triples-is-rdf-1-2s.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
