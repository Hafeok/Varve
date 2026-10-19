---
set: the-management-plane
namespace: varve
adr: 0163
decisions:
  - key: TwoPlanes
    statement: "The data plane is SPARQL, the Graph Store, commits, events and the service description under /datasets/{name}/; the management plane is datasets, placement, settings, grants, bindings, the standby opt-in, state, status and checkpoints under /manage/"
  - key: TokenAudiencePerPlane
    statement: "Each plane accepts only its own audience, the management plane Varve:Auth:Management:Audience by default api://varve-manage, and a token naming both planes' audiences is refused by both"
  - key: SystemDatasetPerFleet
    statement: "Management state is one system dataset per fleet under the store prefix _fleet/, an ordinary dataset with no data-plane address and a name no user dataset can take"
  - key: WriterByConfiguration
    statement: "Varve:Fleet:Writer, true, false or an instance name, decides which instance claims the system dataset and the datasets placed on it; every other instance follows and never claims; a fleet of one is its own writer"
  - key: GrantsAtAppliedPosition
    statement: "A server authorises data-plane requests against the system dataset at the position it has applied, states it as Varve-Management-Position on every data-plane response, and answers Varve-Min-Management-Position above it with one advance and then 503 management-behind"
  - key: DeclarationsAreTrig
    statement: "Declarations are TriG in the varve: vocabulary, one graph per dataset and one fleet graph, validated by code in this milestone"
  - key: ApplyConvergesDesiredState
    statement: "varve apply canonicalises the declaration files with RDFC-1.0, diffs them against the graphs they own at the current position, and commits the replacement of each owned graph as one commit under Varve-If-Graphs-Match at the plan's versions; a stale saved plan is refused"
  - key: ImperativeManagementSameCommits
    statement: "PUT and PATCH on management resources take TriG under If-Match and write the same graphs as apply, so their drift shows in the next plan"
  - key: BootstrapOperatorsRecorded
    statement: "Host configuration holds only the bootstrap operators; the first writer of an empty system dataset records them, a server whose set differs is not ready, and a change is a system commit by a writer started with --record-bootstrap"
  - key: DegradedModeKeepsData
    statement: "Without a system writer management writes are unavailable and the data plane keeps serving under the grants at the last applied position"
  - key: EmbeddedHasNoManagementPlane
    statement: "Embedded hosts have no management plane; they set settings and scopes in-process through the store and protocol APIs, and DatasetSetup is a client of a server"
  - key: SettingsConvergedByTheWriter
    statement: "Settings stay a commit kind of the data dataset; its writer commits a Settings commit carrying the management position when the declarations it has applied differ from the dataset's settings"
  - key: EventTermsRefusedUntilPart2
    statement: "Until ADR 0167, event schemas stay in the dataset's catalogue graphs and the code validator refuses event terms in a management graph"
  - key: PlacementByEvents
    statement: "varve:placedOn names the instance that writes a dataset, by default the system writer's; a change is made by the old writer abdicating with the reason placement when it applies the change, and the newly placed instance claiming after it observes the release"
  - key: ReadinessReportsManagement
    statement: "Readiness reports the system role, epoch, applied position, lag and bootstrap state, and each dataset's role and lag; superseded, a bootstrap mismatch or a lag past Varve:Fleet:AllowedLag is not ready"
  - key: ClaimBasedDatasetPermissions
    statement: "Claims map to read, write and admin permissions per dataset through the grants declared in the system dataset, and Varve keeps no user store"
  - key: DatasetsCreatedByConfiguration
    statement: "Datasets are declared in the system dataset and created by the instance they are placed on, never by host configuration or a request to a protocol endpoint"
  - key: DatasetRoutesAfterAlignment
    statement: "The dataset routes are the service description, sparql, graphs, commits and commits/{position}, diff and events; state, settings, status and checkpoints are under /manage/datasets/{name}/"
  - key: PermissionsAreCumulative
    statement: "A write grant grants read and an admin grant grants all three on its dataset; a grant names a dataset in its own graph"
  - key: StatusIsTheAdminEndpoint
    statement: "GET /manage/datasets/{name}/status reports the dataset's id, head, durability, settings, checkpoints, projection, role and failure"
  - key: AdminEndpoints
    statement: "The management API is GET /manage/datasets, GET, PUT and DELETE /manage/datasets/{name}, and its state, settings, status, checkpoints and grants resources, with /manage/fleet, /manage/graphs, /manage/apply, /manage/sparql and /manage/system"
  - key: ServerAdminGrant
    statement: "An operator is a bootstrap operator or a principal with an operator grant in the fleet graph, administers every dataset and the fleet; a dataset admin administers one dataset's resources"
  - key: DatasetsDiscoveredUnderRoot
    statement: "At start an instance opens every dataset declared in the system dataset, writing those placed on it and following the rest; a dataset is never discovered from a directory or a prefix"
  - key: GrantsAreScopedByGraphSet
    statement: "A grant is (dataset, permission, graph set), the set all, explicit IRIs with varve:defaultGraph, IRI prefixes or templates over the caller; it is declared in the system dataset, not in host configuration"
  - key: ReadinessIsEveryDatasetOpenAndWithinLag
    statement: "GET /health/ready is 200 when every declared dataset is open as writer or follower, none is failed, each is within Varve:Health:ReadyLag, and the management state is ready; otherwise 503 with a not-ready problem listing what fails"
  - key: WithDatasetDeclaresTheDataset
    statement: "Varve.Aspire's WithDataset generates the dataset's declarations and applies them with varve apply from the server's image before any varve setup apply"
  - key: StateIsAResource
    statement: "GET and PUT /manage/datasets/{name}/state read and set open or closed idempotently, and PUT with a takeover naming an epoch is the handover ADR's request"
  - key: SettingsAreAResource
    statement: "GET /manage/datasets/{name}/settings answers the declared settings with the graph's version as ETag; PUT replaces and PATCH merges as TriG under If-Match"
  - key: PutDatasetIsIdempotent
    statement: "PUT /manage/datasets/{name} with the same declarations is 204 and replaces the dataset's graph otherwise, under If-Match when given"
  - key: SetupIsDeclared
    statement: "DatasetSetup declares a dataset's name, ordered baseline sources and ordered named migrations; storage and settings are management declarations"
  - key: ApplyOrder
    statement: "ApplyAsync requires the dataset to exist, adds each baseline by the Graph Store Protocol's POST, then applies each unrecorded migration; a missing dataset is a SetupException naming varve apply"
  - key: SettingsAreDesiredState
    statement: "A dataset's settings are desired state in its management graph, converged by the dataset's writer; a setup never changes them"
---

The rulings of [ADR 0163](../adr/0163-the-management-plane.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Management plane of #105
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.

Moved here, superseded with their keys: `ClaimBasedDatasetPermissions` (0037);
`DatasetsCreatedByConfiguration` and `DatasetRoutesAfterAlignment` (0093); `PermissionsAreCumulative`
and `StatusIsTheAdminEndpoint` (0101); `AdminEndpoints`, `ServerAdminGrant` and
`DatasetsDiscoveredUnderRoot` (0106); `GrantsAreScopedByGraphSet` (0107);
`ReadinessIsEveryDatasetOpenAndWithinLag` (0113); `WithDatasetDeclaresTheDataset` (0117);
`StateIsAResource`, `SettingsAreAResource` and `PutDatasetIsIdempotent` (0118); `SetupIsDeclared`,
`ApplyOrder` and `SettingsAreDesiredState` (0130).
