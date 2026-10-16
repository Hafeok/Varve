# Object storage — the log, the projection and the deployment in an object store

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#92](https://github.com/Hafeok/Varve/issues/92), milestone Object storage |
| **Date** | 2026-10-15 |
| **Tool** | Claude Code 2.1.300, a cloud session |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01LWgaYnWmAnTLPasdBNNuCh` |
| **Branch** | `claude/wizardly-allen-m4p1b9`, from `main` at 9b964b8 (`v0.1.0-preview.6`) |
| **ADRs** | 0134–0142, filed unaccepted; 0015 superseded in part by 0138; 0071, 0072, 0099, 0101, 0113, 0115, 0116, 0117, 0119 and 0130 carry dated amendments |

## The prompt

The maintainer's message, with the session prompt and three ADR drafts in a
zip. None of the four was copied into the repository; the drafts' content is
in ADRs 0136, 0140 and 0141:

> @"/root/.claude/uploads/49d8d39f-7297-5fd8-93e5-98a3367eb1cf/abb27dfe-varve-object-storage.zip" Milestone Object storage (#92). The attached `varve-object-storage.zip` holds this session's prompt and three ADR drafts. Extract it into an empty directory outside the repository, then:
>
> 1. Read `varve-object-storage/varve-claude-code-object-storage.md` in full. It is the session prompt and governs this session; follow it as if I had pasted it here. The three ADR drafts beside it are binding inputs, as the prompt says. Never commit the zip or the extracted folder.
> 2. Before planning, confirm the starting point: `v0.1.0-preview.5` is tagged on `main`, #103 is closed by a release on `main`, #92 is open, and the identity hook has run and signs as the agent. If any of these fails, stop and tell me which one.
> 3. Then plan as the prompt says, and wait for my approval before writing code.

The starting point did not hold when first checked (`v0.1.0-preview.5` was
not yet tagged, and #103 was open with its pull request unmerged); the session
stopped and said which.
The maintainer's answer:

> Tagged preview.5 and landed #102; check again

The session prompt, `varve-claude-code-object-storage.md`:

> # Session: object storage (#92)
>
> This prompt is the original Object storage prompt, revised on 2026-10-15, with the 2026-10-14 addendum (segment-based projections) appended as part C. Where part C and parts A–B disagree, part C wins. The three ADR drafts in this bundle are binding inputs: `varve-adr-adx-boundary.md` (point T7), `varve-adr-rec-reference-domain.md` (the generator) and `varve-adr-tier1-writer-handover.md` (what this milestone must guarantee so Replication can hand over a writer). This session files the first two alongside its own ADRs; the handover ADR is filed by Replication (#97), but its "Object storage must guarantee" list is a requirement here.
>
> Varve runs on Azure Container Apps today only by mounting Azure Files, which breaks every assumption the file backend makes: the OS lease (ADR 0075, 0116), `fsync` (ADR 0073), and memory-mapped reads (ADR 0071). This session adds the fourth implementation of the storage contract (ADR 0018, 0040, 0071): the log as write-once objects in any object store with conditional writes, Azure Blob Storage first; the projections as immutable segments in the same store with local disk as a cache (part C); a single writer by an epoch claimed in a manifest and fenced by the log itself.
>
> **Starting point.** `main` after Commit path (#91, `v0.1.0-preview.5`) and Client and integration part 1 (#103) have landed; this backend relies on group commit. Read ADRs 0018, 0040, 0070–0075, 0082–0084, 0102, 0110–0117, 0121–0126 (Commit path: group commit, per-graph concurrency and the graph-version index), the Client and integration part 1 ADRs, `src/Varve.Store/IStorage.cs`, `FileStorage.cs`, `Varve.Store.Browser`, `Replicas.cs`, and the 6a, 6c, operability and commit-path traceability records before planning. `AGENTS.md` applies in full. Plan first, wait for approval; one PR, red only on `CS0618`; the descriptor at the next free `0.1.0-preview.N` when it lands (ADR 0102), naming #92, with `storage-format: 1`; land through `land/`. Client and integration part 2 (#93) and #87 run in parallel; whichever lands second merges `main` and reconciles (`deploy/azure/` and `Varve.Aspire` with #93).
>
> ## A. Decisions (ADRs, Accepted unless stated)
>
> 1. **Two packages, one of them provider-neutral.** `Varve.Store.ObjectStorage` (layer 4) implements the storage contract over a minimal object-store contract of its own, five operations: create-if-absent (`If-None-Match: *` semantics), replace-if-unchanged (`If-Match` on the object's version), ranged read, list by prefix, delete. Every mainstream object store offers exactly these now: Azure Blob Storage (`If-None-Match`/`If-Match`), Amazon S3 (conditional writes), Google Cloud Storage (generation preconditions), MinIO; the ADR cites each provider's documentation for the conditional semantics it relies on, verified, not assumed. `Varve.Store.Azure` (layer 4) is the first adapter: the five operations over Blob Storage, with `Azure.Storage.Blobs` and `Azure.Identity` (managed, no native assets, under constraint 4) or, if the SDK cannot clear the AOT publish without suppression, over `HttpClient` with a bearer token; bring the evidence and I decide. An in-memory adapter in test code proves the neutral package against the contract, and a MinIO adapter runs the same suite in CI (MinIO is S3-compatible, so it stands in for S3 until an S3 adapter is wanted); S3 and GCS adapters are not built here, and the ADR says what each would be and that nothing in the neutral package changes for them.
>
> 2. **The log on objects, with no leases and no append blobs.** A log segment is a sequence of immutable chunk objects, `log/NNNNNNNN.seg/CCCCCCCC`, each holding one group-commit batch's records, written with create-if-absent; because a chunk name is created at most once, two writers can never both write chunk *n*, which makes the log linearisable without a lease and makes a lost response safe (a retry finds the object exists, reads it, and compares). Sealing writes the segment's chunks out as one consolidated object and then deletes the chunks; the log's bytes are unchanged, since the log format is the concatenation and the object layout is the backend's, so read-forever and the determinism property hold over the logical log. Sealing must never reopen or free a chunk slot that a fence could still need (handover ADR). Reads of a sealed log segment go through the local cache and the memory-mapped reader; reads of the open segment are ranged reads of its chunks. `Durability` declares the third level and the ADR says what that promises per provider.
>
> 3. **Single writer: the epoch is claimed in the manifest, the fence is a log slot.** This replaces the earlier "check the manifest before each chunk create" design, to match the writer handover ADR (points 2–5):
>    - `log/MANIFEST` carries the writer epoch, the writer's instance id, and the epoch history (epoch, instance id, first position, trigger, and a release reason when an epoch is released). It is replaced only with replace-if-unchanged.
>    - Opening for write claims epoch n+1 by replacing the manifest against the version that shows epoch n. Then the new writer lists the log, adopts every chunk that exists in order, verifying the header chain, and creates the next chunk slot with create-if-absent, carrying epoch n+1. If that create fails, it adopts what is now in the slot and tries the next one.
>    - Every chunk header carries its epoch. A writer whose create fails because a higher epoch owns the slot stops accepting writes and fails its in-flight batch; the problem type is the handover ADR's `writer-superseded`, added to the catalogue here.
>    - The manifest is **not** on the commit path: no read or replace of it per batch. Group commit pays exactly one create per batch.
>    - An acknowledgement follows the create that holds the commit (ADR 0124's durability rule, unchanged).
>    - Nothing is renewed in the background and no decision is taken from elapsed time. `varve lease <url>` reports the epoch, its holder and the epoch history. `varve lease --take <url> --epoch <n>` is the operator's explicit takeover: it claims n+1 only if the manifest still shows n, with no time-based check; it is recorded in the manifest's epoch history (trigger `operator`), not as a commit, because the log stays the user's data plus commit metadata.
>    - The manifest can record a released epoch, so Replication's abdication needs no format change.
>    - Automatic handover (standbys, probe-triggered takeover) is Replication's (#97), not this milestone's. The ADR states why fencing replaces leasing and what it costs.
>
> 4. **Credentials: identity only.** `DefaultAzureCredential` (managed identity on Container Apps, `az login` on a workstation); account keys, SAS tokens and connection strings are refused by configuration validation with a problem naming the rule, for the same reason ADR 0037 refuses API keys. The one exception is Azurite on a loopback endpoint, which may use its well-known development key; the exception is checked against the endpoint, not a flag. MinIO in CI follows the same rule as far as MinIO allows; say in the plan what MinIO needs and why it is test-only.
>
> 5. **Configuration.** `Varve:Datasets:{name}:Storage = ObjectStorage` with `Varve:Datasets:{name}:ObjectStorage:Provider = Azure | S3Compatible` and the provider's location (`ContainerUrl` for Azure; endpoint and bucket for S3-compatible, MinIO being the tested one), a `CachePath` for the local cache of log and projection segments (default: the container's ephemeral disk under `/var/lib/varve`), and a `CacheBytes` bound. `--print-config` redacts nothing here because there is nothing secret.
>
> 6. **Determinism holds across backends.** The same script against the file backend and the object-storage backend produces byte-identical `log/` bytes (download the blobs, compare), and, with part C, byte-identical projection segments; a dataset moved from a directory to a container opens and answers identically, which is the migration path and the backup path in one.
>
> 7. **Deployment.** `deploy/azure/` Bicep: a Container Apps environment, the app with a system-assigned identity, a storage account with the container, `Storage Blob Data Contributor` on it for the identity, the OIDC settings as parameters, `/health/ready` as the probe, scale rules from 0 to 1 replica per dataset writer (the ADR says what a second replica does today: lose the epoch race, stay read-only and report it on `/health/ready`; read replicas and handover are Replication's). `Varve.Aspire` gains `WithAzureBlobStorage(...)` over Aspire's Azure Storage hosting integration, so `azd up` deploys the same shape; the sample AppHost gains the Azure variant. The operator guide gains "Run on Azure Container Apps" with cold-start time and the cost of a scale-to-zero start stated from measurement.
>
> 8. **Small self-hosted, a first-class target.** A Raspberry Pi 5 (arm64, 8 GB): the file backend on an NVMe SSD (the guide says plainly that an SD card's `fsync` and write endurance do not suit a log), and the same Pi against MinIO on a NAS with the segment cache local. The operator guide gains "Run on a small machine": the setup for both, the runtime limits for 8 GB, and measured commit latency, query throughput and cold start on the device (run on real hardware by the maintainer if CI has none; the session writes the benchmark script and the page with the numbers left for the maintainer, marked as such). `linux-arm64` stays a CI target for the image and the storage contract suite.
>
> 9. **Vocabulary.** "Segment" already means a log segment (`log/NNNNNNNN.seg`). The projection's immutable objects need a distinct term in AGENTS.md's vocabulary table and in every doc; I propose **extent** (the ADX term T7 borrows), but propose another if it reads better in the code. No document may use one word for both.
>
> 10. **Roadmap text.** Keep `docs/roadmap.md`'s Object storage, Replication and Management plane texts consistent with what this milestone decides; the ADR cross-references Replication where the write-once chunks, the epoch history and the shared projection catalog are replication's seed.
>
> ## B. Definition of done
>
> - The full storage contract suite (`Varve.Store.Tests`, including the external-backend suite) against Azurite and MinIO in CI, on Linux; the same suite against a real storage account as an optional leg that runs only when the federated credential exists in a `main`-restricted environment, like `auth (providers)`.
> - Every milestone 4 and 6a property re-run against the object-storage backend at its iteration counts, including crash recovery and failed state.
> - Failure injection, the gate: a fault-injecting `HttpPipelinePolicy` (or handler) that at every request can fail with 500, 503, 429 with `Retry-After`, time out, or succeed server-side and lose the response; a writer superseded by a newer epoch between normalisation and its chunk create; two candidates claiming the same epoch; the process killed between a chunk create and anything after it; a cold start with the latest catalog or checkpoint missing, corrupt, or from another dataset. After every injected fault the dataset reopens at the last closed commit, no commit is duplicated or lost, no acknowledged commit is lost, the superseded writer appends nothing after the fence, and every pinned and as-of read equals the reference model. Report the injection-point count and every defect found.
> - Determinism across backends and the directory-to-container migration, as properties.
> - The Bicep deploys from a clean subscription in CI's optional leg (`what-if` in the always-on run, a real deploy in the optional one), and the Aspire Azure sample starts against Azurite locally.
> - Allocation: the read path over cached segments allocates nothing per quad, same measurement as the file backend; the append path's per-batch allocation stated.
> - Group commit on this backend: commit latency p50 and p99 for one writer and throughput for eight against Azurite and MinIO and, if the leg exists, the real account, with the observed batch sizes and the request count per commit (which should be one create).
> - Part C's gate additions.
>
> ## Non-goals
>
> Writer handover, standbys and read replicas (Replication, #97, which builds on the epoch history, the chunk fence and the shared catalog; the ADR says so), Azure Files, S3 and GCS adapters (the neutral package is built for them and the ADR sketches each), anything in `Varve.Protocol.Client`, the management plane (#105).
>
> ## Report
>
> The traceability record and PR body: ADR numbers; the SDK-or-REST decision with evidence; the contract suite counts per backend; the injection-point count and defects; the latency, throughput and request-count tables; cold-start time and request count at REC sizes S, M and L; the determinism and migration properties' iteration counts; the Bicep's resource list; the Pi page and what the maintainer must run on the device; which items of the handover ADR's "Object storage must guarantee" list are met, each with its test.
>
> ---
>
> # C. Addendum: segment-based projections (2026-10-14, revised 2026-10-15)
>
> It replaces "`derived/` on ephemeral disk, checkpoints mirrored, cold start as checkpoint plus tail" wherever the two disagree. "Segment" below means a projection segment; part A point 9 renames it.
>
> ## Why
>
> At billions of quads, rebuilding `derived/` or restoring a full checkpoint on every cold start or new follower does not scale. Disk size would bound dataset size. Azure Data Explorer solves the same problem by keeping immutable extents in blob storage and treating local SSD as a cache. T7 applies that to Varve's projections.
>
> ## Requirements
>
> 1. **Segments.**
>    - Each index permutation of the default projection is stored as immutable segment objects in the store. The same goes for the dictionary's id-to-term and term-to-id lookups.
>    - Each segment holds a sorted, compressed run over a position range.
>    - Each quad in a segment carries its asserting position and, if retracted, its retracting position.
>    - Each segment carries statistics (key range, position range) in a header readable by one ranged read.
>    - Segments are written by create-if-absent and are never changed.
>
> 2. **Catalog.**
>    - A projection catalog object names the live segments per permutation and the log position they cover.
>    - Only the dataset's writer replaces it, by replace-if-unchanged, under the epoch fence. A superseded writer cannot swap the catalog.
>    - Followers read the catalog. They never write it.
>
> 3. **Merges.**
>    - Background merges write new segments, then swap the catalog.
>    - Old segments are deleted only when no catalog version that a live reader may hold still names them. State the rule, and do not use a time-based grace period (the no-elapsed-time constraint). Readers announce the catalog version they hold, or a deletion waits for a later catalog event.
>    - A merge never changes a result at any position.
>
> 4. **Cache.**
>    - Local disk is a segment cache with a configured size.
>    - Eviction never changes a result.
>    - The memory-mapped read path works over cached segments.
>
> 5. **Cold start.** Read the catalog, attach segments lazily, replay the log from the catalog position. Measure and document it at three dataset sizes.
>
> 6. **The file backend** uses the same segment format in a directory, so determinism across backends extends to projections.
>
> 7. **Inline term ids.** Report which XSD datatypes ADR set zero's inline class covers. Report whether inline ids order by value within a datatype, so that a POS range scan over a numeric or `xsd:dateTime` predicate is a contiguous run. If they don't, propose the ADR change; don't make it in this milestone without approval.
>
> 8. **The REC portfolio generator** (`varve-adr-rec-reference-domain.md`).
>    - Vendor the REC 4 SHACL release into `testdata/rec/` at a pinned tag, with a checksum and the BSD-3-Clause notice in `THIRD-PARTY-NOTICES`.
>    - Build a seeded, deterministic generator in a test-only project. It produces REC portfolios with a history: renovations, assets moved, points added and retired, tenants entering and leaving.
>    - Define sizes S, M and L. These are the dataset sizes for the cold-start and segment measurements in this milestone, and the stated datasets for later benchmarks.
>
> 9. **The data model and the format change.**
>    - Validity intervals (point 1's asserting and retracting positions) replace point-in-time runs. That is a change to the default projection's model, not only its layout, so it gets its own ADR: what it changes for as-of reads, checkpoints, the graph-version index (ADR 0125) and the bulk loader.
>    - The segment format is a new derived format, version 4 by the current count. `log/` stays storage format 1.
>    - A dataset whose `derived/` is format 3, written by the previous release, opens under this one. The ADR says whether that is a rebuild from the log or a migration, and states that segments, being derived, change by rebuild and never need a read-forever migration.
>
> 10. **CI containers** follow ADR 0099: pinned by digest, from a registry without an anonymous pull limit, never Docker Hub. Azurite comes from `mcr.microsoft.com`. Name MinIO's registry in the plan and show that it meets the same criterion; check too that the MinIO edition and image you pin are still published and licensed for this use, and say so.
>
> ## Plan questions
>
> - The quad counts for S, M and L, and the history shape (commits, and the share of retractions).
> - Does all of the above fit this release? If not, propose the cut. The minimum acceptable cut is the checkpoint written in the segment format with a catalog, and lazy attach on cold start. Merges and the segment cache may follow in a named later milestone. The checkpoint format must not need a break to get there.
> - Name the segment format version, and say whether it falls under the read-forever rule. Segments are derived, so a format change can be a rebuild, not a migration. State that explicitly.
> - Rebuild or migrate for a format-3 `derived/`, with the expected time at REC size M.
> - Give the segment size target and the request count per cold start, against the provider's per-request cost.
> - The term for projection segments (part A point 9).
>
> ## Gate additions
>
> - Over generated histories with retractions: a segment-backed projection equals the in-memory projection at every position; this holds across merges; it holds with any cache eviction schedule.
> - With a superseded writer, the catalog swap is refused and the reader's results are unchanged.
> - A follower attaching the writer's catalog serves the same results as the writer at the same position.
> - Failure injection at every segment write, catalog swap and deletion: no reader ever sees a catalog naming a missing segment.
> - Cold start time and request count measured on Azurite and MinIO at REC sizes S, M and L.
> - The generator gives the same bytes for the same seed and size on linux-x64 and linux-arm64.
> - A dataset written by the previous release (derived format 3) opens and serves identical results at every position; the rebuild or migration time is measured at REC size M.

The maintainer's approval of the plan, with its conditions:

> Approved, with these answers and conditions.
>
> ## The five decisions
>
> **1. REST, not the SDK.** This isn't close. MSAL's extension carries P/Invoke into libsecret, and brief constraint 1 says no P/Invoke in what ships, whether or not it is reachable. With 21 transitive pins, Arrow and ProtectedData on top, the SDK fails constraint 4 as well. Conditions on the REST path:
> - **The token sources:**
>   - the Container Apps managed-identity endpoint (`IDENTITY_ENDPOINT` with `X-IDENTITY-HEADER`);
>   - workload identity (`AZURE_FEDERATED_TOKEN_FILE` exchanged as a client assertion at the Entra token endpoint);
>   - `az account get-access-token`, for workstations only.
> - **Token handling:** tokens are cached until shortly before expiry. They are never logged, never put in a problem body, and never put in telemetry, and a test asserts all three.
> - **API version:** `x-ms-version` is pinned and named in ADR 0134.
> - **ADR 0137** states that this is outbound workload identity, so ADR 0037's inbound rule is untouched.
>
> **2. MinIO: (a), decided by the contract suite, not by a choice.** Don't pick (b) or (c).
> - Slice 2 runs the full contract suite against both SeaweedFS and RustFS. Add a concurrency property: N clients race create-if-absent on one name and exactly one wins; N clients race replace-if-unchanged on one version and exactly one wins.
> - The first server to pass becomes CI's S3-compatible server.
> - If neither passes, the S3-compatible adapter does not ship in this release. Azure only, with the gap recorded in the roadmap. An adapter we can't test against a correct server is not shipped.
> - Replace "MinIO" in the repository's text with "an S3-compatible server (<the one that passed> in CI)". I'll update the issues and the roadmap outside the repository.
>
> **3. S3 credentials: web identity, plus the loopback key, and nothing else.** No key file on a private network: identity-only means only identity. Restructure the Pi page around it:
> - the file backend on NVMe is the supported small-machine setup;
> - an object store on a NAS is documented only where the server supports `AssumeRoleWithWebIdentity`, or where it runs on the same host (the loopback rule).
> Say plainly that a typical NAS deployment falls outside both.
>
> **4. Opening for write claims n+1: accepted, with guards against flapping.** A restart as takeover matches the handover ADR's point 8. As stated, though, two instances that are both configured to write would supersede each other on every restart, and the handover ADR's point 9 bounds flapping by epoch-named triggers, which a restart is not. So:
> - **A superseded instance never claims again on its own.** It stays open read-only. `/health/ready` reports `superseded` and is false, `/health/live` stays true, and the orchestrator therefore never restarts it to win the epoch back. The only way it claims again is a fresh process start.
> - **Only writer-mode opens claim.** Read-only opens never claim: the CLI's read commands, `varve info`, `varve lease` without `--take`, and follower attach. Test that a read-only open leaves the writer record unchanged.
> - **Every claim is recorded** with trigger `restart` or `operator`, and the operator guide explains how to read a run of `restart` claims: two writers are configured for one dataset.
> - **A note on the handover ADR:** restart claims carry no epoch, and they are bounded by the first guard instead. I'll put that into the handover ADR draft.
> - **Graceful shutdown abdicates.** SIGTERM releases the epoch before exit, with reason `shutdown`, which makes the revision rollover clean rather than superseding. If that is more than this milestone can carry, say so; otherwise it is small and belongs here.
>
> **5. The cut: accepted, with one measurement.** Without merges, every read k-way-merges across all extent sets, and that count grows by one per checkpoint, so read cost grows without bound under a checkpoint policy. Measure head and as-of read latency at REC size M after 10, 50 and 100 checkpoints, put the table in the record, and give the operator guide a stated checkpoint budget until Extent maintenance lands. I'll file Extent maintenance as a milestone before Replication and add it to the 1.0 definition.
>
> ## Departures
>
> - **`log/MANIFEST` stays write-once, and the epoch lives in a separate `writer` object:** accepted; that's better than the prompt. The handover ADR's "manifest keeps the epoch history" becomes "the writer record keeps it". I'll amend the draft.
> - **`.chunks/` prefix, and `.seg` as the consolidated object:** accepted.
> - **Tombstones: right idea, but don't keep one per chunk.** As proposed, `log/` keeps an empty object for every batch ever written, so listing and cold start grow with history. Keep a tombstone only at fence slots, which means one per epoch change; consolidation deletes every other chunk.
>   - **Why that's safe:** a writer only ever creates the slot after its own last successful create, or retries a slot it already wrote. When a superseded writer of epoch e was last successful at slot k−1, the only slot it can still attempt is k, and k is exactly the new epoch's fence slot. The new writer computes the fence deterministically from the log it adopted, across a segment boundary too, because the seal is in the log bytes.
>   - **Proof:** add it to the concurrent-candidate property. Over generated schedules, a superseded writer's attempted creates are either its own existing slots or a fence slot.
>   - **Deletion:** fence tombstones are kept for ever. That's one small object per epoch, and the epoch history already lists them. Nothing waits on Replication for this.
> - **`what-if` only in the optional leg:** accepted. Keep `bicep build` and lint always on.
> - **The validity-interval ADR supersedes ADR 0015 in part:** accepted, and I'll read it first. Bring 0015's original reasoning into the new ADR's context: why as-of reads were not served from an index then, and what T7 changes. Don't just cite the number.
> - **Cold-start and cost figures as "to be measured by the maintainer" without the leg:** accepted.
>
> ## Design: one change
>
> **Where the providers live.** Brief principle 3, high cohesion, puts each provider's REST and signing code in its own package, and the layer rule then decides the shape:
> - `IObjectStore`, the five operations, is defined in `Varve.Store` at layer 4, beside `IStorage`, the lowest layer that can define it without knowing its implementers;
> - `Varve.Store.ObjectStorage`, `Varve.Store.Azure` and `Varve.Store.S3` are each layer 5, and each references only `Varve.Store`;
> - the server at layer 6 composes an adapter into the object-storage backend.
>
> If the adapters went inside `Varve.Store.ObjectStorage`, every provider change would be a change to the backend. If they referenced it, that's a same-layer reference. Check this against VARVE0003–0005 in the first slice.
>
> ## Answers to the plan questions
>
> - **Inline ids:** file the inline-set version 2 issue as you propose: offset binary, inline `xsd:dateTime`, `xsd:date` and bounded `xsd:decimal`, new datasets only. Don't change it here.
> - **Extent:** the term, derived format 4 not under read-forever, and format 3 migrating as it runs: all accepted.
> - **Sizes, extent size and request counts:** accepted.
> - **REC v4.1.0 vendored with SHA-256s:** accepted.
> - **Storage settings reaching `DatasetSetup` and the admin `PUT`:** fine for now. Placement moves to the management plane (#105). Note that in the ADR rather than designing around it.
>
> The ADR list and the order of work stand. Two side notes. The roadmap in the repository has no Management plane section because `docs/roadmap.md` predates #105; leave that to a separate docs change, not this PR. Your descriptor takes the next free number when it lands; don't fix `preview.7` in text before then.

The same approval arrived a second time, unchanged, after the session's
container restarted during the benchmarks; nothing in it was new.

## The report

**ADRs.** 0134 (the object-store contract: five operations — read a range,
create if absent, replace if unchanged, list a prefix, delete — defined in
`Varve.Store` at layer 4; `Varve.Store.ObjectStorage`, `Varve.Store.Azure`
and `Varve.Store.S3` each at layer 5, referencing only `Varve.Store`; REST
over `HttpClient`, `x-ms-version` pinned; CI's S3-compatible server chosen by
the suite; the race property part of the contract), 0135 (the log as
write-once chunk objects under `<segment>.chunks/`, consolidated into one
`.seg` on seal; one create a commit batch), 0136 (one writer by an epoch in
a `writer` object replaced if unchanged, fenced by the next log slot; a
tombstone only at a fence slot, kept for ever; no lease, no timer; restart
and operator claims recorded; a superseded instance never claims again;
graceful shutdown abdicates), 0137 (storage credentials are the workload's
identity — managed identity, workload identity, `az` on a workstation, S3
web identity — and a key only on loopback; outbound, so ADR 0037 is
untouched), 0138 (validity intervals in the default projection's
checkpoints; supersedes 0015 in part, its reasoning carried into the
context), 0139 (extents and the projection catalog, derived format 4, not
under read-forever; format 3 migrates as it runs; the catalog under the
fence; the page cache), 0140 (what Varve takes from Azure Data Explorer, and
what it does not), 0141 (RealEstateCore 4.1.0 as the reference domain, sizes
S, M and L), 0142 (deployment on Azure Container Apps, the Aspire Azure
variant, small machines). Nine decision sets, filed unaccepted; `CS0618` is
the one red.

**What else changed**, by dated amendment (ADR 0068): **0015** (superseded in
part by 0138: as-of reads inside a checkpoint's interval are served from the
extents), **0071**, **0072**, **0099**, **0101**, **0113**, **0115**,
**0116**, **0117**, **0119** and **0130** — each names what in it the
object-store backend, the catalog or the extents change. `storage-format.md`
states format 4 and the object layout; `docs/problems/writer-superseded.md`
is the new problem type (`503`). The roadmap's Object storage section states
the scope as built, a new **Extent maintenance** section and row (merging
sets and deleting retired extents) precedes Replication, and its Replication
tier 1 notes what this milestone leaves it. The roadmap's Management plane
section is left to a separate change, as asked.

**The departures the plan proposed**, all accepted in the approval: the epoch
in a separate `writer` object with `log/MANIFEST` write-once; `.chunks/` and
`.seg`; tombstones only at fence slots (the maintainer's correction of the
plan's one per chunk); `what-if` only in the optional Azure leg, `bicep
build` and lint always; ADR 0138 superseding 0015 in part.

**The S3-compatible server**, decided by the suite as condition 2 asks
(ADR 0134 decision 6): **RustFS 1.0.1** passed the storage contract suite and
both race properties five runs in five; **SeaweedFS 4.48** refused writes
until given small volumes, then passed three runs in five and failed two
with `500` when the suite's buckets used up its volumes. RustFS is CI's
server; "MinIO" in the repository's text now reads "an S3-compatible server
(RustFS in CI)". `Varve.Store.S3` ships.

**The properties and gates**, with what they found:

- **The contract suite** runs against the in-memory store, Azurite 3.37.0
  and RustFS 1.0.1 in CI, and against a real storage account in the optional
  leg, reached by GitHub's OIDC token as a federated credential.
- **Faults at every request** (`ObjectFaultTests`): seed 9201, 24 steps,
  828 injection points, in CI; seeds 9202–9205 at 40 steps, 3,820 points,
  on demand; each recovering to a closed commit no earlier than the last
  acknowledged. Against Azurite, faults at the HTTP layer (`HttpFaultTests`):
  175 points at the default six steps (742 at 40, before the default was
  trimmed for CI's time). The extents' writes: 54 writes and 108 fault
  points (`ObjectExtentTests`). **Counterexamples found**: a page cache whose
  blob shared the table's reference looped for ever on a dead entry (found
  with `dotnet-stack`; an open now holds its own reference); a lost catalog
  swap did not mark the dataset superseded.
- **The fence** (`ObjectEpochPropertyTests`, 200 schedules): a superseded
  writer's attempted creates are its own existing slots or the new epoch's
  fence slot, and nothing is appended after it — the proof the approval asks
  for that a tombstone is needed only at a fence.
- **Determinism across backends**: the same requests give byte-identical
  logs, extents and catalogs on files and in objects (250 scripts); a
  dataset moved from a directory to a container and back answers identically
  at every position (40 scripts); the extents equal the full run at every
  position (`ExtentTests`, 60 and 60; in objects, 30 with eviction at 1 B,
  1 MiB and 8 GiB of cache).
- **A format-3 dataset from preview.6** (`testdata/derived-format-3`,
  written by the preview.6 generator, SHA-256s checked): it opens on its full
  checkpoints, answers at all 121 positions what the log alone gives, starts
  a chain of sets at its first checkpoint, and drops its full checkpoints
  with no change to any answer.
- **A read-only open leaves the writer record unchanged**; `/health/ready`
  reports `superseded` and false while `/health/live` stays true; a second
  server's takeover answers the first's next write with `503
  writer-superseded` (against Azurite, `ObjectStorageServerTests`).
- **Tokens** are never in a log line, a problem body or a span: a test
  covers the messages and renderings each is made from.
- Totals at the last run: `Varve.Store.Tests` 265, of which 82 in the object
  namespace; `Varve.Server.Tests` 90; `Varve.Protocol.Tests` 101;
  `Varve.Client.Tests` 102; none failed.

**Benchmarks**, on this session's container (`tests/Varve.Benchmarks`,
`--objects`, `--extent-reads`, `--format3-open`), against Azurite and RustFS
on loopback — so the object-store figures measure the protocol and request
counts, not a cloud's latency.

REC S:

| | files | Azurite | RustFS |
|---|---:|---:|---:|
| load | 482 commits/s | 186 commits/s | 146 commits/s |
| commit p50 / p99 | 0.24 / 34 ms | 2.48 / 35.5 ms | 4.73 / 38.9 ms |
| requests a commit | — | 1.00 | 1.00 |
| checkpoint at the head | 1.5 s | 1.6 s: 9 requests (8 creates, 1 replace), 15 MiB | 1.6 s, the same |
| cold start | — | 2.3 s | 1.3 s |
| 8 writers | 8,321 commits/s | 2,144 commits/s, 0.16 creates a commit | 1,316 commits/s, 0.14 creates a commit |

REC M (25,000 commits, 5,049,846 quads at the head):

| | files | Azurite | RustFS |
|---|---:|---:|---:|
| load | 47.3 s, 528 commits/s | 573.8 s, 44 commits/s | 185.2 s, 135 commits/s |
| commit p50 / p99 | 0.30 / 13.95 ms | 2.74 / 386.8 ms | 5.10 / 30.2 ms |
| requests during the load | — | 30,275: 25,011 creates, 5,264 deletes (consolidation), 275.6 MiB up | the same |
| requests a commit | — | 1.21 (creates 1.00) | 1.21 (creates 1.00) |
| checkpoint at the head | 33.1 s | 39.6 s: 9 requests (8 creates, 1 replace), 415.1 MiB | 31.0 s, the same requests |
| cold start, open and a first read | 0.4 s | 29.1 s: 19,800 requests, 19,790 reads, 180 MiB | 23.2 s, the same requests |
| 8 writers, 2,000 commits of 10 quads | 11,785 commits/s | 1,117 commits/s, 0.17 creates a commit | 1,394 commits/s, 0.14 creates a commit |

Azurite slows as its blob count grows (its p99 at M is ten times its p99 at
S, with the same requests a commit); RustFS does not. Neither is a cloud's
latency.

**Reads after checkpoints** (condition 5), REC M on files, the load and 100
checkpoints in 70.8 s:

| Checkpoints | Head: lookup p50 | Head: a subject's quads p50 | Head: exact count p50 | As-of inside a set | As-of at a set's end |
|---:|---:|---:|---:|---:|---:|
| 10 | 42.0 µs | 5.7 µs | 28.3 µs | 29.8 ms | 0.0 ms |
| 50 | 71.3 µs | 18.3 µs | 143.6 µs | 6.8 ms | 0.1 ms |
| 100 | 98.7 µs | 78.1 µs | 354.0 µs | 6.5 ms | 0.1 ms |

Head reads grow with the number of sets, as the approval expected: a
subject's quads 14× and an exact count 12.5× from 10 to 100. As-of inside a
set falls as sets narrow, since it rewrites less of the log. The operator
guide's budget (`docs/operator/backup-and-restore.md`): keep at most 20
checkpoints until Extent maintenance lands, which keeps head reads within
about twice the 10-checkpoint figures.

**A format-3 dataset at M** (`--format3-open`, REC M written by preview.6
with ten full checkpoints):

| | |
|---|---:|
| head | 25,000 commits, 5,049,846 quads, full checkpoints every 2,500 |
| open as it is, and a first read | 0.5 s |
| the first checkpoint under this release (one extent set over the whole history) | 31.3 s |
| for reference, a rebuild from the log with `derived/` removed | 24.8 s |

The migration costs one checkpoint, about what a rebuild costs; nothing is
rewritten at open.

**A finding for the record: cold start reads every chunk of the open
segment.** At REC S a cold start costs 1,035 requests, 1,025 of them reads;
at M, 19,800 requests, 19,790 of them reads, and 23–29 s on loopback: one a
chunk not yet consolidated (5,264 of the 25,011 were, at the end of the
load), since a segment is consolidated only when it seals. The catalog lets the projection skip what it covers, but the log's
headers below it are still read to rebuild the commit index. Skipping them
needs a shared commit index, deferred and stated in ADR 0139.

**Deployment.** `deploy/azure/container-apps/main.bicep`: a Log Analytics
workspace; a storage account with shared-key access off, its blob service
and one container; a Container Apps environment; the app, at most one
replica, with a system-assigned identity; and the Storage Blob Data
Contributor role on the container for that identity. `bicep build` and lint
run in CI; `what-if` and the Azure contract leg run in
`.github/workflows/azure-objects.yml` when the repository's variables name a
subscription. Aspire gets `WithAzureBlobStorage`.

**Deviations from the plan.**

- **The Aspire sample has no Azurite variant.** The loopback-key rule
  (ADR 0137) refuses a key for an emulator in a neighbouring container,
  which is how Aspire runs one. The Azure variant is tested; a local one
  would need the rule bent, and it is not.
- **Deferred, and stated in ADR 0139**: skipping the log headers below the
  catalog at cold start (needs a shared commit index), and a follower's
  refresh other than by reopening.
- Issue [#114](https://github.com/Hafeok/Varve/issues/114) is the inline-set
  version 2 issue, filed as the approval asks; nothing of it changed here.

**What the next milestones can rely on.** *Extent maintenance*: retired sets
in an object store are never deleted, and a dropped checkpoint merges its
set into the next with `AdjacentRows`; merging older sets and deleting what
no catalog names is that milestone's. *Replication*: the chain and the
catalog are what a replica ships, and the writer record's epoch history is
the handover's. *Management plane* (#105): storage settings reach
`DatasetSetup` and the admin `PUT` for now; placement moves there.

**For the maintainer.**

- Cold-start and cost figures on a real storage account, and on a Pi 5 with
  an NVMe SSD, are **to be measured by the maintainer**, as accepted; the
  commands are the ones above.
- The release descriptor takes the next free number when it lands; at the
  time of writing that was `v0.1.0-preview.7`.

**Commits**: 914cd88, c26624a, 73db0a1, 281df0b, cc65031, 1e1f7f5, 01177be,
0e3e260, c45920b, e7035f8, this record, and the release.
