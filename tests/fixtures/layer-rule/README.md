# Layer-rule fixtures

Two project pairs, built by `LayerRuleFixtureTests` through a real `dotnet
build`. They are **not** members of `Varve.slnx`: one of them exists to fail to
build, and a solution member that fails to build would fail CI.

| Pair | Shape | Expected |
|---|---|---|
| `conforming/` | `Varve.Fixture.Consumer` (layer 1) → `Varve.Fixture.Base` (layer 0) | builds |
| `violating/` | `Varve.Fixture.Lower` (layer 1) → `Varve.Fixture.Upper` (layer 2) | fails with `VARVE0001` |

They inherit the repository's `Directory.Build.props` and
`Directory.Build.targets`, which is the entire point. The unit tests in
`Varve.Analyzers.Tests` construct compilations directly and so prove the rule
logic; they cannot see whether `VarveLayer` is actually surfaced to the
compiler, whether the layer reaches the assembly as metadata, or whether the
analyzer is wired as an analyzer at all. Only a real build shows that, and
every one of those three is a way the rule could be perfectly correct and
completely inert.
