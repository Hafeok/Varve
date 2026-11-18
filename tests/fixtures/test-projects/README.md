# Test project fixtures

The two paths of `eng/test-projects.cs` (#298): every test project under
`tests/` is run by some job. A gate that has never failed is a gate nobody has
tested (`docs/testing.md` §5).

| Case | What it holds | Expected |
|---|---|---|
| `failing` | three test projects: one named by `Test("…")` in `eng/ci.cs`, one by its path in a workflow, and one named nowhere but as the prefix of another path (`tests/Fixture.Unrun.TestsExtra`); a project with no xunit reference | exit 1, one finding: `tests/Fixture.Unrun.Tests` |
| `passing` | the same, with the third project's path in a workflow at the end of a line | exit 0 |

```
dotnet run eng/test-projects.cs -- --root tests/fixtures/test-projects/failing
dotnet run eng/test-projects.cs -- --root tests/fixtures/test-projects/passing
```

The `test-projects-fixtures` job in `eng/ci.cs` requires exactly those exit
codes.
