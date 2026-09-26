---
set: hotpath-attribute
namespace: varve
adr: 0026
decisions:
  - key: MarkHotPathsWhileWriting
    statement: "Hot paths are marked when they are written, before the rule that checks them exists"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: HotPathAttributeIsInternal
    statement: "The hot-path attribute is internal to each assembly, never on a public API baseline and never reaching a consumer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: HotPathMatchedByFullName
    statement: "The hot-path rules match the attribute by full name, not by symbol identity, a forgeable match accepted inside the repository"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0026](../adr/0026-hotpath-attribute.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Moved to a later set: the attribute's placement in `eng/HotPathAttribute.cs` is superseded
by ADR 0064, in its set.
