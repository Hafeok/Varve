# 0225 — The SBOM generator: `eng/sbom.cs`, SPDX 2.3, from the restore

## Status

**Accepted — filed unaccepted by readiness B for #119, 2026-10-27** (ADR 0066).
It was proposed on #119's plan and approved there by the maintainer.
Acceptance is the maintainer's act on the pull request.

It is under [ADR 0224](0224-supply-chain-transparency.md), whose points 1 to 3
it implements. The generator is a build tool with no package. Records decision
set [`the-sbom-generator`](../decisions/the-sbom-generator.md).

## Context

ADR 0224 needs one SPDX document per shipped artefact. Each document must:
- be generated from the locked restore;
- list the runtime closure, not the build packages;
- carry each component's checksum and licence;
- be byte-identical when generated twice from the same commit.

The container's SBOM was Syft's (`anchore/sbom-action`). The candidates were
checked on 2026-10-27 at their sources:

- **Syft, v1.55.0.**
  - Its `dotnet-packages-lock-cataloger` reads `packages.lock.json`. But a
    lockfile records no `PrivateAssets`, so every analyzer appears as a direct
    dependency.
  - Its `.deps.json` cataloger needs a framework-dependent build output. A
    Native AOT binary has none, which is why the image's SBOM had no .NET
    component (ADR 0224, Context).
  - The SPDX encoder writes no checksum for a NuGet package.
  - `documentNamespace` ends in a random UUID, and `created` is the time of
    the run. Its `SOURCE_DATE_EPOCH` change, anchore/syft#3932, is unmerged.
  - Package order is sorted, and the output is otherwise stable: two scans of
    the base image by digest differ only in those two fields.
- **Microsoft's sbom-tool, v4.1.13** (`Microsoft.Sbom.Targets`).
  - It writes SPDX 2.2 by default, and 3.0 on request.
  - Its root package's SWID tag is a `Guid.NewGuid()`, with no override.
  - Its NuGet components come from Component Detection's heuristic, not
    `PrivateAssets`, and dev packages are not filtered out.
  - Licences come from ClearlyDefined over the network.
  - Its MSBuild targets unzip and re-zip the nupkg after pack.
- **`actions/attest`**, which `attest-sbom` now wraps, accepts a document with
  top-level `spdxVersion` and `SPDXID`. That is SPDX 2.x, not 3.0's JSON-LD,
  and it accepts up to 16 MiB.

## Decision

1. **The generator is `eng/sbom.cs`**, a file-based C# script with no package,
   beside the gates. It shares its restore readers with the register gate
   (`eng/lib/Restores.cs`).
2. **Its input is the restore** of the project that builds the artefact:
   `obj/project.assets.json`, its `packages.lock.json`, and the global
   packages folder. It writes two closures:
   - **A library nupkg:** what a consumer of the package resolves. That is the
     graph from its direct dependencies, without the edges a consumer never
     sees (`suppressParent: All`, which is how `PrivateAssets="all"` is
     recorded), and with its project references as `Varve.*` packages.
   - **The tool and a Native AOT binary:** every package with a deployed
     asset (runtime, native or resource) in the target, the binary's RID
     target among them. The binary also lists the .NET runtime packs the
     restore downloaded for that RID.
3. **Each component's version and SHA-512 are the lockfile's**, and a
   disagreement with the restore graph fails. Its SHA-256 is computed from the
   package in the packages folder. Its licence is the nuspec's expression, or
   the register's `PackageLicence` statement as `licenseConcluded` (ADR 0224
   point 4). Its supplier is the nuspec's authors.
4. **SPDX 2.3 JSON**, with LF line endings and every array sorted.
   - `documentNamespace` is a UUIDv5 (RFC 9562 §5.5) of the document's
     content, so the same content gives the same namespace.
   - `created` is `--created`, else `SOURCE_DATE_EPOCH`. At a release it is the
     descriptor's date at 00:00Z (ADR 0224 point 2).
   - Neither the commit id nor any path appears.
5. **The image's SBOM** contains each platform's binary, with its closure and
   runtime.
   - The base image's OS packages are **Syft's**, read from the base image
     pinned by digest, one scan per platform, with the Syft version pinned.
     They are kept as Syft wrote them, licence references and relationships
     included, under identifiers prefixed with the platform.
   - Syft's namespace, creation time and file entries are not carried.
   - The image is `DESCENDANT_OF` each base and `CONTAINS` each binary.
6. **Determinism is a gate.** `--check-against` generates a second time and
   requires every file to be byte-identical to the first. The `sbom` job in
   `eng/ci.cs` runs it.
7. **The descriptor's lines** are `sbom.txt`, one `<kind>: <name>
   sha256:<digest>` per document. `--descriptor` checks a descriptor's `sbom:`
   list against them (ADR 0102, amended 2026-10-27).

## Alternatives considered

- **Syft for everything.** Rejected:
  - it cannot tell a runtime package from an analyzer in a lockfile;
  - it reads nothing from an AOT binary;
  - it writes no NuGet checksum;
  - its output is not deterministic without post-processing.

  It is kept for what it does well: the OS packages of an image.
- **Microsoft's sbom-tool.** Rejected:
  - a random GUID it cannot be told not to write;
  - dev packages in the output;
  - licences fetched over the network;
  - a re-zipped nupkg.
- **SPDX 3.0.** Rejected while `actions/attest` accepts 2.x only.
- **CycloneDX.** Rejected by ADR 0224.

## Consequences

**It is a build tool, not a package,** so constraint 4 needs no register
entry for it. Syft is an Action and a binary that CI downloads by pinned
version; the build provenance records it.

**A new kind of artefact needs a branch in the generator and a line kind in
the descriptor.** #167's Web UI bundle is `asset:` (ADR 0102, amended).

**Checked against the accepted ADRs:**
- **0009:** no package added.
- **0102:** the lines it writes.
- **0111:** the image's SBOM.
- **0066:** filed unaccepted.

**Layer ownership.** None.

**Analyzer rule.** None.

**Open questions owned.** None.
