# VARVE0002 — Layer declaration is missing, invalid, or worked around

| | |
|---|---|
| **Category** | `Varve.Layering` |
| **Severity** | Error |
| **Motivated by** | [ADR 0003 — Package layering](../adr/0003-package-layering.md), [ADR 0060 — Hosts at layer 6](../adr/0060-hosts-at-layer-6-the-composition-root.md) |
| **Enforcement policy** | [ADR 0004](../adr/0004-enforcement-by-analyzers.md) |

## What it reports

Six situations that would leave [VARVE0001](VARVE0001.md) with nothing to
check rather than something to fail, and two that put a composition root in
the wrong layer.

**The compilation declares no layer.**

```
error VARVE0002: Assembly 'Varve.Rdf' declares no layer. A Varve.* assembly
must set the VarveLayer MSBuild property to an integer from 0 to 6, or to
'none' if it is a test or analyzer assembly (ADR 0003, ADR 0060).
```

**It declares `none` without being entitled to.**

```
error VARVE0002: Assembly 'Varve.Rdf' declares VarveLayer as 'none', which is
allowed only for a test assembly or Varve.Analyzers. A published Varve package
has a layer, and a host or benchmark declares layer 6 (ADR 0003, ADR 0060).
```

**The declared value is not a layer.** An integer outside 0–6, or anything
non-numeric that is not the literal `none`.

**A referenced `Varve.*` assembly carries no usable `Varve.Layer` metadata** —
either absent, or present and malformed.

**A packable assembly declares `none`**, whatever it is named.

```
error VARVE0002: Assembly 'Varve.Rdf.Tests' declares VarveLayer as 'none' but
is packable. Whatever it is named, an assembly that is packed is in the package
graph the layering rule describes and must declare an integer from 0 to 6. Set
a layer, or set IsPackable to false (ADR 0003).
```

**An executable declares a layer other than 6.** An executable — `OutputType`
`Exe` or `WinExe` — is a composition root, which ADR 0060 reserves to layer 6.
A test assembly is an executable too, under xUnit v3, and is untouched: it
declares `none`.

```
error VARVE0002: Assembly 'Varve.AotSmoke' is an executable declaring layer 5.
An executable is a composition root, which ADR 0060 reserves to layer 6:
declare VarveLayer 6, or build it as a library.
```

**A library declares layer 6.** Nothing is above layer 6, so nothing may
reference it; a library there is unreachable.

```
error VARVE0002: Assembly 'Varve.Something' declares layer 6 but is not an
executable. Layer 6 is the composition root and nothing may reference it, so a
library there is unreachable: build it as an executable, or give it the layer
of what it is — an integration is layer 5 (ADR 0060).
```

**`Varve.Analyzers` appears among a compilation's referenced assemblies**, from
any assembly other than `Varve.Analyzers.Tests`.

```
error VARVE0002: Assembly 'Varve.Analyzers' is referenced as an ordinary
library. Varve.Analyzers is a build-time component: it is passed to the
compiler as an analyzer and must never appear among a compilation's references.
Reference it with OutputItemType="Analyzer" and ReferenceOutputAssembly="false"
(ADR 0004).
```

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

The last two cases exist because a name is a weak thing to hang an exemption
on. **Whether an assembly is published is the question layering actually turns
on**, and `IsPackable` answers it directly: an assembly that is packed is in
the package graph, whatever it calls itself. The name check stays alongside it
because the two catch different things — a library that is not yet packable is
still held to its name — and it is the pair that leaves no way through.

The analyzer-reference case is not about layers at all. An analyzer reaches the
compiler as `/analyzer:` and never as `/reference:`, so it cannot appear among
`Compilation.SourceModule.ReferencedAssemblySymbols` under this repository's
wiring. If it does, someone referenced it as an ordinary library — which is
both a packaging mistake and the exact shape the old by-name exemption used to
wave through.

## How it works

A layer reaches the analyzer by two routes, and both are needed:

- **The compilation under analysis** reads its own layer from the `VarveLayer`
  MSBuild property, surfaced to the compiler by
  `<CompilerVisibleProperty Include="VarveLayer" />` in `Directory.Build.props`
  and read as `build_property.VarveLayer`.
- **A referenced assembly** is already compiled, so its layer is read back from
  the `[assembly: AssemblyMetadata("Varve.Layer", n)]` attribute that
  `Directory.Build.targets` emits.

`IsPackable` arrives the same way as the declaring compilation's layer:
`<CompilerVisibleProperty Include="IsPackable" />` in `Directory.Build.props`,
read as `build_property.IsPackable`. Absent or unparseable is read as not
packable, which is safe because the repository defaults it to false and a
package project opts in explicitly.

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

For a host — the server, the CLI, a smoke app, a benchmark — which is an
executable:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <VarveLayer>6</VarveLayer>
</PropertyGroup>
```

For a test or analyzer assembly:

```xml
<PropertyGroup>
  <VarveLayer>none</VarveLayer>
</PropertyGroup>
```

A packable project must declare a real layer; `none` is not available to it. If
the project should not be packed, set `<IsPackable>false</IsPackable>` — which
is the repository default, so this only comes up where something set it to
true.

If the diagnostic says `Varve.Analyzers` is referenced as a library, change the
reference to an analyzer reference:

```xml
<ProjectReference Include="../../src/Varve.Analyzers/Varve.Analyzers.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

`Directory.Build.props` already does this for every project, so an ordinary
library reference to it is something a project added deliberately.

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

## Amendment, 2026-09-21

This page previously carried a **Known limit** section stating that the
`Varve.Analyzers` exemption was by name, that an analyzer cannot see whether an
assembly is packable or whether it was referenced as an analyzer, and that
there was therefore no better discriminator.

**Both halves of that were wrong, and the section is removed.** An analyzer
*can* see whether an assembly is packable — `IsPackable` is an MSBuild property
like any other and only needed making compiler-visible. And it can tell an
analyzer reference from a library reference, because the first never reaches
`ReferencedAssemblySymbols` at all. The two checks described above replace the
exemption, and nothing about the rule now rests on an assembly's name alone.

The removal is recorded rather than performed silently, because the reasoning
that produced the wrong conclusion is the part worth being able to find again.
See the matching amendment in
[ADR 0004](../adr/0004-enforcement-by-analyzers.md).

## Amendment, 2026-09-25 — ADR 0060

Layer 6 exists, for hosts. The rule gains the two composition-root cases
above, and the by-name exemption no longer covers `*.Benchmarks`: a benchmark
composes the public packages as a host does, and declares 6.
