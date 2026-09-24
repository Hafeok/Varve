# repo-standard

Declare a GitHub repository's settings in one YAML file, and keep the
repository matching it.

```
repo-standard export --repo OWNER/NAME --file repo-standard.yaml   # capture what is there
repo-standard plan   --repo OWNER/NAME --file repo-standard.yaml   # what apply would change
repo-standard apply  --repo OWNER/NAME --file repo-standard.yaml   # change it
repo-standard check  --repo OWNER/NAME --file repo-standard.yaml   # fail on any drift
```

It manages repository settings (description, topics, default branch, features,
merge methods, sign-off, security options), branch and tag rulesets in the shape
GitHub's ruleset export uses, deployment environments, the names of Actions
secrets, labels, Discussions categories, the Projects v2 boards linked to the
repository, and Actions permissions. It is a single native binary, a `dotnet`
tool, and a GitHub Action. It needs your repository and your token, and
assumes nothing else about who you are.

- [Commands](#commands)
- [The declaration](#the-declaration)
- [The Action](#the-action)
- [The token](#the-token)
- [What it cannot do](#what-it-cannot-do)
- [Building it](#building-it)

## Commands

Every command takes `--repo OWNER/NAME` (default `GITHUB_REPOSITORY`),
`--file PATH` (default `repo-standard.yaml`) and a token from `GITHUB_TOKEN`
or `--token`. Prefer the variable: a command-line argument is visible to other
processes on the machine.

| Command | Does | Exit |
|---|---|---|
| `export` | Reads every kind of resource and writes it as a declaration. How an existing repository is captured without retyping it. Refuses to overwrite a file without `--force`. What it could not read is left out and named in the file's header. | 0, or 2 |
| `plan` | Reads what the declaration manages, compares, and prints each create (`+`), update (`~`), delete (`-`) and difference it cannot write (`!`). Changes nothing. | 0, or 2 |
| `apply` | Plans, then writes only what differs, logging each write. Stops at the first write that fails and prints the plan it did not apply. | 0 converged; 1 a write failed, or a `!` difference remains; 2 |
| `check` | Plans, and exits 1 on any difference in either direction — including something added by hand that the declaration does not name. Prints the report, and appends it as Markdown to `GITHUB_STEP_SUMMARY` when that is set. | 0 no drift; 1 drift; 2 |

Exit 2 is "could not run": a usage error, an invalid declaration, a read that
failed. `--api-url` and `--graphql-url` (default `GITHUB_API_URL` and
`GITHUB_GRAPHQL_URL`) point it at GitHub Enterprise Server.

`apply --allow-status-reset` permits rewriting the Status options of a Projects
board that has items; see [Projects](#projects-v2).

## The declaration

One file per repository. Every top-level key is a kind of resource, and **a kind
that is left out is not managed at all**. A kind that is present is managed
exhaustively: `labels: []` means "no labels", and a label someone adds by hand
is drift. Inside an object, a field left out is not managed.

```yaml
extends: https://example.org/standards/base.yaml   # optional; a path or an https URL

repository:
  description: A graph database for .NET.
  homepage: https://example.org
  topics: [rdf, dotnet]
  default_branch: main
  features: {issues: true, projects: true, wiki: false, discussions: true}
  merge:
    allow_merge_commit: true
    allow_squash_merge: false
    allow_rebase_merge: false
    allow_auto_merge: false
    allow_update_branch: true
    delete_branch_on_merge: true
    merge_commit_title: PR_TITLE          # PR_TITLE | MERGE_MESSAGE
    merge_commit_message: PR_BODY         # PR_BODY | PR_TITLE | BLANK
    squash_merge_commit_title: PR_TITLE   # PR_TITLE | COMMIT_OR_PR_TITLE
    squash_merge_commit_message: PR_BODY  # PR_BODY | COMMIT_MESSAGES | BLANK
  web_commit_signoff_required: true
  security:
    private_vulnerability_reporting: true
    dependabot_alerts: true
    dependabot_security_updates: true
    secret_scanning: true
    secret_scanning_push_protection: true

rulesets:                       # the repository's own; inherited ones are not read
  - name: trunk                 # the rest is GitHub's ruleset export shape
    target: branch              # branch | tag | push
    enforcement: active         # active | evaluate | disabled
    conditions:
      ref_name: {include: ["~DEFAULT_BRANCH"], exclude: []}
    rules:
      - type: deletion
      - type: non_fast_forward
      - type: required_status_checks
        parameters:
          strict_required_status_checks_policy: false
          do_not_enforce_on_create: false
          required_status_checks:
            - context: build
    bypass_actors:
      - {actor_id: 5, actor_type: RepositoryRole, bypass_mode: always}

environments:
  - name: release
    wait_timer: 0                # minutes
    reviewers:
      - {type: User, login: octocat}
      - {type: Team, slug: maintainers}   # a team of the repository's owner
    prevent_self_review: true    # only with reviewers; GitHub keeps it in their rule
    deployment_branch_policy:    # or null: any branch may deploy
      protected_branches: false
      custom_branch_policies: true
    branch_policies:
      - {name: "v*", type: tag}  # type defaults to branch
    secrets: [SIGNING_KEY]       # names only, asserted to exist

secrets: [NUGET_API_KEY]         # the repository's Actions secrets, names only

labels:
  - {name: bug, color: "d73a4a", description: Something isn't working}

discussions:
  categories:
    - {name: Q&A, emoji: ":pray:", description: Ask for help, answerable: true}

projects:                        # Projects v2 boards linked to the repository
  - title: ${repo} work
    owner: octo-org              # defaults to the repository's owner
    short_description: What is in flight
    readme: ""
    public: true
    closed: false
    status:                      # the Status field's options: the board's columns
      - {name: Todo, color: GRAY, description: ""}
      - {name: In progress, color: YELLOW}
      - {name: Done, color: PURPLE}

actions:
  enabled: true
  allowed_actions: selected      # all | local_only | selected
  sha_pinning_required: true
  selected_actions:              # only with allowed_actions: selected
    github_owned_allowed: true
    verified_allowed: false
    patterns_allowed: ["docker/*"]
  workflow:
    default_workflow_permissions: read   # read | write
    can_approve_pull_request_reviews: false
```

`docs/declaration.md` is the full specification: every key, how each kind is
compared and written, and exactly what each field maps to in GitHub's API.

**Validation is strict about names.** A key the schema does not know is an
error, not a setting that is quietly ignored, because a misspelt key would
otherwise be something nobody manages while its author believes it is managed.

**`extends` and overrides.** A base is merged under the repository's file:
mappings merge key by key; a key set to `null` stops managing it; the keyed
lists (`labels`, `rulesets`, `environments`, `projects`,
`discussions.categories`) merge item by item on their name or title, an
overriding item replacing the base item whole, and an item with `absent: true`
removing it; every other list, and every scalar, is replaced. A base fetched
from a URL is fetched with no credentials, and only over https.

**`${owner}` and `${repo}`** in any string are the repository being converged,
so one base serves many repositories. `$${` is a literal `${`; any other
`${...}` is an error.

### The Mind Over Machine baseline

`baselines/mom.yaml` is the Mind Over Machine way of working as a declaration:
trunk rules, signed commits, release tags, a `release` environment,
Discussions, an upstream roadmap board and a downstream work board. Adopt it and
override what is yours:

```yaml
extends: <path or URL to>/baselines/mom.yaml
repository:
  description: What this repository is.
rulesets:
  - name: trunk                 # replaces the baseline's trunk ruleset, whole
    target: branch
    enforcement: active
    conditions: {ref_name: {include: ["~DEFAULT_BRANCH"], exclude: []}}
    rules:
      - type: deletion
      - type: non_fast_forward
      - type: required_status_checks
        parameters:
          strict_required_status_checks_policy: false
          do_not_enforce_on_create: false
          required_status_checks: [{context: build}]
    bypass_actors: []
environments:
  - name: release
    reviewers: [{type: User, login: your-login}]
    deployment_branch_policy: {protected_branches: false, custom_branch_policies: true}
    branch_policies: [{name: "v*", type: tag}]
```

## The Action

`action.yml` beside this file is a composite action. It downloads the native
binary for the runner's platform from a release of the repository the action is
used from, verifies it against the release's `SHA256SUMS`, and runs it. No
`dotnet` is needed on the runner.

| Input | Default | |
|---|---|---|
| `command` | *(required)* | `plan`, `apply` or `check` |
| `file` | `.github/repo-standard.yaml` | the declaration |
| `repo` | the current repository | `OWNER/NAME` |
| `token` | *(required)* | see [The token](#the-token) |
| `version` | the ref the action is used at | a release tag, `v1.2.3`; required when the action is pinned by SHA |
| `api-url`, `graphql-url` | this run's | for GitHub Enterprise Server they are already right |
| `download-url` | the action repository's release | a base URL holding `repo-standard-<platform>` and `SHA256SUMS`, for mirrors and tests |

Platforms: `linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`. The output
`exit-code` is the tool's: 0, 1 for differences, 2 for could not run.

An adopter's workflow — `apply` whenever the declaration changes on the default
branch, and a weekly `check` that opens or updates an issue titled
"Configuration drift" when it finds any:

```yaml
name: Repository settings

on:
  push:
    branches: [main]
    paths: [.github/repo-standard.yaml]
  schedule:
    - cron: "17 6 * * 1"
  workflow_dispatch:

permissions:
  contents: read

jobs:
  apply:
    if: github.event_name == 'push'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@<sha> # v5
      - id: token
        uses: actions/create-github-app-token@<sha> # v3
        with:
          client-id: ${{ secrets.REPO_STANDARD_APP_CLIENT_ID }}
          private-key: ${{ secrets.REPO_STANDARD_APP_PRIVATE_KEY }}
      - uses: <owner>/repo-standard@<sha> # v1.0.0
        with:
          command: apply
          version: v1.0.0
          token: ${{ steps.token.outputs.token }}

  check:
    if: github.event_name != 'push'
    runs-on: ubuntu-latest
    permissions:
      contents: read
      issues: write          # the workflow's own token opens the drift issue
    steps:
      - uses: actions/checkout@<sha> # v5
      - id: token
        uses: actions/create-github-app-token@<sha> # v3
        with:
          client-id: ${{ secrets.REPO_STANDARD_APP_CLIENT_ID }}
          private-key: ${{ secrets.REPO_STANDARD_APP_PRIVATE_KEY }}
      - id: check
        continue-on-error: true
        uses: <owner>/repo-standard@<sha> # v1.0.0
        with:
          command: check
          version: v1.0.0
          token: ${{ steps.token.outputs.token }}
      - if: steps.check.outputs.exit-code == '1'   # 2 is "could not run", not drift
        env:
          GH_TOKEN: ${{ github.token }}
          RUN: ${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}
        run: |
          body="repo-standard check found drift. The report is in the run summary: $RUN"
          number=$(gh issue list --state open --search '"Configuration drift" in:title' \
            --json number,title --jq '.[] | select(.title == "Configuration drift") | .number' | head -n 1)
          if [ -n "$number" ]; then
            gh issue comment "$number" --body "$body"
          else
            gh issue create --title "Configuration drift" --body "$body"
          fi
```

## The token

**The workflow's own `GITHUB_TOKEN` cannot do this, by design.** GitHub never
grants it the Administration permission, which is what changing a
repository's settings, rulesets, environments and security options requires: a
workflow any contributor can edit must not be able to rewrite the rules that
protect the repository from that same workflow. So the token comes from one of
two places.

**A GitHub App you create** and install on the repository, with a token minted
per run by
[`actions/create-github-app-token`](https://github.com/actions/create-github-app-token).
Store its client ID and a private key as repository secrets. Repository permissions:

| Permission | Access | For |
|---|---|---|
| Administration | Read and write | settings, topics, security options, rulesets, environments, Actions permissions |
| Metadata | Read | the repository, its topics and rulesets (granted to every App) |
| Actions | Read | listing environments and their branch policies |
| Environments | Read | environment secret names |
| Secrets | Read | repository secret names |
| Issues | Read and write | labels |
| Discussions | Read | Discussions categories |

Organisation permissions, only if the declaration needs them: **Members:
read**, to resolve a team named as an environment reviewer; **Projects: read
and write**, for boards owned by the organisation. An App's installation token
cannot reach a board owned by a *user*: for that, use a PAT.

**A fine-grained personal access token**, with the same repository
permissions, stored as a secret. An App's token is short-lived, minted per run
and acts as the App, so nothing long-lived leaks and the audit log names the
automation; a PAT is simpler to set up but lives until it expires and acts as
you, with your access.

For a local run, `GITHUB_TOKEN=$(gh auth token)` uses your GitHub CLI login.

**repo-standard never logs the token and never writes it to a file.** It is
placed on the Authorization header of requests to the API and nowhere else,
never sent when fetching an `extends` URL, and anything written to output or to
a file passes through a filter that would mask it if it appeared — a second
line of defence the tests exercise with a server that echoes it back.

**Rate limits** are handled as GitHub asks: writes are sent one at a time at
least a second apart; a request refused for a primary or secondary rate limit
waits for `retry-after`, or until `x-ratelimit-reset`, or at least a minute
doubling each time, and is retried up to five times.

## What it cannot do

**What `export` cannot read**, and so a declaration cannot hold:

- **Secret values.** By design: names only, and `apply` never creates, changes
  or deletes a secret. A missing or unexpected secret is reported (`!`) for a
  person to fix.
- **Organisation-level anything**: rulesets inherited from the organisation,
  organisation secrets, organisation Actions policy. Only the repository's own.
- **Settings outside the kinds above**: branch protection rules (the pre-ruleset
  kind), webhooks, deploy keys, collaborators and team access, autolinks, Pages,
  custom properties, code security configurations, Dependabot secrets, Actions
  variables.
- **Whether Dependabot alerts are on**, with a token that may not ask: GitHub
  answers 404 both for "off" and for "you may not know", and repo-standard reads
  404 as off. Give the token Administration: read.
- **A Discussions category's format** beyond whether it is answerable, and its
  section.

**Discussions categories cannot be written.** GitHub has no API, REST or
GraphQL, that creates, edits or deletes one. They are read and compared, and
every difference is reported as one repo-standard cannot change.

### Projects v2

A board belongs to a user or an organisation, not to the repository, so a board
the repository links and the declaration does not name is **unlinked, not
deleted**. A declared board that is not linked is looked up by title under its
owner and linked if it exists (its settings are then compared on the next run),
or created and linked if not.

**What the Projects v2 API cannot express, and is therefore left out rather
than half supported:**

- **Views**: board, table or roadmap layout, grouping, sorting, filters, which
  fields are shown, and the column order a view presents.
- **Column limits, such as a WIP limit.** A limit is a setting of a board
  view, and the API does not write views.
- **The built-in workflows** (auto-add, auto-archive, "item closed → Done").
  The API can delete one but not create or configure one.
- **Insights charts**, and **iteration fields**.
- **Items**: which issues are on a board is work, not configuration.

**Changing the Status options rewrites the whole list.** The API takes the
options without their ids, so every option is new afterwards and every item
whose option changed loses its Status. `apply` refuses to do that to a board
that has items unless given `--allow-status-reset`, and the plan says how many
items the board has.

## Building it

.NET 10. From this directory:

```bash
dotnet build RepoStandard.slnx -c Release                   # warnings are errors
dotnet test --project tests/RepoStandard.Tests -c Release
dotnet pack src/RepoStandard -c Release -o artifacts        # the dotnet tool
dotnet publish src/RepoStandard -c Release -r linux-x64 -o artifacts/linux-x64   # the native binary
```

The tests are the diff engine over recorded live state, contract tests over a
recorded exchange for every endpoint the tool calls, a round-trip property
(`apply`, then `export`, then `plan` shows nothing) over generated
declarations, and an integration test against a scratch repository that is
skipped unless `REPO_STANDARD_TEST_TOKEN` and `REPO_STANDARD_TEST_REPO` are
set. `tests/RepoStandard.Tests/fixtures/README.md` says where every recording
came from.

Its dependencies: YamlDotNet (MIT), for its YAML parser and emitter only.
Everything else is the .NET base class library.

Licensed under the Mozilla Public License 2.0.
