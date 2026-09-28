---
set: store-hot-path-scope
namespace: varve
origin: "VARVE0003 findings on Varve.Store in session 3 of #43"
decisions:
  - key: RunBuildAllocatesTheRunItReturns
    statement: "Building a run from a commit's delta allocates the run's six key arrays and the array that holds them, because those arrays are the run, kept as long as the index version that holds it; the loop over the quads allocates nothing"
  - key: PrivateTermComparisonResolvesValues
    statement: "Once a dataset has private terms, the store's term comparer resolves a handle to its term to compare or hash it, a lookup through the private-term values and the dictionary per call, because specification 1.2 lets a readable private term equal a canonical one"
---

# What the hot-path rules do not cover in the store

**Unaccepted.** Filed by session 3 of #43, for the maintainer.

Session 3 held `Varve.Store`'s existing `[HotPath]` members to `VARVE0003`
(ADR 0064). Most findings were small members the hot paths call that were
simply unmarked: `QuadKey`'s components and constructor, `SubscriptionFilter`'s
properties, `TermIds.IsValidInline`, `IndexVersion.Runs`,
`DatasetView.ThrowIfDisposed`, and in `Varve.Rdf` the `TermHandle` constructor,
`InlineValue`'s factories and `IQuadSource.TryGetInlineValue`. Those are now
marked and held to the rule. Two are not per-quad costs, and are filed here
rather than unmarked.

**`RunBuildAllocatesTheRunItReturns`.** `Run.Build` is marked hot because its
loop is per quad: one key per quad per order. It also allocates, once per
order per commit, the sorted array it returns. That array is the run (ADR 0041,
`RunsAreImmutable`), and a version holds it until a merge replaces it, so it
cannot be rented or taken from the caller. `Run.Build` cites this. The
alternative is to unmark `Build` and mark only a per-quad helper, which checks
less of the loop.

**`PrivateTermComparisonResolvesValues`.** `StoreTermComparer.ByValue` exists
for datasets with private terms: its remarks cite specification 1.2's note on
§6, that every hash then needs a dictionary lookup. Resolving a term calls the
private-term values and the dictionary's delegate, and compares `RdfTerm`s by
value. The comparer's `Equals` and `GetHashCode` stay checked. The two private
helpers that take the value path cite this, and the path is only taken when
`_privates` is set. The alternative is an allocation-free, handle-only
comparison, which specification 1.2 rules out once private terms are readable.
