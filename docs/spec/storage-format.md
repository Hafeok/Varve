# Storage format

Format specification, **version 1**.

Status: written by milestone 6a with [ADR 0072](../adr/0072-format-version-1.md),
which is the reasoning; this document is the layout. **Version 1 of `log/` is
frozen from the first prerelease tag that writes it** (`v0.1.0-preview.1`, ADR
0029), and from then on every version of Varve reads it. `derived/` carries its
own version and is **not** read forever: a derived file this build does not
read is a cache miss and is rebuilt (ADR 0072).

Scope: the bytes of a dataset directory. The state machine they encode is
[`log-and-projection-model.md`](log-and-projection-model.md) (version 1.4); the
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
    checkpoints/<P>.ckpt
    index/state
    index/runs/<from>-<to>.<n>.run
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
the segment **open-ended, holding a torn tail and nothing else**.

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
   **Except:** when its header verifies and only its body does not, and a record
   whose header verifies and whose position is above `H + 1` follows
   immediately, the log is **refused** — no crash produces a later commit after
   an earlier one that was not flushed.
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
   whose header does not verify is abandoned.

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

Every file begins with a **derived header** — 160 bytes:

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | magic `VRVD` |
| 4 | 2 | derived format version, `1` |
| 6 | 2 | kind: `1` run, `2` checkpoint, `3` projection state |
| 8 | 16 | dataset id |
| 24 | 8 | from position (a run covers the commits after it; otherwise 0) |
| 32 | 8 | to position |
| 40 | 32 | the header hash of the commit at *to position* |
| 72 | 8 | directory offset |
| 80 | 8 | directory length |
| 88 | 32 | SHA-256 of the directory |
| 120 | 8 | reserved |
| 128 | 32 | self-hash of bytes 0–127 |

A derived file is used only if its header and directory verify, its dataset id
is the log's, and its *to position*'s header hash is the log's. Otherwise it is
a cache miss.

### 7.1 Runs and checkpoints

A **quad key** is 32 bytes: four `u64` ids in the order's permutation. The six
orders are `SPOG`, `POSG`, `OSPG`, `GSPO`, `GPOS`, `GOSP`, numbered 0–5 (ADR
0041). A run has twelve **sections**: for each order, its asserted keys, then its
retracted keys, each ascending. A checkpoint is a run with no retractions and a
dictionary.

**Directory:**

| Size | Field |
|---:|---|
| 4 | keys per block, `128` |
| 4 | section count, `12` |
| 16 × 12 | per section: `u64` byte offset, `u64` key count |
| 32 × Σ⌈count / block⌉ | fences: the first key of every block, section by section |
| checkpoint only: 8 | canonical counter |
| checkpoint only: 8 | blank counter |
| checkpoint only: 8 | dictionary offset |
| checkpoint only: 8 | dictionary length |
| checkpoint only: 32 | SHA-256 of the dictionary |

The **dictionary** of a checkpoint is the term entries (§4.4) of every canonical
id from 1 to its counter, in order.

### 7.2 Projection state — `derived/index/state`

Kind 3; *to position* is the projection's position. Its directory:

| Size | Field |
|---:|---|
| 8 | sequence number of the next run file |
| 4 | run count |
| per run | `u16` name length, UTF-8 blob name, `u64` from position, `u64` to position |

Runs are listed oldest first; their ranges are contiguous from 0 to *to
position*. The file is replaced atomically, which is how the projection's
position is persisted with its state (ADR 0016).
