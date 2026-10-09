# 0106 — The admin API: datasets created, listed, opened, closed and deleted; settings; checkpoints; projection status

## Status

**Accepted — filed unaccepted by milestone 7b of #11, 2026-10-08** (ADR 0066).
Decided by the maintainer on the 7b plan: "as proposed. `DELETE` on a closed
dataset removes the directory, stated in the ADR. A directory under the root
that fails to open is listed with `failed` and a reason, not skipped".
**Refines ADR [0093](0093-datasets-are-the-routing-unit.md)** point 4, which
reserved creation "through the admin API (milestone 7b)", and extends ADR
[0101](0101-the-server-configuration-aot-shutdown-readiness.md)'s `/status`.
Acceptance is the maintainer's act on the pull request.

## Context

7a's one `admin` endpoint reports a dataset's status. ADR 0037 gave `admin`
its scope — "settings commits, erasure, checkpoint and projection management"
— and ADR 0093 reserved dataset creation to configuration and the admin API,
never to a protocol request. An operator today edits the configuration and
restarts the server to add a dataset, and has no way to commit a `Settings`
change (spec §9), take a checkpoint, or read the projection's position.

Permissions are per dataset (ADRs 0037, 0091): a policy is decided on a
`DatasetName`. Creating a dataset is a decision about a name that has no
grants yet, and listing is a decision about several.

## Decision

1. **The endpoints**, mounted beside 7a's under the host's prefix:

   | Path | Method | Grant | What |
   |---|---|---|---|
   | `/datasets` | `GET` | any dataset `admin` | the datasets the caller administers: name, id, head, state (`open`, `closed`, `failed` with a reason) |
   | `/datasets/{name}` | `PUT` | server admin | creates `{name}` as a directory under `DatasetsRoot` (body `{ "storage": "File" }`, the default, or `"Memory"`), opens it, `201`; `409 dataset-exists` for a name in use |
   | `/datasets/{name}` | `DELETE` | server admin | **deletes a closed dataset's directory**, `204`; `409 dataset-open` while it is open; `404` for a name that is neither |
   | `/datasets/{name}/close` | `POST` | server admin | drains the sequencer, disposes the dataset, releases the lease (ADR 0101's shutdown for one dataset); the name answers `404` after, until reopened or restarted; `204` |
   | `/datasets/{name}/open` | `POST` | server admin | opens the directory of that name under the root, `204`; `404` when there is none |
   | `/datasets/{name}/settings` | `POST` | dataset `admin` | a `Settings` commit (spec §9): `{ "defaultAccessScope": "AllHistory" }`, `If-Match` as a write's expected position (ADR 0094), `204` with `Varve-Position` |
   | `/datasets/{name}/checkpoints` | `POST` | dataset `admin` | a checkpoint at the head, or `?at=<position>`, `201` with `Varve-Position` the checkpointed position |
   | `/datasets/{name}/status` | `GET` | dataset `admin` | 7a's body, plus `projection: { position, lag, failed }` and `state` |

   Every error is an RFC 9457 problem (ADR 0092).
2. **A server admin** is a caller with a claim value in
   `Varve:Auth:Server:Admin`. A server admin administers every dataset, so
   the grant is cumulative above dataset `admin`. The create, delete, open
   and close calls take it because they concern a name that may have no
   grants, and because they touch the file system under the root.
   `GET /datasets` needs any `admin` grant and filters: a dataset admin sees
   the datasets they administer, a server admin all of them, and the answer
   is `200` with an empty list rather than `403` for a caller with none, so
   that the list never says which names exist to someone who may not know.
3. **Datasets are discovered under the root.** At start the server opens
   every configured dataset, and then every directory directly under
   `DatasetsRoot` that holds a dataset (`log/`) and is not configured, so a
   dataset created by `PUT` survives a restart without a configuration edit,
   and a directory an operator copies into the root is served after one.
   **A directory that fails to open is listed with `failed` and the reason**
   — a lease another process holds (ADR 0075), a manifest that does not
   parse, a refused key-store path — and is not skipped: readiness (ADR
   0101) reports it, and `GET /datasets` shows it. The configuration's
   "at least one dataset" check becomes "at least one dataset, or a root".
4. **Close is the step before delete.** `DELETE` of an open dataset is
   refused, so that no request in flight loses its dataset under it and no
   lease is broken; `close` is the explicit, auditable step, and `open` its
   inverse, so an operator can attach, detach and reattach without a
   restart. A closed dataset's directory is still there, and a restart
   reopens it; **`DELETE` removes the directory**, log and derived data, and
   is not undoable, which the operator guide says in those words.
5. **Settings commits and checkpoints go through the store's own calls** —
   `Dataset.ChangeSettingsAsync` and `Dataset.CheckpointAsync` — with the
   caller as agent and the request id as cause (ADR 0094). A `Settings`
   commit is a commit: it has a position, it reaches the feed (ADR 0046), and
   `If-Match` applies. The store refuses a settings commit with no agent
   (spec §9: every settings change has an agent and a position), and in
   anonymous mode no caller is named (ADR 0094), so there the endpoint
   answers `403` with the problem `agent-required` rather than inventing a
   name. A checkpoint of an empty dataset is `400`.
6. **Projection status** is the default projection's position, the head
   minus it as `lag`, and the failed state (spec §7). Readiness already
   requires lag zero at start; the status shows it after.

## Alternatives considered

- **Create a dataset with its grants in the body.** Grants are the host's
  configuration (ADRs 0037, 0091, 0107), and a grant written through the API
  would be the second place a permission lives. A server admin creates; the
  configuration grants; a restart is not needed for the first and is for
  the second.
- **A registry file of admin-created datasets** instead of discovery. One
  more file to back up and to drift from the directories; the directory is
  the registry (ADR 0093: renaming a dataset is renaming its directory).
- **`DELETE` that closes first.** Convenient and dangerous: one call from
  "serving" to "gone". Two calls, the first harmless.
- **Separate `GET /datasets/{name}/projections`.** The status already
  exists for `admin`, and the projection is one more object in it.

## Consequences

- `Varve.Protocol` gains the endpoints and two seams: the dataset map's
  write side, `IDatasetAdministration` (list, create, open, close, delete;
  an entry carries its name, state, storage, origin — configured, discovered
  or created — reason, id and head), implemented by the host's
  `OpenDatasets` and optional in `ProtocolOptions`, and the server-admin
  decision, a fourth policy name `varve:server-admin` the host registers,
  decided on no dataset. `MapVarveAdministration` mounts the server-level
  routes beside the dataset groups; `Dataset.ProjectionPosition` is the one
  store addition.
- `Varve.Server`'s configuration gains `Auth:Server:Admin`.
- The CLI's `info`, `checkpoint` and `feed` in remote mode call these (ADR
  0105).
- `GET /ready` reports a discovered directory that failed to open, as it
  reports a configured one.

## Checks

- **Checked against the accepted ADRs** (0001–0101) and specification 1.6.
  Touches:
  - **0037**: `admin`'s scope, now built; a server-level grant added;
  - **0046** and spec §9: settings commits reach subscribers;
  - **0075**: the lease, honoured by close and open;
  - **0091**: a fourth policy name;
  - **0093**: point 4, refined as it anticipated;
  - **0101**: `/status` extended, readiness extended.

  No conflict.
- **Layer ownership.** The endpoints are `Varve.Protocol` (5); the file
  system under the root and the grant are `Varve.Server` (6).
- **Analyzer rule.** None.
- **Open questions owned.** None.
