---
set: store-is-sparql-free
namespace: varve
adr: 0005
decisions:
  - key: StoreIsSparqlFree
    statement: "Varve.Store at layer 4 references no SPARQL package and exposes no query language"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: StoreReadAndWriteContracts
    statement: "The store exposes a quad source contract for reads pinned to a log position and a transaction contract for writes that produces one commit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: SparqlUpdateIsLayer5Integration
    statement: "SPARQL Update lives in a layer 5 integration that evaluates WHERE against a pinned position and submits the resulting delta as one commit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: CompositionsLiveAboveStore
    statement: "The store defines contracts and compositions such as the SHACL and SPARQL Update integrations live above it, and hosts reference the integrations"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: QuadSourceContractInRdf
    statement: "The quad source contract lives in Varve.Rdf at layer 1, not in Varve.Store, because the evaluator at layer 3 needs it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: ValidatorContractInStore
    statement: "The pre-commit validator contract is a store concern at layer 4, and a validator that uses it is a layer 5 composition"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: WidenContractNotMoveEvaluator
    statement: "If the quad source contract is too narrow for an optimiser, it is widened by a superseding ADR, never bypassed by moving the evaluator down a layer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
---

The rulings of [ADR 0005](../adr/0005-store-is-sparql-free.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
