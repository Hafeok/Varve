---
set: the-admin-api
namespace: varve
adr: 0106
decisions:
  - key: AdminEndpoints
    statement: "The admin API is GET /datasets, PUT and DELETE /datasets/{name}, POST /datasets/{name}/open and /close, POST /datasets/{name}/settings, POST /datasets/{name}/checkpoints, and /status extended with the projection and the state"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: ServerAdminGrant
    statement: "A server admin is a caller with a claim value in Varve:Auth:Server:Admin, administers every dataset, and alone creates, deletes, opens and closes datasets; the policy name is varve:server-admin"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: ListShowsAdministeredDatasets
    statement: "GET /datasets answers the datasets the caller administers, 200 with an empty list for a caller with none, so the list never reveals names to a caller who may not know them"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: DatasetsDiscoveredUnderRoot
    statement: "At start the server opens every configured dataset and then every directory directly under DatasetsRoot that holds a dataset; a created dataset survives a restart without a configuration edit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: FailedDirectoryListedNotSkipped
    statement: "A directory under the root that fails to open is listed with state failed and the reason, reported by readiness, and never skipped"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: DeleteOnlyWhenClosed
    statement: "DELETE of an open dataset is 409 dataset-open; close drains, disposes and releases the lease and open is its inverse; DELETE of a closed dataset removes its directory, log and derived data, and is not undoable"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: SettingsCommitOverHttp
    statement: "POST /settings makes a Settings commit through Dataset.ChangeSettingsAsync with the caller as agent, the request id as cause and If-Match as the expected position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: CheckpointOnDemand
    statement: "POST /checkpoints takes a checkpoint at the head or at ?at= through Dataset.CheckpointAsync and answers 201 with the position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: ProjectionStatusInStatus
    statement: "/status reports the default projection's position, its lag behind the head and its failed state, beside 7a's fields and the dataset's state"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
---

The rulings of [ADR 0106](../adr/0106-the-admin-api.md), filed unaccepted by milestone 7b of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
