# The repo-standard declaration

Version 1. This document specifies the file `repo-standard` reads and writes.
Where the code and this document disagree, one of them is a defect.

## 1. The file

A declaration is one YAML document whose top level is a mapping. Every
top-level key is either `extends` or a **kind**:

| Kind | Holds | Writable |
|---|---|---|
| `repository` | description, homepage, topics, default branch, features, merge methods, sign-off, security options | yes |
| `labels` | issue and pull-request labels | yes |
| `rulesets` | the repository's own branch, tag and push rulesets | yes |
| `environments` | deployment environments, their protection and their secret names | yes, except secrets |
| `secrets` | the names of the repository's Actions secrets | no — asserted only |
| `discussions` | Discussions categories | no — no API writes them |
| `projects` | Projects v2 boards linked to the repository | yes, within §4.7 |
| `actions` | Actions permissions | yes |

Any other top-level key is an error. The order of this table is the order in
which kinds are read, planned and written: repository settings first, because
the features gate what follows.

### 1.1 What is managed

- **A kind that is absent is not managed.** Nothing about it is read, compared
  or written.
- **A kind that is present is managed exhaustively** where it is a list: what
  the repository has and the declaration does not name is a difference. `labels:
  []` means the repository has no labels.
- **Within an object, a field that is absent is not managed.** A repository
  declaration that names only `description` compares only the description.
- **A list that is present is compared whole**: `topics`, a ruleset's `rules`,
  an environment's `reviewers`. Lists that are sets — topics, reviewers, branch
  policies, secret names, allowed-action patterns, and every list inside a
  ruleset — are compared without regard to order. A board's `status` options
  are compared in order, because the order is the board's columns.

### 1.2 Scalars

Scalars resolve by the YAML 1.2 core schema, narrowed:

- `null`, `~` and an empty plain scalar are null;
- `true` and `false` (and their capitalised forms) are booleans;
- decimal integers and decimal floats are numbers, **except** an integer with a
  leading zero, which is a string (`000000` is a colour, not 0);
- everything else, and anything quoted, is a string. Octal, hexadecimal,
  `.inf` and `.nan` are strings.

Anchors and aliases are allowed. Tags other than `!!str` are an error. A
duplicate key is an error. A file holding more than one document is an error.

### 1.3 Validation

Before anything is read from GitHub, the merged declaration (§2) is validated.
**A key the schema does not know is an error**, with its path. Types and
enumerations are checked as listed in §4. These cross-field rules are checked
too, because GitHub enforces them and a declaration that breaks one could never
converge:

- an environment's `deployment_branch_policy` cannot have both
  `protected_branches` and `custom_branch_policies` true;
- an environment with `branch_policies` and a declared
  `deployment_branch_policy` needs `custom_branch_policies: true`;
- an environment's `prevent_self_review: true` needs a non-empty `reviewers`
  (GitHub keeps the flag inside the reviewers rule, which exists only while
  there are reviewers);
- `actions.selected_actions` needs `allowed_actions: selected` when
  `allowed_actions` is declared;
- the key of every keyed list (§2.2) is unique, case-insensitively.

Every problem the schema finds is reported at once, not only the first; the
cross-field rules are checked once the schema passes.

## 2. `extends`

```yaml
extends: ../standards/base.yaml     # relative to this file
extends: https://example.org/base.yaml
```

The value is a path, resolved against the directory of the file that names it,
or an `https://` URL. `http://` is refused. A base may itself extend another; a
relative `extends` inside a fetched base resolves against the base's URL. A
cycle is an error; so is nesting deeper than ten.

**A base is fetched with no credentials.** The token is sent to GitHub's API
and nowhere else.

### 2.1 Merging

The base is loaded (with its own `extends` resolved), and the file that extends
it is merged over it:

| In the overriding file | Result |
|---|---|
| a mapping, over a mapping | merged key by key, recursively |
| a key set to `null` | the key is removed: no longer managed |
| a keyed list (§2.2) | merged item by item on the key |
| any other list | replaces the base's |
| a scalar | replaces the base's |

### 2.2 Keyed lists

| List | Key |
|---|---|
| `labels` | `name` |
| `rulesets` | `name` |
| `environments` | `name` |
| `projects` | `title` |
| `discussions.categories` | `name` |

An overriding item whose key matches a base item **replaces it whole** — it is
not merged field by field, because a ruleset or an environment half from one
file and half from another is not something anyone could review. An item with
`absent: true` removes the matching base item; an item whose key matches
nothing is appended. `absent` items are dropped after merging, so an `absent`
item that matched nothing has no effect.

### 2.3 Variables

After merging, `${owner}` and `${repo}` in any string value are replaced by
the owner and name of the repository being converged. `$${` is a literal `${`.
Any other `${...}`, or an unclosed `${`, is an error. Keys are not
interpolated.

## 3. How each command uses it

| Command | Reads | Writes |
|---|---|---|
| `export` | every kind | the file, in this format; never overwrites without `--force` |
| `plan` | the kinds present | nothing |
| `apply` | the kinds present | what differs |
| `check` | the kinds present | nothing; the report to `GITHUB_STEP_SUMMARY` if set |

`export` writes what it read in exactly this format, so that `plan` of an
export is empty. A kind it could not read (a 403 for a permission the token
lacks) is left out and named in a comment at the top of the file. Discussions
are left out while they are switched off.

A plan is a list of changes, kind by kind in the order of the table in §1,
and within a kind creates and updates before deletes. Each is one of `+`
create, `~` update, `-` delete, `!` a difference repo-standard reports and
cannot write. `apply` makes the writes in plan order and stops at the first
that fails, printing it and every change it did not make. `apply` exits 1 if a
write failed or any `!` change exists, because the repository then does not
match the declaration.

## 4. The kinds

### 4.1 `repository`

| Key | Type | GitHub |
|---|---|---|
| `description` | string | `description`; unset reads as `""` |
| `homepage` | string | `homepage`; unset reads as `""` |
| `topics` | list of string | `GET`/`PUT /repos/{owner}/{repo}/topics` |
| `default_branch` | string | `default_branch` (the branch must exist) |
| `features.issues` | bool | `has_issues` |
| `features.projects` | bool | `has_projects` |
| `features.wiki` | bool | `has_wiki` |
| `features.discussions` | bool | `has_discussions` |
| `merge.allow_merge_commit` | bool | `allow_merge_commit` |
| `merge.allow_squash_merge` | bool | `allow_squash_merge` |
| `merge.allow_rebase_merge` | bool | `allow_rebase_merge` |
| `merge.allow_auto_merge` | bool | `allow_auto_merge` |
| `merge.allow_update_branch` | bool | `allow_update_branch` |
| `merge.delete_branch_on_merge` | bool | `delete_branch_on_merge` |
| `merge.merge_commit_title` | `PR_TITLE` \| `MERGE_MESSAGE` | `merge_commit_title` |
| `merge.merge_commit_message` | `PR_BODY` \| `PR_TITLE` \| `BLANK` | `merge_commit_message` |
| `merge.squash_merge_commit_title` | `PR_TITLE` \| `COMMIT_OR_PR_TITLE` | `squash_merge_commit_title` |
| `merge.squash_merge_commit_message` | `PR_BODY` \| `COMMIT_MESSAGES` \| `BLANK` | `squash_merge_commit_message` |
| `web_commit_signoff_required` | bool | `web_commit_signoff_required` |
| `security.private_vulnerability_reporting` | bool | `/private-vulnerability-reporting`: `GET` `enabled`, `PUT` on, `DELETE` off |
| `security.dependabot_alerts` | bool | `/vulnerability-alerts`: `GET` 204 on / 404 off, `PUT` on, `DELETE` off |
| `security.dependabot_security_updates` | bool | `/automated-security-fixes`: `GET` `enabled`, `PUT` on, `DELETE` off |
| `security.secret_scanning` | bool | `security_and_analysis.secret_scanning.status` |
| `security.secret_scanning_push_protection` | bool | `security_and_analysis.secret_scanning_push_protection.status` |

Every differing field that lives on the repository object is written in one
`PATCH /repos/{owner}/{repo}`. Topics and each separately-addressed security
option are a change of their own. A field GitHub does not return to the token
(some merge settings need administration) reads as absent, and a declared
field that reads as absent is a difference.

### 4.2 `rulesets`

A list of rulesets **in the JSON shape GitHub's ruleset export uses**:
`name` (the key), `target` (`branch` | `tag` | `push`), `enforcement`
(`active` | `evaluate` | `disabled`), `conditions`, `rules`, `bypass_actors`.
Only the top level is validated here; below it the shape is GitHub's, and GitHub
validates it on write.

Read with `GET /repos/{owner}/{repo}/rulesets?includes_parents=false` and a
`GET` of each. **Only the repository's own rulesets**: those inherited from an
organisation are neither read nor written. The fields GitHub owns — `id`,
`node_id`, `source`, `source_type`, `_links`, `created_at`, `updated_at`,
`current_user_can_bypass` — are removed from both sides, and the rest is
compared **whole, in both directions**, arrays as sets. A difference replaces
the ruleset with `PUT`; a new one is `POST`ed; one not declared is `DELETE`d.
A renamed ruleset is a delete and a create.

`bypass_actors` carry GitHub's ids. For `RepositoryRole` (5 is admin) and
`Integration` (an App's id) those are the same in every repository; for `Team`
they belong to the organisation.

### 4.3 `environments`

| Key | Type | GitHub |
|---|---|---|
| `name` | string, the key | the environment's name |
| `wait_timer` | integer, minutes | the `wait_timer` protection rule; absent reads as 0 |
| `reviewers` | list of `{type: User, login}` or `{type: Team, slug}` | the `required_reviewers` rule; resolved to ids with `GET /users/{login}` and `GET /orgs/{owner}/teams/{slug}` when written |
| `prevent_self_review` | bool | the `required_reviewers` rule; exists only with reviewers (§1.3) |
| `deployment_branch_policy` | `{protected_branches, custom_branch_policies}` or `null` | `deployment_branch_policy`; `null` means any branch |
| `branch_policies` | list of `{name, type: branch \| tag}`, type defaulting to `branch` | `/deployment-branch-policies`, read only while `custom_branch_policies` is true |
| `secrets` | list of string | `GET /environments/{name}/secrets`; asserted only |

A difference is written with one `PUT /environments/{name}`, which replaces the
protection rules; undeclared fields keep their live values. Branch policies are
then added with `POST` and removed with `DELETE`. An environment not declared
is deleted, **and its secrets and deployment history with it**; the plan says
so.

Secret names are compared like `secrets` (§4.4), as `!` changes.

### 4.4 `secrets`

A list of the names of the repository's Actions secrets, read with `GET
/repos/{owner}/{repo}/actions/secrets`. **A secret's value is never read and
never written, and a secret is never created or deleted.** A declared name that
is missing, and a present name that is not declared, are each a `!` change for
a person to resolve.

### 4.5 `labels`

| Key | Type |
|---|---|
| `name` | string, the key; **case-insensitive**, as on GitHub |
| `color` | six hex digits, quoted; compared lower-case, `#` ignored |
| `description` | string; unset reads as `""` |

Created with `POST`, changed with `PATCH /labels/{name}` (a change of case in
the name is a rename, `new_name`), deleted with `DELETE`. **A delete removes the
label from every issue and pull request.** A new name is a delete and a create.

### 4.6 `discussions`

```yaml
discussions:
  categories:
    - {name: Q&A, emoji: ":pray:", description: Ask for help, answerable: true}
```

Read with the GraphQL `repository.discussionCategories`: `name` (the key),
`emoji`, `description`, `isAnswerable` as `answerable`. **Never written**: GitHub
has no API, REST or GraphQL, that creates, edits or deletes a category. Every
difference is a `!` change. Whether Discussions is on is
`repository.features.discussions`.

### 4.7 `projects`

| Key | Type | GitHub (GraphQL) |
|---|---|---|
| `title` | string, the key with `owner` | `ProjectV2.title` |
| `owner` | string | the board's owner; defaults to the repository's owner and is omitted from an export when it is that |
| `short_description` | string | `shortDescription` |
| `readme` | string | `readme` |
| `public` | bool | `public` |
| `closed` | bool | `closed` |
| `status` | list of `{name, color, description}` | the options of the single-select field named `Status` |

`color` is one of `GRAY`, `BLUE`, `GREEN`, `YELLOW`, `ORANGE`, `RED`, `PINK`,
`PURPLE`; `description` defaults to `""`.

Read with `repository.projectsV2`: the boards **linked to the repository**.

- A declared board not linked is looked up by exact title among its owner's
  boards (`repositoryOwner.projectsV2`). If found, it is linked
  (`linkProjectV2ToRepository`) and nothing else — its settings are compared on
  the next run, not written blind. If not found, it is created and linked
  (`createProjectV2` with `repositoryId`), then described
  (`updateProjectV2`) and given its Status options.
- A linked board not declared is **unlinked**
  (`unlinkProjectV2FromRepository`), never deleted: it belongs to its owner.
- Scalar differences are one `updateProjectV2`.
- A `status` difference rewrites the options with `updateProjectV2Field`, or
  creates the field with `createProjectV2Field` if the board has none. **The API
  takes the options without ids, so every item on a changed option loses its
  Status.** On a board with items this is refused unless `apply` is given
  `--allow-status-reset`.

**Not expressible, because the API cannot write them**, and so left out: views
(layout, grouping, sorting, filters, visible fields), column limits such as a
WIP limit, built-in workflows, insights, iteration fields, and items.

### 4.8 `actions`

| Key | Type | GitHub |
|---|---|---|
| `enabled` | bool | `/actions/permissions` `enabled` |
| `allowed_actions` | `all` \| `local_only` \| `selected` | `/actions/permissions` `allowed_actions` |
| `sha_pinning_required` | bool | `/actions/permissions` `sha_pinning_required` |
| `selected_actions.github_owned_allowed` | bool | `/actions/permissions/selected-actions` |
| `selected_actions.verified_allowed` | bool | ″ |
| `selected_actions.patterns_allowed` | list of string | ″ |
| `workflow.default_workflow_permissions` | `read` \| `write` | `/actions/permissions/workflow` |
| `workflow.can_approve_pull_request_reviews` | bool | ″ |

Each endpoint is one `PUT` when anything under it differs; undeclared fields
keep their live values. `selected_actions` is read only while `allowed_actions`
is `selected`.

## 5. Exit codes

| Code | Meaning |
|---|---|
| 0 | `plan` or `export` ran; `check` found no difference; `apply` left none |
| 1 | `check` found a difference; `apply` failed a write or left a `!` difference |
| 2 | could not run: usage, an invalid declaration, a read that failed |
