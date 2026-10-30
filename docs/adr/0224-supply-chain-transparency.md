# 0224 — Supply chain transparency: SBOMs, the dependency gate and vulnerability handling

## Status

**Accepted — filed unaccepted by readiness B for #119, 2026-10-27** (ADR 0066).
Decided by the maintainer on 2026-10-16. This ADR is the session's draft,
reconciled with what was already built: what exists, it records, and what does
not exist, it decides. The maintainer settled the reconciliation on #119 on
2026-10-27. Acceptance is the maintainer's act on the pull request.

**Amends** [ADR 0009](0009-dependency-policy-and-register.md): the register
is checked against every lockfile, licences are checked by scope, and
`eng/native-assets.cs` folds into the register gate.
**Amends** [ADR 0102](0102-a-release-is-a-descriptor.md): descriptor format 2
and its `sbom:` list.
**Amends** [ADR 0029](0029-publishing-and-versioning.md): a release carries
assets beyond nuget.org.
**Amends** [ADR 0111](0111-the-container-image.md): the base image is pinned
by digest, and the image's SBOM is merged.
Each amendment is dated 2026-10-27 and filed unaccepted.

**Revised 2026-10-28** on the pull request's review, before acceptance:
- point 3: every package's attestations come before the push, which is a job
  of its own;
- point 3: `publish.yml` has a dry run on the pull request.
- point 5: CI installs exactly the SDK `global.json` pins, and every lockfile
  is written with it.

**Builds on:**
- constraint 1 (no native assets) and constraint 4 (an ADR per third-party
  package);
- [ADR 0099](0099-register-jwtbearer-and-the-workflows-containers.md)'s
  amendment (CI images pinned by digest);
- [ADR 0037](0037-server-authentication.md) (no API keys) and the keyless
  signing rule.

**Amends** the 1.0 definition (#15, dated 2026-10-16).

The generator is [ADR 0225](0225-the-sbom-generator.md). Records decision
set [`supply-chain-transparency`](../decisions/supply-chain-transparency.md).

## Context

Two EU instruments create demand for supply chain evidence. Neither puts a
legal obligation on Varve itself.

- **NIS2** (Directive (EU) 2022/2555; in Denmark, Cybersikkerhedsloven, in
  force since 1 July 2025). Article 21(2)(d) requires essential and important
  entities to manage the security of their direct suppliers. A NIS2 entity that
  runs Varve has to assess it.
- **The Cyber Resilience Act** (Regulation (EU) 2024/2847). As the maintainer
  reads it, free and open-source software that is not supplied in the course
  of a commercial activity is outside the manufacturer obligations.
  - Anyone who integrates Varve into a product they place on the market is a
    manufacturer, and must do due diligence on the components they integrate.
  - From 11 December 2027 that includes an SBOM in a machine-readable format
    covering at least the top-level dependencies (Annex I, Part II(1)).
  - Reporting of actively exploited vulnerabilities (Article 14) has applied
    since 11 September 2026.

The maintainer decided on 2026-10-16 that Varve is open source, not supplied
commercially, and meets the CRA's SBOM and vulnerability-handling requirements
anyway, so that the people who depend on it can do their own due diligence.

Both groups need three things from Varve:
1. what is inside each artefact;
2. where it came from, verifiably;
3. how vulnerabilities in Varve and its dependencies are found, fixed and
   announced.

**What was built before this ADR, measured on `main` at 3a8f636:**

- **The container image** (ADR 0111) has an SPDX 2.3 SBOM and a SLSA v1
  build-provenance attestation on its manifest digest. The SBOM is made by
  `anchore/sbom-action` (Syft) and verified in `publish.yml` with
  `gh attestation verify`.
  - **It has a gap.** The preview.15 attestation
    (`sha256:f3c1651a…d706`) lists ten packages: the nine Ubuntu packages of
    the chiseled base, and the image itself.
  - Nothing of the .NET closure is there. The image is the Native AOT binary,
    which has no `.deps.json` and no managed assemblies for Syft's .NET
    catalogers to read.
  - Its namespace is a random UUID, and its creation time is the build's.
- **The base image was referenced by tag** (`runtime-deps:10.0-noble-chiseled`).
  ADR 0099 pins CI's images by digest, but not the product's.
- **Every direct package names its ADR** in `Directory.Packages.props` (ADR
  0009), with a `Scope` where one applies (ADR 0133). `eng/dependency-register.cs`
  checks those citations and scopes, and the major-bump rule.
  - It did not see transitive packages, licences, or entries nothing resolves
    any more.
  - No lockfile existed.
- **Constraint 1 on the transitive closure** was already a gate,
  `eng/native-assets.cs`. It covers every packable member of `Varve.slnx`,
  keyed on `assetType: native`, with one allow-list entry: `Varve.Aspire`,
  under ADR 0117.
- **Every Action was pinned by commit SHA**, 127 references, and Dependabot
  watches them. Nothing failed on a tag. #89 is about permissions only, and
  Scorecard reports without failing.
- **`NuGetAudit`** was already on, at the SDK 10 defaults: mode `all`,
  level `low`. `TreatWarningsAsErrors` makes NU1901–1904 errors. There was no
  exemption rule and no published statement.
- **`SECURITY.md`** had the disclosure policy and the latest-preview support
  rule. It said that nothing was published and that no dependency shipped at
  runtime. Both were wrong by then: the server ships 16 third-party packages.
- **`.github/repo-standard.yaml`** declared private vulnerability reporting
  off, although `SECURITY.md` pointed reporters at its button.
- **`global.json`** pinned the SDK with `rollForward: latestFeature`. The draft
  said roll-forward was disabled.

**What the restore resolves, as of this ADR:**
- 65 lockfiles, 213 distinct packages, of which 36 are register entries.
- Runtime closures:

| Artefact | Third-party packages | Licences |
|---|---|---|
| 24 library packages | none | — |
| `Varve.Protocol.Client` | 8 `Microsoft.Extensions.*` abstractions | MIT |
| `Varve.Server`, the tool | 16 | MIT, Apache-2.0 |
| the CLI binary, per RID | those 16, and the .NET runtime packs | MIT, Apache-2.0 |
| `Varve.Aspire` | 71: the `Aspire.Hosting` closure, `Hex1b`'s natives among them | MIT, Apache-2.0, BSD-3-Clause, BSD-2-Clause |

## Decision

### 1. One SBOM per shipped artefact

**The artefacts are:**
- each `Varve.*` NuGet package;
- the `varve` .NET tool;
- the container image;
- the CLI's Native AOT binaries, when #129 distributes them;
- the Web UI bundle, when #167 lands.

**The browser is not an artefact Varve ships.** A browser application gets
the .NET runtime from its own SDK, so the packages' SBOMs are what applies.

**What each SBOM lists:**
- **The artefact's runtime closure:** for a library, what a consumer of the
  package resolves; for the tool and the binaries, what their output
  carries.
- **For each component:**
  - a package URL (`pkg:nuget/…`) and its version;
  - the SHA-256 of the package and the SHA-512 the restore verified;
  - the licence expression, or the register's stated licence;
  - the supplier;
  - the dependency relationships.
- **`Varve.*` packages** appear as components of each other, by package URL,
  without a checksum, because they are built by the same release. Each has
  its own SBOM.
- **The .NET runtime,** where it is shipped: the binaries, and the image
  through them. It is listed as its runtime packs, at the version the restore
  downloaded.

The format is **SPDX 2.3 JSON**: `actions/attest` accepts SPDX 2.x documents
and not SPDX 3.0's JSON-LD.

### 2. SBOMs are generated from what was restored, never written by hand

- **Lockfiles everywhere.** `RestorePackagesWithLockFile` is on in every
  project (`Directory.Build.props`), and `RestoreLockedMode` whenever `CI` is
  `true`.
  - A project published Native AOT restores for every RID it ships
    (`VarveNativeRuntimeIdentifiers`). That way one lockfile serves every
    runner: a locked `publish -r` fails for a RID its lockfile lacks.
  - The resolved graph is checked in, and every change to it, transitive
    packages included, is a diff in the pull request.
  - `tests/fixtures/` is left out: those projects exist to fail.
  - The Aspire AppHost sample's lockfile is the one CI's ubuntu runner writes,
    because its SDK adds packages for the host it restores on. CI restores it
    only there.
- **The SBOM is generated from the restore** of the build that produced the
  artefact (ADR 0225). Every component's version and SHA-512 must equal the
  lockfile's.
- **SBOMs are deterministic.** The same commit and creation time give the same
  bytes on every OS, and CI generates them twice and requires byte equality.
  - **The creation time is `SOURCE_DATE_EPOCH`, and at a release it is the
    descriptor's `date` at 00:00Z, not the commit's time.** The commit that
    lands a descriptor does not exist when the descriptor's digests are
    written. For the same reason, no SBOM carries the commit id; the build
    provenance attestation does. This is the maintainer's decision D3.

### 3. SBOMs are published and attested

- **Each SBOM is published:**
  - embedded in its nupkg at `_manifest/spdx_2.3/manifest.spdx.json`. NuGet
    defines no path (NuGet/Home#12497 is open); this is the layout Microsoft's
    sbom-tool uses;
  - attached to the GitHub release.
- **Each nupkg, the tool's among them, gets two attestations,** as the image
  does: build provenance and the SBOM. Each CLI binary gets the same when #129
  distributes them. Both attestations are keyless, through GitHub's OIDC
  identity.
- **Both attestations precede the push.** No package reaches nuget.org
  without them.
  - **Where the push is.** It is a job of its own (`push`). It needs the
    publish job, which attests every package's provenance, and the
    `attest-packages` job, which attests each package's SBOM. Before minting a
    credential, it checks that the packages it pushes are the attested
    `SHA256SUMS`.
  - **Why the SBOM attestations cannot be steps in the job that pushes:**
    - `actions/attest` takes one SBOM per call, and the packages are a list the
      pack produces (27 today);
    - a job's steps are fixed when the workflow is read, and a step cannot loop
      over an action;
    - so one attestation per package is a matrix, which is jobs, and a step
      cannot wait for jobs;
    - one literal step per package would be a second list of the packages,
      beside `Varve.slnx`, that drifts the first time a package is added.
  - **Why the move costs nothing.** `actions/attest` needs only `id-token:
    write` and `attestations: write`, so it can run in any job. nuget.org's
    trusted-publishing policy names the repository, the workflow file and,
    optionally, the environment, never the job. The push keeps both its file
    and its `release` environment.
- **The release path has a dry run.** `publish.yml` runs on a pull request
  that touches the release pipeline, and on a dispatch, at the head, as its
  landing would cut it: the one pending descriptor's version, or MinVer's.
  - **What it runs:**
    - every gate;
    - the SBOMs, against the descriptor;
    - the pack;
    - every package's provenance and SBOM attestations;
    - the release's assets and `SHA256SUMS`;
    - the verification, against what the run built.
  - **What it skips:**
    - the push to nuget.org;
    - the push to GHCR, and the multi-platform manifest that needs the
      registry. Each platform's image is built as an OCI layout instead, and
      its manifest, a file whose SHA-256 is the image's digest, gets both
      attestations and is verified by that digest;
    - the GitHub release;
    - the check of nuget.org's copies.
  - **So the first tag is not the first run** of anything in the release path
    but the pushes.
  - **A dry run's attestations never satisfy a release's verification.** The
    release's runs `gh attestation verify --source-ref refs/tags/<version>`,
    and a dry run's attestations are signed for a pull request's ref.
- **The release also carries the packages exactly as they were attested,**
  with an attested `SHA256SUMS`.
  - nuget.org adds its repository signature to every package it accepts, as an
    entry of its own (`.signature.p7s`). So the package it serves is not the
    attested package's bytes, and `gh attestation verify` on the nuget.org
    download fails by construction. This was found while building this
    ADR's gate: the draft assumed nuget.org's copy could be verified directly.
- **After a release, CI verifies what was published as a consumer would:**
  - `gh attestation verify --source-ref refs/tags/<version>` on each package
    attached to the release, for both predicates, with each embedded SBOM
    required to be the release's SBOM asset;
  - each package downloaded from nuget.org, every entry but the signature
    required to be the attested package's, byte for byte
    (`eng/nuget-served.cs`);
  - nuget.org's repository signature, with `dotnet nuget verify`;
  - the image, which the manifest job verifies, as before.
- **There is no NuGet author signing,** because it needs a code-signing key.
  nuget.org's repository signature plus the attestations take its place.

### 4. The register, and the gate that checks it

**`Directory.Packages.props` stays the one register** (maintainer's decision
D1).
- **A transitive package has no entry of its own.** It is admitted by the ADR
  of the package that brings it in.
- **Review sees it** through the lockfile diff.
- **The gate checks its licence and its native assets** like any other
  package's.

The register gains three kinds of item, none of which MSBuild reads:
- `ImplicitPackage`: a package an SDK references by itself, so no
  `PackageVersion` can name it (`Microsoft.NET.ILLink.Tasks`,
  `Microsoft.DotNet.ILCompiler`, `NETStandard.Library`, and the AppHost SDK's
  per-host packages). Each names what adds it, and its ADR.
- `PackageLicence`: the licence of a package that declares none by expression,
  read from the file or URL it declares, and stated with what it declares. A
  version that declares something else fails until it is read again.
- `NativeAssetException`: a shipped project that may carry native assets, with
  its ADR and reason. This is the allow-list `eng/native-assets.cs` kept in its
  code.

**The gate is `eng/dependency-register.cs`, extended, not a second gate.**
`eng/native-assets.cs` folds into it. Because it is a whole-graph check, it is
a CI step, not an analyzer.

It fails when:
- a direct package in any lockfile is admitted by neither a `PackageVersion`
  nor an `ImplicitPackage`;
- a register entry is resolved by no lockfile and no local tool;
- an entry cites an ADR that does not exist;
- a package in a shipped project's closure carries a native asset, outside a
  `NativeAssetException`. Constraint 1 on the transitive closure, as before;
- a licence is not on its scope's allow-list:
  - **runtime:** MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, MPL-2.0;
  - **build and test:** also any other OSI-approved licence, since nothing of
    theirs ships.

  A package is runtime when it is in what a consumer of a shipped package
  resolves, or in what a shipped project's output carries.
- an Action is referenced by tag or branch instead of a commit SHA (point 5);
- an audit suppression has no VEX statement, or a statement has no
  suppression (point 6).

**Only runtime packages appear in product SBOMs.**

**One named licence exception: `Microsoft.NETCore.Platforms` 1.1.0.**
- It declares the old .NET Library licence by its link
  (`go.microsoft.com/fwlink/?LinkId=329770`), which is not an OSI-approved
  licence.
- It is recorded as the package declares it, not restated as anything else.
- It is admitted for build only. It ships nothing: it reaches the build
  through `Varve.Analyzers`' `netstandard2.0` target and its analyzer
  packages.
- The gate fails if it ever enters a shipped closure.

**One native-asset exception: `Varve.Aspire`, under ADR 0117** (maintainer's
decision D2).
- It is a development-time package, an AppHost's, outside the three hosts of
  constraint 1.
- `Aspire.Hosting` brings `Hex1b`'s terminal interop, whose natives every
  AppHost carries regardless of Varve.
- Its SBOM lists those natives, and `docs/supply-chain.md` names it.

### 5. The build supply chain is recorded, not shipped

It covers:
- the SDK, pinned in `global.json`;
- the analyzer and Roslyn packages;
- Actions pinned by commit SHA;
- CI images, and now the base image, pinned by digest.

The build-provenance attestation records it. The product SBOMs do not list it,
because none of it ships.

**`global.json` rolls forward to the latest patch only** (`latestPatch`,
maintainer's decision D5). A feature band changes the compiler and the
analyzers, and a patch does not.

**CI installs exactly the patch `global.json` pins** (revised 2026-10-28).
- The SDK's patch fixes the versions of the packages it references by itself
  (`Microsoft.NET.ILLink.Tasks`, `Microsoft.DotNet.ILCompiler`), and every
  lockfile records them (point 2). A machine on another patch writes other
  lockfiles, and a locked restore fails on them. The devcontainer's SDK
  10.0.401 against lockfiles written with 10.0.403 was that failure.
- So `latestPatch` governs a machine that has only another patch of the band,
  and nothing in CI. Every workflow installs the SDK through one composite
  action, `.github/actions/setup-dotnet`, which reads the version from
  `global.json` and installs exactly it, with roll-forward disabled. No step
  names a version. The devcontainer pins the same version.
- **Dependabot's `dotnet-sdk` ecosystem bumps `global.json`.** Its pull
  request is red until it is completed: `dotnet run eng/lockfiles.cs` on the
  new SDK regenerates every lockfile and the devcontainer's pin, and the
  pull request's diff then shows ILLink and ILCompiler moving with the SDK. A
  major version is a target framework's decision, never a bump.
- **A lockfile from another patch fails before the restore, naming the
  script.** `eng/lockfiles.cs --check`, the first step after the SDK is
  installed and a job of `eng/ci.cs`, compares each lockfile's ILLink and
  ILCompiler with the running SDK's bundled version, and says whether the
  lockfile was written with a newer or an older patch. A fixture under
  `tests/fixtures/lockfiles/` proves the failure.

The gate fails on an Action referenced by tag or branch (point 4). #89 does not
cover that.

### 6. Vulnerability handling

- **`NuGetAudit` runs over all packages, direct and transitive** (mode `all`,
  level `low`), and its warnings are errors, as they already were.
- **A vulnerability is exempted only by a `NuGetAuditSuppress`** with a
  matching OpenVEX statement in `security/vex.openvex.json`, status
  `not_affected` with a justification or impact statement. The VEX document is
  published with every release, empty or not, and the gate checks the two
  halves against each other.
- **GitHub private vulnerability reporting is enabled,** and Dependabot alerts
  are on as a cross-check. Both are declared in `.github/repo-standard.yaml`
  and applied by the maintainer.
- **`SECURITY.md` states** the disclosure policy, the contact, and the support
  period.
- **Varve's own vulnerabilities are published as GitHub security advisories**
  with a CVE id, with GitHub as the CNA.
  - Manufacturers integrating Varve then have what they need for their own
    Article 14 reports.
  - Varve does not report to ENISA's Single Reporting Platform, because it is
    not a manufacturer.
- **The support period** (maintainer's decision D4):
  - before 1.0, only the latest preview receives fixes;
  - from 1.0, the latest `1.minor` receives fixes;
  - when `2.0.0` ships, the previous major receives security fixes for 12
    months after it.

### 7. The release descriptor records the SBOMs

ADR 0102's descriptor goes to format 2 and gains an `sbom:` list. It has one
line per shipped artefact: the artefact's kind, its name, and the SHA-256 of
its SBOM. The release becomes the auditable unit: basis, approver, artefacts
and contents.

**The line carries the SBOM's digest, not the artefact's.**
- The descriptor is part of the commit that builds the artefacts, and a
  package or binary embeds the commit it was built from.
- So no descriptor can hold the digest of its own build. Two clean publishes
  of the CLI were byte-identical, but would differ from any commit that
  changed the descriptor.
- The artefact's bytes are bound by its two attestations (subject = the
  artefact's digest) and by the attested `SHA256SUMS` on the release.

### 8. A page for the people doing the assessment

`docs/supply-chain.md` says:
- where the SBOMs, the VEX document and the attestations are, and how to
  verify them;
- the dependency policy (constraints 1 and 4, and the register), with
  `Varve.Aspire`'s exception;
- the vulnerability process and the support period.

It is written for a NIS2 entity's supplier assessment and for a manufacturer's
component due diligence.

## Alternatives considered

- **A hand-maintained list of dependencies.** Rejected: it goes stale on the
  first Dependabot pull request, and nobody can check it against what ships.
- **`eng/dependencies.json`, a second register listing all 213 packages**, as
  the draft proposed. Rejected by the maintainer (D1). It would change on every
  Dependabot pull request, for no decision the lockfile diff does not already
  show. One register, checked against the lockfiles, gives the same coverage.
- **GitHub's dependency-graph SBOM export as the SBOM.** Rejected as the
  primary source. It covers the whole repository rather than one artefact,
  mixes test and build packages with runtime ones, and reflects manifests
  rather than the bytes that were built. It stays useful as a cross-check.
- **CycloneDX instead of SPDX.** Rejected for consistency: the container
  already shipped SPDX, and OpenVEX covers VEX.
- **Doing nothing, because the CRA does not bind non-commercial open
  source.** Rejected by the maintainer. The obligations fall on Varve's users,
  and they cannot meet them without this.
- **NuGet author signing.** Rejected: it needs a long-lived signing key, and
  keyless attestation gives the same verifiability without one.
- **The commit's time as `SOURCE_DATE_EPOCH` at a release.** Rejected (D3). The
  descriptor's digests are written before that commit exists.
- **Keeping `latestFeature`.** Rejected (D5). A feature band is a new compiler
  and new analyzers, which is a change to review, not to roll into.

## Consequences

**The gate:**
- **Every shipped artefact has an SBOM:** attached to the release, embedded in
  the nupkg where one exists, and attested.
- **An SBOM generated twice from the same commit is byte-identical** (the
  `sbom` job in `eng/ci.cs`).
- **Each SBOM's components are the lockfile's**, version and SHA-512.
- **The register gate fails on each case in point 4**, each tested by a
  deliberately broken fixture in `tests/fixtures/dependency-register/`.
- **`NuGetAudit` fails the build** on a known vulnerability without a VEX
  statement.
- **`gh attestation verify` succeeds** for every nupkg, as the release carries
  it, and for the image. nuget.org's copy is checked to be that package plus
  nuget.org's own signature. All of it runs in CI against the published
  artefacts after the release (`publish.yml`, `verify`).
- **The descriptor's `sbom:` digests match the published files.** They are
  checked before anything is pushed.

**Changed with this ADR:**
- ADRs 0009, 0029, 0102 and 0111, by dated amendment;
- `publish.yml`, `ci.yml`, `Directory.Build.props`, `Directory.Build.targets`,
  `Directory.Packages.props`, `global.json`, the `Dockerfile`, and Dependabot's
  configuration;
- the lockfiles;
- `eng/dependency-register.cs`, `eng/sbom.cs`, `eng/nuget-served.cs` and
  `eng/lib/Restores.cs`; `eng/native-assets.cs` is removed;
- `security/vex.openvex.json`, `SECURITY.md`, `docs/supply-chain.md`,
  `docs/releases.md`, and `.github/repo-standard.yaml`;
- issue #15: its container item is "every shipped artefact", with the support
  period.

**Checked against the accepted ADRs:**
- **0009:** the register stays the one register, amended.
- **0029:** trusted publishing is unchanged, and the release gains assets.
- **0037:** no API key and no long-lived credential is added.
- **0066:** filed unaccepted.
- **0068:** the amendments are dated.
- **0099:** pinned images.
- **0102:** amended.
- **0111:** amended.
- **0117:** `Varve.Aspire`'s exception stands.

**Layer ownership.** None; this is the build and the release.

**Analyzer rule.** None. The checks read lockfiles, restore graphs, nuspecs and
workflows, which no compilation sees. Their failure paths are the fixtures.

**Open questions owned.** None.
