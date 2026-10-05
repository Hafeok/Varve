---
set: format-version-1
namespace: varve
adr: 0072
decisions:
  - key: LogEncodingIsProvisional
    statement: "The log's encoding is format version 1, tabulated in docs/spec/storage-format.md, and from the first prerelease tag that writes it every later version reads it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: ReadForeverBindsTheLog
    statement: "The read-forever rule binds log/; a derived/ file of a version the store does not read, of another dataset or naming another header hash is a cache miss and is rebuilt"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: HigherVersionRefused
    statement: "Opening a log of a format version above those the build reads refuses with a message naming the version found and the versions read"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: SegmentPreamble
    statement: "Every segment begins with a header carrying VRVL, the format version, the dataset id, the segment id, its first position and the header hash before it, every header integer little-endian and fixed-width"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: RecordLayout
    statement: "A record is a 128-byte header of body length, kind, flags with the closing flag, position, index, prev, the body's SHA-256 and the header's own hash, then the body; a record never spans segments"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: BodyIsAllocThenAssertThenRetract
    statement: "A commit's body is chunks of alloc, then assert, then retract, each with its own count, with asserted and retracted quads sorted; the closing record's body ends with the commit header and its length"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: HeaderFieldsAndHashes
    statement: "The commit header is version, kind, position, timestamp, agent, cause, graph scope, attachments, kind payload, the dictionary's three counters after the commit, prev and content, with SHA-256 for both hashes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: DomainSeparatedGenesis
    statement: "At position 1, prev is the SHA-256 of a fixed, domain-separated UTF-8 string rather than zeros"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: HeaderHashStoredBeside
    statement: "Every header in log/ and derived/ carries the SHA-256 of its own bytes, so a torn or reordered header is detected by itself"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: TornTailSealedAndSkipped
    statement: "Recovery follows the chain across segments: a torn or unclosed tail is ignored and its segment sealed with an abandoned trailer, segments beyond a copy point are abandoned, and any other break refuses to open; bytes that do not verify are a torn tail only when no record of a later position, no record after a broken segment header, and no sealed trailer verifies after them in the same file"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: SealIsATrailer
    statement: "Sealing appends a trailer naming the last closed position and its header hash, so a seal survives a copy and a closed trailer's successor must continue the chain"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: InMemoryIdLayout
    statement: "An id's class is its top two bits, counters start at 1 so id 0 is the default graph, and an inline id carries a datatype tag in bits 61 to 56 and a 56-bit payload, frozen as the format's id layout"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: InlineSetIntegerAndBoolean
    statement: "The inline set is canonical xsd:integer within range and xsd:boolean, and is part of a dataset's creation settings, so it grows only for datasets created with the larger set"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: ManifestWrittenOnce
    statement: "log/MANIFEST holds the magic, format version, dataset id and creation settings with their hash, and is written once, before any segment"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: DatasetIdIsGiven
    statement: "A dataset's 16-byte id is given by whoever creates the dataset; the store never generates one"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: OpenReadsTheLogSinceTheLastCheckpoint
    statement: "Opening reads every record and commit header, and the bodies after the newest valid checkpoint, which carries the dictionary"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: ProjectionStateIsOneBlob
    statement: "The default projection's persisted state is one derived blob naming its runs and its position, replaced atomically"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
---

The rulings of [ADR 0072](../adr/0072-format-version-1.md), filed unaccepted by session 6a of #10
(ADR 0066). It supersedes ADR 0045, whose rulings moved here under their keys, restated as version
1 states them; ADR 0045 has no rulings left in force and no set file.
