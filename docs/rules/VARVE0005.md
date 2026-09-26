# VARVE0005 — Layer declaration is missing, malformed, or disagrees with the assembly

| | |
|---|---|
| **Category** | `Varve.Layering` |
| **Tier** | 1 (error) |
| **Motivated by** | [ADR 0064](../adr/0064-varve-configuration-and-hot-path-rules.md), `VarveConfigurationAndHotPathRules.PackableAssemblyDeclaresLayer` and `.ExecutableLayerAndRootAgree`; [ADR 0060](../adr/0060-hosts-at-layer-6-the-composition-root.md) |
| **Replaces** | the half of the retired [`VARVE0002`](VARVE0002.md) that `DD0001` does not cover |

## Principle

`DD0001` checks that every reference within the family points strictly down.
It reads the referenced assembly's layer, and it is silent on a project that
declares none itself: unset means "not placed yet", not layer 0. So a host,
which nothing references, and a package that forgot the property would be
checked by nothing. This rule is what places them.

## What it reports

For a compilation whose assembly name starts `Varve.`:

1. **It declares `ArchLayer`**, an integer from 0 to 6, unless it is a test
   assembly (`*.Tests`) or `Varve.Analyzers`, which declare none. A packable
   assembly always declares one, whatever it is called. A test assembly that
   declares a layer is reported too.
2. **An executable declares 6, and only an executable declares 6.** An
   executable is an `OutputType` of `Exe` or `WinExe`. A test assembly is an
   executable under Microsoft.Testing.Platform and declares none, as before.
3. **`ArchCompositionRoot` is `true` exactly when `ArchLayer` is 6.** ADR 0060
   rejected a composition-root flag separate from the layer, because a second
   declaration can disagree with the first. The package needs the flag, so this
   makes disagreement an error instead.

It reads `ArchLayer` and `ArchCompositionRoot` (made visible by the package),
and `IsPackable` and `OutputType` (made visible by `Directory.Build.props`). It
reports against the compilation, so it appears as `CSC : error VARVE0005`.

## Configuration

None beyond the properties it reads. `tools/repo-standard/` is not a `Varve.*`
assembly and is not checked.

## False-positive story

Tier 1 and decidable: four build properties and a name. The one known limit is
the name: an assembly called `*.Tests` may declare no layer, and no analyzer can
tell a test from something named like one. The packable check is what stops a
package escaping by its name (ADR 0004 records the limit, ADR 0064 restates it).
The generic half of rule 1 is proposed upstream; if the package ships it, this
rule drops that half.

There is no `[DesignDecision]` path: the declaration is configuration, and the
answer to "not placed" is to place it.

## Violating example

```xml
<PropertyGroup>
  <IsPackable>true</IsPackable>
</PropertyGroup>
```

```text
CSC : error VARVE0005: Assembly 'Varve.Fixture.PackableNone' is packable and declares no layer.
Decide: set ArchLayer to the layer ADR 0060's table gives it, since a packed assembly is in the package graph whatever it is called | set IsPackable to false.
The declaration is configuration and has no exception path: a Varve assembly that is not placed is checked by nothing, and the answer is to place it.
```

## Conforming example

```xml
<!-- a package -->
<PropertyGroup>
  <ArchLayer>1</ArchLayer>
  <IsPackable>true</IsPackable>
</PropertyGroup>

<!-- a host -->
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <ArchLayer>6</ArchLayer>
  <ArchCompositionRoot>true</ArchCompositionRoot>
</PropertyGroup>
```

## See also

- `tests/Varve.Analyzers.Tests/LayerDeclarationAnalyzerTests.cs`, and
  `LayerRuleFixtureTests.cs` for the wiring through a real build.
