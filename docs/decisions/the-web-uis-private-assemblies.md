---
set: the-web-uis-private-assemblies
namespace: varve
adr: 0252
decisions:
  - key: ThreeAssembliesThreeLayers
    statement: "The Web UI is Varve.WebUi.Components at layer 6, eager, holding what a console page uses; Varve.WebUi.Explorer at layer 7, lazy, holding the explorer's pages and parts and the components only they use; and the host Varve.WebUi at layer 8; every reference among them points strictly down"
  - key: HostLayersAreSixAndUp
    statement: "A host that has private assemblies stacks them below itself at their own layers, every other host stays at 6, and a layer is an integer from 0 to 8"
  - key: HostPartsNameTheirHost
    statement: "A host's private assembly is a library at a host layer, never packed, that names its host in VarveHostPartOf and is part of its composition root"
  - key: NamespacesCarryAssemblyAndLevel
    statement: "A Web UI type's namespace is Varve.WebUi.<Level> in the host and Varve.WebUi.Components.<Level> or Varve.WebUi.Explorer.<Level> in the other two"
  - key: VarveSixHoldsTheThreeApart
    statement: "VARVE0006 refuses the components naming the host or the explorer or declaring a routed page or the app, the explorer naming the host, and the host naming the explorer"
  - key: ExplorerLoadedByItsRoutes
    statement: "The explorer and what only it uses are lazy-loaded when an explorer route is first visited, and the router adds the pages the loader returns"
  - key: EagerOnlyWhenAConsolePageUses
    statement: "A component is in Varve.WebUi.Components, and so eager, only when a console page, part or the shell uses it; one only explorer pages use, or one designed for an explorer screen and not yet on a page, is in Varve.WebUi.Explorer at its level, and moves down when a console page first uses it"
---

The rulings of [ADR 0252](../adr/0252-the-web-uis-private-assemblies.md), filed
unaccepted by the session for #355 (ADR 0066).
