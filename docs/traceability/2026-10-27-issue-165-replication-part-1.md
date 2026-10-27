# Replication, part 1: writer handover, warm standbys, automation, active-active within a store

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The opening message, the
> plan as submitted and the answers are verbatim. The transcript itself is
> held by the maintainer. This record grows with the part; its report is
> brought current at each push.

| | |
|---|---|
| **Issue** | [#165](https://github.com/Hafeok/Varve/issues/165), part 1 of Replication ([#97](https://github.com/Hafeok/Varve/issues/97)); part 2 is [#166](https://github.com/Hafeok/Varve/issues/166) |
| **Date** | 2026-10-27 onwards |
| **Tool** | Claude Code 2.1.311, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata (`configured_model`, `session_context.model`, `last_served_model`) |
| **Session identifier** | `session_01NbWSYTiuFLu3ZNqXGRKLQ3` |
| **Branch** | `claude/adoring-dirac-2wrtt2`, from `main` at 6cdb402 |
| **Machine** | a cloud container, Intel Xeon @ 2.10 GHz, 4 cores, 15 GiB, Ubuntu 24.04.5 LTS, .NET SDK 10.0.403, runtime 10.0.14 |

## The prompts

### The message that started the session

It attached `varve-replication.zip`, holding the session prompt
(`varve-claude-code-replication.md`) and the handover ADR draft
(`varve-adr-tier1-writer-handover.md`), and its text was the session prompt
itself:

> # Session: Replication — handover within a store, log shipping, promotion, and the verified stores (#97, with #117)
>
> Replication is the longest remaining path to 1.0 (#15). Erasure and row-level access (#14) wait on it. The log is the unit of replication: a replica is a projection of a log it does not write.
>
> **Starting point.** Current `main`. Everything this milestone builds on has landed:
> - **the tier-1 follower** for every dataset an instance doesn't write (ADR 0164);
> - **write forwarding,** with the internal address in the writer record, the hop rule and `writer-moved` (ADR 0168);
> - **placement** (`varve:placedOn`) and the management routes (ADRs 0163 and 0166);
> - **the writer record and the log-slot fence** (ADR 0136);
> - **extents with merges, reader records** (`maxDerived` included) and the shared commit index (ADRs 0158–0162);
> - **derived format 5:** seek-don't-narrow, with a 64-bit check per block (ADRs 0177 and 0178, in #148, landing as preview.15).
>
> A set is nine blobs, which a replica copies as they are.
>
> **Read before planning:**
> - #97 in full, with all four amendments, and #117 in full. This milestone takes #117 with it, because a tier-3 log may be shipped only to a verified store.
> - **The attached ADR draft `varve-adr-tier1-writer-handover.md`.** This session files it as ADR 0179 and implements it. Its points 8–11 already constrain the code on `main`.
> - **ADRs 0134–0142** (object storage), **0158–0162** and **0177–0178** (extents), **0163–0168** (the Management plane, above all 0164's follower and 0168's forwarding) and **0094** (request ids).
> - `docs/spec/storage-format.md` (the log, chunks, seals, the writer record and §7 extents);
> - `docs/operator/fleet.md` and the Container Apps two-app layout in `deploy/azure/`.
>
> `AGENTS.md` applies in full. **Plan first, and wait for approval.** The plan must be posted on #97 before any code.
>
> ## A. Decisions (Accepted; each becomes an ADR or a dated amendment)
>
> 1. **Two releases.** This milestone is larger than any before it, so it lands as two parts, each its own release with its own issue, as #120, #131 and #138 did:
>    - **Part 1, within a store:** writer handover (ADR 0179), data-plane tier-1 followers completed (point 2), handover automation, and active-active by dataset across a fleet.
>    - **Part 2, across copies:** tier 3 log shipping, tier 2 geo-replication, promotion, and `varve storage verify` with the compatibility page (#117).
>
>    The last descriptor closes #97, and #117 closes with part 2. If the plan shows a better seam, propose it.
>
> 2. **Tier 1 is mostly built. Say what's left.**
>    - #134 made every non-writer follow every dataset it doesn't write, and forward its writes.
>    - #97's original "writes refused with `409` naming the primary" is **superseded by forwarding** (#97's amendment of 2026-10-19). Record that in the ADR.
>    - What remains:
>      - `Varve-Position` and lag on every follower's responses and readiness, if not already complete;
>      - the warm flag (tailing, with `derived/` caught up) that handover needs.
>
> 3. **Handover, as the draft states.**
>    - Exactly two triggers, abdication and probe failure, never time. The epoch is claimed in the writer record, and the fence is a log slot.
>    - The problem types `writer-superseded` and `writer-stepped-down`, and the takeover request through `PUT /manage/datasets/{d}/state`.
>    - Only warm standbys qualify. Automatic handover is opt-in per dataset (`varve:automaticHandover`, already in the vocabulary).
>    - **Forwarding across a handover:** a non-writer's directory learns the new writer from the writer record (ADR 0168's refresh on `writer-moved` or on an epoch change seen while following). Write that interaction into ADR 0179 and test it, including a write forwarded across the handover.
>
> 4. **Handover automation honours "no timers in Varve".**
>    - Varve never judges liveness. The orchestrator's probe does, and automation turns a probe failure into a takeover request with an operator-role managed identity.
>    - **What to ship:**
>      - Kubernetes: a small sample controller or probe hook in the operator guide, with its manifest validated in CI;
>      - Azure: the N-single-replica layout behind Front Door, with the automation. Propose the smallest managed mechanism, such as a Container Apps job or an Azure Function on an Azure Monitor alert, and say what it costs.
>    - The **default two-app Container Apps layout keeps restart as its recovery.** Measure and document its write downtime beside the takeover time, as #97's amendment requires.
>
> 5. **Tier 3: log shipping between any two stores or a directory.**
>    - **The shipper:** a follower role, which instance, and how it's configured (proposed: a management declaration of a shipping target per dataset).
>    - **What's shipped:**
>      - the log's chunks and sealed segments, byte-identical, created on the target with create-if-absent;
>      - the extents and catalog, so a promoted target cold-starts fast. A target's catalog never names an extent not yet shipped, so ship the catalog last;
>      - not the writer record. The target has its own (point 6).
>    - **Resume:** by the target's tail, verified through the header chain. **No timer decides anything.** An interval may affect how soon shipping happens, never whether it's correct.
>    - **Only to a verified store** (#117), or to a directory. Shipping to an unverified store is refused with a problem naming the compatibility page.
>    - **The extent deletion rule (ADR 0160)** must not delete what the shipper still needs to ship. The shipper is a reader and announces like one.
>
> 6. **Promotion across copies is an operator's act, always.**
>    - `varve promote <target>` claims a fresh epoch on the target's writer record and records a **lineage** marker: the source's identity and the last position shipped.
>    - **The returning old primary:** its next shipped chunk is refused by the header chain (`prev` mismatch) and by the lineage marker. A promoted copy never accepts writes from its old source.
>    - **What RPO 0 means:** RPO is the shipping lag at the moment of loss, and the operator guide states it plainly.
>    - **The rehearsal in every tier:** promote, write, let the old primary return, and show the refusal.
>
> 7. **Tier 2: the store's geo-replication** (Azure RA-GRS first).
>    - A follower reads the secondary endpoint read-only. Its lag comes from the account's last-sync time, never from local time arithmetic.
>    - Promotion after an account failover is point 6's act on the failed-over account.
>    - Bicep for two regions with Front Door.
>    - The gate runs on a real account in the optional `main`-restricted leg, with workload identity federation and no keys.
>
> 8. **`varve storage verify` and the compatibility page** (#117), as the issue states:
>    - the contract suite's race properties against any endpoint, in a scratch prefix;
>    - result records under `testdata/object-stores/results/`;
>    - a generated `docs/operator/object-stores.md` with four states, checked by `eng/status.cs`;
>    - Garage recorded as unsupported.
>
>    Part 2 depends on it.
>
> 9. **Active-active by dataset.** Two regions, two datasets, each written in its home region and followed or shipped in the other, through placement. It's documented and tested, and **never two writers on one dataset.**
>
> 10. **The RustFS gap.** Since #134, the server reaches S3 only by web identity, which RustFS in CI doesn't provide, so fleet tests run on Azurite only. Tier 3 "between any two object stores" needs an S3 target in CI. Find out whether RustFS supports `AssumeRoleWithWebIdentity` (or any keyless STS) against the CI's test issuer. If it does, use it; if not, propose the smallest honest alternative. **No static keys in the server, ever.** A test-only credential path needs an ADR stating why and confining it to test assemblies.
>
> ## B. Definition of done (from #97, with the amendments)
>
> - **Tiers 1 and 3 under failure injection:** a follower equals the reference model at its reported position.
> - **Tier 2** on a real account in the optional leg.
> - **Handover over generated schedules** of abdications, probe-triggered takeovers (including false ones), partitions, concurrent candidates and late triggers:
>   - no acknowledged commit is lost;
>   - positions are contiguous, and each is written once;
>   - the header chain verifies across epochs;
>   - the superseded writer appends nothing after the fence;
>   - no old-epoch trigger changes the writer;
>   - `server-busy` never causes a handover;
>   - a write forwarded across a handover commits exactly once, or fails with `writer-moved` and no commit.
> - **Takeover time** measured on Azurite and RustFS for both triggers, and **write downtime on restart** in the two-app layout.
> - **The promotion rehearsal** in every tier, with the returning old primary refused.
> - **Tier 3 byte identity:** the target's log is byte-identical to the source's up to the shipped position.
> - **`varve storage verify`** passes against Azurite and RustFS, and fails with the counterexample against the two broken in-memory stores of #117.
> - **The close-out** in the last part: README Status, `docs/roadmap.md` (Replication complete), AGENTS.md's State, `eng/status.cs`, the changelog, the operator guide (standbys, takeover automation, epoch history, shipping, promotion, the compatibility page), `docs/problems/`, and the traceability records.
>
> ## Plan questions
>
> 1. The split into two parts, with each part's scope and issue.
> 2. What of tier 1 is left after #134, and the warm flag.
> 3. ADR 0179 as filed: what changes from the draft, and how forwarding and the writer directory behave across a handover.
> 4. The automation: the Kubernetes sample, the Azure mechanism for the N-single-replica layout, and its cost.
> 5. Tier 3: the shipper's role and configuration, what's shipped and in what order, resume, and the shipper as a reader under ADR 0160.
> 6. Promotion: the lineage marker, where it lives, and how the returning old primary is refused.
> 7. Tier 2: the follower over the secondary endpoint, and lag from last-sync time.
> 8. The RustFS gap (point 10).
> 9. ADR numbers from 0179, and amendments (at least 0136, 0160, 0164 and 0168).
>
> ## How this lands
>
> - **Each part is one PR,** red only on `CS0618`, with its own issue and descriptor at the next free `0.1.0-preview.N` (ADR 0102).
> - **The descriptors** carry `accepted-by: mailto:emil@okkels-klein.dk` and a `Closes` trailer for their part's issue. The last part's descriptor closes #97, and part 2's closes #117. Use bare #N elsewhere, and no closing keyword in PR descriptions (#108).
> - **Commits:**
>   - signed with the agent SSH key, as `Claude (Emil) <claude+emil@okkels-klein.dk>`, the repository's rule regardless of any generic hook;
>   - reference #97 (and #117 where relevant), with the DCO sign-off;
>   - never name a model.
> - **A `docs/traceability/` record** per part (ADR 0033).
> - **No suppression or downgrade of DD or VARVE rules.**
> - **CI images** only from `.github/ci-images.yaml`. A new image needs a row, a mirror run, and the maintainer making its package private.
> - **Test time:**
>   - size land-gate iteration counts, and put full depths in the nightly workflow (`docs/testing.md` §8);
>   - report durations per OS (#126);
>   - put the fleet and shipping runs in their own named jobs.
> - **Land through `land/`.**

### The plan, as submitted

Posted on #97 before any code:
<https://github.com/Hafeok/Varve/issues/97#issuecomment-6436808419>.

### The maintainer's answers

> Approved, with these answers and changes. Open the two part issues and start part 1 with the RustFS spike.
> Q1: a separate `varve:standbyOn`. `varve:placedOn` keeps `maxCount 1` unconditionally and means the preferred writer. The writer set is `placedOn` ∪ `standbyOn`, and `standbyOn` requires `varve:automaticHandover true`.
>
> * A fresh or released dataset is claimed by `placedOn` alone, with no race.
> * A change of `placedOn` is a placement event: only the new `placedOn` claims.
> * Adding or removing a `standbyOn` is an event of its own.
> * After a takeover, the holder may differ from `placedOn`, and nothing reacts to that state. Amend ADR 0166 and `shapes/management.ttl` accordingly.
>
> Q2: the stricter rule. Shipping requires a verification record at the target, written by `varve storage verify` run against that exact endpoint under a suite version at or above the build’s minimum, plus the provider being verified on the compiled page. Your `LosesRaces` store passes the probe and fails the race property, so the probe and the `Server` header can’t stand in for verification. Directory targets are exempt.
> Q3: yes, the system dataset gets handover in part 1.
> Q4: an alert, then a Consumption Logic App, then the Container Apps job. Don’t use the action group’s “Logic Apps” action, because it calls the trigger’s SAS callback URL. Use Secure webhook (Entra). On the Logic App, disable SAS (`accessControl.triggers.sasAuthenticationPolicy.state: Disabled`) and add an OAuth authorisation policy that accepts only the action group’s service principal. The Logic App’s identity may only start the job, and the job’s identity holds Operator. The Entra app registration for the secure webhook is an operator-supplied prerequisite in the README, identified by id (the perf ADR’s rule). Also compare a metric alert per app against the log search alert for detection latency, and state the measured end-to-end detection time on the optional leg; #164 measures it on real Container Apps. Confirm the prices against the pricing pages when you write the Bicep.
> Q5: MinIO CE is untested, with the note that the community edition is no longer distributed as binaries. Keep four states. Unsupported means known to break the contract, as Garage does.
> Changes:
>
> 1. Drop `commit-path-failure` as an abdication reason. A projection or validator fault that is deterministic over the log fails identically on every standby, which would give a handover storm with no timer to stop it. Such a fault fails the commit (`500`), and the writer stays. Abdicate on `storage-error` only, and on a fault you can show is local to the instance; if you propose one, state the test that tells the two apart.
> 2. Warm means the default projection has caught up with what was applied, plus your other conditions. Validation projections are not part of warm, because gating doesn’t read them (ADR 0169) and ADR 0174 keeps their lag off readiness. Report binding lag next to `warm`.
> 3. Add a property that a claim in place and a re-open from the catalog give equal datasets at every position after the claim.
>
> Everything else stands as planned, including ADRs 0179–0186. Inference starts at 0190 and the Web UI at 0200. I’ll make the kubeconform package private after its mirror run.

## Report

### The RustFS spike (point 10), first

- **RustFS 1.0.1 implements `AssumeRoleWithWebIdentity`**, and accepts a
  token of mock-oauth2-server 6.0.4 configured as its OIDC provider: issuer
  discovery (full metadata, so rustfs/rustfs#7330 does not apply), audience =
  the configured client id, and a flat `groups` claim naming the `readwrite`
  policy. No role ARN is read. A forged token is refused `403`.
- **Three things it needs**, each found in the spike and stated in
  `.github/storage/compose.yaml`:
  - the issuer is discovered once at start, three attempts within
    milliseconds, so RustFS must wait for the issuer to be healthy;
  - `RUSTFS_OUTBOUND_ALLOW_ORIGINS` must list the issuer's private origin
    (rustfs/rustfs#8023);
  - the token's `iss` must be the address RustFS discovered. mock-oauth2
    takes `iss` from the request's host, so the tests ask for the token under
    that host.
- **The server could not use it**: it built its S3 options without
  `StsEndpoint`. It now reads `Varve:ObjectStorage:StsEndpoint`, and the
  command line takes `--sts-endpoint`. ADR 0137 is amended (dated, unaccepted),
  which also records that the server process never took a key, loopback
  included.
- **The gate:**
  - the object-store contract, races included, passes on RustFS signed only
    by STS credentials (7 cases);
  - the fleet suite, all six cases, passes with its server processes on a
    RustFS bucket by web identity, beside the Azurite run;
  - one fix was needed in the fleet's counting proxy: it now keeps the
    `Host` that SigV4 signed.
- **So no fallback ADR is needed.** ADR 0182 (the in-process S3 leg) is not
  filed, and its number stays free.

### The maintainer's second message

> Keep the loopback commit. In the small-machine guide, add one paragraph: a single machine uses the file backend or a directory, and S3 is for setups with an OIDC issuer (link to the RustFS compose settings as the worked example). Keep the trailer as Co-Authored-By: Claude <noreply@anthropic.com>; the model is named in the traceability record only. Carry on with ADR 0179 and the remaining tier-1 work.

### ADRs 0179 and 0180, filed

- **ADR 0179, writer handover within a store,** is the draft as approved,
  with these changes:
  - abdication is for `storage-error`, and for `cache-error`, a fault of the
    instance's own cache directory. That second reason is proposed with the
    test that tells it apart from a fault deterministic over the log, which
    fails the commit and never hands over;
  - the takeover is answered by the instance that receives it;
  - the claim happens in place, held equal to a re-open by a property;
  - a superseded writer rejoins as a follower;
  - forwarding across a handover answers `writer-moved`;
  - the system dataset hands over too.
- **ADR 0180, warm standbys and the writer set,** states:
  - `placedOn` ∪ `standbyOn`;
  - placement as events, and the start rule;
  - standbys that follow on the interval;
  - warm, as the default projection caught up, with binding lag beside it.

  It asks one question on the pull request: who claims when the preferred
  writer itself abdicates. Read alone, the maintainer's rule leaves that
  release to the instance that made it.
- **The rest of the filing.** `FollowOnDemand` moves from ADR 0164's set to
  0180's, restated, so its one citation is `CS0618` until accepted. ADRs
  0136, 0163, 0164, 0166 and 0168 are amended by dated blocks.

### The rest of tier 1

- **The writer directory across a handover.** The object-storage follower
  reports the highest epoch it adopted (`ObjectStorage.AdoptedEpoch`), and the
  route is read again when it rises. `NoWriter` is not cached. A forward that
  a superseded or stepped-down writer refused is answered `writer-moved` with
  that reason. The catalogue gains `writer-stepped-down`.
- **Positions.** A sweep of every data-plane route on a follower showed the
  code already met ADR 0096 point 4: every response that touched the dataset
  names a position. Readiness now names each dataset's applied position.
- **ADR 0164 point 5 is built.** `Dataset.ReopenDue`, and the reconcile
  round's re-open. The memtable's count is two entries per quad in the tests'
  datasets, not one, so the rule is stated in the measure the writer's flush
  compares with `MemtableLimit`. `MemtableLimit`'s own documentation, "quads",
  may overstate what it counts. That is noted here and not changed.
- **Tier 1 under failure injection** (`ObjectFollowerFaultTests`) found two
  pre-existing defects. Each is fixed with a test that fails without its fix:
  - a follower stayed a commit behind after an advance whose scan failed once
    its refresh had adopted the commits;
  - an open for write failed (`ArrayBufferWriter` asked for no capacity) after
    writers that claimed and closed with no record between them. Handover
    makes such runs of claims routine.

  2,000 schedules ran clean in about 60 s. The land gate runs 60, and the
  nightly run 2,000.

### The maintainer's third message, and what followed

> Good work, and the two bugs are exactly the failures handover would have made routine.
>
> 1. ADR 0180 point 4: warm standbys race, settled by the release reason:
>    * `placement`: only the new `placedOn` claims;
>    * `shutdown` or `storage-error`: warm standbys race, and the store’s conditional write picks one;
>    * a returning `placedOn` starts as a standby under the start rule.
>  State in the operator guide that a rolling deploy moves the writer to a standby on each restart.
> 2. Narrow `cache-error` to `local-disk-error`: a failure of local storage the commit path needs (memtable spills, if they’re local). A failed derived-cache write degrades: caching off for that kind, reported on readiness, writer stays. That’s #146’s rule that the cache is a miss, never a stop. Add a property that a failing cache write never abdicates. Keep the deterministic-fault test as you have it.
> 3. `MemtableLimit`: correct its documentation to what it counts, with the approximate bytes per unit. Expose it as `Varve:Store:MemtableLimit` (ADR 0115’s configuration rules, `configure.md`), since follower memory is something operators on small replicas need to bound. Then test the re-open through the server with a low limit.
> 4. Open a draft PR now, so the RustFS web-identity leg and the fleet jobs run on GitHub before the handover work builds on them. Report the first run’s job durations per OS.
>
> Then continue with the handover.

- **The fence defect was fixed on `main` too.** Main fixed it independently as
  #144. The merge takes main's version, and this branch's test stays.
- **`local-disk-error`.** On the object-storage backend no commit writes a
  local file. The memtable's spills are local, but maintenance writes them off
  the sequencer, and a failed one leaves the memtable growing, unreported.
  The ADR makes a failed spill degrade, and abdicates at the commit that takes
  the memtable past four times `MemtableLimit`. That bound is proposed.
- **`MemtableLimit` counts entries:** a quad, or a term the dictionary adds.
  Measured with a GC heap delta over 20,000 quads, an entry is about 215
  bytes for a quad and about 90 for a short term.
- **The draft PR's first CI run, 417,** stopped every job at the build, on the
  one expected `CS0618`. So no test ran, the fleet's and the RustFS leg's
  included. Its compose did start: the issuer was healthy, and the STS wait
  passed. The citation came from moving `FollowOnDemand` out of ADR 0164's
  accepted set. It was restored instead: 0164's ruling stands for a follower,
  and ADR 0180 adds `StandbysFollowOnTheInterval` for a standby, cited from
  the standby's code when that is written. So the branch builds green until
  the handover code cites an unaccepted ruling.
