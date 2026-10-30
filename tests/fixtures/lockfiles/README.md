# Lockfile fixtures

The failure path of `eng/lockfiles.cs --check` (ADR 0224 point 5, amended
2026-10-28).

| Case | The one flaw | Expected |
|---|---|---|
| `newer-patch` | a lockfile records `Microsoft.NET.ILLink.Tasks` 99.0.1, which no SDK of global.json's band bundles: a lockfile written with a newer SDK patch than global.json pins | exit 1, naming the package, "a newer patch", and `dotnet run eng/lockfiles.cs` |

```
dotnet run eng/lockfiles.cs -- --check --lockfiles tests/fixtures/lockfiles/newer-patch
```

The `lockfiles-fixture` job in `eng/ci.cs` runs it. The lockfile is written by
hand: no real SDK writes 99.0.1, and that is the point. The check reads only the
implicit packages' resolved versions, so the rest of the file is not exercised.
