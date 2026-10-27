---
set: varve-webui-a-blazor-webassembly-host
namespace: varve
adr: 0200
decisions:
  - key: WebUiIsALayer6Host
    statement: "Varve.WebUi is a layer 6 host and composition root, published as static files and never packed, referencing Varve.Protocol.Client and nothing else of Varve"
  - key: WebUiReadsOnlyServedResources
    statement: "Every Web UI screen reads a resource the API already serves, through Varve.Protocol.Client; a screen that needs a new route is out of scope and goes to an issue"
  - key: BlazorWebAssemblyForTheWebUi
    statement: "The Web UI renders with Blazor WebAssembly, Microsoft.AspNetCore.Components.WebAssembly and its closure, at the runtime version the repository pins, with no DevServer and no authentication package"
  - key: BlazorScopedToTheWebUi
    statement: "Blazor is scoped to the Web UI and its tests in the register, and no project under src but the Web UI has a Microsoft.AspNetCore.Components package in its restore graph"
  - key: TrimmedInBlazorsMode
    statement: "The Web UI is published trimmed in Blazor's partial mode with the size measurements' feature switches, and a trim or AOT warning is an error"
  - key: RelinkedAtPublishOnly
    statement: "The runtime is relinked at publish only, by the jobs that install wasm-tools; the build jobs build the Web UI without the wasm workloads"
  - key: SizeOnACleanPublish
    statement: "The Web UI's size is measured on a clean publish, since an incremental one has kept a stale trim"
  - key: WasmSmokeStaysRaw
    statement: "WasmSmoke stays on wasm-experimental for its own claim, that Varve runs in a browser, and its comment points to ADR 0200 for the Web UI's"
---

The rulings of [ADR 0200](../adr/0200-varve-webui-a-blazor-webassembly-host.md), one line each.
Filed unaccepted by Web UI part 1 (#167); every citation is `CS0618` until the maintainer accepts
them (ADR 0066).
