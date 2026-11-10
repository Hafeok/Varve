# `eng/land.cs`'s closing-trailer fixtures

Cases for `eng/land.cs --fixtures` (#209). Each holds a `step`, the pushed
`commits` (`sha`, `message`) and either the pull request's `body` (`push`, the
`Completes:` check before a push) or the issues' `states` (`landed`, the check
after `--main` has moved `main`; `null` when GitHub cannot be read). `says`
lists text the output must contain. The pipeline's `land-fixtures` job runs
them on every build: a `pass-` case must pass and a `fail-` case be refused.

| Case | Step | Why |
|---|---|---|
| `pass-completes-with-trailer` | push | `Completes: #1`, and a commit closes #1 |
| `fail-completes-without-trailer` | push | `Completes: #1`, and no commit closes it; refused naming #1 |
| `pass-completes-none` | push | `Completes: none`, no closing trailer |
| `fail-no-completes-line` | push | no `Completes:` line; one inside an HTML comment does not count |
| `fail-completes-malformed` | push | the template's placeholder left as it is |
| `pass-descriptor-trailer-counts` | push | a release descriptor's own `Closes #14` counts |
| `pass-landed-all-closed` | landed | every issue a trailer names is closed |
| `fail-landed-issue-still-open` | landed | #2 is still open; reported with its commit |
| `fail-landed-state-unreadable` | landed | GitHub cannot be read; non-zero, and it says so |
