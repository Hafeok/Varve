---
set: the-web-uis-private-assemblies
namespace: varve
adr: 0252
decisions:
  - key: ThreeAssemblies
    statement: "The Web UI is the host Varve.WebUi, the eager Varve.WebUi.Components holding what both page sets share, and the lazy Varve.WebUi.Explorer holding the explorer's parts and pages"
  - key: NamespacesCarryAssemblyAndLevel
    statement: "A Web UI type's namespace is Varve.WebUi.<Level> in the host and Varve.WebUi.Components.<Level> or Varve.WebUi.Explorer.<Level> in the other two"
  - key: HostPartsAreLayerSix
    statement: "A host's private assembly is a layer-6 library, never packed, that names its host in VarveHostPartOf and is part of its composition root"
  - key: ExplorerReferencesComponentsWithinLayerSix
    statement: "The explorer references the shared components within layer 6, as part of one host's composition root"
  - key: VarveSixHoldsTheThreeApart
    statement: "VARVE0006 refuses the components naming the host or the explorer or declaring a routed page or the app, the explorer naming the host, and the host naming the explorer"
  - key: ExplorerLoadedByItsRoutes
    statement: "The explorer, Varve.Sparql and Varve.Sparql.Results are lazy-loaded when an explorer route is first visited, and the router adds the pages the loader returns"
---

The rulings of [ADR 0252](../adr/0252-the-web-uis-private-assemblies.md), filed
unaccepted by the session for #355 (ADR 0066).
