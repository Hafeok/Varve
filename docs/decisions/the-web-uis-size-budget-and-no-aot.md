---
set: the-web-uis-size-budget-and-no-aot
namespace: varve
adr: 0205
decisions:
  - key: NotAotCompiled
    statement: "The Web UI is published interpreted, trimmed and relinked, not AOT-compiled: measured, AOT downloaded 2.29 MB more and was no faster to the first screen"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: DownloadBudgetGate
    statement: "eng/browser-size.cs fails when brotli-11 of the Web UI's _framework/ exceeds eng/browser-size.json, the measured size plus 5% rounded up to 10 kB; raising the budget is an amendment to ADR 0205"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: MeasuredFromACleanTree
    statement: "The Web UI's size is measured on a publish from a clean tree into an empty folder"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
---

The rulings of [ADR 0205](../adr/0205-the-web-uis-size-budget-and-no-aot.md), one line each.
Filed unaccepted by Web UI part 1 (#167); every citation is `CS0618` until the maintainer accepts
them (ADR 0066).
