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

## The review round

**The maintainer's rulings** on #174, verbatim where they concern it:

> ADR 0206 edits: accepted as you made them.
> Brand book paths: fix both in #174. Use `docs/ui/assets/varve-core.svg` and `docs/ui/bundle.css`, and check the rest of `docs/ui/` for other paths written relative to the artifact.
> The two CSS gaps: fix them in #174. Patching `docs/ui/` by PR is the process, not a workaround. Add to `bundle.css`, base section:
>
> * `html, body.vw-root { height: 100%; } body.vw-root { margin: 0; } .vw-shell { min-height: 100%; }`
> * `.vw-root [tabindex="-1"]:focus { outline: none; }`. A heading focused on navigation isn’t interactive, so it needs no ring. Every control keeps `:focus-visible`.
>
> Level rule: keep it strict. A component uses only lower levels, never its own level.
>
> * `ConfirmDialog` and `ResourceTable` taking a problem from the page: right. Make it a slot (`RenderFragment`) that the page fills, and update both components’ READMEs in #174 to say so.
> * `CopyButton` as a molecule: right. Add it to the README’s level table and give it a component folder.
> * The drift note as a switch on `Notice`: no. It puts domain text in a molecule, and lower levels must not know Varve’s domain. `Notice` stays generic. The drift note’s wording lives with the dialog’s caller: the page passes a `Notice` into `ConfirmDialog`’s slot, the same pattern as the problem. Put the text in one place in `Pages` or `Tones`, not per page.
>
> Placeholders:
>
> * `unreachable-placement`: directly below the view head, above everything else, with the last data still shown below it. The reference screens follow this.

**What changed:**
- **The two paths.** The core mark is `docs/ui/assets/varve-core.svg`, and the
  stylesheet `docs/ui/bundle.css`. These were the only paths written relative
  to the artifact. Every preview's `../../tokens.css`, `../../bundle.css` and
  `../../assets/varve-core.svg` resolves from
  `docs/ui/components/<Comp>/`, and no other file names a path.
- **`bundle.css`, base section.**
  - `.vw-root [tabindex="-1"]:focus { outline: none; }`.
  - `html, body.vw-root { height: 100%; }` and `body.vw-root { margin: 0; }`.
  - `.vw-shell { min-height: 100%; }` was already in the Shell section, so it
    is not repeated.
- **The level rule in the brand book.** A component never uses one of its own
  level; an organism showing another organism's output takes a slot the page
  fills. Domain text, such as the drift note, lives once at the page level.
- **The unreachable notice** sits directly below the view head, above
  everything else, with the last data below it.
- **`ConfirmDialog`** takes a Note slot and a Result slot, which the page
  fills; its preview's drift note now carries the fixed text.
- **`ResourceTable`** takes a Problem slot, rendered in place of the table.
- **`Notice`** is generic, and its README says where the drift note's
  wording lives.
- **`CopyButton`** joins the molecules in the level table, with a folder, a
  README and a preview. ADR 0206 now says nineteen components.

## The Parts level

**The maintainer's ruling**, verbatim:

> Neither 1 nor 2. Add a level: `Varve.WebUi.Parts`, between `Templates` and `Pages`.
>
> * Parts are domain-aware, non-routed components: `ProblemView`, `ReadStamps`, `DriftNote`, and the like later. They may use the client’s model types (a problem body, a position, a status) and `Tones`, and hold Varve’s wording.
> * Pages are routed screens only. They are the only level that injects or calls the client’s services, i.e. reads data. They may use Parts and everything below.
> * The rule stays uniform: a component uses only lower levels, never its own. Parts don’t use Parts, pages don’t use pages. No exemption anywhere. If two parts later genuinely need each other, that’s a design conversation, not an analyzer exception.
> * The analyzer has two checks:
>    1. the level order over `Atoms < Molecules < Organisms < Templates < Parts < Pages`;
>    2. client service types (anything that calls the server) referenced only in `Pages`; client model types allowed in `Parts` and `Pages`.
>  If the client doesn’t separate service types from model types cleanly enough for the analyzer to tell them apart, say so before inventing a marker.
> * Opt-in to generated code stays scoped to this rule, as you found is possible.
> * Paperwork:
>    * ADR 0206 point 4 is amended in #174 (still unaccepted, so amend in place, no dated block). The decision set’s `AtomicLevelsAreADependencyRule` and `OnlyPagesCallTheApi` statements change to name Parts and the service/model split.
>    * The brand book’s level table gains the Parts row.
>    * Move `ProblemView`, `ReadStamps` and `DriftNote` into `Parts` on the milestone branch.

**How the client's namespaces split.**
- **`Varve.Protocol.Client.Model` holds only values,** and none of its types
  calls the server: the problem, the response facts, readiness and its
  wrappers, the graph content and condition, the RDF document, the received
  events, and two exceptions.
- **Every type that calls the server is outside it:**
  - in `Varve.Protocol.Client`: `SparqlHttpClient`, `VarveHealthClient`,
    `HttpServiceHandler`, `RdfDocumentClient` and `OutboundHttp`;
  - in `.Hosting`: `DatasetSetupService`.
- **The root namespace also holds values that call nothing:** `ClientLimits`,
  `EndpointPolicy`, `BearerTokenProvider`, the follow and setup options, and
  their exceptions.

So the analyzer takes the namespaces as they stand, with no marker:
- `.Model` is the model;
- everything else under `Varve.Protocol.Client` counts as service.

That is strict on the root's values, which no part needs today. This was
reported to the maintainer.

**Amended in place**, since the ADR is unaccepted:
- **ADR 0206 point 4:** the six levels, the uniform rule, Parts and Pages, and
  the service/model split.
- **Point 5:** the tone mapping is called by parts and pages.
- **The Checks' analyzer note:** the two checks, and the generated-code opt-in
  scoped to the rule alone.

**The decision set:** `AtomicLevelsAreADependencyRule`, `OnlyPagesCallTheApi`
and `OneStateToToneMapping` are restated.

**The brand book:** the level table gains the Parts row; domain knowledge
and wording sit in parts; parts don't use parts. The `ConfirmDialog` and
`Notice` READMEs say the drift note lives in a part.
