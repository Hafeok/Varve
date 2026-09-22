# 0029 — Publishing and versioning

## Status

**Accepted.** 2026-09-22.

Milestone 3a produced the first packable projects. This decides what a
published package carries, where its version comes from, and how it reaches
nuget.org.

## Context

Three packages are packable — `Varve.Iri`, `Varve.Rdf`, `Varve.Turtle` — and
none has ever been published. Nothing about publishing is reversible: **a
version pushed to nuget.org cannot be edited, replaced or deleted, only
unlisted.** A package published with a broken icon reference, a licence nobody
can read, or a version number that means nothing is a permanent artifact.

The NuGet organisation is `decision-driven-design`. The repository is
`Hafeok/Varve` today and will move to a `mindovermachine` GitHub organisation
later, and **no code may depend on the location**.

Trusted publishing is already configured on nuget.org: policy owner
`decision-driven-design`, repository `Hafeok/Varve`, workflow file
`publish.yml`, no environment. The repository secret `NUGET_USER` holds the
nuget.org username of the account that created the policy.

## Decision

### Metadata, set once, in `Directory.Build.targets`

| Property | Value |
|---|---|
| `Authors`, `Company` | `decision-driven-design` |
| `PackageLicenseExpression` | `Apache-2.0` |
| `PackageProjectUrl` | the repository URL |
| `RepositoryUrl` | **derived**, via `PublishRepositoryUrl` and SourceLink |
| `PackageIcon` | `icon.png`, embedded |
| `PackageReadmeFile` | `README.md`, one per package |
| `PackageTags` | `rdf;sparql;graph-database;event-sourcing;semantic-web;linked-data` |
| `IncludeSymbols`, `SymbolPackageFormat` | a `.snupkg` beside each package |
| `EmbedAllSources` | sources in the symbols, so stepping in needs no symbol server |

**No `licenseUrl`, no `iconUrl`.** Both are deprecated, and both point at
something that can change under a published package — a licence that a consumer
audited once and that now says something else is worse than no licence at all.
The licence is an SPDX expression and the icon is a file inside the package.

> NuGet nevertheless writes `<licenseUrl>https://licenses.nuget.org/Apache-2.0</licenseUrl>`
> into the nuspec beside the expression, for clients too old to read the
> expression form. That is NuGet's compatibility shim, not a value this
> repository sets, and it points at NuGet's own rendering of the same SPDX
> identifier.

**In `Directory.Build.targets` and not `Directory.Build.props`**, because
`IsPackable` is set in the project body and props is imported before it.

### The location appears exactly once

`RepositoryUrl` comes from the git remote at pack time. `PackageProjectUrl` is
the only value in the tree that names a host, and it is the only line to change
when the repository moves — along with the trusted-publishing policy on
nuget.org, which is not in this repository at all and is the thing most likely
to be forgotten.

### Versioning: MinVer, from the tag

`MinVer` 8.0.0 — Apache-2.0, no dependencies, and `PrivateAssets="all"` keeps it
out of a consumer's graph.

The version comes from the git tag. The alternative is a `Version` property
somebody edits, and then there are two sources of truth for a version number:
the tag that triggers the publish, and the property that names the file. When
they disagree, the package ships as the wrong version — permanently, because a
version cannot be replaced. One source of truth, and it is the thing that
already decides *whether* to publish.

Before the first tag, MinVer produces `0.0.0-alpha.0.<height>`, which is exactly
right: it sorts below everything and says "no release has been made".

### Prereleases are the norm until SPARQL conformance

The first tag is `v0.1.0-preview.1`. **Versions stay `0.x` and prerelease until
the core passes the SPARQL conformance suites**, because a `1.0` from this
project would claim a stability the store does not have — the log format is not
frozen until milestone 6, and the ADRs that decide it carry revisit conditions
that are still open.

That is a promise about the number, not an excuse: a prerelease still cannot be
replaced once pushed, and everything in this ADR applies to the first one.

### The workflow

`.github/workflows/publish.yml`, on tags matching `v*`:

1. Build Release, then run the full suite and every gate. **A tag does not skip
   the checks a pull request runs**; a tag is the one moment they matter most.
2. `dotnet pack` with `ContinuousIntegrationBuild`.
3. With `permissions: id-token: write`, `NuGet/login@v1` pinned by commit SHA,
   with `user: ${{ secrets.NUGET_USER }}`.
4. `dotnet nuget push --api-key ${{ steps.login.outputs.NUGET_API_KEY }}
   --skip-duplicate`, **immediately after** the login step.

Steps 3 and 4 are adjacent because the temporary key lives one hour. Anything
between them is something that can make a publish fail holding a credential.

`--skip-duplicate` because a re-run of a partly-successful publish must not fail
on the packages that already went up — the failure mode to protect against is a
tag that half-published and cannot be retried.

**No API key anywhere.** Trusted publishing exchanges the workflow's OIDC token
for a short-lived key. There is no long-lived secret to leak, rotate or forget,
and `NUGET_USER` is a username.

### The dry run

The build job packs on **every pull request** and runs
`eng/package-metadata.cs` over the result, which opens each `.nupkg` and reads
the nuspec back.

It checks the artifact rather than the properties that were supposed to produce
it. Each of these values can be lost silently — a property set outside the
`IsPackable` condition, a file that stops being packed, an expression that
becomes a URL — and none of it fails a build. The first time anyone would
notice is on nuget.org, after it is permanent.

**Publishing is never run from a pull request.** The dry run packs and inspects;
it does not push, and the workflow that pushes is not triggered by a pull
request at all.

## Alternatives considered

- **A `Version` property in `Directory.Build.props`**, bumped by hand. Simplest,
  and no new dependency. Lost to the two-sources-of-truth argument above. The
  failure it invites — tag and property disagreeing — produces a permanently
  wrong package rather than a build error.
- **Nerdbank.GitVersioning.** More capable: version height, cloud-build
  integration, a committed `version.json`. Lost on size for what is needed here.
  MinVer is one package with no dependencies that reads a tag; the extra
  capability is configuration to maintain for a repository with one release
  line.
- **A long-lived API key in a repository secret.** The ordinary way, and it
  works. Lost because trusted publishing already exists and is configured: a
  secret that cannot leak beats a secret that is stored carefully.
- **Publishing from a manual `workflow_dispatch`** rather than a tag. Lost
  because the tag is the record. A dispatch leaves nothing in the repository
  saying which commit became which version.
- **Asserting the metadata by reading the MSBuild properties** instead of the
  built package. Cheaper and wrong: the properties are the input, and the whole
  class of failure here is an input that does not reach the output.

## Consequences

**The first publish is the risky one**, and it is the one with the least
evidence behind it. The dry run exists for that: by the time a tag is pushed,
the exact pack step has run on every pull request and its output has been read
back.

**`PackageProjectUrl` and the nuget.org trusted-publishing policy both name
`Hafeok/Varve`.** Moving the repository breaks the publish until the policy is
edited, and the failure will look like an authentication error rather than a
configuration one. Recorded here because that is the only place it will be
found.

**MinVer needs tags to be fetched.** `actions/checkout` defaults to a shallow
clone with no tags, so the publish workflow fetches them; a version of
`0.0.0-alpha.0.N` on a tagged build means that was forgotten.

**A `.snupkg` doubles the artifacts and the upload.** Worth it: a consumer
stepping into a parser to see why their document was rejected is exactly the
case this project should support, and without embedded sources they cannot.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0028).
  Touches **0008** (the target framework a package declares) and **0009** (MinVer
  is a new dependency and carries an `Adr` attribute naming this ADR; it is
  build-time, so ADR 0009's lower bar applies, and `PrivateAssets="all"` is what
  makes that true). No conflict with any.
- **Constraint 1.** MinVer ships no native asset, and `eng/native-assets.cs`
  now enforces that over the whole packable closure rather than taking my word.
- **Layer ownership.** None; this is build configuration.
- **Analyzer rule.** None. The check is a gate over a built artifact, which is
  not something an analyzer can see.
- **Open questions owned.** None.
