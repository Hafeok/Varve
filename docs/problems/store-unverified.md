# `store-unverified`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/store-unverified` |
| **Title** | A shipping target's store is not a verified one. |
| **Status** | `409` |
| **Members** | `dataset`, `provider`, `state` |

A declaration names a shipping target (`varve:shipsTo`) whose
`varve:provider` the compatibility page compiled into this build does not
list as verified (ADR 0184 decision 9, ADR 0183). Nothing was applied.

- `dataset` is the dataset whose declaration names the target.
- `provider` is the id it names.
- `state` is what the page says of that id: `untested`, `unsupported`,
  `no adapter`, or `unknown` for an id the page does not list.

A log shipped to a store that loses a race between two writers can be
forked without either side being told. So a target is a store the contract
suite has passed against, and the page lists which those are, each with its
evidence.

This answer is about the provider, which is all an apply can see. The store
itself is checked when shipping starts: it needs a verification record at
its root. A target without one is not an answer to a request. It is
reported on readiness, as `store-unverified` under the dataset's
`shipping` member, and nothing is written to it.

## What to do

- Read [Object stores](../operator/object-stores.md) for the provider's
  state and what it rests on.
- `unknown`: correct `varve:provider` to an id the page lists.
- `untested`: run `varve storage verify` against the store, and send the
  result record so that a later build lists the provider as verified. Until
  a build does, declare a target on a verified provider.
- `unsupported` or `no adapter`: ship to another store. The page says why
  this one cannot be used.
- For a target that readiness reports as `store-unverified`: run
  `varve storage verify` against that store with this build. The shipper
  tries again by itself.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
