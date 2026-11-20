# Shipping states

What `/health/ready` and `varve status` say of a shipping target, and of a
copy (ADRs 0184 and 0185). A target's state is under its dataset's
`shipping` member, by the target's store and prefix. None of them makes an
instance unready, and none is a problem a request receives: a shipper
answers no request.

`varve status --server <address>` prints them for one instance, with each
state's detail.

| State | The shipper | Page |
|---|---|---|
| `shipping` | is copying, or has nothing new to copy | below |
| `retrying` | failed a round and tries again after its back-off | below |
| `target-unconfigured` | cannot reach the store: this instance has no endpoint for it | below |
| `store-unverified` | writes nothing: the target's store holds no verification record | [store-unverified](store-unverified.md) |
| `shipping-diverged` | has stopped: the target holds bytes that are not the source's | [shipping-diverged](shipping-diverged.md) |
| `shipping-target-not-empty` | has stopped: the target's prefix holds something that is not this source's copy | [shipping-target-not-empty](shipping-target-not-empty.md) |
| `replica-promoted` | has stopped: the copy was promoted | [replica-promoted](replica-promoted.md) |

A dataset's own state can also be:

| State | Means | Page |
|---|---|---|
| `awaiting-shipping` | the dataset is declared a copy, and its first shipping round has not written it yet | [awaiting-shipping](awaiting-shipping.md) |

## The three that need no page

- **`shipping`.** `shippedThrough` is the source position the target's log
  holds at least. `lag` is how far the dataset's head is past it: what
  would be lost if the source were lost now. `catalogPosition` is the
  checkpoint the target holds, from which a promoted copy replays.
- **`retrying`.** A round failed against the source or the target.
  `failures` counts the rounds in a row, and `detail` is what the last one
  said. The wait between tries doubles to thirty seconds. The dataset is
  not affected.
- **`target-unconfigured`.** The declaration names an `s3://{bucket}`
  store, and this instance has no entry for it under
  `Varve:Replication:Stores` ([Configure](../configure.md)). Add the
  entry, with the endpoint and the region. No key is set there.
