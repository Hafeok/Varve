# Why Varve

Most software is a system of record. The data is the business. Its history
matters, other systems depend on it, and different people may see different
parts of it. Built on a relational database, each of those needs becomes a
project of its own: an audit table and its triggers, temporal tables, an
outbox and its relay, change data capture, a schema registry, row-level
security policies, migration tooling, read replicas. Each is built once per
system, usually after the incident that showed it was needed, and each is a
copy of the truth that can drift from it.

Varve starts from the opposite end. The log of commits is the source of
truth, and every index, read model and integration stream is computed from
it. The capabilities above are then properties of the storage model, tested
by the same gates as the core, and they cannot drift, because each is a view
of the same log.

## What you get, and what it replaces

Status: **shipped** is in the current preview; a milestone name means it is
planned on the roadmap and not yet released.

| You need | In a typical SQL stack | In Varve | Status |
|---|---|---|---|
| Who changed what, and when | Audit tables, triggers, an `updated_by` column kept honest by convention | Every commit records its agent (the user, through on-behalf-of) and its cause; the commits list is the audit trail | shipped; on-behalf-of: Client and integration |
| What it looked like on a given day | System-versioned temporal tables, or a history schema | Any query, as of any position or time, with the same SPARQL | shipped |
| What changed between two points | Hand-written diff queries over history tables | `diff` between two positions | shipped |
| Consistent reads under concurrent writes | Isolation levels, chosen and tuned | Every read is pinned to one position; snapshot isolation is the model, not a setting | shipped |
| Notify other systems of changes | An outbox table, a relay process, a broker, idempotency on the consumer | The commits resource is the stream: ordered, resumable by position, replayable from the beginning | shipped; `FollowAsync`: Client and integration |
| Domain events for systems that do not speak RDF | Event DTOs, a serialiser, a separate schema registry | Events written in the same commit as the data, rendered as CloudEvents JSON, with versioned JSON Schemas, a catalogue and AsyncAPI | Client and integration |
| Change data capture | Debezium or the database's replication slot, plus its operations | The same commits resource; nothing extra to run | shipped |
| Per-tenant and per-user visibility | Row-level security policies, tested by hand | Datasets for tenants, graph-level grants from your identity provider's claims | shipped; row-level access: milestone 9 |
| Add a field | A migration, a deploy window, a backfill | Write the new property; older records simply do not have it | shipped |
| Enforce the shape of data | Constraints and triggers, plus validation in application code | Shapes checked before each commit, with the report kept | shipped |
| Categories, hierarchies and derived facts | Recursive CTEs, closure tables, denormalised flags kept in sync by triggers | The ontology is data; RDFS by query rewriting, OWL 2 RL and your own rules as a projection, with "why is this here" as a query and as-of reads of what was inferred | Inference |
| Repeatable setup | Migration framework and its version table | Declarative setup that is idempotent by construction; migrations recorded as data | Client and integration |
| Optimistic concurrency | Row-version columns, kept by every writer | `ETag` and `If-Match` on every resource, per graph | per dataset: shipped; per graph: Commit path |
| Read replicas and a region away | Replication configuration, failover runbooks | Followers of the same log; the store's geo-replication; log shipping; promotion rehearsed | Replication |
| Backup | Snapshots that capture state, not history | Copy the log; any copy is a full history and opens on its own | shipped |
| Delete personal data in an append-only history | Hand-rolled scrubbing, or giving up on history | Crypto-shredding: destroy the subject's key, on every replica at once | milestone 9 (preview) |
| Know it is correct | Your test suite | The W3C suites, a reference model, crash injection at every byte, byte-identical logs | shipped |

## Where it is the wrong tool

Saying where Varve stops is part of why the list above can be trusted.

- **Very high write throughput into one consistency boundary.** Each dataset
  has one writer. Partition by dataset, or use a store built for ingestion.
- **Analytical scans over billions of rows.** That is columnar territory.
  Feed it from Varve's commit stream; do not ask Varve to be it.
- **Large binary objects.** Keep the bytes in object storage and their
  metadata in Varve.
- **Microsecond key-value caching.** Use a cache.

## Varve and Azure Data Explorer

Azure Data Explorer — Eventhouse in Fabric — is the well-known system nearest
to parts of Varve: immutable extents in blob storage with local disk as a
cache, materialised views over a delta, continuous export. Varve takes what
fits a log of linked state and refuses what does not (ADR 0140):

- **Taken:** checkpoints as immutable extents in the object store, a catalog,
  a bounded local cache and lazy attach at a cold start (ADRs 0138, 0139);
  the commit stream as a continuous export with a cursor.
- **Refused:** a columnar telemetry store and a second query language;
  sealing, retention or caching driven by elapsed time; derived results
  written back into the source of truth.

**They pair rather than compete.** High-rate telemetry — a building's
sensor readings — belongs in an Eventhouse, which ingests it on many nodes
and scans it with KQL. The building itself — its sites, rooms, assets and
points, renovated and leased over years — is linked state with a history, and
belongs in Varve. Varve's commit stream, followed with a checkpoint, feeds
Event Hubs and so the Eventhouse; each reading carries the IRI of its point,
which joins it back to Varve's state at the time of the reading. The operator guide
shows the pairing ([Azure Container Apps](operator/azure-container-apps.md#pairing-with-an-eventhouse)).

## What it asks of you

You will model data as linked resources rather than tables, and you will
query with SPARQL rather than SQL. Neither is hard. The habits that make a
good relational design (surrogate keys, join tables, `NULL` as unknown,
strings as enumerations) are the ones to unlearn. The learning track
(`docs/learn/`) starts from SQL and gets you there with runnable examples.

## Further reading

- The storage thesis and constraints: the README.
- What is shipped and what is next: `docs/roadmap.md`.
- Running it: `docs/operator/`.
- Learning it: `docs/learn/` (Learning track milestone).
