---
set: term-dictionary-and-id-scheme
namespace: varve
adr: 0012
decisions:
  - key: TermIdClassInHighBits
    statement: "A TermId is 64 bits with its class carried in the high bits, read with a mask, so a reader knows the class without a lookup"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: CounterAllocatedIdClasses
    statement: "Canonical, blank and private ids are counters allocated by the sequencer, and canonical ids are injective over terms (I3)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: InlineIdsForSmallValues
    statement: "Small values are encoded inline, the value being the id, with no dictionary entry"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: PrivateIdsIndependentOfContent
    statement: "Private ids are counters in their own class, independent of content and never interned"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: InlineOnlyCanonicalLexicalForms
    statement: "A literal may be encoded inline only when its lexical form is the canonical one for its datatype"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: InlineSetAndLayoutAtMilestone6
    statement: "Which datatypes qualify for inline encoding and the exact tag layout are decided with the durable format at milestone 6, and no bytes are frozen before then"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0012](../adr/0012-term-dictionary-and-id-scheme.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Open question Q1 is answered by ADR 0044, in its set. The revisit condition is the ADR's
falsifier, not a ruling, and is not enumerated.
