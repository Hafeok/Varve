# Problems that say what to do

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#194](https://github.com/Hafeok/Varve/issues/194) |
| **Date** | 2026-11-10 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app; a sub-agent of the same session wrote the first drafts of 51 pages' `What to do` sections |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYNvBGThZuSuStrnN2Z` |
| **Branch** | `claude/happy-heisenberg-v8vg11-194`, from `main` at 2f7573d8 |
| **ADRs** | [0242](../adr/0242-problems-that-say-what-to-do.md), filed unaccepted |

## The prompts

> Thanks. Take the three server follow-ups, one pull request each, starting from a fresh main: #189 first (If-Match on the state route, with the same ETag the declaration route uses), then #193, then #194. Post your plan on each issue before building. You are not the delegate: file any decision unaccepted, post your heads and ask. Part 2 (#195–#199) waits for its own prompt.

> Take #262 first: the intermittent 400 rdf-syntax on Graph Store POST. Start with the evidence it asks for, so we know whether the runner sent a short body or the server parsed a short read. Then #189, #193 and #194 as before.

## The report

The plan was posted on the issue before building. Its five points are ADR
0242's rulings, all filed unaccepted.

**What was changed.**
- **F02, `forbidden`:** `Exchange` writes the refusal itself, with
  `permission`, `scope`, `dataset`, `agent` and `plane`, and the same
  `WWW-Authenticate`. A `401` is unchanged.
- **F03, a failed dataset:** `DatasetFailure` (`damaged`, `configuration`,
  `transient`) is read from exception types (`OpenDatasets.Classify`). It is
  carried on `DatasetEntry`, the listings and `…/state`, and on the
  `unavailable` problem with `reason` `failed` and a detail that says what
  to do.
- **F04, `writer-superseded`:** `writer` and `epoch` come from the store's
  writer record, read through the new `IWriterDirectory.HolderAsync`. The
  first attempt used the directory's route, which a superseded instance
  still answers `Local`, because it believes it is the writer. The
  object-storage test caught that.
- **F17, `shutting-down`:** the detail says the instance is draining and,
  in a fleet, that the others keep serving.
- **Every `docs/problems/` page** has `## What to do`. A sub-agent drafted
  51 pages against the operator guides, the CLI and the settings, with
  every link checked. This session wrote the four pages above and fixed
  the index's dead link to `limits.md`. `ProblemCatalogueTests` fails a page
  without the section. That failure was shown by renaming `conflict.md`'s
  section: "conflict.md has no '## What to do' section".

**Tests.** `AuthTests` covers the members on a management `403`, a
data-plane `403` and a fleet-scoped `403`. `ManagementTests` covers
`Classify` for each class, wrapped damage included, and `transient` on a
held lease. `ProblemCatalogueTests` covers the damaged `unavailable` and
every page's section. `ObjectStorageServerTests` (Azurite 3.37.0) checks
that the superseded write names `http://127.0.0.1:9/second` at epoch 2.
**Not tested by a request:** shutting-down's new detail. A write during the
drain races the host's stop, and a test that tolerates either outcome
proves nothing.

**Found on the way, not changed** (the drafting agent's report, for the
maintainer): `configure.md` names `request-timeout` and `result-too-large`,
which are not types; `MaxAsOfDistance` is described two ways;
`load-spill-damaged.md` cites a damage guide that doesn't exist; and
`agent-required.md` and `declaration-invalid.md` may be stale against the
management plane.
