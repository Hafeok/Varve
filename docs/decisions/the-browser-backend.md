---
set: the-browser-backend
namespace: varve
adr: 0084
decisions:
  - key: OpfsInADedicatedWorker
    statement: "In a browser the dataset is kept in the origin private file system through synchronous access handles, which exist only in a dedicated worker, so .NET runs in a worker for this backend; log/ has the file backend's names and bytes"
  - key: SynchronousReadThroughJSImport
    statement: "A derived blob is read synchronously by FileSystemSyncAccessHandle.read, called through a source-generated JSImport with the caller's span marshalled as a memory view, which closes ADR 0071's revisit condition for a browser worker"
  - key: GeneratedInteropNeedsUnsafeBlocks
    statement: "Varve.Store.Browser compiles with AllowUnsafeBlocks because the JSImport generator emits unsafe code; no unsafe code is written by hand in it, and no other shipped package turns it on"
  - key: ModuleShippedInTheAssembly
    statement: "The JavaScript the backends call is a constant in the assembly, imported once per runtime through JSHost.ImportAsync from a data URL, so a host deploys and registers nothing"
  - key: BrowserBackendsDeclareCommitted
    statement: "Both browser backends declare Committed: a flush returns when the browser's storage reports it done, and what that survives is the browser's, which may also evict a best-effort origin"
  - key: IndexedDbIsTheFallback
    statement: "Where synchronous access handles do not exist the dataset is kept in IndexedDB, chunks and blobs committed by strict transactions, and an open blob is read whole into memory so that its reads are synchronous"
  - key: BlobsAreGenerations
    statement: "On the origin private file system a derived blob is published as a new generation of its name by a flushed temporary and a move, the highest generation is the blob, and older ones are removed once no reader holds them"
  - key: LeaseIsAnExclusiveAccessHandle
    statement: "The origin private file system backend's lease is an exclusive synchronous access handle on derived/LOCK, released by the browser when the worker ends"
  - key: LeaseIsAWebLock
    statement: "The IndexedDB backend's lease is a Web Lock named after the dataset, taken if available and released by the browser when the context ends"
  - key: BrowserStoreModel
    statement: "Varve.Store.Browser.Model is the browser backend's model namespace and holds BrowserDatasetName, BrowserBackend and BrowserStorageOptions"
  - key: MaintenanceStaysOffInABrowser
    statement: "Maintenance stays off by default in a browser; a host drives it with MaintainAsync"
---

The rulings of [ADR 0084](../adr/0084-the-browser-backend.md), filed unaccepted by a session of
milestone 6c of #10 (ADR 0066).
