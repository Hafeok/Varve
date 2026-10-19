# Management plane, part 1 — the fleet system dataset, grants as code, `varve apply`

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#105](https://github.com/Hafeok/Varve/issues/105), milestone Management plane, part 1 of 2 |
| **Date** | 2026-10-19 |
| **Tool** | Claude Code 2.1.303, a cloud session |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01ALSCca2CQe61mXQ49Fc5oR` |
| **Branch** | `claude/hopeful-pascal-mnkbb4`, from `main` at 9d48ccc |
| **ADRs** | 0163–0166 and 0168, filed unaccepted; 0167 is part 2's. 0017, 0037, 0058, 0093, 0094, 0101, 0106, 0107, 0113, 0115, 0117, 0118, 0130, 0131, 0136 and 0142 carry dated amendments of 2026-10-19 |

## The prompts

The maintainer's first message, with the session prompt and two ADR drafts
in a zip. None of the three was copied into the repository; the management
plane draft's content is in ADRs 0163–0166 and 0168:

> @"/root/.claude/uploads/b1a5f169-7453-5730-90e6-7ef44c2b91b8/949a4ff2-varve-management-plane.zip" Unzip the attached varve-management-plane.zip outside the repository. It holds the session prompt varve-claude-code-management-plane.md, the ADR draft varve-adr-management-plane.md (which you file and implement), and the writer handover ADR draft varve-adr-tier1-writer-handover.md (reference only; Replication files it). Read the prompt in full; it's for milestone Management plane (#105) in github.com/Hafeok/Varve. Work from current main. AGENTS.md applies in full. Read issue #105 and the ADRs in the order the prompt gives, then post the plan answering its ten plan questions and wait for approval before writing code. Extent maintenance (#110) runs in parallel; your tier-1 follower implements its reader contract.

The session prompt, `varve-claude-code-management-plane.md`:

> # Session: Management plane — fleet system dataset, grants as code, `varve apply` (#105)
>
> This milestone separates the data plane from the management plane, puts management state in a system dataset per fleet, and makes grants and settings declarable as code with `varve apply`. It is in 1.0 (#15).
>
> **Starting point.** Current `main`, after `v0.1.0-preview.8`. Commit path (#91), Client and integration (#103, #93) and Object storage (#92) have all landed, so nothing this milestone depends on is still open. Two other sessions run in parallel:
> - **SHACL part 1 (#123)** adds `Varve.Shacl` at layer 3, and ships shapes for #93's event declarations (ADR 0157).
> - **Extent maintenance (#110)** adds a reader contract to `Varve.Store.ObjectStorage`. Every instance that reads extents announces the oldest catalog version it holds, so the writer knows what it may delete. Your tier-1 follower is such a reader.
>
> Whichever of you lands second wires the follower into that contract.
>
> **Read before planning, in this order:**
> - issue #105;
> - the attached ADR draft `varve-adr-management-plane.md`, which this session files and implements;
> - the attached writer handover ADR draft `varve-adr-tier1-writer-handover.md`. It belongs to Replication (#97), but its takeover request targets your route, and its points 8–10 constrain your system writer;
> - the ADRs and release descriptors on `main` for Commit path (0124–0126), Client and integration (0127–0133, 0143–0147) and Object storage (0134–0142), above all 0136 (one writer by an epoch and a log-slot fence), 0130 and 0131 (setup), and 0144 and 0145 (the event catalogue and schema registry);
> - the server auth ADR and the graph-level grants ADRs from 7b, including ADR 0107 and its dated amendment for the catalogue graphs;
> - #14 (Erasure and row-level access). Row-level access is in 1.0, and its classifier bindings will be management declarations.
>
> `AGENTS.md` applies in full. Plan first, and wait for approval.
>
> ## Plan questions
>
> 1. **Format v1.** Can commit metadata carry the authorising management position under format v1? Answer yes or no, citing the format spec section. If no, propose the `derived/` audit index and the ADR amendment.
> 2. **Grant evaluation.** Where does grant evaluation move? Name the types and packages, and check them against the layer rules. The filtered quad source stays in `Varve.Rdf`. Evaluating grants at a position must not pull the system dataset into a lower layer.
> 3. **The tier-1 follower.** Give its contract and its package, and say which parts of it Replication (#97) reuses unchanged. Say how it meets #110's reader contract: as implemented if #110 has landed, and as a stated hook if it hasn't.
> 4. **Who writes the system dataset.** ADR 0136 made every writer-mode open claim the next epoch. So the servers in a fleet must not all open the system dataset for write, or each start supersedes the last. Say:
>    - how exactly one server is the system writer: its configuration, how readiness reflects it, and what the others do;
>    - how that interacts with the writer record and its epoch history;
>    - what a mistaken second writer looks like to an operator (the handover draft's point 9: a run of `restart` claims).
> 5. **The management vocabulary.** Propose the terms, classes and properties, for:
>    - datasets, settings and grants;
>    - principals and graph templates;
>    - bindings;
>    - event schemas;
>    - the standby opt-in.
>
>    Leave room for #14's classifier and key-store bindings without defining them. Show one complete example TriG file for a dataset with on-behalf-of user graphs.
> 6. **Event schemas move here** (ADR 0144 point 6, the draft's point 10). Say:
>    - how the move happens per dataset;
>    - what the in-dataset catalogue graphs become after it. My expectation: writes to them are refused, with a problem pointing at `/manage/`, and their history stays readable;
>    - how the renderer finds the declarations in force for a commit on each side of the move. A commit before the move renders from the dataset's catalogue history, as today. A commit after it renders from the system dataset at the management position that commit records (question 1).
>
>    ADR 0145's golden files and #93's byte-identical replay property must hold across the move, and the plan says how that is tested.
> 7. **Route migration.** List every moved route, every test and doc that names it, and the takeover route from the handover draft.
> 8. **Reconciling `DatasetSetup`.** Say what stays in the dataset and what moves. #93 landed setup as desired-state settings plus additive baselines (ADRs 0130, 0131), with the command `varve setup apply`; say what of that moves here. `varve apply` is this milestone's command and must not collide with setup's.
> 9. **Validation.** Declarations are validated by code in this milestone.
>    - If #123 has landed when you reach that slice, also ship SHACL shapes for the management vocabulary, with a property test that the shapes and the code validator agree on generated declarations, valid and invalid. ADR 0157 did the same for events.
>    - Whether the server switches from code to shapes is SHACL part 2's decision.
> 10. **ADR numbering.** Your ADRs start at 0163: SHACL holds 0148–0157 and Extent maintenance 0158–0162. List the numbers you use and any amendments, at least 0107, 0130, 0131 and 0144.
>
> ## Work
>
> - Follow the ADR's ten decision points. If a point turns out to be wrong in code, stop and say so with the reason. Do not diverge silently; propose an amendment.
> - The gate in the ADR's Consequences is the definition of done. The property tests run over generated grant histories and use the as-of machinery that exists today.
> - The fleet tests run two server processes on Azurite and on RustFS.
> - **CI images.** Use only the images in `.github/ci-images.yaml` (#118). If #118's PR 2 has landed, use the mirror references. Never add an image without a row there.
> - **CI authentication.** CI authenticates `apply` with workload identity federation against the test issuer. There are no keys anywhere.
> - **Request bodies.** Management `PUT` and `PATCH` take TriG bodies. #127 shows the server can parse a truncated body over a real socket. If #127 hasn't landed:
>   - the management routes read every body to completion;
>   - a test sends bodies in small delayed chunks over a real loopback connection.
> - **Test time.**
>   - Size property-test iteration counts for the land gate, and state each one.
>   - Report each new test project's duration per OS (#126).
>   - Put the long fleet runs in a separately named job or category that #129 can move to the release gate.
> - **Must change.** Update everything in the ADR's "Must change" list that lives in the repo, including `eng/status.cs` consistency for the new milestone. The roadmap and #15 already include this milestone.
>
> ## Standing rules
>
> - **One PR,** red only on `CS0618`.
> - **The release descriptor** is for the next free `0.1.0-preview.N` when it lands (ADR 0102).
>   - Its basis names #105 and the ADRs.
>   - Its `accepted-by: mailto:emil@okkels-klein.dk` names the approver, as `docs/releases.md` point 6 requires (the approval on the head is the evidence). That's the descriptor's line. `accepted-by` on an ADR's decision set is the maintainer's alone (ADR 0066).
>   - The descriptor commit carries a `Closes #105` trailer.
> - **Commits:**
>   - signed with the agent SSH key, as `claude+emil@okkels-klein.dk`;
>   - every commit references #105;
>   - no model ids in commit messages;
>   - no closing keyword anywhere but the descriptor commit's trailer, and none in the PR description (#108).
> - A `docs/traceability/` record for the session (ADR 0033).
> - No suppression or downgrade of DD or VARVE rules.
> - The PR must be green before the agent review. Land through `land/`.
> - If another milestone lands first, merge `main`, renumber the release if it collides, and reconcile the shared files: `Varve.Protocol` routes, the README status, `eng/status.cs` and `Varve.Store.ObjectStorage`'s reader contract.

The session posted its plan on #105 and a revision of it. The maintainer's
decision on forwarding, before the approval:

> A maintainer decision from 2026-10-19 changes the ADR draft. The updated draft is in the project as `varve-adr-management-plane.md`, and here are the essentials.
>
> **Point 2's `307` is superseded by a new point 11: write routing by forwarding.**
> - **Any instance accepts a write.** If it isn't the writer of that dataset, or of the system dataset, it forwards the request unchanged to the writer's internal address, which is recorded in the writer record at claim.
>   - The body is streamed and read to completion.
>   - The caller's bearer token is passed through. The writer authenticates the caller, and the commit is attributed to the caller.
>   - SPARQL Update is forwarded whole, so its WHERE clause is evaluated at the writer.
> - **Hop rule.** A forwarded request carries a hop header. A non-writer that receives one answers `503 writer-moved` with the current epoch and never forwards again. The client retries by request id.
> - **Read-your-writes.** A write's response carries its position. Followers honour a minimum-position header on data reads.
> - **Deployment.**
>   - Kubernetes: one StatefulSet with a headless service.
>   - Azure Container Apps, by default: two apps from one image. `varve-writer` has one replica and claims; `varve` has N replicas, the public ingress, and never claims. `varve` forwards writes to `varve-writer`'s FQDN. The writer recovers by restart.
>   - Container Apps with automatic handover: N single-replica apps behind Front Door or Application Gateway.
>   - `deploy/azure/` and `Varve.Aspire` use the two-app layout.
> - **Gate addition.** The two-server fleet sends writes to the non-writer, and changes the writer during a forward.
> - **Plan question 10:** where forwarding lives (package, layer), how the internal address is configured and recorded, how forwarding and the hop rule are tested, and what Replication (#97) reuses. ADR numbering becomes question 11.

The approval, with an updated ADR draft attached:

> @"/root/.claude/uploads/b1a5f169-7453-5730-90e6-7ef44c2b91b8/b202bee9-varve-adr-management-plane.md" Approved with these changes and rulings. The updated ADR draft is attached. Your revision already matches its point 11.
>
> **Changes**
> 1. **Split #105 into two PRs, each its own release.**
>    - Part 1 is slices 1–10, 12 and 13 without the event move. Until part 2, event schemas stay in the dataset's catalogue graphs, and `DeclarationValidator` refuses event terms in management graphs with a message naming part 2.
>    - Part 2 is slice 11 (ADR 0167, the `m{p}` revisions, the move tests) and carries the `Closes #105` trailer.
>    - Part 1's descriptor names #105 without closing it.
> 2. **Following: no LIST polling.**
>    - A refresh probes the next deterministic slot with a GET. A 404 means nothing new. LIST is used only when the probe finds a chunk and the follower has to adopt across a seal.
>    - Follow on demand: a follower advances when a read arrives and its last advance is older than `Varve:Fleet:FollowInterval`, and on `Varve-Min-Position` or `Varve-Min-Management-Position`. An idle dataset makes no requests.
>    - The system dataset is the exception: it is followed on the interval, because grants must advance without data reads.
>    - The operator guide states the request cost per instance and per dataset. The fleet test asserts that an idle dataset made no requests over its run.
> 3. **The forwarding instance authenticates, validating the bearer token locally, before forwarding.** Unauthenticated requests are refused at the edge. Authorisation, attribution and evaluation stay at the writer, which authenticates again.
> 4. **Decision 7: no peer identity.**
>    - Request ids become fleet-unique with the instance prefix, as an ADR 0094 amendment.
>    - The lost-response case stays as it is for direct writes too. Client idempotency keys scoped to the authenticated agent are a separate issue the maintainer is filing. Don't build them here.
>
> **Decisions**
> 1. `m{p}`: yes, in part 2.
> 2. `checkpoints` and `status` move to `/manage/`.
> 3. `varve:placedOn`: yes.
>    - A placement change works by events: the old writer abdicates with reason `placement` when its applied management position shows the change, and the newly placed instance claims after observing the release.
>    - Test it with a write in flight across the change.
>    - If this grows beyond that, cut it to "takes effect on restart", and say so in the PR.
> 4. Remove `Varve:Datasets` and `Varve:Auth:Datasets`, refused at start, with `--from-config`.
> 5. Your wording for draft point 9: accepted.
> 6. `--record-bootstrap`: yes. The change is a system commit.
>
> **Also**
> - **Before refusing a write over the invariant,** a dataset writer whose applied management position is below its log's highest recorded one runs one `AdvanceAsync` of the system follower. It answers `503 management-behind` only if it's still behind.
> - **Container Apps roles.** Under #110's approved plan, followers write their own record under `readers/`. So the `varve` app needs write access scoped to that prefix: Azure, an ABAC condition on the blob path; S3, a prefix-scoped policy. Plain Reader isn't enough. Whichever of #110 and #105 lands second reconciles the Bicep and the hook.
> - **ADR numbers stand:** 0163–0168, with 0167 moving to part 2.

## The report

**Built (part 1: slices 1–10, 12 and 13, without the event move).**

- **The management position on the commit** (ADR 0165, specification 1.8):
  the host attaches `"p"^^varve:managementPosition` to the commit header's
  attachment list, so format 1 holds and no audit index is needed. A
  validator under the sequencer keeps the positions a dataset records from
  decreasing; before a write is authorised, a writer whose dataset records a
  later position than it applied advances its system follower once.
- **The tier-1 follower** (ADR 0164): `ITailingStorage.RefreshAsync` probes
  the next chunk slot, then the segment's consolidated object, and never
  lists; `Dataset.OpenFollowerAsync` and `AdvanceAsync`. A follower holds the
  catalog version it reads, the hook #110's reader contract takes.
- **The writer record** names the claimant's address, and a release can give
  the reason `placement` (ADRs 0136, 0168).
- **`Varve.Management`** (layer 4, ADR 0166): the vocabulary, the reader and
  code validator by focus and path, graph templates over the caller, the
  grant table per position, the RDFC-1.0 plan, and TriG in and out.
- **The server**: one system dataset per fleet at `_fleet/`, written by the
  instance `Varve:Fleet:Writer` names and followed by the others; the grants
  pinned per request at the applied position, named on every response, with
  `Varve-Min-Management-Position`; datasets opened as writer, follower or
  elsewhere by placement, closed when declared closed, deleted by their
  writer; placement changed by events; settings converged by the writer;
  bootstrap operators recorded by the first writer and by
  `--record-bootstrap`; readiness's `management` object; one JwtBearer
  scheme per plane, each refusing the other's audience; and
  `Varve:Manage:Urls`.
- **Forwarding** (ADR 0168): any instance accepts a write, authenticates the
  caller, and forwards the request once, whole, to its writer; the hop rule
  answers `writer-moved`; request ids carry the instance.
- **The `/manage` routes**, `varve apply` with `--plan`, `--exit-code`,
  `--out`, `--from-plan`, `--from-config` and `--client-assertion`, and
  `Varve.Aspire`'s declarations container. `Varve:Datasets`,
  `Varve:Auth:Datasets` and `Varve:Auth:Server:Admin` are refused at start,
  each naming its replacement. `DatasetSetup` keeps the baseline and the
  migrations.

**The gate, and where each part is.**

| Gate | Test |
|---|---|
| plan and apply idempotent; stale saved plan refused; disjoint applies never conflict | `PlanTests` (200 iterations); `ApplyTests` |
| no response at position p exposes a graph the grants at p forbid | `GrantTests`, 100 generated grant histories; replacing the scopes with every graph fails it |
| the minimum-position header is honoured | `GrantTests`, 200 positions; `ForwardingTests` and `FleetTests` for `Varve-Min-Position` |
| as-of grant queries equal the grants in force | `GrantTests`, 200 reads |
| each plane refuses the other's tokens | `AuthTests` |
| a bootstrap mismatch fails readiness | `HealthTests`, a property over generated management states (1,000) |
| the data plane keeps serving with the system writer killed | `FleetTests` |
| a mistaken second writer shows in readiness and the epoch history | `FleetTests` |
| a two-server fleet runs with writes forwarded, and the writer changes during a forward | `FleetTests` (two processes on Azurite); `ForwardingTests`, 100 generated schedules that move the writer with writes in flight |
| an idle dataset makes no requests | `FleetTests`, counted at a proxy in front of the store |

The follower property over generated histories runs 60 iterations
(`ObjectFollowerTests`). Each property's count is sized for the land gate.

**Deviations, stated.**

- **RustFS.** The server reaches an S3 store only by web identity (ADR
  0137), which RustFS in CI has not, so the fleet's server processes run on
  Azurite. The follower and placement-handover contract runs on Azurite and
  on RustFS at the store (`ServiceFollowerTests`).
- **LIST.** The approval allowed a LIST to adopt across a seal; the follower
  needs none, since it reads the sealed segment's consolidated object by name.
- **The release descriptor.** A release closes at least one milestone issue
  (`eng/lib/Releases.cs`), and a milestone heading is *(complete)* when its
  issues are in a release's basis (`eng/lib/Status.cs`). Part 1 closes no
  issue of its own, so it carries no descriptor yet; the pull request asks
  how the maintainer wants it named.
- **Defects the fleet tests found, fixed in the same change:** a role change
  released the old role before opening the new one, so a write between them
  was answered 404; and an idle system writer did not learn it had been
  superseded until it wrote.

**Durations, Linux x64 (the session's container).** Recorded from the
session's own runs; other operating systems are CI's.

| Project | Tests | Duration |
|---|---:|---:|
| `Varve.Management.Tests` | 25 | 3 s |
| `Varve.Sparql.Store.Tests` | 25 | 9 s |
| `Varve.Protocol.Tests` | 113 | 36 s |
| `Varve.Protocol.Client.Tests` | 117 | 7 s |
| `Varve.Server.Tests`, without the services | 103 (11 skipped) | 12 s |
| `Varve.Server.Tests`, `FleetTests` on Azurite | 4 | 22 s |
| `Varve.Events.Tests` | 64 | 6 s |
