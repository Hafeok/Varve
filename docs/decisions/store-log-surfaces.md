---
set: store-log-surfaces
namespace: varve
origin: "DD0013 findings on Varve.Store.Log in session 3 of #43"
decisions:
  - key: UnavailableReasonIsDisplayText
    statement: "CommitResult.Reason is the text a caller shows or logs when a commit is unavailable, a string, because nothing compares, parses or routes on it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: CarriedHandlesAreAPrivateHashSet
    statement: "Commit memoises the handles it carries as a private HashSet, built in a local, published once by ??= and never written after or handed out, because every immutable set measured costs 2.4 to 8.5 times its build on a commit's first externalisation"
---

# Primitives on Varve.Store.Log's surface

**Accepted** by the maintainer on 2026-09-29. Filed by session 3 of #43.

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

**Amended 2026-10-27**, filed unaccepted (ADR 0066): `CommitRequest` above is
`CommitRequestBuilder`, still in `Varve.Store.Log`, and `RequestTerm` is its
term. No key of this set names the type; none changes.

`DecisionDriven.Analyzers` 0.1.0-preview.10 reports the type under DD0019,
because `Assert` and `Retract` change it after it is made. The rule's own
exemption for a type mutable by design is a sealed `*Builder`. That is what the
type is: assembled by its caller, passed once to `Dataset.CommitAsync` and never
handed out as a value. `[DesignDecision]` would have accepted a violation, and no
decision says the request is a model value. Nothing else about the type changes.
Breaking before 1.0 (ADR 0118).

**`CarriedHandlesAreAPrivateHashSet`**, filed 2026-10-27, unaccepted until the
maintainer accepts it (ADR 0066). `Commit.TryExternalise` answers a handle the
commit carries but did not allocate by building, once, the set of every handle
in its delta, metadata and attachments. preview.10's DD0019 reports a
`[Memoized]` plain collection, because readers filling one collection together
race. This one is never filled together: `Carried()` builds it in a local and
`??=` publishes the finished set, which is only read from then on. A reader
that loses the race builds an equal set and drops it. The immutable sets that
answer the rule cost more, by `CommitPathBenchmarks.FirstExternaliseOfACarriedHandle`
at 100,000 quads in one run on one machine:

| Set | Build and one lookup | Allocated |
|---|---:|---:|
| `HashSet<ulong>` | 4.4 ms | 11.95 MB |
| `FrozenSet<ulong>` | 10.7 ms | 22.77 MB |
| `ImmutableHashSet<ulong>` | 37.7 ms | 10.69 MB |
| sorted `ImmutableArray<ulong>` | 15.5 ms | 3.13 MB |

The `HotPath` scope is the exception for a measured cost, not for a per-quad
hot path: the build runs once per commit.
