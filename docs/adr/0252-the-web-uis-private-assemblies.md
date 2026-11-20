# 0252 — The Web UI's private assemblies: three layers, shared components eager, the explorer lazy

## Status

**Accepted — filed unaccepted by the session for Web UI part 2 (#355, under
#96), 2026-11-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer as D1-b on the part 2 plan, 2026-11-18,
with one condition: VARVE0006 covers all three assemblies. Revised in place on
2026-11-19, before acceptance, by the maintainer's ruling that DD0001 is right
and the layering is what changes: the three assemblies take layers 6, 7 and 8.
Revised in place again on 2026-11-20, by the maintainer's ruling that a
component is eager only when a console page uses it (point 7).

It records the decision set
[`the-web-uis-private-assemblies`](../decisions/the-web-uis-private-assemblies.md),
and amends [0060](0060-hosts-at-layer-6-the-composition-root.md),
[0200](0200-varve-webui-a-blazor-webassembly-host.md) and
[0206](0206-the-web-ui-design-source.md) by dated amendments in their files.
Where each ruling is recorded, all filed unaccepted:

| Ruling | Set | Key |
|---|---|---|
| Point 1, the three assemblies at 6, 7 and 8 | `the-web-uis-private-assemblies` | `ThreeAssembliesThreeLayers` |
| Point 2, host layers 6 and up, a layer 0 to 8 (VARVE0005) | `hosts-at-layer-6-the-composition-root` (0060, amended) | `HostLayersSixToEight` |
| Point 3, a host's private assembly (VARVE0005) | `hosts-at-layer-6-the-composition-root` (0060, amended) | `PrivateAssembliesOfAHost` |
| Point 4, namespaces | `the-web-uis-private-assemblies` | `NamespacesCarryAssemblyAndLevel` |
| Point 4, the levels across the three (VARVE0006) | `the-web-ui-design-source` (0206, amended) | `LevelsAcrossThreeAssemblies` |
| Point 5, VARVE0006 holds the three apart | `the-web-uis-private-assemblies` | `VarveSixHoldsTheThreeApart` |
| Point 6, the explorer lazy-loaded | `the-web-uis-private-assemblies` | `ExplorerLoadedByItsRoutes` |
| Point 7, eager only when a console page uses it | `the-web-uis-private-assemblies` | `EagerOnlyWhenAConsolePageUses` |
| The host above its private assemblies; Blazor scoped to the three | `varve-webui-a-blazor-webassembly-host` (0200, amended) | `HostAboveItsPrivateAssemblies` |

Points 2 and 3 change ADR 0060's layer table, so their rulings are in 0060's
set and not repeated here. ADR 0205's amendment of the same day, the size
gate's two rows, records `TwoRowsFirstScreenAndLazySet`, `TheLazySetIsNamed`
and `ExplorerBudgetMeasured` in `the-web-uis-size-budget-and-no-aot`.
VARVE0005 and VARVE0006 cite their rulings by key in their remarks: an
analyzer cannot carry `[Contract]`, which would need a reference to the
decision ledger from a `netstandard2.0` analyzer assembly.

## Context

The data explorer (#96, part 2) is part of the Web UI, and the console's first
screen keeps ADR 0205's budget (ruling 5 of 2026-11-02). Blazor lazy-loads
whole assemblies. Measured on the plan: the parser and the results reader
cost 61.9 kB lazy and would push the first screen over its budget eagerly;
the explorer's own components and pages would grow `Varve.WebUi.wasm`, which
is eager. By 2026-11-19 the first screen measured 1,901.6 kB of its
1,920.0 kB with the explorer's components in the host.

So the explorer has to be an assembly of its own. It cannot reference the
host, which references it, so what both page sets use is a third assembly.
Three rules applied:

- **DD0001** refuses a reference within the family that does not point
  strictly downward. Its logic is a plain comparison of the two layers, with
  no exemption for a composition root or an executable.
- **VARVE0005** (ADR 0060, ADR 0064) held layer 6 as the top, for
  executables only.
- **DD0006** wants a public type under its assembly's name.

The first draft of this ADR put all three assemblies at layer 6. DD0001
refused the explorer's reference to the components and the host's references
to both. A same-layer reference is what "strictly downward" forbids, and the
rule was right: the three are not peers, they depend on each other in one
direction.

## Decision

1. **Three assemblies, three layers.**

   | Layer | Assembly | Loaded | Holds |
   |---:|---|---|---|
   | 6 | `Varve.WebUi.Components` | eager | what a console page uses, which the explorer's pages may use too (point 7): the design system's components the console composes (Atoms to Templates), the parts both use (the problem view, the read stamps, the term, the unreachable notice), `Pages.Services`' `DatasetClients`, and the values they need (`Tones`, `Times`, `ProblemDocs`, `WebUiSettings`, `TermText`, the signed-in `Caller`) |
   | 7 | `Varve.WebUi.Explorer` | lazy | the explorer's pages and parts, the components only its pages use, at their levels, and its references to `Varve.Sparql` and, through the client, `Varve.Sparql.Results` |
   | 8 | `Varve.WebUi` | the host | the app, the console's pages and parts, and the sign-in |

   Every reference among them points strictly down: the host (8) to the
   explorer (7) and the components (6), the explorer (7) to the components
   (6). DD0001 holds that as it holds every other reference in the family,
   with no exception. The explorer naming its host is upward, and refused.
2. **The host layers are 6 and up.** A host that has private assemblies
   stacks them below itself, each at its own layer. Every other host stays at
   6. A layer is an integer from 0 to 8.
3. **A host's private assembly** is a library at a host layer, never packed,
   that names its host in `VarveHostPartOf` and is part of its composition
   root (`ArchCompositionRoot`). VARVE0005 admits a library at 6 or above
   only when it names its host, and an executable anywhere from 6 up
   (ADR 0060, amended).
4. **Namespaces carry the assembly and the level.** `Varve.WebUi.<Level>` in
   the host, and `Varve.WebUi.Components.<Level>` and
   `Varve.WebUi.Explorer.<Level>` in the other two, so every public type is
   under its assembly's name (DD0006), and VARVE0006 reads the level after
   the assembly's part (ADR 0206 point 4, amended).
5. **VARVE0006 holds the three apart** (the maintainer's condition), beside
   DD0001's direction: the components name nothing of the host's or the
   explorer's, and declare no routed page and nothing of the app; the
   explorer names nothing of the host's; and the host names nothing of the
   explorer's, so nothing loads the lazy assembly with the first screen. Each
   rule has violating samples, and a build fixture shows DD0001 refusing an
   explorer that references its host.
6. **Lazy loading.** The host marks `Varve.WebUi.Explorer` and `Varve.Sparql`
   lazy, and `Varve.Sparql.Results` when a page streams a query. The router
   loads them when a route of the explorer is first visited, deep links
   included, and adds their pages from what the loader returns.
7. **A component is eager only when a console page uses it.** The reason
   `Varve.WebUi.Components` loads with the first screen is that the console
   needs it, so it holds only what a console page, part or the shell uses,
   directly or through another component. A component that only explorer
   pages use, or one designed for an explorer screen and not yet on a page,
   is in `Varve.WebUi.Explorer`, at its level there, and VARVE0006's levels
   hold inside it as everywhere. When a console page first uses an explorer
   component, the component moves down to the components assembly. A
   console design on no page (`LagMeter`) stays, and the trimmer drops it.
   The inventory that applied this on 2026-11-20 is on #355: 18 files moved,
   and the first screen dropped 4.8 kB.

## Alternatives considered

- **All three at layer 6**, the first draft. DD0001 refuses every reference
  among them, rightly; answering it by a decision would have weakened the
  one rule that keeps the family acyclic.
- **An upstream exemption in DD0001** for private assemblies of one
  composition root. Refused by the maintainer: the rule is right.
- **Everything in `Varve.WebUi`** (D1-a). The first screen would grow by the
  explorer's components, at 18.4 kB from its budget.
- **Components at layer 5.** Blazor components are not an integration, and
  layer 5's assemblies reference `Varve.Protocol.Client` at their own layer.
- **A family of its own for the Web UI.** It would put Blazor assemblies at
  layer numbers that mean something else in Varve's table.

## Consequences

- Part 1's components moved with no markup change; their contract cases hold
  them (ADR 0206 point 6), and the contract test finds a component in any of
  the three assemblies.
- The size gate's two rows (ADR 0205, amended) measure the first screen
  without the explorer, and the explorer's lazy set.
- **Checks:**
  - Against the accepted ADRs: 0060, 0200 and 0206 are amended in their
    files; 0003 (no `Common` or `Utils`: the names say what each holds);
    0202 (the CSP: lazy assemblies are fetched from `self`); 0064 (VARVE0005
    reads the new layers and `VarveHostPartOf`).
  - Layer ownership: layers 6, 7 and 8, in AGENTS.md's table.
  - Analyzer rules: VARVE0005 (layers 0 to 8; a library at a host layer names
    its host) and VARVE0006 (the three assemblies), each with tests and its
    rule page; DD0001 unchanged.
