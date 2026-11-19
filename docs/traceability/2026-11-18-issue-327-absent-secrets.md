# A declared secret may be absent: repo-standard reports it and is not red

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#327](https://github.com/Hafeok/Varve/issues/327) |
| **Date** | 2026-11-18 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYPUMnLqwEBi4fBQ1tR` |
| **Branch** | `claude/absent-secrets-327`, from `main` at bb1d142 |
| **ADRs** | [0039](../adr/0039-repo-standard.md), amended 2026-11-18, filed unaccepted |

## The prompt

The session's prompt covers three pull requests; this record is step 1's.

> Session prompt: three small 1.0 items — reserve `proof/` (#340), absent secrets in repo-standard (#327), the idle-fleet test (#332)
> Paste into a fresh session on `Hafeok/varve`. Read `AGENTS.md` first and follow it in full, then #340, #327 and #332 with their comments.
> Three independent, small changes. One pull request each, in this order. None carries a release descriptor. They may land as one train.
>
> Step 1 — #327: a declared secret may be absent
> `main`'s weekly `repo-standard` job is red because the `auth-providers` environment declares five secrets that do not exist yet (three `VARVE_ENTRA_*`, two `VARVE_GOOGLE_*`). The maintainer has not provisioned those tenants, and ADR 0223 already says the provider leg skips when none of its identifiers are present.
> The maintainer's ruling of 2026-11-17: the five values may be declared as absent.
>
> * The tool's schema types an environment's secrets as a plain list of names, so a name cannot be marked absent today. Extend it so a secret can be declared with its name and that it may be absent.
> * A secret declared so and missing is reported as "declared, absent", and the run is not red for it. A secret declared without that marking and missing is still red.
> * repo-standard never reads or writes a secret's value. That does not change.
> * Mark the five `auth-providers` values, and nothing else.
> * Fixtures: declared and present; declared as may-be-absent and missing; declared plainly and missing (red); an undeclared secret that exists (as today).
> * If the schema change needs a decision, file it unaccepted as a dated amendment to the ADR that holds repo-standard's schema.
>
> Step 2 — #340: reserve `proof/` in storage format 1
> #292 (accepted as a proposal on 2026-11-18, for a minor after 1.0) will add a `proof/` directory beside `log/` and `derived/`. The format freezes at 1.0, so the name is reserved now.
>
> * Find out first, and post it on #340: what each reader of a dataset directory does today with an unknown sibling of `log/` and `derived/`, on the file backend and on an object store: open for read, open for write, a follower, backup and restore, `varve storage`, and the format inventory's gate. The shipper is being built on #338; say what you found there and tell that session on #166, and do not change its branch.
> * Reserve the name in `docs/spec/storage-format.md` and the format inventory (ADR 0216): 1.0 writes nothing there; a reader ignores it and never deletes it.
> * A read-forever fixture: a format 1 dataset with a `proof/` directory holding objects this build does not know. It opens for read and for write, follows and restores, and the objects are byte for byte the same afterwards.
> * A dated amendment to ADR 0216, filed unaccepted.
> * Build no tile, checkpoint, tree arithmetic or route. That is #292.
>
> Step 3 — #332: an idle fleet's system dataset does not move
> ADR 0244's idle collection runs only when the whole server is idle, and the system dataset counts toward that. The delegate read the code and found that nothing commits to the system dataset on a timer; nothing asserts it.
>
> * A test: a fleet of at least a writer and a follower, with no requests, over many `Fleet:FollowInterval` ticks under a controlled clock. The system dataset's head position does not move.
> * On the same fleet: after a checkpoint and the idle wait, the idle collection runs.
> * Sized for the land gate, with no real waiting. Report its duration per OS.
> * If the test fails, that is a finding: stop, post it on #332 and #227, and do not change product code to make it pass without the maintainer's answer.
>
> How this lands
>
> * Issues are the work log. Comment on each issue when you start it. Work you find and don't do becomes its own issue at once.
> * You are a cloud session, not the maintainer's delegate (ADR 0239). File every decision unaccepted. Never write `accepted-by` on a decision set, and never land.
> * Never write the approval line, for any hash, anywhere, including in a report. Report a head as "head `<sha>`", with its hosted result after reading every failed job.
> * Each description carries `Completes: #N` for its issue, and the completing commit `Closes #N`. No closing keyword in a description.
> * ADR status lines read "Accepted — filed unaccepted by …".
> * Commits: as `Claude (Emil) <claude+emil@okkels-klein.dk>`, signed with the agent key. Never `git commit -s`. The co-author trailer is exactly `Co-Authored-By: Claude <noreply@anthropic.com>`; no commit message names a model.
> * Branches: one per pull request, named `claude/<what>-<issue>`. If a push to a new branch is refused, say so on the issue with the exact refusal and do not try another route.
> * When `main` moves, merge it in. Never rebase a pushed branch, and never force-push.
> * No suppression or downgrade of DD or VARVE rules. Never weaken a gate to make a test pass.
>
> Close-out
> `docs/sessions/2026-11-<name>.md` and a `docs/traceability/` record per pull request: the pull requests, issues closed, what each reader does with an unknown directory, the test's duration per OS, and anything deferred as a linked issue.

## The report

- **What was red.** The weekly `check` on `main`, for five secret names in
  the `auth-providers` environment that do not exist: the test tenants are not
  provisioned, and ADR 0223 has the provider leg skip without them.
- **The schema.** An item of a `secrets` list, the repository's or an
  environment's, is a name, or `{name, may_be_absent}`. A name declared twice
  in one list, in either spelling, is a declaration error.
- **The check.** A name declared `may_be_absent` and missing is an `=` entry,
  "declared, absent": printed, counted after the summary, not a difference,
  never written, and neither `check` nor `apply` exits 1 for it. Present, it
  is as declared and is not reported. A plain name missing is still `!` and
  red; an undeclared name present is still `!` and red. No secret value is
  read or written: the same list endpoints, names only.
- **The declaration.** The five `auth-providers` names are marked, and
  nothing else; the comment above the environment says why. The tool loads
  and validates the file as amended.
- **Fixtures** (recorded scenarios under
  `tools/repo-standard/tests/RepoStandard.Tests/fixtures/recorded/`):
  - `secrets-check-may-be-absent`: declared and present, may-be-absent and
    present, may-be-absent and missing; exit 0, one `=` entry.
  - `environments-check-may-be-absent`: one of each of the four cases the
    prompt names, in an environment; exit 1 for the plain missing name and the
    undeclared one, and the absent one is `=`.
  - `environments-check-all-absent`: shaped like `auth-providers` today;
    exit 0.
  - `secrets-check` and `environments-check`, unchanged: declared plainly and
    missing, and undeclared and present, are red as before.
  - `DeclarationTests`: both spellings load; an unknown key, a non-boolean
    mark, a missing name and a duplicate are each refused with their path.
- **The decision.** `check`'s "any difference exits 1" now has one stated
  exception, so it is a dated amendment to ADR 0039 with one decision,
  `ADeclaredSecretMayBeAbsent` in `repo-standard`, **filed unaccepted**.
- **Run here:** `dotnet run eng/ci.cs -- --only decision-sets --only
  repo-standard-build --only repo-standard-test`: all pass, 98 tests passed
  and 1 skipped (the integration test, which needs a scratch repository).
- **What it will not show until it lands:** the weekly run on `main`. Read
  from the code and the recorded scenarios, not observed against the live
  repository.
