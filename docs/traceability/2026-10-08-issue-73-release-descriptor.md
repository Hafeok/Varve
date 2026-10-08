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

### During the work: item 10

> One addition, item 10: project status is a checked projection. README.md ships inside every nupkg (eng/package-metadata.cs), and preview.1 shipped a README two milestones stale ("Milestone 5c", "Nothing is published yet", 2,803 cases against 2,927). Add eng/status.cs --check, run in ci.cs and in release --plan at the commit under release:
> - README's "## Status" opening line names the latest release (git tag or the descriptor being cut) and the milestone it closed.
> - The conformance table total equals the line count of baseline/passing.txt.
> - The package table lists every packable project with its declared layer (the CompilerVisibleProperty), and no "not built" row names a project that exists.
> - docs/roadmap.md: every milestone whose issue is closed (by a Closes trailer in history, as release-pending resolves it) has "(complete)" in its heading; none that is open does.
> Failure paths as fixtures like the others. In the same PR, bring README and the roadmap headings current to the merged state (milestones 6 and 7a, preview.1 published, 2,927), so the check passes on main. AGENTS.md's close-out list gains README and the roadmap heading.

The session found that no milestone issue had ever been closed by a trailer:
- #4, #5, #6 and #7 were closed by hand;
- #12 was closed by accident, by prose quoting a closing keyword (cd0217e);
- #8 was open although the roadmap marked milestone 4 complete.

It asked which source of "closed" to use. The maintainer's answer:

> Source of "complete" is releases/: a heading says (complete) iff a descriptor whose tag exists names its issue in basis, and must say it then. Backfill releases/v0.1.0-preview.1.yaml at commit a9f0c24 with basis #4–#9 and storage-format: 1, summary from the changelog section; the existing tag means the cut skips it. The cut job closes the issues its basis names with the app token. release-pending keeps the Closes trailer as the signal that forces a descriptor into the PR. issue-refs.cs refuses a closing keyword plus #N outside a trailer line. I reopen #12 by hand.

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

One effect is expected in that run. The `land/` push also runs `agent
review` with **main's** script, from before the amendment, which fails a
`land/` range with an agent's commits. It updates the App's run on the same
sha to failure, so:
- the plan's head guard probably reports that failure;
- the dry run, which goes on past findings, still runs every gate and mints
  and shows the tagging identity;
- the pull request's own `agent review` is restored by commenting `approve
  <sha>` again.

From the merge of this change on, main's script admits an approved head, and
the effect is gone.

### What 7b must change in its close-out

0. Bring `README.md`'s Status opening line to name `v0.1.0-preview.2` and
   milestone 7, and mark the roadmap's `## 7` heading *(complete)*; the plan
   refuses the cut otherwise.
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

### Item 10, as built

- **`eng/status.cs`** with **`eng/lib/Status.cs`** checks four statements:
  - the Status opening line names the newest descriptor and the last
    milestone it closed;
  - the conformance total is the baseline's line count;
  - every packable project has a row with its `<ArchLayer>`, and no
    "not built" row names an existing project;
  - a roadmap heading says *(complete)* exactly when its issues are in a
    release's basis.

  It runs as `status` and `status-fixtures` in `eng/ci.cs`, and in
  `eng/release.cs --plan` on the tree of the commit under release.
- **One departure from the answer, and why.** "Released" counts every
  descriptor in `releases/`, the pending one included, not only those with a
  tag. Counting only tagged ones would make `main` red at every cut: the plan
  needs the heading unmarked before the tag exists, and status on `main`
  needs it marked after. The pending descriptor is the one being cut, so the
  files say *complete* at the tagged commit itself.
- **Several issues per basis.** A basis now has at least one `issue:` line
  rather than exactly one. **`releases/v0.1.0-preview.1.yaml`** records
  preview.1:
  - `commit: a9f0c24`;
  - issues #4 to #9, and `storage-format: 1`;
  - its changelog section as the summary.

  A descriptor whose tag exists and whose `commit` is the tag's commit is a
  record: its shape and storage format are checked, and it is never cut. The
  `.md` notes form is gone.
- **The cut closes the basis's issues** with the App's token, minted with
  `permission-issues: write` beside `permission-contents: write`. The App
  needs **Issues: read & write** as well as Contents.
- **`eng/issue-refs.cs`** refuses a closing keyword with a number anywhere
  but a trailer line of its own.
- **README** is current:
  - "`v0.1.0-preview.1` is the latest release, and it closed milestones 1 to
    5; milestones 6a, 6c and 7a are on `main` since";
  - the package table gains `Varve.Store.Browser` (5), `Varve.Protocol` (5)
    and `Varve.Server` (6, a host, not packed), and its "not built" row is
    "SHACL, CLI";
  - the conformance table gains the four protocol suites, totalling 2,927 of
    2,939 with twelve exemptions;
  - "Nothing is published yet" now says preview.1 is on nuget.org, verified
    against the nuget.org flat container.
- **The roadmap** headings 3 and 5 gain *(complete)*; 1, 2 and 4 keep it.
  Milestone 6 (#10) is not complete: 6b is not built and no release names
  it.

**Failure paths:**

```
$ dotnet run eng/status.cs -- --fixtures tests/fixtures/status
ok   not-built-exists: fails, saying 'is not built, and Varve.Server exists'
ok   package-table: fails, saying 'Varve.Protocol, packable at layer 5, has no row'; 'gives Varve.Iri layer '1', and its project declares 0'
ok   roadmap-headings: fails, saying ''## 5 — SPARQL' must say *(complete)*'; ''## 6 — Durable storage *(complete)*' says *(complete)*, and #10 is in no release's basis'
ok   stale-status-line: fails, saying 'does not name the latest release, v0.1.0-preview.1'; 'does not name milestone 5 (#9)'
ok   wrong-total: fails, saying 'the conformance total is 2, and'
ok  every one of 5 failing status fixture(s) fails, for its own reason
```

The same fixture base with no flaw reports nothing, so each case fails for
its own reason.

Run against `main`'s README before this change, the check reports eight
findings: both parts of the opening line, 2,803 against 2,927, the two
missing packages, the "server" not-built row, and headings 3 and 5.

`issue-refs` over the 2026-09-22 range refuses the two commits that quoted a
closing keyword in prose, among them cd0217e, which closed #12:

```
FAIL: 2 closing keyword(s) outside a trailer line. …
  1cfad663  subject line, and illustrated it with a quoted "Closes #12" where the number
  cd0217e7  "Closes #12" as an entire commit message does not satisfy a rule about bodies.
```

**For the maintainer, from item 10:**
- grant the App Issues: read & write;
- reopen #12;
- close #8 and #9, or leave them: preview.1's record names them, and the
  cut skips a record, so nothing closes them for you.

### Recorded for later

- `eng/decision-report/baseline.md` still names `HumanReviewGatesReleases`
  with 0088's statement. The report never gates and its baseline is
  regenerated by the maintainer.
- The `release` environment's reviewer and the App's Contents permission are
  live settings. The first is applied by repo-standard on merge; the second
  is the maintainer's grant.
