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

**2. The configuration is the shipped one,** as ADR 0110 states:
`DOTNET_gcServer=0`, `DOTNET_gcConcurrent=1`, `DOTNET_GCConserveMemory=5`.

**3. The gate, for a run of any length** (restating `FlatWithinABand`):
- every 30-second sample of the working set, less the dataset's own, from
  minute 30 to the end lies within ±25% of those samples' median;
- the median of the last ten minutes is within 10% of minutes 30–40;
- open handles and `derived/` files stay within fixed bounds.

**4. The soak reports its working set by cause,** one CSV column each:
- the collector's view (`GC.GetGCMemoryInfo`: committed, heap,
  fragmentation, promoted);
- the live heap after a full collection;
- native memory: the process's private bytes less the collector's committed
  memory;
- the dataset's own (ADR 0082), and beside it the term dictionary's
  in-memory index, which #61 named as the candidate addition.

**5. A judge.** `Varve.Benchmarks --soak-judge <csv>` applies point 3 and
exits non-zero on a miss, naming the sample, the window and the bound it
broke. Its failure path has fixtures: a CSV that drifts, one outside the
band, one that leaks handles, and one whose dataset never settles.

**6. Where and how long.**
- **The release gate** (ADR 0214's `release-gate.yml`) runs the soak for
  **four hours on Linux** and judges it. Four hours is enough because:
  - ADR 0110 point 5 shows the collector settles by minute 30, so everything
    after that is steady-state evidence;
  - it puts three and a half hours of steady state behind the drift window,
    seven times the 30-minute reference;
  - at the measured rate it is about 500,000 commits, 500 checkpoints and
    their merges, so each maintenance cycle repeats hundreds of times. A
    leak of 0.1 MB a cycle would show as more than the 10% band;
  - it fits GitHub's six-hour job limit with room for the build.
- **The nightly** runs the same soak on **Windows once a week.**
- **The release gate refuses** when the latest Windows soak on `main`'s
  nightly is red, or older than eight days. A step reads the nightly's runs
  through the Actions API with the job's own token and names the run it
  judged. Eight days is a week's schedule and a day of slack.
- **One eight-hour run** on Linux is reported in the pull request, to bound
  the slope over a day's operation.

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
  it does. The pull request reports the eight-hour run and what #61's
  question found.
- `docs/testing.md` gains the soak's place in the release gate, and the
  operator guide states the gate and the configuration it is judged under.

## Checks

- **Checked against the accepted ADRs.** Touches **0082** (amended), **0110**
  (superseded in part), **0124** and **0158** (the workload's commits and
  merges).
- **Layer ownership.** `tests/Varve.Benchmarks` and the workflows; no `src/`
  change unless the soak finds a leak, which then gets its own fix and test.
- **Analyzer rule.** None.
- **Open questions owned.** None.
