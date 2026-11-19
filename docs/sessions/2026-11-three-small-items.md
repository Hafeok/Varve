# Session, 2026-11-18: three small 1.0 items

Developed with AI assistance under human review, by a cloud session that is
not the maintainer's delegate (ADR 0239). Each pull request has its own
record, with the prompt:
[#327's](../traceability/2026-11-18-issue-327-absent-secrets.md),
[#340's](../traceability/2026-11-18-issue-340-reserve-proof.md),
[#332's](../traceability/2026-11-18-issue-332-idle-fleet.md).

## Pull requests

None carries a release descriptor; they may land as one train, in this order.

| PR | Step | Head | Closes | Hosted result |
|---|---|---|---|---|
| [#343](https://github.com/Hafeok/Varve/pull/343) | 1: a declared secret may be absent in repo-standard | `eb7c6a02` | #327 | every job green but `agent review` (no approval yet) |
| [#346](https://github.com/Hafeok/Varve/pull/346) | 2: `proof/` reserved in storage format 1 | `424ef678` | #340 | red on `CS0618` for the unaccepted `ProofIsReserved` (both `build` jobs, `api freeze`, the decision report); `pipeline (devcontainer)` red earlier, at the image, on `packages.microsoft.com` answering `429`; and `agent review` |
| [#353](https://github.com/Hafeok/Varve/pull/353) | 3: an idle fleet's system dataset does not move | `8337e23e`, then this close-out | #332 | every job green but `agent review` at `8337e23e` |

## Decisions, all filed unaccepted

- **ADR 0039**, amended 2026-11-18: `ADeclaredSecretMayBeAbsent` in
  `repo-standard`.
- **ADR 0216**, amended 2026-11-18: `ProofIsReserved` in
  `the-format-inventory`, and a fourth inventory rule, *reserved*.

No decision for #332: two internal seams in `Varve.Server` and a CI step, no
behaviour.

## Issues

- **Closed by trailer, once landed:** #327, #340, #332.
- **Commented:** #166, telling the shipper's session (#338) that `proof/` is
  reserved and that its retired-set deletion must list named prefixes only.
  Its branch was read, not changed.
- **Opened:** none. Nothing was found that this session left undone.

## What each reader does with an unknown sibling of `log/` and `derived/`

From `main` at bb1d142 (posted on
[#340](https://github.com/Hafeok/Varve/issues/340#issuecomment-6978986842)):
none of them looks. Each names what it reads.

| Reader | File backend | Object store |
|---|---|---|
| Open for read | no read-only file open | lists `log/`, `derived/`, `derived/extents/`, `readers/`; reads `writer` |
| Open for write | creates and lists `log/` and `derived/` only | as for read, plus `probe/<id>`, `writer`, `readers/<id>`; deletes only under `derived/` and replaced chunks under `log/` |
| A follower | none (only `ObjectStorage` tails) | probes the next `log/` slot, reads `derived/` |
| Backup and restore | a directory copy carries it; `ShipAsync` does not, and accepts a target holding only `proof/` | `ShipAsync` into a directory, or the store's own replication |
| `varve storage` | n/a | the catalog, `derived/extents/`, `readers/` |
| The format gate | the inventory table and each fixture's `SHA256SUMS`; no dataset | same |
| The shipper (#338, b46d4f4) | n/a | lists `log/` only; "not empty" is `log/`, `writer`, `lineage`; deletes only replaced chunks |
| Deleting the dataset | removes the directory, `proof/` with it | removes the prefix, `proof/` with it |

`ReservedProofTests` holds it on both backends with objects no build knows.

## The idle fleet test's duration

| Where | Duration |
|---|---:|
| `ubuntu-latest`, hosted, in the server suite | 0.6 s |
| `windows-latest`, hosted, in the server suite | 0.6 s |
| this session's Linux container, alone | 0.37 s |

600 ticks of `Fleet:FollowInterval` and the idle wait, under a stepped clock.
The `build` matrix has no macOS leg. The test passed, so #332 and #227 have no
finding.

## Test counts, run here

| Suite | Before | After |
|---|---:|---:|
| `RepoStandard.Tests` | 89 passed, 1 skipped | 98 passed, 1 skipped |
| `Varve.Store.Tests` | — | 398 passed, 93 skipped (with `CS0618` relaxed on this machine only) |
| `Varve.Server.Tests` | — | 197 passed, 60 skipped |

## Waiting on the maintainer

- `accepted-by` on `ADeclaredSecretMayBeAbsent` and `ProofIsReserved`.
- An approval on each head; the session wrote none.
- On #346, a re-run of `pipeline (devcontainer)` once the decision is
  accepted, if `packages.microsoft.com` is still refusing.
