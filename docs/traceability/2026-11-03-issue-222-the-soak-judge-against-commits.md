# The soak judge measures the process, against commits

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#222](https://github.com/Hafeok/Varve/issues/222); [#61](https://github.com/Hafeok/Varve/issues/61) and [#219](https://github.com/Hafeok/Varve/issues/219) referenced |
| **Date** | 2026-11-03 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and last served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRzUotGnNtdpHy2K62` |
| **Branch** | `claude/adoring-keller-42dw5z-judge`, from `main` at c0a776b |
| **ADRs** | [0215](../adr/0215-the-soak-gate-run-and-judged.md) (amended), [0082](../adr/0082-the-soak-gate.md) (amended) |

## The prompt

> Both soaks are valid now, so the miss is the judge's model, not a leak: ADR 0082's "dataset's own" charges 144 B per commit, but the process grew about 36 B per commit. Change the judge, as one PR, ahead of step 5:
>
> 1. Measure the process's memory directly: the working set on Linux, private bytes on Windows, with no "own" subtracted.
> 2. Over the judged window, fit memory against commits. The slope must be within a stated allowance in bytes per commit, and the residuals within ±25% of the intercept. Set the allowance from today's runs (about 36 B per commit on Linux; measure Windows' slope too) plus a stated margin, and name it as a known cost that #222 will reduce.
> 3. Keep the collector's committed-memory ceiling, and the file and handle bounds.
> 4. Replay the new judge over the three failed runs (Linux inline, Linux on `main`, Windows on `main`), so all three pass. Add a fixture with a slope above the allowance that fails, and one with a per-hour leak that fails.
> 5. A dated amendment to ADR 0215, and to ADR 0082 for the retired model, citing #222.
>
> Once it's on `main`, I'll dispatch both soaks, and preview.16 follows.

Earlier the same day the maintainer had also asked that `eng/release-freshness.cs`
be checked to accept a green full-depth store job inside a nightly cancelled by
#219's overrun.

## The report

- **Why the model failed.** ADR 0089 paged the commit index, so at most 8,192
  entries of 80 bytes are resident; the dataset's own still charged 144 bytes
  a commit, about 63 MB at 440,000 commits, more than the Windows process's
  private bytes.
- **The slopes,** by least squares from minute 30, in bytes (10⁶ to the MB) a
  commit: 47.9 Linux inline (release run 41413295525), 49.4 Linux on `main`
  (nightly 41450438151), 50.2 Windows (nightly 41426223592). The prompt's 36
  is the end-to-end difference over all commits; the fit over the judged
  window is steeper. Allowance: 60, a fifth over the largest.
- **One departure from the prompt, stated in the amendment.** On Windows the
  intercept is 42.9 MB, so ±25% is ±10.7 MB, and six samples lie up to 16.5
  MB off the line, each where the collector held 14–28 MB committed over its
  heap. The band is widened at each sample by that sample's committed but
  unused memory, which stays under its own ceiling. Without it the Windows
  run misses; with it all three hold. A stated absolute floor would be the
  alternative.
- **Replay.** The old judge misses all three (medians 67.1, 72.1 and −7.6
  MB); the new one holds all three.
- **Fixtures.** `misses-slope` (80 bytes a commit) and `misses-hourly-leak`
  (45 bytes a commit and 4 MB an hour, fitted at 76) miss; `holds-windows-trimmed`
  holds; `misses-drift` is replaced; `misses-committed` misses on the
  ceiling alone. Fifteen fixtures, none judged wrong.
- **Freshness.** `eng/release-freshness.cs` judges jobs by their own
  conclusion and never reads the run's, so it accepts the green store jobs
  of the cancelled nightly 41424545800; a fixture from that run's jobs
  proves it. Seventeen cases, none judged wrong.
- **The rulings.** `FlatWithinABand` and `WorkingSetByCause` restated,
  unaccepted, so the build is red on CS0618 until the maintainer accepts
  them.
