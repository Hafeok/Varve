# 0140 — What Varve takes from Azure Data Explorer, and what it does not

## Status

**Accepted — filed unaccepted by milestone Object storage of #92, 2026-10-15**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on 2026-10-14: Varve gains ADX's real-time derivation
features (materialised views, standing queries, alerts as result deltas);
Varve takes ADX's storage scaling, projections stored as immutable extents in
the object store with local disk as a cache (T7); it does not become a
telemetry ingestion store. Filed from the maintainer's draft, which is binding
on this milestone; its text is kept, with the house's sections and checks.

Milestone: none on its own. It bounds Continuous queries (after 1.0) and
records why telemetry analytics is not a Varve goal. Related: group commit
(ADR 0124), write-once chunks and cold start (ADRs 0135, 0139), follow and
CloudEvents (Client and integration), declarations and grants at a position
(Management plane, #105), incremental maintenance and the grant property
(Inference), checkpoints (ADR 0078) and the horizon (ADR 0015), erasure (#14),
and the standing constraint that Varve makes no decision from elapsed time.

**Vocabulary.** The draft says "segment" for a projection's immutable object.
In this repository "segment" means a log segment (`log/NNNNNNNN.seg`), so this
ADR says **extent**, which is ADX's own word and the one AGENTS.md's vocabulary
table now carries (ADR 0139).

## Context

Azure Data Explorer (ADX), and Eventhouse in Fabric Real-Time Intelligence, is the closest well-known system to some of what Varve's log-first model promises:
- append-only ingestion;
- derived tables maintained on ingest;
- materialised views that are fresh at query time;
- continuous export;
- follower databases;
- dashboards and alerting on top.

The question "can't Varve do the same?" will keep coming back, from us and from users. ADX's design rests on two premises Varve does not share:
1. **High-volume telemetry, ingested on many nodes.** Data is stored as immutable columnar extents and queried by scans and aggregates.
2. **Time as a control input.** Batches seal on elapsed time. Retention and hot caching are measured from ingestion time. Materialised-view freshness is bounded by wall-clock age.

Varve's premises:
- one writer per dataset;
- a log of linked, slowly changing state with provenance per commit;
- every read pinned to a position;
- no decision taken from elapsed time.

Some of ADX carries over unchanged; some would break an invariant if copied. This ADR records which is which, so later proposals are checked against it rather than re-argued.

## Decision

### Taken from ADX

**T1. Materialised part plus delta at read time.** An ADX materialised view combines the materialised part with the not-yet-materialised delta at query time, so a query of the whole view is always up to date ([materialised views](https://learn.microsoft.com/kusto/management/materialized-views/materialized-view-overview)).
- Varve's derived results (Continuous queries, and Inference tier 2) do the same, keyed by position. A read at position p uses the projection at its applied position q ≤ p and applies the log delta (q, p].
- Lag therefore costs read latency, never correctness.

**T2. Derivation declared once, maintained on write.** ADX update policies and materialised views are declarations that the engine maintains. In Varve:
- standing queries are management declarations;
- they are maintained as projections from exact deltas.

**T3. Continuous export with a cursor.** This already exists as `FollowAsync` with a checkpoint, the CloudEvents rendering and tier-3 log shipping. Nothing new is needed.

**T4. Follower databases.** These already exist as the tier-1 follower (Management plane) and the replication tiers (#97).

**T5. Alerting as a reaction to changed results.** Activator-style rules fire when a condition starts or stops holding.
- In Varve, a standing query's result delta (solutions added and removed at a position) is the event.
- Varve emits that event. Acting on it (mail, Teams, a workflow) stays outside Varve, the same way the liveness probe stays outside writer handover.

**T6. Backfill on creation.** A new materialised view in ADX is built over existing data. In Varve, a new standing query builds in the background from the log, and readiness reports its lag.

**T7. Projections as immutable extents in the store, with local disk as a cache.** ADX scales storage by keeping extents in blob storage, separate from compute ([caching](https://learn.microsoft.com/kusto/management/cache-policy)):
- extents are immutable, compressed and sorted, with per-extent statistics;
- extents are merged in the background;
- local SSD is only a cache.

Varve applies the same to its projections, not only to the log:
- Each index permutation of the default projection is stored as immutable, sorted, compressed extents in the object store. The same goes for the dictionary's lookup projections.
  - A extent covers a position range.
  - Each quad in a extent carries its asserting position and, if retracted, its retracting position, so as-of reads work across extents.
  - Each extent carries statistics (key range, position range) so scans skip it.
- A projection catalog object names the live extents and the position they cover. Only the dataset's writer replaces it, under the same epoch fence as the log.
- Extents are merged in the background. A merge writes new extents and swaps the catalog.
- Local disk caches extents. Eviction never changes a result.
- Extents are derived. They can be dropped and rebuilt from the log, and they never enter it.

Consequences of this design:
- Cold start is: read the catalog, attach extents lazily, replay the log tail.
- Tier-1 followers share the writer's extents instead of each building their own.
- Disk size is a cache budget, not a dataset limit.

A POS permutation over inline literal ids is a per-predicate column, i.e. vertical partitioning. An aggregate over one predicate is then a sequential scan of compressed values. This is how Varve serves analytical reads over large datasets without a second storage model (see N1).

### Not taken from ADX

**N1. No columnar telemetry store, and no second query language.**
- Varve does not add extent-style columnar storage as its primary representation.
- It does not add KQL or KQL-style time-series operators (`make-series`, `series_decompose`, anomaly detection) to its query surface.
- SPARQL 1.1/1.2 is the only query language, as Inference already decided for rules.
- *Reason: the storage layout is not the gap.* With T7, a predicate's values in POS extents are a compressed column, and storage cost per reading is within a small constant factor of an extent.
- *Reason: the write path is the gap.*
  - A dataset has one writer, so ingest scales per dataset, not per node.
  - Every commit carries agent, cause and a header-chain link, which is provenance nobody asks of a sensor reading.
  - Group commit amortises this, but a firehose of independent readings is still the wrong workload.
- *Reason: query execution.* A query runs on one node (N10).
- *Instead.* High-rate telemetry belongs in an Eventhouse, or any columnar store. Varve feeds it CloudEvents and holds the linked state that the telemetry's IRIs refer to. Large, slowly changing datasets, including analytical reads over them, are Varve's own ground through T7.
- A separate columnar *secondary projection* for declared predicates stays possible under the thesis's pluggable projections. It needs its own ADR, and it is not planned: T7 covers most of what it would give.

**N2. No sealing on elapsed time.**
- ADX's queued ingestion seals a batch on size, item count or elapsed time, whichever comes first. The documented default is five minutes, so a small trickle waits for the timer ([batching policy](https://learn.microsoft.com/kusto/management/batching-policy)).
- Varve's group commit stays as decided: the batch is whatever arrived while the previous write was in flight. No timer holds a commit back.
- There is one commit path. ADX has two paths, queued and streaming, each with its own latency and throughput trade-off. Varve has no second path with different guarantees.

**N3. No retention or caching driven by age.**
- ADX retention removes data once it is older than the soft-delete period since ingestion, at an unspecified time after that. The hot cache keeps the most recent span on local SSD ([retention](https://learn.microsoft.com/kusto/management/retention-policy), [caching](https://learn.microsoft.com/kusto/management/cache-policy)).
- In Varve, the horizon moves only by an explicit act: an applied management declaration or an operator request naming a position.
  - That act may compute the position from a timestamp ("the first commit after 1 January").
  - Nothing moves because time passed.
  - A scheduled task outside Varve can issue the act.
  - This must be checked against the compaction ADR from milestone 6. If that ADR allows a time-driven trigger, this point supersedes it.
- What lives on local disk (`derived/`, checkpoints, cached chunks) is a performance matter for the backend. It never decides what is readable.

**N4. No wall-clock freshness bounds and no `now()` in derived results.**
- `materialized_view(name, max_age)` returns the materialised part only if it is younger than `now() - max_age` ([materialized_view()](https://learn.microsoft.com/kusto/query/materialized-view-function)).
- Varve expresses freshness as a position: `Varve-Min-Position` or the existing pinning. A stale-but-fast read, if ever wanted, takes a position bound, never an age.
- Standing queries must not use `NOW()`. A window such as "the last five minutes" is written against the timestamp of the commit at the evaluated position. Its result is then a function of the position, and as-of reads hold.
- The visible difference from ADX is deliberate: such a window slides only when a commit arrives.

**N5. No derived data written back into the source of truth.**
- ADX update policies write their output into ordinary target tables, optionally in the same transaction as the source ([update policy](https://learn.microsoft.com/kusto/management/update-policy)).
- In Varve, derived results are projections. They are never written to the log, they can be dropped and rebuilt, and they are readable as of any position. This is the same rule as Inference tier 2.
- An application that wants a derived fact *asserted* writes it itself, in its own commit, with its own agent.

**N6. No correctness that depends on tuning.**
- An ADX materialised view's `lookback` limits how much of the materialised part each cycle scans. If it is set too short, the view can contain duplicate records ([lookback](https://learn.microsoft.com/kusto/management/materialized-views/materialized-view-create#lookback-period)).
- Varve may offer knobs that trade cost for speed. Every knob must leave the result equal to a from-scratch evaluation at the same position. A knob that can change the answer is not offered.

**N7. No eventually consistent derived tables without a stated position.**
- Every derived result Varve serves states the position it reflects, and it is either exact at that position or refused.
- There is no "approximately the time of materialisation".

**N8. No purge by rewriting storage.** ADX removes data for compliance by rewriting extents. Varve's answer is crypto-erasure (#14), which works on every replica and every copy without rewriting the log.

**N9. No query functions as the access-control mechanism.**
- ADX row-level security is a query function applied per table.
- Varve's access control is graph-level grants, declared in the management plane and evaluated at a management position.
- Row-level access comes with the erasure classifier (#14), not as a rewrite function attached to a table.

**N10. No scatter-gather query execution, for now.**
- ADX distributes one query across the nodes that hold its extents.
- In Varve, one query runs on one node. Followers add read capacity across queries, not parallelism within one.
- T7 does not block distribution later: extents are shared, immutable objects that any node can scan. Distributed execution would be its own milestone after 1.0, with its own ADR. It is not planned.

## Alternatives considered

- **Make Varve an ADX for graphs: columnar extents, KQL-like operators, time-based policies.** Rejected:
  - it competes with mature columnar stores on their ground;
  - it breaks the position-only rule in three places (N2, N3, N4);
  - it adds a second query language.
- **Leave ADX out of the docs entirely.** Rejected: the comparison will be made anyway, and without this record, features get proposed one at a time and each is argued from scratch.
- **Allow `NOW()` in standing queries, evaluated by a timer.** Rejected:
  - the result would change with no commit;
  - as-of reads of the result would be undefined;
  - a timer would sit inside Varve's decisions (N4).

## Consequences

- Object storage (#92) settles T7 before it lands. Its plan either:
  - writes the default projection's checkpoint as extents plus a catalog; or
  - defines the checkpoint format so it can become extents without a format break, and records the gap.

  The prompt addendum carries the requirements.
- Continuous queries (after 1.0) are specified against T1, T2, T5, T6 and N4 to N7. Its gate includes:
  - incremental equals from-scratch at every position;
  - as-of reads of results equal results evaluated at that position;
  - `NOW()` is refused in declarations.
- `docs/why.md` gains a short "Varve and Azure Data Explorer" section: where Varve fits next to an Eventhouse, the CloudEvents feed into it, and the IRIs that join telemetry back to Varve's state.
- The operator guide's Azure section shows the pairing: Varve to Event Hubs to Eventhouse, and a scheduled task outside Varve that issues retention acts (N3).
- N3 is checked against the compaction ADR before this ADR is accepted.
- Proposals that reintroduce elapsed-time decisions, a second query language, or derived data in the log cite this ADR and supersede the specific point. They do not diverge silently.

**Must change with this ADR:**
- Roadmap: add the "After 1.0" section with Continuous queries. In the Object storage text, "`derived/` on ephemeral disk, checkpoints mirrored" becomes extent-based projections with a local cache (T7).
- The Object storage session prompt: the addendum.
- `docs/why.md`: the ADX comparison.
- Operator guide: the Eventhouse pairing and retention acts.
- Handover document: the roadmap table.
- #15 is **not** amended. Continuous queries is after 1.0.

## Checks

- **Checked against the accepted ADRs** (0001–0133). N3 was to be checked
  against milestone 6's compaction ADR. **There is no such ADR**: milestone 6
  wrote none. The rulings on the horizon and on checkpoints are ADR 0015 (no
  destructive compaction; the archive horizon fails loudly) and ADR 0078
  (checkpoints by commit count or log bytes only, "checkpoints on a timer"
  rejected); ADR 0114 bounds as-of distance in commits, not time. None allows
  a time-driven trigger, so N3 supersedes nothing; an archive or horizon ADR,
  when written, cites this one. T7 is decided for this milestone by ADRs 0138
  and 0139, which supersede ADR 0015 in part.
- **Layer ownership.** Records boundaries; owns no code.
- **Analyzer rule.** None.
- **Open questions owned.** None.
