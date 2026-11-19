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
  - key: TwoRowsFirstScreenAndLazySet
    statement: "The size gate measures two rows, the first screen without the lazy set at the unchanged budget, and the lazy set the boot manifest marks, at its own budget"
  - key: TheLazySetIsNamed
    statement: "eng/browser-size.json names the lazy assemblies, and the gate fails when the boot manifest's lazy set differs from them"
  - key: ExplorerBudgetMeasured
    statement: "The explorer's budget is its lazy set measured from a clean tree plus 5 percent, rounded up to 10 kB, 60,000 bytes for Varve.WebUi.Explorer and Varve.Sparql on 2026-11-19"
---

The rulings of [ADR 0205](../adr/0205-the-web-uis-size-budget-and-no-aot.md), one line each.
Filed unaccepted by Web UI part 1 (#167); every citation is `CS0618` until the maintainer accepts
them (ADR 0066).
