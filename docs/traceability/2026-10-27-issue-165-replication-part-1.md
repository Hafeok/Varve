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

### Run 420: the first run that tested

Run 420 (bed8fca) built on every OS. It waited in the queue behind other
pull requests' runs before its jobs started. **The fleet job passed, on
Azurite and on RustFS by web identity.** Three jobs failed, all on one test:
#143's guard, which wants every class of the objects namespace to carry one of
two traits, and four of this branch's classes carried none. The test-server
job never got to the takeover test, which read the release in the CLI's old
spelling; that would have failed too. Both are fixed in 2601679.

Durations per OS, in minutes:

| Job | ubuntu | windows | macos |
|---|---|---|---|
| build (the `test-store` failure) | 9.1 | 13.8 | |
| pipeline (devcontainer; stopped at `test-store`) | 11.9 | | |
| storage (services; the same failure) | 5.3 | | |
| **fleet (services)** | **2.3** | | |
| conformance | 2.8 | 4.0 | |
| native aot | 2.7 | 4.7 | |
| repo-standard native and action | 1.1 | 1.5 | |
| durability | 0.3 | 1.1 | 0.6 |
| auth (mock-oauth2 / zitadel) | 2.3 / 2.6 | | |
| samples / samples (eventhubs) | 2.0 / 2.1 | | |
| browser wasm / storage in Chromium | 1.9 / 2.5 | | |
| decision report | 2.7 | | |
| container (x64 / arm64) | 4.4 / 3.0 | | |
| rec generator (x64 / arm64) | 1.5 / 1.2 | | |
| aspire sample | 1.8 | | |
| dco, release pending, bicep | 0.3 each | | |

The local store suite also turned up a flaky case from `main`: the
concurrent-claims fence case failed 2 runs in 40 there, and 6 in 40 here. It
assumed every epoch in the history is still held, but a claimant can win an
epoch and be fenced by the next before its own fence lands. It is fixed in
the same commit.

### The server's writer set (ADR 0180)

- **Roles.** `standby` is a dataset role beside `writer`, `follower` and
  `elsewhere`. A dataset with automatic handover and a `standbyOn` is
  converged by events between the declarations applied, as point 2 says.
- **The start rule** (point 3). A member claims only from an empty or
  released record when it is the preferred writer, from its own record, or
  from a holder outside the set. A holder named by no declared member, where
  the preferred writer is the system writer's, is taken for a member and not
  fenced.
- **Standbys** tail on the interval, inside the round that already runs on
  it, and read the writer record whole each round. The ADR said "conditional
  GET"; the store contract has five operations and no conditional read (ADR
  0134), so point 5 now says so. The cost stays three GETs.
- **After a release**, `placement` is claimed by the preferred writer alone,
  while `shutdown`, `storage-error` and `local-disk-error` are raced by every
  warm member. The race is a claim in place guarded by the record's epoch.
  A loser opens again as a standby. A superseded holder rejoins as a standby.
- **Readiness** carries `tailing` and `warm` for a standby.
- **The fleet case:** a rolling restart. a holds d with b as its standby; a
  is stopped by SIGTERM; b, warm, claims in place; a comes back as b's
  standby and forwards to it. Over three runs, takeover after the shutdown
  release took 188–288 ms on Azurite and 212–316 ms on RustFS, at the
  suite's 200 ms follow interval. Fleet 16/16, server 151 (6 provider tests
  skipped locally), protocol 119.
- **Not yet cited:** the rulings of ADR 0180 are cited in the last commit,
  because a citation of an unaccepted ruling is `CS0618`, which stops every
  CI job at the build.

### The system dataset's writer set, and the takeover route

- **The system dataset** hands over with `Varve:Fleet:AutomaticHandover`:
  `Varve:Fleet:Writer` is then its writer set, the preferred writer first.
  It takes over on a shutdown release in 632–1004 ms, over three runs on each
  store. That is slower than a data dataset because `_fleet` is released
  last on shutdown.
- **The takeover route**, `PUT /manage/datasets/{d}/state` with
  `{"takeover":{"epoch":n}}`, `_fleet` included. It is answered by the
  instance that receives it, authorised as an operator at its applied
  position, and never forwarded. A refusal is `409 takeover-refused`, with
  `reason` and `epoch`.
  - **One change to ADR 0179 point 6:** `epoch-moved` is checked before
    `cold`. The winner of a takeover is no longer a standby, so a repeated
    trigger sent to it would otherwise read as `cold` rather than the
    no-op it is.
  - **Kill to the first write through the standby** took 397–560 ms on
    Azurite and RustFS, and 617–842 ms for the system dataset's first
    management write, over three runs each.
- **Model types.** The analyzers refused the outcome type in `Varve.Protocol`
  (DD0010) and a naked `long` epoch (DD0013). The design change took a
  `TakeoverEpoch` wrapper and `TakeoverOutcome` in `Varve.Protocol.Model`.

### The maintainer's messages since, and the fourth set of answers

> what are we waiting for ?

> carry on with the schedule tests while CI runs

> is there anything left to do for  you ?

> great give me a full report of what you have done and what you have left todo, details on the things i need to decide on

> Good progress.
> D1: I’ll accept 0179 and 0180 on the PR once you’ve applied D2 below. Then cite as you go; there should be no red phase.
> D2:
>
> 1. `local-disk-error`: release at whichever comes first: 4× `MemtableLimit`, or the GC’s memory load (`GC.GetGCMemoryInfo()`, cgroup-aware) reaching its high-load threshold while spills are failing. Both are checked at commit, never on a timer. That way a small container releases cleanly before the out-of-memory killer acts. Test both arms in `HandoverAbdicationTests`.
> 2. `epoch-moved` before `cold`: agreed.
> 3. The writer record read whole: agreed. Keep `IObjectStore` at five operations, and correct ADR 0180 point 5’s text to say so.
>
> D3: answered by D1.
> D4: the release descriptor’s `accepted-by: mailto:emil@okkels-klein.dk` is yours to write (`docs/releases.md` point 6). “Never write `accepted-by`” applies to decision sets only. My earlier instructions blurred the two.
> D5: active-active stays in part 1, after ADR 0181: the plan’s four-process property and its docs. If it needs new server code beyond placement and forwarding, it becomes its own PR then.
> Also:
>
> * The concurrent-claims assertion: in the PR, quote the old assertion and the new one, and say why the old was wrong and why the new one isn’t weaker. If it was hiding a real race, fix the race instead.
> * Takeover times: write them to the `handover (services)` job summary per run, as the plan said, beside the two-app restart downtime once ADR 0181 measures it.
> * `ShapesBindingTests`: run the stress comparison against `main` I asked for, and record the result on an issue either way.
>
> Your order is fine: items 1 and 2, then ADR 0181 and the automation, then active-active, then `writer-stepped-down` and the descriptor.

- **D2.1.** The spill bound is now the store's, decided at the commit that
  crosses it: `Dataset.SpillBound` names the measure that was reached,
  `Memtable` or `MemoryLoad`, and stays reached. The server releases on it.
  `HandoverAbdicationTests` tests each measure on its own, and the commit
  that crosses it and not one before.
- **A follower now meets a projection fault as the writer does.** Writing the
  projection-fault case showed that a follower's apply ignored the fault: a
  fault "deterministic over the log" was not, for a follower. Its apply now
  fails its projection at the position, as the writer's does (spec §7), and
  its log goes on. ADR 0179's text said such a fault "fails the commit with
  `500`"; under §7 the commit stands and the dataset fails, and the text now
  says so.

### The maintainer's fifth message

> D2 looks right. I’ll accept 0179 and 0180. The projection-fault correction to spec §7 is right too.
>
> The fence race is the important finding. Split 0119abc, with its deterministic test and the new exact-equality assertion, into its own small PR from main, closing #181 with a Closes #181 trailer on its commit. It should land ahead of the rest of #175, because every dataset’s writer fence depends on it. Then merge main back into #175.
>
> #183 stays yours to note; the free session will fix it, and I’ve added the empty-report diagnosis to it.
>
> Then continue: the placement-events property, ADR 0181 and the automation, active-active, writer-stepped-down and the descriptor. Cite as you go once the ADRs are accepted.

- **The fence fix, apart.** #185 carries 0119abc from `main`, with its own
  record (`2026-10-28-issue-181-a-later-epoch-in-the-listed-log.md`).
  `MemoryWriterFenceTests` ran 1,035 times there with the fix, up to six
  processes at once, and all passed.
- **Placement as events (ADR 0180 point 2).** What a member of a writer set
  does at a reconcile is now one pure function, `OpenDatasets.Decide`, over
  the declaration it applied before, the one it applies now, its state and
  the writer record. `PlacementEventTests` runs it for three members over
  generated histories of declarations and takeovers. A holder abdicates
  only in the round its `placedOn` changed, quiet rounds move no epoch,
  and a release for placement is the new `placedOn`'s alone. Three mutants
  each fail it: abdicating whenever the holder is not preferred, opening a
  placement release by any member, and letting a standby claim one. Its
  full depth, 20,000 histories, takes under a second, so it is also the land
  gate's count.
- **The two-app layout's write downtime on a restart** is now measured
  beside the takeover times, by the handover (services) job. The case has
  a system writer with no standby and a follower forwarding. The writer stops
  and starts again on its cache, and the downtime runs until the first write
  the follower forwards. Locally, three runs each: Azurite 2,568–3,221 ms,
  RustFS 1,998–2,264 ms, against 504–908 ms for a takeover. The process start
  is included; a platform's own restart time adds to it, and #164 measures
  that on real Container Apps.

### The maintainer's sixth message

> For a forwarded write whose outcome is unknown (the store’s response to the create was lost), answer the client with readiness A’s new problem type commit-outcome-unknown (ADR 0214, on #180’s step-3 branch), with position where known and requestId, not a separate 503 unavailable. One type for one situation. Merge it in when it reaches main.

- Noted. ADR 0214 is not on `main` yet. The writer's answer to an append
  whose outcome is unknown (`Dataset.AppendFailed`, the storage-error
  abdication) moves to `commit-outcome-unknown` in the merge that brings it
  in.

### ADR 0181, filed, and the takeover command

- **ADR 0181** is filed unaccepted with its decision set,
  `handover-automation`. It records the maintainer's answers: Q4's chain,
  and plan point 4's Kubernetes sample and Azure layout. It adds what the
  chain needed:
  - a fixed job per app, so the Logic App passes nothing;
  - a custom role for the Logic App. The built-in Jobs Operator role also
    grants `listSecrets`. A start may override the job's template, so the
    OAuth policy is the trust boundary;
  - `--managed-identity` for the CLI.

  The prices are from the Azure retail prices API, West Europe,
  pay-as-you-go, because the pricing pages render theirs in script. The
  Bicep's README confirms them against the pages.
- **Two citations could not be confirmed.** The maintainer called the rule
  that an operator-supplied prerequisite is identified by id "the perf
  ADR's". No ADR on this branch has it, so 0181 attributes it to the
  maintainer's answer and says so.
- **`varve manage takeover`** is the automation's one act. A fleet case
  kills the writer, a, of two datasets with different standbys, and runs
  the command naming a, c and b. It skips a, which is dead, and moves each
  dataset to its warm standby. It leaves a dataset with no handover, and the
  system dataset without `Varve:Fleet:AutomaticHandover`, to a's restart. Run
  again, it does nothing and exits 0. It passes on Azurite and RustFS.

### The Kubernetes sample (ADR 0181 point 8)

- **`deploy/kubernetes/`** holds the fleet with handover (`fleet.yaml`) and
  the controller (`handover.yaml`).
- **The `kubernetes sample` CI job** runs kubeconform, strict, against
  Kubernetes 1.34.0, with the schemas pinned to a commit
  (89a35a5e9a1412a692a2603d79c3786f602238a4) rather than their `master`.
  - Locally, all 12 resources are valid.
  - A misspelt `servicename` in the StatefulSet makes it invalid (1 of 5).
  - Its image is a new row of `.github/ci-images.yaml`, so the job is red
    until the maintainer runs the mirror and makes the package private.
- **The official `kubectl` image has no shell**, so the controller can't
  run on it. The sample pins `alpine/k8s` by digest, and says any image
  with `kubectl`, `jq` and `bash` serves.
- **Checked at development, not in CI** (both run in that image):
  - The controller's `jq` makes a Job from the CronJob that kubeconform
    finds valid. Its arguments name the failed pod, then the other two pods'
    addresses, then the template's own.
  - Against a stub `kubectl` replaying pod events, the watch started one Job
    for each `Ready` that turned from `True` to anything else, a deletion
    included. It started none for a pod that was never ready, nor for a
    repeated `False`.

### The maintainer's seventh message

> #185 looks right; I’ll approve 80a80f6 once CI is green. After it lands, merge `main` into #175, and add the log listing to the in-place claim when the newest segment is consolidated, with its test.
> On #175:
>
> 1. The Kubernetes controller: go back to the plan’s C# controller (raw `HttpClient`, the pod’s service-account token, source-generated JSON), built into Varve’s own GHCR image and calling the same takeover logic as `varve manage takeover`. No `alpine/k8s`: a community Docker Hub image holding RBAC over the writer is the wrong place for a third-party dependency. If the C# controller hit a blocker, tell me what it was.
> 2. Restart against takeover: put the measured 2.0–3.2 s restart downtime and the 0.3–0.9 s takeover in the operator guide, beside the choice of layout.
> 3. The perf ADR is a project draft that #164 files. Your credit in 0181 is right for now.
>
> I’ll mirror kubeconform, make the package private, and add `kubernetes sample` to the required checks once it’s green. Then carry on with the Azure Bicep, active-active, `writer-stepped-down` and the descriptor.

- **The controller is C#**: `varve manage watch-kubernetes`, in Varve's
  image.
  - It lists the fleet's pods, then watches them, over a raw `HttpClient`
    that trusts the service account's CA, with the service account's token
    read at each request.
  - On a `Ready` that turns from `True`, it calls `Manage.TakeOverFromAsync`,
    the code `varve manage takeover` now shares. The operator token's
    options are shared too, as `Manage.OperatorToken`.
  - `handover.yaml` keeps the service account, a role that reads and
    watches pods, and one Deployment. The CronJob, the ConfigMap and
    `alpine/k8s` are gone.
- **One blocker: the source-generated JSON.** In a packable project, the
  System.Text.Json generator's metadata code calls `Type.GetConstructor`
  and `Type.GetProperty`, which ADR 0064's banned symbols refuse: 19
  `RS0030` errors. Only test and `eng/` projects use a
  `JsonSerializerContext` today. The controller reads the API server's JSON
  with `JsonDocument`, as every JSON reader in `src/` does; that needs no
  reflection either. ADR 0181 point 8 says so.
- **The fleet case** runs the controller's watcher against an API server
  stood up at the process boundary. That server serves a list, then
  streams events. On Azurite and RustFS:
  - a, killed, turns not ready; the controller moves d to b and `other` to
    c;
  - a repeat, and a pod first seen not ready, start nothing;
  - an `ERROR` event makes it list again;
  - every request carries the token file's token.

  A mutant that takes over on any not-ready event fails it.
- **The operator guide's layout choice** now holds the measured table:
  restart 2,568–3,221 ms on Azurite and 1,998–2,264 ms on RustFS, against
  0.3–0.9 s for a warm standby. It says what each figure includes and
  excludes, and the Container Apps page points to it.
- **ADR 0181 point 6's signal does not work as written.** Azure Monitor's
  metric reference describes `RestartCount` as "the cumulative number of
  times the replica has restarted since it was created". A static threshold
  above 0 therefore fires at a replica's first restart and stays fired, so
  later failures start no takeover; a metric alert cannot say "increased".
  The alert's kind also decides how the Logic App learns the failed app:
  a metric alert names the app as its target, while a log search alert
  names the workspace and carries the app as a dimension. So the choice is
  put to the maintainer before the Bicep's alert and Logic App are written.

### Acceptance, 2026-10-28

A later session (Claude Code, model `claude-fable-5-1`), on the maintainer's
instruction to accept every decision on the open pull requests, added
`accepted-by` and `accepted-at` to the 30 keys this branch files over
main: writer-handover-within-a-store (11 keys, ADR 0179), warm-standbys-and-the-writer-set (7 keys, ADR 0180), handover-automation (12 keys, ADR 0181). The acceptance is the maintainer's, who had read them; the
session only wrote it at their direction, as the acceptance of ADRs
0127–0133 was written. The ADRs' status text and the index rows are left as
filed, as in that precedent.

### The maintainer's eighth message

> ADR 0181 point 6: take (c), the `Replicas` < 1 metric alert per app plus the log search alert on probe failures, into one action group. Record in the ADR and the guide that a crash is recovered by Container Apps’ own restart in seconds (your 2.0–3.2 s). The alerts exist for a replica that’s gone (b, fast) and one that’s running but stuck not ready (a, a 3–10 minute backstop). #164 measures both detection times.
> One required addition: stale triggers. A trigger can arrive after the writer has already recovered by restart and reclaimed. Before requesting the takeover, `varve manage takeover --from X` (and the Kubernetes controller, which shares the code) reads X’s own state:
>
> * X answers ready and holds the dataset: the trigger is stale. Do nothing, exit 0, and log it.
> * X doesn’t answer, or answers not ready: proceed.
>
> Add a fleet test for it: the writer is killed, restarts and reclaims, and then the late trigger arrives and changes nothing. Add the same case to `HandoverScheduleTests` if its model can express a late trigger after a restart.
> The `JsonDocument` decision is fine as recorded in point 8. Carry on with active-active, `writer-stepped-down` and the descriptor in parallel.

- **The maintainer accepted 0179–0181** (f2978d2). Point 6 changed after
  that, so ADR 0181 changes by a dated amendment, as ADR 0062 says.
  - `AzureDetectionIsAMetricAlertPerApp` keeps its key with the new
    statement, and has no acceptance until the maintainer gives it again.
  - The stale-trigger rule is a new key, `StaleTriggerAsksTheFailedInstance`,
    also unaccepted.
- **The stale-trigger check.** The state resource's epoch history now
  carries each claim's `address`.
  - Before it sends a takeover from an instance the record still names, the
    command asks that address for `/health/ready` and the dataset's state,
    within five seconds. For the system dataset it asks the system writer's
    address.
  - Ready and holding the dataset as its writer means the trigger is stale.
- **The fleet case.** a is killed, restarts and reclaims, then the late
  trigger arrives. It passes on Azurite and RustFS, and fails without the
  check: b, warm, takes d.
- **`HandoverScheduleTests` cannot express it.** In the store's model, a
  trigger after the restart reads the restarted holder's epoch and looks
  like a genuine probe. The check is the command's, above the store.
- The server suite passes: 174 tests.

### Citing the accepted rulings, and writer-stepped-down

- **Citations.** `[Contract]` is valid on classes, interfaces and delegates
  only, once per type, so each ruling is cited on the type that embodies it:
  - `WriterSetIsPlacedOnAndStandbyOn` on `Placement`;
  - `WarmIsDefaultProjectionCaughtUp` on `OpenDatasets.Entry`;
  - `ReadinessReportsTheStandby` on `Readiness`;
  - `EveryHandoverIsVisible` on `EpochClaim`;
  - the two Kubernetes rulings on the controller and its `Watcher`;
  - `AutomationIdentityHoldsOperatorWithoutSecret` on `OperatorToken`.

  The member decision moved into `MemberDecision`, which cites
  `PlacementIsAnEvent`, with nested `StartRule` and `ReleaseRace` for their
  rulings. The takeover code moved from `Manage` into `Takeovers`, which cites
  `TakeoverCommandIsIdempotent`. The two rulings the amendment of 2026-10-28
  changed or added are not cited until they are accepted, so nothing is red.
- **`writer-stepped-down` is emitted** (ruling `SteppedDownAnswers`):
  - `Dataset.StepDown(reason)` makes the sequencer refuse every commit not
    yet appended with `WriterSteppedDownException`, an exception at the
    store's boundary cited like `WriterSupersededException`. Nothing of the
    refused commit is written, and a batch being appended finishes;
  - the server steps the writer down when it releases: in `ReleaseFor`, for
    placement, storage-error and local-disk-error, and at shutdown before
    the release;
  - the update and write endpoints answer `503 writer-stepped-down` with its
    `reason`;
  - it left `NotYetEmittable`. The catalogue test triggers it through a
    stepped-down dataset, and `HandoverAbdicationTests` checks the refusal,
    its reason and that nothing was written.
- Suites: store 407, protocol 119, server 174. The one server failure in one
  run was #183's `ShapesBindingTests` flake, which then passed three times.

### The Azure handover layout's template (ADR 0181 points 5–7)

`deploy/azure/container-apps-handover/` was written in a worktree of its
own, alongside the citations, then read and built here. It holds the N
single-replica apps, Front Door Standard, a takeover job per app, the two
alerts into one Secure-webhook action group, the Consumption Logic App and
a custom role for it. `az bicep build` and `az bicep lint` (0.48.1) pass,
with no warnings. Three choices differ from the first sketch:
- **`aud` is the webhook app registration's client id**, not its identifier
  URI: a v2.0 access token's `aud` is always the API's client id, per
  Microsoft's access-token claims reference.
- **Each app's `Varve:Fleet:InternalUrl` is its FQDN**,
  `https://{app}.{defaultDomain}`, without `.internal.`: that name is for
  apps with internal ingress, and an external app's FQDN stays inside the
  environment for the other apps.
- **`Varve:Auth:SubjectClaim` is `oid`**, so the jobs' identity is named by
  the agent IRI the template adds to the bootstrap operators,
  `https://login.microsoftonline.com/{tenant}/v2.0#{principalId}`.

Left for #164's leg, and said so in the README:
- what text separates a readiness failure from a liveness one in
  `ContainerAppSystemLogs_CL`;
- whether `Replicas` reports 0, or nothing, for a replica that is gone;
- the alerts' payloads, live.

The prices were confirmed against the Azure retail prices API, which backs
the pricing pages.

### Active-active within a store (#97 point 9, D5)

- **The plan's four-process property, as a fleet case** on Azurite and
  RustFS: four server processes on one store, as two regions of two.
  - Dataset a's writer set is region 1's and b's is region 2's.
  - Generated schedules hold writes through any instance, the holder's
    crash and restart, its crash and a takeover by its standby, and its
    graceful restart.
  - After every step, each dataset has exactly one writer among the live
    instances, in its own region, and its other member is a warm standby.
  - At the end, every acknowledged write is present, every write present
    was tried, the head equals the writes present (one commit each), and
    every claim in the epoch history is the region's.
- It needed no server code beyond placement and forwarding, so it stays in
  #175.
- 12 schedules ran clean on each store, about 2 minutes each. The land gate
  runs 2, about 20 s a store. A mutant that lets a non-member claim on a
  release fails it on both.
- The operator guide's fleet page has the layout and what it guarantees.
  Active-active across copies is part 2's.

### The close-out

- **`releases/v0.1.0-preview.16.yaml`**, the next free version: basis #165,
  storage and delta format 1, and ADRs 0179–0181 with 0206, which `main`
  holds unreleased. `accepted-by` is the maintainer's, which this session
  writes for a descriptor (`docs/releases.md` point 6; decision sets never).
  `CHANGELOG.md` is its projection.
- README's Status, AGENTS.md's State, the roadmap's Replication section (part
  1 shipped, part 2's scope) and storage format §8.2 (the writer record, which
  the spec left to ADR 0136) are current. `eng/status.cs` passes.
- **Still to do before this lands:**
  - `commit-outcome-unknown` once ADR 0214 is on `main`;
  - the maintainer's acceptance of the amendment of 2026-10-28;
  - the kubeconform mirror.

### #185 merged in, and the in-place claim's listing

> #185 has landed, so the fence covers a consolidated last segment on main. Merge main into #175, add the log listing to the in-place claim for a log whose newest segment is consolidated (it must reach the same verdict as the write-open's fence), and add its test. Rerun HandoverScheduleTests at nightly depth, then post the new head.

- **The merge.** #185's fence replaces this branch's own version of it from
  0119abc: the comparison with `_listedTail` for a consolidated newest
  segment, and `ListedTailEpochAsync`. Both sides' tests in
  `ObjectEpochPropertyTests` are kept, and the nightly sets both
  `VARVE_HANDOVER_SCHEDULES` and `VARVE_FENCE_SCHEDULES`.
- **The listing.** `ObjectSegmentStore.ClaimInPlaceAsync` refreshes by
  probing and holds no listing. When its newest segment is consolidated
  after the refresh, it now lists the log and reads the highest chunk still
  listed, as the write-open does. The listing comes after the refresh, so it
  holds every chunk the refresh adopted that was not deleted since.
- **Its test,**
  `ClaimInPlaceTests.a_claim_in_place_over_a_segment_a_newer_epoch_sealed_and_consolidated_is_superseded_as_a_write_open_is`.
  - The follower wins epoch 2 in the record. Before it fences, b claims 3,
    commits until segment 0 is sealed and consolidated, and stops before it
    opens segment 1.
  - It has two arms: the seal's deletes pending, and done.
  - In both, the follower is superseded and fences nothing in segment 1, as
    #185's write-open case is.
  - With the listing removed, the deleted arm fails with "the follower, of
    epoch 2, fenced the next segment after epoch 3's seal", and the pending
    arm passes, since its refresh adopts the seal itself.
- **`HandoverScheduleTests` at nightly depth:** 1,000 schedules ran clean, in
  73 s.

### Acceptance of ADR 0181's amendment, 2026-10-30

A later session (Claude Code, model `claude-fable-5-1`), on the maintainer's
instruction, added `accepted-by` and `accepted-at` to the two keys of
handover-automation that the amendment of 2026-10-28 filed unaccepted:
`StaleTriggerAsksTheFailedInstance` (point 2's stale trigger) and
`AzureDetectionIsAMetricAlertPerApp` (point 6's signal). The acceptance is
the maintainer's, who had read them; the session only wrote it at their
direction, as the acceptance of 2026-10-28 above was written. The
amendment's status text and the index row are left as filed. With it, the
two keys may be cited (ADR 0066).

### The citations, main merged in, and #175 made ready

> Add the citations now, in one push with the remaining correctness work, since #175 isn't ready to land yet: 1. Merge main (bringing #185 if it isn't in yet) and add the in-place claim's log listing for a log whose newest segment is consolidated, with its test, and rerun HandoverScheduleTests at nightly depth. 2. Cite StaleTriggerAsksTheFailedInstance on the stale-trigger check in Takeovers, and AzureDetectionIsAMetricAlertPerApp where the Azure layout carries its citations (the Bicep's README, if no code carries it). 3. commit-outcome-unknown: if ADR 0214 (#190) reaches main before you push, include it. Otherwise file a follow-up issue to switch the forwarded-write answer once 0214 lands, link it in the PR body, and don't wait for it. 4. Update the PR body's "Still open", renumber the descriptor if preview.16 is taken by then, mark the PR ready, and post the new head.

- **The merge (a40bb1c).** #185 and the in-place claim's listing were in
  already (01d11b9, db9b4bf). `main` had since moved by ADR 0213, #146's
  verified derived data, and ADR 0206's acceptance. The conflicts were
  lists both sides appended to: the nightly's variables, and the Protocol
  and Store API baselines.
- **ADR 0213's status (d092fbd).** preview.16 is the first release to ship
  0213, and the release check accepts a basis ADR only if its status reads
  Accepted. 0213's decision set was accepted in 70d991c, but its status line
  still read "Filed unaccepted". At the maintainer's direction it now opens
  "Accepted — filed unaccepted by …", as the others do, and names
  70d991c. Nothing else in 0213 changed.
- **The citations.**
  - The stale-trigger check moves into `Takeovers.StaleTrigger`, which
    carries
    `[Contract(typeof(HandoverAutomation.StaleTriggerAsksTheFailedInstance))]`,
    since a type carries one contract.
  - No code carries the Azure signal, so `AzureDetectionIsAMetricAlertPerApp`
    is named in the handover layout's README and beside the alerts in its
    `main.bicep`.
- **preview.16**, still free on `main`:
  - its basis gains ADR 0213;
  - its summary gains #146's verified derived data, under Changed, and
    derived format 6, under Upgrading, since they ship with it;
  - `CHANGELOG.md` is regenerated.

  #179 also proposes preview.16 and renumbers if this lands first.
- **`commit-outcome-unknown`:** ADR 0214 is not on `main` (#190 is open).
  #210 switches the forwarded write's lost answer to it once 0214 lands.
- **The note on #165 about `TermDictionary`'s caches** (from #190's step 3).
  A follower caches only what it adopted from chunks that exist in the log,
  and the claim's refresh runs before its fence. So every id it holds is in
  the new writer's history. The claim-in-place property already checks
  this: at every position after the claim, it compares every quad, rendered
  as terms, with a dataset re-opened from the catalog. A cached id read back
  as another term would fail it. No change is needed.
- **Measured after the merge:**
  - `HandoverScheduleTests` at nightly depth, 1,000 schedules, clean;
  - `ClaimInPlaceTests` at 1,000 schedules, `ObjectEpochPropertyTests`,
    `HandoverAbdicationTests` and `CacheFaultTests`: 16 of 16;
  - the fleet suite on both stores, 34 of 34.

  One whole-suite run of the server tests failed once. A fleet case's
  dataset declaration hit the 30 s HTTP timeout while the suite ran in
  parallel. On its own, it and the fleet suite passed.

### #190 merged in, and the forwarded write's lost answer

> ADR 0214 is on `main` now (#190 landed at eb5d451a), so `commit-outcome-unknown` is available for the forwarded write whose answer was lost, and `main` carries #180, #200 and #190 since this branch's last merge. A merge of `main` into this branch conflicts in seven files, and four of them are this branch's own design territory, so the merge is left to this session […]. Still open on this branch after that: the forwarded write's answer, `503 commit-outcome-unknown` with `requestId` and no `position`, in place of `503 unavailable` with `writer-unreachable`, as the record says.

- **The merge (05cb4846).**
  - **`Sequencer.cs`.** Main's failed append, settled member by member from
    the log, is taken whole. This branch's abdication flag goes on both of
    its paths:
    - a failure that is not the storage's waits for a reopen, and abdicates
      unless the writer was superseded, as before;
    - a storage failure abdicates for `storage-error`, unless the disk was
      full, which recovers in place at the next write (ADR 0214 point 2).

    Main's private `AppendFailed(Exception)` is renamed
    `RecordAppendFailure`, since this branch's public `AppendFailed` is the
    flag the writer set reads.
  - **`Follower.cs`.** Main's `AdoptClosedAsync(upTo, …)` is the one helper.
    The follower's read and the claim in place call it unbounded. It keeps
    this branch's projection fault at the position, so main's writer, which
    adopts its own unacknowledged commits after a failed append, takes the
    same rule.
  - **The rest.** `DatasetHealth` carries both sides' fields. The catalogue
    test has the `stepped`, `full` and `unflushed` datasets. The API
    baselines are additive.
- **ADRs 0214 and 0215's status (f3fbd275).** Like 0213's: both were
  accepted in 97b39fee, and preview.16 is the first release to ship them. So
  each status line now opens "Accepted — filed unaccepted by …" and names
  97b39fee, as the maintainer directed for 0213. Nothing else in either
  changed.
- **The forwarded write.** `Forwarding.ForwardAsync`'s unreachable branch now
  answers `503 commit-outcome-unknown`, with `requestId` and no `position`,
  cited `OutcomeUnknownIs503WithTheRequestId`. A connection refused before
  any byte went is answered the same: this instance can't tell it from a
  lost answer. The tests and the operator guide follow. `EventMoves`' own
  forward of a declaration move to the system writer still answers `503
  unavailable`, reason `writer-unreachable`. It is a step of the move's
  resumable protocol, not a client's write, and is left for the maintainer.
- **preview.16** gains ADRs 0214 and 0215 in its basis, and notes for #190's
  failed-batch answers and full-disk recovery, the soak gate, the forwarded
  write, and #200's quad source fix (ADR 0022, amended).

### `handover (services)` red on 138ce40: a failed claim kept its epoch

`A_writer_whose_batch_fails_against_the_store_abdicates_and_the_warm_standby_takes_over`
failed once in CI on Azurite: "b did not hold d as writer". b's log showed
"did not open in its new role … `_followers/d/local` is open elsewhere".
Locally it passed alone 20 times. It failed 2 runs in 12 with three copies
running at once, a CI runner's load.

**Read from the record at the failure:**
1. After a abdicated for `storage-error`, a's own new standby was warm in
   time. The ruling `ClaimAfterAReleaseByItsReason` has every warm member
   race, a included, and a claimed epoch 2. Its writer record PUT went
   through the test's proxy, which fails only `log/` PUTs.
2. a's fence then failed: "Creating `d/log/…/00000003` failed 6 times". So
   the claim failed holding epoch 2.
3. b, which had read the release at epoch 1, lost its claim with the epoch
   moved.
4. Each loser then reopens as a standby. That met its own cache directory's
   lease, because `SwitchAsync` stages the new open beside the old one, and
   storage that has taken an epoch holds that directory as a writer.
5. Nothing ever released epoch 2, so the record named a writer that could
   not write.

**Two defects, both fixed within the accepted rulings:**
- **A claim that fails after winning the record releases it.**
  `WriterSet.ClaimInPlaceAsync`'s failure path marks the epoch to release
  for `storage-error` (ADR 0179 point 6) when the standby opens again.
- **A standby whose storage took an epoch in place is closed before its
  next read-only open,** as a file dataset is. That covers a claim, or a
  claim failing after the record. `Entry.OpenedToFollow`, carried across
  `Take`, marks an open in the followers' directory. A plain follower's
  reopen past `MemtableLimit` is still staged beside the old one, so reads
  go on. A first version closed it too, and its fleet case caught the gap.

**The test,**
`A_claim_that_fails_after_winning_the_record_releases_its_epoch_and_another_member_claims`:
- a, alone, writes through the failing proxy, so its own standby claims and
  fails.
- The record must show a's later epoch released for `storage-error`.
- b, started then, claims and writes.

Each fix removed on its own fails it on both stores. With both fixes:
- the old case passed 24 runs in 24 under the same load;
- the CI job's handover set, three at a time, passed 9 of 9;
- the server suite passed 183, with 6 skipped.

**For the maintainer:** whether the member that released for `storage-error`
or `local-disk-error` should race its own release is the ruling's to say. As
it stands, the race converges: each failed claim releases again, until a
member that reaches the store wins.
