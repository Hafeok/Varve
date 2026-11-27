# `awaiting-shipping`

| | |
|---|---|
| **Where** | `/health/ready` and `varve status`, as a dataset's own state, with its `reason` |
| **The instance** | stays ready |
| **The dataset** | is not served yet: there is nothing to read |

The dataset is declared a copy of a source, with `varve:copyOf`, and the
source's shipper has not written it yet (ADR 0185 decision 10). The fleet
that serves a copy never creates a dataset under that declaration. Its
member opens nothing and writes nothing, and waits for the first shipping
round to write the copy's lineage record and its log.

Once the first round has run, the member follows the copy as a warm
standby, and the state is `ready`.

## What to do

- Usually nothing. Look at the source: `varve status` there shows the
  shipping target and its state.
- If the source reports [`store-unverified`](store-unverified.md), run
  `varve storage verify` against the target's store.
- If the source reports no shipping target, its declaration has no
  `varve:shipsTo` for this store and prefix. Declare it.
- If the `reason` says the store holds a dataset with no lineage record,
  that one is not a copy and is not written here. Remove `varve:copyOf` if
  it is this fleet's own dataset, or ship to another prefix.
