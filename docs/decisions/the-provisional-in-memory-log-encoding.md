---
set: the-provisional-in-memory-log-encoding
namespace: varve
adr: 0045
decisions:
  - key: LogEncodingIsProvisional
    statement: "The milestone 4 log encoding is provisional: every byte may change at milestone 6, whose format carries its own version byte"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: SegmentPreamble
    statement: "Every segment begins with VRVL, a version byte and three zero bytes, and every integer is little-endian"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: RecordLayout
    statement: "A record is a payload length, flags with the closing flag in bit 0, the kind, reserved bytes, the position, its index within the commit and the payload, and a commit's body is split across its records"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: BodyIsAllocThenAssertThenRetract
    statement: "A commit's body is alloc, then A, then R, each counted, with A and R sorted by id"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: HeaderFieldsAndHashes
    statement: "The header is version, kind, position, timestamp, agent, cause, graph scope, attachments, kind payload, prev and content, where content hashes the body and prev the previous header with SHA-256"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: DomainSeparatedGenesis
    statement: "At position 1, prev is the SHA-256 of a fixed, domain-separated UTF-8 string rather than zeros"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: HeaderHashStoredBeside
    statement: "Each header's own hash is stored beside it, so a change to the last header breaks verification"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: TornTailSealedAndSkipped
    statement: "On open a torn or unclosed tail is ignored, its segment is sealed and appending resumes in a new one, and any other disorder refuses to open"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: InMemoryIdLayout
    statement: "An id's class is its top two bits, counters start at 1 so id 0 is the default graph, and an inline id carries a datatype tag in bits 61 to 56 and a 56-bit payload"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: InlineSetIntegerAndBoolean
    statement: "The in-memory inline set is canonical xsd:integer within range and xsd:boolean"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0045](../adr/0045-the-provisional-in-memory-log-encoding.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
