# VARVE0002 — Layer is not declared, or a referenced Varve assembly carries no layer metadata

| | |
|---|---|
| **Category** | `Varve.Layering` |
| **Severity** | Error |
| **Motivated by** | [ADR 0003 — Package layering](../adr/0003-package-layering.md) |
| **Enforcement policy** | [ADR 0004](../adr/0004-enforcement-by-analyzers.md) |

## What it reports

Four situations, all of which would leave [VARVE0001](VARVE0001.md) with
nothing to check.

**The compilation declares no layer.**

```
error VARVE0002: Assembly 'Varve.Rdf' declares no layer. A Varve.* assembly
must set the VarveLayer MSBuild property to an integer from 0 to 5, or to
'none' if it is a test, benchmark or analyzer assembly (ADR 0003).
```

**It declares `none` without being entitled to.**

```
error VARVE0002: Assembly 'Varve.Rdf' declares VarveLayer as 'none', which is
allowed only for a test assembly, a benchmark assembly, or Varve.Analyzers. A
published Varve package has a layer (ADR 0003).
```

**The declared value is not a layer.** An integer outside 0–5, or anything
non-numeric that is not the literal `none`.

**A referenced `Varve.*` assembly carries no usable `Varve.Layer` metadata** —
either absent, or present and malformed.

## Why

`VARVE0001` compares two numbers. Without this rule, either number could be
missing, and a missing number is not a failure — it is an assembly the
direction check silently skips.

That is the whole reason this rule exists: **an undeclared layer would turn the
layering rule off rather than fail it.** Turning enforcement off should be a
visible act, not something achieved by leaving a line out of a project file.

The value `none` is a declaration, not an absence. Reading it as "I have no
layer, and here is why that is allowed" is what makes it safe to accept from a
test assembly and refuse from a package.

## How it works

A layer reaches the analyzer by two routes, and both are needed:

- **The compilation under analysis** reads its own layer from the `VarveLayer`
  MSBuild property, surfaced to the compiler by
  `<CompilerVisibleProperty Include="VarveLayer" />` in `Directory.Build.props`
  and read as `build_property.VarveLayer`.
- **A referenced assembly** is already compiled, so its layer is read back from
  the `[assembly: AssemblyMetadata("Varve.Layer", n)]` attribute that
  `Directory.Build.targets` emits.

The BCL's `AssemblyMetadataAttribute` is used rather than a Varve-defined
attribute. A shared attribute type would have to live in a package below layer
0, which is the same as saying it would break the rule it exists to express.

## How to satisfy it

Declare the layer in the project file:

```xml
<PropertyGroup>
  <VarveLayer>1</VarveLayer>
</PropertyGroup>
```

For a test, benchmark or analyzer assembly:

```xml
<PropertyGroup>
  <VarveLayer>none</VarveLayer>
</PropertyGroup>
```

If the diagnostic names a *referenced* assembly, the missing declaration is in
that assembly's project, not yours.

If it names an assembly you do not own — a `Varve.*` package from elsewhere —
that package was not built with this repository's `Directory.Build.targets` and
cannot be layer-checked. Take that seriously rather than suppressing it: an
unlayered `Varve.*` dependency is outside the graph the rule describes.

## Suppression

Effectively never. Suppressing this rule re-opens the omission bypass it exists
to close, which means suppressing `VARVE0001` as well, indirectly and without
saying so. If a case genuinely warrants it, it warrants a superseding ADR.

## Known limit

**The `Varve.Analyzers` exemption is by name.** An assembly literally named
`Varve.Analyzers` is exempt from carrying layer metadata when referenced,
because it is a build-time component with no meaningful layer. A future
assembly could take that name and skip the check.

An analyzer cannot see whether an assembly is packable, or whether it was
referenced as an analyzer rather than as a library, so there is no better
discriminator available at the point where the rule runs. This is recorded as a
limit in [ADR 0004](../adr/0004-enforcement-by-analyzers.md) rather than
papered over. If it ever matters in practice, the fix is the CI-side
package-graph check, which can see what an analyzer cannot.
