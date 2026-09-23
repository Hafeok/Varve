# repo-standard: repository settings as code, CLI and Action

| | |
|---|---|
| **Issue** | [#23](https://github.com/Hafeok/Varve/issues/23) |
| **Date** | 2026-09-23 |
| **Tool** | Claude Code (cloud session, `claude.ai/code`) |
| **Model** | Claude Opus 5.5 (`claude-opus-5-5`) |
| **Branch** | `claude/epic-feynman-6deidm` |
| **ADR** | [0039](../adr/0039-repo-standard.md) |

Contemporaneous. The plan was presented and approved before any file was
written; the maintainer confirmed its two open choices (YAML through
YamlDotNet's parser rather than its static context, and
`--allow-status-reset`).

## The prompt

> Build `repo-standard`: a CLI that applies a declared set of GitHub repository
> settings, and a GitHub Action that runs it. Two artefacts. Anyone can use them
> with their own repository and their own token; nothing assumes the adopter is
> us or is this repository.
>
> It is built here, in Varve, for convenience, and will move to its own home
> later. So: it lives under `tools/repo-standard/` with its own solution,
> references nothing in `src/`, and nothing in `src/` references it. It is a
> Varve project for the purposes of the repository's rules: `VarveLayer` none,
> the Varve analyzers as a reference, warnings as errors, every package in the
> dependency register with an ADR (one ADR for the tool covers its
> dependencies), licence headers, issue references, traceability record. It is
> not published from this repository: no NuGet package and no release assets;
> the Action in section 2 is written so that it works once the tool has a home,
> and until then the workflow in section 3 builds it from source.
>
> Plan first, wait for approval. Conventional commits, every commit references an
> issue, DCO sign-off, trunk-based; `AGENTS.md` applies.
>
> **1. The CLI.** .NET 10, C# latest, one project plus tests, buildable as a
> `dotnet tool` and as a Native AOT single-file binary (the AOT build is a CI
> gate; publishing the artefacts is not in scope here). Managed code only,
> minimal dependencies: `HttpClient` and source-generated `System.Text.Json` for
> the GitHub REST and GraphQL APIs; YAML via `YamlDotNet` with its static
> context, or JSON if that fails under AOT (say which). No Octokit.
>
> Commands, all taking `--repo owner/name`, `--file repo-standard.yaml` and a
> token from `--token` or `GITHUB_TOKEN`: `export` (read the live settings, write
> them as a declaration); `plan` (diff declaration against live, print
> create/update/delete per resource, change nothing); `apply` (converge; read,
> compare, write only what differs; log each change; stop on the first failed
> write with the remaining plan printed); `check` (like `plan`, but any
> difference in either direction, including something added by hand that the
> declaration does not name, exits non-zero with a report on stdout and, when
> present, `GITHUB_STEP_SUMMARY`).
>
> Declaration, YAML, one file per repository, optional `extends: <path or URL>`
> for a shared base that the repository's file overrides. Resources: repository
> settings (description, topics, default branch, merge methods, features,
> security options such as private vulnerability reporting and Dependabot),
> rulesets (branch and tag, in the JSON shape GitHub's ruleset export uses,
> including required checks and bypass actors), environments (reviewers, wait
> timer, branch and tag policies), secret names (asserted to exist, values never
> read or written), labels, Discussions categories, Projects v2 boards linked to
> the repository with their status columns, Actions permissions. State precisely
> what the Projects API cannot do (for example a WIP limit) and leave those out
> rather than half-supporting them.
>
> `tools/repo-standard/baselines/mom.yaml` ships with the tool: the Mind Over
> Machine way of working as a declaration, extractable by running `export`
> against a repository set up to it. A repository adopts it with `extends` and
> its own overrides.
>
> The tool never logs the token and never writes it to a file. Retries GitHub
> secondary rate limits per GitHub's guidance.
>
> Tests: the diff engine over recorded live state, one fixture per resource kind,
> including the added-by-hand case; contract tests against recorded HTTP
> exchanges for every endpoint used; a round-trip property (`apply` then `export`
> then `plan` shows no diff) over generated declarations. An integration test
> against a scratch repository exists and is skipped unless a token is present;
> it is not run in this session.
>
> **2. The Action.** `tools/repo-standard/action.yml`, a composite action, written
> for its future home: inputs `command` (`plan`, `apply`, `check`), `file`
> (default `.github/repo-standard.yaml`), `repo` (default the current
> repository), `token` (required), `version`. It downloads the AOT binary for the
> runner's platform from a release, verifies its checksum, and runs it; no
> `dotnet` on the runner required. Until the tool has a home with releases, the
> action is exercised by a test that runs it against a locally built binary, and
> Varve's own workflow (section 3) does not use it.
>
> The README shows an adopter's workflow: on push to the declaration run
> `apply`; weekly run `check` and open or update an issue titled "Configuration
> drift" when it fails. It shows the two ways to supply `token`: a GitHub App the
> adopter creates (permission list given; token minted with
> `actions/create-github-app-token`) or a fine-grained PAT, with the trade-off in
> two sentences. It says why the workflow's own `GITHUB_TOKEN` cannot do this: no
> Administration permission, by design.
>
> **3. Varve's own workflow.** `.github/workflows/repo-standard.yml` in this
> repository: builds the tool from source, then on push to
> `.github/repo-standard.yaml` runs `apply`, and weekly runs `check` and opens or
> updates an issue titled "Configuration drift" when it fails. The token comes
> from `actions/create-github-app-token` (pinned by SHA) with app id and key read
> from repository secrets whose names are stated in the workflow; the maintainer
> adds the secrets. The workflow is added disabled-safe: until
> `.github/repo-standard.yaml` exists it does nothing, so this session commits no
> declaration and changes no setting.
>
> **Not in this session:** running the tool against any repository, this one
> included. Publishing it. Organisation-level settings. Secret values. A GUI.
>
> **Report:** what was built, the declaration format as shipped, what `export`
> cannot read and what Projects v2 cannot express, dependencies with versions
> and the ADR for each, the recorded-fixture coverage per endpoint, the exact
> commands for the maintainer's first `export` and `check` against this
> repository from a local machine with `gh auth token`, and what must be true
> before the workflow's first `apply` runs.

## The report

### What was built

| Where | What |
|---|---|
| `tools/repo-standard/src/RepoStandard` | The CLI: `export`, `plan`, `apply`, `check`. One project, `net10.0`, packs as the `dotnet` tool `repo-standard`, publishes as a Native AOT single file (7.9 MB on linux-x64). |
| `tools/repo-standard/tests/RepoStandard.Tests` | 87 tests: 86 pass, and the integration test skips without a token. |
| `tools/repo-standard/action.yml` | The composite Action. |
| `tools/repo-standard/tests/action/replay-server.cs` | A recorded GitHub over HTTP, for testing the Action. |
| `tools/repo-standard/baselines/mom.yaml` | The Mind Over Machine baseline. |
| `tools/repo-standard/docs/declaration.md` | The declaration's specification. |
| `tools/repo-standard/README.md` | The adopter's documentation: commands, the declaration, the Action, the token, the limits. |
| `docs/adr/0039-repo-standard.md` | The decision, the isolation, the dependency. |
| `eng/ci.cs`, `.github/workflows/ci.yml` | The tool's build and tests as jobs; a job that publishes the AOT binary and runs the Action against it. |
| `.github/workflows/repo-standard.yml` | Varve's own workflow, inert until `.github/repo-standard.yaml` exists. |

Nothing was run against any repository. No declaration was committed; no
setting was changed.

### Decisions taken inside the plan, and where it moved

- **YAML through YamlDotNet's parser and emitter, not its static context**
  (confirmed by the maintainer). A ruleset is GitHub's export shape, which a
  typed model would have to own, so YAML is read as a `JsonNode` tree. Measured
  before it was relied on: the Native AOT publish with ILC warnings as errors is
  clean, and the published binary parses and validates `mom.yaml`. JSON
  declarations, the planned fallback, were not needed.
  **Source-generated `System.Text.Json`** is used for the fixed-shape body (the
  GraphQL request); the variable-shape payloads are `JsonNode`, which is
  AOT-safe without it. The trimming analyzer caught `JsonArray.Add<T>` wherever
  C# picked the generic overload for a `JsonObject`, and those sites use a
  node-typed helper.
- **`actions/create-github-app-token` is v3.2.0**
  (`bcd2ba49218906704ab6c1aa796996da409d3eb1`, a lightweight tag, so the commit
  itself), not the v2.2.2 the plan named: v3 is the current release. v3
  deprecates `app-id` for `client-id`, so the workflow reads the App's **client
  ID**, from `REPO_STANDARD_APP_CLIENT_ID`. The prompt said "app id"; this is the
  non-deprecated equivalent.
- **The Action gained three things beyond the prompt's inputs**:
  - `api-url` and `graphql-url` (defaulting to the run's own URLs) — needed for
    GitHub Enterprise Server, and what lets the Action test point it at
    localhost;
  - `download-url`, for mirrors and for that test;
  - an `exit-code` output, so a workflow can open a drift issue on 1 (drift)
    and not on 2 (could not run).
- **Composite actions cannot see their own repository or ref**
  (`github.action_repository` is empty inside one), so the Action reads both
  from `github.action_path`. Pinned by SHA, it needs `version`.
- **`VARVE0001`/`VARVE0002` are inert on this tool**: they apply to `Varve.*`
  assemblies. The analyzer is referenced and runs; the ADR says so rather than
  renaming the tool into their scope.
- **`mom.yaml` is hand-written in export's shape**, from the standard and
  Varve's `GOVERNANCE.md`, because nothing was run against a repository.

### Found while building it

- **`export` leaked the token into the file it wrote.** If GitHub's error
  message ever contains the token, export quoted it into the "could not be read"
  header, which was not filtered. The token test found it, using a server that
  echoes the Authorization header. Fixed.
- **Paging followed any `Link`.** The client now refuses a next page on another
  scheme or host, since the token rides on every request. Found when the replay
  server's recorded `Link` pointed at `api.github.com`.
- **The mutation check on the round-trip property**: removing the set-ordering
  of environment reviewers made it fail within 150 iterations, so the property
  has teeth.

### The declaration as shipped

`tools/repo-standard/docs/declaration.md` is normative; in brief:

- **Top-level kinds:** `repository`, `labels`, `rulesets`, `environments`,
  `secrets`, `discussions`, `projects`, `actions`, and optionally `extends`.
- **What is managed:** a kind left out is not managed. A kind present is
  managed exhaustively, so anything added by hand is drift. Inside an object, a
  field left out is not managed. Lists that are sets compare without order; a
  board's Status options compare in order.
- **`extends`:** a path or an `https` URL, fetched with no credentials.
  - Mappings merge key by key, and `null` unmanages a key.
  - Keyed lists (labels, rulesets, environments by name; projects by title;
    categories by name) merge by key, with the overriding item replacing the
    base item whole; `absent: true` removes an item.
  - Any other list or scalar is replaced.
- **Variables:** `${owner}` and `${repo}` interpolate; `$${` is a literal `${`.
- **Validation:** unknown keys are errors, and GitHub's cross-field rules are
  checked before any request.
- **Exit codes:** 0 clean, 1 differences or a failed write, 2 could not run.

### What `export` cannot read, and what Projects v2 cannot express

**Not readable, so not in a declaration:**
- secret values (by design);
- anything at organisation level, including inherited rulesets;
- branch protection of the pre-ruleset kind, webhooks, deploy keys,
  collaborators and team access, autolinks, Pages, custom properties, code
  security configurations, Dependabot secrets, Actions variables;
- a Discussions category's format beyond `answerable`, and its section;
- whether Dependabot alerts are on, with a token that may not ask: GitHub
  answers 404 for both "off" and "not allowed to know".

**Readable but not writable:** Discussions categories. No REST or GraphQL
mutation creates, edits or deletes one; checked against the published GraphQL
schema.

**Projects v2 cannot express, so these are left out:**
- views: layout, grouping, sorting, filters, visible fields;
- column limits, **including the standard's WIP limit of 3**;
- built-in workflows (only deletable);
- insights and iteration fields;
- items.

Rewriting Status options replaces them without ids (`updateProjectV2Field`
takes no option id), which clears Status on items. So it needs
`--allow-status-reset` on a board with items. An unlinked board is linked, not
rewritten blind; a board linked but not declared is unlinked, never deleted.

### Dependencies

| Package | Version | Class | ADR |
|---|---|---|---|
| `YamlDotNet` | 18.1.0 (MIT, no dependencies) | runtime of the tool | [0039](../adr/0039-repo-standard.md) |
| `xunit.v3` | 4.0.1 | test | 0009 (already registered) |
| `Microsoft.Testing.Extensions.TrxReport` | 2.4.1 | test | 0007 (already registered) |
| `CsCheck` | 4.9.1 | test | 0025 (already registered) |

`eng/dependency-register.cs` passes: 14 packages, each citing its ADR.

### Recorded-fixture coverage per endpoint

The recordings are **GitHub's published OpenAPI examples**
(`github/rest-api-description@02e8fa6`) and, for GraphQL, responses constructed
to the published schema. They are **not live captures**; the fixtures' README
says which is which and how a capture replaces one.

| Endpoint | Scenarios |
|---|---|
| `GET /repos/{owner}/{repo}` | repository-check, repository-apply-enable, repository-apply-disable, export-all, action-check-clean |
| `PATCH /repos/{owner}/{repo}` | repository-apply-enable |
| `GET /repos/{owner}/{repo}/topics` | repository-check, repository-apply-*, export-all, action-check-clean |
| `PUT /repos/{owner}/{repo}/topics` | repository-apply-enable |
| `GET /repos/{owner}/{repo}/private-vulnerability-reporting` | repository-check, repository-apply-*, export-all, action-check-clean |
| `PUT …/private-vulnerability-reporting` | repository-apply-enable |
| `DELETE …/private-vulnerability-reporting` | repository-apply-disable |
| `GET /repos/{owner}/{repo}/vulnerability-alerts` | repository-check (204), repository-apply-enable (404), repository-apply-disable, export-all, action-check-clean |
| `PUT …/vulnerability-alerts` | repository-apply-enable |
| `DELETE …/vulnerability-alerts` | repository-apply-disable |
| `GET /repos/{owner}/{repo}/automated-security-fixes` | repository-check, repository-apply-*, export-all, action-check-clean |
| `PUT …/automated-security-fixes` | repository-apply-enable |
| `DELETE …/automated-security-fixes` | repository-apply-disable |
| `GET /repos/{owner}/{repo}/rulesets` | rulesets-check, rulesets-apply, export-all, action-check-clean |
| `GET /repos/{owner}/{repo}/rulesets/{ruleset_id}` | rulesets-check, rulesets-apply, export-all, action-check-clean |
| `POST /repos/{owner}/{repo}/rulesets` | rulesets-apply |
| `PUT /repos/{owner}/{repo}/rulesets/{ruleset_id}` | rulesets-apply |
| `DELETE /repos/{owner}/{repo}/rulesets/{ruleset_id}` | rulesets-apply |
| `GET /repos/{owner}/{repo}/environments` | environments-check, environments-apply, environments-apply-update, export-all, action-check-clean |
| `PUT …/environments/{environment_name}` | environments-apply, environments-apply-update |
| `DELETE …/environments/{environment_name}` | environments-apply |
| `GET …/environments/{environment_name}/deployment-branch-policies` | environments-check, environments-apply, environments-apply-update, export-all, action-check-clean |
| `POST …/deployment-branch-policies` | environments-apply |
| `DELETE …/deployment-branch-policies/{branch_policy_id}` | environments-apply-update |
| `GET …/environments/{environment_name}/secrets` | environments-check, environments-apply, environments-apply-update, export-all, action-check-clean |
| `GET /users/{username}` | environments-apply-update |
| `GET /orgs/{org}/teams/{team_slug}` | environments-apply, environments-apply-update |
| `GET /repos/{owner}/{repo}/actions/secrets` | secrets-check, secrets-apply, rate-limit-check (a 403 secondary limit, then 200), export-all, action-check-clean |
| `GET /repos/{owner}/{repo}/labels` | labels-check and labels-apply (two pages over a `Link` header), apply-stops-at-failure, export-all, action-check-clean |
| `POST /repos/{owner}/{repo}/labels` | labels-apply, apply-stops-at-failure (a 422) |
| `PATCH /repos/{owner}/{repo}/labels/{name}` | labels-apply |
| `DELETE /repos/{owner}/{repo}/labels/{name}` | labels-apply |
| `GET /repos/{owner}/{repo}/actions/permissions` | actions-check, actions-apply, export-all, action-check-clean |
| `PUT /repos/{owner}/{repo}/actions/permissions` | actions-apply |
| `GET …/actions/permissions/selected-actions` | actions-check, actions-apply, export-all, action-check-clean |
| `PUT …/actions/permissions/selected-actions` | actions-apply |
| `GET …/actions/permissions/workflow` | actions-check, actions-apply, export-all, action-check-clean |
| `PUT …/actions/permissions/workflow` | actions-apply |
| GraphQL `DiscussionCategories` | discussions-check, export-all |
| GraphQL `LinkedProjects` | projects-check, projects-apply-refused, projects-apply-reset, projects-apply-create, export-all, action-check-clean |
| GraphQL `OwnerProjects` | projects-apply-create |
| GraphQL `CreateProject` | projects-apply-create |
| GraphQL `UpdateProject` | projects-apply-create |
| GraphQL `LinkProject` | projects-apply-create |
| GraphQL `UnlinkProject` | projects-apply-reset |
| GraphQL `UpdateStatusField` | projects-apply-create, projects-apply-reset |
| GraphQL `CreateStatusField` | projects-apply-create |

`Every_endpoint_and_operation_the_tool_uses_has_a_recorded_exchange` enforces
this table against the code's own catalogue.

### The maintainer's first `export` and `check`, from a local machine

From a clone of this repository at the branch's head, with the .NET SDK that
`global.json` names and the GitHub CLI logged in as the owner:

```bash
gh auth refresh --scopes read:project       # boards are read through GraphQL; without it, projects are left out with a warning
export GITHUB_TOKEN="$(gh auth token)"

dotnet build tools/repo-standard/src/RepoStandard --configuration Release

dotnet run --project tools/repo-standard/src/RepoStandard --configuration Release --no-build -- \
  export --repo Hafeok/Varve --file .github/repo-standard.yaml

dotnet run --project tools/repo-standard/src/RepoStandard --configuration Release --no-build -- \
  check --repo Hafeok/Varve --file .github/repo-standard.yaml
```

The `check` straight after an `export` should print "No differences". That is
the round-trip property on real data, and the first evidence against GitHub
itself rather than its documentation. **Do not commit the file yet**: committing
it to `main` is what triggers the workflow's first `apply`.

### What must be true before the workflow's first `apply`

1. **A GitHub App exists and is installed on `Hafeok/Varve`**, with repository
   permissions Administration read and write, Actions read, Environments read,
   Secrets read, Issues read and write, Discussions read (Metadata read is
   automatic).
2. **The repository has the secrets** `REPO_STANDARD_APP_CLIENT_ID` (the App's
   client ID) and `REPO_STANDARD_APP_PRIVATE_KEY` (a private key for it).
3. **`projects` is removed from the declaration.** `Hafeok/Varve` is owned by a
   user, and the boards "Varve roadmap" and "Varve work" presumably by the same
   user. A GitHub App's installation token cannot reach a user-owned Projects v2
   board. The workflow's App token would not see them, and `check` and `apply`
   would report them missing or fail. Keep them in a local declaration run with
   `gh auth token` if you want them checked. The alternative is to move the
   boards to an organisation and grant the App organisation Projects
   permission.
4. **The export was taken after the secrets were added.** Otherwise
   `secrets:` will not name the two new ones, and the first `check` reports
   them as added by hand.
5. **The file is reviewed as the intended standard, not only the current
   state.** The export captures what is; if anything is not as it should be,
   the first `apply` would make it permanent. In particular:
   - the rulesets' bypass list: ruleset 2's bypass for the Claude GitHub App
     under ADR 0034 comes through as an `Integration` actor id;
   - the `release` environment's reviewer;
   - the Discussions categories, which the workflow can only report on, never
     fix.
6. **`plan`, run locally with the App's permissions in mind, shows no
   differences** other than those intended. The first `apply` then writes
   nothing, or only what the maintainer chose.
7. Optionally, ruleset 1's required checks gain `repo-standard native and
   action (ubuntu-latest)` and `(windows-latest)`. If they do, the declaration's
   `trunk` ruleset must list them too, or `check` reports the difference.

## Commits

- `docs(adr): 0039, repo-standard, and the declaration it reads`
- `feat(repo-standard): export, plan, apply and check`
- `test(repo-standard): recorded exchanges, the round trip, and a live test`
- `feat(repo-standard): the Action, and a replayed GitHub to test it against`
- `ci: repo-standard's build, tests, native binary and Action`
- `ci: keep this repository's settings to a declaration, once there is one`
- `docs(traceability): the repo-standard session, and the state it leaves`
