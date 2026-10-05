# Varve.Store.BrowserTests

The browser storage backends' tests, run in a browser (ADR 0084). A
`browser-wasm` app, `wasm-experimental` like `Varve.WasmSmoke`, and a layer 6
host (ADR 0060).

It runs, and reports every check of:

- **the storage contract** — `../Varve.Store.Tests/StorageContractCases.cs`,
  the same file xunit runs on the memory and file backends — against each
  backend the context has;
- **the lease**: a second opener of a dataset refused, and released by
  disposal;
- **seals and lengths across a reopen**;
- **a dataset with disk runs** (maintenance driven by `MaintainAsync`, which is
  off by default in a browser), a checkpoint, a reopen and as-of reads, all
  through the synchronous blob read;
- **the deterministic script** (`DeterministicScript.cs`) on the origin
  private file system, printing a SHA-256 of every file under `log/`, read
  through `File` rather than through the backend;
- **measurements**: a 4 KiB block read, in order and at random, from a 16 MiB
  blob; opening and publishing it; a 256-byte append and flush.

The page loads it twice. `index.html` starts .NET in a dedicated worker, where
synchronous access handles exist: both backends run, and `OpenAsync` must
choose the origin private file system. `index.html?thread=main` starts .NET on
the page's thread, where they do not: IndexedDB runs, `OpenAsync` must choose
it, and the file system backend must refuse.

## Running it

```bash
dotnet build tests/Varve.Store.BrowserTests -c Release -p:WarningsNotAsErrors=CS0618
dotnet run eng/browser-tests.cs -- --chrome /path/to/chrome
```

The override is ADR 0066's, while ADR 0084's decisions are unaccepted; drop
it once they are. `eng/browser-tests.cs` serves the app bundle on localhost (a
secure context, which the origin private file system needs), starts Chromium
headless, reads `globalThis.varveReport` over the DevTools protocol, runs the
deterministic script on `FileStorage` here, and requires the two `log/`s to be
byte-identical. It finds Chromium from `--chrome`, `VARVE_CHROME`,
`CHROME_PATH`, or `google-chrome`/`chromium` on `PATH`. Exit 0 is a pass.

By hand: serve `bin/Release/net10.0/browser-wasm/AppBundle` over HTTP on
localhost and open `index.html`; the report is on the page and on
`globalThis.varveReport`.

The timer in a page that is not cross-origin isolated has 0.1 ms resolution,
so the flush figures are coarse; the block reads are timed in bulk.
