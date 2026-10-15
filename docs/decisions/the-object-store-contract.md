---
set: the-object-store-contract
namespace: varve
adr: 0134
decisions:
  - key: FiveOperations
    statement: "The object-store contract is IObjectStore in Varve.Store beside IStorage: create-if-absent, replace-if-unchanged against an opaque version, ranged read, list by prefix, and delete, and nothing else"
  - key: ProvidersAreTheirOwnPackages
    statement: "Each provider's REST and signing code is a layer-5 package of its own referencing only Varve.Store; Varve.Store.ObjectStorage implements IStorage over IObjectStore and knows no provider; the host composes the two"
  - key: RestOverHttpClient
    statement: "The adapters speak each provider's REST API over HttpClient; no provider SDK is referenced, because the Azure SDK's closure carries P/Invoke and some twenty packages"
  - key: AzureServiceVersionPinned
    statement: "Varve.Store.Azure sends one pinned x-ms-version on every request, named in ADR 0134 and changed only by an amendment"
  - key: LostRaceIsMappedPerProvider
    statement: "Each adapter maps its provider's refusal of a condition to the contract's lost race: Azure's 409 BlobAlreadyExists and 412 ConditionNotMet, S3's 412 PreconditionFailed, with S3's 409 ConditionalRequestConflict retried"
  - key: S3ServerChosenByTheSuite
    statement: "CI's S3-compatible server is the first of SeaweedFS and RustFS to pass the storage contract suite and the conditional-write race property; if neither passes, no S3-compatible adapter ships"
  - key: BackendContractsNameTheStore
    statement: "The object-storage packages' contracts may name Varve.Store's types: they exist only to implement its contracts, so every consumer of them already depends on it"
  - key: AdaptersSpeakHttp
    statement: "Varve.Store.Azure and Varve.Store.S3 set VarveSpeaksHttp: a container, bucket or endpoint address is a transport address, held as System.Uri, and never an IRI"
---

The rulings of [ADR 0134](../adr/0134-the-object-store-contract.md), filed unaccepted by milestone
Object storage of #92 (ADR 0066).
