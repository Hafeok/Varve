---
set: validator-contract-and-overlay
namespace: varve
adr: 0017
decisions:
  - key: OverlayIsALayer1QuadSource
    statement: "Overlay(B, (A, R)) = (B minus R) union A is a quad source in Varve.Rdf at layer 1, merged at scan time and exact under the effective-delta invariant"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: OneOverlayImplementation
    statement: "One overlay implementation serves as-of reads, diff and pre-commit validation"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ValidatorSeesOverlayAndDelta
    statement: "A pre-commit validator reads Overlay(G_head, delta) and the delta, and nothing else"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ValidatorAcceptsOrRejects
    statement: "A validator accepts, optionally with an attachment carried in the commit's metadata, or rejects with a report, and a rejection leaves no trace"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ClassificationGateIsPolicy
    statement: "The erasure-mode classification gate is a validator policy in the integration layer; the store provides the hook and takes no view"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: OverlayPlacementRestsOnOpaqueHandle
    statement: "The overlay's layer 1 placement depends on the opaque term handle of ADR 0022, so a change to 0022 is a change to it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0017](../adr/0017-validator-contract-and-overlay.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
