# The release gate's freshness fixtures

Cases for `eng/release-freshness.cs --fixtures` (ADR 0215 point 6, amended
2026-11-02 and 2026-11-03). Each holds the release's `version`, what `--mode`
must say of it (`inline`), `now`, and the jobs the Actions API would return
for main's nightly runs, newest first, each optionally with its `steps`
(`name`, `conclusion`). Optionally `soak_issues`, the open issues labelled
`soak` (`number`, `html_url`), and `evidence`, lines the Markdown that
`--evidence` prints for the GitHub Release's notes must contain. The
pipeline's `release-freshness-fixtures` job runs them on every build: a
`pass-` case must pass and a `fail-` case be refused, every case's mode must
match, and its evidence must say each line.

A preview (`0.x.y-preview.N`) runs no long gate inline. It is refused unless
the store at full depth on each runner, which holds the faults at every
point, is green within three days, judged by the job's own conclusion. Main's
soaks and soak issues inform it and never refuse it. Any other release runs
the faults and the Linux soak inline, and is refused when the Windows soak is
red or older than eight days, or while an issue labelled `soak` is open.

A soak's verdict is read from its job: `green` when it succeeded, `red` when
its step "The soak misses the gate" failed (or, in a job from before that
step, "Judge the soak" failed), `invalid` for any other completed job that
did not succeed, `none` when there is no completed soak. A store job's is
`green`, `red` for a failure, `cancelled` for any other end, or `none`.

| Case | Version | Why |
|---|---|---|
| `pass-preview-all-fresh` | preview | every check's latest run green and in its window |
| `pass-preview-skipped-passed-over` | preview | the newest store jobs were skipped; the earlier ones are fresh |
| `pass-preview-faults-in-a-cancelled-run` | preview | the store jobs at full depth are green in a nightly cancelled by a leg of its own (#219), run 41424545800: a job is judged by its own conclusion, not its run's |
| `pass-preview-linux-soak-red-informs` | preview | the latest Linux soak missed the gate: red, informs |
| `pass-preview-linux-soak-invalid-informs` | preview | the latest Linux soak was not judged: invalid, informs |
| `pass-preview-no-linux-soak-informs` | preview | no Linux soak on main at all: none, informs |
| `pass-preview-windows-soak-nine-days-old-informs` | preview | the Windows soak is older than eight days, informs |
| `pass-preview-windows-soak-cancelled-informs` | preview | the Windows soak was cancelled: invalid, informs |
| `pass-preview-verdicts-read-from-steps` | preview | red and invalid told apart by steps, not by conclusion: a soak that crashed before its judge is invalid, one whose judge missed is red |
| `pass-preview-soaks-before-the-verdict-steps-inform` | preview | the soaks of 2026-11-02, from before the verdict steps: a failed "Judge the soak" is red, a soak that failed before it invalid |
| `pass-preview-soak-issue-open-informs` | preview | an open soak issue informs a preview and is listed in its evidence |
| `fail-preview-faults-four-days-old` | preview | the store at full depth on Windows is four days old |
| `fail-preview-faults-red` | preview | the store at full depth on Linux failed: red |
| `fail-preview-no-faults` | preview | no store at full depth on main at all: none |
| `pass-release-ignores-the-linux-soak` | 1.0.0 | inline: a red Linux soak on main does not matter |
| `pass-release-minor-runs-inline` | 1.2.0 | a later minor runs inline |
| `pass-release-later-preview-runs-inline` | 1.1.0-preview.1 | a preview after 1.0 runs inline |
| `pass-release-no-soak-issue` | 1.0.0 | no issue labelled soak is open |
| `pass-not-a-release` | none | the nightly's own call runs what its inputs say |
| `pass-green-three-days-ago` | 1.0.0 | the latest Windows soak passed three days ago |
| `fail-release-soak-issue-open` | 1.0.0 | an issue labelled soak is open |
| `fail-release-windows-soak-red` | 1.0.0 | the latest Windows soak failed |
| `fail-red` | 1.0.0 | the latest Windows soak failed, though an older one passed |
| `fail-nine-days-old` | 1.0.0 | the Windows soak is green, but older than eight days |
| `fail-none` | 1.0.0 | no Windows soak on main at all |
| `fail-still-running` | 1.0.0 | the only Windows soak has not finished |
