---
set: graph-level-authorisation
namespace: varve
adr: 0107
decisions:
  - key: AccessScopesSeam
    statement: "Varve.Protocol receives the resolved scope per request through IAccessScopes, a host-implemented seam answering a readable GraphScope, a writable GraphScope and whether the caller is admin, and knows no claim"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: ScopedReadsThroughGraphScopedQuadSource
    statement: "A read whose readable scope is not all evaluates over GraphScopedQuadSource, a layer-1 wrapper that filters every Match, Contains and Estimate by graph so that an unreadable graph is unobservable through any pattern or enumeration"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: AllScopeSkipsTheWrapper
    statement: "A request whose scope is all is served by the unwrapped view, allocating and costing what it did before; the wrapper is skipped, not a no-op layer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: UnreadableGraphIsAbsentNotAnError
    statement: "A FROM or FROM NAMED naming an unreadable graph names an empty graph, never an error, so no error text reveals a graph's existence"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: FeedAndDiffFilteredPerGraph
    statement: "The feed and the diff filter each delta to the readable graphs, drop commits that become empty, and deliver Settings and Erasure commits to admin only"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: ScopedWritesCheckedBeforeSubmit
    statement: "The effective delta is computed as before and every quad is checked against the writable set before the submit; one unwritable quad fails the request with 403 naming the graph and nothing commits"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: PatternsEvaluateOverReadableScope
    statement: "A DELETE WHERE, a DELETE/INSERT WHERE and every other pattern of an update evaluate over the readable scope, so a caller cannot delete what it cannot read; UpdateOptions carries ReadScope and WriteScope"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: UpdateSeamCarriesAccessScope
    statement: "ISparqlUpdateExecutor.ExecuteAsync takes the caller's CallerScope and the host passes it to the update options"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: GspOnUnreadableIs404OnUnwritableIs403
    statement: "A Graph Store PUT or DELETE of an unwritable graph is 403 and of an unreadable one 404, the same as a missing graph"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: AdminIsDatasetWide
    statement: "Admin is dataset-wide and reads and writes every graph; there is no graph-scoped admin"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: ScopeFilterIsASetLookup
    statement: "The scoped source decides an explicitly granted graph by a hash set lookup of its handle under the source's term equality, resolved once when the source is wrapped, which allocates nothing per quad"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: PrefixDecisionMemoisedPerGraph
    statement: "A prefix grant is decided once per distinct graph handle the scoped source meets, by externalising the graph and comparing its IRI with the prefixes, and the decision is kept in a map for the source's lifetime, so a prefix check costs its allocation once per graph and never per quad"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: GoverningGraphSeam
    statement: "GraphScope takes a governing-graph mapping, and a graph with a governor is allowed exactly when its governor is, the decision memoised per graph handle; Varve.Rdf knows no rule that names a governor"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-16T00:00:00Z
---

The rulings of [ADR 0107](../adr/0107-graph-level-authorisation.md), filed unaccepted by milestone 7b of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.

Moved to a later set: `GrantsAreScopedByGraphSet` is superseded by ADR 0163, under which the mapping from principals to graph sets lives in the system dataset, in its set.

Moved to a later set: `RowLevelIsMilestone9` is superseded by ADR 0230, under which row-level access is decided by a classifier binding and a grant's subject set, in its set.
