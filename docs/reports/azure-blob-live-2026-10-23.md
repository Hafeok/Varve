# Azure Blob Storage, live: the object-storage backend against a real account, 2026-10-23

The object-storage backend (`Varve.Store.ObjectStorage` over
`Varve.Store.Azure`, ADRs 0134–0137) is proved in CI against an in-memory
store and Azurite. This run took the same suites, and a few that only mean
something on the service, to a real storage account (#143). Everything the
suites say about Azurite was already known; what is new is what the service
itself does, and two defects the run found, [#144](https://github.com/Hafeok/Varve/issues/144)
and [#145](https://github.com/Hafeok/Varve/issues/145), both fixed on the
same branch and pinned in memory.

Developed with AI assistance under human review; the session is recorded in
[`docs/traceability/2026-10-23-issue-143-azure-blob-live.md`](../traceability/2026-10-23-issue-143-azure-blob-live.md).

## 1. Environment

| | |
|---|---|
| **Account** | A disposable test account the maintainer deployed from a template outside the repository: StorageV2, `Standard_LRS`, Hot tier, **flat namespace** (hierarchical namespace off), blob versioning off, soft delete off, shared-key access **disabled**, OAuth by default, TLS 1.2, HTTPS only, a 7-day lifecycle rule on the test container. One container, `varve-test`. |
| **Region** | Not read: the control plane was off limits to the session (data plane only). The endpoint's DNS name resolves through the `gvx01prdstr03a` storage cluster, which the maintainer can map to the region. |
| **Identity** | The maintainer's user account, Storage Blob Data Contributor on the account, through a dedicated Azure CLI profile (`AZURE_CONFIG_DIR`). No key, SAS or connection string anywhere (ADR 0137). |
| **Client** | `Varve.Store.Azure` over `HttpClient`, no Azure SDK (ADR 0134, decision 3); `x-ms-version: 2025-11-05`, which the service echoed on every answer. .NET SDK 10.0.401 on Linux x64; Azure CLI 2.76.0 for the token; Azurite 3.37.0 in Docker on the same machine for the emulator column. |
| **Client location** | The maintainer's workstation under WSL2 (kernel 5.15), on a residential connection; the maintainer states the city. A request round trip to the account measured 23–27 ms at the median throughout the run. |
| **Isolation** | Every run under `<utc-time>-<random>/` in the container, every test under a prefix below it, each deleted at the test's end. Nothing outside a run's prefix was listed, written or deleted. A listing of the container under each run's prefix after the run returned nothing. |

The smoke call of step 0 (one PUT with `If-None-Match: *`, one GET, one
DELETE under a run prefix, by `curl` with a CLI token) succeeded at the first
attempt: `201`, `200`, `202`.

## 2. What exists, and where it ran before

| Suite | What it proves | Ran against, before this run |
|---|---|---|
| `ObjectStoreContractCases` (7 cases, the two race properties included) | ADR 0134's five operations and decision 7 | memory, Azurite, RustFS, and the account on the optional CI leg (`azure-objects.yml`, maintainer-dispatched) |
| `StorageContractTests` (9 cases) over the backend | the storage contract of ADR 0071 over objects | memory, Azurite, RustFS, the account on the optional leg |
| `ObjectStorageTests`, `ObjectEpochPropertyTests` | the claim, the fence, the takeover, the probe (ADR 0136, 0134 decision 9) | memory only, with the store's hook for the schedules |
| `ObjectFaultTests` | a fault of four kinds at every request of a seeded workload | memory only |
| `AzuriteFaultTests` (`HttpFaultTests`) | a 500, 503, 429, timeout and lost response at every request over HTTP | Azurite only |
| `ObjectExtentMaintenanceTests`, `ObjectReaderScheduleTests` | merges, reader records, deletion (ADRs 0158–0161) | memory; the follower case also on Azurite and RustFS |
| `Varve.Server.Tests`: `FleetTests`, `ObjectStorageServerTests` | two server processes on one store, `varve info`, `varve lease` | Azurite only |
| `tests/Varve.Benchmarks` `Soak` | the file backend's working set over an hour (#61) | the file backend; not an object store |

There is **no blob lease and no lease test**: ADR 0136 refuses a lease, one
writer holds an epoch in the `writer` record, and a successor's claim fences
the log by creating the next slot. The brief's "lease" suite is the fence,
and that is what ran.

## 3. The live leg

One command runs it, and it is skipped wherever the container is not named
(`docs/testing.md` §8):

```bash
dotnet test --project tests/Varve.Store.Tests/Varve.Store.Tests.csproj --configuration Release \
  -- --filter-trait Category=AzureLive
```

The fixture (`AzureAccount`) pins the tenant by running `az account
get-access-token --tenant` itself, the adapter's own CLI source with the
tenant named, since there is no SDK credential to pin (ADR 0137); CI's
federated path is unchanged. Suites written for a container of their own run
under the run's prefix through `PrefixedObjectStore`, a test decorator with
the real adapter beneath, so every existing suite ran unchanged. The writer
fence was extracted into `WriterFenceCases`, which runs in memory, on Azurite
and on the account.

## 4. Emulator against live

Every case passed on both sides at the end of the run, after the fixes for
#144 and #145; the three failures seen before them are in §5. Durations are
one run each, wall clock, on the machine above; Azurite's column includes
creating a container per test.

| Suite | Case | Azurite | Live |
|---|---|---|---|
| `ObjectStoreContractTests` | a delete deletes once | pass, 216 ms | pass, 1 s |
| `ObjectStoreContractTests` | a list returns the prefix's objects in ordinal order with their lengths | pass, 431 ms | pass, 460 ms |
| `ObjectStoreContractTests` | a ranged read returns the range, short at the end, none for no object | pass, 259 ms | pass, 289 ms |
| `ObjectStoreContractTests` | create-if-absent writes once and reads back | pass, 332 ms | pass, 282 ms |
| `ObjectStoreContractTests` | of clients racing create-if-absent on one name exactly one wins (16 × 8 rounds) | pass, 1 s | pass, 836 ms |
| `ObjectStoreContractTests` | of clients racing replace-if-unchanged on one version exactly one wins (16 × 8 rounds) | pass, 2 s | pass, 1 s |
| `ObjectStoreContractTests` | replace-if-unchanged needs the current version | pass, 342 ms | pass, 268 ms |
| `StorageContractTests` | a blob is invisible until published and an unpublished writer leaves nothing | pass, 631 ms | pass, 653 ms |
| `StorageContractTests` | a dataset commits checkpoints reopens and reads as of over the backend | pass, 6 s | pass, 4 s |
| `StorageContractTests` | a sealed segment refuses appends and only the newest may be open | pass, 1 s | pass, 791 ms |
| `StorageContractTests` | an open blob keeps its bytes when its name is replaced | pass, 563 ms | pass, 636 ms |
| `StorageContractTests` | appended bytes read back and a read past the end is short | pass, 658 ms | pass, 704 ms |
| `StorageContractTests` | bytes already read never change | pass, 127 ms | pass, 647 ms |
| `StorageContractTests` | derived blobs are published replaced listed in ordinal order and deleted | pass, 207 ms | pass, 668 ms |
| `StorageContractTests` | the backend declares its durability | pass, 1 s | pass, 609 ms |
| `StorageContractTests` | the manifest is empty until written and is written once before any segment | pass, 2 s | pass, 2 s |
| `ExtentMaintenanceTests` | a follower keeps serving across merges and deletions | pass, 14 s | pass, 18 s |
| `WriterFenceTests` | a second writer supersedes a stalled first, whose next write is refused and lands nowhere | pass, 5 s | pass, 3 s |
| `WriterFenceTests` | a segment of fence chunks alone, left by writers that committed nothing, opens and takes a commit (#144) | pass, 3 s | pass, 3 s |
| `WriterFenceTests` | a writer claims the first epoch, fences the first slot and releases it on close | pass, 2 s | pass, 2 s |
| `WriterFenceTests` | an operator takeover names the epoch and fences the writer that held it | pass, 8 s | pass, 3 s |
| `WriterFenceTests` | of writers claiming at once exactly one holds the epoch and the rest do not retry (six claimants) | pass, 10 s | pass, 3 s |
| `FaultTests` | a 500, 503, 429, timeout and lost response at every request: the dataset reopens with nothing lost or doubled | pass, 327 s | pass, 2218 s |
| `AdapterTests` | a container address with a query is refused without echoing it | pass, 1 ms | — |
| `AdapterTests` | a create that finds a blob is 409 blob already exists and reads as a lost race | pass, 207 ms | — |
| `AdapterTests` / `ListingTests` | a list pages past five thousand names | pass, 12 s | pass, 8 s |
| `AdapterTests` / `ConditionalWriteTests` | the answers to a create that finds a blob, a stale replace and a replace of nothing: 409, 412, 412 | pass, 73 ms | pass, 306 ms |
| `AdapterTests` | the development key signs only Azurite on a loopback address | pass, 0 ms | — |
| `ConditionalWriteTests` | a transient fault on a create never lands it twice | — | pass, 1 s |
| `ReadAfterWriteTests` | a blob one client created is read and listed at once by another | — | pass, 7 s |
| `ReadAfterWriteTests` | a sealed segment is read at once by a follower | — | pass, 2 s |
| `RaceTests` | of clients racing create if absent on one name exactly one wins and the rest are told 409 | — | pass, 3 s |
| `RaceTests` | of clients racing replace if unchanged on one version exactly one wins and the rest are told 412 | — | pass, 3 s |
| `LatencyTests` | commit latency for small commits is measured and reported | — | pass, 15 s |

The HTTP failure-injection workload: 99 requests in the clean workload, a fault of each of five kinds at every one of them, 495 injection points, nothing lost or doubled at any, no crash while reopening, on both: 327 s on Azurite alone (916 s when the in-memory suite shared the CPU), 1,984 s on the account. The fence cases' durations on Azurite were taken while the memory suite ran beside them and are high by that.

## 5. What differed, and what was found

Each with its issue.

1. **A reader-record replace whose answer was lost made the next
   announcement fail as forgotten — [#145](https://github.com/Hafeok/Varve/issues/145), fixed.**
   `AzureAccountStorageContractTests.a_dataset_commits_checkpoints_reopens_and_reads_as_of_over_the_backend`
   failed twice out of two before the fix, and passed four of four after it.
   The trace showed the checkpoint's catalog swap, then a reader-record PUT
   that threw `TaskCanceledException` — the dataset's close cancelling an
   announcement that had just started — then a retry refused `412`, and a GET
   showing the record at a version written by the cancelled PUT, which the
   service had applied. Never seen in memory or on Azurite, where the round
   trip is a millisecond and the cancellation lands before the write; on the
   service the round trip is 25 ms. The fix reads the record back when a
   replace is refused and adopts it when it is the instance's own, by reader
   id and holder; ADR 0160 carries a dated amendment, and the case is pinned
   in memory by a replace cancelled after the store applied it.
2. **A segment of fence chunks alone crashed the next open — [#144](https://github.com/Hafeok/Varve/issues/144), fixed.**
   Found by the new concurrent-claim case in memory, in its first run:
   writers claiming in turn before any commit leave segment 0 as a run of
   fences with no bytes, and `Segment.Prepend` constructed an
   `ArrayBufferWriter` with capacity zero, which it refuses. Reachable on the
   service by two instances restarting at once; pinned deterministically
   (`FenceRunOpens`) on all three stores. Not an emulator difference: a
   defect no suite had reached.
3. **The service's answers to the conditional writes are Azurite's, and one
   of ADR 0134's is neither's.** `409 BlobAlreadyExists` for a create that
   finds a blob (640 of 640 losing racers) and `412 ConditionNotMet` for a
   stale replace (620 of 620), as ADR 0134 took from Azurite's source. A
   replace of a blob that does not exist is `412 ConditionNotMet` on the
   service and on Azurite alike; the `404` decision 5 also maps was observed
   from neither. Nothing changes in code, since the mapping is harmless; ADR
   0134 carries a dated amendment recording what was observed, pinned on
   both sides.
4. **Concurrent claims are a chain, not a race, when they read different
   versions.** Six writers opening at once: those that read the same record
   version race it and one wins; a claimant that reads the winner's record
   supersedes the winner in its turn (ADR 0136 decision 2: an open for write
   claims the next epoch), and the superseded one finds out at its first
   write, refused by the fence slot. On the account one run went 1 → 2 → 3.
   As designed, and decision 7 names the pattern as a misconfiguration to
   read in the history; the test as first written assumed one epoch and was
   wrong. No issue: the case now asserts the chain, that exactly one of the
   claimants commits, and that nothing else lands.
5. **A test-fixture race on Azurite**, unrelated to the service: the Azurite
   fixture kept the last container it created in a static, and two test
   classes running in parallel read each other's. Fixed in the fixture; the
   fault suite's first run under the new fence tests hit it.

Nothing else differed. Read-after-write, the race properties, list paging
past 5,000 names, the extent-maintenance follower and the fence held on the
service exactly as on Azurite.

## 6. What the emulator cannot answer, answered

- **A stalled writer.** There is no renewal to stop and no expiry to wait
  for (ADR 0136, decision 9). A writer that stops writing is superseded by
  the next open's claim, which fences the next log slot; its next write is
  refused with `WriterSupersededException`, whenever it comes, and lands
  nowhere: a third writer read the second's commit and nothing of the
  refused one. The fence slot alone prevents the split-brain append; there is
  no lease to fail first. Verified on the account (`StalledWriterIsFenced`).
- **Racing create-if-absent and replace-if-unchanged.** 20 rounds × 32
  clients each: one winner every round, the object the winner's bytes; the
  31 losers answered `409 BlobAlreadyExists` and `412 ConditionNotMet`
  respectively, 620 of each, nothing else.
- **Transient faults on the commit path, at most once.** Injected by a
  handler in the pipeline, because the service cannot be made to throttle on
  demand: a `503` with `Retry-After` before a create is retried and reaches
  the service once; a `500` before a create is not retried, nothing reaches
  the service, and the caller is told the outcome is unknown; an answer lost
  after the service applied a create is not retried, the caller's own retry
  is told `409`, and the blob holds the first attempt's bytes. The whole
  workload, with a fault of each of five kinds at every request, reopened
  with nothing lost or doubled (§4).
- **Read-after-write.** A second client, with a connection pool of its own,
  read and listed each of 50 blobs the moment the first client's create
  returned, at the create's version; a follower opened right after the
  writer sealed its first segment read it whole and at the writer's head.
- **Latency**, informational only, from one machine on one afternoon, not a
  performance claim: 100 of each, sequential, milliseconds.

  | Operation | p50 | p95 | p99 | max |
  |---|---|---|---|---|
  | create-if-absent, 1 KiB | 25 | 27 | 47 | 977 (the first request: connection, TLS) |
  | read, 1 KiB | 23 | 28 | 35 | 48 |
  | commit of one quad through the dataset | 26 | 42 | 65 | 129 |

  A commit is one create (ADR 0135), so its median is the round trip. The
  client is the workstation of §1; the service is in the region the
  maintainer records there.

## 7. ADRs touched

- **ADR 0160**, dated amendment: a refused replace of the reader record is
  read back before it means forgotten (#145). No ruling in the ledger
  changes.
- **ADR 0134**, dated amendment: the service's answers, observed; a replace
  of nothing is `412` on the service and `404` on Azurite. No ruling changes.
- **ADR 0136** is confirmed, not contradicted: the fence held under every
  schedule the suites ran on the service, and the chained claim of §5.4 is
  its decision 2.
- No ADR is contradicted; nothing was changed silently. Both amendments are
  filed unaccepted (ADR 0066).

## 8. What remains untested, and why

- **The service's own throttling and server errors** (`429`, `503`, `500`
  from the service rather than from the handler): they cannot be induced on
  demand without the control plane or load the test account is not for. The
  adapter's handling of them is tested by injection.
- **Hierarchical-namespace accounts, geo-redundant SKUs, versioning and
  soft delete on**: the backend targets a plain flat-namespace account, as
  the template sets; other account shapes are a separate question.
- **The server legs**: `FleetTests` and `ObjectStorageServerTests` run two
  server processes on Azurite and were not run on the account; they are
  Azurite-gated by `VARVE_AZURITE` and would need the same fixture work in
  `Varve.Server.Tests`.
- **Workload identity and the managed-identity endpoint**: the CI leg's and
  Container Apps' token sources were not exercised from a workstation; only
  the CLI source was.
- **Token expiry during a run**: the run was shorter than a token's life, so
  the five-minute refresh margin was not crossed.
- **Uploads near the 5,000 MiB single-request limit** (ADR 0158): nothing
  that large was written.
- **A soak on the object-storage backend**: the benchmark soak is the file
  backend's (#61); none exists for objects.
- **The fault suite at every request** ran once, with the default six-step
  workload; `VARVE_HTTP_FAULT_STEPS` makes it deeper and
  `VARVE_AZURE_FAULT_STRIDE` thinner.

## 9. Stubs and deferred items

- None of the live tests is a stub. The tracing handler in the fixture
  (`VARVE_AZURE_TRACE`) is a diagnostic, off unless named.
- `azure-objects.yml` still filters the optional CI leg by the two original
  class names; switching it to the trait would run the whole leg there,
  about three minutes plus the fault suite, which is the maintainer's call.
- The region of the account is the maintainer's to record in §1.
