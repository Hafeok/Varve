# Analyzer fixtures

Projects built by `Varve.Analyzers.Tests` through a real `dotnet build`. They
are **not** members of `Varve.slnx`: most of them exist to fail to build, and a
solution member that fails to build would fail CI.

| Fixture | Shape | Expected |
|---|---|---|
| `layer-rule/conforming/` | `Varve.Fixture.Consumer` (layer 1) → `Varve.Fixture.Base` (layer 0) | builds |
| `layer-rule/violating/` | `Varve.Fixture.Lower` (layer 1) → `Varve.Fixture.Upper` (layer 2) | fails with `DD0001` |
| `layer-rule/packable/` | `Varve.Fixture.Packaged` — packable, layer 0 | builds |
| `layer-rule/packable/` | `Varve.Fixture.PackableNone` — packable, no `ArchLayer` | fails with `VARVE0005` |
| `banned-api/` | `Varve.Fixture.BannedSymbol` — packable, uses `System.Uri` | fails with `RS0030` |
| `banned-api/` | `Varve.Fixture.AmbientClock` — packable, `VarveDeterministic`, reads the clock and `Random` | fails with `RS0030` |
| `public-api/` | `Varve.Fixture.UndeclaredApi` — packable, public type in neither baseline | fails with `RS0016` |

They inherit the repository's `Directory.Build.props` and
`Directory.Build.targets`, which is the entire point.

**For the layer rules** (`DD0001` from `DecisionDriven.Analyzers`, `VARVE0005`
from `Varve.Analyzers`), the unit tests construct compilations directly and so
prove the rule logic; they cannot see whether `ArchLayer` is actually surfaced
to the compiler, whether the package generates the `[ArchLayer]` a referencing
build reads back, or whether either analyzer is wired as an analyzer at all. Only a real build shows that, and
every one of those three is a way the rule could be perfectly correct and
completely inert.

**For the off-the-shelf analyzers** there is no unit test at all — the rule
logic is Microsoft's and is not ours to test. What is ours is the wiring:
`eng/BannedSymbols.txt` reaching the analyzer as an `AdditionalFiles` entry,
the per-project `PublicAPI.*.txt` pair reaching it as another, and the packages
being referenced with the right `PrivateAssets`. Each of those can be wrong in
a way whose only symptom is a green build, so the fixtures are the only
evidence that they are right. The diagnostic ids the tests assert were taken
from the output of a real build.
