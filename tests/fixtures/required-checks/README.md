# Required-checks fixture

The failure path of `eng/required-checks.cs`: a gate that has never failed is a
gate nobody has tested (`docs/testing.md` §5).

```
dotnet run eng/required-checks.cs -- --declaration tests/fixtures/required-checks/repo-standard.yaml --workflows tests/fixtures/required-checks/workflows
```

exits 1 and names four required checks:

- `build (macos-latest)`, which the build job's matrix does not produce;
- `nightly`, which only a workflow that does not run on pull requests reports;
- `signed`, pinned to an App, which no workflow posts as the gates App;
- `reviewed`, which the gates App posts but which is required without
  `integration_id`, so any job named `reviewed` would satisfy it.

`build (ubuntu-latest)` (a matrix expansion), `lint` (a job with no `name:`,
reported by its id) and `approved` (pinned, posted by `gates.yml` on
`pull_request_target`) pass. The `required-checks-fixture` job in `eng/ci.cs`
requires exactly that exit code.
