# 0045 — The provisional log encoding, and the in-memory id layout

## Status

**Accepted, and provisional by design.** 2026-09-23.

Nothing here is a durable format. ADR 0012 and ADR 0014 put the on-disk
encoding, the inline datatype set and the tag layout at **milestone 6**, and
that stands: the file backend's format may change every byte below, and carries
its own version discriminator when it does. This ADR exists because milestone 4
already needs *some* bytes — the header chain hashes them (I6), recovery parses
them (records), and §10's determinism property compares them.

## Context

The in-memory log is bytes in a segment store (ADR 0040), not objects. That is
deliberate: a log that is objects in memory would pass the determinism,
recovery and chain properties vacuously, and the milestone 6 backend would meet
them for the first time. Writing bytes now means those properties test
something.

So an encoding is needed, and the decisions in it are real even if temporary:
how a record is framed, where the closing flag lives, what exactly a header
contains and hashes, and what `prev` is at position 1 (ADR 0014 says only "a
fixed value").

## Decision

All integers are little-endian. Every segment begins with an 8-byte preamble:
`VRVL`, a version byte (`0`), and three zero bytes.

### A record

| Field | Bytes | |
|---|---:|---|
| payload length | 4 | |
| flags | 1 | bit 0 is the **closing flag**; other bits zero |
| kind | 1 | the commit kind, repeated on every record and checked against the header |
| reserved | 2 | zero, checked |
| position | 8 | the commit this record belongs to |
| index | 4 | the record's index within its commit, from 0 |
| payload | *n* | |

A commit's **body** is split across its records' payloads in order, at most
`DatasetOptions.MaxRecordBytes` per record. The closing record's payload ends
with the header, its length, and its hash:
`body tail ‖ header ‖ u32 header length ‖ SHA-256(header)`.

### The body — what `content` hashes

`alloc`, then `A`, then `R`, each a count followed by entries:

- an allocation is an id and a term: IRI (length, UTF-8), blank node (nothing
  beyond the id), literal (lexical form, datatype IRI, language tag, base
  direction), or triple term (three ids — its components are allocated first);
- a quad is four ids, and `A` and `R` are sorted by id, `(s, p, o, g)`.

### The header — what the chain hashes

`version, kind, position, timestamp (UTC ticks), agent, cause, graph scope,
attachments, kind payload, prev, content`. Absent metadata is id 0. The kind
payload is empty for `Data`, carries the changed fields for `Settings`, and will
carry the key id for `Erasure`.

- **`content` = SHA-256 of the body.** **`prev` = SHA-256 of the previous
  header.** **At position 1, `prev` = SHA-256 of the UTF-8 string
  `Varve log, before position 1`** — a domain-separated constant rather than
  zeros, so that a hash of nothing, or of another format's genesis, cannot be
  mistaken for it.
- **The header's own hash is stored beside it.** Without it, a byte flipped in
  the *last* commit's header would go unnoticed, because only a successor's
  `prev` covers a header. §10 asks that any single-byte header change break
  verification, including the last.

### Recovery

A record whose length runs past the end of its segment is **torn**. Records of a
commit with no closing record are **unclosed**. On open, a torn or unclosed tail
is ignored — never parsed as data — and, because the contract has no truncate,
**the store seals that segment and appends from a new one**. The ignored bytes
stay in the log. A record with index 0 for a position that already has pending
records restarts that commit, which is how the parser reads past a tail that
recovery abandoned. Anything else out of order — a position not one past the
readable head, an index that skips, a non-zero reserved byte, a kind that
disagrees with its header — refuses to open.

### The id layout in memory

The class is the top two bits: `00` canonical, `01` blank, `10` private
(reserved; nothing allocates it), `11` inline. Canonical and blank ids are
counters from 1 within their class, so no id is 0 and 0 remains the default
graph. An inline id carries a datatype tag in bits 61–56 and a 56-bit
two's-complement payload in bits 55–0. **The in-memory inline set is canonical
`xsd:integer` in range and `xsd:boolean`**, by ADR 0012's amendment: `"1"` is
inline and `"01"` is not.

## Alternatives considered

- **Keep the in-memory log as objects**, hash a canonical serialisation only when
  hashing. Rejected above: determinism and recovery would test nothing.
- **Zero as the genesis `prev`.** The usual choice. Rejected for the reason
  stated: a constant with a domain string cannot collide with a hash of empty
  input or with another format.
- **A checksum per record** (CRC-32) instead of a stored header hash. Would
  detect a torn record without relying on the length. Rejected for now: the
  chain and the content hash already detect every change to the body and the
  header, and CRC-32 is not in the BCL on every target without a package.
- **Varint ids in the log.** Smaller logs. Rejected here as ADR 0012 rejected it
  for the id scheme: a milestone 6 compression decision, not a milestone 4 one.
- **A larger inline set** — `xsd:decimal`, dates. Rejected: ADR 0012 says start
  with values provably never looked up by value, and grow with evidence.

## Consequences

- **The determinism property is over real bytes**, and it holds for crash-free
  histories (specification 1.2, §10, ADR 0046): a recovered tail stays in the
  log by construction.
- **A flipped length in the final record can look like a torn tail** and be
  recovered past rather than refused. The property test states this exactly:
  every single-byte change either refuses to open or yields an earlier readable
  head — never the same head with different content.
- **Every byte here may change at milestone 6.** The file backend's format
  begins with its own version byte and owes nothing to this one.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0044).
  Touches **0012** (the class tags and the inline constraint; the layout remains
  milestone 6's to freeze), **0013** (the closing flag is in the record; a torn
  tail is discarded by being ignored), **0014** (what is hashed and the genesis
  value), **0018** and **0040** (no truncate, so recovery seals and moves on),
  and **0021** (settings travel in the header's kind payload). No conflict with
  any.
- **Layer ownership.** `Varve.Store`, **layer 4**, and entirely internal.
- **Analyzer rule.** None.
- **Open questions owned.** None.
