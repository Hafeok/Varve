# 0093 — Datasets are the routing unit; the name is the host's

## Status

**Proposed — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Acceptance is the maintainer's act on the pull request.

## Context

A Varve dataset is a directory with one log, one sequencer, one lease (ADRs
0011, 0075) and an identity of its own, `DatasetId`, written into its manifest
(ADR 0072). A server will host several datasets, as a database server hosts
several databases. A request has to say which one it is for. The SPARQL
Protocol has no notion of this: an endpoint *is* a dataset.

## Decision

1. **One server, many datasets, each under its own prefix**:

   | Path | What |
   |---|---|
   | `/datasets/{name}/` | the service description |
   | `/datasets/{name}/sparql` | query and update; a `GET` with neither parameter is the service description |
   | `/datasets/{name}/graphs` and `/datasets/{name}/graphs/{**path}` | the Graph Store, indirect and direct identification |
   | `/datasets/{name}/feed` | the change feed (ADR 0097) |
   | `/datasets/{name}/diff` | the diff (ADR 0097) |
   | `/datasets/{name}/status` | the dataset's status, the one `admin` endpoint of 7a (ADR 0101) |

   Every endpoint of a dataset answers for that dataset alone.
2. **`{name}` is a `DatasetName`**: 1 to 63 characters from `[A-Za-z0-9._-]`,
   starting with a letter or digit, compared ordinally. It is never `.` or `..`.
   A request whose name is not a valid name, or names no configured dataset,
   is `404` with the `dataset-not-found` problem. The answer is the same for
   both, so an unauthorised caller cannot probe which names exist.
3. **The name is the host's; `DatasetId` is the store's.** The server maps a
   name to a directory `{DatasetsRoot}/{name}`, or to a memory store when
   configured so. The mapping lives in the host's configuration and nowhere in
   the dataset. Renaming a dataset is renaming its directory. Its `DatasetId`,
   its log and every client's positions are unchanged.
4. **Datasets are created through configuration**, and later through the admin
   API (milestone 7b), **never by a request to a protocol endpoint**. No query,
   update or Graph Store write creates a dataset.
5. **`Varve.Protocol` asks an `IDatasetResolver`** for the dataset a request
   names (ADR 0091). A host that serves one dataset, such as the conformance
   host, mounts the endpoint groups without `{name}`, and its resolver answers
   for that one dataset.
6. **Direct graph identification** (GSP §4.1) takes the request's own address:
   scheme, host and path, without the query string. The IRI of the graph
   `PUT` to `/datasets/a/graphs/people/1` is
   `https://host/datasets/a/graphs/people/1`. Behind a reverse proxy the host
   honours the forwarded headers it is configured to trust (ADR 0101), and no
   others.

## Alternatives considered

- **One dataset per server process.** The simplest host, and every deployment
  of more than one dataset would need a router in front. Rejected: the lease
  already guarantees one process per dataset, and nothing prevents one process
  per several.
- **The `DatasetId` in the path.** Stable across renames, unreadable, and it
  would make a dataset's identity an addressing concern. The name is for
  people; the id is for the log.
- **Datasets created on first write.** Convenient, and a typo becomes a new
  dataset. Rejected, as databases reject it.

## Consequences

- Authorisation is per dataset by construction: the route's `{name}` is the
  resource every policy decides on (ADRs 0091, 0037).
- Moving a dataset between servers is a copy of its directory and a line of
  configuration. Positions in clients' hands stay valid.

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0072**: `DatasetId` stays the store's;
  - **0075**: one process per directory, which several datasets per process
    respects;
  - **0037**: per-dataset permissions.

  No conflict.
- **Layer ownership.** The mapping is `Varve.Server`, layer 6. The resolver
  contract is `Varve.Protocol`, layer 5.
- **Analyzer rule.** None.
- **Open questions owned.** None.
