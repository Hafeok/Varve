---
set: the-web-ui-design-source
namespace: varve
adr: 0206
decisions:
  - key: DocsUiIsTheDesignSource
    statement: "docs/ui/ is the Web UI's design source and changes only by pull request; the design system artifact is a published view of it, first copied at a stated version recorded in traceability"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: TokensJsonIsCanonical
    statement: "tokens.json is canonical and tokens.css is generated from it as custom properties only, with no class; a check regenerates tokens.css and fails on any difference"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: OneCopyOfTheCss
    statement: "Varve.WebUi links docs/ui/tokens.css and docs/ui/bundle.css into wwwroot without copying them"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: PlainCssFromTheAppsOrigin
    statement: "The Web UI uses no component library, CSS framework, web font, icon font, style attribute or Blazor CSS isolation; classes carry the vw- prefix"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: AtomicLevelsAreADependencyRule
    statement: "Components live in the Atoms, Molecules, Organisms, Templates, Parts and Pages namespaces of Varve.WebUi, in that order, and a component references only lower levels, never its own, with no exemption"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: OnlyPagesCallTheApi
    statement: "Only Varve.WebUi.Pages, the routed screens, references or injects the client's service types, Varve.Protocol.Client outside its Model namespace; Parts and Pages may use the client's model types, and Atoms to Templates use neither; the model holds values only and names no service type"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: TheAppIsTheTopLevel
    statement: "Varve.WebUi.App, holding the root component, the layout and the services the shell reads, is the level above Pages: it may use the client's service and model types, no level names a type of it, and its root does not name its layout, which the composition root hands in; a namespace below a level is that level"
  - key: NoTypeOutsideTheLevelsHoldsAService
    statement: "No type of the Web UI outside the level namespaces names a client service type, and no type names a type of a higher level, component or not, so no level reaches the server except Pages and App"
  - key: OneStateToToneMapping
    statement: "One tested mapping in Varve.WebUi.Tones maps API state words and problem types to the five tones, called by parts and pages; atoms take a Tone and never map"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: TheMarkupContractTest
    statement: "Each component's cases under docs/ui/components/<Comp>/cases/ are rendered by the Razor component and compared to their expected fragments semantically; a component with a preview and no cases fails, and each preview shows its cases' fragments verbatim"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
---

The rulings of [ADR 0206](../adr/0206-the-web-ui-design-source.md),
filed unaccepted by #167 (ADR 0066).
