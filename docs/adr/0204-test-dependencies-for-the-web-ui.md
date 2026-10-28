# 0204 — Test dependencies for the Web UI: bUnit, Playwright on the runner's Chrome, axe-core

## Status

**Accepted — filed unaccepted by Web UI part 1 (#167, milestone #96),
2026-10-27** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 1 plan (#96, 2026-10-27):
- **D5:** Playwright, on the runner's Chrome. This ADR states that
  `Microsoft.Playwright` ships a Node.js driver as native binaries in the test
  closure, and that the native-asset gate scopes to shipped artefacts only and
  no `src/` closure contains the driver.
- **Accessibility (A7):** the automated audit is axe-core, run through the
  browser tests. It never replaces a keyboard walk-through.

Records decision set [`test-dependencies-for-the-web-ui`](../decisions/test-dependencies-for-the-web-ui.md).

## Context

The Web UI (ADR 0200) is tested at two levels:
- **Components,** against fixtures: ADR 0206's markup contract, and the
  screens over fixtures captured from a running server.
- **The application,** in a real browser against a real `Varve.Server`: every
  screen, every act, sign-in, the CSP, and an accessibility audit on every
  screen.

The BCL has no Razor component renderer and no browser driver. The
repository's own browser runner, `eng/browser-tests.cs` (ADR 0084), drives one
page over the DevTools protocol and reads a global. That fits a storage
contract, but not an application that is typed into, clicked, walked by
keyboard, waited on while it renders, and audited.

Constraint 1 forbids a native asset in a **shipped** artefact (ADR 0009's
amendment of 2026-09-22). `eng/native-assets.cs` walks the restore graphs of
the packable members of `Varve.slnx`, and of a member that declares
`VarveShipped` (ADR 0200). No test project is either.

## Decision

1. **bUnit for component tests.** `bunit` at **2.11.3**, licence MIT, scoped
   to `tests/Varve.WebUi.Tests/`.
   - **What it does:** it renders a Razor component outside a browser, with
     parameters, a fake `IJSRuntime` (loose mode, so a component's interop
     calls are recorded and answered with defaults), and a semantic markup
     comparison through `AngleSharp.Diffing`. The comparison ignores attribute
     order and Blazor's own `blazor:*` attributes. Whitespace is preserved
     where the contract asks for it (ADR 0206 point 6).
   - **Why this version:** 2.11.3 is not the latest. 2.12.x and 2.13.x require
     the Blazor packages at 10.0.13 or later. The repository pins the
     runtime's 10.0.12 (ADR 0200), and transitive pinning refuses a lower pin
     as a downgrade. 2.11.3 is the newest release whose floor (10.0.10) the
     pin satisfies. Its `AngleSharp` floor, 1.8.1, is met by the pinned 1.8.2
     (ADR 0063). The version moves when the repository's 10.0.x pins move.
   - **The closure:** `AngleSharp.Css`, `AngleSharp.Diffing`, and
     `Microsoft.AspNetCore.Components.WebAssembly.Authentication` with its
     `Authorization` and logging and caching abstractions, all managed. The
     authentication package is inert in the tests, and the Web UI never
     references it (ADR 0201).
2. **Playwright for the browser tests.** `Microsoft.Playwright`, licence
   MIT, scoped to the Web UI's browser test project.
   - **Native binaries in the test closure.** `Microsoft.Playwright` ships a
     **Node.js driver as native binaries**, one per platform, copied into the
     test project's output and started by the library. That is a native asset
     in a test closure. Constraint 1, as scoped, does not reach it:
     - the native-asset gate walks only shipped members, and no test project
       is one;
     - the register's scope refuses the package in any project outside its
       test project, so no `src/` closure can contain the driver.
   - **No browser download.** Playwright launches the runner's preinstalled
     Chrome by its path (`VARVE_CHROME`), as the `browser storage in Chromium`
     job does. So there is no Playwright browser download, no row in
     `.github/ci-images.yaml`, and no Docker Hub pull.
   - **Not ADR 0084's runner.** That runner stays for the storage contract.
     Rewriting Playwright's input, waiting and isolation over raw DevTools
     would be a large driver of our own, tested by nothing else.
   - **Its own job.** The browser tests run in their own named CI job, outside
     `eng/ci.cs`, because the devcontainer has no browser (ADR 0036).
3. **axe-core for the accessibility audit.** `Deque.AxeCore.Playwright`,
   licence MIT, scoped as Playwright is. It depends on
   `Deque.AxeCore.Commons`, licence MPL-2.0, which embeds axe-core (MPL-2.0)
   and depends on `Newtonsoft.Json` (MIT). All three are managed, and all
   stay in the test closure.
   - It runs on every screen and every dialog, in both themes, with the tags
     `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa` and `wcag22aa`, and zero
     violations allowed.
   - It never replaces the keyboard walk-through, which the traceability
     record holds.
4. **Registered when used.** bUnit enters the register with this ADR.
   Playwright and the axe package enter with the browser tests, citing this
   ADR, each at the newest stable version that resolves against the
   repository's pins on that day.

## Alternatives considered

- **No component library: test components only through the browser.** Every
  case would boot the runtime, and the markup contract (ADR 0206 point 6)
  would need a server or a static host. bUnit renders in-process, in
  milliseconds, in the devcontainer.
- **bUnit at its latest, with the repository's 10.0.x pins moved to 10.0.13.**
  That is a change to every Microsoft package the repository pins, made for a
  test library. It belongs to the next runtime bump, not to this milestone.
- **Extend `eng/browser-tests.cs` (ADR 0084) and admit no package.** Refused
  on the plan (D5) for the reason in point 2.
- **Playwright's own browser download, or a Playwright image.** That needs a
  download from Playwright's CDN or a new mirrored image. The runner's Chrome
  is already there and already trusted by an existing job.
- **pa11y or Lighthouse for the audit.** Both are Node tools outside the .NET
  test run. Deque's package runs axe-core inside the Playwright tests that
  already visit every screen.

## Consequences

- **The register:** `bunit` 2.11.3, `Adr="0204"`,
  `Scope="tests/Varve.WebUi.Tests/"`. `Microsoft.Playwright` and
  `Deque.AxeCore.Playwright` join with the browser tests.
- **`Varve.WebUi.Tests`** renders every component against the cases under
  `docs/ui/components/*/cases/` (ADR 0206 point 6). It runs in `eng/ci.cs`
  (`test-webui`), in the devcontainer, with no browser.
- **The browser tests** run in their own job, on the runner's Chrome, and
  report their durations per OS (#126).
- **Checks:**
  - Against the accepted ADRs: 0009 and its amendment (the register,
    constraint 1 scoped to shipped artefacts), 0063 (`AngleSharp`), 0084 (the
    storage runner, unchanged), 0036 (the devcontainer), 0099's amendment
    of 2026-10-19 (CI's images from the mirror, never Docker Hub), 0133 (scopes), 0200, 0201 and 0206.
    No conflict.
  - Layer ownership: tests only.
  - Analyzer rule: none new.
