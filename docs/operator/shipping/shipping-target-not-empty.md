# `shipping-target-not-empty`

| | |
|---|---|
| **Where** | `/health/ready` and `varve status`, under a dataset's `shipping` member |
| **The shipper** | has stopped for this target, and wrote nothing there |
| **The dataset** | is not affected |

The target's prefix holds something that is not a copy of this source (ADR
0184 decision 10):
- a dataset of another id;
- a copy whose lineage record names another source;
- a log with no lineage record at all. A copy made once by `ShipAsync`, a
  backup restored there, and a dataset another fleet created there are all
  of this kind.

A shipper continues only a copy it can prove is this source's, by its
lineage record and its log's tail.

## What to do

- Ship to a prefix that holds nothing: change `varve:prefix` on the
  target, or remove what is under it if you are certain nothing needs it.
- If the prefix holds a dataset the target's fleet created, that fleet
  declared the dataset before the first shipping round wrote it. Declare
  the dataset there as a copy, with `varve:copyOf`: a fleet never creates
  a dataset under that declaration, and waits for the shipper
  ([`awaiting-shipping`](awaiting-shipping.md)).
- To continue a copy made by `ShipAsync`, there is no way: its objects are
  not the source's. Ship afresh.
