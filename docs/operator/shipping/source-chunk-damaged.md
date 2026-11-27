# `source-chunk-damaged`

| | |
|---|---|
| **Where** | `/health/ready` and `varve status`, under the dataset's `shipping` member, with the slot in `detail` |
| **The shipper** | reads the chunk again every round, and ships nothing past it |
| **The source dataset** | is not affected by the shipper. See below for what the chunk means for it |
| **The target** | holds the log up to the chunk before, and nothing of this one |

A chunk of the source's log did not verify when the shipper read it: the
hash in its header is not the hash of its header, or the hash of its bytes
is not the one the header records (ADR 0184 decision 6). The shipper
checks both before it creates a chunk on a target. A chunk that fails
either is not shipped, and nothing is written for it.

`detail` names the log slot, `NNNNNNNN.chunks/CCCCCCCC`: the segment and
the chunk.

## What to do

1. **Wait one round.** A read can be damaged where the object is not. The
   shipper reads the chunk again at its next round, and the state returns
   to `shipping` by itself if that read verifies.
2. **If it stays,** the object in the source's store is damaged. A chunk
   is written once and never changed, so the store changed bytes it had
   acknowledged. This is damage under `log/`, and nothing rebuilds a log:
   [Damaged data](../backup-and-restore.md#damaged-data) says what that
   means for the source dataset, which fails its next open.
3. **The target is whole up to the chunk before.** It is a copy to restore
   or promote from, and what it lacks is every commit from that chunk on.
   `shippedThrough` is the last position it holds.
4. **Report it** on an issue, with the store, its version and the slot.

Nothing here is retried into success by a timer, and nothing on the target
is overwritten.
