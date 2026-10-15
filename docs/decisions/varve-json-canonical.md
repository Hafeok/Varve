---
set: varve-json-canonical
namespace: varve
adr: 0147
decisions:
  - key: CanonicalJsonAtLayerZero
    statement: "Varve.Json.Canonical, at layer 0 with no Varve reference, is the one RFC 8785 implementation; Varve.JsonLd and Varve.Events use it"
  - key: CanonicalBytesReadForever
    statement: "What Varve.Json.Canonical writes for an existing input never changes"
  - key: JsonValuesAreBclPrimitives
    statement: "Varve.Json.Canonical's surface takes JSON's own values as the BCL types that hold them, double for a number, bool for true and false, string or UTF-8 bytes for text, and answers lengths and comparison signs as int: they are the values of the format, not quantities that could be swapped"
---

The rulings of [ADR 0147](../adr/0147-varve-json-canonical.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration, part 2,
of #93 (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
