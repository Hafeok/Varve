# Backup and restore as log copies, and upgrade across format versions

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#129](https://github.com/Hafeok/Varve/issues/129) (the operator guide, pull request C of [the split](https://github.com/Hafeok/Varve/issues/129#issuecomment-6978780204)); found: #363 |
| **Date** | 2026-11-19 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS9TK9P9VYitD1bP8Z` |
| **Branch** | `claude/operator-guide-c-129`, stacked on `claude/relaxed-wright-nv07v6` (#345) at 18a0d5ab |
| **ADRs** | none |

## The prompt

> For the operator guide session.
> 1. Branches: push each remaining part to its own branch, one pull request each: claude/operator-guide-b1-129, claude/operator-guide-b2-129, claude/operator-guide-c-129, claude/operator-guide-d-129. Base each on main. If a part depends on #345's changes, stack it on #345's branch and say so in its description. Try each push once; if one is refused, say so on #129 with the exact refusal and do not try another route.
> 2. #358: add your finding there…
> 3. #345: report 'head <sha>' with the hosted result on 18a0d5ab…
> 4. Each new pull request: Completes its own issue or Completes: none with Refs #129, its decisions filed unaccepted, and its ADR status line in the 'Accepted — filed unaccepted by …' form.

Part C was scoped from it: rewrite `backup-and-restore.md` and `upgrade.md`
against ADR 0216, storage-format §§6–8 and ADR 0213; a server test of a cold
copy restored under a running server; an object-store copy test; and an
`upgrade (previous release)` job. It depends on ADR 0248's markers, so it is
stacked on #345.

## The report

- **The pages.**
  - `backup-and-restore.md`:
    - A copy is a copy of the log, kept by ADR 0216's rules: the log is read forever, and `derived/` is rebuilt.
    - No key is ever in a copy: ADR 0074 refuses a key store, and erasure (ADR 0019) is not built (#203).
    - A table of an object-store dataset's objects says which a copy holds. It holds `log/` and `derived/` (extents and catalog). It never holds `writer` or `readers/` for a restore target, and the page says why.
    - What a geo or asynchronous copy can catch mid-swap.
    - `ShipAsync` writes a checkpoint into the source when there is none (`Replicas.cs:42–45`), so it needs the writer.
    - Restoring to an empty prefix.
    - "Damaged data" is kept unchanged.
  - `upgrade.md`:
    - The format inventory and its rules.
    - The new job.
    - The derived-6 fixture gap (#302).
    - A rolling upgrade stays untested until #318, and a downgrade stays untested.
- **The tests.**
  - **`BackupRestoreTests`** (Varve.Server.Tests; ci.yml `build`, eng/ci.cs `test-server`):
    - A file dataset with four commits and a checkpoint is declared closed.
    - Its `log/` alone and its whole directory are copied, and it is declared open and written on.
    - Each copy is restored in its place under the running server and declared open.
    - The test checks the id, the head and all four positions, and a write after the copy's head.
  - **`ObjectCopyTests` and `AzuriteObjectCopyTests`** (Varve.Store.Tests, namespace `Objects`):
    - **The copy:** `log/` and `derived/`, without `writer` or `readers/`, copied to another prefix while a follower is announced. Opened there, it has the same id, head, checkpoints and every answer, a writer record of its own, and takes a write.
    - **Two mid-swap states:** a catalog whose newest set is missing opens with every answer; a log missing its newest chunk opens below the head and takes a write.
    - **Where they run:**
      - the in-memory cases in ci.yml `build` (Ubuntu and Windows) and again in `storage-services`;
      - the Azurite case in `storage-services`, and skipped elsewhere.
- **The job.** `upgrade (previous release)` (ci.yml `upgrade`) runs `eng/upgrade.cs`.
  - **Which release:** `--resolve` takes the newest descriptor in `releases/` whose version is a tag, by SemVer precedence. Here that was `0.1.0-preview.22`.
  - **The previous release writes:** the job installs that `Varve.Server` tool from nuget.org, and its CLI creates, loads, updates, checkpoints and updates again.
  - **This build reads:** it opens the dataset in place and from `log/` alone, and compares `info`, `export --as-of` at every position, and a query. Then it writes once more.
  - **Not a required check:** it reaches nuget.org.
  - **No hash check:** the descriptor's `tool` hash is the attested package's, and nuget.org serves a repository-signed one (`eng/nuget-served.cs`), so the script does not compare it.
- **Procedure markers.**
  - **Base `18a0d5ab`:** 72 blocks. 44 are run by a job, 25 by none, and 3 are examples.
  - **This branch:** 74 blocks. 48 are run by a job, 23 by none, and 3 are examples.
  - **What changed:** the cold copy and the restore moved from none to `build`. Two blocks were added: an object-store copy (`build` and `storage-services`) and the check after an upgrade (`upgrade`).
- **Found and filed:** #363. Nothing tests what an open does with an object-store copy that is missing a chunk in the middle of its log.
- **Run locally:**
  - `eng/ci.cs` `procedures`, `procedures-fixtures`, `site-links`, `test-projects`, `licence-headers`, `sdk-pin`, `required-checks` and `register`: all passed.
  - `BackupRestoreTests`: 1 passed.
  - `ObjectCopyTests`: 3 passed.
  - `AzuriteObjectCopyTests`: 1 passed. This ran against Azurite 3.35.0 from npm, not the 3.37.0 image CI pins.
  - `eng/upgrade.cs` against the installed `0.1.0-preview.22` tool and a Release build of this branch: it passed in 9 s.
  - `eng/dco.cs`, `eng/issue-refs.cs` and `eng/model-names.cs` with `--base origin/main`.

  The whole pipeline was not run here; CI runs it.
