# Adopting the Mind Over Machine stewardship standard

| | |
|---|---|
| **Issues** | [#16](https://github.com/Hafeok/Varve/issues/16), [#21](https://github.com/Hafeok/Varve/issues/21) |
| **Date** | 2026-09-22 |
| **Tool** | Claude Code (cloud sandbox) |
| **Model** | Claude Opus 5 |
| **Branch** | `chore/mom-stewardship`, merged to `main` |
| **Pull request** | [#22](https://github.com/Hafeok/Varve/pull/22) |

This is the first record filed under the rule it introduces
([ADR 0033](../adr/0033-commit-traceability.md)), and unlike its neighbours it
is contemporaneous rather than reconstructed.

## The prompt

> Varve adopts the Mind Over Machine open-source stewardship standard, as
> written at <https://github.com/mindovermachine-dev>, in full. Read that
> profile README first, then <https://docs.mindovermachine.dk> and the
> `how-we-work` repository in that organisation. Where the organisation
> publishes a template or a file (governance, contribution guide, devcontainer
> skill, board layout), mirror it rather than paraphrase it, adapting only for
> .NET. Where it publishes none, write ours to the standard's wording.
>
> The GitHub side is already in place: two rulesets on `main` (ruleset 1:
> required status checks, no force push, no deletion, no bypass; ruleset 2:
> required signed commits, with the Claude GitHub App on the bypass list), a tag
> ruleset on `v*` restricted to the maintainer with signed tags, the `release`
> environment with the maintainer as required reviewer, Discussions, and the two
> project boards "Varve roadmap" and "Varve work". Do not change any of these;
> document them. Milestone 3b (PR #3) is merged and this session works from that
> `main`.
>
> Four of the standard's items reverse decisions we made earlier. Apply them as
> written; the earlier decisions are superseded, not softened.
>
> Plan mode: present the plan, wait for approval. Work on a branch is no longer
> required (see A1), but this session still works on `chore/mom-stewardship` and
> lands it with one squash-free merge, because it changes the rules the trunk
> runs under.
>
> **Part A, the four reversals.** A1 — trunk-based development, pull requests
> optional, reviews non-blocking; supersede the "everything through a pull
> request" rule from the 3a close-out. A2 — copyleft: relicense to MPL-2.0,
> superseding ADR 0002, with a per-file header enforced by a file-based C# check
> in `eng/`, and the alternatives recorded (Apache-2.0, LGPL-3.0, AGPL-3.0). A3
> — full traceability: every commit on `main` references a tracked issue,
> enforced by a CI check and proven on this branch; one issue per milestone and
> per ADR set; a `docs/traceability/` directory with one file per AI-assisted
> session; `CLAUDE.md` becomes a pointer to a vendor-neutral `AGENTS.md`. A4 —
> signed commits, with cloud AI sessions as a stated deviation through a ruleset
> bypass, written as an ADR, plus one experiment run and reported without acting
> on it: whether the sandbox can create a commit through the GitHub GraphQL
> `createCommitOnBranch` mutation.
>
> **Part B, the rest of the standard.** Work through the checklist item by item;
> the report states, per item, "met by", "added in this session", or "planned in
> milestone N". Semantic versioning; containerized development built on the
> `how-we-work` pattern with a .NET overlay; containerized pipelines runnable
> locally as one file-based C# entry point `eng/ci.cs` that CI calls inside the
> devcontainer image, with `core.autocrlf=false` on every checkout;
> Detroit-style testing stated in `CONTRIBUTING.md`; managed test data; highly
> componentized; open project management with the two boards and a WIP limit of
> 3; dedicated channels; modern distribution; agentic AI ready with `AGENTS.md`
> under 150 lines.
>
> **Part C, the authentication ADR.** Add the attached ADR with the next number,
> Accepted. Roadmap: milestone 7 gains authentication; a new "Operability"
> milestone between 7 and 8; a "1.0 definition" section with its exclusions.
> Never list: no API keys, no credential scheme other than OIDC bearer tokens.
>
> **Part D, the standard contributor files.** README, CONTRIBUTING, GOVERNANCE,
> SECURITY, CODE_OF_CONDUCT, issue templates with blank issues disabled, PR
> template, `CHANGELOG.md` in Keep a Changelog format from conventional commits
> via a file-based C# script backfilled for milestones 1 to 3a, Dependabot
> config with the register policy amendment implemented in
> `eng/dependency-register.cs` and both paths proven, and an OpenSSF Scorecard
> workflow pinned by SHA.
>
> **Non-goals.** No production code. No docs site. No spec changes.
>
> **Final report.** Filed as the traceability record for this session and as the
> body of the merge. Per checklist item: met by, added, or planned. The ADRs
> added and superseded with numbers. Every workflow and gate added, each proven
> by a failing run. Confirmation that every commit in the session is signed,
> signed off and references an issue. What the maintainer still has to do by
> hand.

The attached `adr-server-authentication.md` is ADR 0037, added with only its
heading and number-assignment line changed.

## The report

### Sources read

The organisation's profile README
(`mindovermachine-dev/.github/profile/README.md`) carries the fourteen-item
checklist verbatim and is the text this report is written against.
`how-we-work` supplied the `init-takt-devcontainer` skill, its
`docs/architecture.md` layering model (core, then one stack overlay, then an
optional profile overlay) and its `templates/` tree.
`docs.mindovermachine.dk` supplied the methodology pages.

**The organisation publishes no template for the governance files.** I checked
`CONTRIBUTING.md`, `GOVERNANCE.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md` and
`SUPPORT.md` in both `mindovermachine-dev/.github` and `how-we-work`; all ten
are 404. Those files are therefore written to the standard's wording rather
than mirrored, as the prompt directs. The issue templates *do* mirror something
real: the Problem / Solution / Value / Deliverables / Acceptance criteria shape
of `standard-mom.md`.

### The checklist, item by item

| # | Item | Status |
|---:|---|---|
| 1 | **Semantic Versioning** | **Added** — [ADR 0035](../adr/0035-semantic-versioning.md). 0029 already produced SemVer-shaped numbers from the tag; what was missing was what the number *promises* and what decides which part moves. The public API baselines, already gating the build under `RS0016`, are the evidence: a line leaving `PublicAPI.Shipped.txt` is a breaking change and the diff says so. The conformance ratchet is the other half, because the baselines cover shape and not behaviour. |
| 2 | **Containerized Development** | **Added** — `.devcontainer/`, mirroring the core skill's base and features, with a .NET overlay pinned to `global.json`'s exact SDK. [ADR 0036](../adr/0036-containerised-development.md). |
| 3 | **Containerized Pipelines** | **Added** — `eng/ci.cs`, twelve jobs, called by `ci.yml` inside the devcontainer image. Local and CI are the same file in the same image. |
| 4 | **Trunk-Based Development** | **Added** — [ADR 0032](../adr/0032-trunk-based-development.md) supersedes the pull-request-only rule. |
| 5 | **Non-Blocking Reviews** | **Added** — same ADR. The blocking review is the automated one; human review moved to the `release` environment, which `publish.yml` now actually references. |
| 6 | **Full Traceability** | **Added** — [ADR 0033](../adr/0033-commit-traceability.md), `eng/issue-refs.cs`, eighteen issues, and this directory. |
| 7 | **Agentic AI Ready** | **Added** — `AGENTS.md`, 149 lines; `CLAUDE.md` is one line pointing at it. |
| 8 | **Cryptographic Security** | **Added, with a stated deviation** — [ADR 0034](../adr/0034-commit-signing-and-the-sandbox-exception.md). See the finding below. |
| 9 | **Detroit-Style Testing** | **Met by** the existing suites; **stated** in `CONTRIBUTING.md` for the first time. The words mock, fake and stub appear nowhere in this repository, and ADR 0018 already said "the memory backend is a real backend, not a test double". Adding the first double now needs an argument. |
| 10 | **Managed Test Data** | **Met by** the W3C submodule pinned by commit and the generated fixtures under version control; **stated** as a rule in `CONTRIBUTING.md` — nothing is fetched at test time, and `SubmoduleGuardTests` pins the case count so a suite cannot pass by being empty. |
| 11 | **Highly Componentized** | **Met by** the layer rule ([ADR 0003](../adr/0003-package-layering.md)), enforced by `VARVE0001`/`VARVE0002`, and the one-package-one-reason rule. Three individually publishable packages today; the roadmap adds more per milestone. |
| 12 | **Open Project Management** | **Partly added.** The flow, the two boards' roles and the WIP limit of 3 are documented in `GOVERNANCE.md`, and the eighteen issues exist. **Populating the boards is blocked from this session** — see below. |
| 13 | **Dedicated Channels** | **Partly added.** `SECURITY.md`, the issue templates with blank issues disabled, and the assignment rule (assigned before work starts, unassigned after 14 days idle) are in. Discussions and its categories are the maintainer's to create. |
| 14 | **Modern Distribution** | **Met by** NuGet, already configured (ADR 0029). **Planned**: the server as a container image and the CLI as a `dotnet tool` in the **Operability** milestone; `winget` and Homebrew in the **1.0 definition**. Both are now written into `docs/roadmap.md`. |

### ADRs

**Added: 0031–0037.**

| # | Title | Note |
|---:|---|---|
| [0031](../adr/0031-licence-mpl-2-0.md) | Licence: MPL-2.0 | **Supersedes 0002.** Records the departure from the brief's constraint 6. |
| [0032](../adr/0032-trunk-based-development.md) | Trunk-based development and non-blocking review | Supersedes the 3a close-out's pull-request rule |
| [0033](../adr/0033-commit-traceability.md) | Commit traceability and AI-session records | Narrows the earlier wording rule to prose |
| [0034](../adr/0034-commit-signing-and-the-sandbox-exception.md) | Commit signing and the sandbox exception | Carries a revisit condition |
| [0035](../adr/0035-semantic-versioning.md) | Semantic versioning | Complements 0029; supersedes nothing |
| [0036](../adr/0036-containerised-development.md) | Containerised development and the local pipeline | |
| [0037](../adr/0037-server-authentication.md) | Authentication and authorisation for the server | The attached text, numbered on merge |

**Superseded: 0002** (Licence: Apache-2.0), by 0031. Its text is unedited; the
supersession is recorded in `docs/adr/README.md`, as 0006's and 0020's are.

**Amended: 0009**, with a dated amendment for the Dependabot register policy.
An amendment rather than a successor because the decision stands and gains
detail, which is what `CONTRIBUTING.md` says an amendment is for.

### The finding that changes A4

**A4's premise is false in this environment, and it was measured rather than
assumed.**

The sandbox *does* hold a signing key and *does* sign — every commit carries an
ed25519 SSH signature, the merged milestone 3b commits included. What fails is
on GitHub's side:

```
GET /repos/Hafeok/Varve/commits/7e4589e → commit.verification
  verified : false
  reason   : "unknown_key"
  signature: -----BEGIN SSH SIGNATURE----- …
```

The problem is **registration, not capability**. The obvious fix — register the
key on the maintainer's account — is recorded in ADR 0034 as a **rejected**
alternative: the key is platform-provided, not rotatable by us, and reused
across sessions, so registering it would let any such session produce commits
GitHub attests as the maintainer's. A verified badge meaning "some machine
somewhere held the shared key" is worse than an honest absence of one.

The decision is unchanged. Its stated reason is now the true one.

### The two experiments, run and reported without being acted on

**1. GraphQL `createCommitOnBranch`** — the mutation GitHub signs with its own
key. **Cannot be attempted at all**: this session's egress proxy refuses every
GraphQL request, mutation and query alike. That is a sandbox limit rather than
a GitHub one, and it could lift without notice.

**2. The REST contents API** — the same idea by a route that *is* open, and the
obvious next guess. **The hypothesis was wrong.** A probe commit created
through `PUT /repos/.../contents/...` came back:

```
verified : false
reason   : "unsigned"
signature: NONE
```

GitHub's automatic signing covers commits made through the web interface, not
every commit made through the API by a token. Recorded in ADR 0034 so that the
next person does not re-run it. Neither route is a candidate today; the ADR's
revisit condition names both anyway, because either could become one.

### Gates and workflows added, each proven by a failing run

`docs/testing.md` §5: a gate that has never failed is a gate nobody has tested.
Every one below was run against a deliberate failure before it was trusted.

| Gate | Failure path proven | Pass path |
|---|---|---|
| `eng/licence-headers.cs` | Before the headers went in: `FAIL: 131 of 131`, exit 1. Then against `tests/fixtures/licence-header/`: `FAIL: 1 of 2`, exit 1, naming `WithoutNotice.cs` | `ok  134 .cs files carry the MPL-2.0 notice`, exit 0 |
| `eng/issue-refs.cs` | Against real history — `46bcc3b..7e4589e`, a commit from before the rule: `FAIL: 1 of 1`, exit 1. Merge exemption proven over `7ca2804..2e703c8`: 3 merges skipped, 11 checked | `ok  8 commit(s) reference an issue`, exit 0 |
| `eng/dependency-register.cs --base` | Major bump with the ADR untouched, and a new package citing an untouched ADR: both exit 1, both naming the ADR | Patch and minor bumps pass; a major bump with the cited ADR changed passes. **Five paths, all five run** |
| `eng/ci.cs` | A source file stripped of its notice: stops at `licence-headers`, prints the table with that row bold, "later jobs did not run", exit 1. An unknown `--only` name: exit 2 | 12 of 12 green locally in ~75 s |
| `eng/changelog.cs --check` | A hand-edited `CHANGELOG.md`: `FAIL: CHANGELOG.md is stale`, exit 1 | `ok  CHANGELOG.md is up to date`, exit 0 |
| `ci.yml` `pipeline (devcontainer)` | **Failed on its first real run**, and the defect was real — see below | Green on `81138cf` |
| `scorecard.yml` | Not yet run; it triggers on push to `main`, `branch_protection_rule` and weekly | — |

**The pipeline job earned its place on the first push.** It failed because
`dotnet workload install` needs elevation inside the container — the default
user is not root and the SDK lives under `/usr/share/dotnet`. That defect was
**not findable in this session**: there is no Docker here, so the image could
never be built locally, and ADR 0036 says exactly that — the CI job is the
proof and a local run is not. One push later the proof did its job.

**The claim it exists to support is now evidenced.** On `81138cf` all eight
checks are green, and the `pipeline (devcontainer)` job printed this from
inside the image:

```
| register        | pass |  3.1 |   | test-iri     | pass |  2.9 |
| licence-headers | pass |  2.8 |   | test-rdf     | pass |  3.3 |
| issue-refs      | pass |  2.2 |   | test-turtle  | pass |  8.2 |
| restore         | pass |  1.7 |   | conformance  | pass | 11.8 |
| native-assets   | pass |  3.1 |   | pack         | pass |  3.6 |
| build           | pass | 10.2 |
| test-analyzers  | pass | 18.6 |
```

The same twelve jobs, in the same order, that `dotnet run eng/ci.cs` printed
locally — which is the whole of what "local and CI are the same code in the
same container" was asked to mean. Inside that run: `ok  134 .cs files carry
the MPL-2.0 notice`, and `Baseline: 883 test(s). Exempt: 0. Newly passing: 0.
Regressed: 0. Missing from the run: 0.`

Both conformance legs are green after `core.autocrlf=false`, which is the other
half of ADR 0036 and the one that had no local proof either.

`dependabot.yml` is configuration rather than a gate, and is arranged to match
ADR 0009's amendment: patch and minor grouped into one weekly pull request that
passes the register gate, majors alone so they arrive red with the gate naming
what they need. The two Roslyn packages are ignored, because
`Directory.Packages.props` explains at length that 5.0.0 is a floor rather than
a version to keep current.

### Commits

All nine commits on this branch are **signed**, **signed off**, and
**reference an issue** — verified per commit and confirmed by the gate itself.

**One precision about "signed".** Each commit carries a valid SSH signature.
GitHub reports them as unverified with `reason: "unknown_key"`, for the reason
ADR 0034 records, and ruleset 2's bypass is what lets them reach `main`. Saying
"signed" without that sentence would be the kind of claim this repository's
rules exist to prevent.

### What was not done, and why

- **No production code, no docs site, no spec changes** — the stated
  non-goals, all observed. The conformance baseline is untouched at 883 of 883
  with `exemptions.txt` still empty.
- **The project boards are not populated.** GitHub Projects v2 is a
  GraphQL-only API and GraphQL is refused at this session's proxy; there is no
  REST route. The token carries the `project` scope, so this is an environment
  limit and not a permissions one. The board layout, the upstream/downstream
  flow and the WIP limit are documented in `GOVERNANCE.md`, and the eighteen
  issues that belong on them exist.
- **Discussion categories were not created.** They are a repository setting.
- **The `experiment/api-signed-commit` branch could not be deleted.** The push
  proxy refuses ref deletions and the REST delete returns 403. It holds one
  file and one commit.

### What the maintainer still has to do by hand

1. **Update the nuget.org trusted-publishing policy to name the `release`
   environment**, before the first `v*` tag. The policy was registered against
   this workflow with *no* environment; adding `environment: release` changes
   the OIDC claim it is matched against, and the push step will fail
   authentication in a way that looks like a bad credential. A note sits beside
   the change in `publish.yml`. **Nothing is published yet, so nothing is
   broken today.**
2. **Add `pipeline (devcontainer)` to ruleset 1's required-checks list**, or the
   containerised pipeline gates nothing.
3. **Populate the two boards** — *Varve roadmap* with issues #4–#15 in
   milestone order, *Varve work* as a Kanban with the WIP limit of 3.
4. **Create the Discussions categories**: Announcements, Q&A, Ideas, Show and
   tell.
5. **Attach the held transcripts** to `topic-log-and-projection-model.md` and
   `topic-adr-positions.md`, and divide the backfilled milestone records into
   real sessions if the transcripts support it. The git history cannot: all 69
   non-merge commits on `main` carry the same session identifier.
6. **Delete the `experiment/api-signed-commit` branch**, and the stale
   `milestone/3a`, `milestone/3b` and `fix/3a-closeout` refs if you want them
   gone — ADR 0032 makes them artefacts of a superseded rule.
7. **Decide on `publish_results` in `scorecard.yml`.** It is off. Turning it on
   publishes an assessment of this repository to the public OpenSSF API and
   enables a badge; that is an outward-facing publication and was left as the
   maintainer's call.
8. **Enable private vulnerability reporting**, which `SECURITY.md` names as the
   preferred channel.
9. **Consider contributing the .NET overlay back** to
   `mindovermachine-dev/how-we-work` as a `takt-stack-dotnet` overlay. It is
   written in that style deliberately.

### Two smaller findings

**`CLAUDE.md` carried a stale fact.** It said the log and projection model was
version 1; it has been 1.1 since the cipher changed. `AGENTS.md` says 1.1. The
attached authentication ADR had it right.

**The licence-header gate documented itself into being generated code.** Its
banner quoted the marker the toolchains write, the compiler looks for exactly
that in a file's leading comment, and the first build rejected the gate's own
nullable annotations with CS8669. The marker is now spelled out once, in the
method that uses it, with a comment saying why it is not spelled out above.

## What this record does not contain

The full turn-by-turn transcript, which the maintainer holds. The prompt above
is the maintainer's own text; the parts abridged in it are the four parts'
sub-items, which are reproduced in full in issue
[#16](https://github.com/Hafeok/Varve/issues/16).
