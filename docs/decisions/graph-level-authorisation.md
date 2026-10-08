---
set: graph-level-authorisation
namespace: varve
adr: 0106
decisions:
  - key: GrantsAreScopedByGraphSet
    statement: "A grant is (dataset, permission, graph set), the set all, an explicit list of graph IRIs with default naming the default graph, or a set of IRI prefixes; 7a's Read and Write lists are the all case; the mapping from claims lives in the host's configuration"
  - key: AccessScopesSeam
    statement: "Varve.Protocol receives the resolved scope per request through IAccessScopes, a host-implemented seam answering a readable GraphScope, a writable GraphScope and whether the caller is admin, and knows no claim"
  - key: ScopedReadsThroughGraphScopedQuadSource
    statement: "A read whose readable scope is not all evaluates over GraphScopedQuadSource, a layer-1 wrapper that filters every Match, Contains and Estimate by graph so that an unreadable graph is unobservable through any pattern or enumeration"
  - key: AllScopeSkipsTheWrapper
    statement: "A request whose scope is all is served by the unwrapped view, allocating and costing what it did before; the wrapper is skipped, not a no-op layer"
  - key: UnreadableGraphIsAbsentNotAnError
    statement: "A FROM or FROM NAMED naming an unreadable graph names an empty graph, never an error, so no error text reveals a graph's existence"
  - key: FeedAndDiffFilteredPerGraph
    statement: "The feed and the diff filter each delta to the readable graphs, drop commits that become empty, and deliver Settings and Erasure commits to admin only"
  - key: ScopedWritesCheckedBeforeSubmit
    statement: "The effective delta is computed as before and every quad is checked against the writable set before the submit; one unwritable quad fails the request with 403 naming the graph and nothing commits"
  - key: PatternsEvaluateOverReadableScope
    statement: "A DELETE WHERE, a DELETE/INSERT WHERE and every other pattern of an update evaluate over the readable scope, so a caller cannot delete what it cannot read; UpdateOptions carries ReadScope and WriteScope"
  - key: UpdateSeamCarriesAccessScope
    statement: "ISparqlUpdateExecutor.ExecuteAsync takes the caller's CallerScope and the host passes it to the update options"
  - key: GspOnUnreadableIs404OnUnwritableIs403
    statement: "A Graph Store PUT or DELETE of an unwritable graph is 403 and of an unreadable one 404, the same as a missing graph"
  - key: AdminIsDatasetWide
    statement: "Admin is dataset-wide and reads and writes every graph; there is no graph-scoped admin"
  - key: ScopeFilterIsASetLookup
    statement: "The scoped source decides an explicitly granted graph by a hash set lookup of its handle under the source's term equality, resolved once when the source is wrapped, which allocates nothing per quad"
  - key: PrefixDecisionMemoisedPerGraph
    statement: "A prefix grant is decided once per distinct graph handle the scoped source meets, by externalising the graph and comparing its IRI with the prefixes, and the decision is kept in a map for the source's lifetime, so a prefix check costs its allocation once per graph and never per quad"
  - key: RowLevelIsMilestone9
    statement: "Graph-level is what the index key gives for free; row-level access per subject or classifier is milestone 9's beside erasure and is not approximated here"
---

The rulings of [ADR 0106](../adr/0106-graph-level-authorisation.md), filed unaccepted by milestone 7b of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
