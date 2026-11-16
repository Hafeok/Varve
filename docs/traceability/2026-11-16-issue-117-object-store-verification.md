# Object store verification: the suite as shipped code, `varve storage verify`, the compatibility page (pull request 1 of Replication part 2)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim
> below. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#117](https://github.com/Hafeok/Varve/issues/117), completed; [#166](https://github.com/Hafeok/Varve/issues/166), whose first pull request this is; [#97](https://github.com/Hafeok/Varve/issues/97). Opened by this session: [#314](https://github.com/Hafeok/Varve/issues/314), [#315](https://github.com/Hafeok/Varve/issues/315) |
| **Date** | 2026-11-16 |
| **Tool** | Claude Code 2.1.325, a cloud session. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* This tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYSB3pCh3SLBkJVgjCv` |
| **Branch** | `claude/amazing-johnson-1iatse`, from `main` at 590ea35 |
| **ADRs** | [0183](../adr/0183-object-store-verification-and-the-compatibility-page.md) filed unaccepted, with set `verified-object-stores`; [0134](../adr/0134-the-object-store-contract.md), [0216](../adr/0216-the-format-inventory.md) and [0217](../adr/0217-coordination-records-read-conservatively.md) amended, unaccepted |

## The prompt

The maintainer's first message: "Read AGENTS.md, then the session prompt in
the attached varve-claude-code-replication-part2.md, #166, #117 and the
approved plan on #97. Your first deliverable is the delta plan on #166. Wait
for my answer before writing code."

The attached session prompt, verbatim:

````markdown
# Session prompt: Replication part 2 — verified stores, log shipping, promotion, geo-replication (#166, with #117)

Paste into a fresh session on `Hafeok/varve`. Read `AGENTS.md` first and follow it in full.

Part 2 finishes the Replication milestone (#97). Part 1 (#165, preview.16) replaced a writer within one store. Part 2 puts copies of the log elsewhere and makes one of them authoritative. Its descriptor closes #166, #117 and #97.

## What is already decided

Do not re-open these. They are the approved plan and the issue:

- **#166**, the deliverables and the gate.
- **The plan approved on #97** (https://github.com/Hafeok/Varve/issues/97#issuecomment-6436808419), sections 5 to 8 and the maintainer's answers. It fixes: what is shipped and in what order; resume by the target's tail through the header chain; the shipper as a reader under ADR 0160 and as the target's maintenance writer; the `lineage` record; `varve promote` and its epoch rule; how the returning old primary is refused; tier 2 over the RA-GRS secondary with lag from `LastSyncTime`.
- **#117**, in full.
- **ADR numbers 0183 to 0186** are reserved for this part and free. Check every open pull request again before you file them.

The plan was written against `main` at `6cdb402` on 2026-10-27. A lot has landed since. **Your first deliverable is a delta plan on #166: what in the approved plan no longer matches `main`, and what you would change.** Wait for the maintainer's answer before writing code.

## What has changed since the plan

Read each of these and say in the delta plan how it touches part 2.

1. **Part 1 as it landed** (ADRs 0179 to 0181), and its follow-ups:
   - #216: a release for a storage or local-disk error starts the releaser's hold-off (`ClaimAfterAReleaseByItsReason`, restated).
   - #251: a member whose open or write against the store fails holds off before claiming again (`ClaimsHoldOffAfterAStoreFailure`, ADR 0163's amendment).
   - Promotion claims on the target's writer record with trigger `promotion`. Say how that claim sits with the hold-off and the writer set, and whether a shipper's failed write to the target starts a hold-off of its own.
2. **Row-level access part 1** (#208, ADR 0238) and **Inference part 1** (#178). Grants, row rules and regime bindings are declarations in a fleet's system dataset, not in the dataset being shipped.
   - State what governs a read of a shipped copy: the target fleet's own declarations, or something shipped with the dataset.
   - If it is the target fleet's own, the operator guide must say that a replica is only as protected as the declarations made where it is served, and active-active across copies needs the same declarations on both sides.
3. **Erasure mode is not built yet** (milestone 9 part 2, #203), but it constrains shipping, the way it constrained the inference closure.
   - A shipped log carries the ciphertext of private terms, and the key store is never inside a dataset directory.
   - An erasure has to take effect on every copy. Record in ADR 0184 the precondition milestone 9 must meet (the copies share the key store's destruction, or erasure mode refuses a `varve:ShippingTarget`), and leave the choice to milestone 9 as an open question.
4. **The memory work** (#268 for #222, #264 for #227, and #256): seek directories are read on demand, a checkpoint flushes the memtable, and superseded runs stay on disk until the next open. The shipper copies "a set's nine blobs as they are". Check that this still describes a set on `main` when you start, and that the shipper's reader record still protects what it is copying.
5. **Supply chain** (ADR 0224, 0225): lockfiles with locked restore, the dependency register, one SBOM per shipped artefact, descriptor format 2 with its `sbom:` block.
   - `varve storage verify` ships in the CLI, so the contract suite moving into shipped code changes the CLI's closure. Any new package needs a register entry and an ADR.
   - Regenerate lockfiles with `dotnet run eng/lockfiles.cs`. Write the descriptor's digests with `eng/release.cs --sbom` once #261 has landed.
6. **Credentials** (ADR 0223, ADR 0037): OIDC and workload identity federation only. No account keys, no connection strings, no SAS tokens, in the server, the CLI, the tests or the Bicep.
7. **The tier 2 leg on a real account will not have an account at first.** The maintainer has not provisioned it.
   - The leg follows ADR 0223's rule: with none of its identifiers present it skips and names what is missing; with some present it fails.
   - Deliver the Bicep and a step-by-step page for the maintainer to set it up, as `docs/auth-providers.md` does for the provider legs.
   - Say in the close-out that tier 2's real-account gate has not run, if it has not. Do not report tier 2 as verified on the strength of the emulator.

## Rulings added for this part

- **Three pull requests, one release.** Part 2 is too large to review or land as one head.
  1. **#117:** ADR 0183, `varve storage verify`, the result records and the generated compatibility page. No descriptor. It can land as soon as it is green.
  2. **Tier 3 and promotion:** ADRs 0184 and 0185, with the tier 3 gate and the promotion rehearsal. No descriptor.
  3. **Tier 2, active-active across copies and the close-out:** ADR 0186, the Bicep, the operator guide, and the release descriptor, which closes #166, #117 and #97.

  If your delta plan shows a better seam, propose it.
- **No timer decides anything.** `Varve:Replication:ShipInterval` affects how soon shipping happens, never whether it is correct. Tier 2's lag comes from the account's `LastSyncTime`, never from local clock arithmetic.
- **Nothing on a target is ever overwritten.** Every log write is create-if-absent of byte-identical content; anything else is `shipping-diverged`.
- **Promotion is always an operator's act.** Nothing in Varve promotes a replica on its own.
- **RustFS.** The fleet tests on RustFS have been flaky ("the response ended prematurely"), partly from #251, which is fixed. Put the shipping runs between Azurite and RustFS in their own named job. A failure there that you cannot reproduce is reported on its own issue with the job id, not retried until green.
- **Problem types** (`store-unverified`, `shipping-diverged`, `shipping-target-not-empty`, `dataset-is-replica`, `replica-promoted`) each get a `docs/problems/` page with a "What to do" section, as #194 now requires.

## Plan questions for the delta plan

1. What in the approved plan's sections 5 to 8 no longer matches `main`, with paths.
2. The promotion claim and the hold-off (change 1).
3. What governs reads of a shipped copy (change 2).
4. The erasure precondition's wording (change 3).
5. Whether the three-pull-request split holds, and what each one's gate is.
6. Which CI jobs are new, their expected durations per OS, and what moves to the nightly at full depth.
7. Any ADR beyond 0183 to 0186, and every dated amendment (at least 0136, 0160, 0163 and 0164).

## How this lands

- **Issues are the work log.** Comment on #166 when you start. Post the delta plan there. Decisions, questions and blockers go on the issue when they arise. Work you find and don't do becomes its own issue at once.
- **You are a cloud session, not the maintainer's delegate** (ADR 0239). File every decision unaccepted. Never write `accepted-by` on a decision set, never post an approval, never land. Post each head with its full `eng/ci.cs` result and ask. A pull request with an unaccepted decision is red on `CS0618`; say on the pull request whether that is the only failure.
- **Each pull request description** carries one `Completes:` line: `Completes: #117` for the first, `Completes: none` for the second, and `Completes: #166, #117, #97` only if the first did not close #117. No closing keyword in a description.
- **Closing trailers:** `Closes #117` on the commit that completes #117. The descriptor commit carries `Closes #166` and `Closes #97`. Everything else uses `Refs`.
- **The descriptor** is `releases/v0.1.0-preview.N.yaml` at the next free number when it lands, format 2, with `accepted-by: mailto:emil@okkels-klein.dk`. That line in a descriptor is written by the session; a decision set's never is.
- **ADR status lines** read "Accepted — filed unaccepted by …", or the release check refuses them.
- **Commits:** as `Claude (Emil) <claude+emil@okkels-klein.dk>`, signed with the agent key. Never `git commit -s`; the hook adds the sign-off. The co-author trailer is exactly `Co-Authored-By: Claude <noreply@anthropic.com>`. No commit message names a model (ADR 0241); model identifiers in API form are allowed, and the traceability record names the model.
- **A `docs/traceability/` record** per pull request (ADR 0033), and the close-out in `docs/sessions/`.
- **No suppression or downgrade of DD or VARVE rules.** Never weaken a gate to make a test pass.
- **CI images** only from `.github/ci-images.yaml`. No Docker Hub pulls. A new image needs a row, a mirror run, and the maintainer making its package private.
- **Test time:** size the land-gate iteration counts and put full depths in the nightly (`docs/testing.md` §8). Report durations per OS.
- **Landing** is by the delegate, through `land/`, possibly as part of a landing train. When `main` moves, merge it in; do not rebase a pushed branch.

## Definition of done

#166's gate, unchanged:
- Tier 3 under failure injection: a follower of the target equals the reference model at its reported position.
- Byte identity: the target's log is byte-identical to the source's up to the shipped position.
- The promotion rehearsal in tiers 2 and 3: promote, write, then the old primary returns and is refused.
- Tier 2 on a real account in the optional `main`-restricted leg, by workload identity federation, with no keys; or stated plainly as not yet run.
- `varve storage verify` passes against Azurite and RustFS, and fails with the counterexample against the two broken stores.
- The close-out: README Status, `docs/roadmap.md` (Replication complete), AGENTS.md State, `eng/status.cs`, the changelog, the operator guide (shipping, promotion, RPO stated plainly, the compatibility page, the geo layout), `docs/problems/`, and the traceability records.

## Added 2026-11-16

`main` has moved since this prompt was written on 2026-11-11. Account for these in the delta plan as well:

- **Landed:** #253 and #252 (the hold-off and the restated release rule), #280 and #281 (ADR 0243 and landing trains), #259 to #261 (issues as the work log, `Completes:` checked at landing, the descriptor date follows the digests), #286 (Aspire 13.6.5), #293 (SDK 10.0.404), #263, #265 and #266. The latest release is `v0.1.0-preview.21`.
- **Landing today:** #268 (ADR 0240, nothing resident per commit) and #264 (ADR 0244, memory bounds). ADR 0244 makes a commit wait at twice the memtable's limit and makes an open flush runs. Say what that means for the shipper as the target's maintenance writer, and for a promoted target's first open.
- **In flight:** #296 puts the Web UI in the container image as `v0.1.0-preview.22`, so your descriptor is preview.23 or later. Take the next free number when you land.
- **#267 (ADR 0242)** requires every problem page to have a `## What to do` section, and a refusal never to confirm that something hidden exists. Your five new problem types follow both.

**Rules that are new since the prompt:**
- **Never write the approval line,** for any hash, in a comment, a description or a commit message (#290). Report a head as "head `<sha>`".
- **The `commit-msg` hook and the model-names gate** (#282) refuse a commit message that names a model. The trailer is exactly `Co-Authored-By: Claude <noreply@anthropic.com>`.
- **`eng/release.cs --sbom <version>`** writes the descriptor's digests and its date together. A descriptor dated more than three days before landing is refused, so run it again before you ask for approval.
- **Never force-push.** Merge `main` in.
- **ADR numbers 0183 to 0186** are still free on `main` and in every open pull request as of today. Check again when you file them.
````

The maintainer's second message, after the delta plan, verbatim:

> An excellent plan. Answers:
>
> * D1: the root of the store URL. Say in ADR 0183 that the record guards against a mistake, not an adversary.
> * D2: as proposed.
> * D3: option (a), ship the source fleet's `_fleet`, with three conditions. It is never consulted for authorisation, with a test: a grant present only in the shipped `_fleet` grants nothing on the target. It is shipped ahead of the dataset commits that refer to it, so a follower of the replica never meets a foreign position it can't resolve. And open a follow-up issue for restoring a lost source fleet's declarations from its shipped `_fleet`; that is not part 2.
> * D4, D5, D7, D8: as proposed.
> * D6: a new workflow and the `azure-geo` environment. Declare the environment in `.github/repo-standard.yaml`, restricted to `main`.
> * D9: open it now.
> * D10: a directory is a target only. The guide says plainly that a file-backend deployment cannot be a shipping source, and points to `ShipAsync` and backup-and-restore.
> * D11: no. Readiness reports the shipped catalog's position beside the shipped tail, and the guide says the source's checkpoint policy bounds a promoted open.
>
> One addition: #132 puts a hash of each write's idempotency key in commit metadata. Shipped copies keep every commit's metadata intact, and any hop you add passes the `Idempotency-Key` header through unchanged. Read #132's notes for replication.
> I've closed #284 as a duplicate of #278. Record these answers on #166, then file ADR 0183 and start pull request 1.

## The report

- **The delta plan came first.** #166 already carried one from an earlier
  session, written against `2cad18d` and unanswered. This session checked it
  against `main` at `48a1fd5`, posted a revision that replaces it, and built
  nothing until the maintainer answered D1 to D11. The answers are recorded
  on #166.
- **Opened on those answers:** #314 (a drift check for a shipped dataset's
  declarations across two fleets) and #315 (restoring a lost source fleet's
  declarations from its shipped `_fleet`). The erasure precondition for log
  shipping was put on #203.
- **ADR 0183**, filed unaccepted, with seven rulings:
  - the contract suite is shipped code;
  - a run is confined to a scratch prefix;
  - a pass leaves a verification record at the store's root, which guards
    against a mistake and not an adversary (the maintainer's answer D1);
  - a record this build does not read is not a verification;
  - a result record per provider and version;
  - the compatibility page is generated;
  - two broken stores gate the suite.
- **Built:**
  - `ObjectStoreVerification` in `Varve.Store.ObjectStorage`: ADR 0134's
    seven cases, moved from `tests/`. The tests against the in-memory store,
    Azurite, RustFS and a real account now call it, so there is one suite.
  - `varve storage verify <store>`, with `--record` for the result record.
  - `eng/object-stores.cs`, which generates
    `docs/operator/object-stores.md` from
    `testdata/object-stores/providers.yaml` and the result records.
    `eng/status.cs` checks it as its sixth statement.
  - The format inventory's two new rows and the fixture
    `testdata/formats/verification-next`.
  - CI: `storage (services)` runs the command against Azurite and RustFS;
    the nightly run races 64 rounds; `azure-objects.yml` writes Azure Blob
    Storage's record; `aws-objects.yml` is new and skips until its role
    exists (ADR 0223).
- **Decisions taken while building,** each in ADR 0183 and listed on #117:
  - an emulator is its own provider, so Azure Blob Storage is *untested* on
    the page until the optional leg's record is added;
  - the server's version and the deployment are the operator's statement,
    where the approved plan had "from the response headers";
  - a failed run removes the verification record;
  - the `store-unverified` problem type and the compiled list of verified
    providers moved to pull request 2, where their first use is.
- **Measured,** on this session's Linux container:
  - `ObjectStoreVerificationTests`, `MemoryObjectStoreContractTests` and
    `VerificationRecordFormatTests`: 26 tests, all passing, under 2 s. They
    pass with `DOTNET_PROCESSOR_COUNT=1` too, which matters for the store
    that loses races.
  - `StorageVerifyTests`: 13 tests, all passing, under 2 s.
  - **The gate's two broken stores.** The store that ignores conditions
    fails at the first case: "a second create of the same name returned a
    version". The store that loses races opens for write (the probe passes
    it), passes the five operations, and fails the create race in round 0:
    "… of 16 racing creates were told they wrote".
  - **Azurite 3.37.0, for real.** Started from its npm package in this
    container, since there is no container runtime here. `varve storage
    verify` passed against a fresh container in 1.8 s, wrote its result
    record, and a second run replaced the verification record.
  - **RustFS was not run.** Its binary is not reachable from here. Its leg
    first runs in CI.
  - **The page check's failure path.** The fixture
    `tests/fixtures/status/object-stores-edited` is the page with Garage
    changed to verified. `eng/status.cs --fixtures` reports it: "is not
    what testdata/object-stores/providers.yaml and the records under
    testdata/object-stores/results/ generate".
  - **`dotnet run eng/ci.cs -- --base origin/main`:** every job up to
    `build` passes, and `build` fails on `CS0618` at one place, the
    citation of `VerifiedObjectStores.TheContractSuiteIsShippedCode`. That
    is ADR 0066's rule for a decision filed unaccepted.
  - **The same run with `CS0618` not an error** (the environment variable
    `WarningsNotAsErrors=CS0618`, on this machine only, to see the rest):
    all 70 jobs pass. `test-store` 495 tests, 402 passed and 93 skipped for
    services, 5 min 08 s. `test-server` 252 tests, 192 passed and 60
    skipped.
  - **Lockfiles:** `eng/lockfiles.cs` needs the WebAssembly workloads,
    which this container lacks, so it was not run. No project reference or
    package changed, and the `lockfiles` and locked `restore` jobs pass.
- **Found:**
  - This clone was shallow and had no tags. `decision-sets` and `release`
    fail on such a clone for reasons that have nothing to do with a change.
    `git fetch --unshallow` and `git fetch --tags` fix it.
  - `core.hooksPath` was not set in this cloud session, so the first commit
    had no sign-off. It was set, and the commit made again before anything
    was pushed.
  - CI's `storage (services)` job builds with warnings as errors. So it
    cannot produce the Azurite and RustFS result records while ADR 0183's
    rulings are unaccepted, and the page shows both as untested until then.
- **Left:**
  - Azurite's and RustFS's result records, from CI's run, with the page
    regenerated.
  - Azure Blob Storage's record, from `azure-objects.yml`, which the
    maintainer dispatches.
  - The Amazon S3 role, which the maintainer creates.
  - Pull requests 2 and 3 of #166.
