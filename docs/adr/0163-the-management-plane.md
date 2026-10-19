# 0163 — The management plane: a fleet system dataset, declarations as code, `varve apply`

## Status

**Accepted — filed unaccepted by milestone Management plane of #105,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer:
- on 2026-10-13: separate data and management planes; principal mapping as
  code; TriG as the declaration format; one system dataset per fleet from the
  start;
- on 2026-10-19: write routing by forwarding (point 11, in ADR 0168), and the
  approved plan of #105 with its rulings:
  - the milestone in two releases, the event-schema move in the second (ADR
    0167);
  - `checkpoints` and `status` move to `/manage/`;
  - placement in this milestone, changed by events;
  - `Varve:Datasets` and `Varve:Auth:Datasets` removed;
  - a bootstrap change as an explicit system commit.

**Supersedes:**
- in ADR [0037](0037-server-authentication.md): `ClaimBasedDatasetPermissions`;
- in ADR [0093](0093-datasets-are-the-routing-unit.md): `DatasetsCreatedByConfiguration`
  and `DatasetRoutesAfterAlignment`;
- in ADR [0101](0101-the-server-configuration-aot-shutdown-readiness.md):
  `PermissionsAreCumulative` and `StatusIsTheAdminEndpoint`;
- in ADR [0106](0106-the-admin-api.md): `AdminEndpoints`, `ServerAdminGrant`
  and `DatasetsDiscoveredUnderRoot`;
- in ADR [0107](0107-graph-level-authorisation.md): `GrantsAreScopedByGraphSet`;
- in ADR [0113](0113-health-and-readiness.md): `ReadinessIsEveryDatasetOpenAndWithinLag`;
- in ADR [0117](0117-varve-aspire.md): `WithDatasetDeclaresTheDataset`;
- in ADR [0118](0118-api-alignment-while-everything-is-preview.md):
  `StateIsAResource`, `SettingsAreAResource` and `PutDatasetIsIdempotent`;
- in ADR [0130](0130-client-side-setup-and-system-graphs.md): `SetupIsDeclared`,
  `ApplyOrder` and `SettingsAreDesiredState`.

Each superseded ADR carries a dated block naming this one (ADR 0068).

**Related:**
- the follower: ADR [0164](0164-the-tier-1-follower.md);
- the position on the commit: ADR [0165](0165-the-management-position-on-the-commit.md);
- the package and the vocabulary: ADR [0166](0166-varve-management-and-the-vocabulary.md);
- forwarding: ADR [0168](0168-write-routing-by-forwarding.md).

ADR 0167, the event-schema move, is part 2's.

## Context

Configuration and data share one API today:
- The dataset state and settings resources sit under `/datasets/{name}`, next
  to SPARQL and the Graph Store.
- Grants live in host configuration (ADR 0107). They are not versioned, not
  auditable, and cannot be declared and reviewed in a repository.

A team that runs Varve as its data store needs three things from its
configuration:
- It is declared in the application's repository and applied by CI.
- Drift is detectable.
- The question "who could read this graph on that date" has an answer.

Varve already has the machinery for all three in the log: positions, as-of
reads and conditional commits.

Several servers can form a fleet that shares one object store, with
active-active by dataset. Grants must therefore mean the same on every server.
Grants local to each server cannot guarantee that.

## Decision

**1. Two planes.**
- **The data plane:** SPARQL Query and Update, the Graph Store, commits,
  events and the service description, under `/datasets/{name}/`.
- **The management plane:** under `/manage/`. It covers:
  - datasets and their placement;
  - settings;
  - grants;
  - shapes and TBox bindings;
  - the standby opt-in;
  - dataset state, status and checkpoints;
  - the takeover request of the writer handover ADR.

  Event schemas join in part 2 (ADR 0167). The routes:
  - `GET /manage/datasets`;
  - `GET`, `PUT` and `DELETE /manage/datasets/{name}`, where `PUT` takes the
    dataset's declaration graph as TriG;
  - `…/state`, `…/settings`, `…/status`, `…/checkpoints` and `…/grants`;
  - `/manage/fleet`, `/manage/graphs`, `/manage/apply`, `/manage/sparql`,
    `/manage/system`.

  The old data-plane paths are removed, not aliased (ADR 0118's rule).
- **A token for each plane.**
  - A management route requires a token whose audience is
    `Varve:Auth:Management:Audience`, default `api://varve-manage`.
  - It also requires the **operator** role, or the **dataset-admin** role (an
    `admin` grant on that dataset) for that dataset's resources.
  - The data plane's audiences are `Varve:Auth:Audiences`, as before.
  - Each plane runs its own JwtBearer scheme and accepts only its own
    audience. A token whose `aud` names both planes' audiences is refused by
    both.
- **A second listener** is optional (`Varve:Manage:Urls`), so that network
  policy can isolate management. The default is one listener.

**2. Management state is a system dataset per fleet.**
- **The fleet** is the set of servers sharing one system dataset, on the
  fleet's store. A single server with the file backend is a fleet of one.
- **The system dataset is an ordinary Varve dataset internally:** one log,
  positions, as-of reads and the header chain.
  - It has no data-plane address.
  - It lives under the store prefix `_fleet/`, or `{DatasetsRoot}/_fleet/` on
    the file backend.
  - A dataset name must start with a letter or a digit (ADR 0093), so no user
    dataset can take it.
- **It has one writer at a time,** like every dataset.
  - `Varve:Fleet:Writer` decides who: `true`, `false`, or an instance name.
    An instance name is compared with the instance's own name
    (`Varve:ObjectStorage:Instance`, by default the host name), so that the
    pods of one StatefulSet share one configuration.
  - The writing instance claims the system dataset (ADR 0136).
  - Every other instance follows it as a tier-1 follower (ADR 0164) and never
    claims.
  - A fleet of one is its own writer whatever the key says.
- **A management write to a non-writer is forwarded** to the writer (ADR 0168).

**3. Grants are evaluated from the applied position.**
- A server authorises data-plane requests against the system dataset at the
  position its follower (or its writer) has applied.
- **Every data-plane response carries `Varve-Management-Position`.**
- **`Varve-Min-Management-Position: p`** asks for at least position p.
  - A server below p advances its follower once.
  - It answers `503 management-behind` only if it is still behind. The
    problem has members `position` and `appliedPosition`.
- **Readiness reports the lag** behind the system tail (point 12).
- **A commit records the management position** it was authorised under (ADR
  0165). The same position will decide, from part 2 on, which event-schema
  declarations render the commit's events.

**4. Declarations are TriG in the `varve:` vocabulary** (ADR 0166).
- Each dataset's declarations are one named graph of the system dataset,
  `varve:manage/dataset/{enc(name)}`.
- Fleet-wide declarations are `varve:manage/fleet`.
- Grants name principals as agent IRIs (`<issuer>#<sub>`), or as roles and
  groups from token claims.
- Graph sets are explicit IRIs, IRI prefixes, or an IRI template over the
  caller, which serves on-behalf-of users who own their graphs.
- The vocabulary reserves room for #14's classifier and key-store bindings.
- **Validation:**
  - code validates declarations in this milestone;
  - shapes ship alongside, with a property that shapes and code agree, once
    `Varve.Shacl` is on `main`;
  - switching to the shapes is SHACL part 2's decision.

**5. `apply` converges desired state.**
- **The plan.** `varve apply --plan <files…>` reads the declaration files,
  canonicalises them with RDFC-1.0, diffs them against the graphs they own at
  the current system position, and prints the diff with that position and
  each owned graph's version.
  - A file owns every graph it names, whole.
  - `--out` saves the plan.
  - `--exit-code` exits 2 on a non-empty diff, which is the drift check CI
    runs.
- **The commit.** `varve apply` commits the replacement of each owned graph as
  one commit, `POST /manage/apply`, under `Varve-If-Graphs-Match` naming the
  owned graphs at the plan's versions.
  - Applies to disjoint graphs never conflict.
  - Applies to the same graph conflict, and one is refused.
  - A saved plan whose versions are stale is refused (`412`).
- **Authentication.** CI authenticates by workload identity federation: the
  CLI exchanges a client assertion (a federated token file, or GitHub
  Actions' OIDC token) for an access token. There are no keys.
- **`varve setup apply`** (ADR 0130) keeps its name and its data-shaped
  meaning. The two commands never share one.

**6. Imperative management is the same commits.**
- `PUT` and `PATCH` on management resources take TriG under `If-Match`, the
  resource graph's version.
- They write the same graphs, so drift from them shows in the next plan.

**7. Bootstrap and break-glass.**
- **Host configuration holds only the bootstrap operators,**
  `Varve:Manage:BootstrapOperators`: agent IRIs or role values. They are
  always operators.
- **The first writer of an empty system dataset records the set** in
  `varve:manage/bootstrap`.
- **A mismatch fails readiness.** A server whose configured set differs from
  the recorded one reports `bootstrap: mismatch` and is not ready, so the
  fleet cannot silently disagree about who reaches management.
- **Changing the set is an explicit system commit.** The writer, started once
  with `--record-bootstrap`, records its configured set.

**8. Degraded mode.**
- **Without a system writer, management writes are unavailable.** A forwarded
  one is `503 unavailable` with reason `writer-unreachable` (ADR 0168).
- **Recovery:** the orchestrator's restart now, and handover once Replication
  lands.
- **The data plane keeps serving,** under the grants at the last applied
  position. Management being unavailable never stops data.

**9. Embedded hosts have no management plane.** They set settings and
scopes in-process, through the store's and the protocol's APIs. `DatasetSetup`
is a client of a server.

**10. `DatasetSetup` keeps data-shaped work.**
- **What stays in the dataset:** seed data, data migrations, and the setup
  graph that records them, under `varve setup apply`.
- **What leaves `DatasetSetup` and its file:** a dataset's existence and
  storage, and its settings. They are management declarations now.
- **How settings converge.** Settings stay a commit kind of the data dataset
  (ADR 0021), because the store needs them in its own log. The dataset's
  writer converges them: when the declarations at its applied management
  position differ from the dataset's settings, it commits a `Settings` commit
  carrying that position (ADR 0165).
- **Event schemas stay in the catalogue graphs until part 2** (ADR 0167). In
  this release, the code validator refuses event terms in a management graph,
  with a message that names part 2.

**11. Write routing by forwarding.** Any instance accepts a write and forwards
it to the writer of its dataset (ADR 0168).

**12. Placement, by events.**
- **Who writes a dataset.** `varve:placedOn "<instance>"` names it, by default
  the system writer's instance. An instance claims the datasets placed on it,
  if its `Writer` key lets it claim at all.
- **Every other instance follows the dataset** and forwards its writes.
- **A placement change works by events, never by time.**
  - When the old writer's applied management position shows the change, it
    abdicates: it releases the epoch with the reason `placement`.
  - The newly placed instance claims after it observes that release.
  - A write in flight across the change is answered, or refused with `503`,
    by the old writer. A forward made after the release reaches the new
    writer.

**13. Readiness.**
- `/health/ready` gains a `management` object: role (`writer`, `follower` or
  `superseded`), epoch, applied position, lag and bootstrap state.
- Each dataset entry gains its role and lag.
- An instance is not ready when:
  - its system role is `superseded`;
  - its bootstrap is `mismatch`;
  - its management lag exceeds `Varve:Fleet:AllowedLag`;
  - a dataset fails ADR 0113's rule.

## Alternatives considered

- **`307` to the writer's advertised URL.** Superseded by point 11, as ADR
  0168 says.
- **Grants in host configuration, as today.** They are unversioned and
  unauditable, and they can differ between servers in a fleet.
- **Grants in a graph of each data dataset.** Anyone who reads that graph
  reads the grants, and the grants travel with the data into copies.
- **A system dataset per server, with a fleet later.** Refused by the
  maintainer. Per-fleet consistency would then arrive as a migration of grant
  state.
- **YAML declarations mapped to RDF.** The system dataset stores RDF, so a
  mapping layer is one more thing to keep consistent. Shapes validate TriG
  directly.
- **Synchronous grant propagation, where the writer waits for every server.**
  Fleet membership is not declared, and management writes would wait for the
  slowest server. `Varve-Min-Management-Position` gives the guarantee to the
  client that needs it.
- **Placement changed on restart only.** Simpler, but a placement change would
  need a coordinated restart. Abdication is an event the old writer already
  knows how to make (ADR 0136 point 6).

## Consequences

- **The milestone builds a tier-1 follower before Replication** (ADR 0164).
- **Breaking changes before 1.0:**
  - the admin routes move to `/manage/`;
  - `Varve:Datasets` and `Varve:Auth:Datasets` are refused at start, and
    `varve apply --from-config` prints the TriG that replaces them;
  - `DatasetSetup.Storage` and `DatasetSetup.Settings` are gone, and the setup
    file's `storage` and `settings` keys are refused;
  - `Varve:Auth:Server:Admin` becomes `Varve:Manage:BootstrapOperators`.
- **The gate:**
  - plan and apply are idempotent;
  - a stale saved plan is refused;
  - disjoint applies never conflict, and same-graph applies do;
  - over generated grant histories, no data-plane response at management
    position p exposes a graph the grants at p forbid;
  - the minimum-position header is honoured;
  - as-of grant queries equal the grants in force;
  - each plane refuses the other's tokens;
  - a bootstrap mismatch fails readiness;
  - the data plane keeps serving with the system writer killed;
  - a mistaken second writer shows in readiness and in the epoch history;
  - a two-server fleet on Azurite and on RustFS runs the suite, with writes
    forwarded.
- **What changes with it:**
  - the problem catalogue, the operator guide, the Entra guide and
    `deploy/azure/`;
  - `Varve.Aspire`;
  - the learning track's multi-tenancy chapter;
  - the Web UI's console;
  - Replication's takeover route;
  - #14's bindings.

## Checks

- **Checked against the accepted ADRs** (0001–0147). It supersedes the
  rulings listed under Status, each with a dated block in its ADR. It also
  touches:
  - **0021** (settings stay a commit kind);
  - **0075** and **0136** (the system dataset's writer is claimed like any);
  - **0091** (the `IAccessScopes` seam stays, its implementation moves);
  - **0105** (the CLI gains `apply`);
  - **0115** (new keys; the removed ones refused);
  - **0119** (new problem types);
  - **0125** and **0126** (`Varve-If-Graphs-Match` on the system dataset).

  No other conflict.
- **Layer ownership.**
  - `Varve.Management` (4): the model, the validator, the grants, the plan.
  - `Varve.Protocol` (5): the routes, the headers, the forwarder.
  - `Varve.Server` (6): the fleet, the configuration, the composition.
- **Analyzer rule.** None new.
- **Open questions owned.** None. Event schemas are ADR 0167's (part 2).
