# 0080 — Derived keys compressed in their blocks

## Status

**Proposed — filed unaccepted by session 6c of #10, 2026-10-05** (ADR 0066).
Acceptance is the maintainer's act on the pull request.

Answers the question the 6a record left for the maintainer, "whether derived
runs are compressed, given the locality numbers above; that is a derived
format version, never a `log/` one". A derived format change (version 2,
storage format §7), which ADR [0072](0072-format-version-1.md) allows.

**Revisit condition:** a scan benchmark where decoding blocks costs more than
the I/O it saves on the machines Varve targets.

## Context

A key is 32 bytes, four 64-bit ids, in six orders: 192 bytes of runs per
quad. 6a measured ADR 0012's locality hypothesis: sorted keys delta-encode to
5.2–7.1 bytes with counter ids. And the 100-million-quad bulk gate of 6c needs
the delta's six orders, the log and the spills on one disk at once; at 192
bytes a quad the runs alone would be 19 GB.

## Decision

Keys on disk are stored in their blocks of 128, compressed: the block's first
key whole, 32 bytes, then each key as a byte naming the first of its ids that
differs from the key before, that id's increase as a varint, and the ids after
it as varints. A block decodes on its own. The directory holds, per section,
its byte length and where each block begins, beside the fences; a reader holds
both in memory (40 bytes per 128 keys) and reads and decodes one block per
seek. It holds them in chunks of 64 KiB, below the large object heap, read
from the directory in pieces and hashed as they are read, and a writer streams
the directory the same way: as one array per run they were rebuilt larger at
every merge and checkpoint on a heap that is not compacted, which was the
soak's remaining growth (issue #61). The cursor, the estimate and the merge still count keys by block and
index: only the bytes of a block changed. A block that does not decode, or
does not use exactly its bytes, is refused as damaged.

Derived format version 2 is this and ADR 0079's term sections together. A
version 1 file is a cache miss and is rebuilt.

## Alternatives considered

- **A general compressor** (Deflate, Brotli, in the BCL) over each block.
  Slower to decode and blind to the structure that delta coding uses; and a
  block of 4 KiB is small for a dictionary coder.
- **No compression.** The 100-million-quad gate does not fit the test
  machine's disk, and every disk read is six times larger than it need be.
- **Compressing across blocks.** Smaller, but a seek would decode from the
  start of a run.

## Consequences

- Runs are about six times smaller on disk; a bulk load writes and reads that
  much less. Sizes per quad are in `tests/Varve.Benchmarks/README.md`.
- A scan decodes varints; the benchmark rows for scans over disk runs are
  measured again with it.

## Checks

- **Checked against the accepted ADRs** (0001–0077). Touches **0041** (a run's
  meaning unchanged), **0071** (blocks still read through the synchronous
  read), **0072** (the derived version), **0012** (the locality it uses). No
  conflict.
- **Layer ownership.** `Varve.Store`, layer 4. Nothing public.
- **Analyzer rule.** None. The decoder is `[HotPath]` and allocates nothing.
- **Open questions owned.** None.
