# The soaks inform previews

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#129](https://github.com/Hafeok/Varve/issues/129); [#219](https://github.com/Hafeok/Varve/issues/219) and [#222](https://github.com/Hafeok/Varve/issues/222) referenced |
| **Date** | 2026-11-03 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRzUotGnNtdpHy2K62` |
| **Branch** | `claude/adoring-keller-42dw5z-inform`, from `main` at 3024c7a3 |
| **ADRs** | [0215](../adr/0215-the-soak-gate-run-and-judged.md) (amended) |

## The prompt

> Change of plan from the maintainer: for previews, the soaks inform and never gate. One small PR from `main`:
> 1. Preview descriptors: `eng/release-freshness.cs` no longer refuses a preview on the soaks. The release records the latest Linux and Windows soak verdicts (green, red, invalid or none) and the latest full-depth store runs, with links, in the descriptor and the release notes.
> 2. Faults stay a preview gate, read by job: a green full-depth store job on both runners within 3 days, counting jobs inside a run cancelled by another job's timeout (#219).
> 3. A red or invalid soak opens or updates one issue labelled `soak`, from the nightly using the workflow's own token, with the verdict, the run and the judge's output.
> 4. Non-preview releases (`1.0.0` and later): the soaks and faults still run inline, and the release is refused while any `soak` issue is open.
> 5. A dated amendment to ADR 0215 recording the maintainer's decision and the reason (previews were blocked two days on the judge's model, not on a leak). Fixtures for both paths.

During the work the maintainer changed the design. That change was relayed
by the coordinating session and is given here as it arrived, in summary:
keep the Windows soak's freshness check for 1.0.0 and later; **no
`evidence:` block in the descriptor**, which stays exactly as it was;
instead the release workflow runs the printing mode and writes the evidence
into the GitHub Release's notes automatically, exactly as it is at that
moment, with nothing pasted by hand and nothing waiting on a soak; the ADR
amendment says so; and the descriptor-evidence fixtures are dropped.

## The report

- **The gate.** `eng/release-freshness.cs` refuses a preview only on the
  faults: the newest completed `store, full depth` job on each runner,
  green by its own conclusion and within three days. The soaks print as
  `inform:` lines. A release that is not a preview is refused on the Windows
  soak's freshness, as before, and while an open issue is labelled `soak`.
- **Verdicts by job.** A soak is `green` when its job succeeded, `red` when
  its step "The soak misses the gate" failed, `invalid` for any other
  completed failure, `none` without a run; a job from before those steps is
  `red` when "Judge the soak" failed. A store job is `green`, `red`,
  `cancelled` or `none`. Read live on 2026-11-03, main's soaks of 2026-11-02
  are both red and its faults green, so a preview passes.
- **The evidence.** `--evidence` prints it as Markdown; `publish.yml`'s
  release job appends it to the notes it creates the release with, and the
  release gate prints it to its step summary. The descriptor format is
  unchanged.
- **The issue.** The nightly's `soak issue` job, after both soaks, reads
  each failed soak's `verdict.txt` and `judge.txt` from its artefact and
  comments on the open `soak` issue or opens it.
- **The rulings.** `PreviewsCheckMainsLatestRuns` and
  `WindowsWeeklyAndFresh` restated; `ARedOrInvalidSoakOpensAnIssue`,
  `NoReleaseWhileASoakIssueIsOpen` and `TheReleaseNotesHoldMainsEvidence`
  added; all unaccepted, none cited in code.
- **Fixtures.** 26 freshness cases, each of them able to name lines its
  evidence must print.
