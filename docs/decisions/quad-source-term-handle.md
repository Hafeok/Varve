---
set: quad-source-term-handle
namespace: varve
adr: 0022
decisions:
  - key: OpaqueTermHandle
    statement: "The quad source contract works over TermHandle, an opaque 64-bit handle defined in Varve.Rdf that carries no meaning at layer 1"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: InternaliseAndExternalise
    statement: "A source maps a term to its handle with TryInternalise and back with TryExternalise, which answers false for a shredded private term"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: SourceSuppliesEquality
    statement: "Handle equality comes from the source's TermComparer, and a consumer never compares handles with =="
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: PrivateTermsCompareByPlaintext
    statement: "A readable private term compares by its plaintext term against private and canonical terms alike, and a shredded one is equal only to itself"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: TermComparerIsTermEquality
    statement: "TermComparer is RDF term equality, never value equality, which is the evaluator's at layer 3"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: HandleFixedWidthNotGeneric
    statement: "The handle is a fixed 64-bit type rather than a generic parameter, so there is one evaluator and no generic virtual method for AOT to resolve"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: InMemoryDatasetInternsItsOwn
    statement: "An in-memory dataset without a store brings its own interning table, and nothing about the contract presumes a log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0022](../adr/0022-quad-source-term-handle.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

The revisit condition was judged on 2026-09-25 by ADR 0050's rule and did not fire; a
judgement is not a ruling and is not enumerated. ADRs 0049 and 0050 widen the contract
with members of their own, in their sets.
