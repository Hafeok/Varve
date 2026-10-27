# The Web UI's design source enters the repository

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#167](https://github.com/Hafeok/Varve/issues/167), part 1 of [#96](https://github.com/Hafeok/Varve/issues/96) |
| **Date** | 2026-10-27 |
| **Tool** | Claude Code 2.1.311, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYNvBGThZuSuStrnN2Z`, the session of Web UI part 1, whose record is on its milestone branch (`2026-10-27-issue-167-web-ui-part-1.md`) |
| **Branch** | `claude/happy-heisenberg-v8vg11-docs-ui`, from `main` at f7b66ae |
| **Source** | the Varve design system artifact, version **1793092562-f3b6**, received as `varve-docs-ui-0.1.zip` |

## The prompt

> Before the next slice: open a docs-only PR from the attached varve-docs-ui-0.1.zip, unpacked at the repo root, under #167. Number the ADR as the next free after your range (0206 unless taken) and fix its references and the decision set’s adr: to match. In the traceability record, name the source as the Varve design system artifact at version 1793092562-f3b6. Leave the decision set unaccepted; I accept on the PR. No code in that PR. The check that regenerates tokens.css from tokens.json, the CSS linking into wwwroot, the level rule and the contract test are ADR 0206’s points 2, 4 and 6 and belong in the milestone PR. Then build the components in the Atoms, Molecules, Organisms, Templates and Pages namespaces against docs/ui/, and keep the visual-decision placeholders only for what the screens will settle.

## The report

**What was taken.** The zip was unpacked into a scratch directory first, read,
then copied to the repository root. Every file landed under `docs/` as the zip
holds it:
- `docs/ui/`:
  - the brand book;
  - `tokens.json`, and `tokens.css` generated from it;
  - `bundle.css`;
  - the core mark;
  - eighteen components, each a `README.md` and a `preview.html`.
- `docs/adr/0206-the-web-ui-design-source.md` and its decision set
  `docs/decisions/the-web-ui-design-source.md`.

The previews carry no script. Each links only `../../tokens.css` and
`../../bundle.css`, and the core mark only `../../assets/varve-core.svg`.
Nothing is fetched from elsewhere.

**Numbering.** 0206 is free on `main` and on every branch of the remote, so the
number stands. The decision set's `adr:` already read `0206`. It is left
unaccepted.

**References fixed in the ADR**, because this pull request lands before the
milestone's:
- **0200–0205.** These exist only on Web UI part 1's branch (0200 and 0203
  written, the rest planned). The Status and the Context say so, and the Checks
  name each ADR touched by number, with "planned" where it is not yet written.
- **The range checked.** "Checked against the accepted ADRs (0001–0205)" now
  reads 0001–0178, which is `main`'s accepted range, plus Web UI part 1's.
- **"AGENTS.md, principle 1"** is now "Stable dependencies". AGENTS.md numbers
  no principles; the stable dependency rule is under its hard constraints.

**Left as received, and reported.** The brand book is the design source, and
these are its own to change:
- **The core mark's path.** It says `Logos/varve-core.svg`; the file is
  `docs/ui/assets/varve-core.svg`. The claim that it is "the same file as the
  repository's package icon" holds: it is byte-identical to `eng/icon.svg`
  (SHA-256 `64b3a631…12bb1`).
- **The stylesheet's path.** "Implementing in `Varve.WebUi`" names
  `components/bundle.css`; the file is `docs/ui/bundle.css`, as ADR 0206 point 1
  says.
- **Not yet present, as expected:** `components/<Comp>/cases/` and
  `screens/*.html`, which ADR 0206 point 1 lists. They come with the
  milestone's contract test and screens.

**No code.** ADR 0206's points 2, 4 and 6 belong to the milestone pull request:
- the check that regenerates `tokens.css` from `tokens.json`;
- linking the CSS into `wwwroot`;
- the level rule;
- the markup contract test.
