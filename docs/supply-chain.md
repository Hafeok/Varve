# Supply chain

This page is for the person assessing Varve as a component.

- **A NIS2 supplier assessment.** An essential or important entity that runs
  Varve manages the security of its direct suppliers under Article 21(2)(d) of
  Directive (EU) 2022/2555 (in Denmark, Cybersikkerhedsloven).
- **A manufacturer's component due diligence.** Anyone who builds Varve into a
  product they place on the EU market does this under the Cyber Resilience Act
  (Regulation (EU) 2024/2847).

Varve is open source (MPL-2.0) and is not supplied in the course of a
commercial activity. It meets the CRA's SBOM and vulnerability-handling
requirements anyway, so that the people who depend on it can meet theirs
([ADR 0224](adr/0224-supply-chain-transparency.md)).

Everything below is checked by a gate on every pull request, and again at the
release. Nothing is a promise kept by hand.

## What each artefact contains

Every artefact Varve ships has one **SPDX 2.3 SBOM** of its runtime closure:

| Artefact | Where | What its SBOM lists |
|---|---|---|
| Each `Varve.*` package | nuget.org | what a consumer of the package resolves |
| The `varve` tool (`Varve.Server`) | nuget.org | everything the tool package carries |
| The container image | `ghcr.io/hafeok/varve` | each platform's binary and its closure, the .NET runtime, and the base image's OS packages |
| The CLI binaries, per RID | the GitHub release, once #129 distributes them | the closure compiled in, and the .NET runtime |

**Each component has:**
- its package URL (`pkg:nuget/…`, `pkg:deb/…`) and version;
- the SHA-256 of the package, and the SHA-512 NuGet verified at restore;
- its licence;
- its supplier;
- what depends on what.

**Build and test packages** (analyzers, test frameworks, the benchmark
harness) are not listed, because none of them ships. The build provenance
records them (below).

**SBOMs are generated from the locked restore**, never written by hand
([ADR 0225](adr/0225-the-sbom-generator.md)).
- Every project restores from a checked-in `packages.lock.json`, and CI
  restores in locked mode.
- Generated twice from the same commit, an SBOM is byte-identical. CI checks
  this on every pull request.

**Where to get them:**
- **Inside each package**, at `_manifest/spdx_2.3/manifest.spdx.json`.
- **Attached to each GitHub release**, as `<Package>.<version>.spdx.json`. The
  image's is `varve-image-<version>.spdx.json`.
- **As attestations** on each package and on the image (below).

**The release descriptor records them.** Each release's descriptor,
`releases/<version>.yaml`, names every artefact with the SHA-256 of its SBOM,
under `sbom:`. The descriptor is reviewed and approved before the release is
cut ([ADR 0102](adr/0102-a-release-is-a-descriptor.md)), so the SBOM you
download can be checked against a line a human approved:

```sh
sha256sum Varve.Rdf.0.1.0-preview.16.spdx.json
grep 'nupkg: Varve.Rdf ' releases/v0.1.0-preview.16.yaml
```

## Where it came from

**Each package and the image carry two attestations, both keyless,** signed
through GitHub's OIDC identity for this repository's `publish.yml` with
Sigstore. No signing key exists to be stolen.
- **SLSA v1 build provenance:** the workflow, the commit, the runner, and
  the build supply chain:
  - the SDK, pinned by `global.json` to its latest patch;
  - every Action, pinned by commit SHA;
  - every CI image, and the container's base image, pinned by digest.
- **The SBOM**, as an attestation on the artefact's digest.

To verify a package as the release published it:

```sh
gh release download v0.1.0-preview.16 --repo Hafeok/Varve --pattern 'Varve.Rdf.*'
gh attestation verify Varve.Rdf.0.1.0-preview.16.nupkg --owner hafeok \
  --source-ref refs/tags/v0.1.0-preview.16 --predicate-type https://slsa.dev/provenance/v1
gh attestation verify Varve.Rdf.0.1.0-preview.16.nupkg --owner hafeok \
  --source-ref refs/tags/v0.1.0-preview.16 --predicate-type https://spdx.dev/Document/v2.3
```

`--source-ref` requires the attestation to come from the release's tag. The
release workflow also runs as a dry run on pull requests, attesting what it
builds there. Those attestations are signed for the pull request's ref, so
they never pass for a release.

And the image:

```sh
gh attestation verify oci://ghcr.io/hafeok/varve:v0.1.0-preview.16 --owner hafeok \
  --source-ref refs/tags/v0.1.0-preview.16 --predicate-type https://spdx.dev/Document/v2.3
```

**About the copy on nuget.org.** It is the same package plus nuget.org's
repository signature, `.signature.p7s`, which nuget.org adds to every package
it accepts. Its bytes therefore differ from the attested package's, and
`gh attestation verify` on the nuget.org download fails by construction. The
release's copy is the attested one.

After every release, CI checks the link between the two copies:
1. it downloads each package from nuget.org;
2. it requires every entry except the signature to be the attested package's,
   byte for byte (`eng/nuget-served.cs`);
3. it verifies nuget.org's signature with `dotnet nuget verify`.

You can do the same:

```sh
dotnet nuget verify --all Varve.Rdf.0.1.0-preview.16.nupkg
```

**Varve does not author-sign its packages.** That needs a long-lived
code-signing key, and the keyless attestations take its place.

## The dependency policy

- **100% managed: no native asset in a shipped artefact** (hard constraint 1).
  - The gate checks the transitive closure of every shipped package.
  - **One exception is stated:** `Varve.Aspire`, a development-time package
    for an Aspire AppHost, outside the three hosts the constraint is about.
    `Aspire.Hosting` brings `Hex1b`'s terminal interop, with its native
    libraries, into every AppHost regardless of Varve. `Varve.Aspire`'s SBOM
    lists them ([ADR 0117](adr/0117-varve-aspire.md)).
- **Every third-party package needs a decision** (hard constraint 4).
  - `Directory.Packages.props` is the register: each direct package names the
    ADR that admits it, and each transitive package is admitted by the package
    that brings it in.
  - The register gate fails on any of these:
    - a resolved package that nothing admits;
    - a register entry nothing resolves;
    - an ADR citation that does not exist;
    - a licence outside its scope's allow-list;
    - a native asset in a shipped closure;
    - an Action not pinned by commit SHA.
  - Each failure has a deliberately broken fixture in
    `tests/fixtures/dependency-register/`.
- **Licences.**
  - **What ships:** MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause or MPL-2.0.
  - **Build and test packages:** also another OSI-approved licence, since
    nothing of theirs ships.
  - **A package that declares no SPDX expression** has its licence read and
    stated in the register, beside what it declares, and is re-read when that
    changes.
  - **One build-only exception:** `Microsoft.NETCore.Platforms` 1.1.0 declares
    the old .NET Library licence. It reaches the build through
    `netstandard2.0` analyzers and ships nothing.
- **What ships, today:**

| Artefact | Third-party packages at runtime |
|---|---|
| 24 library packages | none |
| `Varve.Protocol.Client` | 8 `Microsoft.Extensions` abstractions (MIT) |
| `Varve.Server`, the tool and the CLI | 16 (MIT, Apache-2.0), and in the binaries and the image the .NET runtime |
| `Varve.Aspire` | 71, the `Aspire.Hosting` closure (MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause) |

## Vulnerabilities

- **Known vulnerabilities fail the build.** `NuGetAudit` checks every package,
  direct and transitive, against nuget.org's vulnerability database on every
  restore, with its findings as errors.
- **An exemption is published.** It exists only as a `NuGetAuditSuppress`
  matched by an OpenVEX statement in `security/vex.openvex.json`, status
  `not_affected`, saying why. The gate checks the two against each other. The
  document is attached to every release as `varve-<version>.openvex.json`,
  empty or not. Today it is empty.
- **Reporting:** privately, through GitHub's *Report a vulnerability*, or by
  email. [`SECURITY.md`](../SECURITY.md) has the policy and the response time.
- **Advisories:** GitHub security advisories with a CVE id, GitHub as the
  CNA, naming the affected and fixed versions. A manufacturer integrating Varve
  can cite them in its own reports under the CRA's Article 14. Varve is not a
  manufacturer and does not report to ENISA's Single Reporting Platform.
- **The support period:**
  - before 1.0, the most recent preview;
  - from 1.0, the latest `1.minor`;
  - after `2.0.0`, security fixes for `1.x` for 12 months.

  The same rule holds for each later major. `SECURITY.md` has the table.
