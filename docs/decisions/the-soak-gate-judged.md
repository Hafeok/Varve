---
set: the-soak-gate-judged
namespace: varve
adr: 0215
decisions:
  - key: BoundedVocabulary
    statement: "The soak runs with a vocabulary its writer fills before minute 30, --subjects 2000, and a run whose quad count is not within 5% of its median from minute 30 is not judged"
  - key: FlatWithinABand
    statement: "The soak gate holds when every 30-second sample from minute 30 to the end of the working set, less the dataset's own and less the collector's committed but unused memory, lies within plus or minus a quarter of those samples' median, the median of the last ten minutes is no more than a tenth over minutes thirty to forty, and handles and derived files stay within the bounds the configuration gives; moved from ADR 0110 by ADR 0215's supersession in part, restated for a run of any length"
  - key: CommittedUnderItsOwnCeiling
    statement: "The collector's committed memory, which it returns on its own schedule and so is not judged in the band, is reported beside it and stays at most twice the live heap after a full collection plus 128 MB"
  - key: BoundsFromTheConfiguration
    statement: "The bound on derived files is the fixed files and their temporaries, nine files for each extent set the kept checkpoints, a checkpoint before the drop, a merge's output and a fan-out of retired sets can hold, the runs the merge rule allows over the live quads and the commit index's blobs over the commits; the bound on handles is the process's own before the dataset opened, one per derived file, the log's append handle and a reader per segment, and the runtime's own"
  - key: TheShippedCollectorOrNotJudged
    statement: "The soak imports the server's runtime configuration, prints the collector the runtime reports, and a run under any other collector is not judged"
  - key: WorkingSetByCause
    statement: "The soak reports per sample the collector's committed, heap, fragmented and promoted memory, the live heap after a full collection, native memory as private bytes less committed, and the dataset's own"
  - key: JudgedByAScript
    statement: "Varve.Benchmarks --soak-judge applies the gate to a soak's output and exits 0 when it holds, 1 when it misses and 2 when it cannot judge, and its fixtures prove each exit on every pipeline run"
  - key: FourHoursOnLinuxAtARelease
    statement: "A release runs the soak for four hours on Linux under the shipped runtime configuration in release-gate.yml and is not cut unless the judge holds"
  - key: WindowsWeeklyAndFresh
    statement: "The nightly runs the soak on Windows once a week, and a release refuses when the latest Windows soak on main's nightly is red or older than eight days"
  - key: EightHoursInAzureBeforeVersionOne
    statement: "An eight-hour soak under the shipped configuration runs before 1.0 in #164's Azure environment as a Container Apps job, since hosted runners stop a job at six hours, and is judged by the same script"
---

The rulings of [ADR 0215](../adr/0215-the-soak-gate-run-and-judged.md),
filed unaccepted by 1.0 readiness A for #61 (ADR 0066). `FlatWithinABand`
moves here from ADR 0110's set under its key, restated (ADR 0068 point 4).
