# 0206 — The Web UI's design source, its levels, and the markup contract

## Status

**Accepted — filed unaccepted by #167, 2026-10-27** (ADR 0066). Acceptance is the
maintainer's act on the pull request. The number is the next after Web UI part
1's range, 0200–0205 (the plan on #96); renumber at landing if it is taken.
Filed ahead of that milestone's pull request, which carries 0200–0205 and
implements this ADR's points 2, 4 and 6.

Records decision set
[`the-web-ui-design-source`](../decisions/the-web-ui-design-source.md).

## Context

`Varve.WebUi` (ADRs 0200–0205, filed with Web UI part 1's pull request, #167) is
a Blazor WebAssembly client of the public API.
Its visual design was made outside the repository, as a design system kept as a
claude.ai artifact: tokens in two themes, a brand book, and component previews
written as plain HTML over two stylesheets. Without a decision, the design lives
in a page the repository cannot review, and the app's CSS and markup drift from
it one change at a time.

Three constraints shape the answer. The console's CSP is `style-src 'self'`
with no `'unsafe-inline'`, so no inline style attribute renders. The bundle has
a size budget (ADR 0205), which web fonts and a CSS framework would spend. And
a rule that exists only in a document is not a rule (AGENTS.md), so the design's
structure has to be checked by the build.

## Decision

**1. `docs/ui/` is the design source, and changes only by pull request.** It
holds:
- `tokens.json`: the tokens, canonical;
- `tokens.css`: generated from `tokens.json`, never edited;
- `bundle.css`: the component stylesheet;
- `README.md`: the brand book (principles, content rules, colour, levels, terms,
  type, space, states, accessibility);
- `components/<Comp>/README.md` and `components/<Comp>/preview.html`: each
  component's guidelines and reference markup;
- `components/<Comp>/cases/`: the contract cases of point 6;
- `screens/*.html`: one reference page per console screen and state, built from
  the components' markup over the two stylesheets.

The first copy is taken from the design system artifact at a stated version;
the traceability record names the artifact and its version id. From then on the
artifact is a published view of `docs/ui/`, refreshed from it, never edited as
the source.

**2. One copy of the CSS.** `Varve.WebUi` links `docs/ui/tokens.css` and
`docs/ui/bundle.css` into `wwwroot/css/` with `<Content Include=… Link=…>`; no
build step copies them. A check regenerates `tokens.css` from `tokens.json` and
fails on any difference.

**3. Plain CSS, served from the app's origin.** No component library, no CSS
framework, no web fonts, no icon font, no `style` attributes, and no Blazor CSS
isolation (`.razor.css`), which rewrites selectors with generated attributes and
splits the shared stylesheet. Classes carry the `vw-` prefix; a data-dependent
width uses `<meter>` or a class, never a computed style.

**4. Components are organised by atomic level, and the levels are a dependency
rule.** Namespaces `Varve.WebUi.Atoms`, `.Molecules`, `.Organisms`,
`.Templates` and `.Pages`. A component references components of lower levels
only. Only `.Pages` references `Varve.Protocol.Client` or injects a service
that calls the server; everything below takes parameters. This is the stable
dependency principle (AGENTS.md, "Stable dependencies") applied inside one host: the
lowest levels have the most dependents and change least, and they are the
abstract ones, knowing nothing of Varve's domain.

**5. One state-to-tone mapping.** The mapping from an API state word or problem
type to one of the five tones (ok, warn, critical, neutral, past) is one pure,
tested mapping in `Varve.WebUi.Tones`, called by organisms and pages. Atoms take
a `Tone` (declared in `.Atoms`) and a word; no atom maps, and no page repeats
the table. A test enumerates `ProblemCatalogue` and the documented state words
and fails on any that has no tone.

**6. The markup contract test.** Each component has, under
`docs/ui/components/<Comp>/cases/`, one case per state the preview shows: its
parameters as JSON and its expected markup as an HTML fragment. A component
test renders the Razor component with each case's parameters and compares it to
the fragment with bUnit's semantic markup comparison (attribute order and
insignificant whitespace ignored). A design change without the code change, or
the reverse, fails the build. The preview shows the same fragments, so the
design system page and the app render one markup.

## Alternatives considered

- **The artifact as the source, copied into the repository when it changes.**
  The audit trail would sit outside Git, and a copy is a second source.
  Rejected for the same reason declarations are applied from Git.
- **A component library** (Fluent UI Blazor, MudBlazor) or a CSS framework.
  Package tails under constraint 4, bundle bytes under ADR 0205, and in some of
  them reflection the trimmer warns about. The console's component set is
  nineteen components; the design system already specifies them.
- **A separate Razor class library for the components.** One consumer today;
  part 2 is the same app. A package boundary, a public API baseline and a layer
  position for no gain. Revisited when a second consumer exists (Graphsite's
  terms and triples are the likely one), by its own ADR.
- **PNG screen references.** Not diffable, drift silently, and need a
  translation into markup. Screens are reference HTML; PNGs may be rendered from
  them when wanted.
- **Blazor CSS isolation.** Scoped styles per component, but generated
  attributes break the shared class contract and the semantic comparison of
  point 6.
- **A Storybook-style catalogue for Blazor.** A third-party dependency; the
  design system page and `docs/ui/` already serve as the catalogue.

## Consequences

- A visual change is a pull request to `docs/ui/` with the matching component
  change, and the contract test shows its reach: a token or atom change fails
  many cases, a page change few.
- The design system artifact lags `docs/ui/` until it is refreshed; whether that
  refresh can be automated from the repository is checked on first use and
  recorded here by dated amendment.
- Part 2 (the data explorer) inherits the levels, the tone mapping, and the
  term, provenance and change encodings already in `docs/ui/README.md`,
  including the graph view's scope.
- Splitting the components into their own package later is a move of
  namespaces, not a rewrite.

## Checks

- **Checked against the accepted ADRs** (0001–0178) and against Web UI part
  1's, filed unaccepted on its milestone branch (0200 and 0203 written, 0201,
  0202, 0204 and 0205 planned on #96). Touches:
  - **0060** (and its amendment of 2026-10-27 on that branch): `Varve.WebUi`
    stays a host at layer 6; the levels are namespaces inside it, not packages.
  - **0200**: the Web UI's host and its trimmed bundle.
  - **0202** (planned): the hosting and the CSP, `style-src 'self'` with no
    inline style, which point 3 serves.
  - **0204** (planned): bUnit, whose semantic comparison point 6 uses.
  - **0205** (planned): the size budget, which point 3 serves.
  - **0119**: the problem catalogue the tone mapping enumerates.

  No conflict.
- **Layer ownership.** `Varve.WebUi`, layer 6. `docs/ui/` is documentation.
- **Analyzer rule.** The level rule of point 4: a component type in a level
  namespace references component types only in lower level namespaces, and
  only `.Pages` references `Varve.Protocol.Client`. Razor components compile to
  C# through a source generator, and most analyzers skip generated code; the
  rule must opt into analysing it. Use the DecisionDriven layer rule if it can
  be declared per namespace; otherwise a new VARVE rule. If the generated
  component classes are not visible to an analyzer, stop and say so before
  substituting a test over the compiled assembly.
- **Tests.**
  - `TokensCssTests.tokens_css_is_generated_from_tokens_json`: regenerate and
    compare byte for byte.
  - `ComponentContractTests`: every case under `docs/ui/components/*/cases/`
    renders to its fragment; a component with a preview and no cases fails.
  - `ToneMappingTests.every_problem_type_and_state_word_has_a_tone`: over
    `ProblemCatalogue` and the documented state words.
  - The level rule's analyzer tests, a violating and a conforming sample per
    level.
