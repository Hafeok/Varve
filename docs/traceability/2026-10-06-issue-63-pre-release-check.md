# Pre-release check for v0.1.0-preview.1

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#63](https://github.com/Hafeok/Varve/issues/63) |
| **Date** | 2026-10-06 |
| **Tool** | Claude Code 2.1.291, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served — from the session's own metadata (`get_session`: `session_context.model` and `last_served_model`) |
| **Session identifier** | `session_01ASeu6JdJhvuMmqQhTd9pRY` |
| **Branch** | `claude/pensive-sagan-qu5pah`, from `main` at 66a65d2 |

## The prompt

> Pre-release check, no tag yet. On main: dotnet pack every packable project in Release with ContinuousIntegrationBuild; open each nupkg and verify PackageLicenseExpression MPL-2.0, the README and icon present, RepositoryUrl, Authors decision-driven-design, and the version that tag v0.1.0-preview.1 will produce. Verify publish.yml still lists every packable project, uses the release environment, trusted publishing via NuGet/login with NUGET_USER, and attaches the changelog section as release notes. Report every mismatch; fix only in the workflow and packaging metadata. Confirm from ADR 0072 that tagging freezes format version 1 and list anything in log/ still marked provisional.

## The report

**Method.** `dotnet pack Varve.slnx -c Release -p:ContinuousIntegrationBuild=true`
on 66a65d2 under a **local** tag `v0.1.0-preview.1` (deleted afterwards, never
pushed), then `eng/package-metadata.cs` and a direct read of every nuspec.

**Mismatches, and what was done.**

1. **The tag would have published `0.0.0-alpha.0.49`.** MinVer's default tag
   prefix is empty, so it logs "Ignoring non-version tag v0.1.0-preview.1".
   Fixed: `MinVerTagPrefix` `v` in `Directory.Build.targets`; the re-pack gives
   `0.1.0-preview.1` for all ten. `publish.yml` now also checks each package's
   version against the tag before anything is pushed.
2. **No GitHub release, no changelog notes.** Fixed: the publish job extracts
   the `## [<version>]` section of `CHANGELOG.md` before pushing and fails if
   there is none; a separate `release` job, with `contents: write` and after
   the push, creates the release with those notes, marked prerelease for a
   prerelease version.
3. **Gates CI runs that the tag did not**: decision sets, banned symbols, and
   the browser storage tests in headless Chromium (the only tests of
   `Varve.Store.Browser`, which is published). Added.

**Verified as correct.** All ten packable projects (`Varve.Iri`, `Xsd`, `Rdf`,
`Turtle`, `Sparql`, `Sparql.Results`, `Sparql.Evaluation`, `Store`,
`Sparql.Store`, `Store.Browser`) pack through `Varve.slnx`; each nuspec has
`<license type="expression">MPL-2.0</license>`, authors `decision-driven-design`,
its own `README.md` and `icon.png` in the package, `LICENSE` and `NOTICE`, and a
repository element with the GitHub URL and the commit. `publish.yml` uses the
`release` environment and `NuGet/login` with `secrets.NUGET_USER`, adjacent to
the push.

**Blockers left for the maintainer.**

- `eng/changelog.cs` has no tag mode, so `CHANGELOG.md` has no
  `## [0.1.0-preview.1]` section and the tag fails at the release-notes step
  (before anything is pushed) until it does. *Answered by the second prompt,
  below.*
- The nuget.org trusted-publishing policy was created with no environment (ADR
  0029); it must be updated to name `release`.

**Format version 1.** ADR 0072, accepted 2026-10-05, freezes `log/` version 1
from `v0.1.0-preview.1`; `docs/spec/storage-format.md` says the same.
`derived/` is not frozen. Still marked provisional: the summary of
`TermIds` in `src/Varve.Store/TermIds.cs` ("Provisional by ADR 0045"), stale
since 0072 froze the layout. Reserved, not provisional: the erasure-mode
settings field, the private term entry and the private counter (ADR 0074),
frozen bytes no code writes yet.

## The second prompt

> Open the PR, extended with: eng/changelog.cs --release <version> (cuts [Unreleased] into the versioned section with date and compare link) and --check (newest tag has its section); the release-per-milestone ADR with the "release pending" CI check (fails the next milestone PR while the last milestone's release is pending); the TermIds comment corrected to ADR 0072. Keep the metadata and version gates as you built them. I update the trusted-publishing policy to name the release environment before tagging.

## The second report

- **ADR 0085**, filed unaccepted with its decision set: every milestone ends in
  a release; the release is cut by the changelog tool and its commit tagged;
  released sections are fixed; the next milestone waits. Nothing cites it in
  code, so it adds no `CS0618`.
- **`eng/changelog.cs --release <version> [--date]`** cuts `[Unreleased]` into
  `## [<version>] - <date>` and writes compare links from `PackageProjectUrl`.
  Later runs copy released sections as they are and regenerate `[Unreleased]`
  from the commits after the newest tag, or after the cut commit before the
  tag exists. **`--check`** now checks that the newest `v*` tag reachable from
  HEAD has a non-empty section, in the working tree and at the tag; the old
  stale-file comparison could not pass once committed and ran nowhere.
- **`eng/release-pending.cs`**: fails a change that adds a line to
  `eng/changelog-sections.txt` while the base's newest section has no `v*` tag
  containing its first commit. A job in `eng/ci.cs` and the `release pending`
  job in `ci.yml`.
- `publish.yml` runs `--check`, and the notes extraction stops at the link
  definitions, so the oldest release's notes do not carry them.
- `TermIds`' summary names ADR 0072's frozen layout instead of ADR 0045.

**Exercised in a scratch clone**, with throwaway local tags: cut, commit,
regenerate (no change), `--check` before and after tagging, a commit after the
tag landing under `[Unreleased]`, a second cut with a compare link, a repeated
cut refused; the gate passing with the previous milestone tagged, failing
without, and passing a change that adds no section.

**What the tag now needs, in order:** this pull request merged; the
trusted-publishing policy naming `release` (the maintainer's, as stated);
`dotnet run eng/changelog.cs -- --release 0.1.0-preview.1` on `main`,
committed with its `Refs`; the tag on that commit.

## The third prompt

> Yes, watch #64. Also open the two port issues the ADR expects as exit criteria: one in how-we-work for the process gates (changelog with release cut, licence headers, issue references, DCO, package metadata, native assets, release-pending) and one in the analyzer repository for the ledger gates (register citations, suppression citations), each listing the eng/ scripts and the Varve commit they come from. Reference both from AGENTS.md's eng/ line.

The session reported that no ADR named such exit criteria, that the analyzer
repository never names Varve, that nothing checks DCO, and that suppression
citations are `DD0008`, and asked. The maintainer's answers, verbatim:

> Write a short ADR, Accepted, "Home of the eng/ gates": eng/ holds Varve-specific gates (conformance ratchet, exemptions, guard counts, benchmarks, ci.cs as orchestrator) and temporarily hosts generic ones pending their port. Two destinations: process gates (changelog with release cut, licence headers, issue references, DCO, package metadata, native assets, release-pending) to mindovermachine-dev/how-we-work beside repo-standard, as the stewardship standard made executable; ledger gates (register citations, suppression citations, decision-set checks not already covered by DDGEN) to the analyzer repository beside DecisionDriven.Report. Exit criteria: one issue in each destination listing the scripts and the Varve commit they come from; a script leaves eng/ when its port is released and Varve consumes it from there. Alternatives: keep everything in Varve (rejected: every adopter re-implements the standard), port now (rejected: blocks the release). Then open the two issues citing the ADR, and reference it from AGENTS.md's eng/ line. Same PR as #64 if it is still open, otherwise a small follow-up.
>
> Correction: the analyzer repository never names Varve, so its issue describes the ledger gates generically and carries no consumer, commit or link back. Provenance (scripts, commits, the link to the analyzer issue) goes in a Varve issue. Check how-we-work for an equivalent rule before writing its issue; if it has one, split the same way.
>
> Correction to the ADR and issues: DCO is a workflow check, not a script; list it in the how-we-work issue as a workflow to port, on its own line. Suppression citations are DD0008 and already live in the analyzer repository; remove them from the ledger-gates list. If the register-citation gate is the only ledger gate left, the analyzer issue covers that one; if DDGEN already covers the decision-set checks, say so in the ADR.
>
> Ledger gates for the analyzer issue: eng/dependency-register.cs (every package version cites an ADR; bump classification), plus eng/banned-symbols.cs only if it enforces ADR citations on entries. State generically that the ported form should cite decision keys rather than ADR numbers.

## The third report

- **ADR 0086, "Home of the eng/ gates"**, in #64. Its status says Accepted, as
  decided; its decision set carries no `accepted-by`, which a session never
  writes, so the maintainer adds it.
- **Facts checked before writing:**
  - how-we-work has no rule against naming a consumer, so its issue carries
    the provenance itself;
  - `banned-symbols.cs` does enforce ADR citations, so it is a ledger gate;
  - DDGEN covers only duplicate, non-identifier and colliding keys (0001,
    0002, 0005), so `decision-sets.cs`'s other generic checks are ported too;
  - **no workflow checks DCO in Varve**: the issue lists it as a workflow to
    write, not one to port.
- **Issues:**
  [how-we-work#1](https://github.com/mindovermachine-dev/how-we-work/issues/1)
  for the process gates, with scripts and commits;
  [decision-driven-analyzers#84](https://github.com/Hafeok/decision-driven-analyzers/issues/84)
  for the ledger gates, generic, naming no consumer; and
  [#65](https://github.com/Hafeok/Varve/issues/65), the ledger gates'
  provenance. AGENTS.md's `eng/` line references all three through ADR 0086.
