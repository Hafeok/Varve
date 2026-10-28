# The Blob backend against a real storage account — failure, fence and conditional writes

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#143](https://github.com/Hafeok/Varve/issues/143); the two defects it found are [#144](https://github.com/Hafeok/Varve/issues/144) and [#145](https://github.com/Hafeok/Varve/issues/145) |
| **Date** | 2026-10-23 |
| **Tool** | Claude Code 2.1.309, a local session in the maintainer's WSL2 workstation |
| **Model** | `claude-fable-5-1`, from the session's own metadata |
| **Session identifier** | `66ad8861-82e0-4845-823d-ad239f419d19`, the local session's id as its scratch directory names it |
| **Branch** | `issue-143-azure-blob-live`, from `main` at 6f438ff |
| **Signing** | The commits are signed with the maintainer's own key: this run predates the separate agent signing identity. Later local sessions sign as the agent identity. |
| **Report** | [`docs/reports/azure-blob-live-2026-10-23.md`](../reports/azure-blob-live-2026-10-23.md) |

## The prompt

The maintainer started the session with the test account's variables set
(`AZURE_CONFIG_DIR`, `VARVE_AZURE_BLOB_ENDPOINT`, `VARVE_AZURE_BLOB_CONTAINER`,
`VARVE_AZURE_TENANT_ID`) and this brief, pasted whole:

> # Claude Code session: Blob backend failure and lease suite against real Azure Storage
>
> ## Context
>
> Varve has an Azure Blob storage backend: the log lives in a storage account, derived state lives on local ephemeral disk, and a blob lease is the single-writer lock. Its behaviour has so far been tested against emulation (Azurite or an in-process fake). Azurite does not reproduce production behaviour reliably for leases, conditional writes (If-Match / If-None-Match on ETags), throttling, or latency. This session runs the backend's conformance, failure-injection and lease tests against a real account and records what differs.
>
> A disposable test account exists. The operator has set these in the shell that started this session:
>
> - `AZURE_CONFIG_DIR`: a dedicated Azure CLI profile, logged in to the right tenant and subscription only
> - `VARVE_AZURE_BLOB_ENDPOINT`: for example `https://<account>.blob.core.windows.net/`
> - `VARVE_AZURE_BLOB_CONTAINER`: `varve-test`
> - `VARVE_AZURE_TENANT_ID`
>
> ## Hard rules for this session
>
> 1. **Data plane only.** Never run an `az` command that creates, changes or deletes Azure resources, role assignments, or account settings. Allowed `az` use: `az account show` and `az storage blob list --auth-mode login` against the test container. Never run `az login`, `az account set`, or anything that touches another subscription or tenant. If something needs a control-plane change, stop and say so.
> 2. **No keys.** Never fetch, use or write an account key, connection string or SAS. The account has shared-key access disabled. All access is Entra tokens.
> 3. **Pin the credential.** In the test fixture, construct `AzureCliCredential` explicitly, with `TenantId` set from `VARVE_AZURE_TENANT_ID`. Do not use a bare `DefaultAzureCredential`. On a developer machine its chain can pick up a Visual Studio or VS Code account from another tenant before it reaches the CLI. `AzureCliCredential` honours `AZURE_CONFIG_DIR`. Production code paths keep whatever credential abstraction the backend ADR defines; only the test fixture pins.
> 4. **Isolation per run.** Every test run writes under a unique prefix `varve-test/<utc-timestamp>-<short-guid>/`. Each test owns its own sub-prefix. At the end of the run, delete only blobs under that run's prefix. Never list or delete outside it. The account also has a 7-day lifecycle policy on `varve-test/` as a backstop.
> 5. **Gated tests.** Real-Azure tests are skipped unless `VARVE_AZURE_BLOB_ENDPOINT` is set, and they are marked with a trait or category (e.g. `Category=AzureLive`) so CI never runs them by accident. Nothing about this session requires CI secrets.
> 6. **Traceability.** Per the MoM stewardship standard: open or reference an issue before committing, tie every commit to it, keep commits authored as Emil with sign-off, and record the AI session (tool and model) in the traceability record. Commit messages carry no model id.
> 7. **Dependencies.** Check the dependency register before adding a package. If `Azure.Identity` or `Azure.Storage.Blobs` is not already cited by an ADR at the needed major version, add the ADR entry first. A test-only package still needs one.
> 8. All architecture analyzers stay green. No suppressions without an ADR citation.
>
> ## Work
>
> ### Step 0: Orient and verify
>
> - Locate the Blob backend, its ADRs, and the existing test suites that cover it (conformance against the storage abstraction, failure injection, lease and single-writer tests, the soak test). Report what exists and which emulator each currently runs against.
> - If the Blob backend is not on main yet, stop and report. Do not build it in this session.
> - Run `az account show` and confirm the tenant matches `VARVE_AZURE_TENANT_ID`. Do one data-plane smoke call: write, read and delete a single blob under the run prefix. Role assignments can take several minutes to propagate. On a 403, retry with backoff for up to 10 minutes before reporting.
>
> ### Step 1: Live fixture
>
> - Add a live-storage fixture beside the emulator fixture, implementing the same test seam. The existing suites should run unchanged against both. If the seam does not allow that, report why before changing production code.
> - Add a test project or test collection for the live leg only if the existing layout cannot gate by trait. Prefer the trait.
>
> ### Step 2: Run the existing suites live
>
> Run, against the real account:
>
> - the storage-abstraction conformance suite
> - the failure-injection suite (crash points around segment seal, commit index update, lease renew)
> - the lease and single-writer tests: acquire, renew, expiry, break, a second writer rejected, epoch fencing on conditional writes after lease loss
> - conditional-write semantics the backend relies on: create-if-absent (`If-None-Match: *`), replace-if-match (`If-Match: <etag>`), and the expected 409/412 outcomes
>
> Record pass, fail and timing per test, for both emulator and live.
>
> ### Step 3: Tests the emulator cannot answer
>
> Add live-only tests where the backend's correctness depends on them:
>
> - **Lease expiry under a stalled writer.** A writer whose renewals stop loses the lease within the documented bound, and its next conditional write fails. Verify that the epoch fence, not the lease alone, prevents a split-brain append.
> - **Concurrent create-if-absent race.** N tasks race to create the same segment or head blob. Exactly one succeeds; the rest get 409 or 412. Repeat enough times to be meaningful, and report the count.
> - **Transient faults.** Behaviour under 503/500 responses and timeouts, using the SDK retry policy as configured by the backend. A retried conditional write must not commit twice. Check the at-most-once property of the commit path explicitly.
> - **Read-after-write.** A sealed segment is immediately visible to a second client.
> - **Latency.** A BenchmarkDotNet-style or simple timed run of commit latency (p50/p95/p99) for small commits from this machine, labelled with region and client location. These are informational, not a gate, and must not be presented as performance claims.
>
> ### Step 4: Report
>
> Write `docs/reports/azure-blob-live-<date>.md` with:
>
> - environment (region, SKU, flat namespace, SDK versions, client location)
> - emulator vs live results table
> - every behavioural difference found, each linked to an issue
> - any ADR the findings contradict, with a proposed amendment or superseding ADR rather than a silent change
> - what remains untested and why
>
> ## Definition of done
>
> - Live leg runs locally with one command, for example `dotnet test --filter Category=AzureLive`, and is skipped everywhere else.
> - Existing suites pass live, or each failure has an issue and a written explanation.
> - Step 3 tests exist and pass, or their failures are issues.
> - The report is committed. No blobs remain under the run prefix.
> - Analyzers green. Dependency register consistent.
>
> List any stubs or deferred items at the end of the session report.

## Where the brief and the code differ, and what the session did about it

- **There is no blob lease.** The brief's context says a blob lease is the
  single-writer lock. ADR 0136 refuses a lease: one writer holds an epoch in
  the `writer` record, and a successor's claim fences the log at the next
  slot by create-if-absent. The brief's "lease" tests map onto the epoch
  fence, and were run as such (`WriterFenceCases`); the "expiry under a
  stalled writer" has no bound in time to verify, because nothing is decided
  from elapsed time, and the session verified the fence instead, which is
  what the brief asked the fence to be checked against.
- **There is no SDK, so there is no `AzureCliCredential`.** ADR 0134 decision 3
  refuses the Azure SDK, and ADR 0137 names the three token sources. The
  fixture pins the tenant the way the adapter's own CLI source works: it
  runs `az account get-access-token --resource https://storage.azure.com/
  --tenant <VARVE_AZURE_TENANT_ID>` itself, which honours
  `AZURE_CONFIG_DIR`. No package was added; the register is unchanged.
- **The container is a shared one**, so suites written for a container of
  their own run under the run's prefix through a prefixing decorator in the
  tests, with the real adapter beneath.

## What was done

- **Step 0.** The backend, its ADRs and every suite were located; the tenant
  of `az account show` matched `VARVE_AZURE_TENANT_ID`; the smoke call (PUT
  with `If-None-Match: *`, GET, DELETE under a run prefix, by `curl` with a
  CLI token) answered `201`, `200`, `202` at the first attempt, and a listing
  under the prefix afterwards was empty.
- **Step 1.** `AzureAccount` in `tests/Varve.Store.Tests/ObjectStorage/AzureAccountTests.cs`:
  the container from either variable set, the tenant-pinned CLI token, one
  cached token per process, a run prefix of UTC time and a random part, a
  prefix per test, deletion under the test's prefix only, and an env-gated
  tracing handler. `PrefixedObjectStore` lets a suite written for a
  container of its own run under the prefix. Every live class carries
  `[Trait("Category", "AzureLive")]`; the leg is one command, and it skips
  without the container.
- **Step 2.** The object-store contract, the storage contract, the
  extent-maintenance follower, the writer fence (extracted into
  `WriterFenceCases`, run in memory, on Azurite and live) and the HTTP
  failure-injection workload (its loop extracted into `HttpFaultSuite`, with
  a stride) ran live. The storage contract failed twice before the fix for
  #145 and passed four times after; the fault suite at every request passed
  on both sides.
- **Step 3.** `AzureLiveTests.cs`: the service's status codes with a
  recording handler; a 503, a 500 and a lost answer on one create, at most
  once; read-after-write from a second client and a follower; 20 × 32 racing
  creates and replaces with the losers' answers counted; list paging past
  5,000 names; latency, informational.
- **Two defects, fixed and pinned:** #144 (`ObjectSegmentStore.Segment.Prepend`
  with an empty buffer) and #145 (`ObjectStorage.AnnounceAsync` after a
  replace whose answer was lost). ADRs 0160 and 0134 carry dated amendments.
- **Step 4.** The report, `docs/testing.md` §9, and this record.
- **Not done, and why.** The optional CI workflow still filters by class;
  switching it to the trait is the maintainer's call. The server legs
  (`FleetTests`, `ObjectStorageServerTests`) were not taken to the account.
  The region was not read, since the control plane was off limits.

## What the session decided, and is open to the maintainer

- The brief's "lease" vocabulary was mapped onto the epoch fence, and the
  brief's `AzureCliCredential` onto the adapter's own CLI source with the
  tenant named, rather than adding the SDK the ADRs refuse.
- The concurrent-claim case asserts the chain ADR 0136 describes, not a
  single winner, after the first version of the case assumed one and was
  wrong on every store.
- The #145 fix adopts a record that carries this instance's reader id and
  holder; a record of the same id with another holder is still forgotten.
  Whether a holder-less follower should be treated the same is the
  maintainer's to confirm.
- Two product changes were made in `src/` on the strength of pinned tests;
  the ADR amendments are filed unaccepted.
