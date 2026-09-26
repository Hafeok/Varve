---
set: inmemorydataset-is-a-value-built-by-a-builder
namespace: varve
adr: 0067
decisions:
  - key: InMemoryDatasetIsAValue
    statement: "InMemoryDataset is an immutable value in Varve.Rdf, an IQuadSource whose quads and interning table never change once made"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: InMemoryDatasetBuilder
    statement: "A sealed InMemoryDatasetBuilder in Varve.Rdf carries the mutators, and ToDataset() returns a copied snapshot"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: HandlesStableAcrossSnapshots
    statement: "A builder only appends to its interning table, so a handle means the same term in every snapshot it produces"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
---

The rulings of [ADR 0067](../adr/0067-inmemorydataset-is-a-value-built-by-a-builder.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
