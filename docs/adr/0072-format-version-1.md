# 0072 — Format version 1, read forever from the first prerelease that writes it

## Status

**Accepted — filed unaccepted by session 6a of #10, 2026-10-02** (ADR 0066).

It **supersedes** ADR [0045](0045-the-provisional-in-memory-log-encoding.md),
which was provisional by design and said so. It **discharges** the milestone 6
items of ADR [0012](0012-term-dictionary-and-id-scheme.md) (the on-disk id
layout and the inline set) and ADR [0014](0014-header-chain-and-divergence.md)
(a version discriminator from the first byte).

The bytes are tabulated in
[`docs/spec/storage-format.md`](../spec/storage-format.md), version 1. That file
is the layout; this ADR is the reasoning.

## Context

ADR 0045 wrote bytes so that milestone 4's properties tested something, and
said every one of them could change here. From the first prerelease tag that
writes a dataset to disk, they cannot: a `log/` is copied, backed up and checked
into repositories (§2) by tools the store does not know about, and a copy made
today must open in every later version. ADR 0014 named the cost: the header
format is an input to every subsequent header, so it is fixed once.

Milestone 4 left four things the file backend needs and the provisional format
did not have:

- **A header that checks itself.** In version 0, only a successor's `prev`
  covers a record header; a torn length in the last record can be read as data
  or as a cut. With writes reordered by power loss, a closing record can be
  durable while the record before it is not; version 0 would then fail the
  content hash and **refuse to open**, where the right answer is that the commit
  never closed.
- **A seal that survives a copy.** A sealed segment was sealed only in the
  backend's bookkeeping. A file attribute does not survive `git`, and a copy
  that loses it can make the store append after a discarded tail.
- **A way to open without reading every body.** Version 0 rebuilt the
  dictionary by decoding every commit.
- **A body a bulk loader can stream.** Version 0 wrote one count per section
  before the section, which a loader cannot know until it has finished.

## Decision

### Format version 1

Little-endian, fixed-width integers in every header; LEB128 varints allowed
only inside bodies. SHA-256 throughout.

- **`log/MANIFEST`**, written once at creation: magic, format version, the
  dataset id, the creation settings and their hash, and a hash of the whole.
- **Segment `log/NNNNNNNN.seg`**: a header (magic, format version, dataset id,
  segment id, the first position it holds, and the header hash of the commit
  before it), then records, then — once sealed — a trailer.
- **Record**: a 128-byte header (body length, kind, flags with the closing flag,
  position, index within the commit, `prev` — the previous commit's header hash
  —, the SHA-256 of this record's body, and a hash of the header itself), then
  the body.
- **Commit body**: chunks, each a tag (alloc, assert, retract) and a count and
  its entries, in tag order. The closing record's body ends with the commit
  header and its length.
- **Commit header** (I6): format version, kind, position, timestamp, agent,
  cause, scope, attachments, kind payload, **the dictionary's canonical, blank
  and private counters after this commit**, `prev`, and `content` — the hash of
  every chunk of the commit, in order.
- **Genesis** `prev` at position 1: SHA-256 of `Varve log, before position 1`,
  as in version 0.

### Every header hashes itself

The manifest, the segment header, every record header, the trailer, and every
`derived/` header carry a SHA-256 of their own bytes. A header that does not
match its hash was never completely written, whoever wrote it; it is torn, not
damaged, and is treated as the end of what was written.

### A seal is in the bytes

Sealing a segment appends a **trailer**: magic, version, a status (closed, or
tail abandoned by recovery), the dataset id, the segment id, the last closed
position at the segment's end and its header hash, and a hash of itself.
Trailers are found at the end of the file. The rules for walking the log:

1. A segment with a **closed** trailer ends exactly at a record boundary, and
   its trailer's position and hash are the readable head there; the next
   segment must exist with the next id and continue the chain, or the log is
   **refused** as damaged.
2. A segment with an **abandoned** trailer ends with a tail recovery discarded;
   the next segment, if any, must continue the chain.
3. A segment with **no trailer** is open-ended — the active segment, or one
   copied mid-write. Later segments whose header does not continue the readable
   head and claims a first position above `head + 1` are **abandoned**: they
   come from beyond the point where a copy was taken, and stay in `log/`, never
   rewritten. A later segment claiming a position at or below `head + 1`
   without continuing the chain is **refused**: that is a different history,
   not a later one.
4. On open, recovery seals what it abandoned: the newest segment, when it holds
   a torn or unclosed tail, gets an abandoned trailer; a newest segment beyond a
   copy point is sealed by the backend; and the next commit starts a new
   segment whose header continues from the readable head.

### What counts as damage beyond the unclosed tail

Within a segment, a record whose header hash fails, or whose body does not
match its content hash, is the start of a **torn tail** — unless something
written after it in the same file verifies. A record header of a **later**
position anywhere after the break, a record header anywhere after a segment
header that does not verify, or a closed trailer over the break refuses. No
crash produces any of them, because the later bytes were written only after a
flush made the broken ones durable, and a copy takes each file as a prefix. A
later segment does not cover an earlier one: a copy may take the two at
different moments. A sealed segment's trailer that no longer verifies refuses
when a later segment exists, since the next segment is created only after the
trailer is flushed.

So I6, as specification 1.5 states it: a byte changed anywhere a later closed
commit or a sealed trailer covers refuses; only a byte of the newest commit's
records may read as a torn write, and then the log opens at the position before
it with exactly the state there, never at the same head. A body before the
newest checkpoint is read only when something needs it, so a change there
refuses at that read rather than at open. The two cases are separate
properties, on segments of 1 KiB and records of 64 bytes so that commits span
seals.

### The id layout and the inline set are frozen

ADR 0045's in-memory layout becomes the format: the class in bits 63–62
(`00` canonical, `01` blank, `10` private, `11` inline), counters from 1, id 0
the default graph, an inline datatype tag in bits 61–56 and a 56-bit
two's-complement payload. The inline set is canonical `xsd:integer` in range
(tag 1) and canonical `xsd:boolean` (tag 2). **The inline set is part of the
creation settings**: growing it would give a term created before the change a
canonical id and the same term after it an inline one, so a larger set applies
only to datasets created with it, never to an existing one.

### Opening reads the log since the last checkpoint

Because each commit header carries the dictionary's counters, open reads every
**record header** and every **commit header** — to verify the chain (I6), and to
know each position's timestamp, location and counters — but reads **bodies**
only after the newest valid checkpoint, which carries the dictionary. Bodies
before it are verified against their content hash whenever they are read.

### `derived/` has its own version, and is not read forever

Every file under `derived/` ends with a header carrying magic, format version,
kind, the dataset id, the position range it covers and the header hash of the
commit it reaches. It ends the file rather than beginning it because derived
files are written as streams — a merged run is not in memory — and only at the
end is everything the header describes known. **The read-forever rule binds `log/` only.** A derived file
of a version this store does not read, of another dataset, or naming a header
hash that is not the log's, is a cache miss and is rebuilt (ADR 0041's rule,
extended from checkpoints to runs and to the projection's state).

### Read forever, from the first prerelease that writes it

From the tag that first publishes a build writing version 1 —
`v0.1.0-preview.1` (ADR 0029) — every later version of Varve opens a version 1
`log/`. Opening a `log/` of a **higher** version refuses with a message naming
the version found and the versions this build reads. A lower version than 1
does not exist on disk: version 0 was never written outside memory.

### The dataset id is given, never generated

The manifest and every segment carry a 16-byte dataset id, which the creator of
the dataset supplies (`DatasetId`). The store never generates one: ambient
randomness is banned in `Varve.Store` (ADR 0011), and two machines fed the same
requests with the same id must write the same bytes (§10).

## Alternatives considered

- **Keep version 0's framing and add a CRC per record.** Smaller headers. CRC-32
  is not in the BCL on every target without a package (ADR 0045's reason), and
  SHA-256 is, and is already on the path.
- **A record-level chain** (`prev` as the previous *record's* header hash).
  Detects a lost record inside a multi-record commit; so does the record index,
  and a record-level chain would have to skip abandoned tails explicitly.
  `prev` as the previous *commit's* header hash binds every record to its place
  in the chain at no extra cost.
- **Seal by file attribute** (read-only). Lost by a copy; a copy that loses it
  appends after a discarded tail and loses commits.
- **A pointer to the next segment in the trailer.** Considered, and dropped:
  every case it served is the chain rule above, and a pointer written into a
  sealed segment cannot be updated when recovery later abandons its successor.
- **Varint headers.** Smaller logs, and headers that cannot be found or
  skipped without parsing. The brief fixes headers as fixed-width; bodies, where
  the volume is, may use varints.
- **Read-forever for `derived/` too.** Would freeze the run layout before its
  compression is measured. Derived data is reproducible by definition; a cache
  that cannot change is a cost with no benefit.
- **A generated dataset id.** Every store does it. It would need ambient
  randomness in `Varve.Store`, and the determinism property would need it
  injected — which is what taking it from the creator already is.

## Consequences

- **The format is a promise from the first prerelease.** A change to anything
  under `log/` is version 2, read alongside version 1 for ever.
- **Headers cost bytes**: 128 per record, 104 per segment header and trailer,
  96 for the manifest. Commits of a single quad grow; records of a megabyte do
  not notice.
- **Open costs one header read per record and the bytes since the last
  checkpoint.** Without a checkpoint it is the whole log, as before.
- **The id layout cannot change** without a format version; the inline set can
  grow only for new datasets.
- **`MemoryStorage` writes version 1 too**: the property tests run against the
  same bytes the file backend writes.

## Checks

- **Checked against the accepted ADRs** (0001–0069) and specification 1.4.
  Supersedes **0045**. Discharges the milestone 6 items of **0012** and
  **0014**. Touches **0010** and **0013** (chunks carry an effective delta; a
  multi-record commit's records are discardable together), **0016** and
  **0041** (derived data names the commit it materialises), **0018** and
  **0040** (records never span segments; the store writes the trailer), **0021**
  (settings in the kind payload), **0029** (the tag that freezes it), **0074**
  (the private entry's layout), and **0076** (what a bulk commit needs). No
  conflict with any.
- **Layer ownership.** `Varve.Store`, **layer 4**. Public: `DatasetId` and
  `FormatVersion` in `Varve.Store.Log`, and `UnsupportedFormatException`.
- **Analyzer rule.** None.
- **Open questions owned.** None.
