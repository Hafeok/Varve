---
set: store-hot-path-scope
namespace: varve
origin: "VARVE0003 findings on Varve.Store in session 3 of #43"
decisions:
  - key: RunBuildAllocatesTheRunItReturns
    statement: "Building a run from a commit's delta allocates the run's six key arrays and the array that holds them, because those arrays are the run, kept as long as the index version that holds it; the loop over the quads allocates nothing"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: PrivateTermComparisonResolvesValues
    statement: "Once a dataset has private terms, the store's term comparer resolves a handle to its term to compare or hash it, a lookup through the private-term values and the dictionary per call, because specification 1.2 lets a readable private term equal a canonical one"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: TermLookupsAreHashLookups
    statement: "The store's per-term lookups, interning a term, resolving a request's term and staging one, are lookups in hash tables keyed by term under RdfTerm's comparer or by the ids of a triple term's parts, which hash and compare in place and allocate nothing; a term the table does not hold yet adds its entry and its allocation, once per term per request"
---

# What the hot-path rules do not cover in the store

**Accepted** by the maintainer on 2026-09-29. Filed by session 3 of #43.

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

**`TermLookupsAreHashLookups`.** Filed by the close of session 3 of #43,
unaccepted. The store's inner loops are now marked: encoding and decoding a
commit's quads (`LogFormat.EncodeQuads`, `.DecodeQuads` and the log `Reader`),
interning (`TermDictionary.TryFind`), resolving a request's terms
(`Resolver.Resolve`), staging (`StagingView.Stage`, `.StageTriple`,
`.TryInternalise`), and the subscription filter (`Dataset.Keep`). Orchestration
is not marked: opening, appending, a commit's sequencing, a checkpoint's
encoding, and the term entries of a body, which are per term and allocate the
term. Each hash table lookup is a call into `ConcurrentDictionary`,
`Dictionary` or a comparer, and a new term adds an entry. The private helpers
that do this cite this key: `TermDictionary.TryFindTerm` and `.TryFindTriple`,
`Resolver.Label`, `.Triple` and `.Fresh`, and `StagingView.StageValue`,
`.StageParts`, `.TryFindStaged` and `.TryFindStagedParts`. The alternative is
an open-addressed table of Varve's own over term bytes, which ADR 0016 leaves
to milestone 6's storage engine.

`TermIds.TryInline` parsed an inline integer with `Utf8Parser`, which is not on
the allow-list; the lexical form is already checked canonical and at most 18
characters, so it now reads the digits itself.
