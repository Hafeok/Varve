---
set: cli-distribution
namespace: varve
adr: 0226
decisions:
  - key: SixBinariesFromOneRidList
    statement: "The CLI ships as a Native AOT binary for each RID in VarveNativeRuntimeIdentifiers (Directory.Build.props), built on a runner of that RID, the executable alone archived as varve-<version>-<rid>.tar.gz or .zip and run once to say the release's version; ci.yml's native aot job builds and smoke-runs the same six on every pull request"
  - key: EveryBinaryIsAttestedAndNamed
    statement: "Each archive carries a keyless build-provenance and SBOM attestation on its digest, the descriptor's sbom: names one binary line per RID with its SBOM's SHA-256, and eng/release.cs requires them from the first commit that carries eng/winget.cs"
  - key: ChecksumsCoverEveryAssetBeforeThePush
    statement: "SHA256SUMS is written and attested in release-assets over the packages, the archives and the winget manifests, and the push to nuget.org checks the packages against it, so nothing is pushed until every artefact the release carries exists and is attested"
  - key: TheTapUpdatesItselfKeylessly
    statement: "Hafeok/homebrew-varve's own workflow reads Varve's newest release (prereleases included until a 1.0.0 or later exists), verifies each archive's attestation for the tag, writes the formula, smoke-installs it on macOS arm64, macOS x64 and Linux, and commits with the tap's own GITHUB_TOKEN; Varve's workflows never push to the tap"
  - key: WingetIsPreparedByCiAndSubmittedByHand
    statement: "CI writes, validates and at a tag installs the winget manifests for Hafeok.Varve and attaches them to the release; the maintainer submits them with an interactive sign-in, and no credential for winget is stored anywhere, because an App installation token cannot open a pull request outside its installation"
---

The rulings of [ADR 0226](../adr/0226-cli-distribution.md), filed unaccepted by
readiness B for #129 (ADR 0066).
