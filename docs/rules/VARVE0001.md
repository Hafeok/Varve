# VARVE0001 — Reference is not to a strictly lower layer

| | |
|---|---|
| **Category** | `Varve.Layering` |
| **Severity** | Error |
| **Motivated by** | [ADR 0003 — Package layering](../adr/0003-package-layering.md) |
| **Enforcement policy** | [ADR 0004](../adr/0004-enforcement-by-analyzers.md) |

## What it reports

A compilation references a `Varve.*` assembly whose declared layer is not
strictly lower than the declaring assembly's own.

```
error VARVE0001: 'Varve.Turtle' (layer 2) references 'Varve.Sparql' (layer 2).
A Varve package may reference only packages in a strictly lower layer, and a
same-layer reference is a violation: the two are one package or two layers
(ADR 0003).
```

The diagnostic has no source location. It is a fact about an assembly and its
reference set, not about a line of code, so it is reported against the
compilation and appears as `CSC : error VARVE0001`.

## Why

The package graph is a DAG with fixed layers, and a package may reference only
packages in a lower layer. The layers are in
[ADR 0003](../adr/0003-package-layering.md); the short form is that contracts
live in the lowest layer that can define them without knowing their
implementers, and everything above composes.

Layering is cheap to declare and expensive to retrofit. A cycle introduced in
month three is discovered in month nine as an inability to publish one package
without the other, and by then the fix is a redesign.

**Same-layer references are violations, not exceptions.** Two packages in one
layer that need each other are one package or two layers. The rule exists to
force that question to be answered rather than deferred; if it allowed
same-layer references, the layer numbers would describe a preference rather
than a structure.

## How to satisfy it

Three real options, in the order worth trying:

1. **Move the shared type down.** Usually the reference exists because both
   packages need a type one of them happens to own. If that type does not need
   to know its implementers, it belongs in a lower layer — typically
   `Varve.Rdf` at layer 1.
2. **Invert the dependency with a contract.** The lower package defines a small
   interface over `Varve.Rdf` types; the higher one implements it; a package at
   layer 5 composes them. This is what
   [ADR 0005](../adr/0005-store-is-sparql-free.md) does for SPARQL Update over
   the store, and it is the pattern, not a special case.
3. **Merge the two packages**, if they genuinely have one reason to change.
   High cohesion is a principle in its own right, and two packages that cannot
   be separated were never two packages.

What does not work, and is called out in ADR 0003 because it looks like it
does: keeping the reference but typing it as a callback, a service locator, or
an `InternalsVisibleTo`. The dependency is real whichever way the arrow points
in the project file.

## Suppression

Legitimate only with a recorded exception. The justification cites the ADR:

```csharp
[SuppressMessage("Varve.Layering", "VARVE0001:Reference is not to a strictly lower layer",
    Justification = "ADR 0003: recorded exception, see open question 1.")]
```

A repository-wide `NoWarn` is not an allowed form. See
[ADR 0004](../adr/0004-enforcement-by-analyzers.md) for the suppression policy;
`VARVE0008` will enforce the ADR citation once it is implemented.

## Not reported

- A compilation whose assembly is not named `Varve.*`.
- A test assembly, or `Varve.Analyzers`. These compose across layers by
  nature and declare `VarveLayer=none`. A benchmark assembly is a host and
  declares layer 6 (ADR 0060), so it is checked like any other.
- A compilation whose own layer declaration is missing or malformed, and a
  reference whose layer metadata is missing or malformed. Both are
  [VARVE0002](VARVE0002.md), and reporting them here as well would say the same
  thing twice.

## Known limit

An analyzer sees one compilation at a time, so this rule checks the assemblies
a compilation references — not the published package graph a consumer would
resolve. A violation expressible only through NuGet metadata is not visible
here. ADR 0003 records a CI-side package-graph check as the complementary net;
it is not built yet.
