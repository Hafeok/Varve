# Storage format

Format specification, **version 1**.

Status: written by milestone 6a with [ADR 0072](../adr/0072-format-version-1.md),
which is the reasoning; this document is the layout. `log/` is version 1.
`derived/` is **derived format version 3** since milestone 7b (ADR 0109): a
run carries a filter over its terms' hashes after its hash index. Version 2
(milestone 6c, ADRs 0079 and 0080: runs carry the dictionary's entries, and
keys are compressed in their blocks) is read as before, with no filter; a
derived file of version 1 is a cache miss. **Version 1 of `log/` is
frozen from the first prerelease tag that writes it** (`v0.1.0-preview.1`, ADR
0029), and from then on every version of Varve reads it. `derived/` carries its
own version and is **not** read forever: a derived file this build does not
read is a cache miss and is rebuilt (ADR 0072).

Scope: the bytes of a dataset directory. The state machine they encode is
[`log-and-projection-model.md`](log-and-projection-model.md) (version 1.5); the
storage contract that writes them is ADRs 0018, 0040 and 0071.

## 1. Conventions

- Integers in headers are **little-endian and fixed-width**. `u8`, `u16`, `u32`,
  `u64`; `i64` is two's complement.
- Inside bodies, `uleb` is an unsigned LEB128 varint: seven bits per byte, least
  significant group first, high bit set on every byte but the last. A value
  takes at most 10 bytes, and a non-minimal encoding (a trailing `0x80`-group
  that adds nothing) is refused.
- `bytes(n)` is `n` raw bytes; `lbytes` is a `uleb` length and that many bytes.
- `hash` is SHA-256, 32 bytes. "Self-hash" is the SHA-256 of the header bytes
  before it.
- A **dataset id** is 16 bytes in RFC 9562 order — the order a GUID is written
  as text (`Guid.TryWriteBytes(…, bigEndian: true)`).
- **Genesis** is `SHA-256("Varve log, before position 1")` over the UTF-8 bytes,
  the `prev` of position 1 and the header hash "before" a dataset's first
  commit.
- Reserved bytes are written zero and checked zero.

## 2. The directory

```
<dataset>/
  log/
    MANIFEST
    00000000.seg
    00000001.seg
    …
  derived/
    .gitignore          "*", written at creation
    LOCK                the lease (ADR 0075), held open; not a blob
    LOCK.owner          who holds it, for the refusal's message; not a blob
    checkpoints/<P>
    index/state
    index/runs/<from>-<to>.<n>
    index/commits/<from>-<to>.<n>   the commit index (ADR 0089)
    bulk/<head>/…       a bulk load's spills (ADR 0081), deleted when it ends and on open
```

Segment file names are the segment id in eight decimal digits. Positions in
derived names are twenty decimal digits. Files ending `.tmp` under `derived/`
are unpublished writes and are deleted on open.

## 3. `log/MANIFEST` — 96 bytes, written once

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | magic `VRVM` |
| 4 | 2 | format version, `1` |
| 6 | 2 | reserved |
| 8 | 16 | dataset id |
| 24 | 2 | inline set version, `1` (§8) |
| 26 | 2 | hash algorithm, `1` = SHA-256 |
| 28 | 4 | reserved |
| 32 | 32 | creation settings hash: SHA-256 of bytes 24–31 |
| 64 | 32 | self-hash of bytes 0–63 |

The manifest is written before any segment exists. A directory with no valid
manifest and no segments holds no dataset; one with segments and no valid
manifest is refused as damaged. A format version above those a build reads is
refused with a message naming the version found and the versions read.

## 4. Segment — `log/NNNNNNNN.seg`

A segment is a **header**, then **records**, then, once sealed, a **trailer**.
A record never spans two segments; a commit's records may.

### 4.1 Segment header — 104 bytes

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | magic `VRVL` |
| 4 | 2 | format version, `1` |
| 6 | 2 | reserved |
| 8 | 16 | dataset id |
| 24 | 4 | segment id |
| 28 | 4 | reserved |
| 32 | 8 | first position: the readable head + 1 when the segment was created |
| 40 | 32 | the header hash of the commit at *first position − 1*, or genesis |
| 72 | 32 | self-hash of bytes 0–71 |

### 4.2 Record header — 128 bytes

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | body length |
| 4 | 1 | commit kind: `0` Data, `1` Erasure, `2` Settings |
| 5 | 1 | flags: bit 0 is the **closing flag**; other bits zero |
| 6 | 2 | reserved |
| 8 | 8 | position of the commit this record belongs to |
| 16 | 4 | index of the record within its commit, from 0 |
| 20 | 4 | reserved |
| 24 | 32 | `prev`: the header hash of the commit before, or genesis |
| 56 | 32 | SHA-256 of this record's body |
| 88 | 8 | reserved |
| 96 | 32 | self-hash of bytes 0–95 |

### 4.3 Record body and commit body

A commit's **body** is the concatenation of its records' bodies, except that the
closing record's body ends with the **commit header** and its length:

```
closing record body = body tail ‖ commit header ‖ u32 commit header length
```

The body is a sequence of **chunks**, which may span record boundaries:

| Field | |
|---|---|
| `u8` tag | `1` allocations, `2` asserted quads, `3` retracted quads |
| `uleb` count | entries in this chunk |
| entries | `count` term entries (tag 1) or quads (tags 2, 3) |

Tags appear in non-decreasing order; a tag may repeat, which is how a writer
streams a section whose size it does not know yet. Asserted quads, and
retracted quads, are each in ascending `(s, p, o, g)` order across all their
chunks. A quad is four `uleb` ids, `s p o g`.

**`content`** is the SHA-256 of the commit body.

### 4.4 Term entry

| Field | |
|---|---|
| `uleb` id | the id allocated |
| `u8` kind | below |

| Kind | Then | Id class |
|---:|---|---|
| `0` IRI | `lbytes` UTF-8 | canonical |
| `1` blank node | nothing | blank |
| `2` literal | `lbytes` lexical form, `lbytes` datatype IRI (empty for a simple literal and for a language-tagged one), `lbytes` language tag, `u8` base direction (`0` none, `1` ltr, `2` rtl) | canonical |
| `3` triple term | `uleb` subject, `uleb` predicate, `uleb` object ids, each allocated earlier | canonical |
| `4` private | `bytes(16)` key id, `bytes(32)` synthetic IV `V`, `lbytes` ciphertext over the whole term encoding (ADR 0074) | private |

Within a commit, allocations are in ascending id order per class, and each id
is the class counter plus one.

### 4.5 Commit header

Fixed-width fields; two counted lists.

| Size | Field |
|---:|---|
| 2 | format version, `1` |
| 1 | kind |
| 1 | reserved |
| 8 | position |
| 8 | timestamp, `i64` UTC ticks (100 ns since 0001-01-01) |
| 8 | agent id, or 0 |
| 8 | cause id, or 0 |
| 8 | graph scope id, or 0 |
| 8 | the dictionary's canonical counter after this commit |
| 8 | the dictionary's blank counter after this commit |
| 8 | the dictionary's private counter after this commit (zero until erasure mode) |
| 4 | attachment count `n` |
| 8·n | attachment ids |
| 4 | kind payload length `m` |
| m | kind payload (§4.6) |
| 32 | `prev` |
| 32 | `content` |

The commit's **header hash** is the SHA-256 of these bytes. It is the next
commit's `prev` (I6).

### 4.6 Kind payloads

| Kind | Payload |
|---|---|
| Data | empty |
| Settings | `u8` field count, then per field `u8` field id and `u8` value: field `1` default access scope (`0` AllHistory, `1` Current); field `2` erasure mode (`0` off, `1` on), reserved (ADR 0074) |
| Erasure | `bytes(16)` key id (ADR 0074) |

### 4.7 Trailer — 104 bytes, at the end of a sealed segment

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | magic `VRVT` |
| 4 | 2 | format version, `1` |
| 6 | 1 | status: `1` closed, `2` tail abandoned by recovery |
| 7 | 1 | reserved |
| 8 | 16 | dataset id |
| 24 | 4 | segment id |
| 28 | 4 | reserved |
| 32 | 8 | the readable head at the end of this segment |
| 40 | 32 | its header hash, or genesis |
| 72 | 32 | self-hash of bytes 0–71 |

A trailer is recognised by reading the last 104 bytes of the file. Records end
where the trailer begins.

## 5. Reading the log

Segments are read in ascending id, from segment 0, keeping the **readable
head** `H` and its header hash `h` (initially 0 and genesis).

**A segment header continues the chain** when its magic, version, dataset id,
segment id and self-hash verify, its first position is `H + 1`, and its hash
field is `h`. A header whose magic and self-hash verify but which names another
dataset or another segment id is a foreign segment and **refuses**. A header that
does not verify at all — a crash while the segment was being created — makes
the segment **open-ended, holding a torn tail and nothing else**; if a record
header verifies anywhere after it in the file, or the file ends with a closed
trailer, the log is **refused**, since a segment's header is flushed before
its first record.

1. **Records.** Within a segment, a record is read when its header's self-hash
   verifies, its body fits before the end (or the trailer), and its body hash
   verifies. Its position must be `H + 1`, its `prev` must be `h`, and its index
   must be the number of records pending for that position, with the same kind
   — except that the first record of a segment may have index 0 while records
   of its position are pending from the segment before, which **restarts** the
   commit: recovery abandoned it at a seal and began again;
   a record with the closing flag closes the commit, whose header must verify
   against the pending records (§4.5), and `H` and `h` advance. A verified
   record that breaks one of these rules **refuses** the log.
2. **Torn tail.** The first record that does not verify ends what the segment
   holds; the commit it belongs to, and any pending records, are discarded.
   **Except:** when, anywhere after it in the same segment file, a record
   header verifies whose position is above `H + 1`, the log is **refused** — no
   crash produces a later commit after an earlier one that was not flushed, and
   a copy takes each file as a prefix. Every offset is looked at, since a broken
   length says nothing about where the next record starts. Bytes of a later
   segment do not count: a copy may take two files at different moments
   (specification 1.5, I6).
3. **Closed trailer.** The segment must end exactly at a record boundary with no
   torn bytes, and the trailer's head and hash must equal `H` and `h`. The next
   segment must exist with the next id and continue the chain, or, if no later
   segment exists, the log ends. Anything else **refuses**. Records pending at a
   closed trailer continue in the next segment.
4. **Abandoned trailer.** The segment may end with a torn or unclosed tail,
   which is discarded; the trailer's head and hash must equal `H` and `h`. The
   next segment, if any, must continue the chain.
5. **No trailer.** The segment is open-ended. Every later segment whose header
   does not continue the chain and whose first position is above `H + 1` is
   **abandoned** (it was written beyond the point where this copy of the
   directory was taken); the first that continues resumes the walk. A later
   segment whose header verifies, does not continue, and claims a first position
   at or below `H + 1` is a different history and **refuses**. A later segment
   whose header does not verify is abandoned, unless a record header verifies
   after it in the file, which **refuses** as above. A segment without a trailer
   whose bytes after its last record are exactly a trailer's length and agree
   with this segment's trailer in two of magic, dataset id and segment id is a
   sealed trailer that was changed, and **refuses** when a later segment exists:
   the next segment is created only after the trailer is flushed.

**On open, recovery** makes the next commit safe to write:

- if the segment the walk ended in has no trailer and holds a discarded tail, it
  is given an **abandoned** trailer if it is the newest segment;
- the newest segment, if it is not the one the walk ended in, is sealed by the
  backend;
- the next commit starts a **new segment** whose header continues from `H` —
  except when the walk ended in the newest segment, with no trailer and nothing
  discarded, which then remains the active segment.

Recovery writes nothing else, and rewrites nothing.

## 6. Writing the log

- A commit's records are appended to the active segment. When the next record
  would take the segment past the segment size, the segment is sealed — its
  trailer appended with status closed, and flushed — and a new segment created,
  whose header is written and flushed before the first record goes in. A
  segment holding only its header takes the next record whatever its size.
- The segment is flushed once per commit, after the closing record (ADR 0013).

## 7. `derived/`

A derived file is written as a stream, so what describes it comes last: its
**contents**, then its **directory**, then the **derived header**, which is the
file's last 160 bytes. A file is published by a temporary file, a flush and a
rename (ADR 0071, ADR 0073), so a reader never sees one half written.

### 7.1 Derived header — the last 160 bytes

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | magic `VRVD` |
| 4 | 2 | derived format version, `3`; `2` is read (§7.2) |
| 6 | 2 | kind: `1` run, `2` checkpoint, `3` projection state, `4` commit index |
| 8 | 16 | dataset id |
| 24 | 8 | from position: a run or a commit index blob covers the commits after it; 0 for the others |
| 32 | 8 | to position |
| 40 | 32 | the header hash of the commit at *to position* |
| 72 | 8 | directory offset |
| 80 | 8 | directory length; the directory ends where the header begins |
| 88 | 32 | SHA-256 of the directory |
| 120 | 8 | reserved |
| 128 | 32 | self-hash of bytes 0–127 |

A derived file is used only if its header and directory verify, its version is
2 or 3, its kind is the one expected, its dataset id is the log's, and its *to
position*'s header hash is the log's. Otherwise it is a cache miss.

### 7.2 Runs and checkpoints

A **quad key** is four `u64` ids in the order's permutation. The six orders
are `SPOG`, `POSG`, `OSPG`, `GSPO`, `GPOS`, `GOSP`, numbered 0–5 (ADR 0041). A
run has twelve **key sections**, in this order: for each order, its asserted
keys, then its retracted keys, each ascending. After them comes its **term
section** (ADR 0079): the dictionary entries of the canonical ids its commits
allocated. A checkpoint is a run from position 0 with no retractions, whose
term section holds every canonical id from 1 to its counter.

**Key sections** start at offset 0 and follow each other with no gap. Each is
a sequence of **blocks** of 128 keys (the last may hold fewer), compressed
(ADR 0080):

| Field | |
|---|---|
| `bytes(32)` | the block's first key, four `u64` little-endian |
| per later key: `u8` *d* | the index, 0–3, of the first id that differs from the key before |
| `uleb` | that id's increase over the key before's |
| `uleb` × (3 − *d*) | the ids after it, as they are |

A block uses exactly its bytes and decodes to exactly its keys, or it is
damaged.

**The term section** follows the key sections:

| Region | |
|---|---|
| entries | the entries of counters `From + 1` to `To`, in order, each as storage format §4.4 writes it — the id, then the term |
| offsets | `To − From + 1` `u64`: where each entry starts within the entries, then their end |
| hash index | `To − From` pairs of `u64` hash and `u64` id, ascending by hash, then id |

The **hash** is of the entry's **key** — the entry without its id — with a
language tag's ASCII letters lowercased, since tags compare ignoring case: a
64-bit hash, eight bytes at a time through SplitMix64's finaliser (see
`TermKey.Hash`). It orders the index and is searched by interpolation; it is
not a cryptographic hash, and equal hashes are told apart by comparing keys.

**The term filter** (version 3, ADR 0109) follows the hash index: a blocked
Bloom filter over the same hashes, `max(1, ⌈(To − From) / 64⌉)` blocks of 64
bytes — eight bits a term. A hash's block is `((hash >> 32) × blocks) >> 32`;
its three bits in the block are `hash & 511`, `(hash >> 9) & 511` and
`(hash >> 18) & 511`, each a bit index in the block's 512, least significant
bit first. A lookup by key probes the filter before the index and skips the
run when any of the three bits is clear. A version-2 run has no filter
section, and a reader looks up its index for every key.

**Directory:**

| Size | Field |
|---:|---|
| 4 | keys per block, `128` |
| 4 | key section count, `12` |
| 24 × 12 | per key section: `u64` byte offset, `u64` key count, `u64` byte length |
| 32 × Σ blocks | fences: the first key of every block, section by section |
| 8 × Σ blocks | where every block begins within its section, section by section |
| 8 | term section's `From`: the canonical counter before its first entry |
| 8 | term section's `To` |
| 8 | entries' offset |
| 8 | entries' length |
| 8 | offsets' offset: the entries' end |
| 8 | hash index's offset: the offsets' end; in version 2 the index ends at the directory |
| 8 | the blank counter at the run's *to position* |
| 8 | version 3: the filter's offset, the index's end |
| 8 | version 3: the filter's length; the filter ends at the directory |

A reader holds the fences and block starts in memory — 40 bytes per 128 keys —
and the filter — eight bits a term — and reads a block at a time through the
synchronous blob read; a term by id is two offsets and an entry, a term by key
a filter probe and, when it may be there, a window of the index and the
entries its hash names. Keys and entries are verified by their directory's placement,
not hashed one by one: a derived file torn by a crash is never published, and
bit rot in a published one is outside 6a and 6c.

Names: a run is `index/runs/<from>-<to>.<n>`, positions in twenty digits and
`n` a sequence number never reused; a checkpoint is `checkpoints/<P>`.

### 7.3 Projection state — `derived/index/state`

Kind 3; *to position* is the projection's position, which is the last run's.
Its contents are its directory, at offset 0:

| Size | Field |
|---|---|
| 8 | the next run sequence number |
| 4 | run count |
| per run | `u16` name length, UTF-8 blob name, `u64` from position, `u64` to position |

Runs are listed oldest first; their ranges are contiguous from 0 to *to
position*. The oldest may be a checkpoint, named as one. The file is replaced
atomically, which is how the projection's position is persisted with its state
(ADR 0016). Runs that no valid state names are deleted on open.

### 7.4 Commit index — `derived/index/commits/<from>-<to>.<n>`

Kind 4 (ADR 0089). The entries of the commits after *from position* up to *to
position*, end to end from offset 0, 80 bytes each, little-endian:

| Offset | Size | Field |
|---:|---:|---|
| 0 | 8 | the commit's timestamp, in ticks |
| 8 | 32 | the hash of its header |
| 40 | 4 | the segment its first record is in |
| 44 | 4 | zero |
| 48 | 8 | the offset of its first record in that segment |
| 56 | 8 | the canonical counter after it |
| 64 | 8 | the blank counter after it |
| 72 | 8 | the bytes of the log's records up to its end |

The directory follows the entries, at *directory offset* = 80 × the count:

| Size | Field |
|---|---|
| 4 | entries per block, `128` |
| 8 | the count, *to position* − *from position* |
| 8 per block | the first timestamp of each block |

The header's hash at *to position* is the last entry's header hash. A blob is
used only as §7.1 says and if its last entry's hash is the header's; on open,
the blobs that continue the chain from position 0 are compared entry by entry
with the log, and the index is rebuilt from the log from the first that
differs. The newest entries are not in a blob but in memory; the blobs are
merged, so there are few. The *n* is a sequence number.
