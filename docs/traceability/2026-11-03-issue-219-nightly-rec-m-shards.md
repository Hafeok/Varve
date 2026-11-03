# The nightly's REC M run in shards, and a budget on every leg

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#219](https://github.com/Hafeok/Varve/issues/219); [#126](https://github.com/Hafeok/Varve/issues/126) and [#13](https://github.com/Hafeok/Varve/issues/13) referenced |
| **Date** | 2026-11-03 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRzUotGnNtdpHy2K62` |
| **Branch** | `claude/adoring-keller-42dw5z-nightly`, from `main` at 51d664f |
| **ADRs** | [0176](../adr/0176-the-rec-gate.md) (its nightly run, unchanged in what it compares) |

## The prompt

The maintainer, on 2026-11-03: "take it after the judge fix if no one else
has. Shard or speed up the long-properties leg so the nightly completes".
The work is the issue's Solution:

> 1. **Measure where the five hours go:** per test class and per property, at nightly depth. Name the top items in the PR.
> 2. **Fix by the measurement, in this order of preference:**
>    - make the slow properties cheaper per iteration (a fixture rebuilt per case, a full validation where an incremental one is the point);
>    - shard the leg across parallel jobs by test class, each well inside the limit;
>    - reduce an iteration count only with the reason stated in `docs/testing.md` §8, never to make the clock fit.
> 3. **A budget check:** each nightly leg logs its duration (#126's `eng/test-durations.cs`), and the nightly fails with a clear message when a leg passes 80% of its timeout. So the next overrun shows up a week early, not as a red nightly.

## The report

- **Where the time goes: one test.** The TRX of the last two green nights,
  read with `eng/test-durations.cs`: on 2026-10-29 (run 40845292534) the
  leg's tests ran 295 minutes, of which
  `RecGateTests.REC_M_standalone_commit_time_and_projection_agree_at_100_positions`
  took 251, `REC_S_standalone_commit_time_and_projection_agree` 44, and
  everything else under 4 (`RecStandaloneTests` 1.8, `ProjectionPropertyTests`
  1.1, `GateOracleTests` 0.4, `RuleTests` under a second). On 2026-10-28
  (run 40660469058) REC M took 143 and REC S 35. The commits between the
  two runs changed nothing under `src/Varve.Shacl*`, `src/Varve.Store`,
  the evaluator or these tests: the spread is the runner's. No property
  with an iteration count is among the slow items, so sharding by class
  could not help: one test was the leg.
- **Inside REC M**, sampled on a 4-core development machine with the test
  instrumented (not committed) and a probe that landed a prefix of the
  history without the gate:
  - a standalone validation of REC M, the oracle, takes about 3 s at
    position 250, 67 s at 2,500, 254 s at 6,001 and 234 s at 24,751 (the
    622 buildings are bootstrapped by about 5,600);
  - a bootstrap commit through the `reject` gate takes about 0.45 s, the
    projection's apply about 0.08 s; an event commit 5–6 ms and 2–3 ms;
  - a commit without the gate about 3–10 ms.
  So the 100 comparisons are about 360 of an estimated 415 minutes here,
  and the gate and the projection over the whole history about 55. Twelve
  stack samples during the bootstrap are all in the gate's incremental
  validation, most in the evaluator's optimiser estimating, per focus
  node, the SPARQL constraint pre-bound to it; it is not what the leg waits
  for, and is noted, not changed.
- **Speeding it up (preference 1)** has nothing to take: the full
  validation is the test's oracle, compared against the incremental
  projection and the gate, so it cannot become incremental.
- **Sharding (preference 2), by position rather than by class.** A new
  `rec-m` job runs REC M in four shards side by side. Each takes the whole
  history through the gate and the projection, as the single leg did, and
  compares the *i*th of the 100 positions where *i* mod 4 is the shard
  (`VARVE_REC_M_SHARD=k/n`, from `strategy.job-index` and `job-total`, so
  the list is the only thing to change; unset, one run compares all 100 as
  before). Nothing is searched less. `The_REC_M_shards_compare_each_of_the_100_positions_once`
  runs on every pull request and holds the partition for 1 to 12 shards; a
  shard whose value does not read fails; a shard asserts it compared every
  position it owns. The `long` leg for the project runs with
  `--filter-not-method` on REC M alone, so any test added later runs there,
  and a renamed REC M makes the shards run no test, which Microsoft.Testing.Platform
  fails (exit code 8, checked).
- **Expected durations** against the 240-minute budget (80% of 300): each
  shard about 53 + 362 / 4 ≈ 143 minutes on the development machine, which
  ran REC M about 1.65 times slower than the slow night; so about 85–90
  minutes on the slow runner and about 55 on the fast one, plus checkout
  and build. The `long` leg for the project without REC M: about 50
  minutes. Every other leg is as it was, the longest the Windows store at
  full depth at about 36 minutes against its 150-minute step.
- **The budget (part 3)** did not exist: neither `eng/` nor the workflows
  checked a duration against a limit. `eng/test-durations.cs` takes
  `--started <unix-seconds>` and `--timeout-minutes <n>`, writes the leg's
  elapsed time against its limit to the log and the job summary, and fails
  with an `::error` naming the leg, its time and its share when it passes
  80% (exit 1; usage errors 2), whether or not a TRX was written. Each leg
  records its start as its first step (the store job, before its test step,
  whose 150 minutes are its limit), and each limit is a matrix value used
  both as `timeout-minutes` and by the budget, so the two cannot drift. The
  soaks are not budgeted: their length is their parameter, and the Linux
  soak's 245 minutes are 72% of its own 340.
- **Gates run locally:** licence headers, decision sets, banned symbols,
  issue references, DCO, required checks, CI images, release freshness
  fixtures and status pass; `actionlint` 1.7.7 is clean on `nightly.yml`;
  the Shacl.Store tests pass at a pull request's depth. One shard
  (`1/100`, the whole history and one comparison, at position 250) was run
  end to end at nightly depth and passed in 55 minutes, the replay's 53 as
  estimated.
- **Not verified:** the shards' time on GitHub's runners, until the next
  night; whether the budget step runs after a job is cancelled at its
  limit (it runs on `always()`, and is meant to fire before the limit).
