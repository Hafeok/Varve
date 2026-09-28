---
set: store-log-surfaces
namespace: varve
origin: "DD0013 findings on Varve.Store.Log in session 3 of #43"
decisions:
  - key: UnavailableReasonIsDisplayText
    statement: "CommitResult.Reason is the text a caller shows or logs when a commit is unavailable, a string, because nothing compares, parses or routes on it"
---

# Primitives on Varve.Store.Log's surface

**Unaccepted.** Filed by session 3 of #43, for the maintainer.

ADR 0065 left two `string`s on the store's surface to the two-bucket rule: the
derived store's names, and `CommitResult.Reason`.

The names took the design change. `IDerivedStore` takes and lists a `BlobName`,
a `readonly record struct` over a non-empty string in `Varve.Store.Log`,
ordered ordinally because `ListAsync` returns names in ordinal order
(ADR 0040). A checkpoint's name is one (`Checkpoint.Name`).

`RequestTerm`, one of the three value types ADR 0065 did not move, took the
design change too: it is a `CommitRequest`'s term, so it moved to
`Varve.Store.Log` with `CommitRequest`.

**`UnavailableReasonIsDisplayText`.** `CommitResult.Reason` says why a commit
was unavailable. It is diagnostic text, set by the store and read by a person.
No caller branches on it: the outcome that matters is `CommitOutcome.Unavailable`.
`CommitResult.Reason` cites this. The alternative is a wrapper, an
`UnavailableReason` over `string`, which names the text but adds no rule to it.
