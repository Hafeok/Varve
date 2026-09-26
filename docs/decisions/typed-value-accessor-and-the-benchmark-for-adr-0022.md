---
set: typed-value-accessor-and-the-benchmark-for-adr-0022
namespace: varve
adr: 0050
decisions:
  - key: TypedValueAccessor
    statement: "IQuadSource gains TryGetInlineValue, true only when the handle itself encodes the term's value, which it returns as an InlineValue of kind Integer or Boolean"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: FalseMeansNotInline
    statement: "False from TryGetInlineValue means only that the handle is not inline, and the consumer externalises and parses as before"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: InMemoryDatasetNeverInline
    statement: "InMemoryDataset has no inline ids and always answers false rather than parsing behind the accessor"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
---

The rulings of [ADR 0050](../adr/0050-typed-value-accessor-and-the-benchmark-for-adr-0022.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

The three-arm benchmark plan and its verdict rule were carried out in milestone 5b and judged
on 2026-09-25 in ADR 0022's Status; a discharged plan is not a ruling in force and is not
enumerated.
