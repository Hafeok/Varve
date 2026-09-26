---
set: build-time-analyzer-packages
namespace: varve
adr: 0063
decisions:
  - key: DecisionDrivenPackagesAdmitted
    statement: "DecisionDriven.Analyzers and DecisionDriven.Report are admitted as build-time packages with PrivateAssets all, their register entries citing ADR 0063"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: LatestPreviewUntilOnePointZero
    statement: "The two prerelease packages track the latest preview until the analyzer repository ships 1.0, each bump's commit carrying that preview's breaking changes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: FloorRaiseWaitsForAmendment
    statement: "A preview that raises the Roslyn floor it declares is not taken until a dated amendment to ADR 0009 raises Varve's"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: BannedSymbolEntriesCiteAnAdr
    statement: "Every banned-symbols entry cites an ADR in its comment and ends its message with the ADR number, gated in eng/ once session 2 of issue 43 touches the file"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: OffTheShelfThenDdThenVarve
    statement: "An off-the-shelf analyzer is used where it expresses a rule exactly, a DD rule where it does, and a VARVE rule only for what neither can express"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0063](../adr/0063-build-time-analyzer-packages.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
