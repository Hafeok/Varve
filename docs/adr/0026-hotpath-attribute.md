# 0026 — Where the `[HotPath]` attribute lives

## Status

**Accepted.** 2026-09-21.

## Context

`docs/brief.md` reserves a rule: members marked with a `[HotPath]` attribute
may not box, capture closures, allocate arrays or strings, use LINQ, or call
members that are not hot-path-safe. ADR 0004 reserves it as **VARVE0006** and
it is not implemented.

The attribute has a placement problem that is not obvious until it is stated.
An attribute is a type, and a type has to live somewhere. Every packable
project needs it — the parsers, the model, the store, the evaluator — so the
obvious answer is a shared package.

**There cannot be one.** Principle 3 forbids `Common`, `Core`, `Utils`,
`Helpers` and `Abstractions` packages outright, and the layering rule (ADR
0003) forbids something more specific: a package every other package
references must sit below layer 0, and layer 0 is `Varve.Iri` and `Varve.Xsd`,
which are real components with real content. A package containing one attribute
would be exactly the grab-bag the brief names.

This is the same problem ADR 0003 already solved once. It needed every
assembly to carry a layer number, could not define an attribute to carry it,
and used the BCL's `AssemblyMetadataAttribute` instead. There is no BCL
attribute meaning "hot path", so the same answer is not available twice.

## Decision

**One source file, linked into every packable project, defining an `internal`
attribute.**

`eng/HotPathAttribute.cs`:

```csharp
namespace Varve;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property
                | AttributeTargets.Constructor, Inherited = false)]
internal sealed class HotPathAttribute : Attribute { }
```

Linked by `Directory.Build.targets` into every project, with `Link` so it
appears under `Properties/` rather than polluting the project's own tree.

**`internal`**, which does three things at once:

- it never enters a public API baseline, so marking a method does not change
  the package's surface;
- it never reaches a consumer, so nobody outside Varve can mark anything;
- it makes the duplication harmless — each assembly gets its own copy of
  `Varve.HotPathAttribute`, and since neither is public they cannot collide.

**VARVE0006 will match it by full name**, `Varve.HotPathAttribute`, not by
symbol identity — because there is no single symbol to identify. That is the
same technique the layer rules already use for `AssemblyMetadata`.

**Marking starts now**, at milestone 3a, on the parse and write paths. The rule
does not exist yet; the marks do. A rule that arrives to find nothing marked
gets switched on against an empty set and proves nothing.

## Alternatives considered

- **A shared attributes package** below layer 0. The normal answer, and what
  most repositories do. Rejected by principle 3 and by ADR 0003's layering: it
  is a grab-bag by definition, and putting it below layer 0 makes every package
  depend on something with no reason to change except that other things need
  it.
- **A public attribute in `Varve.Rdf`.** No new package, and `Varve.Rdf` is at
  layer 1 so most things can reach it. Rejected twice over: `Varve.Iri` is at
  layer 0 and could not use it, which is precisely where the hottest code will
  be; and a public attribute would enter the API baseline and become something
  a consumer could apply to their own code, implying a guarantee we do not make.
- **`MethodImplOptions.AggressiveInlining` as a proxy**, or any existing BCL
  attribute reused for the purpose. Rejected: it means something else, and an
  analyzer keyed on it would fire on code that merely wanted inlining.
- **A naming convention** — methods in a `HotPath` region, or a filename
  suffix. Rejected: an analyzer can match it, but nothing stops a method
  drifting out of the convention silently, and ADR 0004's whole position is
  that a rule binds on something explicit.
- **Waiting until VARVE0006 exists.** The tidier sequence. Rejected because the
  marks are the expensive part and the rule is the cheap part: deciding which
  members are hot paths is a judgement made while writing them, and
  reconstructing it later from code that has been through three milestones is
  worse work.

## Consequences

**Every packable project compiles a copy of the attribute.** A few bytes of
metadata per assembly, and no way for two copies to conflict because both are
internal. The cost is real and trivial; the alternative was a package.

**The attribute is inert until VARVE0006.** Marking a method changes nothing
today. That is the point — it is a record of intent, made when the intent is
fresh, waiting for a rule that can check it. It also means a wrong mark costs
nothing to fix until the rule lands, and a great deal after.

**A linked file is invisible in the project tree** and easy to be surprised by.
`Directory.Build.targets` is where it happens and is the one place to look; the
`Link` metadata puts it under `Properties/` in an IDE, which is where a
generated or linked file is conventionally expected.

**VARVE0006's matching is by name and therefore forgeable.** Anything can
declare `Varve.HotPathAttribute` and be treated as marked. Inside a repository
whose projects are all ours this is not a threat, and it is the same trade ADR
0003 accepted for `AssemblyMetadata` — recorded here so that it is a known
limit rather than a surprise.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0020–0024).
  Touches **0003** (the layering argument that rules out a shared package, and
  the by-name matching technique it established), **0004** (VARVE0006 is
  reserved there; this decides its mechanism and reserves no new id), and
  **0024** (`Materialise()` inside a `[HotPath]` member is the first thing
  VARVE0006 should catch). No conflict with any.
- **Layer ownership.** None. The attribute is not in a package; it is a source
  file compiled into each assembly that needs it.
- **Analyzer rule.** **VARVE0006**, already reserved in ADR 0004's table. No
  new id is allocated, because this ADR decides a mechanism rather than a rule.
- **Open questions owned.** None.
