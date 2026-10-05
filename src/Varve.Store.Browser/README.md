# Varve.Store.Browser

Browser storage for a Varve store (ADR 0084).

- **The origin private file system, in a dedicated worker.** Segments and
  derived blobs are files, read and written through synchronous access
  handles; a derived blob is read synchronously into the caller's span, which
  is what the store's scan needs. `log/` has the same names and bytes as the
  file backend's, so a log copied out of the browser opens on the desktop.
- **IndexedDB elsewhere** — the page's own thread, or a browser without
  synchronous access handles. An open derived blob is read into memory whole,
  which bounds it by what the tab may hold.
- **Both declare `Durability.Committed`**: a flush returns when the browser's
  storage reports it done. An origin's storage is best-effort unless the page
  is granted `navigator.storage.persist()`, which the host asks for if it
  needs to.
- **One opener at a time** across the origin's tabs and workers, released by
  the browser when the context ends; a second is refused with
  `DatasetLeasedException`.
- **Maintenance is off by default** in a browser; call `Dataset.MaintainAsync`.

```csharp
// In a dedicated worker: the origin private file system. On the page: IndexedDB.
await using BrowserStorage storage = await BrowserStorage.OpenAsync(
    new BrowserDatasetName("datasets/notes"),
    new BrowserStorageOptions { Clock = TimeProvider.System });

await using Dataset dataset = await Dataset.OpenAsync(storage, new DatasetOptions { Clock = TimeProvider.System });
```

To run .NET in a worker, start `dotnet.js` from a module worker:

```js
// main.js
new Worker('./worker.js', { type: 'module' });

// worker.js
import { dotnet } from './_framework/dotnet.js';
const { getAssemblyExports, getConfig } = await dotnet.create();
```

The package ships its JavaScript inside the assembly and imports it from a
`data:` URL, so there is nothing to deploy; a Content Security Policy that
refuses `data:` scripts refuses it. It is compiled with `AllowUnsafeBlocks`
because the `[JSImport]` source generator requires it; no code written in it
is unsafe.

Layer 5 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
