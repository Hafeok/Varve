---
set: package-layering
namespace: varve
adr: 0003
decisions:
  - key: LayersStrictlyDownward
    statement: "A package declares its layer, and a reference is legal only to a package in a strictly lower layer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: SameLayerReferenceIsViolation
    statement: "A same-layer reference is a violation, not an exception: two packages in one layer that need each other are one package or two layers"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: ContractsInLowestLayer
    statement: "A contract lives in the lowest layer that can define it without knowing its implementers"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: NoUpwardKnowledge
    statement: "A lower layer never learns about a higher one, including through a callback typed to a concrete higher-layer type, service location or InternalsVisibleTo"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: InternalsVisibleToTestsOnly
    statement: "InternalsVisibleTo is permitted toward *.Tests assemblies only"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: UndeclaredReferencedLayerIsError
    statement: "A referenced Varve assembly that declares no layer is an error in its own right, so the direction rule cannot be bypassed by omission"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
---

The rulings of [ADR 0003](../adr/0003-package-layering.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Moved to later sets: the layer table to ADR 0060's (`LayerTable`); the declaration mechanism
and which assemblies declare no layer to ADR 0064's (`LayerDeclaredPerProject`,
`UnlayeredAssemblies`); the escape for a forbidden reference to ADR 0062's
(`LayerExceptionEscape`). Open question 2 is closed by ADR 0048 and its answer is in that set.
Open question 1 is an open question, not a ruling, and is not enumerated.
