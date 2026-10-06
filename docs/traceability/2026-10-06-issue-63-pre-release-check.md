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
  (before anything is pushed) until it does.
- The nuget.org trusted-publishing policy was created with no environment (ADR
  0029); it must be updated to name `release`.

**Format version 1.** ADR 0072, accepted 2026-10-05, freezes `log/` version 1
from `v0.1.0-preview.1`; `docs/spec/storage-format.md` says the same.
`derived/` is not frozen. Still marked provisional: the summary of
`TermIds` in `src/Varve.Store/TermIds.cs` ("Provisional by ADR 0045"), stale
since 0072 froze the layout. Reserved, not provisional: the erasure-mode
settings field, the private term entry and the private counter (ADR 0074),
frozen bytes no code writes yet.
