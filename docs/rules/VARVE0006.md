# VARVE0006 — A Web UI component uses only lower levels, only pages and the app use the client's services, and the client's model holds values only

| | |
|---|---|
| **Category** | `Varve.Layering` |
| **Tier** | 1 (error) |
| **Motivated by** | [ADR 0206](../adr/0206-the-web-ui-design-source.md) point 4 and its amendment of 2026-10-30, `TheWebUiDesignSource.AtomicLevelsAreADependencyRule`, `.OnlyPagesCallTheApi`, `.TheAppIsTheTopLevel` and `.NoTypeOutsideTheLevelsHoldsAService`; the brand book's levels, [`docs/ui/README.md`](../ui/README.md) |

## Principle

The Web UI's components are organised by atomic level, and the levels are a
dependency rule: the stable dependency principle applied inside one host. The
lowest levels have the most dependents and change least, and they know
nothing of Varve's domain. Parts hold the domain. Only pages, the routed
screens, and the app above them read the server. A rule that exists only in a document is not a
rule, so this is the analyzer that makes it one.

## What it reports

The levels are namespaces, lowest first:
1. `Varve.WebUi.Atoms`
2. `Varve.WebUi.Molecules`
3. `Varve.WebUi.Organisms`
4. `Varve.WebUi.Templates`
5. `Varve.WebUi.Parts`
6. `Varve.WebUi.Pages`
7. `Varve.WebUi.App`: the root component, the layout every screen renders in,
   and the services the shell reads. It renders the pages, and no level uses
   it, so it is the top.

A namespace below a level is that level: the services a page uses,
`Varve.WebUi.Pages.Services`, are of the page level.

For every name that resolves to a type, it makes three checks: the first two
in the Web UI, the third in the client's model.

1. **The level order.** A component, a type implementing
   `Microsoft.AspNetCore.Components.IComponent`, may be used only from a
   higher level:
   - it must be in a level namespace lower than the user's;
   - its own level is refused too, so parts don't use parts and pages don't use
     pages;
   - a type that is not a component, such as a record a list takes, may be of
     the user's own level or lower, never higher: a type of a higher level may
     hold what this level must not reach;
   - a type naming itself is not counted;
   - there is no exemption at the top: the app's root does not name its
     layout, which is of its own level. The composition root, `Program`,
     which is in no Web UI namespace, hands it the layout's type.
2. **Services against models.**
   - A type in `Varve.Protocol.Client.Model` (a problem body, a position, a
     status) is named only in `Parts`, `Pages` and `App`.
   - Any other type under `Varve.Protocol.Client` is named only in `Pages` and
     `App`. Those are the services that call the server: `SparqlHttpClient`,
     `VarveHealthClient` and the rest.
   - **A type of the Web UI in no level** (`Varve.WebUi`, `Varve.WebUi.SignIn`
     and the like) names no service, though it may name the model. Otherwise
     it would be a way round the levels: a part names it, and reaches the
     server through it. The Web UI is the assembly that declares the levels;
     its tests reference them, are not the Web UI, and are not held to this.

3. **The model holds values only.** A type in `Varve.Protocol.Client.Model`
   names no other type under `Varve.Protocol.Client`. Without this check, the
   model could quietly become a back door: a part may name a model type, so a
   model type that hands out a service would put the service in the part.
   This check needs no Blazor, so it runs in the client itself. It found one
   type when it was added: `BearerToken.Fixed` returned a
   `BearerTokenProvider`. That moved to `BearerTokens.Fixed`, beside the
   delegate (ADR 0127's amendment of 2026-10-27).

**The hole this closed (#187, 2026-10-30).** Until then, types outside the
level namespaces were not checked at all. `ShellFacts`, in `Varve.WebUi`, held
the management client, and `MainLayout`, then a part, named it: a part read
the server, and the rule said nothing. The violating sample
`A_type_in_no_level_naming_a_service_is_reported` is that type, and
`A_level_naming_a_type_of_a_higher_level_is_reported` is the part naming it.
`ShellFacts` and `MainLayout` are now `App`'s, and the services pages use are
`Pages.Services`'.

A type outside the Web UI's namespaces, such as the composition root
`Program`, is not checked. A compilation without Blazor's `IComponent` makes
only the third check.

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
- **The shell needs what no screen reads:** it is the app's. `MainLayout` reads
  it through a service in `Varve.WebUi.App`, and passes it down as
  parameters, or as a cascading value of a type the lower level may name.
- **A type outside the levels needs a service:** it is a service holder. Put it
  in the level that uses it: `Pages.Services` for a page's, `App` for the
  shell's.

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
- **The model names no service** (check 3 above).
- **A type a part needs moves into `.Model`; it does not earn an exception.**
  There is no marker attribute and no list of allowed types: the namespace is
  the whole test.
