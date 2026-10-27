---
set: the-web-ui-design-source
namespace: varve
adr: 0206
decisions:
  - key: DocsUiIsTheDesignSource
    statement: "docs/ui/ is the Web UI's design source and changes only by pull request; the design system artifact is a published view of it, first copied at a stated version recorded in traceability"
  - key: TokensJsonIsCanonical
    statement: "tokens.json is canonical and tokens.css is generated from it; a check regenerates tokens.css and fails on any difference"
  - key: OneCopyOfTheCss
    statement: "Varve.WebUi links docs/ui/tokens.css and docs/ui/bundle.css into wwwroot without copying them"
  - key: PlainCssFromTheAppsOrigin
    statement: "The Web UI uses no component library, CSS framework, web font, icon font, style attribute or Blazor CSS isolation; classes carry the vw- prefix"
  - key: AtomicLevelsAreADependencyRule
    statement: "Components live in the Atoms, Molecules, Organisms, Templates, Parts and Pages namespaces of Varve.WebUi, in that order, and a component references only lower levels, never its own, with no exemption"
  - key: OnlyPagesCallTheApi
    statement: "Only Varve.WebUi.Pages, the routed screens, references or injects the client's service types, Varve.Protocol.Client outside its Model namespace; Parts and Pages may use the client's model types, and Atoms to Templates use neither"
  - key: OneStateToToneMapping
    statement: "One tested mapping in Varve.WebUi.Tones maps API state words and problem types to the five tones, called by parts and pages; atoms take a Tone and never map"
  - key: TheMarkupContractTest
    statement: "Each component's cases under docs/ui/components/<Comp>/cases/ are rendered by the Razor component and compared to their expected fragments semantically; a component with a preview and no cases fails"
---

The rulings of [ADR 0206](../adr/0206-the-web-ui-design-source.md),
filed unaccepted by #167 (ADR 0066).
