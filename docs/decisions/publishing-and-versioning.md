---
set: publishing-and-versioning
namespace: varve
adr: 0029
decisions:
  - key: PackageMetadataSetOnce
    statement: "Package metadata is set once in Directory.Build.targets, with the licence as an SPDX expression and the icon embedded, never licenseUrl or iconUrl"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: HostNamedOnce
    statement: "PackageProjectUrl is the only value naming the host, and RepositoryUrl is derived from the git remote at pack time"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: VersionFromTagByMinVer
    statement: "A package's version comes from its git tag through MinVer, and no Version property exists"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: PrereleaseUntilSparqlConformance
    statement: "The first tag is v0.1.0-preview.1, and versions stay 0.x prerelease until the core passes the SPARQL conformance suites"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: PublishOnTagAfterEveryGate
    statement: "publish.yml publishes on a v* tag only after the full suite and every gate pass, and never from a pull request"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: TrustedPublishingNoApiKey
    statement: "Publishing exchanges the workflow's OIDC token for a short-lived key, pushes immediately after login with --skip-duplicate, and stores no API key"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: PackDryRunOnPullRequests
    statement: "Every pull request packs and eng/package-metadata.cs reads each package's nuspec back, failing closed on a missing repository element"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: SymbolPackagesEmbedSources
    statement: "Each package ships a .snupkg with embedded sources"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0029](../adr/0029-publishing-and-versioning.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Moved to a later set: `PackageLicenseExpression` `Apache-2.0` is superseded by ADR 0031, in its
set.
