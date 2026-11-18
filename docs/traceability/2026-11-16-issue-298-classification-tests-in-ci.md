# `Varve.Classification.Tests` runs in CI, and a test project no job runs fails a gate

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#298](https://github.com/Hafeok/Varve/issues/298); found by the operator-guide audit on [#129](https://github.com/Hafeok/Varve/issues/129#issuecomment-6915878686) |
| **Date** | 2026-11-16 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS9TK9P9VYitD1bP8Z` |
| **Branch** | `claude/relaxed-wright-nv07v6`, from `main` at 48a1fd5 |
| **ADRs** | none; no behaviour is decided |

## The prompt

> Excellent audit. First, before the plan: fix #298 now, as its own small pull request, so `Varve.Classification.Tests` runs in CI. If any test fails once it runs, report it on #298 and stop; don't fix product code.
> Your ADR is 0247, not 0246: the #132 session's branch already has an ADR 0246. Check again when you file it.
> Answers:
>
> 1. Markdig in `eng/site/`.
> 2. (a), narrowly: fetch only the Mermaid package's own archive by exact version and SHA-512, with no npm install and no dependency tree. One register entry, build scope, with its licence. Check each generated SVG contains no script and no reference to another origin.
> 3. Don't edit ADR 0114. Add a dated note with the right target, and exempt that one link in the gate by name, citing the note.
> 4. Include `docs/spec/` and `docs/rules/`.
> 5. Yes to the markers and their gate.
> 6. The Entra Bicep additions are a separate issue and pull request, landed before the Entra page.
> 7. Yes: move the Zitadel machine client off its secret, and extend `auth (zitadel)`. Stop and report if the flows don't meet.
> 8. Yes to a `kind` job, run nightly and on changes under `deploy/kubernetes/`, not on every pull request. Its image goes through `.github/ci-images.yaml`.
> 9. Yes to the upgrade job for a single dataset, in pull request C. Open an issue for the mixed-fleet rolling upgrade, marked for 1.0, and have `upgrade.md` say a rolling upgrade is untested until it lands. Downgrade stays stated as untested.
>
> Record these on #129, open the docs-site issue, and proceed: #298 first, then step 1.

Only the first paragraph is this pull request's. The answers are recorded on
[#129](https://github.com/Hafeok/Varve/issues/129#issuecomment-6917200543),
and the docs site is [#317](https://github.com/Hafeok/Varve/issues/317).

## The report

- **The gap.** `tests/Varve.Classification.Tests` has been in `Varve.slnx`
  since milestone 9 part 1 (#202). Every job built it, and none ran it: it had
  no step in ci.yml's `build` job and no `Test(...)` in `eng/ci.cs`. The other
  29 test projects under `tests/` are each run by a workflow or by `eng/ci.cs`.
  `Varve.WebUi.Tests` runs only through `eng/ci.cs`, in `pipeline
  (devcontainer)`, and the two browser projects through their own scripts.
- **Run here first, as CI will:** `dotnet test --project
  tests/Varve.Classification.Tests/Varve.Classification.Tests.csproj
  --configuration Release`: 5 passed, 0 failed, 0 skipped. No product code
  needed changing, so the stop condition in the prompt did not arise.
- **The change.**
  - A `Test Varve.Classification` step in ci.yml's `build` job, on both
    runners, and in publish.yml's test steps, beside `Varve.Shacl.Store`.
  - `test-classification` in `eng/ci.cs`.
  - **The gate, `eng/test-projects.cs`**, the jobs `test-projects` and
    `test-projects-fixtures`. A test project is a directory `tests/<name>/`
    holding `<name>.csproj` that references xunit. It is run when `eng/ci.cs`
    names it in `Test("<name>")`, or when `eng/ci.cs` or a workflow names its
    path as a whole segment. It reads the jobs' text, so a project a job only
    builds still passes; the header says so.
- **The gate's failure path, proven both ways.**
  - Against `main` before the change: `tests/Varve.Classification.Tests is a
    test project that no job runs`, exit 1; 30 test projects, 29 run.
  - Its fixtures, `tests/fixtures/test-projects/`: `failing` exits 1 with one
    finding (`Fixture.Unrun.Tests`, named only as the prefix of another path),
    and `passing` exits 0.
- **Run locally with the change:** `dotnet run eng/ci.cs -- --only
  licence-headers --only sdk-pin --only test-projects --only
  test-projects-fixtures --only test-classification`, all pass. The whole
  pipeline was not run here; CI runs it.
