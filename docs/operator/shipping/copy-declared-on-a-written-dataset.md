# `copy-declared-on-a-written-dataset`

| | |
|---|---|
| **Where** | `/health/ready` and `varve status`, on the dataset, as `"declaration": "copy-declared-on-a-written-dataset"` with a `reason` |
| **The instance** | stays ready |
| **The dataset** | is served and written as before. Nothing was taken from its writer |

The dataset's declaration says `varve:copyOf`, and this instance writes
the dataset. The two cannot both be true (ADR 0185 decision 10). A copy is
made only by its source's shipper, under an empty prefix. A dataset that
is written in this fleet, with no lineage record naming that source, is
not a copy of it.

`varve apply` refuses such a declaration with
[`declaration-invalid`](../../problems/declaration-invalid.md) when the
instance that answers can see the dataset on its store. This state is for
the apply that could not see it: the store was not read at that moment.
The declaration was applied, the running writer keeps writing, and
readiness says so at once.

**What happens if it is left.** While the writer runs, nothing. At its
next start the member reads the declaration first, finds a dataset with no
lineage record under a name declared a copy, and does not open it for
write. So the declaration must be put right before the writer is next
restarted.

## What to do

- **If the dataset is this fleet's own,** remove `varve:copyOf` and its
  `varve:ShippingSource` from the dataset's declaration, and apply it.
- **If a copy of that source is wanted here,** declare it under another
  dataset name, whose prefix is empty. The source's shipper then writes
  it.
- **If this dataset was meant to become the copy,** that cannot be done in
  place: a target is written only by create-if-absent of the source's
  bytes. Ship to a fresh prefix, and retire this dataset when the copy
  serves.
