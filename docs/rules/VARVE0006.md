# VARVE0006 — A Web UI component uses only lower levels, and only pages use the client's services

| | |
|---|---|
| **Category** | `Varve.Layering` |
| **Tier** | 1 (error) |
| **Motivated by** | [ADR 0206](../adr/0206-the-web-ui-design-source.md) point 4, `TheWebUiDesignSource.AtomicLevelsAreADependencyRule` and `.OnlyPagesCallTheApi`; the brand book's levels, [`docs/ui/README.md`](../ui/README.md) |

## Principle

The Web UI's components are organised by atomic level, and the levels are a
dependency rule: the stable dependency principle applied inside one host. The
lowest levels have the most dependents and change least, and they know
nothing of Varve's domain. Parts hold the domain. Only pages, the routed
screens, read the server. A rule that exists only in a document is not a
rule, so this is the analyzer that makes it one.

## What it reports

The levels are namespaces, lowest first:
1. `Varve.WebUi.Atoms`
2. `Varve.WebUi.Molecules`
3. `Varve.WebUi.Organisms`
4. `Varve.WebUi.Templates`
5. `Varve.WebUi.Parts`
6. `Varve.WebUi.Pages`

For every name that a type in a level namespace writes, and that resolves to a
type, it makes two checks:

1. **The level order.** A component, a type implementing
   `Microsoft.AspNetCore.Components.IComponent`, may be used only from a
   higher level:
   - it must be in a level namespace lower than the user's;
   - its own level is refused too, so parts don't use parts and pages don't use
     pages;
   - a type of the same level that is not a component, such as a record a list
     takes, is not counted;
   - nor is a type naming itself.
2. **Services against models.**
   - A type in `Varve.Protocol.Client.Model` (a problem body, a position, a
     status) is named only in `Parts` and `Pages`.
   - Any other type under `Varve.Protocol.Client` is named only in `Pages`.
     Those are the services that call the server: `SparqlHttpClient`,
     `VarveHealthClient` and the rest.

Types outside the level namespaces, such as `App` and `Tones`, are not checked.
A compilation without Blazor's `IComponent` is not checked at all.

## Generated code, and only for this rule

Razor compiles each component to C# through a source generator. That is
generated code, and an analyzer neither analyses nor reports in it unless it
opts in. A component that renders another names it in
`__builder.OpenComponent<global::Varve.WebUi.Atoms.Button>(…)`, so the
generated code is where the edges are.

This rule opts in, with `ConfigureGeneratedCodeAnalysis(Analyze |
ReportDiagnostics)`. The setting is per analyzer, so the `DD` rules stay out of
the components, whose parameters are not contracts.

Where Razor maps generated code back to the `.razor` file, as for an
`@inject` or an expression, the diagnostic points there. A component tag has
no such mapping, so a level violation points at the `OpenComponent` line of the
generated `*_razor.g.cs`. Its message names both the component that uses and
the one used.

A `cref` in a documentation comment, and a `nameof`, name a type without using
it, and are not counted. Razor writes a `nameof` beside the `OpenComponent` for
each parameter it sets, so one tag is one report.

## How to satisfy it

- **A component needs what another of its level renders:** render both from a
  higher level, and pass one's output to the other in a slot (a
  `RenderFragment`). `ConfirmDialog` takes the problem panel this way, and
  `ResourceTable` takes its problem the same way.
- **A component needs Varve's wording or the client's model:** it is a part.
  Move it to `Varve.WebUi.Parts`, and give the lower level abstract parameters
  (words, tones, numbers, slots).
- **A component reads the server:** it is a page. Pages are the routed screens;
  the data flows down from them as parameters.

## Exception path

None. The rule has no `[DesignDecision]` path. If two components of one level
genuinely need each other, that is a design conversation, and its outcome
changes ADR 0206, not this rule's reach.

## False-positive story

Tier 1 and decidable: namespaces, one interface, and the symbols the compiler
resolves.

The service/model split is by namespace, and the namespace is the convention
below.

## The client's convention: the model holds values only

This is a stated rule, not a habit (ADR 0206 point 4):
- **`Varve.Protocol.Client.Model` holds values only:** problem bodies,
  response facts, readiness, positions, and their wrappers. Nothing in it
  calls the server.
- **Everything else under `Varve.Protocol.Client`, `.Hosting` included,
  counts as a service.** That is strict on the few values there that call
  nothing: `ClientLimits`, `EndpointPolicy`, the token provider, and the
  follow and setup options. No part needs them today.
- **A type a part needs moves into `.Model`; it does not earn an exception.**
  There is no marker attribute and no list of allowed types: the namespace is
  the whole test.
