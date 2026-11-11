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
**Shutting-down's new detail** was at first not tested: a write during the
drain races the host's stop. The maintainer's ruling of 2026-11-11 (below)
moved the test to where the problem is built.

**Found on the way, not changed** (the drafting agent's report, for the
maintainer): `configure.md` names `request-timeout` and `result-too-large`,
which are not types; `MaxAsOfDistance` is described two ways;
`load-spill-damaged.md` cites a damage guide that doesn't exist; and
`agent-required.md` and `declaration-invalid.md` may be stale against the
management plane.

## The maintainer's rulings, 2026-11-11

> 2. ADR 0242, the 403: agreed, with one condition from row-level access. A 403 names only what the caller sent, never anything looked up, and never confirms that something hidden exists. Where the caller can't see the target (a hidden graph, a row-restricted read), the answer stays what it is today. Add a test that a hidden graph and an absent graph get the same answer, and state the condition in the ADR.
> 3. `shutting-down`: test the message where the problem is built, not through a real shutdown.

**What was done.**
- The condition is in ADR 0242's ruling 1 and in a sixth key,
  `RefusalsNameOnlyWhatTheCallerSent`, filed unaccepted. The `forbidden`
  page says the same.
- `AuthTests.a_hidden_graph_and_an_absent_graph_get_the_same_answer_and_so_do_datasets`
  compares a hidden graph with an absent one for a graph-scoped caller:
  Graph Store `GET`, `HEAD`, `PUT`, `POST` and `DELETE`, and `INSERT DATA`,
  `DROP GRAPH` and `CLEAR GRAPH`. It also compares a dataset the caller
  holds nothing on with one that doesn't exist, on the management plane
  and on the data plane. Only the request id and the name the caller sent
  are taken out before the bodies are compared. All match, so no code
  changed for the condition: the members were already only what the caller
  sent.
- The `shutting-down` problem is built in `ServerHost.ShuttingDownAsync`,
  and `HostTests.A_write_while_draining_is_shutting_down_and_says_what_to_do`
  checks both messages there, with no real stop.

