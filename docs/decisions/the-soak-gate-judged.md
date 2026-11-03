---
set: the-soak-gate-judged
namespace: varve
adr: 0215
decisions:
  - key: BoundedVocabulary
    statement: "The soak runs over a fixed vocabulary, --subjects 2000, that a load before the clock brings to the size the workload keeps it at, each quad asserted with the two thirds the workload holds it present, through commits with no readers; the timed run then starts with the unchanged workload on every OS, and a run whose quad count is not within 5% of its median from minute 30 is not judged"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: FlatWithinABand
    statement: "The soak gate holds when the process's memory as its OS holds it, the working set on Linux and private bytes on Windows, with nothing subtracted, fitted against commits by least squares over every 30-second sample from minute 30 to the end, has a slope of at most 60 bytes a commit, a known cost #222 reduces, and every sample lies within plus or minus a quarter of the fit's intercept, widened at each sample by the collector's committed but unused memory there; and handles and derived files stay within the bounds the configuration gives; moved from ADR 0110 by ADR 0215's supersession in part, restated by its amendment of 2026-11-03"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-03T00:00:00Z
  - key: CommittedUnderItsOwnCeiling
    statement: "The collector's committed memory, which it returns on its own schedule and so is not judged in the band, is reported beside it and stays at most twice the live heap after a full collection plus 128 MB"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: BoundsFromTheConfiguration
    statement: "The bound on derived files is the fixed files and their temporaries, nine files for each extent set the kept checkpoints, a checkpoint before the drop, a merge's output and a fan-out of retired sets can hold, the runs the merge rule allows over the live quads and the commit index's blobs over the commits; the bound on handles is the process's own before the dataset opened, one per derived file, the log's append handle and a reader per segment, and the runtime's own"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: TheShippedCollectorOrNotJudged
    statement: "The soak imports the server's runtime configuration, prints the collector the runtime reports, and a run under any other collector is not judged"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: WorkingSetByCause
    statement: "The soak reports per sample the working set and private bytes, the collector's committed, heap, fragmented and promoted memory, the live heap after a full collection, native memory as private bytes less committed, and the runs' and checkpoints' directories, and states the OS it ran on before its table; ADR 0082's dataset's own is retired by ADR 0215's amendment of 2026-11-03"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-03T00:00:00Z
  - key: JudgedByAScript
    statement: "Varve.Benchmarks --soak-judge applies the gate to a soak's output and exits 0 when it holds, 1 when it misses and 2 when it cannot judge, and its fixtures prove each exit on every pipeline run"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: FourHoursOnLinuxAtARelease
    statement: "A release that is not a preview, 1.0.0 and every later one, runs the soak for four hours on Linux under the shipped runtime configuration in release-gate.yml and is not cut unless the judge holds; the nightly runs the same soak on Linux every night, and a dispatch on main runs it alone"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: WindowsWeeklyAndFresh
    statement: "The nightly runs the soak on Windows once a week, and a release that is not a preview refuses when the latest Windows soak on main's nightly is red or older than eight days; restated by ADR 0215's amendment of 2026-11-03, under which the soaks inform a preview"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-03T00:00:00Z
  - key: PreviewsCheckMainsLatestRuns
    statement: "A preview, 0.x.y-preview.N, runs neither the faults nor the Linux soak inline: release-gate.yml refuses it unless the newest completed store job at full depth on Linux and on Windows on main's nightly, which holds the faults at every point, is green by the job's own conclusion and at most three days old, a job in a run cancelled by another job counting; main's soaks inform a preview and never refuse it; eng/release-freshness.cs judges it, and its fixtures prove the preview and the inline path on every pipeline run; restated by ADR 0215's amendment of 2026-11-03"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-03T00:00:00Z
  - key: EightHoursInAzureBeforeVersionOne
    statement: "An eight-hour soak under the shipped configuration runs before 1.0 in #164's Azure environment as a Container Apps job, since hosted runners stop a job at six hours, and is judged by the same script"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ARedOrInvalidSoakOpensAnIssue
    statement: "A soak in main's nightly that ran and did not succeed, a red one whose judge missed or an invalid one that was not judged, crashed, was cancelled or timed out, comments on the one open issue labelled soak or opens it, creating the label when it is missing, with the workflow's own token, the verdict, the OS, the run and the judge's output; the verdict is read from the soak job's steps, red when the step The soak misses the gate failed"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-03T00:00:00Z
  - key: NoReleaseWhileASoakIssueIsOpen
    statement: "A release that is not a preview, 1.0.0 and every later one, is refused by release-gate.yml while any open issue is labelled soak, pull requests excluded"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-03T00:00:00Z
  - key: TheReleaseNotesHoldMainsEvidence
    statement: "Every GitHub Release's notes carry main's latest Linux and Windows soak, with their verdicts green, red, invalid or none, and main's latest store job at full depth on each runner, green, red, cancelled or none, each with its job's link and date, and the open soak issues, as they are when publish.yml creates the release; the release descriptor does not carry them"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-03T00:00:00Z
---

The rulings of [ADR 0215](../adr/0215-the-soak-gate-run-and-judged.md),
filed unaccepted by 1.0 readiness A for #61 (ADR 0066). `FlatWithinABand`
moves here from ADR 0110's set under its key, restated (ADR 0068 point 4).

`BoundedVocabulary` is restated by the amendment of 2026-11-02: the writer's
rate filled the vocabulary before minute 30 on Linux and not on the first
weekly Windows runner, so the steady size is reached by a load before the
clock, and the rate is no longer part of the ruling. The 5% rule from minute
30 stays as it was. Accepted again by the maintainer on 2026-11-02 at their
direction.

`FourHoursOnLinuxAtARelease` is restated by the amendment of 2026-11-02, and
`PreviewsCheckMainsLatestRuns` is filed by it: a preview checks main's runs
of the long gates instead of running them, since four hours inline put every
release behind the one before it. Both await the maintainer's acceptance.

`FlatWithinABand` and `WorkingSetByCause` are restated by the amendment of
2026-11-03: ADR 0082's dataset's own charged 144 bytes a commit that the
process does not hold, so the gate now measures the process's memory directly
and bounds its slope against commits (#222). Both await the maintainer's
acceptance.

`PreviewsCheckMainsLatestRuns` and `WindowsWeeklyAndFresh` are restated by
the amendment of 2026-11-03, "the soaks inform previews", and
`ARedOrInvalidSoakOpensAnIssue`, `NoReleaseWhileASoakIssueIsOpen` and
`TheReleaseNotesHoldMainsEvidence` are added by it: by the maintainer's
decision, previews were blocked two days by the judge's out-of-date model of
the dataset's own and not by a leak, so the soaks inform a preview, the faults
gate it, and a red or invalid soak opens an issue that a later release waits
on. All five await the maintainer's acceptance.
