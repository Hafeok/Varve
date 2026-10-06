# 0084 — The browser backend: OPFS sync access handles in a worker, IndexedDB as the fallback

## Status

**Accepted — filed unaccepted by session 6c of #10, 2026-10-05**
(ADR 0066). Acceptance is the maintainer's act on the pull request.

Decides what ADR [0018](0018-storage-abstraction.md) left to milestone 6 for
the browser, what ADR [0073](0073-durability-per-host-declared.md) left to 6c
("the browser backend declares what 6c finds"), and answers ADR
[0071](0071-synchronous-reads-over-asynchronous-storage.md)'s revisit
condition for the browser: **it is not met.** A synchronous read exists in a
browser worker, so ADR 0071 stands and no cursor becomes asynchronous. It
supersedes nothing.

**Revisit condition:** a browser this project supports that has neither
synchronous access handles in a dedicated worker nor IndexedDB, or a dataset
that must be served on the page's own thread at a size the IndexedDB
fallback's memory bound (below) cannot hold.

## Context

ADR 0071 made derived data read synchronously, through
`IReadableBlob.Read(offset, span)`, and made its own revisit condition "a host
where no synchronous read exists — 6c decides this for the browser". ADR 0018's
table already named the two browser candidates: an OPFS synchronous access
handle in a worker, or one IndexedDB record per chunk. This session tried the
first from .NET and measured it. What it found, on .NET 10.0.401 (runtime
10.0.12, `wasm-tools` and `wasm-experimental` 10.0.112), in headless Chromium
141.0.7390.37:

1. **`[JSImport]` reaches `FileSystemSyncAccessHandle.read` synchronously.**
   A source-generated import whose parameter is `Span<byte>` marshalled as
   `JSType.MemoryView` hands the JavaScript side a view of the caller's span
   for the duration of the call. `handle.read(buffer, { at })` is synchronous,
   so the call returns to .NET with the bytes in the span. Trimmed publish of
   the test app: no IL warning. Measured: a 4 KiB block in 5–12 µs through the
   documented `IMemoryView.set` (a copy from a scratch buffer), 5 µs through the
   undocumented `_unsafe_create_view` (none) — against about 0.8 µs for
   `RandomAccess.Read` on the desktop (ADR 0071's table). The browser read is
   an order of magnitude slower per block and still a small part of a scan.
2. **Synchronous access handles exist only in a dedicated worker.** The
   standard declares `FileSystemSyncAccessHandle` `[Exposed=DedicatedWorker,
   SecureContext]` (File System Standard §2.6). .NET's `dotnet.js` starts in a
   module worker unchanged: `import { dotnet } from './_framework/dotnet.js'`
   inside `new Worker(url, { type: 'module' })` ran the runtime, the imports
   and the store. On the page's thread the type does not exist.
3. **The `[JSImport]` generator emits unsafe code.** A library declaring one
   without `AllowUnsafeBlocks` fails with `SYSLIB1074: JSImportAttribute
   requires unsafe code` and `CS0227`. No shipped package has turned it on
   before.
4. **A synchronous access handle locks its file, exclusively** (§2.1: the
   lock is "taken-exclusive"). Measured in Chromium 141, with a handle open on
   a file: a second `createSyncAccessHandle` throws `NoModificationAllowedError`;
   `move()` *onto* it, `move()` *of* it and `removeEntry` of it all throw
   `NoModificationAllowedError`. Two handles in `mode: 'read-only'` coexist,
   but a read-only handle locks the destination of a move all the same.
   `move()` onto an existing, unlocked file replaces it. `move()` is on
   `FileSystemFileHandle.prototype` in Chromium; it is not in the standard yet.
5. **A `File` snapshot does not survive replacement.** After its name is
   replaced, reading a `File` taken by `getFile()` throws `NotReadableError`,
   synchronously through `FileReaderSync` too, and a `FileReaderSync` read of a
   4 KiB slice cost 1.4 ms. It is not a read path.
6. **`flush()` is specified as "Synchronously persists any changes made
   through handle to the file associated with handle to disk"** (§2.6.5). The
   standard says nothing of the device's cache, of what survives power loss,
   or of the storage bucket: an origin's storage is **best-effort** unless the
   page was granted `navigator.storage.persist()`, and best-effort storage may
   be evicted by the browser under pressure. In a private window the origin
   private file system lives in memory. This session did not read Chromium's
   implementation of `flush()` per platform, and states that rather than
   guesses it.

## Decision

### Two backends in one package, `Varve.Store.Browser`, layer 5

**`Varve.Store.Browser`**, a shipped package at **layer 5** (an integration of
the store with a host's storage), references `Varve.Store` only. Its public
surface is `BrowserStorage` (an `IStorage`, `IAsyncDisposable`) with three
openers, and its model, `Varve.Store.Browser.Model`: `BrowserDatasetName`,
`BrowserBackend` and `BrowserStorageOptions` (ADR 0069's shape).

- `BrowserStorage.OpenAsync(name, options)` opens the **origin private file
  system** backend where synchronous access handles exist, and the
  **IndexedDB** backend otherwise. `OpenOriginPrivateFileSystemAsync` and
  `OpenIndexedDbAsync` choose explicitly; the first refuses with
  `PlatformNotSupportedException` outside a dedicated worker.
- **The two are two places.** A dataset written through one is not visible
  through the other under the same name.

The assembly is `[SupportedOSPlatform("browser")]`, targets `net10.0` and
builds without the WebAssembly workloads, so it is in `Varve.slnx`.

### The origin private file system, in a dedicated worker (`OpfsInADedicatedWorker`)

**.NET runs in a dedicated worker for this backend.** The dataset is a
directory of the origin private file system named by `BrowserDatasetName`,
laid out as `FileStorage` lays out a dataset directory:

- **`log/` has the file backend's names and bytes**: `NNNNNNNN.seg` and
  `MANIFEST`. A segment is appended to and read through one held synchronous
  access handle, and flushed with `flush()`. The manifest is written beside,
  flushed and moved into place, once, before any segment. A log copied out of
  the browser opens on the desktop, and the determinism test (below) proves the
  bytes are the same.
- **A seal is in the bytes** (ADR 0072). The backend's own record of it is
  `derived/SEALED`, the highest sealed segment's id; every segment but the
  newest is sealed by construction. A lost `SEALED` reports the newest
  segment open, and the store's recovery seals it again from its trailer, as
  it does after a copy that loses a file attribute.

### The synchronous read (`SynchronousReadThroughJSImport`)

`IReadableBlob.Read` calls `FileSystemSyncAccessHandle.read` through a
source-generated `[JSImport]`, the caller's span marshalled as a memory view,
and returns with the bytes in it. **This is ADR 0071's synchronous read on the
browser**, and the reason its revisit condition is not met. The JavaScript
reads into a scratch buffer and copies with `IMemoryView.set`, the documented
member; reading straight into the span through the undocumented
`_unsafe_create_view` would save about half the per-block cost and is not
taken.

### Published blobs are generations (`BlobsAreGenerations`)

Fact 4 rules out FileStorage's publish — rename over the name — whenever a
reader holds the old blob, and rules out its Windows path too (ADR 0071's
amendment), because a locked file cannot be moved aside. So a blob named `N`
is stored as `derived/N~G`, `G` a generation above every other:

- **publish** writes `N~G~tmp`, flushes it, closes it and moves it to `N~G`.
  The highest generation of a name is the blob, so the move is the atomic
  replacement — **across a crash too**, which the Windows path is not;
- the previous generation is removed when no reader holds it, at the next
  call that can, or at the next open, which also removes unpublished
  temporaries and every generation but the highest;
- readers of one file share one handle, reference-counted, because the handle
  is exclusive;
- deleting a blob a reader holds removes it from the listing at once and the
  file later; a crash before then brings the blob back at the next open,
  which is ADR 0071's deferred delete, as FileStorage does it on Windows.

The names without `~` in `derived/` are the backend's: `LOCK`, `LOCK.owner`,
`SEALED`.

### The JavaScript ships in the assembly (`ModuleShippedInTheAssembly`)

The module the imports call is a constant in the assembly, imported through
`JSHost.ImportAsync` from a `data:` URL, at every open (the browser's module
map makes a repeat import the first one's module, so no static state is
kept). **A host deploys nothing and registers nothing.** Rejected: a static web
asset (`_content/Varve.Store.Browser/…`), which needs the Razor or
WebAssembly SDK's static asset pipeline in the consumer and a URL the host
must serve; and `setModuleImports` by the host, which makes every host carry
the backend's JavaScript. The cost: a page whose Content Security Policy
forbids `data:` for scripts cannot use the package unchanged. No such host
exists yet; one that does is a reason to add a URL option, not to change the
default.

### `AllowUnsafeBlocks`, for generated code only (`GeneratedInteropNeedsUnsafeBlocks`)

`Varve.Store.Browser` compiles with `AllowUnsafeBlocks`, **because the
`[JSImport]` generator emits unsafe code and refuses to run without it** (fact
3). No line written by hand in the package is unsafe; the attribute is the
only interop the package declares. ADR 0071 kept unsafe code out of
`Varve.Store` for a raw-pointer read path; this is a different question —
whether a package may contain the platform's own generated marshalling — and
it is answered only for this package. No other shipped package turns it on.

### Durability: both backends declare `Committed` (`BrowserBackendsDeclareCommitted`)

**`Synchronised` is too strong for the origin private file system.** It
promises, in ADR 0073's words, that a commit survives power loss on a device
that honours its flush. The browser promises less, and promises it about
something else:

- `flush()` "persists … to disk" (fact 6): the standard's whole statement. It
  does not say the device's cache is flushed, and this session did not verify
  what Chromium calls on each platform;
- the file's name lives in the browser's own bookkeeping of the origin's
  storage, not in a directory this process can flush;
- **the origin's storage is best-effort unless persistence was granted**, and
  the browser may evict it. A dataset can be lost without any power loss;
- in a private window the storage is memory.

What a completed flush means here is "the browser's storage reported it
done", and what that survives is the browser's. That is what `Committed`
says — "handed to a store that reported success" — and it is what ADR 0018's
table already wrote for the browser. **The maintainer's "TransactionCommitted"
is `Durability.Committed`**; no member is renamed. The IndexedDB backend
declares `Committed` in its plainest sense: a flush returns when a
`durability: 'strict'` transaction completes. A host that needs the dataset
kept asks for `navigator.storage.persist()`; the package does not, because in
some browsers that prompts the user.

### IndexedDB, where synchronous access handles do not exist (`IndexedDbIsTheFallback`)

One database per dataset, `varve/<name>`, three object stores:

- `chunks`: a segment's bytes, **one record per flush**, keyed by segment and
  offset, put in one strict transaction with the segment's length and seal in
  `meta`. Bytes appended and not yet flushed are held in memory until the
  flush the store makes after each commit, and reads see them;
- `meta`: each segment's length and seal, and the manifest, written once by a
  transaction that refuses when it exists;
- `blobs`: each derived blob whole; a put is the atomic replacement, a delete
  removes it.

**IndexedDB has no synchronous read.** So opening a blob (`OpenAsync`, which
ADR 0071 lets fetch) reads it whole into a managed array, and every `Read`
after that is a copy from it. **The bound: an open blob costs its length in
managed memory for as long as it is open**, and a blob is written from memory
too, so the largest blob is bounded by what the tab may hold. The log is read
from IndexedDB on demand and is not bounded this way. A reader keeps the bytes
it opened when the name is replaced, as the contract requires.

### What happens on the main thread

`BrowserStorage.OpenAsync` on the page's thread opens IndexedDB, with the
bound above. ADR 0071 holds there too: the read is synchronous, from memory.
A host that wants the origin private file system runs .NET in a dedicated
worker, which is two lines of JavaScript (the test app's `main.js` and
`worker.js`).

### One opener at a time (`LeaseIsAnExclusiveAccessHandle`, `LeaseIsAWebLock`)

ADR 0075's rule, held by the browser instead of the operating system:

- **the origin private file system backend's lease is a synchronous access
  handle on `derived/LOCK`**, exclusive across every tab and worker of the
  origin (fact 4). The browser releases it when the handle is closed or the
  worker ends, however it ends, so a stale lease does not exist. A refused
  opener reads `derived/LOCK.owner` for the message; it names a browser
  context and the time from the injected clock, there being no process id;
- **the IndexedDB backend's lease is a Web Lock** (`navigator.locks.request`
  with `ifAvailable`) named `varve/<name>`, held until disposal, released by
  the browser when the context ends.

A second opener is refused with `DatasetLeasedException` in both.

### Maintenance stays off by default in a browser (`MaintenanceStaysOffInABrowser`)

`DatasetOptions.Maintenance` already defaults to `Off` when
`OperatingSystem.IsBrowser()` (ADR 0042's amendment). That is kept: a browser
worker that the page may suspend or terminate at any moment is not a place to
start a task the host did not ask for. The host drives maintenance with
`Dataset.MaintainAsync`, and the test app does, writing disk runs and reading
them back through the synchronous read.

### The model (`BrowserStoreModel`)

`Varve.Store.Browser.Model` holds what `BrowserStorage`'s members name:
`BrowserDatasetName` (a path of ASCII parts, not a host path; the browser's
counterpart of `DatasetDirectory`), `BrowserBackend`, `BrowserStorageOptions`.

### How it is tested

- **The storage contract runs in headless Chromium against both backends.**
  The cases of `StorageContractTests` moved into
  `tests/Varve.Store.Tests/StorageContractCases.cs`, plain methods with no test
  framework; xunit's tests delegate to them on the desktop backends, and
  `tests/Varve.Store.BrowserTests` (a layer 6 `browser-wasm` host, like
  `Varve.WasmSmoke`) links the same file and runs it in the browser. It loads
  twice: .NET in a worker (both backends) and on the page's thread (IndexedDB,
  and the refusal of the other). Beside the contract: the lease, seals and
  lengths across a reopen, a dataset with disk runs and a checkpoint read as of
  through the synchronous read, and measurements.
- **Determinism across hosts.** `DeterministicScript` — a fixed dataset id, a
  clock that steps one second per reading, fixed requests with retracts, named
  graphs, metadata and a commit split over records, 2 KiB segments so the log
  seals ten of them, and a close and reopen halfway — runs on `FileStorage` on
  the desktop and on the origin private file system in the browser. **`log/`
  is byte-identical**: twelve files, compared by SHA-256, the browser's read
  through `File`, not through the backend.
- **`eng/browser-tests.cs`** drives it with the BCL alone: a static file
  server on `TcpListener`, Chromium headless with a DevTools port, the DevTools
  protocol over `ClientWebSocket`, the report from `globalThis.varveReport`.
  No package, no browser download. CI's `browser-tests` job runs it with the
  runner's Chrome; it is outside `eng/ci.cs`, as the `wasm` job is (ADR 0036:
  the devcontainer has no browser).

Measured on this session's machine, Chromium 141, timer resolution 0.1 ms in
a page that is not cross-origin isolated, so the flush figures are coarse:

| | OPFS (worker) | IndexedDB (worker) | IndexedDB (page) |
|---|---:|---:|---:|
| 4 KiB block read, in order, 16 MiB blob | 6–12 µs | 1.5–1.8 µs (memory) | 1.5–3.0 µs (memory) |
| 4 KiB block read, 10,000 at random | 8–11 µs | 1.9–2.4 µs | 2.0–4.4 µs |
| open a 16 MiB blob | 3–17 ms | 33–73 ms | 30–44 ms |
| publish a 16 MiB blob | 48–63 ms | 107–243 ms | 108–146 ms |
| append 256 bytes and flush, median | 0.2–0.3 ms | 1.2–1.4 ms | 1.1–1.3 ms |

Ranges are over three runs. The IndexedDB reads are copies from managed
memory, which is why they are faster than the file system's; their cost was
paid at open. The file system flush is fast enough on this machine that it
may not reach a device at all; that is another reason not to call it
`Synchronised`, and not a figure to rely on.

## Alternatives considered

- **Make the cursor asynchronous** (ADR 0071's named alternative if no
  synchronous read existed). Not needed: one exists, in a worker, and on the
  page's thread the fallback serves one from memory.
- **The origin private file system on the page's thread, through
  `getFile()` and `createWritable()`**, so that the worker and the page see one
  dataset. Asynchronous only, like IndexedDB, so the blob is still read into
  memory at open; and `createWritable` swaps the file on `close()` with no
  flush the standard defines. The maintainer asked for IndexedDB, whose
  transactions do say when they are committed.
- **`FileReaderSync` on a `File` snapshot** for the synchronous read, so that
  the read needs no exclusive handle. Fact 5: 1.4 ms per 4 KiB, and the
  snapshot dies when its name is replaced.
- **Read-only access handles** (`mode: 'read-only'`) so that readers need no
  sharing. They coexist with each other, but still lock a move's destination
  (fact 4), are Chromium's alone today, and a shared handle is portable.
- **Rename over the name, as `FileStorage` does**, with a fallback while a
  reader holds it. There is no fallback: a locked file cannot be moved aside.
- **`Synchronised` for the origin private file system.** Rejected above.
- **The JavaScript as a static web asset, or registered by the host.**
  Rejected above.
- **Running .NET multithreaded** (`WasmEnableThreads`) to keep the page's
  thread free while a worker reads. It requires cross-origin isolation of
  the host page, and is not needed: a host that wants
  the page free runs the whole runtime in a worker.

## Consequences

- **A browser host chooses where .NET runs.** In a worker it gets files and
  synchronous reads from them; on the page it gets IndexedDB and a memory
  bound per open blob. The two are two datasets.
- **`log/` from the browser is the desktop's `log/`.** A dataset can be copied
  out of the origin private file system and opened by `FileStorage`.
- **Unsafe code is admitted in one shipped package**, generated, and the
  package says so in its project file.
- **The browser's durability is weaker than the file backend's**, by
  declaration: `Dataset.Durability` reports `Committed` there.
- **CI gains a browser job** that runs the contract, the lease, the
  determinism comparison and the measurements in Chromium on every push.
- `eng/browser-tests.cs` is the one script in `eng/` that references a Varve
  project (the desktop half of the determinism comparison), and the one with
  CA2266 off, because the licence notice must be its first line.

## Checks

- **Checked against the accepted ADRs** (0001–0077). Completes **0018** for
  the browser (its table's browser column, as decided here, and its
  `Committed`), and **0073** ("the browser backend declares what 6c finds").
  Does not meet **0071**'s revisit condition, and says why. Applies **0075**'s
  rule with the browser's locks, **0072**'s seal-in-bytes with a bookkeeping
  file, **0042**'s amendment unchanged, **0060** (the test app is a layer 6
  host), **0069** (a model namespace for a layer 5 package) and **0066** (filed
  unaccepted). No conflict with any. 0078–0083 are being filed in parallel and
  were not read.
- **Layer ownership.** `Varve.Store.Browser`, **layer 5**, referencing
  `Varve.Store` only.
- **Analyzer rule.** None new. `IReadableBlob.Read`'s browser implementation is
  `[HotPath]` and answered by `SynchronousReadThroughJSImport`.
- **Open questions owned.** None. Closes ADR 0071's revisit condition for the
  browser, and ADR 0073's open browser row.
