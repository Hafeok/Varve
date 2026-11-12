# 0200 — `Varve.WebUi`: a Blazor WebAssembly host at layer 6, and what Blazor costs

## Status

**Accepted — filed unaccepted by Web UI part 1 (#167, milestone #96),
2026-10-27** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer:
- in #96: the Web UI is a Blazor WebAssembly client of the public API with no
  endpoints of its own, in 1.0 (#15);
- on the part 1 plan (#96, 2026-10-27):
  - its name is `Varve.WebUi`, served under `/ui/`, because part 2's data
    explorer is the same application, and because a namespace
    `Varve.Console` would bind before `System.Console` inside `Varve.*`;
  - the ADR admitting Blazor answers WasmSmoke's comment with the measured
    size of both routes for the first screen.

**Amends** by dated amendment [ADR 0060](0060-hosts-at-layer-6-the-composition-root.md):
a host may be a browser application published as static files.

Records decision set [`varve-webui-a-blazor-webassembly-host`](../decisions/varve-webui-a-blazor-webassembly-host.md).

## Context

Varve can be operated only through the API and the CLI. #96 adds a Web UI.
Part 1 is the operator console (#167); part 2, the data explorer, comes after
Inference (#95).

Constraint 4 says every package needs an ADR. `tests/Varve.WasmSmoke` states
why it does not use Blazor: constraint 3's claim is that Varve runs in a
browser, not that it runs in a web framework, and Blazor would bring in an
ASP.NET Core package tail with nothing to do with that claim. The Web UI makes
a different claim. It is an application, with routing, forms, focus management
and an accessibility tree on every screen. Whether that application should
carry Blazor's tail is a question of what the tail costs and what it buys.

The browser size measurements (2026-10-15, `varve-browser-size.md`) put the
.NET runtime's floor at about 0.85 MB brotli, and Varve's store variant at
1.21 MB.

## Decision

1. **`Varve.WebUi` is a host at layer 6** (ADR 0060). It is a composition
   root and never a library:
   - `src/Varve.WebUi/`, with `ArchLayer` 6 and `ArchCompositionRoot`;
   - the SDK `Microsoft.NET.Sdk.BlazorWebAssembly`;
   - published as static files and never packed.

   It references `Varve.Protocol.Client` and nothing else of Varve. It reaches
   the server through that client only. Every screen reads a resource the API
   already serves, and a screen that would need a new route is out of scope
   and goes to an issue.

2. **Blazor WebAssembly** renders it: `Microsoft.AspNetCore.Components.WebAssembly`
   and its closure (the components, the WebAssembly host, JSInterop, and
   their `Microsoft.Extensions` DI, logging, options and configuration).
   All are MIT, managed and trimming-annotated, with no native asset.
   - **The version** is 10.0.12, the runtime's version as the repository pins
     it for JwtBearer and the `Microsoft.Extensions` abstractions. 10.0.13 and
     10.0.14 exist, but under transitive pinning the pinned 10.0.12
     abstractions would refuse their closure as a downgrade.
   - **No `…DevServer`.** The server or a static host serves the bundle,
     tests included.
   - **No `…Authentication`.** Sign-in is the Web UI's own (ADR 0201).

3. **Blazor is scoped to the Web UI.** The register entry carries
   `Scope="src/Varve.WebUi/, tests/Varve.WebUi.Tests/"`, so the register gate
   refuses a direct reference from anywhere else. `Varve.WebUi.Tests`'s
   closure test refuses one that arrives by any path: no project under `src/`
   but the Web UI has a `Microsoft.AspNetCore.Components` package anywhere in
   its restore graph.

   The layer rule already refuses the only other path. A layer-6 Web UI
   cannot be referenced by a library, and `Varve.Server` referencing it
   would be a same-layer reference.

4. **Published trimmed, in Blazor's own trim mode.** That is `partial`: every
   assembly marked trimmable is trimmed, Varve's libraries included, and the
   app's assembly is kept. Measured on the first screen, `TrimMode=full`
   bought nothing:
   - **Without rooting the app**, it removed the components the router makes
     from their `Type`. The layout named only by `typeof()` lost its
     constructor, and the first browser run failed with `CtorNotLocated`.
   - **With the app rooted**, it measured the same size as `partial`.

   The size measurements' feature switches stay on:
   - `InvariantGlobalization` and `InvariantTimezone`;
   - `EventSourceSupport`, `DebuggerSupport`, `MetricsSupport` and
     `StackTraceSupport` off;
   - `UseSystemResourceKeys`.

   A trim or AOT warning is an error.

5. **The runtime is relinked at publish only.** `WasmBuildNative` needs the
   `wasm-tools` workload. The jobs that publish install it, and the build jobs
   that build `Varve.slnx` do not. Relinking takes `dotnet.native.wasm` from
   976 KB to 417 KB brotli.

6. **Sizes are measured on a clean publish.** An incremental publish kept a
   stale trim twice in the measurements below:
   - once the bundle without the app's components, after the root was added;
   - once 2.006 MB where the clean publish is 1.674 MB.

   The size gate (ADR 0205) publishes from a clean tree.

7. **WasmSmoke stays on `wasm-experimental`.** Its comment stays true for its
   claim. It gains a pointer to this ADR, which answers it for the Web UI's.

### What Blazor costs, measured

The first screen, Health, was built both ways: as the Web UI's Blazor
component, and on WasmSmoke's route (`Microsoft.NET.Sdk.WebAssembly`,
`[JSImport]` DOM interop written by hand). Both builds have:
- the same markup: heading, button, read-at line, management list, datasets
  table and problem view;
- the same switches, trimming and native relink;
- the same `Varve.Protocol.Client` read of `/health/ready`.

Measurement conditions:
- **Size:** the whole published `wwwroot`, maps excluded, each file compressed
  with brotli quality 11 and window 24, as `brotli -q 11`.
- **Time:** navigation to the readiness rendered, in headless Chromium, ten
  cold loads each in a fresh browser context, from a static host on loopback
  to a server on loopback. It is boot cost, with no network.
- **Tooling:** SDK 10.0.403, runtime 10.0.14, with `wasm-tools`.

| Build | raw | brotli | first screen, median of 10 |
|---|---:|---:|---:|
| WasmSmoke's route: `wasm-experimental`, DOM by hand | 3.30 MB | **1.048 MB** | **428 ms** (396–461) |
| `Varve.WebUi`: Blazor WebAssembly | 5.27 MB | **1.675 MB** | **770 ms** (693–928) |
| *Difference* | +1.97 MB | **+0.627 MB** | **+342 ms** |

**Where the 0.627 MB goes** (brotli):

| Part | KB |
|---|---:|
| `Microsoft.AspNetCore.Components`, `.Web`, `.WebAssembly`, JSInterop and `blazor.webassembly.js` | 195 |
| the Web UI's own assembly, kept whole (partial trim) | 66 |
| `System.Private.CoreLib` kept beyond the raw route's | 105 |
| `System.Text.Json` kept beyond the raw route's (configuration and JSInterop) | 101 |
| `Microsoft.Extensions` DI, logging, options, configuration; `Immutable`, `Concurrent`, LINQ, `DiagnosticSource` and the rest, less the raw route's `main.js` | 160 |

At 10 Mbit/s the difference is about 0.5 s of download on the first visit.
The `_framework` files are fingerprinted and immutable, so a returning visit
costs neither the download nor most of the boot.

**The plan's preliminary figure was wrong.** It gave 1.525 MB for an empty
Blazor app. That build was `TrimMode=full` without the app rooted (point 4):
it trimmed its own components away and never ran. The numbers above come from
builds that render the screen.

**Why Blazor anyway.** The raw route's 1.048 MB buys one screen that rebuilds
its whole tree on every read. It has no router, no diff (a refresh loses focus
and scroll), no forms, no dialogs, no component tests and no focus management
beyond one `focus()` call. Every one of those is DOM interop Varve would write
and test alone, for every screen of part 1 and part 2. Its cost grows with
each screen.

Blazor's tail is a fixed cost, paid once. It gives:
- **routing**, with `FocusOnNavigate` (WCAG 2.4.3);
- **a diffing renderer** that keeps focus across a refresh;
- **forms** and **dialogs**;
- **a component model** that bUnit tests against fixtures (ADR 0204).

The application claim is the one this cost buys, and the size gate (ADR 0205)
holds it from growing unseen.

## Alternatives considered

- **WasmSmoke's route, `wasm-experimental` with hand-written DOM interop.**
  Measured above: 0.63 MB and 340 ms cheaper on the first screen. Refused for
  what it does not include. A router, a renderer that diffs, focus handling,
  forms and a testable component model would all be Varve's own code, with
  every accessibility property of part 1's screens and part 2's built on DOM
  calls that nothing else tests.
- **A front end outside .NET** (TypeScript with a framework, or Web
  Components). It is smaller, but the one UI Varve ships would then not use
  `Varve.Protocol.Client`, the typed client every .NET caller uses. The
  client's model would be written again, in another language, and drift. It
  breaks constraint 1's spirit for the application that most shows what the
  API serves.
- **`TrimMode=full` with the app rooted.** The same size as `partial`, and one
  more setting to keep right.
- **Blazor's `…Authentication` package for sign-in.** ADR 0201's: it holds one
  access token, and the Web UI needs one per plane.
- **Server-side rendering (Blazor Server or a Web App).** That would give the
  Web UI endpoints of its own on the server, and a connection per user.
  Refused by #96: the Web UI is a client of the public API with no endpoints.

## Consequences

- **The register:** `Microsoft.AspNetCore.Components.WebAssembly` at 10.0.12,
  `Adr="0200"`, scoped to the Web UI and its tests.
- **The solution:** it gains `src/Varve.WebUi` and `tests/Varve.WebUi.Tests`.
  The build jobs build it without the wasm workloads, and the jobs that
  publish it install them.
- **The bundle is 1.674 MB brotli** with the first screen. The budget and the
  AOT choice are ADR 0205's.
- **WasmSmoke's csproj comment** points here.
- **Checks:**
  - Against the accepted ADRs: 0003, 0060 (amended), 0064 (the layer
    declaration), 0084 (the browser host: storage only, unchanged), 0009
    (the register) and 0133 (scopes). No conflict.
  - Layer ownership: `Varve.WebUi` (6).
  - Analyzer rule: none new.
