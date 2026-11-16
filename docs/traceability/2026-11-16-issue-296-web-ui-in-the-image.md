# The container image ships the Web UI and serves it by default

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#296](https://github.com/Hafeok/Varve/issues/296); opened [#297](https://github.com/Hafeok/Varve/issues/297) |
| **Date** | 2026-11-16 |
| **Tool** | Claude Code, a cloud session (claude.ai/code), not the maintainer's delegate (ADR 0239) |
| **Model** | not written by the tool: its environment does not put a model identifier in a file pushed to a repository. The session gave it in its chat reply, and the maintainer fills it in here |
| **Session identifier** | `session_01NbWSYQ2hTZ1UHvTN2k9zgV` |
| **Branch** | `claude/eloquent-fermi-6lgxt9`, from `main` at 6869a5f8 |
| **ADRs** | [0245](../adr/0245-the-web-ui-in-the-container-image.md), filed unaccepted; 0111, 0201, 0202 and 0225 amended 2026-11-16, unaccepted |

## The prompt

> Read AGENTS.md, then #296, then the session prompt in the attached
> varve-claude-code-web-ui-in-the-image.md. Post your plan on #296 and wait
> for my answer before writing code.

The attached session prompt, *the container image ships the Web UI and
serves it by default (#296)*, set out the maintainer's ruling of 2026-11-16,
five closed rulings, eight plan questions, the gate, and how the work lands.

The maintainer's answer to the plan, recorded on #296:

> The plan is agreed, with these answers.
>
> 1. Off switch: `Varve:WebUi:Enabled`, `true` by default. Don't document the
>    empty-path trick as the way to turn it off.
> 2. Generated settings: an explicit `Varve:WebUi:Settings`, `File` by
>    default and `Generated` when set; the image sets it. No detection by a
>    missing file, and the image's bundle stays identical to the attested
>    zip's contents. When `Generated`, the bundle's `appsettings.json` and its
>    `.br` and `.gz` copies are never served. Any `Varve:WebUi:Auth:*` or
>    `Varve:WebUi:Server` set while it is `File` refuses the start, naming the
>    setting, like every other bad setting.
> 3. Management listener: its own issue; open it now and link it from #296,
>    with your reading of the code and the fix you'd propose (a public
>    management address the operator supplies, and the console using an
>    address per plane). In this pull request: correct
>    `docs/operator/web-ui.md` so it no longer says that setup works, and
>    shape the generated settings so a management address can be added later
>    as one field.
> 4. Everything else as you proposed: `/app/webui`, the build from the same
>    run's attested zip, `NotConfigured`, ADR 0245, and its own release as
>    preview.22. Post the measured tool-package figure and the CI time added
>    on #296.
>
> Record these on #296 as the maintainer's answers, then build.

## The report

See the session's close-out,
[`docs/sessions/2026-11-web-ui-in-the-image.md`](../sessions/2026-11-web-ui-in-the-image.md),
for what was built, the measurements, the test counts and the questions.
