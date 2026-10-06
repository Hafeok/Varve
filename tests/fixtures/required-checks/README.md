# Required-checks fixture

The failure path of `eng/required-checks.cs`: a gate that has never failed is a
gate nobody has tested (`docs/testing.md` §5).

```
dotnet run eng/required-checks.cs -- --declaration tests/fixtures/required-checks/repo-standard.yaml --workflows tests/fixtures/required-checks/workflows
```

exits 1 and names two required checks:

- `build (macos-latest)`, which the build job's matrix does not produce;
- `nightly`, which only a workflow that does not run on pull requests reports.

`build (ubuntu-latest)` (a matrix expansion) and `lint` (a job with no `name:`,
reported by its id) pass. The `required-checks-fixture` job in `eng/ci.cs`
requires exactly that exit code.
