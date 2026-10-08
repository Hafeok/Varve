# Release by descriptor

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#73](https://github.com/Hafeok/Varve/issues/73), opened by this session |
| **Date** | 2026-10-08 |
| **Tool** | Claude Code 2.1.294, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata (`get_session`: `session_context.model` and `last_served_model`) |
| **Session identifier** | `session_01YbhFvg7wVgX3FdRbofvTt8` |
| **Branch** | `claude/gracious-planck-l5ugrc`, from `main` at 58c1709 |

## The prompts

### The brief

> A release is a decision, and this repository files decisions. Today the one decision still taken by a command is the release itself: ADR 0085 has the maintainer cut the tag by hand, and `release-pending` reminds the next milestone's PR when that slipped. Replace it with a release descriptor in the repository: adding `releases/<version>.yaml` to a pull request proposes the release, merging it through the `land/` path cuts it, and nothing else in the path holds a credential or a human step. Read ADRs 0072, 0085–0088 and `eng/changelog.cs`, `eng/release-pending.cs` and the release workflow before planning. Plan first, wait for approval; one PR, red only on `CS0618`.
> The shape is taken from `mindovermachine-dev/actor-indexed-determination` (`spec/release-format.md`, its release workflow and validator); read them for behaviour, write ours in C# under `eng/`, and credit the repository in the ADR. Where we differ, and why:
>
> 1. Descriptor. `releases/v0.1.0-preview.2.yaml`:
>    * `format: 1`.
>    * `version`: SemVer 2 including prerelease, equal to the filename stem, never reused. Their regex forbids prerelease; ours must accept `-preview.N`.
>    * `title`: one line, no version prefix; the tag message is `<version> — <title>`.
>    * `commit`: optional, a retro-cut only, must be an ancestor of `main`. Omitted is normal.
>    * `basis`: mandatory. The milestone issue (`#NN`), every ADR number and ledger decision key first shipped by this release, and `storage-format: <n>`; CI resolves each: the issue exists and is closed by a commit in the range, each ADR file exists with status Accepted, each ledger key exists, and the storage format equals the constant in `Varve.Store`. ADR 0072's read-forever commitment is therefore a reviewed line, not a sentence in a report.
>    * `accepted-by: mailto:emil@okkels-klein.dk`, which the `land/` acceptance of the PR already proves; the validator checks it against `eng/identities.json`.
>    * `summary`: the release notes, written, reviewed in the PR.
>    * `prerelease` derived from the version, not declared.
>    * Immutable once cut: a version with a tag is skipped; corrections are a new version, which NuGet's immutability requires anyway.
> 2. Notes. The descriptor is the source. `CHANGELOG.md` becomes a projection of `releases/`: `changelog.cs --release` folds the descriptor in under its version heading, `--check` verifies the fold and fails on a hand edit. One place to write.
> 3. Tagging actor. The tag and the GitHub Release are created by `varve-gates` with an app token, never by `GITHUB_TOKEN` and never with a composite human-plus-tool identity. Reason one is the identity rules (ADR 0087 and `eng/identities.json`: the maintainer accepted, the app executed). Reason two is mechanical: a tag pushed with `GITHUB_TOKEN` does not trigger workflows, so the NuGet trusted-publishing job on tag push would never run; an app-token tag triggers it. Prove that in the dry run report.
> 4. Gates at the commit under release, which may differ from the tip when `commit` is set: the full CI job set, the AOT publishes, the public API baselines, `decision-sets.cs`, `dependency-register.cs`, `licence-headers.cs`. A valid descriptor whose gates fail is not cut and leaves no tag.
> 5. `release-pending` inverts. Today it reminds; now it gates: a PR that closes a milestone issue must carry a descriptor whose `basis` names that issue, and the check fails otherwise. Delete the reminder path.
> 6. Dry run via `workflow_dispatch` with `dry_run: true`: every gate runs, nothing is created, the step summary states what would have been tagged.
> 7. The miss their history records (v5.14.0, v5.15.1: the ratified head was not the merged head) cannot occur under ADR 0088's fast-forward-only path, because the merged head is the reviewed head. State that in the ADR as the reason the descriptor needs no head guard here, and name the guard as the first thing to add if ADR 0088 is ever relaxed.
>
> Deliverables, in the usual order: the ADR superseding 0085 (dated, alternatives: keep the manual tag; tag from the changelog heading; GitHub's release UI), `eng/release.cs` (validator plus the plan step, a C# file-based app like the other gates, with its failure paths proven by running them: bad version, reused version, unresolved basis, wrong storage format, hand-edited changelog), the release workflow, the `release-pending` and `changelog.cs` changes, `docs/releases.md` (format, rules, why the descriptor and not the tag), `AGENTS.md` updated (a milestone PR carries its descriptor), and the dry run executed on this PR's head and reported. No descriptor is added in this PR; the first is 7b's `v0.1.0-preview.2`.
> Non-goals: Docker image tagging (operability milestone), signing of tags beyond the app identity, anything in `tools/repo-standard/` (the standard adopts this later, adopter-neutral, once Varve has cut one release with it).
> Report: the traceability record and the PR body; the ADR number; the validator's failure-path proof; the dry run's step summary; what 7b must change in its close-out.

### The approval of the plan

The session's plan disagreed with point 7: under ADR 0088 session pull
requests merge by the merge button (#70, #71), and `agent review` failed any
`land/` range with an agent's commits, so the merged head is not the reviewed
head and a head guard is needed now. The maintainer's answer:

> Approved with these changes:
> 1. Head guard ships as you describe. Add to the ADR and docs/releases.md: a descriptor landed by merge commit is recovered by a retro-cut, commit: <approved PR head>, which is an ancestor of main and carries the check run; that is the one legitimate use of commit. release --check on a PR prints the landing instruction.
> 2. Landing: amend ADR 0087 rule 3 rather than pushing to main directly. An agent commit is admitted in a land/ range iff the range head is a PR head with a successful gates-app agent review. The maintainer pushes the approved head to land/<name>; the existing fast-forward does the rest. Non-release PRs unchanged for now.
> 3. GitHub Release after the NuGet push, app token. Yes.
> 4. Changelog pure projection, --unreleased to stdout, date required. Yes; date is the preparation date, documented.
> 5. Basis completeness at ADR level only: adr: lines complete both ways, no decision: lines; --draft prints keys as information. issue: and storage-format: as proposed.
> 6. release environment drops its reviewer, keeps the main and v* restriction. Yes.
> 7. I will grant the app Contents read & write before you reach the dry run, and push land/release-dry-run when you ask.

## The report

**ADR [0102](../adr/0102-a-release-is-a-descriptor.md)**, *A release is a
descriptor, and landing it cuts it*. It supersedes 0085 whole and 0088's
`HumanReviewGatesReleases`, and amends 0087 rule 3 (dated amendments in 0087
and 0088). Its decision set, `a-release-is-a-descriptor`, is filed without
`accepted-by`:
- ten keys;
- `EveryMilestoneEndsInARelease` (from 0085) and `HumanReviewGatesReleases`
  (from 0088) moved in, with their statements changed;
- 0085's other four keys not carried, and its set file removed;
- 0087's set gains `ApprovedHeadLandsThroughLand`, unaccepted.

Nothing in the code cites any of them, so the Release build is green, with no
`CS0618` at all.

### What was built

- **`eng/lib/Releases.cs`**: the descriptor reader, a strict YAML subset
  with no package, plus the format-1 shape rules, SemVer precedence and the
  `CHANGELOG.md` projection. Shared by the three scripts below.
- **`eng/release.cs`**:
  - `--check` (job `release` in `eng/ci.cs`): shape, projection, cut
    descriptors immutable, and each proposed descriptor resolved at the
    commit under release (version, `commit`, `issue`, `adr` complete both
    ways and Accepted, `storage-format`, `accepted-by`). It prints the
    landing instruction.
  - `--plan`: the same for the pending descriptor, one at a time, plus the
    issue's existence and the head guard online. It writes
    `GITHUB_OUTPUT` and `GITHUB_STEP_SUMMARY`.
  - `--draft`, `--notes`, and `--fixtures` (job `release-fixtures`).
- **`eng/changelog.cs`**: `CHANGELOG.md` is the projection of `releases/`.
  `--release` folds, `--check` compares byte for byte, and `--unreleased`
  prints the commit view. `releases/v0.1.0-preview.1.md` holds preview.1's
  section verbatim (identical to the tagged file's).
- **`eng/release-pending.cs`**, inverted: it fails a range that closes a
  milestone issue (from `docs/roadmap.md` at the base) without adding a
  descriptor whose basis names it. The reminder path is deleted.
- **`eng/agent-review.cs`**: on a `land/` push, an agent's commits are
  admitted when the head is a pull request's head carrying the gates App's
  successful `agent review`. The job gains `checks: read`, and
  `eng/agent-review-tamper.cs` gains three cases, all passing.
- **`.github/workflows/release.yml`**: plan, then gates (`ci.yml` called at
  the commit under release), then cut (the gates App's token from the `gates`
  environment, minted with `permission-contents: write`, and an annotated tag
  pushed). It triggers on a push to `main`, on a push to `land/release-**`
  (always a dry run), and on `workflow_dispatch`.
  - `ci.yml` is callable with a `ref` threaded into every checkout.
  - When called it skips the range gates (`dco`, `release pending`, and
    `issue-refs`/`release` inside the pipeline), the durability measurement
    and the decision report.
- **`publish.yml`**: the notes and the title come from the descriptor before
  anything is pushed. The GitHub Release is created after the NuGet push with
  the gates App's token, in the `gates` environment.
- **`.github/repo-standard.yaml`**:
  - the gates App (5220446) bypasses the `v*` tag ruleset;
  - `release` has no reviewer, and is restricted to `main` and `v*` tags;
  - `gates` admits `v*` tags.

  The maintainer's tag bypass stays, for repair; a tag made by hand
  publishes nothing, because `publish.yml` fails without a descriptor.
- **Docs**:
  - `docs/releases.md`: the format, the rules, how a release lands, the
    changelog, the dry run, and why the descriptor and not the tag;
  - `AGENTS.md`: a milestone PR carries its descriptor and lands at its
    approved head;
  - `GOVERNANCE.md`, `CONTRIBUTING.md`, the ADR index and the
    `changelog-sections.txt` header.

### The validator's failure paths

`dotnet run eng/release.cs -- --fixtures tests/fixtures/releases` on this
branch:

```
ok   bad-version: fails, saying 'version 'v0.1.0-preview.02' is not v<SemVer 2.0.0>'
ok   hand-edited-changelog: fails, saying 'CHANGELOG.md is not the projection of'
ok   reused-version: fails, saying 'v0.1.0-preview.1 is tagged already; versions are never reused'
ok   unresolved-basis: fails, saying '999999 is not closed by a commit in'; 'ADR 0999 does not exist at'
ok   wrong-storage-format: fails, saying 'storage format 99 is not Varve.Store's, 1'
ok  every one of 5 failing release fixture(s) fails, for its own reason
```

`release pending`'s failure path was run in a scratch worktree (never
pushed). A commit saying `Closes #11` with no descriptor gives:

```
FAIL: #11 is a milestone issue (docs/roadmap.md), and 0f81cdc closes it, but this change adds no
      releases/<version>.yaml whose basis names it ('  - issue: 11').
```

That run exited 1. With `releases/v0.1.0-preview.2.yaml` naming `issue: 11`
added, it exited 0.

The pipeline jobs that changed all pass locally: `register`,
`licence-headers`, `decision-sets`, `banned-symbols`, `issue-refs`, `dco`,
`dco-fixture`, `required-checks`, `required-checks-fixture`,
`agent-review-tamper`, `release-pending`, `changelog`, `release` and
`release-fixtures`. `dotnet build Varve.slnx -c Release` reported 0 warnings
and 0 errors.

### The dry run on this branch's head

**Locally**, offline: `eng/release.cs --plan --dry-run --fallback
tests/fixtures/releases/dry-run --offline`, on head `fc0e925`. Its step
summary:

```
### Release plan (dry run: nothing is created)

`releases/` has nothing pending, so the dry run plans `tests/fixtures/releases/dry-run` instead.

| Version | `v0.1.0-preview.2` (prerelease) |
| Descriptor | `tests/fixtures/releases/dry-run/v0.1.0-preview.2.yaml` |
| Commit under release | `fc0e925f4ab091595c72ebb85fe91973fcf40266` |
| Previous release | `v0.1.0-preview.1` |
| Tag message | v0.1.0-preview.2 — Dry run of the release path |
| Basis | #73; 13 ADR(s); storage format 1 |
| Accepted by | mailto:emil@okkels-klein.dk |

- offline: the issue's existence and the head guard were **not** checked

**Not cut: 1 problem(s).** No tag is created.

- tests/fixtures/releases/dry-run/v0.1.0-preview.2.yaml:23: ADR 0102 is not Accepted at fc0e925f4ab0 (0102-a-release-is-a-descriptor.md)
```

It blocks on exactly one thing, the unaccepted ADR, which is correct. With
the acceptance simulated in a scratch commit (never pushed), it plans
`count=1`, `version=v0.1.0-preview.2`,
`message=v0.1.0-preview.2 — Dry run of the release path`,
`prerelease=true` and `previous=v0.1.0-preview.1`. The summary ends:
"`v0.1.0-preview.2` would be tagged on `95910048d9f3` once every gate passes
at that commit."

**On GitHub** the dry run needs the maintainer's push of this head to
`land/release-dry-run`. `workflow_dispatch` only reaches a workflow that is
on `main`, and only `main` and `land/**` reach the `gates` environment. That
run is reported on the pull request when it has happened.

### What 7b must change in its close-out

1. Add `releases/v0.1.0-preview.2.yaml`, starting from `dotnet run
   eng/release.cs -- --draft v0.1.0-preview.2`. It needs:
   - `issue: 11`;
   - the `adr:` lines it prints (0090 to 0102 and 7b's own);
   - `storage-format: 1`;
   - a written summary.
2. Say `Closes #11` in a commit, or `release pending` is red.
3. Run `dotnet run eng/changelog.cs -- --release v0.1.0-preview.2` and commit
   `CHANGELOG.md`.
4. Remove `tests/fixtures/releases/dry-run/`, or bump it to the version after
   7b's, since it goes stale once preview.2 is cut.
5. Land by fast-forward at the approved head (`land/<name>`, then `git push
   origin <head>:main`), not by the merge button. A merge commit is not cut,
   and is recovered by a retro-cut.

### Recorded for later

- `eng/decision-report/baseline.md` still names `HumanReviewGatesReleases`
  with 0088's statement. The report never gates and its baseline is
  regenerated by the maintainer.
- The `release` environment's reviewer and the App's Contents permission are
  live settings. The first is applied by repo-standard on merge; the second
  is the maintainer's grant.
