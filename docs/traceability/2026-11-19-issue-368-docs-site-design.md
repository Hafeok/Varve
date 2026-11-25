# The docs site styled from the design source, with ledger-read ADR badges

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#368](https://github.com/Hafeok/Varve/issues/368), under [#129](https://github.com/Hafeok/Varve/issues/129)'s docs-site deliverable |
| **Pull request** | [#379](https://github.com/Hafeok/Varve/pull/379) |
| **Date** | 2026-11-19 |
| **Tool** | Claude Code, a cloud session started from the desktop app; not the maintainer's delegate (ADR 0239) |
| **Model** | `claude-opus-5-5`, configured and last served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRkCLYi9UL4eh3NMHx` |
| **Branch** | `claude/docs-site-design-129`, from `main` at c754bcb |
| **Decision** | ADR 0247 amended 2026-11-19, ruling `SiteStyledFromDesignSource`, filed unaccepted in `docs-site`; no new ADR number |

## The prompts

The session prompt, with `docs-site-design.zip` attached:

> Session prompt: the docs site takes its design from the design source (#129, the docs site deliverable)
> Paste into a fresh session on `Hafeok/varve`, with `docs-site-design.zip` attached. Read `AGENTS.md` first and follow it in full. Then read ADR 0247 (the docs site), ADR 0206 (`docs/ui/` is the design source), `eng/site/`, and #317 and #324, which built the site.
> What this is
> The docs site is live at https://hafeok.github.io/Varve/ and renders correctly, with a stylesheet of its own embedded in the generator. The maintainer's design session has produced a design for it from the same design system the Web UI uses. This session builds it.
> Unzip `docs-site-design.zip` at the repository root. It adds `docs/ui/site/`:
>
> * `README.md`, the spec. Where this prompt and the spec differ, the spec wins on how a page looks; this prompt wins on gates and on how the work lands. Ruling 4 below replaces the spec's ADR status mapping.
> * `site.css`, the stylesheet.
> * `reference/`, pages rendered from the repository's Markdown at `c754bcb`. Their markup and classes are the contract; their text and exact heading ids are not.
>
> If the zip is not attached, stop and say so. Do not rebuild the design from this prompt.
> Open one issue for the work, linked from #129. One pull request.
> What to build
> 1. Stylesheets come from the design source
> `eng/site` stops embedding its own `site.css`. At build it:
>
> * writes `site-tokens.css` from `docs/ui/tokens.json`, with the light values on `:root` and the dark values under `@media (prefers-color-scheme: dark)`. The site has no script to set `data-theme`, so the colour scheme follows the reader's system;
> * copies `docs/ui/bundle.css` and `docs/ui/site/site.css` verbatim.
>
> This amends ADR 0247. File the amendment unaccepted, with a ruling such as `SiteStyledFromDesignSource`. A test holds `site-tokens.css` to `tokens.json`.
> 2. The frame
> `Layout.Page` writes the frame as the spec gives it:
>
> * a skip link, then the bar with the five sections and `aria-current` on the current one;
>    * from 861px wide the bar is one row and sticky;
>    * at 860px and below it is two rows and scrolls away with the page: the brand, then the five sections, wrapped. Every section stays visible down to 320px, with no menu button and no script. This is in `site.css`; nothing in the generator changes for it;
> * in the document, the article comes first, then the page list, then "On this page";
> * previous and next links, and the foot with the commit and the source page.
>
> A section's page order is the order of its README's links, then any remaining pages by name.
> 3. Page heads
>
> * An ADR shows its number above the title, a status badge and a link to its decision set.
> * A problem type shows "Problem type" and its HTTP status above the title, its name in mono, and its Title row as the lede.
> * The H1 moves into the head and is not repeated in the body.
>
> The ADR index splits each status cell into a badge and a Notes cell.
> 4. The ADR badge is read from the ledger, not from the status line
> This is a ruling added to the design, and it is closed.
>
> * Every ADR's status line on `main` reads "Accepted — filed unaccepted by …", because the release check requires that form. It reads the same before and after the maintainer accepts. So the status line cannot tell an accepted ADR from one that is not.
> * Acceptance is in the decision set: a ruling is accepted when its key carries `accepted-by`. The badge is computed from the ADR's decision set, and from the sets its dated amendments name:
>    * every ruling accepted: Accepted (tone ok);
>    * any ruling unaccepted: Not yet accepted (tone warn), with the count;
>    * superseded, as the set's note records it: Superseded (tone neutral).
> * An ADR with no decision set is shown by its status line, with a plain "no decision set" note. List these in the plan; do not guess their state.
> * The mapping lives in one place, with a test for each case, including an accepted ADR with an unaccepted amendment.
>
> The reason is the maintainer's ruling of 2026-11-19: a reader must never expect one thing and get another. A badge that says "Accepted" over rulings nobody accepted would do exactly that.
> 5. Markup
>
> * Tables are wrapped in `div.vd-table`. A table whose header cells are all empty gets `vd-table--kv`.
> * Every `h2` and `h3` gets an `a.vd-anchor` link.
> * Diagrams are `figure.vd-diagram`, with an `alt` built from the diagram's node labels.
> * The API reference pages use the same frame and `ul.vd-api`.
>
> Answered before you start
> These were open; the maintainer has answered them. Do not ask them again.
>
> 1. The preview script does not come into the repository. It is the design side's tool and stays there, so `eng/` stays C# only. `docs/ui/site/reference/` belongs to the design, not to the build: when the design changes, the design side replaces those pages. The contract test (under Gates) is how they stay true once `eng/site` is the only renderer.
> 2. No fonts, no icons, nothing new to licence. The faces are system faces only (`--font-sans`, `--font-mono`). The one image is `docs/ui/assets/varve-core.svg`, the repository's own work under its licence. `bundle.css` and `tokens.json` are the repository's. Mermaid is already in the register (ADR 0247). Nothing enters the dependency register.
> 3. The foot's commit does not break reproducibility. The commit is an input to the build, so one commit gives one set of bytes. The site writes no build time and no date anywhere. Add a test that two builds from one commit are byte-identical. In the plan, still say which commit the foot names on a pull-request build, on `main`, and in a local build.
> 4. The bar at phone width is two rows, as in section 2.
>
> Gates
> Keep every existing gate, unchanged:
>
> * no script on any page, and the site's Content Security Policy as ADR 0247 states it;
> * nothing from a third-party origin: every font, icon and image is served from the site;
> * the link and anchor check on the built output;
> * `every_stylesheet_balances_its_braces` (#357) now covers `docs/ui/site/site.css` too.
>
> Add:
>
> * An accessibility audit with axe on three built pages (home, an ADR, an operator page), in both colour schemes, at 1440 and 390 wide. WCAG 2.2 AA, as for the Web UI.
>    * axe is injected by the test's browser, and the pages' policy blocks injected script. So only the browser context that runs axe gets Playwright's `bypassCSP: true`, and the test says why. The no-script and no-third-party-origin gates keep reading the built files exactly as they were written to disk; they are not weakened.
>    * Use the browser and the axe dependency the Web UI's tests already have. No new package without a register entry and an ADR.
> * A contract test that checks structure only. Build one page of each kind from the current Markdown: home, a section index, an ADR, a problem type, an operator page and an API reference page. Compare each with its page under `docs/ui/site/reference/` on:
>    * the bar, the frame's areas and their order in the document;
>    * the page list's items and `aria-current`;
>    * the head's eyebrow, title, meta and lede;
>    * the table wrappers, the anchors, the diagram figure, the pager and the foot.
> Ignore heading ids and all text: the reference pages are pinned to `c754bcb`, and the docs move on.
> * A reproducibility test: two builds from one commit are byte-identical.
> * The size of what a page loads: report the bytes of the three stylesheets, before and after. `bundle.css` is the Web UI's whole bundle; say how much of it the site uses. If it is a small part, say so on the issue as a finding. Do not split the bundle here.
>
> Plan questions
> Post the plan on the issue and wait for the maintainer's answer before building.
>
> 1. ADRs with no decision set, and what the badge shows for each (ruling 4).
> 2. The diagram `alt`: how node labels are read from the Mermaid source, and what a diagram with no labels gets.
> 3. What changes for pages other sessions are writing now: the operator guide (#345 and its later parts) and the learning track (#94). Say whether any Markdown they write must change.
> 4. The ADR number for the amendment's ruling, after checking `main` and every open pull request.
> 5. The foot's commit on a pull-request build, on `main`, and in a local build (answered point 3).
>
> Review before the pull request
> Before opening the pull request, put the built site in front of the maintainer:
>
> * screenshots in light and dark, at 1440 and 390 wide, of the home page, an ADR (one accepted, one not), a problem type, an operator page with a table and a diagram, and an API reference page;
> * upload them as a workflow artifact with the built site, and link the run on the issue, with the list of what each shows. Do not commit screenshots to the repository.
>
> Open the pull request only after he has answered.
> Not in this work
>
> * Search, a version switcher, or anything else that needs script. If the spec asks for one, say so on the issue and leave it out.
> * Any change to the Web UI's components, `tokens.json` or `bundle.css`. If the site needs a token the design source lacks, open an issue.
> * The content of any page.
>
> How this lands
>
> * Issues are the work log. Comment on #129 when you start, with the new issue's number. Decisions, questions and blockers go on the issue when they arise. Work you find and don't do becomes its own issue at once.
> * You are a cloud session, not the maintainer's delegate (ADR 0239). File every decision unaccepted. Never write `accepted-by` on a decision set, and never land.
> * Never write the approval line, for any hash, anywhere, including in a report. Report a head as "head `<sha>`", with its hosted result after reading every failed job.
> * The description carries `Completes: #N` for the issue you open, with `Refs #129`. The completing commit carries `Closes #N`. No closing keyword in a description. No pull request closes #129.
> * The amendment's status follows ADR 0247's form, and its ruling is cited from the generator code that implements it. The pull request is then red on `CS0618` until the maintainer accepts; say on it whether that is the only failure.
> * Never edit a release descriptor that `main` holds (#358).
> * Commits: as `Claude (Emil) <claude+emil@okkels-klein.dk>`, signed with the agent key. Never `git commit -s`. The co-author trailer is exactly `Co-Authored-By: Claude <noreply@anthropic.com>`; no commit message names a model.
> * Branch: `claude/docs-site-design-129`. If the push is refused, say so on the issue with the exact refusal and do not try another route.
> * CI images only from `.github/ci-images.yaml`. No Docker Hub pulls.
> * When `main` moves, merge it in. Never rebase a pushed branch, and never force-push.
> * No suppression or downgrade of DD or VARVE rules. Never weaken a gate to make a test pass.
>
> Close-out
> `docs/sessions/2026-11-<name>.md` and a `docs/traceability/` record: the pull request, the issue, the ruling's number, the stylesheet sizes before and after, the audit's result per page and scheme, what the maintainer changed after the screenshots, and anything deferred as a linked issue.

The maintainer's answers to the plan:

> For the docs site design session.
>
> The plan on #368 is accepted. Answers:
>
> 1. Ruling 4, as corrected:
>    - Superseded is read from the ADR index row. The seven ADRs with no decision set show Superseded from it; 0002 shows Superseded although its status line says Accepted.
>    - An ADR's badge reflects the rulings in its own decision set only. An unaccepted ruling that amends another ADR shows on the amended ADR as a note with the amendment's date, not as its badge.
>    - Add a test with a fixture ledger for each case, since main has no "Not yet accepted" case. The screenshots show that badge from the pull-request build of ADR 0247.
> 2. No new ADR number: a dated amendment to ADR 0247, SiteStyledFromDesignSource filed unaccepted in the docs-site set.
> 3. Cite it from tests/Varve.Site.Tests. Widen the axe package's register scope to that project under the same amendment.
> 4. The foot's licence is read from the repository (the LICENSE file or the SPDX expression the build already has), never typed. Correct "Apache-2.0" in the pages under docs/ui/site/reference/ in the same pull request, and say so in the description.
> 5. The API reference page: the contract test checks its frame and ul.vd-api only. Open an issue for the missing design page.
> 6. The size is accepted: copy bundle.css verbatim. Open an issue to extract what the site shares with the Web UI, with your 5% finding.
> 7. #369 is the learning track's: its first pull request adds the learn section to SiteMap. Do not build it. Make the frame take its sections from the site map, so a sixth section needs no change of yours, and say on #94 that you both change eng/site.
>
> Build, then put the screenshots and the built site up as a workflow artifact and link the run on #368. Open the pull request only after the maintainer has seen them.

The maintainer's answer to the review:

> For the docs site design session.
>
> The screenshots are reviewed by the architect; the maintainer looks at the artifact himself. Open the pull request with these changes:
>
> 1. Badge wording. Three states, one place, with tests:
>    - every ruling accepted: "Accepted" (ok);
>    - some accepted and some not: "Accepted · N pending" (warn), where N counts the unaccepted rulings; say in the head which amendment they belong to when they have a date;
>    - none accepted: "Not yet accepted" (warn).
>    Superseded stays as it is.
> 2. tabindex="0" on code blocks and tables: accepted. Give each an accessible name the audit accepts.
> 3. site-tokens.css: the generator resolves every token reference. The test fails when the output holds an unresolved "{…}". Note on #368 that the design's preview file has unresolved {accent} references, for the design side to fix.
> 4. tabular-nums: apply it to tables and numeric cells only, unless the design side answers otherwise on #368 before you push.
> 5. The licence fix in the spec's drawing stands.
>
> After the maintainer's delegate accepts SiteStyledFromDesignSource, the audit runs in CI for the first time: report its result per page, scheme and width, and attach axe.md.

## The report

The session's own summary is [docs/sessions/2026-11-docs-site-design.md](../sessions/2026-11-docs-site-design.md). In brief:

- **What was built.**
  - **Styles:** `eng/site` writes the design's frame and heads, and takes its stylesheets from `docs/ui/`: `site-tokens.css` generated with every reference resolved, then `bundle.css` and `site/site.css` copied.
  - **Badges:** an ADR's badge is read from the ledger, in one mapping, with a fixture ledger case for each state.
  - **Tests:** `tests/Varve.Site.Tests` holds the site to its design, its ledger and its policy, and runs the audit.
- **The ruling.** No new number. ADR 0247 is amended 2026-11-19, with `SiteStyledFromDesignSource` filed unaccepted and cited from `SiteTokensTests`.
- **Stylesheet sizes:** 3,186 bytes before (one stylesheet); 43,156 after (`site-tokens.css` 2,039, `bundle.css` 24,541, `site.css` 16,576). About 1.3 KB of `bundle.css` is used (#373).
- **The audit**, axe-core at WCAG 2.2 AA: no violation on the home page, ADR 0247 or `operator/configure`, in light and dark, at 1440 and 390, in all 12 runs. That held locally, and in CI for the first time after the acceptance ([run 44709366630](https://github.com/Hafeok/Varve/actions/runs/44709366630), head `ed23b7eb`, `axe.md` in its `docs-site-review` artifact). Its result per page, scheme and width is on #379 and in the close-out.
- **The acceptance:** `SiteStyledFromDesignSource` was accepted on 2026-11-20 by the maintainer's delegate at his word (`cc235495`, ADR 0239). The session then merged in `main` at `bd5eedd`.
- **What the maintainer changed after the screenshots:** the badge's three accepted states with the pending amendment in the head; a name for every scrolling region; token references resolved; tabular figures in tables and numbers only. The licence fix in the spec's drawing stands.
- **Deferred:** #369 (the learning track's section), #372 (an API reference design page), #373 (the shared part of `bundle.css`).
