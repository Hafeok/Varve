# Session, 2026-11-19: the docs site takes its design from the design source

Developed with AI assistance under human review, by a cloud session that is
not the maintainer's delegate (ADR 0239). The prompts are in the
[traceability record](../traceability/2026-11-19-issue-368-docs-site-design.md).

## Pull request

| PR | Issue | Closes | Decision |
|---|---|---|---|
| [#379](https://github.com/Hafeok/Varve/pull/379) | [#368](https://github.com/Hafeok/Varve/issues/368), under #129 | #368 | ADR 0247 amended 2026-11-19, `SiteStyledFromDesignSource`, filed unaccepted |

The pull request was red on `CS0618` for the unaccepted ruling, and on
nothing else, until the maintainer's delegate accepted
`SiteStyledFromDesignSource` on 2026-11-20 (`cc235495`, ADR 0239). `main`
at `bd5eedd` was then merged in (`ed23b7eb`), cleanly.

One slip, caught by CI and fixed: an unused `using` (`IDE0005`) in
`OutputCheckTests.cs`. My local runs had turned warnings-as-errors off on
the command line, which hid it.

## What was built

- **Styles from the design source.** `eng/site` embeds no stylesheet.
  - It writes `site-tokens.css` from `docs/ui/tokens.json`: light on `:root`, dark under `prefers-color-scheme: dark`, with every token reference resolved.
  - It copies `docs/ui/bundle.css`, `docs/ui/site/site.css` and the mark.
- **The frame and the heads**, as `docs/ui/site/` draws them:
  - the bar comes from the site map;
  - page order is a section README's links;
  - the H1 moves into the head;
  - the foot names the commit and the licence the build states.
- **The ADR badge from the ledger**, in one mapping (`Ledger.StateOf`):
  - **Superseded**, from the ADR index row;
  - **Accepted**;
  - **Accepted · N pending**, with the amendment those rulings belong to named in the head;
  - **Not yet accepted**;
  - an unaccepted amendment by another ADR is a dated note, not its badge.
- **Markup:** table wrappers, heading anchors, the diagram figure with an `alt` from its node labels, `ul.vd-api`, and every code block and table a named region in the tab order.
- **Review:** `eng/site … screenshots`, uploaded by ci.yml's `docs site` job as `docs-site-review`.

## Measured

**Stylesheets a page loads**, in bytes:

| | `site-tokens.css` | `bundle.css` | `site.css` | Total |
|---|---|---|---|---|
| Before | — | — | 3,186 (embedded) | 3,186 |
| After | 2,039 | 24,541 | 16,576 | 43,156 |

The site uses about 1.3 KB of `bundle.css` (`vw-state` and its tones), about 5% (#373).

**The audit:** axe-core at WCAG 2.2 AA, in CI for the first time after the
acceptance: [run 44709366630](https://github.com/Hafeok/Varve/actions/runs/44709366630),
head `ed23b7eb`, with `axe.md` in its `docs-site-review` artifact. The
session's local runs gave the same result. "None" means no violation; the
number is the rules that passed.

| Page | Light, 1440 | Dark, 1440 | Light, 390 | Dark, 390 |
|---|---|---|---|---|
| `index.html` | none (21) | none (21) | none (21) | none (21) |
| `adr/0247-the-docs-site.html` | none (27) | none (27) | none (28) | none (28) |
| `operator/configure.html` | none (25) | none (25) | none (25) | none (25) |

**Tests:** `tests/Varve.Site.Tests` has 67 tests. All 67 pass, both locally
and in CI's `docs site` job.

**Badges on the branch:** 213 Accepted, 7 Superseded, and 1 "Accepted · 1 pending" (ADR 0247).

## What the maintainer changed after the screenshots

1. **The badge's wording:** "Accepted", "Accepted · N pending" with the amendment named in the head, and "Not yet accepted". Superseded is unchanged.
2. **`tabindex="0"` on code blocks and tables**, each with a name the audit accepts.
3. **`site-tokens.css` resolves every token reference.** The design's preview file has unresolved `{accent}`; that is noted on #368 for the design side.
4. **Tabular figures in tables and numbers only.** The design side did not answer otherwise.
5. **The licence fix in the spec's frame drawing stands.**

## Found along the way

- Markdig's alignment `style` attributes were blocked by the policy on 37 pages of the live site.
- A header-cell pattern that matched `<thead>` hid every key-value table.
- Scrolling code blocks and tables were not reachable by keyboard.
- ADR 0002's status line says Accepted, though 0031 supersedes it.

## Deferred, each its own issue

- [#369](https://github.com/Hafeok/Varve/issues/369): the learning track adds its section to the site map in its own first pull request.
- [#372](https://github.com/Hafeok/Varve/issues/372): a design reference page for the API reference.
- [#373](https://github.com/Hafeok/Varve/issues/373): extract what the site shares with the Web UI from `bundle.css`.
