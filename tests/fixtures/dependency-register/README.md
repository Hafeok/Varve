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

## What was resolved (ADR 0224)

Each directory with an `expect.txt` is one failure case of the checks against
what the restore resolved. The case is copied to a directory of its own outside
the repository, so that nothing of Varve's build reaches it. If it has a
`restore.txt`, each project listed there is restored for real from nuget.org,
with its lockfile written. Then the gate runs on the copy's register, with the
arguments in `args.txt`, and **must exit 1 and print every line of
`expect.txt`**. That is what makes each case fail for its own reason.

```
dotnet run eng/dependency-register.cs -- --fixtures tests/fixtures/dependency-register
```

The `register-fixtures` job in `eng/ci.cs` runs them.

| Case | The one flaw | Restored |
|---|---|---|
| `unadmitted-implicit-package` | `IsAotCompatible` makes the SDK reference `Microsoft.NET.ILLink.Tasks` itself, and the register has no `ImplicitPackage` for it | yes |
| `stale-entry` | `Microsoft.Extensions.Options` is registered, and nothing references it | yes |
| `dangling-adr` | the entry cites ADR 9999, which does not exist | no |
| `action-by-tag` | `actions/checkout@v4`, by tag; the other reference is pinned and is not reported | no |
| `suppression-without-statement` | an audit suppression, and no VEX document saying why it is not exploitable | no |
| `statement-without-suppression` | a VEX statement for a vulnerability nothing suppresses: a statement that outlived its reason | no |
| `runtime-licence` | a shipped project depends on Npgsql, whose `PostgreSQL` licence is OSI-approved but not on the runtime allow-list; in a test project it would pass | yes |
| `undeclared-licence` | JsonSchema.Net 9.0.0 declares its licence by a file, the end-user agreement ADR 0146 pins 8.0.5 to avoid, and the register states none | yes |
| `native-asset` | a shipped project depends on a package with native assets, and no `NativeAssetException` admits it (constraint 1) | yes |
| `build-only-exception-shipped` | the build-only licence exception, depended on by a shipped project | yes |

The packages are real ones, chosen because each is the case:
- JsonSchema.Net 9.0.0 is the end-user agreement this repository already
  refused (ADR 0146);
- Npgsql's `PostgreSQL` licence is OSI-approved and not a runtime one;
- `Gee.External.Capstone` is the native library BenchmarkDotNet brings.

A restore case needs the network, as CI's restore does.
