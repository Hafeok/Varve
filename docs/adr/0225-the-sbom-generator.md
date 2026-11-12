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

## Amendment, 2026-11-06 — the Web UI bundle

Filed by Web UI part 1 (#167) on its pull request, #187, at the maintainer's
direction of 2026-11-06, and accepted there (ADR 0066).

The Consequences say a new kind of artefact needs a branch in the generator
and a line kind in the descriptor. #167 ships one: `varve-webui-<version>.zip`,
the static files published from `src/Varve.WebUi`, attached to the release.
It is published on its own, in neither the tool nor the image, so it has an
SBOM of its own (ADR 0224 point 1).

- **Its input is the Web UI's restore for `browser-wasm`,** as point 2 has it
  for a binary: every package with a deployed asset in the target, and Varve's
  own libraries by their package URLs.
- **`Microsoft.AspNetCore.App.Internal.Assets` is named too.** It carries
  `blazor.webassembly.js`, the script `index.html` loads, as a static web
  asset, and a restore graph lists no static web asset as deployed. The SDK
  references the package by itself, the lockfile holds its version and hash,
  and a restore without it is a finding.
- **The .NET runtime is `Microsoft.NETCore.App.Runtime.Mono.browser-wasm`,**
  at the version of the runtime the SDK bundles.
  - The wasm-tools workload supplies the pack, so the restore graph does not
    name it.
  - The version is read from the lockfile's
    `Microsoft.NET.Sdk.WebAssembly.Pack`, which the SDK references by itself
    at that version, and which `eng/lockfiles.cs` holds to `global.json`'s
    SDK.
  - `publish.yml` fails a bundle published with any other runtime pack.
- **Emscripten is listed by pack, not by library.** The relink runs
  Emscripten and links its system libraries into `dotnet.native.wasm`. The
  wasm-tools workload supplies it in three packs, each a NuGet package, named
  by package URL at the workload's version, which is the runtime pack's:
  - `Microsoft.NET.Runtime.Emscripten.3.1.56.Cache.linux-x64`: the prebuilt
    system libraries, libc and compiler-rt among them. The bundle contains
    what the relink links from it;
  - `Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.linux-x64`: the compiler and
    linker, a build tool;
  - `Microsoft.NET.Runtime.Emscripten.3.1.56.Node.linux-x64`: the Node.js
    that runs Emscripten's scripts, a build tool.

  The two build tools are related to the bundle by `BUILD_TOOL_OF`, never
  `CONTAINS`. The libraries inside the first pack are not listed one by one,
  nor are their own licences: each pack declares its licence by URL, and its
  `LICENSE` file, read on 2026-11-06, is the MIT licence. Emscripten's
  version and the publish runner's RID are in the packs' names, which no
  restore gives, so they are constants of `eng/sbom.cs`; `publish.yml` fails
  a bundle whose build had other Emscripten packs installed than these.
- **Nothing outside NuGet.** The bundle has no npm or other JavaScript
  lockfile. Its one script of its own, its two stylesheets and its mark are
  this repository's files, and are the root's. A dependency resolved outside
  NuGet would first need its lockfile checked in and restored locked, each
  package in the register with its scope, ADR and licence, and its components
  listed with `pkg:npm` URLs (ADR 0224 points 2, 4 and 5).
- **The list is what the bundle can carry.** The publish trims, so an
  assembly the restore deploys may be absent from the bundle. None is present
  that the list lacks.
- **The descriptor's line** is `asset: varve-webui-<version>.zip sha256:…`.
  `eng/release.cs` requires it while `src/Varve.WebUi` declares
  `VarveShipped`.
- **Publishing.** `publish.yml` attests the zip keylessly with its build
  provenance and with this SBOM, attaches both to the release, and verifies
  both after it, as for a package (ADR 0224 point 3).
- **The rule has its fixtures** (ADR 0224's gate): `eng/sbom.cs --fixtures
  tests/fixtures/sbom-descriptor` passes a descriptor whose `asset:` line
  carries the generated digest, and refuses one whose digest differs and one
  with no line. `tests/fixtures/releases/missing-asset-line` is
  `eng/release.cs`'s case.

Records decision `TheWebUiBundleFromItsRestore`.
