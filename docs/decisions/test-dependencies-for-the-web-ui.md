---
set: test-dependencies-for-the-web-ui
namespace: varve
adr: 0204
decisions:
  - key: BunitForComponentTests
    statement: "bUnit renders the Web UI's components in its tests, scoped to tests/Varve.WebUi.Tests/, at the newest version whose Blazor floor the repository's pinned runtime version satisfies"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: PlaywrightOnTheRunnersChrome
    statement: "The Web UI's browser tests use Microsoft.Playwright, launching the runner's preinstalled Chrome by path, with no browser download, no CI image row and no Docker Hub pull"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: PlaywrightDriverIsATestOnlyNative
    statement: "Microsoft.Playwright's Node.js driver is native binaries in a test closure; the native-asset gate walks shipped members only, and the register's scope keeps the package out of every src/ closure"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: AxeCoreAuditsEveryScreen
    statement: "Deque.AxeCore.Playwright audits every screen and dialog in both themes against WCAG 2.2 AA tags with zero violations allowed, and never replaces the keyboard walk-through"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: BrowserTestsHaveTheirOwnJob
    statement: "The Web UI's browser tests run in their own named CI job outside eng/ci.cs, and their durations are reported per OS"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
---

The rulings of [ADR 0204](../adr/0204-test-dependencies-for-the-web-ui.md), one line each.
Filed unaccepted by Web UI part 1 (#167); every citation is `CS0618` until the maintainer accepts
them (ADR 0066).
