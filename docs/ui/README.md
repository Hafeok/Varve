# Varve

Varve is an event-sourced RDF store. Its Web UI (`Varve.WebUi`) is a client of the public HTTP API: the operator console first, the data explorer later, one application. This system is what the Web UI implements from. Its stylesheet is plain CSS over custom properties: no component library, no CSS framework, no web fonts, no icon font, no inline styles.

## Principles

1. **Every view says where it was read.** A view shows the `Varve-Position` it was read at in a `position` stamp in its head. A dataset view pinned with `Varve-As-Of` shows the as-of banner instead, above everything else in the view. A view never mixes positions silently: when two resources were read at different positions, both stamps show.
2. **State is a mark and a word.** Every state carries its word from the API (`open`, `failed`, `follower`, `superseded`) and a mark whose shape differs per tone. Colour is the third channel, never the only one.
3. **The API's words, verbatim.** Labels, state words and problem titles are the ones the server and `docs/problems/` use. The UI does not rename `writer epoch` to "leader term" or `follower` to "replica".
4. **Dense and calm.** Operators scan tables. Rows are 37px, panels sit on hairlines, and the only shadow is an overlay's. Attention colour is reserved for something that needs attention.
5. **The record is the motif.** The varve band (pale layer, dark line) appears in exactly two places: the core mark, and the as-of banner's edge, where it means "you are reading an older layer". Nowhere else.

## Content

- **Voice.** Plain, short, present tense. Name what the operator recognises: dataset, position, commit, writer, follower, projection. A control states its effect: "Close dataset", "Take checkpoint". A result says what happened and where: "Checkpoint taken at position 18402".
- **Case.** Sentence case everywhere, including buttons and headings. Uppercase only through the `label` style, which the CSS transforms; the source text stays in sentence case for screen readers.
- **Positions** are integers in `code`, with no thousands separator, so a copied position pastes into the CLI or a header unchanged. Selecting a stamp selects the whole number.
- **Times** are ISO 8601 UTC in `code` (`2026-10-27T08:41:07Z`), with a relative time as a `caption` beside it ("2 min ago"). Never a relative time alone.
- **IRIs** are shown whole in `code`, wrapping anywhere. Where a column is too narrow, truncate in the middle and keep the full IRI in the cell's accessible name and a copy button.
- **Errors** are problems: render the problem panel with the server's `title`, never a paraphrase, and the request id. No apologies, no "something went wrong".
- **No emoji.** No exclamation marks.

## Levels

Components are organised by atomic level, and the levels are a dependency rule: a component uses only levels below it. This is the stable dependency principle applied to the interface. Tokens and atoms have the most dependents and change least; pages depend on everything and change with the API.

| Level | Components | Uses |
|---|---|---|
| Tokens | `tokens.json` | nothing |
| Atoms | `Button`, `Field`, `StateBadge`, `PositionStamp`, `TermChip` | tokens |
| Molecules | `KeyValue`, `LagMeter`, `Notice`, `ViewHead`, `Triple`, `AsOfBanner`, `CopyButton` | atoms |
| Organisms | `ResourceTable`, `CommitList`, `ProblemPanel`, `SourceBlock`, `ConfirmDialog`, `Panel` | molecules, atoms |
| Templates | `Shell` and the screen layouts | organisms and below; no data |
| Parts | domain-aware pieces that are not routed: the problem view, the read stamps, the drift note | templates and below; the client's model types and the tone mapping; Varve's wording |
| Pages | the routed screens | everything; the only level that calls the API |

- **Lower levels are abstract.** Atoms, molecules, organisms and templates know nothing of Varve's domain. An atom takes words, tones and values as parameters. `StateBadge` takes a tone and a word; it does not know what `failed` means.
- **Domain knowledge sits in parts.** The mapping from an API state or problem to a tone (the table under Colour) is one tested mapping that parts and pages call, never a lookup repeated per page or pushed down into an atom. Domain text is the same: the drift note's wording lives once, in a part, which a page passes into the dialog's slot; no molecule carries it.
- **Never the same level.** A component uses only lower levels, never one of its own: parts don't use parts, and pages don't use pages. Where an organism shows what another organism renders (a problem in a dialog, a problem in a table's place), it takes a slot the page fills.
- **Only pages read data.** Pages are the routed screens and the only level that calls the API. Everything below a page takes parameters, so every component renders from a fixture without a server.
- **A change to a lower level is a change to every screen.** Change tokens and atoms deliberately; a page may change freely.

## Colour

Two themes, light first. Every text token's note names the grounds it reads on; all pairs meet 4.5:1 in both themes, `control-border` and `focus-ring` meet 3:1.

- Page: `ground`. Resources render on `surface`. Header rows, source blocks and the navigation rail use `surface-sunk`. Hairlines are `rule`; control outlines are `control-border`.
- Text: `ink`; secondary `ink-muted`. Links and the primary action: `accent`, with `on-accent` on an accent fill.
- The log's own colour is `layer` with `layer-ink`: position stamps, the selected row, a new commit in the live tail.
- State tones, each a word, a mark and a colour:

| Tone | Tokens | Mark | Words from the API |
|---|---|---|---|
| ok | `ok`, `ok-soft` | filled disc | `open`, `ready`, `writer`, `applied`, within lag |
| warn | `warn`, `warn-soft` | triangle | `rebuilding`, behind its lag bound, `not-ready` while starting, the drift note, anonymous mode |
| critical | `critical`, `critical-soft` | square | `failed`, `superseded`, lease lost, `mismatch`, `not-ready` |
| neutral | `surface-sunk`, `ink-muted` | hollow ring | `closed`, `follower`, `elsewhere` |
| past | `past`, `past-soft` | two bands | a view pinned as of a position |

`ok` and `critical` differ in hue more than lightness; that is accepted because neither ever appears without its mark and word. Do not introduce a tone outside this table: a new state maps onto one of the five.

## Terms, provenance and change

These encodings hold everywhere a term or a statement appears: results, deltas, diffs, declarations and the graph view.

- **Term kind is form first.** An IRI is `term-iri` text with no frame; a literal is quoted on `surface-sunk` with its datatype or language as a caption; a blank node has a dashed `control-border` outline; a triple term is a frame in `edge` written `<<( … )>>`. Use `TermChip`; never print a term as bare text.
- **Inferred is dashed and named.** An entailed statement draws its predicate or edge dashed in `edge-inferred` and carries the word "inferred", with the rule or binding when the API gives one. Asserted statements are solid in `edge`. Never show inference by paleness alone.
- **Change has its own tokens.** Assertions are `added` / `added-soft` with a `+`; retractions are `removed` / `removed-soft` with a `−` and a strikethrough. These are not states: never use `ok` or `critical` for a delta, because a retraction is not a failure.

## The graph view

Part 2 (the data explorer) will have a graph view. Its scope is fixed now so the rest of the system can serve it:

- **A neighbourhood, not the dataset.** The view starts from one resource and shows its edges; the operator expands a node on demand. It has a stated node budget, and when a neighbourhood is over budget it says how many nodes were left out and offers the table instead.
- **The table is primary.** Every graph view has a table of the same statements (`Triple` rows) one control away; the graph is a lens on it, never the only way to read the data.
- **Time is the distinctive part.** The same neighbourhood can be shown as of a position (with the as-of banner) or as a diff between two positions, with added and removed edges overlaid in the change tokens.
- **Nodes are `TermChip`s, edges are `edge` or `edge-inferred`.** No new colours for node types; `rdf:type` is shown as a caption on the node, not as a fill.
- How it is rendered (SVG through Razor or a canvas through interop) and the layout algorithm are part 2's ADR, measured against the size budget and the CSP.

## Type

System faces only (`sans`, `mono`): no font files, so nothing is added to the bundle budget or the CSP. Numbers use tabular figures throughout (`.vw-root` sets them).

These are the style names of `tokens.json`, the design's vocabulary, not classes: `tokens.css` holds custom properties only, and `bundle.css` applies the styles through its `vw-` component classes.

- `page-title` once per screen, the resource's name. `section-title` for panel heads.
- `body` for everything running; `body-strong` for a row's identifying cell.
- `caption` for secondary lines and relative times; `label` for field labels and table headers.
- `metric` for the one number a panel is about (head position, lag). `code` for positions, IRIs, ids, Turtle, TriG and problem types; `code-small` inside dense cells.

## Space, radius, elevation

- A 4px grid: `space-1` to `space-8`. Panels pad `space-4`; panels stack with `space-6`; table cells pad `space-2` by `space-3`.
- Corners are nearly square: `radius-sm` on controls, badges and stamps, `radius-md` on panels and dialogs. `radius-round` only for the live-tail dot.
- Elevation: none on the page. `shadow-overlay` only for a dialog or menu.

## States every screen has

A screen renders all of these, from the same components:

- **Loading:** the table's loading row; never a spinner over a blank page.
- **Empty:** the table's empty row, saying what would appear and how it gets there ("No datasets are declared. Declare one in TriG and run `varve apply`.").
- **Forbidden (`403`)** and every other problem: the problem panel in place of the view's body, with the view head still showing what was requested.
- **Signed out (`401`):** sign-in starts; no screen renders a `401`.
- **Server unreachable:** a warn notice directly below the view head, above everything else, with the server address, the one time of the last successful read, and "Read again"; the last data stays visible below it, marked with its position, and its panels are stale (`vw-panel--stale`), each saying "As last read at" that same time.
- **Conflict on a write (`409`, `412`):** the problem panel inside the dialog that made the write, with `expectedPosition` and `headPosition` from its members, and the action to reload at the new position.

## Iconography

There is no icon set. State marks are CSS shapes drawn from tokens (`state`, `notice`, `tail`). The one image is the core mark (`docs/ui/assets/varve-core.svg`), the same file as the repository's package icon, used at 24px in the shell bar. Do not add icons for navigation or actions: words do that work.

## Accessibility

WCAG 2.2 AA. Every focusable element shows `focus-ring` as a 2px solid outline offset 2px. Lag is a native `<meter>` with `low`, `high` and `optimum` set, so its state reaches assistive technology. Dialogs are native `<dialog>` opened modal. Tables keep real `<th scope>` headers, and every table has a name: a caption, or the panel title through `aria-labelledby`. The shell's first focusable element is a skip link to the main column (2.4.1), and its navigation is named "Main". A region that scrolls inside itself takes focus and has a name. In forced-colors mode the state marks render in `CanvasText`, and the word carries the state.

## Implementing in `Varve.WebUi`

- `tokens.css` (generated from `tokens.json`) and `docs/ui/bundle.css` are the two stylesheets, served from the app's own origin, so the CSP needs `style-src 'self'` only.
- Wrap the app in `.vw-root`. Components are Razor components emitting the markup in each component's preview; class names are the contract.
- Do not use Blazor CSS isolation (`.razor.css`): it rewrites selectors with generated attributes and splits the one shared stylesheet. Styles are global and class-based, namespaced by the `vw-` prefix.
- Do not set `style` attributes. Widths that depend on data use `<meter>` or classes, never computed inline styles.
- Theme: the root element always carries `data-theme`. The app sets it at boot from `prefers-color-scheme`, or from the viewer's explicit choice when one is stored, and follows changes to the media query while no choice is stored.
