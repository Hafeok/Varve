---
set: supply-chain-transparency
namespace: varve
adr: 0224
decisions:
  - key: OneSbomPerShippedArtefact
    statement: "Every shipped artefact (each Varve package, the tool, the container image, the CLI binaries, the Web UI bundle) has one SPDX 2.3 SBOM of its runtime closure, components by package URL with version, SHA-256, SHA-512, licence and supplier, and the .NET runtime where it is shipped"
  - key: SbomsComeFromTheLockedRestore
    statement: "Every project restores with a lockfile, CI restores in locked mode, and an SBOM is generated from the restore of the build that made its artefact, its components' versions and SHA-512 equal to the lockfile's"
  - key: SbomsAreDeterministic
    statement: "An SBOM generated twice from the same commit and creation time is byte-identical on every OS; at a release its creation time is the descriptor's date at 00:00Z, and no SBOM carries the commit id"
  - key: SbomsArePublishedAndAttested
    statement: "Each SBOM is embedded in its nupkg at _manifest/spdx_2.3/manifest.spdx.json, attached to the release, and attested keylessly beside a build-provenance attestation, and CI verifies both for every published package after the release"
  - key: AttestationsPrecedeThePush
    statement: "Every package's provenance and SBOM attestations are made before the push to nuget.org, which is a job of its own in publish.yml, under the release environment, and pushes only the attested SHA256SUMS"
  - key: TheReleasePathHasADryRun
    statement: "publish.yml runs every job on a pull request that touches the release pipeline, attestations, release assets and verification included, skipping only the pushes to nuget.org and GHCR, the registry's manifest and the GitHub release; a release's verification requires the tag's source ref"
  - key: OneRegisterCheckedAgainstTheLockfiles
    statement: "Directory.Packages.props is the one register; a transitive package is admitted by the ADR of the package that brings it in, and the gate checks every lockfile for unadmitted and stale entries and every resolved package's licence and native assets"
  - key: LicencesByScope
    statement: "A runtime package is MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause or MPL-2.0; a build or test package may also be under another OSI-approved licence; a package declaring no expression has its read licence stated in the register with what it declares"
  - key: PlatformsIsABuildOnlyException
    statement: "Microsoft.NETCore.Platforms 1.1.0 is recorded with the .NET Library licence it declares and admitted for build only, reaching the build through netstandard2.0 analyzers, and the gate fails if it enters a shipped closure"
  - key: AspireCarriesNativesByException
    statement: "Varve.Aspire, a development-time AppHost package, may carry Hex1b's native assets under ADR 0117, and its SBOM lists them"
  - key: ActionsArePinnedBySha
    statement: "Every Action in a workflow or action.yml is referenced by its full commit SHA, and the register gate fails on a tag or a branch"
  - key: SdkRollsForwardToPatchesOnly
    statement: "global.json pins the SDK with rollForward latestPatch: a feature band, which changes the compiler and analyzers, is a reviewed change"
  - key: AuditExemptionsNeedVex
    statement: "NuGetAudit runs over every package with its findings as errors, and a NuGetAuditSuppress stands only with an OpenVEX not_affected statement in security/vex.openvex.json, published with every release"
  - key: TheSupportPeriod
    statement: "Before 1.0 only the latest preview receives fixes; from 1.0 the latest 1.minor receives fixes, and when 2.0.0 ships the previous major receives security fixes for 12 months"
  - key: AdvisoriesThroughGitHub
    statement: "Varve's own vulnerabilities are reported privately through GitHub and published as GitHub security advisories with a CVE id, GitHub as the CNA"
---

The rulings of [ADR 0224](../adr/0224-supply-chain-transparency.md),
filed unaccepted by readiness B for #119 (ADR 0066).
