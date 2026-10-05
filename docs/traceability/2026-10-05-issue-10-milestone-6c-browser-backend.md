# Milestone 6c, item 5 — the browser storage backend

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#10](https://github.com/Hafeok/Varve/issues/10) |
| **Date** | 2026-10-05 |
| **Tool** | Claude Code, a subagent in a git worktree, launched by the milestone 6c cloud session; the tool's version was not reported to the subagent, and the maintainer fills it in |
| **Model** | `claude-opus-5-5` (Claude Opus 5.5), as the session's own environment reports it |
| **Session identifier** | `session_01JZjxTGanMjuGR2KWbPjWRi` |
| **Branch** | the worktree branch `worktree-agent-a23e4320781d4864c`, handed back to the launching session; not pushed |

## The prompt

> You are building the browser storage backend for Varve (milestone 6c item 5, Refs #10), in this git worktree of the Varve repository. Read AGENTS.md first; it binds you (DD/VARVE analyzers at error severity, never suppress them, MPL-2.0 header on every .cs file, no reflection, AOT/trim-safe, layering rules, ADR-first). Read docs/adr/0018, 0040, 0071 (synchronous reads of derived data; its revisit condition is exactly this question), 0073 (durability per host; the browser's level is 6c's to declare), 0042's 2026-10-02 amendment (maintenance off by default in browser), docs/spec/storage-format.md, src/Varve.Store/IStorage.cs, src/Varve.Store/MemoryStorage.cs, src/Varve.Store/FileStorage.cs, tests/Varve.Store.Tests/StorageContractTests.cs, and tests/Varve.WasmSmoke (README, csproj, Smoke.cs, main.js). Toolchain: `export PATH=/root/.dotnet:$PATH`; .NET SDK 10.0.401 with wasm-tools and wasm-experimental workloads installed. Chromium is at /opt/pw-browsers (chromium-1194 / chromium_headless_shell-1194); node at /opt/node22/bin/node. Do not add NuGet packages (every package needs an ADR; avoid). Do not run `playwright install`.
>
> Goal (the maintainer's words): "Browser backend: OPFS with synchronous access handles in a worker (decision 0071's synchronous read on the browser); IndexedDB fallback declaring TransactionCommitted durability where OPFS sync handles are unavailable. Contract tests in headless Chromium; the determinism test across desktop and browser (same script, byte-identical log/). If OPFS sync handles cannot be reached from .NET WASM without JS interop that breaks AOT, say so with evidence and bring the alternative; maintenance stays off by default in the browser." Note: the Durability enum's existing member for a transactional store is `Committed` (see src/Varve.Store/Log/Durability.cs) — "TransactionCommitted" in the maintainer's words maps to it; do not rename public members, say so in your report.
>
> What to build:
> 1. A new shipped package `src/Varve.Store.Browser` (layer 5 integration: `<ArchLayer>5</ArchLayer>`, references Varve.Store only; follow how src/Varve.Sparql.Store is set up: PublicAPI files, DomainModel.cs, README, csproj conventions, Directory.Build props). It implements `IStorage` (ISegmentStore + IDerivedStore + IReadableBlob + IBlobWriter) over OPFS using `System.Runtime.InteropServices.JavaScript` `[JSImport]` (source-generated, AOT/trim-safe) and a small JS module the package ships (find the cleanest way: embedded as a static web asset or loaded via JSHost.ImportAsync with a URL the host provides — decide and justify). Reads of derived blobs must be synchronous: `FileSystemSyncAccessHandle.read(view, {at})` called synchronously through JSImport with a `Span<byte>` marshalled as MemoryView — this only works in a dedicated Web Worker, so the .NET runtime must run inside a worker (dotnet.js can be started in a worker). Determine whether [JSImport]'s generated code requires AllowUnsafeBlocks in the package; if so that is a decision to record (ADR) because shipped packages so far have no unsafe code. Writes/flushes/opens may be async. Durability: OPFS sync handle `flush()` → declare what it actually promises (likely `Synchronised` is too strong? research the spec: https://fs.spec.whatwg.org — decide and argue in the ADR). Directory listing, atomic publish of derived blobs (write temp then `move()`/rename — check FileSystemHandle.move support in Chromium), manifest written once, segments sealed (seal must be in-bytes per ADR 0072; backend bookkeeping for sealed state may be a marker file or derived from trailer — mirror what FileStorage does for IsSealed). The one-process lease (ADR 0075): OPFS sync access handles are exclusive per file, which can serve as the lease; say how.
> 2. An IndexedDB fallback backend in the same package, used when sync access handles are unavailable (main thread, or browsers without them), declaring `Durability.Committed`. IndexedDB is async-only, so its synchronous blob read must be served from an in-memory copy loaded at OpenAsync (ADR 0071 allows OpenAsync to fetch) — state that bound.
> 3. Browser contract tests run in headless Chromium: build a test app (e.g. tests/Varve.Store.BrowserTests, browser-wasm, layer 6 host like WasmSmoke) that runs the storage contract cases (share the case logic: you may refactor StorageContractTests' bodies into framework-independent static methods in a file linked into both projects, keeping xunit tests delegating to them) against both browser backends and a dataset-level smoke (commit, checkpoint, reopen, as-of). Drive Chromium headless with the BCL only: a script under eng/ (look at eng/*.cs for the `dotnet run file.cs` style used there) that serves the AppBundle over HTTP (HttpListener or a minimal TcpListener server — no packages), launches chromium with --headless=new --remote-debugging-port, connects via System.Net.WebSockets.ClientWebSocket to the DevTools protocol, waits for `globalThis.varveReport`, prints it, exits non-zero on failure. OPFS requires a secure context: http://localhost counts.
> 4. Determinism across desktop and browser: one deterministic script (fixed DatasetId, injected clock, fixed requests incl. a seal: small SegmentBytes) run on FileStorage on desktop and on the OPFS backend in the browser; the browser app exposes the resulting log/ files' bytes (e.g. SHA-256 per file name in the report, or base64), and the eng script compares to the desktop run's — byte-identical log/ required.
> 5. Wire a CI job in .github/workflows/ci.yml modelled on the existing `wasm` job (install workloads, build, run the eng script with the runner's chromium — ubuntu runners have google-chrome; make the path configurable) and add the job to eng/ci.cs if that file lists jobs (check how).
> 6. File an ADR (next free number: check docs/adr; the main session is using 0078–0083, so use 0084 — and if more are needed 0085) "The browser backend: OPFS sync access handles in a worker, IndexedDB as the fallback", Status filed unaccepted per ADR 0066 (copy the status wording style of ADR 0070), with a decision set file in docs/decisions/ per docs/decisions/README.md WITHOUT accepted-by (a session never writes accepted-by). Cite it from code with [Contract]/[DesignDecision] as other store types do. It should close ADR 0071's revisit condition for the browser (synchronous read exists in a worker) and state what happens on the main thread (fallback), and that maintenance stays Off by default in a browser (DatasetOptions already does that).
> 7. If OPFS sync handles genuinely cannot be reached from .NET WASM without breaking AOT/trimming, stop and report that with concrete evidence (build output, runtime error) and build the alternative instead.
>
> Verify: `dotnet build Varve.slnx -c Release` zero warnings (add the new projects to Varve.slnx), the Store tests still pass, the browser tests pass in headless Chromium here, the determinism comparison passes. Commit your work in the worktree branch with conventional commits whose bodies contain `Refs #10` and end with the sign-off and co-author lines given (never put a model identifier in commit messages). Do not push. Final report: the branch name and commit list, the design decisions and evidence (what Chromium version, what worked/failed), measured numbers (e.g. sync read latency per 4 KiB block, flush latency), files changed, anything left undone.

## The report

**OPFS synchronous access handles are reachable from .NET WebAssembly**, AOT-
and trim-safe, through source-generated `[JSImport]`: a `Span<byte>` crosses as
a memory view and `FileSystemSyncAccessHandle.read` fills it before the call
returns. .NET 10's `dotnet.js` runs unchanged in a module worker. Evidence and
decisions are in [ADR 0084](../adr/0084-the-browser-backend.md); in short:

- The `[JSImport]` generator requires `AllowUnsafeBlocks` (`SYSLIB1074`), so the
  new package turns it on for generated code only — recorded as a decision.
- A synchronous access handle locks its file against a second handle, a move
  onto it, a move of it and its removal (measured in Chromium 141), so
  FileStorage's rename-over publish cannot work while a reader holds a blob.
  Blobs are published as generations, `name~G`, the highest winning.
- Both browser backends declare `Committed`, not `Synchronised`: the standard
  says only that `flush()` persists "to disk", and an origin's storage is
  best-effort and evictable. The maintainer's "TransactionCommitted" is
  `Durability.Committed`; no member was renamed.
- The IndexedDB fallback serves the synchronous read from a copy read whole at
  blob open: an open blob costs its length in memory.
- The leases: an exclusive access handle on `derived/LOCK`, and a Web Lock.
- `log/` written in the browser is byte-identical to `FileStorage`'s for the
  same deterministic script: twelve files.

What was left undone, and the build's state (red only on `CS0618`, by ADR 0066,
until 0084 is accepted), is in the session's hand-back report to the launching
session.
