# 0215 — The soak gate, run and judged: a bounded vocabulary, four hours, a judge, and Windows weekly

## Status

**Filed unaccepted by 1.0 readiness A (#61, under #129), 2026-10-28** (ADR
0066). Acceptance is the maintainer's act on the pull request. The plan it
follows was approved on #129 on 2026-10-27, with this answer: Linux in the
release gate; Windows weekly in the nightly; and the release gate refuses
when the latest Windows soak is red or older than eight days.

**Supersedes in part** [ADR 0110](0110-the-shipped-runtime-configuration.md):
`FlatWithinABand` moves to this ADR's set under its key, restated for a run
of any length with a bounded vocabulary (ADR 0068 point 4).

**Amends** [ADR 0082](0082-the-soak-gate.md) by dated amendment: the soak is
run by CI and judged by a script, not by hand.

**Amended 2026-11-02:** a preview checks main's latest runs of the long gates
instead of running them inline, and the nightly runs the Linux soak (see the
end, "the long gates on main"). Amends ADR 0214 point 6 to match.

Records decision set [`the-soak-gate-judged`](../decisions/the-soak-gate-judged.md).

## Context

ADR 0082 states the soak gate, ADR 0110 judges it under the shipped runtime
configuration, and #61 blocks 1.0 on it. Where #86 left it (#61, 2026-10-10):
a two-hour soak under the shipped configuration drifted +20.3% from minutes
30–40 to 110–120 (+17.1% net of the dataset's own), against 10% allowed.
The dataset more than doubled in that window: the default vocabulary, about
ten million distinct quads, is never reached, so the run cannot say whether
the growth is the dataset's or something held that should not be. The soak
has always been run and read by hand, and never in CI.

## Decision

**1. The workload reaches a steady size.** The soak runs `--soak <minutes>
--policy --subjects 2000`: about a million distinct quads, which the
writer's rate fills well before minute 30. From then on, the dataset's own
should be flat. A run is valid only if the quad count is flat from minute
30, within 5%; the judge refuses to judge one that is not.

**2. The configuration is the shipped one,** as ADR 0110 states: the
workstation collector, concurrent collections, `ConserveMemory` 5. The three
settings live in one file, `src/Varve.Server/ShippedRuntime.props`, which
`Varve.Server` imports, so the tool, the AOT binary and the container image
carry them, and which `Varve.Benchmarks` imports, so the soak runs under
exactly what the image ships. The soak prints the collector the runtime
reports; the judge refuses to judge a run under any other.

**3. The gate, for a run of any length** (restating `FlatWithinABand`):
- every 30-second sample of the working set, less the dataset's own and less
  the collector's committed but unused memory, from minute 30 to the end lies
  within ±25% of those samples' median;
- the median of the last ten minutes is no more than 10% over minutes 30–40:
  growth is what the gate is for, and a working set that shrinks holds;
- the collector's committed memory, which it returns on its own schedule and
  so is not judged in the band, is reported beside it and stays at most twice
  the live heap after a full collection plus 128 MB;
- open handles and `derived/` files stay within the bounds the configuration
  gives (the amendment of 2026-10-30 states them).

**4. The soak reports its working set by cause,** one CSV column each:
- the collector's view (`GC.GetGCMemoryInfo`: committed, heap,
  fragmentation, promoted);
- the live heap after a full collection;
- native memory: the process's private bytes less the collector's committed
  memory;
- the dataset's own (ADR 0082), and beside it the term dictionary's
  in-memory index, which #61 named as the candidate addition;
- the log's segments, and before the table the process's handles before the
  dataset opened, which the handles bound starts from.

**5. A judge.** `Varve.Benchmarks --soak-judge <csv>` applies point 3 and
exits non-zero on a miss, naming the sample, the window and the bound it
broke. Its failure path has fixtures: a CSV that drifts, one outside the
band, one that leaks handles, and one whose dataset never settles.

**6. Where and how long.**
- **The release gate** (ADR 0214's `release-gate.yml`) runs the soak for
  **four hours on Linux** and judges it, for a release that is not a
  preview; a preview checks the nightly's latest Linux soak instead (the
  amendment of 2026-11-02). Four hours is enough because:
  - ADR 0110 point 5 shows the collector settles by minute 30, so everything
    after that is steady-state evidence;
  - it puts three and a half hours of steady state behind the drift window,
    seven times the 30-minute reference;
  - at the measured rate it is about 500,000 commits, 500 checkpoints and
    their merges, so each maintenance cycle repeats hundreds of times. A
    leak of 0.1 MB a cycle would show as more than the 10% band;
  - it fits GitHub's six-hour job limit with room for the build.
- **The nightly** runs the same soak on **Windows once a week,** and on
  Linux every night (the amendment of 2026-11-02).
- **The release gate refuses** when the latest Windows soak on `main`'s
  nightly is red, or older than eight days. A step reads the nightly's runs
  through the Actions API with the job's own token and names the run it
  judged. Eight days is a week's schedule and a day of slack.
- **An eight-hour soak runs before 1.0 in #164's Azure environment,** as a
  Container Apps job under the shipped configuration, judged by the same
  script, to bound the slope over a day's operation. Hosted runners stop a
  job at six hours, and this pull request's container did not hold a run
  past two (the amendment of 2026-10-30).

## Alternatives considered

- **Windows in the release gate too.** The maintainer chose weekly in the
  nightly, with the freshness check: a release does not wait four more hours
  on a second runner, and a red or stale Windows soak still stops it.
- **A shorter soak in the land gate.** A soak shorter than the collector's
  settling time measures the collector.
- **Bound the working set absolutely.** Rejected in ADR 0082 already: a band
  around the run's own median is machine-independent.

## Consequences

- #61 closes when a release gate's four-hour soak passes under this ADR, if
  it does. The pull request reports what #61's question found; the
  eight-hour run follows in #164's environment before 1.0.
- `docs/testing.md` gains the soak's place in the release gate, and the
  operator guide states the gate and the configuration it is judged under.

## Checks

- **Checked against the accepted ADRs.** Touches **0082** (amended), **0110**
  (superseded in part), **0124** and **0158** (the workload's commits and
  merges).
- **Layer ownership.** `tests/Varve.Benchmarks` and the workflows. The soak
  found one leak, retired extent sets' empty directories, fixed in the file
  backend with its own test; and the shipped runtime configuration moved
  into a props file the server and the soak share.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-30 — the soak measured, and its bounds

**#61's question is answered: the working set does not grow.** A soak under
the shipped configuration, `--soak 480 --policy --subjects 2000`, ran 111
minutes in this pull request's container before the container was
restarted. From minute 30 to 111:
- the working set less the dataset's own had a median of 122 MB and drifted
  −6.0% from minutes 30–40 to the last ten;
- the quads were steady at 666,329;
- the live heap after a full collection stayed between 9 and 18 MB, and
  native memory at about 193 MB;
- the dataset's own grew from 12 to 34 MB, as ADR 0082 expects.

Twelve samples were 26–48% over the median. They were the collector's
committed memory, 94 MB in those samples against 60 elsewhere, while the
live heap stayed at 13: memory the collector had committed and not used.
Point 3 therefore judges the band without it and puts it under a ceiling of
its own. Committed less twice the live heap peaked at 97 MB; the margin of
128 MB is that with a third to spare. Without it, the band holds with no
sample outside, and the drift is −17.9%, which a growth-only check passes.

**One thing grew that is not the dataset's own,** and is fixed. A retired
extent set's files were deleted and its directory was not: in a second run,
132 of the 138 directories under `derived/extents` were empty after 49.5
minutes, about 2.7 a minute, some 1,300 over eight hours. The file backend
now removes a set's directory with its last file
(`FileStorageTests.a_retired_extent_set_leaves_no_directory_behind`). The
judge counted files, so it did not see them.

**The bounds** (`tests/Varve.Benchmarks/SoakBounds.cs`), from the soak's
configuration: twelve checkpoints kept, the default merge policy's fan-out
of 4, a memtable of 20,000 quads, a commit cache of 4,096.

| Term | Bound | Measured peak |
|---|---|---:|
| Fixed files and their temporaries | 6 + 2 | 7 |
| Extent sets, 9 files each | 12 kept + 1 before the drop + 1 merge output + 4 retired held open = 18 sets, 162 files | 9 sets |
| Disk runs | ⌈log₄(live quads ÷ 20,000)⌉ + 2 = 5 at 666,000 quads | 5 |
| Commit-index blobs | ⌊log₂(commits ÷ 4,096)⌋ + 2: 7 at 236,000 commits, 9 at a million | 4 |
| **Derived files** | **182 at the 111-minute run's end** | **97** |
| Handles | before the dataset + derived files + 1 + segments + 64 for the runtime's own | 141 |

The fourth set term is a platform's: where a file a reader has open cannot
be deleted, the inputs of the latest change stay until the views that hold
them close, a fan-out of sets at most. Linux deletes them at once. The
runtime's allowance of 64 handles covers the thread pool's threads and
events; Linux showed 7, and Windows counts a handle per thread and per
event, so the weekly Windows soak is the measure of it. The 111-minute run
did not record its handles before the dataset; a second run had 60 of them
then and 67 at most besides the store's.

Judged by the amended script, with the collector line and those 60 handles
added to its output and two log segments, the 111-minute run holds: 163
samples from minute 30, a median of 78 MB less the collector's unused, drift
−17.9%, the collector's unused at most 109 MB, handles at most 141 of 309,
derived files at most 97 of 182.

**The eight-hour soak** cannot run on hosted runners, which stop a job at six
hours, and this container did not hold a run past two hours: it was
restarted three times during the step. Point 6 now runs it in #164's Azure
environment before 1.0.

## Amendment, 2026-11-02 — the long gates on main, and a preview checks them

**Inline four-hour gates serialise every release behind the previous one.**
The release gate ran the faults at full depth and the four-hour Linux soak
on the commit under release. `release.yml` admits one release run at a time
on a ref, so each release waited four hours and more for its own gate, and
the next waited behind it, however small the change between them: a preview
with a one-line fix took as long to cut as 1.0. A preview is cut often, and
its purpose is to put main's state in front of users; the evidence that
main's state holds is the same evidence whether it is gathered on the
release commit or on main the night before. So:

- **The long gates run on main.** The nightly's store job at full depth
  already ran every fault at every point on Linux and on Windows (ADR 0214
  point 6), and it stays the faults' run. The nightly now also runs the
  four-hour soak on Linux, through `release-gate.yml` as the Windows soak
  does. A dispatch of the nightly on main runs either alone (`run`: `store
  at full depth`, `linux soak`, `windows soak`), so evidence can be gathered
  before a release without waiting for the night. `release-gate.yml` has no
  trigger of its own.
- **A preview checks them.** For a `0.x.y-preview.N` descriptor, the release
  gate runs neither inline. It refuses unless the latest completed run on
  main's nightly of each is green and within its window: the Linux soak,
  eight days, as the Windows soak; the store at full depth on each runner,
  three days. A skipped job is not a run. `eng/release-freshness.cs`, which
  generalises the Windows soak's check of point 6, judges it and names each
  run it judged.
- **Every other release runs both inline,** on the release commit, as
  before: 1.0.0, and every later minor and major release, and a preview of
  any of them. The Windows soak is checked for freshness for every release,
  as point 6 says.
- **Fixtures for both paths.** `tests/fixtures/release-freshness/` holds a
  preview whose evidence is all fresh, and previews refused for a red Linux
  soak, a missing one, faults four days old and a Windows soak nine days
  old; a preview whose newest store jobs were skipped and whose earlier ones
  are fresh; and releases that run inline (1.0.0, 1.2.0, 1.1.0-preview.1, no
  release) and need only the Windows soak, one refused for it. The
  pipeline's `release-freshness-fixtures` job runs them on every build.

**Three days for the faults.** They run every night, so a fresh run is at
most a day old on a normal morning. Three days lets two nights fail to run,
a runner outage or a nightly cancelled for an unrelated leg, without
blocking a preview, and refuses at the third, when main has moved for half a
week without the faults. A red run refuses at once, whatever its age.

**What fits the jobs' limits.** Measured on the nightly of 2026-11-01 (run
41294147201) and the Windows soak of 2026-10-31 (run 41192568259):

| Run | Job | Took | Its timeout |
|---|---|---:|---:|
| The faults, in the store at full depth | Linux | 25.5 min | the step's 150 |
| | Windows | 36 min | the step's 150 |
| The four-hour soak, build and judge | Windows | 242 min | 340 |

The load before the clock of the amendment above adds well under a minute
(41 s in its fixture). The faults fit every night many times over. The soak
is about 245 minutes on either runner, so it does not fit a 300-minute job
with #219's warning at 80% of a job's timeout (82%); the soak job keeps its
340 minutes in `release-gate.yml`, under GitHub's six-hour limit, where it
is 72%. Within that job it fits a nightly schedule as well as a weekly one,
so the Linux soak runs nightly: a red or cancelled night is replaced by the
next one instead of blocking previews for a week, and a preview cut on any
morning has a soak of main's previous day behind it. #219's five-hour leg is
a job of its own and does not share the soak's limit.
