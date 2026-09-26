---
set: containerised-development
namespace: varve
adr: 0036
decisions:
  - key: DevcontainerMirrorsTheCore
    statement: "The devcontainer mirrors the stewardship core's template and overlays .NET with the SDK pinned exactly to global.json and the wasm workloads"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: CiCsIsThePipeline
    statement: "eng/ci.cs runs the jobs CI runs, with --list and --only, and CI runs it inside the devcontainer image"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: CiCsKeptInStepWithWorkflows
    statement: "A job added to ci.yml is added to eng/ci.cs too"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: AutocrlfOffEverywhere
    statement: "Every workflow checkout and the devcontainer set core.autocrlf=false, so every platform checks out the same bytes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: CrLfTestedOnOwnDocuments
    statement: "CR LF handling in parsers is tested on documents this repository owns, never on whatever a checkout produced"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0036](../adr/0036-containerised-development.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
