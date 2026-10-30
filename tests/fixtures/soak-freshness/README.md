# The Windows soak's freshness fixtures

Cases for `eng/soak-freshness.cs --fixtures` (ADR 0215 point 6). Each holds
`now` and the jobs the Actions API would return for main's nightly runs,
newest first. The pipeline's `soak-freshness-fixtures` job runs them on every
build: a `pass-` case must be judged fresh, and a `fail-` case refused.

| Case | Why |
|---|---|
| `pass-green-three-days-ago` | the latest Windows soak passed three days ago |
| `fail-red` | the latest one failed, though an older one passed |
| `fail-nine-days-old` | green, but older than eight days |
| `fail-none` | no Windows soak on main at all |
| `fail-still-running` | the only one has not finished |
