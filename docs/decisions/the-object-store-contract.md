---
set: the-object-store-contract
namespace: varve
adr: 0134
decisions:
  - key: FiveOperations
    statement: "The object-store contract is IObjectStore in Varve.Store beside IStorage: create-if-absent, replace-if-unchanged against an opaque version, ranged read, list by prefix, and delete, and nothing else"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: ProvidersAreTheirOwnPackages
    statement: "Each provider's REST and signing code is a layer-5 package of its own referencing only Varve.Store; Varve.Store.ObjectStorage implements IStorage over IObjectStore and knows no provider; the host composes the two"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: RestOverHttpClient
    statement: "The adapters speak each provider's REST API over HttpClient; no provider SDK is referenced, because the Azure SDK's closure carries P/Invoke and some twenty packages"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: AzureServiceVersionPinned
    statement: "Varve.Store.Azure sends one pinned x-ms-version on every request, named in ADR 0134 and changed only by an amendment"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: LostRaceIsMappedPerProvider
    statement: "Each adapter maps its provider's refusal of a condition to the contract's lost race: Azure's 409 BlobAlreadyExists and 412 ConditionNotMet, S3's 412 PreconditionFailed, with S3's 409 ConditionalRequestConflict retried"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: ConditionalWritesProbedAtOpen
    statement: "Every open of the object-storage backend for write probes under a random key of the reserved probe/ that a second create-if-absent and a replace against a stale version are both refused, and refuses to open a store where either succeeds; a follower, which never writes the log, does not probe"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: S3ServerChosenByTheSuite
    statement: "CI's S3-compatible server is the first of SeaweedFS and RustFS to pass the storage contract suite and the conditional-write race property; if neither passes, no S3-compatible adapter ships"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: BackendContractsNameTheStore
    statement: "The object-storage packages' contracts may name Varve.Store's types: they exist only to implement its contracts, so every consumer of them already depends on it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: AdaptersSpeakHttp
    statement: "Varve.Store.Azure and Varve.Store.S3 set VarveSpeaksHttp: a container, bucket or endpoint address is a transport address, held as System.Uri, and never an IRI"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: AFollowerWritesOnlyItsReaderRecord
    statement: "A follower writes one object, its reader record under readers/, and its identity's write access is scoped to that prefix; it still never writes the log, the writer record, the catalog or an extent"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
---

The rulings of [ADR 0134](../adr/0134-the-object-store-contract.md), filed unaccepted by milestone
Object storage of #92 (ADR 0066).
