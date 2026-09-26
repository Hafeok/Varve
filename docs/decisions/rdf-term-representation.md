---
set: rdf-term-representation
namespace: varve
adr: 0024
decisions:
  - key: TermViewIsARefStruct
    statement: "RdfTermView and QuadView are readonly ref structs valid only for the parse callback, so a use after it fails to compile"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ViewsNestByArena
    statement: "A term view nests by arena: pooled term slots for the current quad addressed by index, so a triple term's components are other slots"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: OwnedTermIsASealedClass
    statement: "RdfTerm is a sealed class with static factories and no public constructor that owns its bytes and caches its hash"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: MaterialiseIsTheOnlyCrossing
    statement: "An owned term is made only on request, by RdfTermView.Materialise() or IQuadSource.TryExternalise, and nothing on the streaming path calls either"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: OwnedTermEqualityIsTermEquality
    statement: "RdfTerm equality is RDF 1.1 Concepts section 3.3 term equality, permanently, and Varve.Xsd's value equality never changes it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: XsdStringIsFoldedAway
    statement: "An explicit xsd:string datatype is folded away as a spelling of the same term, and rdf:langString and rdf:dirLangString are refused as explicit datatypes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0024](../adr/0024-rdf-term-representation.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
