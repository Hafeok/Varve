---
set: the-sbom-generator
namespace: varve
adr: 0225
decisions:
  - key: TheGeneratorIsInEng
    statement: "SBOMs are written by eng/sbom.cs, a file-based script with no package, from obj/project.assets.json, the project's lockfile and the global packages folder"
  - key: TwoClosures
    statement: "A library's SBOM is what a consumer of the package resolves, without suppressed edges; the tool's and a binary's is every package with a deployed asset in the target, and a binary's also the .NET runtime packs for its RID"
  - key: Spdx23Json
    statement: "The format is SPDX 2.3 JSON with LF line endings, sorted arrays, and a UUIDv5 namespace of the document's content"
  - key: SyftForTheBaseImageOnly
    statement: "The image's OS packages are Syft's, from the base image pinned by digest at a pinned Syft version, kept under platform-prefixed identifiers without Syft's namespace, time or files"
  - key: DeterminismIsAGate
    statement: "CI generates every SBOM twice and requires the two to be byte-identical"
---

The rulings of [ADR 0225](../adr/0225-the-sbom-generator.md),
filed unaccepted by readiness B for #119 (ADR 0066).
