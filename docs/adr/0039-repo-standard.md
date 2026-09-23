# 0039 — repo-standard: repository settings as code, built here and moving out

## Status

Accepted. 2026-09-23, with the plan at
[#23](https://github.com/Hafeok/Varve/issues/23).

## Context

Varve's repository settings are part of how it works, not decoration.
`GOVERNANCE.md` describes two rulesets on `main`, a tag ruleset, a `release`
environment with a required reviewer, Discussions and its categories, and two
project boards, and several ADRs depend on them being so —
[0032](0032-trunk-based-development.md) makes ruleset 1 the blocking review,
[0034](0034-commit-signing-and-the-sandbox-exception.md) is a bypass on ruleset
2, [0029](0029-publishing-and-versioning.md) gates publishing on the `release`
environment. Today every one of those is a click in a settings page. Nothing
records it, nothing notices when it changes, and the stewardship record
(`docs/traceability/2026-09-22-issue-16-mom-stewardship.md`) could only
*document* them, because there was no way to state them.

That is the same gap [0004](0004-enforcement-by-analyzers.md) names for code: a
rule only in a document is not a rule. The settings need to be a declaration a
tool converges the repository to, and checks for drift.

The tool is not Varve-specific. Every repository run to the Mind Over Machine
standard needs the same thing, and so does anyone else's. It is built here
because this is where it is needed first, and will move to a repository of its
own; everything below is shaped so that the move is a copy.

What exists already, and why none of it is used:

- **Terraform's GitHub provider**, **Pulumi**: a state backend and a runtime to
  run a declaration of one repository's settings. The state is the problem as
  much as the weight — a drift check against a stored state tells you what the
  tool last did, not what the repository is.
- **The Probot settings app** (`.github/settings.yml`): a hosted service to trust
  with administration of the repository, no rulesets in the shape GitHub
  exports them, no drift report.
- **Octokit.NET**: a large reflection-based client for the whole API, to call
  some forty endpoints. Its trimming and AOT status would have to be argued, and
  constraint 2 would have to be argued for a tool that does not need it.

## Decision

### What and where

`tools/repo-standard/` holds a CLI and a GitHub Action.

- **The CLI** has four commands — `export`, `plan`, `apply`, `check` — over a
  YAML declaration of repository settings, rulesets, environments, secret names,
  labels, Discussions categories, linked Projects v2 boards and Actions
  permissions. `tools/repo-standard/docs/declaration.md` is its specification;
  `tools/repo-standard/README.md` is its user documentation. It builds as a
  `dotnet` tool and as a Native AOT single-file binary.
- **The Action** (`tools/repo-standard/action.yml`) is composite: it downloads
  the binary for the runner from a release of the repository the action is used
  from, verifies it against `SHA256SUMS`, and runs it. Written for the tool's
  future home; nothing in it names this repository.
- **Varve's own workflow** (`.github/workflows/repo-standard.yml`) builds the tool
  from source, because there is no release to download yet, and does nothing at
  all until `.github/repo-standard.yaml` exists.

**It is not published from this repository**: no NuGet package, no release
assets. Publishing starts in its own home.

### Isolation, so the move is a copy

- **Nothing in `src/` references it, and it references nothing in `src/`.** It
  has its own solution, `tools/repo-standard/RepoStandard.slnx`, outside
  `Varve.slnx`.
- **It is held to Varve's rules while it lives here**: `VarveLayer` `none`,
  `Varve.Analyzers` passed to the compiler as an analyzer, warnings as errors,
  latest-recommended analysis, the MPL-2.0 header on every file, every package
  in `Directory.Packages.props` citing this ADR, issue references and sign-off on
  every commit, and a traceability record.
- **The seam is one line.** `tools/repo-standard/Directory.Build.props` imports
  the repository's props; on the move, the import goes and the properties it
  supplied are written out. It does **not** import the repository's
  `Directory.Build.targets`, which would give the tool Varve's package identity
  — its tags, its project URL, MinVer from Varve's tags, the public-API baseline.
  A `Directory.Build.targets` beside the tool replaces it.
- **The analyzer reference is the one coupling**, and it is build-time only.

**Stated plainly, because it looks like more than it is:** `VARVE0001` and
`VARVE0002` apply to assemblies named `Varve.*`, so on the `RepoStandard`
assembly they are wired and inert. The tool declares `VarveLayer` `none` for the
record. Renaming the tool into the analyzers' scope to make them fire was
considered and rejected: the name is what adopters type, and the layering rule
has nothing to say about a tool that references no layer.

### Dependencies

One runtime package, and none new for tests.

| Package | Version | Class | Why |
|---|---|---|---|
| `YamlDotNet` | 18.1.0 | runtime, of the tool only | The declaration is YAML. MIT, no dependencies of its own, managed. |
| `xunit.v3`, `Microsoft.Testing.Extensions.TrxReport`, `CsCheck` | as registered | test-only | Already admitted by 0009, 0007 and [0025](0025-property-based-testing.md); the round-trip property is a CsCheck property. |

Everything else is the BCL: `HttpClient` for both GitHub APIs, and
`System.Text.Json` — source-generated for the fixed-shape bodies (the GraphQL
request envelope), `JsonNode` for the rest. **No Octokit.**

**YamlDotNet's parser and emitter only, not its serializer or its static
context.** The static generator (`Vecc.YamlDotNet.Analyzers.StaticGenerator`)
needs a typed model, and a declaration is not typed all the way down: a ruleset
is carried in the shape GitHub's ruleset export uses, and that shape is
GitHub's. So YAML is read into a `JsonNode` tree, exactly as JSON would be, and
the tree is what the rest of the tool compares and writes. The parser and the
emitter use no reflection. This was measured, not assumed: the tool publishes
with Native AOT, `IlcTreatWarningsAsErrors` on, with no warning, and the
published binary parses and validates `baselines/mom.yaml`. The fallback the
plan allowed — JSON declarations — was not needed. It also leaves one package
where the static context would have been two.

A consequence of reading YAML as a tree: scalars resolve by the YAML 1.2 core
schema, narrowed. Octal, hexadecimal, `.inf` and `.nan` are strings, and an
integer with a leading zero stays a string, so that a label colour such as
`000000` written unquoted is not the number 0. The writer quotes any string a
YAML 1.1 reader would take for something else.

### The token

The tool authenticates to GitHub with the token it is given, placed on the
Authorization header and nowhere else. It never logs it and never writes it to
a file; output passes through a filter that would mask it, as a second line of
defence that the tests exercise with a server that echoes the token back. It is
never sent to an `extends` URL, and a `Link` header that points away from the
API's host is not followed.

**This does not contradict [0037](0037-server-authentication.md).** 0037 is how
`Varve.Server` authenticates *its* callers, and forbids Varve inventing a
credential scheme of its own. repo-standard authenticates *to* GitHub, a client
of an API whose schemes GitHub sets. It supports a GitHub App's installation
token — short-lived, minted per run, the recommended way — and a fine-grained
personal access token as the simpler choice for adopters. **Varve's own workflow
uses only an App token**, minted by `actions/create-github-app-token`, and no
long-lived credential is added to this repository.

### What is out, and said so

Discussions categories cannot be written — no API does it — so they are read,
compared and reported. Projects v2 views, column limits (the standard's WIP
limit among them), built-in workflows, insights and iteration fields cannot be
written by the API and are left out rather than half supported. Rewriting a
board's Status options clears the Status of items on changed options, so it
needs `--allow-status-reset`. Organisation-level settings and secret values are
out of scope. The README lists each.

## Alternatives considered

- **Build it in its own repository from the start.** The better end state, and
  where it goes. Not first, because the maintainer wanted it where it is needed
  and under the same gates as everything else, and the isolation above makes
  starting here cheap to undo.
- **JSON declarations, no YAML package.** One dependency fewer. Rejected while
  YAML worked under AOT: a declaration is written and reviewed by people, and
  comments — the baseline is mostly comments — are the thing JSON lacks. It was
  the planned fallback, and would have been taken if the AOT build had failed.
- **YamlDotNet's static context.** See above: it needs a typed model of GitHub's
  ruleset shape, and a second package.
- **Diff against a stored state, Terraform-style.** Rejected: `check` exists to
  find what a person changed by hand, which only a read of the live repository
  can see.
- **Octokit.NET.** Rejected, above.

## Consequences

- The repository's settings can be declared, applied and checked, once the
  maintainer adds `.github/repo-standard.yaml` and the App secrets. Until then
  the workflow is inert and **this ADR changes no setting.**
- `eng/ci.cs` gains the tool's build and tests, and `ci.yml` gains the tool's
  AOT publish and run on Linux and Windows, and a test of the Action against a
  locally built binary. The tool's jobs are separate from Varve's, so moving it
  out deletes jobs rather than untangling them.
- `Directory.Packages.props` registers `YamlDotNet` citing this ADR. On the move
  that line moves with it.
- The recorded exchanges the contract tests replay are built from GitHub's
  published OpenAPI examples and GraphQL schema, not captured from a live
  repository, because this session ran the tool against none. The integration
  test can record real ones (`REPO_STANDARD_RECORD_DIR`); replacing the
  documented examples with captured ones is follow-up work, not a condition.

## Checks

- `dotnet run eng/dependency-register.cs` — `YamlDotNet` cites this ADR.
- `dotnet run eng/ci.cs -- --only repo-standard-build --only repo-standard-test`.
- `ci.yml`, job `repo-standard (…)`: Native AOT publish with ILC warnings as
  errors, then run; job `repo-standard action`: the Action end to end against a
  replayed GitHub, and refusing a binary whose checksum does not match.
