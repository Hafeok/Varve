# Dependency register fixtures

The failure path of `eng/dependency-register.cs`'s scope check (ADR 0133,
ADR 0009 amended 2026-10-13). Each directory is a register and the projects
under it.

| Case | What it holds | Expected |
|---|---|---|
| `failing` | `src/Library` references `Microsoft.Identity.Web`, which the register scopes to `samples/` | exit 1, naming the project and the scope |
| `passing` | only `samples/Sample` references it | exit 0 |

```
dotnet run eng/dependency-register.cs -- tests/fixtures/dependency-register/failing/Directory.Packages.props
```

The `register-fixture` job in `eng/ci.cs` runs both. These projects are never
built: they are outside `Varve.slnx`, and the repository's own register check
skips `tests/fixtures/`.
