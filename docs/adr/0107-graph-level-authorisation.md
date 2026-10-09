# 0107 — Graph-level authorisation: scoped grants, the scoped quad source, and the row-level boundary

## Status

**Accepted — filed unaccepted by milestone 7b of #11, 2026-10-08** (ADR 0066).
Decided by the maintainer in the 7b brief, item 5, and on the plan. **Refines
ADR [0037](0037-server-authentication.md)** point 3 (the three permissions per
dataset become the `all` case of a scoped grant) and **amends ADR
[0091](0091-varve-protocol-and-varve-server.md)** by a dated note: the update
seam carries the caller's scope, and a fourth seam resolves it. Acceptance is
the maintainer's act on the pull request.

## Context

7a's permissions are per dataset: `read`, `write` and `admin`, each a set of
claim values, decided on the route's `DatasetName`. A dataset is often shared
by parties who may see different graphs of it. The store's model gives graphs
for free: a graph is a component of every index key (ADR 0041), every pattern
names its graphs through `GraphPattern` (ADR 0022), and the feed's filter is
by graph (ADR 0042). Nothing in the store knows a subject or a classifier
until erasure mode's classifier assigns quads to classes (ADRs 0021, 0023,
milestone 9).

The protocol's reads evaluate over an `IQuadSource` the host pins (ADR 0095),
its updates evaluate patterns inside `SparqlUpdate` over the staging view
(ADR 0057), and its feed reads commits through the subscription (ADR 0097).
Each is a place where a graph can be hidden; none of them should know a
claim.

## Decision

1. **A grant is scoped**: `(dataset, permission, graph set)`, the graph set
   `all`, an explicit list of graph IRIs where `default` names the default
   graph, or a set of IRI prefixes. 7a's `Read` and `Write` lists are the
   `all` case, so existing configuration and claim mappings mean what they
   meant. The host's configuration (ADR 0101's shape) gains, per dataset:

   ```json
   "Grants": [
     { "Claim": "Varve.People", "Permission": "write", "Graphs": [ "default", "https://example.org/graphs/people" ] },
     { "Claim": "Varve.Public", "Permission": "read",  "GraphPrefixes": [ "https://example.org/graphs/public/" ] }
   ]
   ```

   A caller's readable set is the union of the graph sets of the `read` and
   `write` grants any of its claims matches (write grants read, as 7a's lists
   do); its writable set the union of the `write` grants'. **Admin is
   dataset-wide**: there is no graph-scoped admin, and a dataset admin reads
   and writes every graph.
2. **The scope reaches `Varve.Protocol` as a value, resolved by the host.** A
   fourth seam, `IAccessScopes`, answers `CallerScope ScopesOf(ClaimsPrincipal,
   DatasetName)`: a readable `GraphScope`, a writable `GraphScope`, and whether
   the caller is admin. `Varve.Protocol` knows no claim. The conformance host
   and anonymous mode answer `all`, `all`, admin.
3. **`GraphScope` and `GraphScopedQuadSource` live in `Varve.Rdf`**, layer 1,
   beside the overlay (ADR 0017): a wrapper over `IQuadSource` that filters
   every `Match` and `Contains` by graph, so that a `GRAPH ?g { }` over it
   enumerates only readable graphs, a `Match` of an unreadable named graph or
   of the unreadable default graph is empty, and `Estimate` answers the
   inner's bound for a readable pattern and zero for an unreadable one.
   `TryInternalise` and `TryExternalise` pass through: a term's existence is
   not a graph's. The filter is `[HotPath]`: an explicit set is a lookup of
   the graph handle in a set of handles resolved once; a prefix set is a
   decision per distinct graph handle, memoised, so a prefix check runs once
   per graph per source, not per quad. The two are this ADR's hot-path
   exceptions, `ScopeFilterIsASetLookup` and `PrefixDecisionMemoisedPerGraph`
   in its set, cited where `VARVE0003` would otherwise report the set lookup
   and the map.
4. **Reads.** When the readable scope is not `all`, the request's view is
   `GraphScopedQuadSource(pinned, readable)`; **when it is `all`, the wrapper
   is not built**, so a request with an `all` grant allocates and costs what
   it did in 7a, measured. The scoped source answers the query, the
   `CONSTRUCT`, the Graph Store `GET`, the service description's graph
   enumeration, and every `FROM`/`FROM NAMED`: **a dataset clause that names
   an unreadable graph names an empty graph**, not an error, so no error text
   says whether the graph exists.
5. **The feed and the diff** filter each delivered commit's delta to the
   readable graphs, drop a commit whose filtered delta is empty, and deliver
   `Settings` and `Erasure` commits to `admin` only; the next delivered
   record carries its true position, so resumption is unchanged (spec §8).
6. **Writes.** The effective delta is computed as today; then, before the
   submit, **every quad in it is checked against the writable set**, and one
   unwritable quad fails the whole request with `403` and a problem body
   naming the graph (`graph-not-writable`, the graph IRI as an extension
   member), with nothing committed.
   - `UpdateOptions` gains `ReadScope` and `WriteScope`; a `DELETE WHERE`, a
     `DELETE … INSERT … WHERE` and every other pattern evaluate over
     `GraphScopedQuadSource(staging overlay, readable)`, so a caller cannot
     delete what it cannot read, and the check before the submit throws
     `GraphNotWritableException`, which the protocol turns into the `403`.
   - `ISparqlUpdateExecutor.ExecuteAsync` gains the `CallerScope` parameter;
     the host passes it to the options. ADR 0091 records it by amendment.
   - Graph Store `PUT`, `POST` and `DELETE`: a quad of the body or of the
     target graph outside the writable set is `403`; a `PUT` or `DELETE` of a
     graph **outside the readable set is `404`**, the same answer as for a
     missing graph, because a caller who cannot read it cannot know it exists.
     A `POST` to the store, whose graph the server just named, reveals
     nothing and is `403` alone when that name is outside the writable set.
   - A `CLEAR ALL`, `DROP ALL` or `DELETE WHERE` over every graph clears
     what the caller reads: a graph outside the readable set is untouched
     and unmentioned, which is the same rule as for a `FROM`.
   - `LOAD` and `CLEAR`/`DROP`/`ADD`/`COPY`/`MOVE` produce quads like any
     operation, and the same check applies to the composed delta.
7. **The tests.** Every W3C query and update evaluation case re-run through
   the protocol with an `all` grant equals the unscoped run, as a third
   subject of the conformance harness under the ratchet; a property that for
   any generated dataset, scope `S` and query `Q`, the scoped result equals
   `Q` over the sub-dataset holding only `S`'s graphs; a property that no
   response — body, headers, problem details, service description, feed,
   diff — to a caller without `read` on `G` contains an IRI that occurs only
   in `G`; the 7a auth matrix extended with a graph-scoped user; the
   allocation test of the `all` path, and a benchmark row of the scoped
   path's cost per pattern evaluation.
8. **The boundary.** Graph-level is what the store's model gives for free.
   **Row-level** — per subject, per classifier, per value — is milestone 9's,
   beside erasure, where the classifier already assigns quads to classes
   (ADRs 0021, 0023); it is not approximated here by patterns, filters or
   rewriting, and a grant cannot name anything but graphs.

## Alternatives considered

- **Rewrite the query** to add `GRAPH` restrictions. Fragile across property
  paths, `MINUS`, `EXISTS` and `FROM`; the quad source is below all of them
  and sees every pattern once.
- **Filter in the evaluator.** A layer-3 package would then know a grant.
  The wrapper is layer 1 and knows a set of graphs.
- **Scope inside the store** (a view per caller). ADR 0037 rejected
  authorisation inside the store; a scoped source is a view, and it lives
  where the overlay does.
- **`403` for a Graph Store `PUT` of an unreadable graph.** Tells the caller
  the graph exists. `404` is the same answer as for an absent graph.
- **Allow `FROM <unreadable>` to error.** The error text would name the
  graph's existence.
- **Row-level now, by subject prefix.** A prefix of a subject is not a
  property of a quad the index keys; it would be a scan with a filter, and a
  half of milestone 9's design with none of its classifier.

## Consequences

- A user without `read` on `G` cannot observe `G` through any endpoint,
  which the second property states and tests.
- `Varve.Rdf`'s baseline gains `GraphScope`, `GraphScopedQuadSource`,
  `CallerScope` and `GraphNotWritableException` — the exception at layer 1
  beside the scope, because the executor that throws it and the protocol
  that answers it are both layer 5 and cannot reference each other (ADR
  0091); `Varve.Sparql.Store`'s gains the two options; `Varve.Protocol`'s the
  seam and the parameter.
- A scoped request pays a lookup per quad of an unrestricted pattern; the
  benchmark row says how much.
- The service description's named-graph list is per caller, so its `ETag`
  is per position and the response varies by authorisation; `Vary` says so.

## Checks

- **Checked against the accepted ADRs** (0001–0101) and specification 1.6.
  Touches:
  - **0017**: the overlay's family, one more wrapper at layer 1;
  - **0022** and **0041**: graphs in every key and every pattern;
  - **0037**: point 3 refined; the mapping stays in the server;
  - **0042**, **0046**, **0097**: the feed's filter and the kinds that bypass
    it, now per caller;
  - **0057**: the update's evaluation source wrapped, the submit checked;
  - **0091**: two seams by amendment;
  - **0092**: a new problem type;
  - **0095**: the pinned view wrapped, its lifetime unchanged.

  No conflict.
- **Layer ownership.** `Varve.Rdf` (1) the scope and the wrapper;
  `Varve.Sparql.Store` (5) the options and the check; `Varve.Protocol` (5)
  the seam and the answers; `Varve.Server` (6) the grants.
- **Analyzer rule.** None new. `VARVE0003` checks the filter's hot path.
- **Open questions owned.** None. Row-level access is milestone 9's.
