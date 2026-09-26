---
set: blank-node-identity-in-process
namespace: varve
adr: 0044
decisions:
  - key: ExistingTermsByHandle
    statement: "A request addresses an existing blank node, or any existing term, by RequestTerm.Existing(handle), and an unknown handle fails the request with an exception and leaves no trace"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: RequestBlankNodesAreFresh
    statement: "A blank node term in a request is always fresh: each distinct label is one new node in that request, even a label TryExternalise produced"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: ExternalisedBlankLabelsFromIds
    statement: "TryExternalise of a blank id gives a label derived from the id, stable within the dataset and no identity across datasets"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: NoTripleTermAroundExistingBlank
    statement: "A request cannot build a new triple term around an existing blank node, a stated limit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: SkolemSchemeWithTheServer
    statement: "The skolem IRI scheme for blank node identity across protocols is decided at milestone 7, with the server"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0044](../adr/0044-blank-node-identity-in-process.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

This answers the in-process half of ADR 0012's open question Q1.
