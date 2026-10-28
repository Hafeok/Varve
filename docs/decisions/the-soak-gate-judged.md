---
set: the-soak-gate-judged
namespace: varve
adr: 0215
decisions:
  - key: BoundedVocabulary
    statement: "The soak runs with a vocabulary its writer fills before minute 30, --subjects 2000, and a run whose quad count is not within 5% of its median from minute 30 is not judged"
  - key: FlatWithinABand
    statement: "The soak gate holds when every 30-second sample from minute 30 to the end of the working set less the dataset's own lies within plus or minus a quarter of those samples' median, the median of the last ten minutes is within a tenth of minutes thirty to forty, and handles and derived files stay within fixed bounds; moved from ADR 0110 by ADR 0215's supersession in part, restated for a run of any length"
  - key: WorkingSetByCause
    statement: "The soak reports per sample the collector's committed, heap, fragmented and promoted memory, the live heap after a full collection, native memory as private bytes less committed, and the dataset's own"
  - key: JudgedByAScript
    statement: "Varve.Benchmarks --soak-judge applies the gate to a soak's output and exits 0 when it holds, 1 when it misses and 2 when it cannot judge, and its fixtures prove each exit on every pipeline run"
  - key: FourHoursOnLinuxAtARelease
    statement: "A release runs the soak for four hours on Linux under the shipped runtime configuration in release-gate.yml and is not cut unless the judge holds"
  - key: WindowsWeeklyAndFresh
    statement: "The nightly runs the soak on Windows once a week, and a release refuses when the latest Windows soak on main's nightly is red or older than eight days"
---

The rulings of [ADR 0215](../adr/0215-the-soak-gate-run-and-judged.md),
filed unaccepted by 1.0 readiness A for #61 (ADR 0066). `FlatWithinABand`
moves here from ADR 0110's set under its key, restated (ADR 0068 point 4).
