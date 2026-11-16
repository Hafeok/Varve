# 0227 — The public API freeze gate: each package against its published nupkg, a break marked or refused

## Status

**Filed unaccepted by readiness B for #129, 2026-11-16** (ADR 0066). Decided
by the maintainer on #119 (F1, 2026-10-27), and planned on #129 on
2026-10-27 and again on 2026-11-16. Acceptance is the maintainer's act on the
pull request.

**Amends** [ADR 0035](0035-semantic-versioning.md) points 2 and 4: before
1.0, the published package is the evidence of a break, and the freeze has a
gate. The amendment is dated 2026-11-16 in that file, and is filed
unaccepted.

**Builds on:**
- [ADR 0029](0029-publishing-and-versioning.md) and
  [ADR 0102](0102-a-release-is-a-descriptor.md): one tag, one version, and a
  release is a descriptor whose summary is its notes;
- [ADR 0088](0088-only-checked-commits-reach-main.md): a gate is required by
  its job name;
- [ADR 0224](0224-supply-chain-transparency.md): CI restores locked, with
  .NET set up only through `.github/actions/setup-dotnet`.

Records decision set [`api-freeze`](../decisions/api-freeze.md).

## Context

ADR 0035 made the public API baselines the evidence of a breaking change: a
line leaving `PublicAPI.Shipped.txt` is a removal. It also named the stronger
tool, ApiCompat, which compares what the compiler emitted with what was
published, as "a candidate for the 1.0 freeze", needing its own ADR. #129
asks for that freeze: a gate that compares every `Varve.*` package with the
last release and reports every removal or signature change.

On `main` at `48a1fd5d`, after v0.1.0-preview.21:

- **28 library packages and one tool.** Every library's
  `PublicAPI.Shipped.txt` holds its header only. The whole surface, 6,237
  lines, is in `PublicAPI.Unshipped.txt`.
- **So the baselines cannot see a break between previews.** A removal edits
  only `Unshipped`, which promises nothing (ADR 0035 point 2). It happened:
  `0be712ac` moved `BearerToken.Fixed` to `BearerTokens.Fixed` in
  `Varve.Protocol.Client`, preview.20 shipped it, and no commit and no
  descriptor called it breaking.
- **The SDK carries ApiCompat** as package validation
  (`EnablePackageValidation`, `PackageValidationBaselineVersion`), in
  `Microsoft.NET.Sdk` itself. It restores the published package of the
  baseline version and compares its `lib/` assemblies with the packed ones.
  No package is added, so no register entry.
- **Set as a global property, the baseline reaches every referenced
  project.** Their restore then asks nuget.org for a version they never
  shipped: `NU1102` for `Varve.Delta`, `Varve.JsonLd` and the rest, when
  `Varve.Protocol.Client` was packed against preview.2.

## Decision

### 1. The baseline before 1.0 is the published package

- **Each library package is compared with its own nupkg**, as published on
  nuget.org at the newest `v*` tag that is an ancestor of the commit, by the
  SDK's package validation.
- **The PublicAPI files stay as they are.** `RS0016` and `RS0017` still make
  every public member a declared line, and the surface still moves to
  `PublicAPI.Shipped.txt` at 1.0, not before. Until then the files declare
  the surface, and the published package is the evidence of a break.
- **A package that packs as a .NET tool** (`Varve.Server`) has no `lib/`
  surface. It is reported as not a library; its contract is its command line
  (ADR 0105).
- **A package with no published baseline** is reported as new.

### 2. One package at a time

- `eng/api-freeze.cs` packs each library alone, with `VarveApiFreezeProject`
  naming it and `VarveApiFreezeBaseline` the version.
  `Directory.Build.props` turns validation on only for the project whose
  name matches, so nothing reaches the projects it references.
- **The findings are written, not thrown.** Validation writes them as a
  suppression file (`GenerateCompatibilitySuppressionFile`,
  `ApiCompatSuppressionOutputFile`): each with its diagnostic (`CP0001` a type
  removed, `CP0002` a member removed, and ApiCompat's others), the member's
  documentation id and the assembly. The script reads them and decides.
- No suppression file is committed. A break is never suppressed; it is
  marked, as below.

### 3. Before 1.0, a break fails only where nothing marks it

Every finding is reported, in the log and the step summary. A finding fails
only when both of these hold (the maintainer's F1):

- **no marking commit:** no commit since the baseline tag that touches
  `src/<package>/` has a subject `type(scope)!: …` or a `BREAKING CHANGE:`
  footer;
- **no marking descriptor:** no proposed descriptor (a
  `releases/<version>.yaml` whose tag does not exist yet) has a paragraph or
  list item of its summary naming the package and saying `(breaking)`, the
  form the descriptors already use.

A marking commit covers every finding in its package over the range. Which
member a commit broke is not in the commit, and requiring a name per member
would make the mark harder to write than the break. That is stated, not
hidden: a second, unmarked break in a package that already has a `!` commit
since the tag passes.

### 4. From 1.0, a break needs a new major

When the baseline's major is 1 or more, every finding fails unless a
proposed descriptor's major is above the baseline's. A mark does not excuse
it. This is ADR 0035 point 1, enforced.

### 5. Where it runs

- **`ci.yml`'s `api freeze` job**, on every pull request, push to `main` and
  `land/` branch, and on a release's call, where the proposed descriptor is
  the release's own. Ruleset 1 requires it by name (ADR 0088). It needs the
  tags and the history since the newest one (`fetch-depth: 0`).
- **`eng/ci.cs`'s `api-freeze-fixtures`** runs the rule's cases with no
  network: a fixture package's baseline surface is packed into a temporary
  directory and its other surfaces compared with it through
  `PackageValidationBaselinePath` (`tests/fixtures/api-freeze/`).
- **Measured:** against v0.1.0-preview.21, the 28 libraries took 3 min 14 s
  under `CI=true` on 4 cores, locked restore included, with no findings.

### 6. Members whose only consumer is a test, reported

- `eng/api-freeze.cs --test-only` reads the built assemblies with
  `System.Reflection.Metadata`, in the shared framework. It lists every
  visible type and member of every library package that a test assembly
  references and no other assembly does.
  - A test assembly is one under `tests/`, or a project named `*.Tests`.
  - Every other assembly the repository builds counts as a product
    consumer: another package, a host, a sample.
- **Counted apart, never listed as test-only:**
  - overrides, and members named as a member of an interface their type
    implements, since a caller reaches them through the base or the
    interface;
  - constants and enum values, which the compiler copies into the caller;
  - members nothing outside the package references at all.
- `ci.yml`'s `api freeze` job writes the report to the step summary. It
  never fails the job. Reflection, configuration binding and dependency
  injection leave no reference, so each entry is for the maintainer's
  decision, not a gate's.

## Alternatives considered

- **Move `Unshipped` to `Shipped` at every release,** so that ADR 0035's
  evidence covers previews. It is a release-time edit of 28 files, the
  surface written down rather than compiled, and a step a release can
  forget. The published package is what consumers have.
- **A tool package (`Microsoft.DotNet.ApiCompat.Tool`) run over two
  directories.** The same engine, as a package with a register entry and
  its own version to keep in step with the SDK. Package validation is
  already in the SDK.
- **The baseline as a global property,** one pack of the solution. It fails
  restore, as the context shows.
- **Fail every finding before 1.0, as from 1.0.** ADR 0035 point 3 makes no
  compatibility promise in 0.x, and the maintainer's F1 asks for a report
  that fails only an unmarked break.
- **Match a marking commit to the member it broke.** The commit names no
  member, and a `!` commit's diff can touch the declaration of more than
  one. Kept per package (point 3).
- **Committed suppression files,** ApiCompat's own escape. A suppressed
  break would pass every later run without anyone reading it again. A break
  is marked once, in the commit or the descriptor, and the mark covers only
  the range up to the next release.

## Consequences

- **A pull request that removes or changes public surface** needs a `!`
  commit, or the release's descriptor marks it `(breaking)`. The check says
  which member, and which package's commits it looked at.
- **Every release's call** compares with the release before it, so the
  descriptor of a release that breaks something says so, or the release
  fails.
- **The PR that files this ADR** lists the test-only members for the
  maintainer's decision. Nothing is made internal by it.
- **The freeze itself** (moving the surface to `PublicAPI.Shipped.txt`)
  happens at 1.0, when point 4 takes over.

## Checks

- **Checked against the accepted ADRs** (0001–0225, 0230–0243).
  - **0035:** points 2 and 4 amended. Point 2's baseline files remain the
    declaration of the surface, and the published package becomes the
    evidence of a break until 1.0. Point 4's freeze now has a gate. Its
    alternatives' "a candidate for the 1.0 freeze" is this ADR.
  - **0088:** one more required check, `api freeze`.
  - **0224:** the job sets up .NET through the composite action, and restores
    locked.
  - **Constraint 4:** no new package.
- **Layer ownership.** None: `eng/`, a workflow, `Directory.Build.props` and
  documentation.
- **Open questions owned.** Whether "the management API" means more than the
  `Varve.Management` package and the management calls of
  `Varve.Protocol.Client` (#129, M1).
