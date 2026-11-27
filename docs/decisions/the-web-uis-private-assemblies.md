---
set: the-web-uis-private-assemblies
namespace: varve
adr: 0252
decisions:
  - key: ThreeAssembliesThreeLayers
    statement: "The Web UI is Varve.WebUi.Components at layer 6, eager, holding what a console page uses; Varve.WebUi.Explorer at layer 7, lazy, holding the explorer's pages and parts and the components only they use; and the host Varve.WebUi at layer 8; every reference among them points strictly down"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: NamespacesCarryAssemblyAndLevel
    statement: "A Web UI type's namespace is Varve.WebUi.<Level> in the host and Varve.WebUi.Components.<Level> or Varve.WebUi.Explorer.<Level> in the other two"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: VarveSixHoldsTheThreeApart
    statement: "VARVE0006 refuses the components naming the host or the explorer or declaring a routed page or the app, the explorer naming the host, and the host naming the explorer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: ExplorerLoadedByItsRoutes
    statement: "The explorer and what only it uses are lazy-loaded when an explorer route is first visited, and the router adds the pages the loader returns"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: EagerOnlyWhenAConsolePageUses
    statement: "A component is in Varve.WebUi.Components, and so eager, only when a console page, part or the shell uses it; one only explorer pages use, or one designed for an explorer screen and not yet on a page, is in Varve.WebUi.Explorer at its level, and moves down when a console page first uses it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
---

The rulings of [ADR 0252](../adr/0252-the-web-uis-private-assemblies.md), filed
unaccepted by the session for #355 (ADR 0066) and accepted by the maintainer
on 2026-11-24, written by his delegate (ADR 0239).
