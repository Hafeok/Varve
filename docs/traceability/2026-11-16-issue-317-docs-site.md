# The docs site: a generator in eng/ on Markdig, Mermaid drawn at build time, no script

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#317](https://github.com/Hafeok/Varve/issues/317), the docs-site deliverable of [#129](https://github.com/Hafeok/Varve/issues/129); found: [#323](https://github.com/Hafeok/Varve/issues/323) |
| **Date** | 2026-11-16 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS9TK9P9VYitD1bP8Z` |
| **Branch** | from `main` at 48a1fd5 |
| **ADRs** | [0247](../adr/0247-the-docs-site.md), filed unaccepted; [0224](../adr/0224-supply-chain-transparency.md) amended 2026-11-16, unaccepted; [0114](../adr/0114-resource-governance.md) noted 2026-11-16 (no ruling) |

## The prompt

The session prompt, `varve-claude-code-operator-guide-and-docs-site.md`, step 1:

> One pull request, under an issue you open and link from #129.
>
> - **An ADR chooses the generator,** under constraint 4 (every third-party package needs an ADR and a register entry) and ADR 0224 (locked restore, the dependency gate). Candidates from #129: DocFX, a small generator in `eng/`, or a static generator that runs only in CI. Compare them on: what enters the dependency register, whether the build is reproducible, and how Mermaid renders without a script from a third-party origin. State the choice and the reasons.
> - **Published to GitHub Pages from `main` through OIDC.** No token stored. Declare the Pages environment in `.github/repo-standard.yaml`, restricted to `main`.
> - **Mermaid renders on the site.** Prefer rendering at build time to shipping a script.
> - **The site carries** the ADRs, the operator guide, the learning track as far as it exists (#94), the `varve:` vocabulary, and the problem types.
> - **A gate** checks every internal link and fails on a broken anchor. It runs in `eng/ci.cs`.
> - **Each package's API reference is generated from its public API baseline** (`PublicAPI.Shipped.txt` and `Unshipped.txt`), so it can't drift.
> - The site's own Content Security Policy is stated, and the pages load nothing from a third-party origin.

The maintainer's answers of 2026-11-16 to the plan's questions, the part that bears on this step (recorded on [#129](https://github.com/Hafeok/Varve/issues/129#issuecomment-6917200543)):

> Your ADR is 0247, not 0246: the #132 session's branch already has an ADR 0246. Check again when you file it.
>
> 1. Markdig in `eng/site/`.
> 2. (a), narrowly: fetch only the Mermaid package's own archive by exact version and SHA-512, with no npm install and no dependency tree. One register entry, build scope, with its licence. Check each generated SVG contains no script and no reference to another origin.
> 3. Don't edit ADR 0114. Add a dated note with the right target, and exempt that one link in the gate by name, citing the note.
> 4. Include `docs/spec/` and `docs/rules/`.
>
> […] proceed: #298 first, then step 1.

## The report

- **ADR 0247**, filed unaccepted, with nine rulings in `docs/decisions/docs-site.md`. It compares the three candidates on the register, reproducibility and Mermaid, and chooses `eng/site/` on Markdig.
  - The number was checked against every branch at filing: 0244–0246 are taken.
  - It amends ADR 0224 point 1 by a dated block: the register holds `BuildArchive` entries.
- **The register.**
  - `Markdig` 1.6.0 (BSD-2-Clause; no dependency on `net10.0`), scoped to `eng/site/`.
  - `Microsoft.Playwright`'s scope widens to `eng/site/`, under ADR 0204's admission and ADR 0247's use.
  - One `BuildArchive` for `mermaid` 12.3.0 (MIT): npm's archive URL, and npm's published SHA-512, which the session checked against the downloaded bytes.
  - Versions were resolved from nuget.org and the npm registry on 2026-11-16, not taken from memory.
  - `eng/dependency-register.cs` checks a `BuildArchive`'s ADR, exact version, URL, integrity, licence and scope. Three new fixtures each fail for their own reason, and `register-fixtures` passes 13 of 13.
- **Mermaid's bundle is self-contained.** Before any code was written, `dist/mermaid.min.js` alone drew a flowchart in headless Chromium with every request refused, and none was attempted. Its 22 declared dependencies are compiled into it. With HTML labels off, the SVG has no `foreignObject` and no URI but the SVG namespace.
- **Why each diagram is a file, not inline.** Mermaid's SVG carries its own `<style>`. Inline, it would need `'unsafe-inline'` for styles. As a file shown by `<img>`, it runs no script and loads nothing, so the page policy is `default-src 'none'; style-src 'self'; img-src 'self'; base-uri 'none'; form-action 'none'`.
- **The link gate, measured on this branch:** 540 pages, 1,207 relative links, 0 broken, 1 exempt (ADR 0114's link to 0095).
  - Markdig's GitHub heading identifiers agreed with every anchor written for GitHub. The only broken links were the two the audit had found.
  - `docs/problems/README.md`'s link to `limits.md` is corrected with exactly #267's line, so the two merge cleanly.
  - ADR 0114 gets a dated note giving the right target, and its text is not edited.
- **Reproducibility:** two builds of the same commit, 573 files each, were byte-identical (`diff -r`).
- **The output check.** The build checks what it wrote: no script in any page, and every local link and anchor of the built site resolves. A fixture page holding a `<script>` is refused, and is refused for that alone: the session found its first version also failing on navigation to sections the fixture lacked, and made the navigation name only what the site carries.
- **The SVG check.** It refuses `script`, `foreignObject`, `iframe`, `embed` and `object`, event handlers, references other than to its own fragments, `@import`, and absolute URIs other than namespace names. Five failing fixtures each fail alone. Mermaid's own output and an own-fragment SVG pass.
- **The API reference.** The first build put generic types and `~`-marked lines (oblivious nullability) into "(other)". Both were fixed, and every line of all 28 packages now sits under a type.
- **How it looks.** Screenshots in Chromium of an ADR with its diagram, the configure page at 390 px in the dark theme, the home page and an API page: no horizontal scroll at phone width, and no console message, so no policy violation.
- **CI:**
  - `site-links`, `site-links-fixtures`, `site-svg-fixtures` and `site-script-fixture` in `eng/ci.cs`;
  - ci.yml's `docs site` builds twice on the runner's Chrome and compares;
  - `docs-site.yml` deploys from `main` by OIDC into `github-pages`, which is declared in `.github/repo-standard.yaml` and restricted to `main`.
- **Found and filed:** #323. repo-standard can't declare the Pages source. Until it can, the maintainer sets it to "GitHub Actions" once, or the first deployment fails.
- **Run locally:**
  - `register`, `register-fixtures`, `register-packages`, `restore`, `decision-sets`, `lockfiles`, `sdk-pin`, `required-checks`, `licence-headers` and the four site jobs, all passing;
  - the site built twice against Chromium 1243.

  The whole pipeline was not run here; CI runs it.
