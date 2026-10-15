# 0134 — The object-store contract: five operations, one package per provider, REST over `HttpClient`

## Status

**Accepted — filed unaccepted by milestone Object storage of #92, 2026-10-15**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the Object storage plan, 2026-10-15: REST rather than a
provider SDK; the providers in packages of their own, the contract in
`Varve.Store`; the S3-compatible server chosen by the contract suite.

## Context

Azure Container Apps has no block disk. Azure Files breaks three things the
file backend rests on: the OS lease (ADR 0075), `fsync` (ADR 0073) and the
synchronous read over a local file (ADR 0071). A durable Varve on a
serverless container, or on any host whose only durable storage is an
object store, needs a fourth implementation of the storage contract (ADRs
0018, 0040, 0071).

ADR 0018 rejected "one key-value blob store, append emulated" as the
contract itself: chunking is the segment store in disguise. That stands.
The contract a store backend implements is still `IStorage`; what this ADR
adds is the much smaller contract **beneath** one implementation of it — the
operations an object store must offer for the log to live in it.

Every mainstream object store now offers conditional writes, and those are
all a single-writer, write-once log needs:

- **Azure Blob Storage**: `If-None-Match: *` on Put Blob performs the write
  "only if the resource doesn't exist, and fail[s] the operation if it does
  exist"; `If-Match` on Put Blob, Put Block List and Delete Blob; a failed
  condition is `412 ConditionNotMet` ([conditional headers](https://learn.microsoft.com/rest/api/storageservices/specifying-conditional-headers-for-blob-service-operations),
  [error codes](https://learn.microsoft.com/rest/api/storageservices/common-rest-api-error-codes)).
  A create-if-absent that finds a blob is answered **`409 BlobAlreadyExists`**
  rather than 412. The REST reference does not state that; Azurite, which
  copies the service's error precedence, does in its source
  (`WriteConditionalHeadersValidator.ts`: "Put Blob, Commit Block List has
  special logic for ifNoneMatch equals \*, will return 409 conflict for
  existing blob"), and the adapter's tests against Azurite prove it.
  Storage is strongly consistent: "after the service performs an insert or
  update operation, subsequent read or list operations return the latest
  update" ([concurrency](https://learn.microsoft.com/azure/storage/blobs/concurrency-manage)).
  A ranged read is `x-ms-range` on Get Blob, a list is List Blobs with
  `prefix`, at most 5,000 a page.
- **Amazon S3**: `If-None-Match: *` on PutObject since 20 August 2024 and
  `If-Match` since 25 November 2024
  ([announcement](https://aws.amazon.com/about-aws/whats-new/2024/08/amazon-s3-conditional-writes),
  [announcement](https://aws.amazon.com/about-aws/whats-new/2024/11/amazon-s3-functionality-conditional-writes)).
  A failed condition is `412 Precondition Failed`; "if a conflicting
  operation occurs during the upload S3 returns a 409
  ConditionalRequestConflict response. On a 409 failure you should retry"
  ([PutObject](https://docs.aws.amazon.com/AmazonS3/latest/API/API_PutObject.html)).
  "If multiple conditional writes … occur for the same object name, the
  first write operation to finish succeeds"
  ([conditional writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html)).
  Reads and lists are strongly consistent after a write
  ([S3](https://docs.aws.amazon.com/AmazonS3/latest/userguide/Welcome.html)).
- **Google Cloud Storage**: "the generation-match precondition accepts the
  value `0` as a special case … the request only proceeds if no object with
  the specified name exists"; `x-goog-if-generation-match` otherwise; a
  failed precondition is `412`
  ([preconditions](https://docs.cloud.google.com/storage/docs/request-preconditions)).

## Decision

**1. The contract is five operations, and it is `IObjectStore` in
`Varve.Store`.** Create-if-absent, replace-if-unchanged against an opaque
version, ranged read, list by prefix, delete. Nothing else: no append, no
lease, no multipart, no copy, no metadata. It lives in `Varve.Store` (layer 4),
beside `IStorage`, which is the lowest layer that can define it without
knowing its implementers. Its names, versions, entries and reads are types in
the store's model namespace, `Varve.Store.Log`, as `BlobName` is.

- A lost race is not an exception: create-if-absent and replace-if-unchanged
  return `null`. An exception from a write means its outcome is unknown, and
  the caller finds out by reading (ADR 0135).
- Reads and lists see every write that has returned. A provider that cannot
  promise that cannot be adapted.

**2. Each provider is a package of its own; the backend knows none.**
`Varve.Store.ObjectStorage` implements `IStorage` over `IObjectStore` and
references only `Varve.Store`. `Varve.Store.Azure` and `Varve.Store.S3` are
each a layer-5 package referencing only `Varve.Store`. The server, at layer 6,
composes an adapter into the backend. A provider's change is a change to its
own package; the backend is not touched, and no package references another
at its own layer. Their contracts may name `Varve.Store`'s types
(`ArchContractTypeAssemblies`, set in each project and nowhere else, as ADR
0064 requires): they exist only to implement its contracts.

**3. REST over `HttpClient`; no provider SDK.** The Azure SDK clears a Native
AOT publish with no warnings (measured: `Azure.Storage.Blobs` 12.30.1 and
`Azure.Identity` 1.21.0, linux-x64, `TrimmerSingleWarn=false`, no suppression,
no native file in the output), but its closure brings about twenty pinned
packages: `Azure.Core` without `IsAotCompatible`, MSAL and
`Microsoft.Identity.Client.Extensions.Msal`, whose P/Invoke into `libsecret`
ships whether or not it is reached (brief constraint 1), the 2016
`System.Security.Cryptography.ProtectedData`, and `Apache.Arrow`, new in Blobs
12.30. That fails constraint 1 and constraint 4. The adapters speak the
providers' REST APIs over `HttpClient`, which the BCL ships; their signing is
the BCL's HMAC and SHA-256.

**4. The Azure service version is pinned.** Every request from
`Varve.Store.Azure` carries `x-ms-version: 2025-11-05`, which Azure and
Azurite 3.37.0 both serve. Changing it is an amendment to this ADR, with the
adapter suite re-run against Azurite and, on the optional leg, a real account.

**5. Each adapter maps its provider's refusal to the lost race.** Azure:
`409 BlobAlreadyExists` and `412 ConditionNotMet` from a create-if-absent,
`412 ConditionNotMet` and `404` from a replace-if-unchanged. S3: `412
PreconditionFailed`, and `404` from an `If-Match` on no object; `409
ConditionalRequestConflict` is retried, as the documentation asks. Throttling
(`429`, `503` with `Retry-After`) and `500` are retried by the adapter a
bounded number of times, waiting as `Retry-After` says; waiting decides how
fast, never whether (ADR 0136).

**6. CI's S3-compatible server is chosen by the contract suite.** MinIO is no
longer distributed as an image anyone may pull: its repository was archived
on 2026-04-25, its Docker Hub images deleted in September 2026, and
`quay.io/minio/minio` refuses anonymous pulls. The candidates are
**SeaweedFS** (`ghcr.io/seaweedfs/seaweedfs`, Apache-2.0) and **RustFS**
(`ghcr.io/rustfs/rustfs`, Apache-2.0), both on GHCR. Each runs the full
storage contract suite and the race property of decision 7; the first to
pass is CI's S3-compatible server. If neither passes, `Varve.Store.S3` does
not ship in the release, and the roadmap records the gap: an adapter that
cannot be tested against a correct server is not shipped.

**7. The race property is part of the contract.** Of sixteen clients racing
create-if-absent on one name, exactly one is told it wrote, and the object
holds its bytes; of sixteen racing replace-if-unchanged against one version,
exactly one is. The epoch fence (ADR 0136) rests on the first, the writer
record on the second.

**8. What the S3 and GCS adapters would be.** `Varve.Store.S3` is built here
for any S3-compatible server and for Amazon S3 itself: PutObject with
`If-None-Match: *` and `If-Match`, GetObject with `Range`, ListObjectsV2,
DeleteObject, SigV4. A **GCS** adapter, not built, is the XML API with
`x-goog-if-generation-match: 0` for create-if-absent and the generation as
the version for replace-if-unchanged, a ranged GET, a list with `prefix`, and
DELETE, authenticated by workload identity federation. Nothing in
`Varve.Store.ObjectStorage` changes for either.

## Alternatives considered

- **The Azure SDK.** Admissible on AOT alone, refused on constraints 1 and 4
  (decision 3). Its retry policies and credential chain are the parts worth
  having, and both are a few hundred lines over `HttpClient`.
- **Append blobs, or Put Block and Put Block List as an append.** An append
  blob's append is conditional on length, so it can serve a single writer,
  but it is Azure-only, and an append has no create-if-absent per batch to
  fence a superseded writer with. The chunk log (ADR 0135) needs only what
  every store has.
- **Blob leases.** A lease is renewed on a timer and expires on one, which
  is a decision from elapsed time (ADR 0136).
- **The contract inside `Varve.Store.ObjectStorage`.** The adapters would then
  reference the backend at the same layer, a violation, or the backend would
  reference the adapters, so a provider's change would be the backend's.
- **MinIO built from its archived source, or AIStor Free.** Refused by the
  maintainer: the first would distribute an unmaintained AGPL binary, the
  second's terms were not verified.

## Consequences

- `Varve.Store` gains `IObjectStore`, `ObjectName`, `ObjectVersion`,
  `ObjectEntry` and `ObjectRead`, and `Durability.Replicated` (ADR 0135).
- Three packages: `Varve.Store.ObjectStorage`, `Varve.Store.Azure`,
  `Varve.Store.S3` (the last subject to decision 6). The README's package
  table lists them at layer 5.
- The adapters' suites run against Azurite and the chosen S3-compatible
  server in CI, and against a real storage account on an optional leg.
- No package is added to `Directory.Packages.props`.

## Checks

- **Checked against the accepted ADRs** (0001–0133). Touches **0018** (the
  rejected "append emulated" alternative stands: `IStorage` is still the
  contract; this is beneath one implementation of it), **0040** (a backend
  outside the store's assembly), **0064** (contract vocabulary widened per
  project), **0084** (the precedent for a layer-5 backend). No conflict.
- **Layer ownership.** `IObjectStore` and its model: `Varve.Store`, layer 4.
  The backend and each adapter: layer 5. The composition: `Varve.Server`,
  layer 6.
- **Analyzer rule.** None new. `DD0001` holds the layers.
- **Open questions owned.** None.
