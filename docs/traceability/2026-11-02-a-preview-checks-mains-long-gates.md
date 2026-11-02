# A preview checks main's long gates instead of running them inline

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#129](https://github.com/Hafeok/Varve/issues/129); [#219](https://github.com/Hafeok/Varve/issues/219) and [#218](https://github.com/Hafeok/Varve/pull/218) referenced |
| **Date** | 2026-11-02 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRzUotGnNtdpHy2K62` |
| **Branch** | `claude/adoring-keller-42dw5z`, from `main` at 6ad1c0e |
| **ADRs** | [0215](../adr/0215-the-soak-gate-run-and-judged.md) (amended), [0214](../adr/0214-file-backend-failure-injection.md) (amended) |

## The prompt

> 2. Then the release gate's freshness change, as one small PR from `main`:
>    * The Linux 4-hour soak and the full-depth fault sweeps run on `main`: the faults nightly, the Linux soak nightly or at least weekly. Say which fits the 300-minute job limit, given #219.
>    * Give them a `workflow_dispatch` trigger, in the nightly or a workflow of their own, not on `release-gate.yml`. I can then start one on `main` when evidence is needed.
>    * The release gate, for a `0.x.y-preview.N` descriptor, refuses unless the latest run of each on `main` is green and within its freshness window: 8 days for the soaks, and propose one for the faults. That's `eng/soak-freshness.cs` generalised.
>    * A non-preview descriptor (`1.0.0`, or any later minor or major) still runs both inline on the release commit.
>    * A dated amendment to ADR 0215, with the reason: inline 4-hour gates serialise every release behind the previous one. Add fixtures for the preview and non-preview paths.

## The report

- **The faults on main.** The nightly's `store, full depth` job already ran
  every fault at every point on both runners; it is the faults' run. On the
  nightly of 2026-11-01 it took 25.5 minutes on Linux and 36 on Windows.
- **The Linux soak on main, nightly.** The Windows soak of 2026-10-31 took
  242 minutes for build, soak and judge; #218's load adds under a minute. At
  about 245 minutes the soak is 82% of a 300-minute job, over #219's 80%
  warning, and 72% of the 340 minutes its job has in `release-gate.yml`.
  The job's limit is the same for either schedule, so it runs nightly.
- **Dispatch.** The nightly's `workflow_dispatch` takes `run`: the night,
  the store at full depth, the Linux soak or the Windows soak.
- **The gate.** `eng/release-freshness.cs` decides the mode from the
  version and judges each check on main's latest completed, non-skipped
  job: three days proposed for the faults. Sixteen fixtures; with the
  preview rule broken to "always inline", six are judged wrong.
- **The rulings.** `FourHoursOnLinuxAtARelease` and ADR 0214's
  `RunInTheReleaseGateAndNightly` restated and `PreviewsCheckMainsLatestRuns`
  filed, all unaccepted; none is cited in code.
- **Gates run locally:** licence headers, decision sets, banned symbols,
  issue references, DCO, the freshness fixtures, required checks, CI images,
  changelog, status and release pass.
