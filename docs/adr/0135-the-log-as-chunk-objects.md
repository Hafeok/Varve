# 0135 — The log as write-once chunk objects, consolidated on seal

## Status

**Accepted — filed unaccepted by milestone Object storage of #92, 2026-10-15**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the Object storage plan, 2026-10-15, with the chunk
prefix `.chunks/` and the consolidated `.seg` as proposed, and fence chunks
the only tombstones kept (ADR 0136).

## Context

The log is append-only segments (ADR 0018), in format version 1 for ever
(ADR 0072). An object store has no append: an object is written whole, once,
or replaced whole. Group commit (ADR 0124) already made the batch the unit of
durability: "on the object store (#92) a batch is one object write". The
question this ADR answers is what objects the log becomes, so that

- a batch costs one request,
- two writers can never both believe they wrote the same place,
- a lost response is safe to retry,
- and the log's bytes — what a reader verifies, what a copy carries, what
  determinism compares — do not change.

## Decision

**1. An open segment is a sequence of chunk objects, one per flush.** The
backend holds appends in memory; `FlushAsync` writes them as the next chunk,
`log/NNNNNNNN.chunks/CCCCCCCC` under the dataset's prefix, by
**create-if-absent**. A flush with nothing appended writes nothing. Group
commit flushes once per batch, so a batch is exactly one create: the
request count per commit, measured, is one. Opening a segment
(`CreateSegmentAsync`) creates its chunk 0, which holds no bytes; sealing
(`SealAsync`) creates a chunk marked as the seal, holding whatever was still
appended.

The chunks are under `.chunks/`, not under the segment's own name: a store
backed by a file system — SeaweedFS's filer, and MinIO when it was the
candidate — refuses an object whose name is also a prefix of others, and a
container downloaded to a disk would too.

**2. Every chunk is a header, then log bytes.** The header is 136 bytes,
little-endian:

```
  0  "VRVC"
  4  u8   version, 1
  5  u8   flags: 1 fence, 2 seal, 4 opens a segment
  6  u16  zero
  8  u64  writer epoch (ADR 0136)
 16  u32  segment
 20  u32  chunk
 24  u64  offset of the first byte in the segment
 32  u32  length of the bytes
 36  u32  zero
 40  [32] SHA-256 of the bytes
 72  [32] the previous chunk's header hash in this segment; zero for chunk 0
104  [32] SHA-256 of bytes 0..104
```

**The log is the concatenation of every chunk's bytes, in order**, and the
header is the backend's, not the log's. No byte of format 1 changes, so the
release's basis stays `storage-format: 1`, a log read out of chunks is
byte-identical to the file backend's, and the store verifies it with the
same reader. The header gives the backend what the log does not: the epoch
for the fence, and a chain it can check when it adopts chunks another writer
made, before the store reads a byte.

**3. A lost response reads back.** A create whose outcome is unknown — a
timeout, a dropped connection, a `500` after the store applied it — is
retried. A retry that finds the object reads it and compares: the same bytes
are this writer's earlier attempt, whose answer was lost, and count as
success. Bytes that differ belong to another writer (ADR 0136). A chunk's
bytes carry its epoch, so no other writer's chunk can be equal to this one's.
A write that fails every attempt without an answer fails the batch, and the
store's sequencer refuses writes until the dataset is opened again, as on
every backend (ADR 0124); opening adopts the chunk if it was written.

**4. A sealed segment is consolidated on seal.** The seal that closes a
segment then writes its bytes as one object, `log/NNNNNNNN.seg`, by
create-if-absent — byte-identical to the file backend's segment of the same
number — and from then on the segment is that object. Its chunks, **except its
fence chunks**, which are kept for ever (ADR 0136), are deleted by the
dataset's maintenance, which the seal leaves due and the sequencer starts
after the commit, sixteen deletes at a time. Chunks in the store are then
bounded by the open segment. The seal is durable before the consolidation
starts; the commit that rolled the segment waits for the one upload of a
segment's length, and not for the deletes, which on Azurite took 531 s for
one segment's 5,264 chunks when they were inline. A consolidation that fails
— the store unreachable — is left to the same maintenance, which does it
again: the store calls it through `IStorageMaintenance`, a member of the
store's contract a backend implements when it has maintenance of its own,
off the sequencer (ADR 0070), and a backend starts nothing of its own (ADR
0042). The log's bytes are unchanged
by it: before, the segment is its chunks' concatenation; after, it is the
one object. A consolidation interrupted at any step leaves either the chunks
or the object or both, and the object, when present, is what the segment is.

**5. Reads.** A consolidated segment is read from a local copy in the cache
directory, downloaded whole on its first read and kept; the copy is written
as a temporary file and renamed, so it is whole or absent. A segment not yet
consolidated — the open one, and a sealed one maintenance has not reached —
is held in memory, chunks and pending appends together: at most one
segment's length (64 MiB by default) beyond the sealed segment waiting for
consolidation. Opening lists `log/` once and reads only the newest chunk,
which the fence needs; the dataset's open then reads the open segment from
the slot the catalog records, and a read before it reads the chunks below
then (ADR 0139).

**6. Durability `Replicated`.** A new level of `Durability`: "acknowledged by
an object store with conditional writes; what that survives is the store's
own replication". On Azure, LRS keeps three copies in one datacentre and ZRS
three across zones; on Amazon S3 Standard, at least three availability zones;
on a self-hosted S3-compatible server, whatever its own replication or
erasure coding is configured to be — the operator guide says so for each.
Group commit is on for it, as for every level but `None`.

**7. Derived data stays local until extents.** The backend's derived store is
the file backend's, over a directory inside the cache directory, which also
holds that directory's OS lock: one process uses a cache directory at a time.
Everything derived here is rebuilt from the log on a fresh cache. Extents
and the projection catalog move into the store (ADR 0139).

## Alternatives considered

- **Chunks keyed by byte offset rather than by number.** Two writers could
  then write chunks at different offsets without either create failing,
  which is exactly the fence the chunk number gives (ADR 0136).
- **One object per segment, replaced on every flush.** A replace of a 64 MiB
  object per batch, and replace-if-unchanged as the only fence: a request
  per batch that grows with the segment.
- **Delete every chunk on consolidation.** A deleted name can be created
  again, and a superseded writer's next create would then succeed into a
  sealed segment: an acknowledged commit lost. Keeping every chunk as an
  empty tombstone was proposed and refused by the maintainer: `log/` would
  hold an object per batch ever written, so listing and cold start would
  grow with history. Only fence chunks need keeping (ADR 0136, which proves
  it).
- **Consolidate only in maintenance, off the commit path.** The commit that
  rolls a segment would not wait for its upload, but chunks then pile up
  while maintenance is behind or off, and every cold start reads them: at REC
  size M, 19,790 chunk reads and 23–29 s on loopback before this changed.
  The maintainer ruled that consolidation runs on seal, with maintenance as
  the retry.

## Consequences

- `Varve.Store` gains `Durability.Replicated` and `IStorageMaintenance`; the
  dataset's maintenance calls it.
- A segment in the store is listed as `.seg` or as `.chunks/` and never both
  for long; the reader prefers `.seg`.
- The open segment is in memory: a writer's memory includes up to two
  segments' length. The operator guide's 8 GB page states it.
- The store's open still reads every segment's header and record headers;
  over a cache that is local reads after one download per segment. ADR 0139
  bounds what a cold start reads.

## Checks

- **Checked against the accepted ADRs** (0001–0133). Touches **0018** (no
  truncate, no positional write: a chunk is created once; consolidation
  deletes chunks only after the same bytes exist as one object, so no byte of
  the log is ever lost or rewritten), **0040** (the segment contract
  unchanged), **0042** (the backend starts nothing; the dataset calls it),
  **0070** (maintenance off the sequencer), **0072** (format 1 unchanged;
  the header is outside the log's bytes), **0073** (the new backend declares
  its own level), **0124** (one create per batch). No conflict.
- **Layer ownership.** `Durability` and `IStorageMaintenance`: `Varve.Store`,
  layer 4. The chunk log: `Varve.Store.ObjectStorage`, layer 5.
- **Analyzer rule.** None.
- **Open questions owned.** None.
