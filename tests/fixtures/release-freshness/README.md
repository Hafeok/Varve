# The release gate's freshness fixtures

Cases for `eng/release-freshness.cs --fixtures` (ADR 0215 point 6, amended
2026-11-02). Each holds the release's `version`, what `--mode` must say of it
(`inline`), `now`, and the jobs the Actions API would return for main's
nightly runs, newest first. The pipeline's `release-freshness-fixtures` job
runs them on every build: a `pass-` case must be judged fresh, a `fail-` case
refused, and every case's mode must match.

A preview (`0.x.y-preview.N`) runs no long gate inline and needs the Windows
soak and the Linux soak green within eight days, and the store at full depth
on each runner green within three. Any other release runs the faults and the
Linux soak inline and needs only the Windows soak.

| Case | Version | Why |
|---|---|---|
| `pass-preview-all-fresh` | preview | every check's latest run green and in its window |
| `pass-preview-skipped-passed-over` | preview | the newest store jobs were skipped; the earlier ones are fresh |
| `pass-preview-faults-in-a-cancelled-run` | preview | the store jobs at full depth are green in a nightly cancelled by a leg of its own (#219), run 41424545800: a job is judged by its own conclusion, not its run's |
| `fail-preview-linux-soak-red` | preview | the latest Linux soak failed, though an older one passed |
| `fail-preview-no-linux-soak` | preview | no Linux soak on main at all |
| `fail-preview-faults-four-days-old` | preview | the store at full depth on Windows is four days old |
| `fail-preview-windows-soak-nine-days-old` | preview | the Windows soak is older than eight days |
| `pass-release-ignores-the-linux-soak` | 1.0.0 | inline: a red Linux soak on main does not matter |
| `pass-release-minor-runs-inline` | 1.2.0 | a later minor runs inline |
| `pass-release-later-preview-runs-inline` | 1.1.0-preview.1 | a preview after 1.0 runs inline |
| `pass-not-a-release` | none | the nightly's own call runs what its inputs say |
| `pass-green-three-days-ago` | 1.0.0 | the latest Windows soak passed three days ago |
| `fail-release-windows-soak-red` | 1.0.0 | the latest Windows soak failed |
| `fail-red` | 1.0.0 | the latest Windows soak failed, though an older one passed |
| `fail-nine-days-old` | 1.0.0 | the Windows soak is green, but older than eight days |
| `fail-none` | 1.0.0 | no Windows soak on main at all |
| `fail-still-running` | 1.0.0 | the only Windows soak has not finished |
