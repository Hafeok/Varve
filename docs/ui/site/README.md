# The docs site's design

The docs site is styled from the design source, like the Web UI. It is documentation, not the console, so it reads at a larger size, but its colours, faces, marks and rules all come from `docs/ui/`. This page specifies the design. `site.css` is the stylesheet. The `reference/` pages are rendered from the repository's real Markdown and show what `eng/site` should write.

## What the site loads

All three stylesheets are served from the site's own origin. The CSP (`style-src 'self'`, no script) stays as ADR 0247 set it.

1. **`site-tokens.css`**, which `eng/site` generates from `docs/ui/tokens.json` at build.
   - Every colour token is set on `:root` with its light value. The dark values sit inside `@media (prefers-color-scheme: dark)`.
   - The site has no script, so it can't set `data-theme` the way the Web UI does. The reader's system preference decides the theme.
   - The spacing, radius and font tokens are copied from the `:root` block of `tokens.css`.
2. **`docs/ui/bundle.css`**, verbatim. The site uses its atoms, so the docs and the console draw a state the same way (`vw-state` and its tones).
3. **`site.css`**, this design. Its classes are prefixed `vd-`, and it never restyles a `vw-` class.

`eng/site` reads all three from `docs/ui/` at build and no longer embeds a stylesheet of its own. A change to the design system therefore shows up on the site at the next build, and a test can't be fooled by a stale copy.

## Frame

```
+-----------------------------------------------------------------------+
| [mark] Varve | Documentation   Operator guide  Specifications  ...  GitHub |  bar: 52px, sticky
+--------------+----------------------------------------+---------------+
| SECTION      |  Documentation / Section                | ON THIS PAGE  |
| Overview     |  EYEBROW                                | h2            |
| Page         |  Title                                  | h2            |
| >Current<    |  meta: status badge, decision set       |   h3          |
| Page         |  ------------------------------------  |               |
|              |  prose, 46rem                           |               |
|  ground      |  surface                                |  surface      |
+--------------+----------------------------------------+---------------+
| Built from <commit>. This page is docs/....            Apache-2.0      |
+-----------------------------------------------------------------------+
```

- **Bar.**
  - On the left: the core mark at 24px, "Varve", a hairline, then "Documentation".
  - Then the five sections a reader starts from: Operator guide, Specifications, Decisions, Problems, API. The current section is marked with `aria-current` and a 2px `accent` underline.
  - On the right: the GitHub link.
  - Decision sets, Rules, Formats and Vocabulary are reached from the home page and from links in the text, not from the bar.
- **Page list** (`nav.vd-pages`): the current section's pages, on `ground`. The current page is drawn with `layer` and `layer-ink`, the selected-row treatment from the console.
  - **Order:** the order of the links in the section's `README.md`, then any page the README doesn't link, in name order.
  - **ADR labels:** the number in `mono` before the title.
  - **Identifier pages** (problems): the whole label in `mono`.
- **Article:** at most 46rem wide (about 75 characters) on `surface`, so the page reads like a panel on its ground.
- **On this page** (`aside.vd-toc`): the page's h2 and h3 headings. It is written only when a page has two or more.
- **Previous and next** at the end of each article, in the page list's order.
- **Foot:** the commit the site was built from, and the source page, both as GitHub links.

**Order in the document:** the bar first, then the article, then the page list, then the contents. Grid areas place them on screen. A screen reader, and a phone, reach the content before a list of 222 ADRs.

## Narrow screens

- **Below 1240px:** the contents column is dropped.
- **Below 860px:**
  - one column;
  - the page list moves below the article, and a "Pages in this section" link in the breadcrumbs jumps to it;
  - the bar becomes two rows that scroll away with the page: the brand, then the five sections, wrapped. Every section stays visible down to 320px, with no menu button.
- There is no menu button and no script.

## Page heads

| Page | Eyebrow | Title | Meta |
|---|---|---|---|
| Section index | none | the section's name | its summary from the site map, as the lede |
| Article | none | its H1 | none |
| ADR | `ADR 0247` | the H1 with the number removed | **Status** badge, **Decision set** link |
| Problem type | `Problem type` and `HTTP 422` | the type's name in `mono` | its **Title** row as the lede |

The H1 moves into the head and is not repeated in the body.

**An ADR's status badge** is read from the first words of its `## Status` paragraph. The tone follows the design system's mapping, kept in one place in `eng/site`:

| Status words | Tone | Word shown |
|---|---|---|
| Accepted | ok | Accepted |
| Accepted, but filed unaccepted and not yet accepted | warn | Filed unaccepted |
| Superseded | neutral | Superseded |

A superseded ADR is historical, not a failure, so it takes `neutral` and not `critical`. Its Status paragraph still says what superseded it.

**The ADR index:** each status cell is split into the badge and a **Notes** cell. The Notes cell keeps the README's own text, such as "amended by 0068" or "by 0031".

## Prose

- **Body:** sans, 16px on 26px. The console's 14px body is for scanning tables, not for reading.
- **Headings:**
  - h2 22px/28 semibold, with 48px above;
  - h3 17px/24.
  - No rules under headings: space separates sections.
  - Each h2 and h3 gets an anchor link (`a.vd-anchor`), shown on hover and on focus. The link gate already checks the identifiers it links to.
- **Inline code:** mono at 0.8125em on `surface-sunk`, with `line-height: 1`, so it doesn't push the lines apart.
- **Code blocks:** mono 13px on 20px, on `surface-sunk` with a `rule` border. They scroll sideways and never wrap.
- **Tables:** in a `div.vd-table` that scrolls.
  - The header row is `surface-sunk` with uppercase labels; rows are separated by hairlines; there are no vertical lines. This is the console's table.
  - A table whose header cells are all empty (the problem pages' key-value table) gets `vd-table--kv`: no header row, and the key column muted.
- **Blockquotes:** on `layer` with `layer-ink`.
- **Diagrams:** keep ADR 0247's white card in both themes, since Mermaid's neutral theme is drawn for a light ground. The card is framed with `rule` and scrolls if the drawing is wider than the column.
- **Links:** `accent`, underlined.

## Home

- **The title "Varve documentation"** and one sentence on what Varve is.
- **Three ways in:**
  - "Run a server" as the one primary button;
  - "Operator guide" and "Architecture decisions" as secondary buttons.
- **Reference:** a card for each part, with its name, its summary from the site map, and a count of its pages ("221 decisions", "58 problem types").
- **Learning track:** a warn notice saying it isn't written yet, until #94 lands.
- **No hero image.** The core mark appears in the bar only.
- **The varve band** stays where the design system allows it: the mark itself, and nowhere else on the site.

## Accessibility

- A skip link to `#main`.
- Every link has a visible focus ring (`focus-ring`, 2px, offset 2px).
- `aria-current="page"` marks the current section in the bar and the current page in the list.
- The page list and the contents are labelled landmarks.
- In forced colours, the current-page marks use `Highlight`.
- Every diagram's `img` gets an `alt` built from its node labels. It's not a description, but it's better than an empty one.

## What `eng/site` changes

- **`Layout.Page`** takes the page's section, the section's pages in order with their titles, the head (eyebrow, title, meta), and the previous and next pages.
- **The contents column** is built from the rendered headings, after Markdig's GitHub-compatible identifiers are assigned.
- **The section's page order** comes from its README's links.
- **The ADR status mapping and the problem-type head** each live in one place, with tests.
- **`site.css`** is no longer an embedded resource. The build copies `docs/ui/site/site.css` and `docs/ui/bundle.css`, and writes `site-tokens.css` from `tokens.json`.
- **The API reference pages** use the same frame. Their member lists become `ul.vd-api`.
- **The existing output check** still holds: no script, no third-party origin, every link whole.

The reference pages are rendered by a Python preview, not by `eng/site`, so their heading identifiers can differ slightly from Markdig's. The markup and classes are the contract; the generator's exact ids are not.
