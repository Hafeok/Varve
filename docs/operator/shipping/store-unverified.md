# `store-unverified`

| | |
|---|---|
| **Where** | `/health/ready` and `varve status`, under a dataset's `shipping` member |
| **The shipper** | writes nothing to the target, and tries again after its back-off |
| **The dataset** | is not affected |

The target's store holds no verification record this build accepts (ADR
0184 decision 9, ADR 0183). `detail` says which:
- there is none at the store's root;
- there is one of a format this build does not read;
- there is one of an older suite than this build needs.

A log shipped to a store that loses a race between two writers can be
forked without either side being told. So a shipper writes only to a store
the contract suite has passed against, and the record at the store's root
is how it knows.

This is not an answer to a request. `varve apply` accepts the declaration,
whatever the store's provider, and says beside its answer when the project
has not tested that provider.

## What to do

- Run `varve storage verify` against the target's store, as an identity
  that may write at its root, with this build
  ([Object stores](../object-stores.md)). A pass leaves the record. The
  shipper starts by itself at its next try: nothing is restarted.
- If the run fails, the store does not keep the contract a log rests on.
  Ship to another store. The run prints the case it broke.
- After upgrading the store, run it again: the record says what one
  version of the store did.
