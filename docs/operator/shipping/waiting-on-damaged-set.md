# `waiting-on-damaged-set`

| | |
|---|---|
| **Where** | `/health/ready` and `varve status`, under the dataset's `shipping` member, with the blob in `detail` |
| **The shipper** | ships the log as before, and reads the blob again every round |
| **The source dataset** | serves and commits. A read that needs the damaged block re-runs it from the log (ADR 0213) |
| **The target** | holds the log up to `shippedThrough`, and its catalog is the last one whose sets all verified |

A blob of an extent set on the source did not verify when the shipper
checked it (ADR 0184 decision 5). The shipper verifies every blob of a set
whole before it creates it on a target, as a reader of the set verifies
the blocks it reads (ADR 0213). A damaged blob is not shipped. The catalog
that names its set is not published on the target either, so the target's
catalog never names a set the target does not have whole.

`detail` names the blob: `extents/<set>/<blob>`.

**What the target can do meanwhile.** Its log is complete up to
`shippedThrough`, so nothing committed is missing from it. Its catalog is
older than the source's, so a copy promoted now replays more of the log
before it answers: from `catalogPosition` to its head.

## What to do

1. **Usually nothing.** This is damage under `derived/`, which is derived
   from the log and never needs a backup
   ([Damaged data](../backup-and-restore.md#damaged-data)). The source's
   writer writes a damaged set again from the log, under a fresh name, on
   its next maintenance round after **it** has met the damage: in a read
   that touches the damaged block, or in a merge over the set (ADR 0213).
   The new set reaches the target as any set does, and the state returns
   to `shipping`.
2. **The shipper's finding does not tell the writer.** The shipper reads
   the store, and may run on another instance. Until a read or a merge on
   the source meets the same block, the writer does not know, and this
   state stays. Watch `varve.store.derived.damaged` on the source
   ([Observe](../observe.md#metrics)): it counts what the writer has found.
3. **If it stays for long,** report it on an issue with the blob's name
   and the store. The dataset is not at risk meanwhile, and neither is the
   copy's log.

Nothing on the target is overwritten, and a set is never rewritten on a
target: a rewrite is the source writer's.
