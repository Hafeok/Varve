---
set: the-container-image
namespace: varve
adr: 0111
decisions:
  - key: GhcrOnly
    statement: "The image is ghcr.io/hafeok/varve on GitHub Container Registry alone; Docker Hub is a roadmap note for a mirror on demand under an org-owned namespace"
  - key: NativeAotPerArchitectureOnNativeRunners
    statement: "publish.yml publishes Varve.Server with Native AOT on ubuntu-24.04 and ubuntu-24.04-arm, builds one image per architecture from that binary, and joins them by digest into one multi-architecture manifest"
  - key: ChiseledRuntimeDepsBase
    statement: "The base is mcr.microsoft.com/dotnet/runtime-deps chiseled, the one pull outside GHCR and ECR Public, acceptable because MCR has no anonymous rate limit of the kind that bit CI and Docker Hub does"
  - key: NonRootReadOnlyVolumePort8080
    statement: "The image runs as the non-root app user on a read-only root filesystem with datasets on a volume at /var/lib/varve and listens on 8080; it carries no HEALTHCHECK, the orchestrator probing /health/ready"
  - key: TaggedByDescriptorVersion
    statement: "The image is tagged by the release descriptor's version, latest only for a version with no prerelease part, and a pull request's CI publishes pr-<sha> for its own suites alone"
  - key: AttestedProvenanceAndSbom
    statement: "publish.yml attests build provenance and an SPDX SBOM for the manifest's digest through GitHub artifact attestations and verifies both with gh attestation verify"
  - key: PullRequestCiRunsTheImageItBuilt
    statement: "The pull request's CI runs the W3C protocol suites and both auth legs against a container pulled from the image it built, read-only, on a volume, as the non-root user"
---

The rulings of [ADR 0111](../adr/0111-the-container-image.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
