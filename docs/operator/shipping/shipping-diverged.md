# `shipping-diverged`

| | |
|---|---|
| **Where** | `/health/ready` and `varve status`, under a dataset's `shipping` member |
| **The shipper** | has stopped for this target, and changes nothing there |
| **The dataset** | is not affected |

The target holds, at a name the shipper was about to write, bytes that are
not the source's (ADR 0184 decision 3). `detail` names the log slot.

Every object on a target is written by create-if-absent of the source's
bytes. A create that finds the name taken reads it back: equal bytes are
success, and anything else is this state. Nothing was overwritten and
nothing was deleted.

It means something other than this source's shipper wrote under the
target's prefix, or the target is a copy of another history of the dataset:
- the copy was promoted and written, and its old source came back
  (see [`replica-promoted`](replica-promoted.md), which is what the
  shipper reports when it sees the promotion first);
- the source was restored from a backup older than what was shipped;
- something else wrote to the prefix.

## What to do

- Do not delete objects on the target to make the shipper go on. The two
  logs differ, and one of them holds commits the other does not.
- Decide which of the two is the dataset. If it is the source, declare a
  new target under a fresh prefix and remove this one: shipping starts
  again from the first slot. If it is the copy, the source is the one to
  retire.
- The shipper does not start again for this target by itself. It starts
  again when the declaration changes or the instance restarts, and reports
  the same state while the target is as it is.
