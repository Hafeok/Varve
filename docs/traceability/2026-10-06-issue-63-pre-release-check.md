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

## The fourth prompt

> DCO: add eng/dco.cs, a file-based check that every commit in the PR range (base..head) carries a Signed-off-by trailer whose name and email match the commit's author, with the same configurable exempt list issue-refs.cs will get; run it in ci.yml as its own job and in ci.cs; prove it with a failing fixture. Add the job name to the required checks list in .github/repo-standard.yaml so ruleset 1 gates on it. Amend the stewardship ADR's dated notes: enforcement existed only on paper until this commit. In how-we-work#1 the line becomes a script to port, not one to write. Same PR as #64 if still open; otherwise a follow-up before the tag.

The session reported two conflicts and asked. First, ADR 0034 point 3 has
session commits authored by the session and signed off by the human, so a
strict author match fails all 11 of #64's commits. Second, a required check on
ruleset 1, which has no bypass, refuses every direct push, which ADR 0032's
amendment says needs a superseding ADR. The maintainer's answers, verbatim:

> Extend the identity map: human entries may carry delegates: (identities allowed to act for them), editable only by the human's own PR. Rules: a decision first filed in a commit authored by an agent identity may be accepted only by that identity's responsible human or a delegate (decision-sets.cs); a PR containing agent-authored commits requires an approving review from the responsible human or a delegate (new check on pull_request_review, added to required checks via repo-standard.yaml). ADR: the map is the interim form of the ledger's authority model and is replaced by ledger grants when the generator reads the ledger.
>
> Supersede ADR 0032 (dated, new number): main accepts only commits whose SHA already has passing required checks; humans push to a short-lived branch, let CI run, then fast-forward main to that SHA or merge a review-free PR; cloud sessions unchanged. Ruleset 1 keeps all gates including dco with no bypass. Record whether ruleset 1 already refused unverified direct pushes for the existing checks or whether the earlier description was wrong. Update AGENTS.md, CONTRIBUTING.md and GOVERNANCE.md; repo-standard.yaml adds dco to the required checks.

## The fourth report

- **No identity map existed**, so "extend" became "create":
  `eng/identities.json` holds humans (emails, login, delegates), agents
  (emails, responsible human) and the exempt list; the loader shared by the
  gates is `eng/lib/Identities.cs`. `eng/issue-refs.cs` now reads its exempt
  list from it.
- **The worker restart changed the session's git identity** to
  `Claude (Emil) <claude+emil@okkels-klein.dk>`, set by the environment.
  The map lists that email under the agent `claude-code`, with `emil`
  responsible.
- **`eng/dco.cs`**:
  - a human's commit needs a sign-off matching its author, name exactly and
    email ignoring case;
  - an agent's commit needs one by its responsible human or a delegate;
  - exempt automation is skipped.

  The failing fixture (`tests/fixtures/dco/`, five cases) exits 1; the passing
  one (six cases, including a merge and an unknown author) exits 0. The
  `dco-fixture` job in `eng/ci.cs` and a step in the `dco` job require both.
- **`eng/decision-sets.cs`**: a decision filed by an agent's commit is
  accepted only by its responsible human or a delegate. Proven in a scratch
  clone: an acceptance by a stranger gives exactly one finding; the
  maintainer's acceptance passes.
- **`eng/agent-review.cs`** and `.github/workflows/agent-review.yml`, job
  `agent review`:
  - approval on the current head;
  - approvers and delegate ownership judged from the base's map;
  - exit 2 when the reviews cannot be read.

  Proven against a local stand-in for the reviews API: approved on the head
  passes; a stale approval, a stranger's approval, and a delegate change by a
  non-owner fail.
- **ADR 0088 supersedes 0032.** It records that **ruleset 1 never refused a
  direct push for a failing check**: its required list was empty from the
  first export. 0032's description was wrong, and its "DCO" was not a check
  at all.
- **ADR 0034** gets a dated amendment: point 3 was enforced only on paper until
  `eng/dco.cs`.
- **`repo-standard.yaml`** requires 14 checks by job name. It leaves out the
  durability jobs (they measure) and the decision report (it never gates). The
  schema does not validate keys inside rule parameters, so the names are only
  as right as their match against `ci.yml`.
- **Blocker found, not solved**: GitHub refuses a review by a pull request's
  author, and session pull requests are opened under the maintainer's account.
  So `agent review` cannot pass on them until the map names a delegate, or
  session pull requests come from another account (ADR 0087, Consequences).

## Acceptance

The maintainer accepted all 21 decisions of 0085–0088 in 4c7e911 and set
0085's Status to Accepted. Asked to update the index and the Status lines,
the session marked 0085–0088 accepted in the index, recorded 0085's
`Proposed` interval, and brought 0086–0088's Status lines up to date.

`dco` fails on 4c7e911. Its author is `Emil Klein -  Claude Code AI
<emil@okkels-klein.dk>` and its sign-off `Emil Okkels Klein
<emil@okkels-klein.dk>`, so the names differ and ADR 0087's human rule
refuses it. The session does not rewrite the maintainer's commit; how it is
resolved is the maintainer's choice.

## The fifth prompt

> Merge #64 with agent review red; the ruleset applies on merge. Next PR: the agent review check accepts, for a PR opened under the responsible human's own GitHub account, a comment review by that human whose body contains "approve <head sha>"; approving reviews remain required for PRs opened by anyone else; old-head and stranger cases keep failing. ADR 0087 gains the exit: approval becomes a signed ledger Review over the head SHA by an Approve holder when the ledger gates ship. Add a test that every required check name in repo-standard.yaml is a job name in ci.yml. I accept 0086–0088 on the branch and do the first land/ fast-forward myself.

## The fifth report

- **#64 merged** as 4f2230c with `agent review` red, as instructed. The
  `Repository settings` workflow applied the declaration on that push and
  succeeded, so ruleset 1 now requires the 14 checks.
- **`agent review`**: on a pull request opened under the responsible human's
  login, a comment review by that human containing `approve <sha>` counts as
  approval, if the sha is the head in full or at least seven characters.
  Tested against a local stand-in for the reviews API:
  - own pull request with the full sha, or seven characters of it: pass;
  - an earlier head's sha, six characters, or the comment by a stranger: fail;
  - a pull request by someone else with the human's comment: fail;
  - with an approving review: pass.
- **`eng/required-checks.cs`** checks every required name against the job
  names every workflow reports, matrix-expanded, and against pull-request
  triggers.
  - Every workflow and not `ci.yml` alone, because `agent review` lives in
    `agent-review.yml`; reading only `ci.yml` would fail it.
  - The live declaration passes, which also confirms the 14 names now in force.
  - The fixture fails on a name outside the matrix and on one reported only by
    a workflow that does not run on pull requests.
- **ADR 0087** amended (the comment form; the exit to a signed ledger
  `Review`), and **ADR 0088** amended (the required-checks gate). Their three
  new keys are filed unaccepted.
- **Session error, recovered:** a `git reset --hard` used to clean up a
  throwaway test commit also discarded the uncommitted edit to
  `eng/agent-review.cs`. Nothing had been pushed. The edit was reapplied,
  committed, and the eight cases re-run on top of it.
- **Found, not fixed:** `agent-review.yml` runs the pull request's own
  `eng/agent-review.cs`, so a pull request can change the gate that judges it.
  Running the base's script would deadlock this pull request, which needs its
  own new rule to pass. It is a follow-up once this rule is on `main`.

## The sixth prompt

> Follow-up PR after #66: agent-review.yml becomes pull_request_target, checking out the base ref's eng/agent-review.cs only, never building or running PR content, reading head SHA and reviews via the API; header comment states the hazard and the rule. Minimum SHA prefix in the comment form becomes 12 characters. ADR 0087 dated amendment records that the judging script is always main's. Test: a PR that edits agent-review.cs to always pass still fails under main's copy.

The session raised three problems and asked:
- landing a `pull_request_target`-only workflow deadlocks, because main has
  no such trigger yet;
- `pull_request_review` runs take their workflow from the pull request;
- required checks are matched by name.

The maintainer's answers, verbatim:

> Land in two PRs. PR 1: keep pull_request, add pull_request_target (base-ref script only, API reads only) and a push trigger for land/** that passes when the range has no agent-authored commits and fails otherwise; the check run "agent review" is created or updated on the head SHA on all three paths. PR 2, after merge: remove pull_request. Test the land/ path with the first fast-forward.
>
> Trusted triggers only: pull_request_target, issue_comment (PR comments, created), push on land/**. Approval is a PR conversation comment "approve <sha ≥12>" by a holder of Approve for every agent identity in the PR, resolved via the API from the comment's author; review submissions are not consulted; pull_request_review and pull_request_review_comment are not triggers. Same check run "agent review" updated on the head SHA from all three paths. ADR 0087 amendment notes the change and that the ledger form replaces it.
>
> This PR: the agent review check run is created and updated with an installation token of a dedicated gates GitHub App (checks: write only), minted from an environment restricted to main, so no PR-defined workflow can report it; ruleset 1 requires "agent review" from that app; required-checks.cs records the app pinning; ADR 0087 amendment records that check names are spoofable and only app-pinned checks count. If repo-standard cannot declare the app on a required check, add that first.

And, mid-turn:

> Amend ADR 0087 (dated): approval by PR comment is not a valid workflow; it is tolerated only until the ledger review gate exists and is removed in the PR that adopts it. File the decision so the ledger project can cite it.

## The sixth report

- **`eng/agent-review.cs`**, rewritten. It reads the pull request (head,
  commits, head identity map, conversation comments) and, for a `land/`
  push, the compare range, through the API only.
  - Approval: `approve <sha>`, at least 12 characters, by a holder of Approve
    for each agent, judged against main's map.
  - On the trusted paths it posts the check run `agent review` with the gates
    App's token, updating that App's run on the head if one exists.
  - The JSON request bodies use `JsonObject`, because file-based apps are
    AOT-analysed and reflection serialisation is refused.
- **`agent-review.yml`**:
  - the `judge` job runs on `pull_request_target`, `issue_comment` (a PR
    comment containing "approve") and `land/**` pushes;
  - it checks out main by name, mints the token in the `gates` environment
    and posts the check;
  - a transitional `bootstrap` job, named `agent review`, runs on
    `pull_request` with the base's script, and PR 2 removes it.
- **`eng/agent-review-tamper.cs`**, a job of `eng/ci.cs`, 17 parts:
  - the tampered copy passes on its own;
  - main's copy posts failure for it;
  - the approval cases: 12 characters pass; 11, an earlier head, a stranger
    and a review submission alone fail;
  - the `issue_comment` and `land/` paths;
  - the workflow structure.

  Its failure path was proven by pointing the judge's checkout at the pull
  request head, which fails two parts.
- **`repo-standard.yaml`**: the `gates` environment, with branch policies
  `main` and `land/**`, and the ruleset `land`, which restricts `land/**` to
  the maintainer.
  - **The departure from the instruction:** the environment also admits
    `land/**`, not `main` alone. Otherwise the `land/` path could never post
    the check, and no human landing could pass.
  - The integration id of the App is not known, so the pin is not declared.
    Without a pin, `eng/required-checks.cs` fails on purpose.
- **`eng/required-checks.cs`**: a pinned name must be posted by the gates App
  from a `pull_request_target` workflow, and a name the App posts must be
  pinned. Its fixture gained one case of each.
- **ADR 0087**, second amendment of 2026-10-06, with four new rulings, among
  them `CommentApprovalIsNotAValidWorkflow` for the ledger project to cite.
  - `OwnPullRequestApprovedByComment` is revoked.
  - `AgentPullRequestsApprovedOnTheHead` has its statement changed and its
    acceptance withdrawn.
- **Still name-spoofable:** the 13 other required checks. The amendment says
  so, rather than claiming more than holds.

## The seventh prompt

> Gates app created and installed on Varve; environment gates exists, restricted to main, with GATES_APP_CLIENT_ID and GATES_APP_PRIVATE_KEY. App ID: 5220446. Pin "agent review" to it in repo-standard.yaml and take #68 out of draft. Also: the repo-standard app's secrets now live in a main-restricted environment named gates; make repo-apply use it, in #68 or a follow-up, whichever is cleaner.

## The seventh report

- **`agent review` is pinned to App 5220446** in `repo-standard.yaml`.
  `eng/required-checks.cs` passes: 14 required, 1 pinned.
- **The repo-standard App's secrets: in #68, not a follow-up.** Merging #68
  makes `repo-standard.yml` apply the declaration, and that run must mint the
  repo-standard App's token. With the secrets only in `gates`, it can do so
  only if the workflow names the environment. So #68 makes three changes
  together:
  - the job runs with `environment: gates`;
  - repository-level `secrets` is `[]`;
  - the `gates` environment declares all four secrets.

  repo-standard reports a secret mismatch in either direction, so the lists
  must match where the secrets actually are.
- **Unchanged from the earlier report:** the declaration gives `gates` the
  branch policies `main` and `land/**`, so applying #68 adds `land/**` to the
  environment, which was created with `main` only.

## The eighth prompt

The second half of the sixth prompt's plan:

> PR 2, after merge: remove pull_request. Test the land/ path with the first fast-forward.

## The eighth report

- **#68's settings apply on main** (run 37587597100):
  - it applied the `land` ruleset, the pinned `trunk` ruleset and the `gates`
    environment;
  - it ended red only on secrets, which repo-standard never writes. The
    `REPO_STANDARD_APP_*` secrets are still at repository level, not in
    `gates`;
  - its token step worked only because an environment job also sees
    repository secrets;
  - the maintainer's hand move of the secrets is what converges it.
- **`agent-review.yml`** loses the `pull_request` trigger and the transitional
  `bootstrap` job. `eng/agent-review.cs` loses its `pull_request` mode, so a
  failing verdict is always a posted check, never an exit code.
- **`eng/agent-review-tamper.cs`** has two new parts, 19 in all: no
  `pull_request` trigger, and no job named `agent review`. Both fail against
  main's workflow before this change and pass after it.
- **ADR 0087** gets a dated amendment recording the removal. The amendment
  also restores the `Alternatives considered` heading, which the second
  amendment's commit dropped.
- **The land/ path** is tested by the maintainer's first fast-forward, not by
  this session.
