---
set: varve-delta-and-the-read-forever-rule
namespace: varve
adr: 0128
decisions:
  - key: DeltaFormatIsAPackage
    statement: "Varve.Delta owns the delta format's reader, writer, record model and event framing, over Varve.Rdf types and its own position, timestamp and commit-kind types"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: DeltaIsLayerThree
    statement: "Varve.Delta is layer 3 by dependency, referencing Varve.Rdf and Varve.Turtle, whose N-Quads term parser and writer it uses"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: StoreDoesNotReferenceDelta
    statement: "Varve.Store does not reference Varve.Delta; Varve.Protocol maps a store's Commit to the writer through ChangeFeedWriter"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: DeltaVersionOneReadForever
    statement: "DeltaFormat.Current is 1; a version-1 document is read by every later release and its meaning never changes; a change a version-1 reader would misread is version 2"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: DescriptorCarriesDeltaFormat
    statement: "A release descriptor's basis carries delta-format equal to DeltaFormat.Current at the commit under release"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
---

The rulings of [ADR 0128](../adr/0128-varve-delta-and-the-read-forever-rule.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration of #93
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.
