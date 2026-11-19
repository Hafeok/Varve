# 0252 — The Web UI's private assemblies: shared components eager, the explorer lazy

## Status

**Accepted — filed unaccepted by the session for Web UI part 2 (#355, under
#96), 2026-11-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer as D1-b on the part 2 plan, 2026-11-18,
with one condition: VARVE0006 covers all three assemblies.

It records the decision set
[`the-web-uis-private-assemblies`](../decisions/the-web-uis-private-assemblies.md),
and amends [0060](0060-hosts-are-layer-6.md), [0200](0200-the-web-ui-a-blazor-webassembly-host.md)
and [0206](0206-the-web-ui-design-source.md) by dated amendments in their files.

## Context

The data explorer (#96, part 2) is part of the Web UI, and the console's first
screen keeps ADR 0205's budget (ruling 5 of 2026-11-02). Blazor lazy-loads
whole assemblies. Measured on the plan: the parser and the results reader
cost 61.9 kB lazy and would push the first screen over its budget eagerly;
the explorer's own components and pages would grow `Varve.WebUi.wasm`, which
is eager, by an estimated 40 to 60 kB against 52.9 kB of headroom.

So the explorer has to be an assembly of its own, and an explorer assembly
cannot reference the host (the host references it). What both page sets use
has to be a third assembly. Three rules stood in the way: ADR 0060 reserves
layer 6 to executables (VARVE0005), DD0001 refuses a reference within a
layer, and DD0006 wants a public type under its assembly's name.

## Decision

1. **Three assemblies.**
   - **`Varve.WebUi`**, the host: the app, the console's pages and the parts
     only it uses.
   - **`Varve.WebUi.Components`**, eager: what both page sets share, which is
     the design system's levels (Atoms to Templates), `Pages.Services`, the
     parts both page sets use (the problem view, the read stamps, the term,
     the unreachable notice), and the values they need (`Tones`, `Times`,
     `ProblemDocs`, `WebUiSettings`, the sign-in).
   - **`Varve.WebUi.Explorer`**, lazy: the explorer's parts and pages, and its
     reference to `Varve.Sparql`.
2. **Namespaces carry the assembly and the level.** `Varve.WebUi.<Level>`
   in the host, `Varve.WebUi.Components.<Level>` and
   `Varve.WebUi.Explorer.<Level>` in the other two, so every public type is
   under its assembly's name (DD0006) and VARVE0006 reads the level after
   the assembly's part (ADR 0206 point 4, amended).
3. **A host's private assembly is layer 6.** It is a library, never packed,
   and names its host in `VarveHostPartOf`; VARVE0005 admits a layer-6
   library that does, and only that (ADR 0060, amended). It is part of the
   host's composition root (`ArchCompositionRoot`).
4. **References run host → explorer → components, and host → components.**
   The explorer's reference to the components is within layer 6, answered
   for DD0001 by this decision, because the alternative, a layer below for
   Blazor components, would put a browser framework into the package graph.
5. **VARVE0006 holds the three apart** (the maintainer's condition): the
   components name nothing of the host's or the explorer's and declare no
   routed page and nothing of the app; the explorer names nothing of the
   host's; and the host names nothing of the explorer's, so nothing loads
   the lazy assembly with the first screen. Each rule has violating samples.
6. **Lazy loading.** The host marks `Varve.WebUi.Explorer`, `Varve.Sparql`
   and `Varve.Sparql.Results` lazy. The router loads them when a route of
   the explorer is first visited, deep links included, and adds their pages
   from what the loader returns.

## Alternatives considered

- **Everything in `Varve.WebUi`** (D1-a). The first screen would grow by the
  explorer's components, and might not fit its budget.
- **Keeping the namespaces `Varve.WebUi.<Level>` in every assembly**, and
  answering DD0006 per type. The only reason would be that the moved files
  were named so; the design change is cheap and makes the assembly visible
  in every name.
- **Components at layer 5.** Blazor components are not an integration, and
  a layer-5 assembly with Blazor in its closure would sit in the package
  graph's layers.

## Consequences

- Part 1's components move with no markup change; their contract cases hold
  them (ADR 0206 point 6), and the contract test finds a component in any of
  the three assemblies.
- The size gate measures two rows: the first screen and the lazy set
  (ADR 0205, amended).
- **Checks:**
  - Against the accepted ADRs: 0060, 0200 and 0206 are amended by this
    ADR's own amendments; 0003 (no `Common` or `Utils`: the names say what
    each holds); 0202 (the CSP: lazy assemblies are fetched from `self`).
  - Layer ownership: layer 6.
  - Analyzer rules: VARVE0005 (a layer-6 library naming its host) and
    VARVE0006 (the three assemblies), each with tests and its rule page.
