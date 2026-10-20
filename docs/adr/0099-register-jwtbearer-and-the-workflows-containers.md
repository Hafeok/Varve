# 0099 — Register: `JwtBearer` and `Microsoft.IdentityModel.*` in the server; no Testcontainers; the shared framework

## Status

**Accepted — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Decided by the maintainer on the 7a plan: "JwtBearer in Varve.Server only with
a register ADR and a 0037 amendment, conditional on the server's AOT publish
staying green". Acceptance is the maintainer's act on the pull request.

**Revisit condition — and the condition of this decision:** the Native AOT
publish of `Varve.Server` stops being green because of these packages. A trim
or AOT warning they raise is a failure of the condition, not a warning to
suppress. If it fails, the server writes its own bearer handler over the shared
framework, as the alternative below says.

## Context

ADR 0037's consequences state: "No new runtime package. `JwtBearer` is in the
shared framework; the register gains no entry." **That is false.**
`Microsoft.AspNetCore.Authentication.JwtBearer` has been a NuGet package, not
part of `Microsoft.AspNetCore.App`, since ASP.NET Core 3.0. For `net10.0` it
depends on `Microsoft.IdentityModel.Protocols.OpenIdConnect`, which brings:

- `Microsoft.IdentityModel.Protocols`;
- `Microsoft.IdentityModel.Tokens`;
- `Microsoft.IdentityModel.JsonWebTokens`;
- `Microsoft.IdentityModel.Logging`;
- `Microsoft.IdentityModel.Abstractions`.

The error was found while planning milestone 7a, by reading the package's
NuGet page against the shared framework's contents. ADR 0037's decision —
validate OIDC bearer tokens with `JwtBearer`, no `Microsoft.Identity.Web`, no
MSAL — stands. Only its accounting was wrong.

Milestone 7a also brings what is in the shared framework, and what the tests
need to stand up identity providers.

## Decision

1. **`Varve.Server`, and nothing else, takes
   `Microsoft.AspNetCore.Authentication.JwtBearer`** and its transitive
   `Microsoft.IdentityModel.*` closure as runtime dependencies.
   - Versions are resolved from NuGet into `Directory.Packages.props` with
     `Adr="0099"` (ADR 0009).
   - The direct package is pinned. The transitive ones are listed in the
     register as they resolve.
   - All are managed code, MIT-licensed and published by Microsoft. None ships
     a native asset (`eng/native-assets.cs` checks).
   - `Varve.Protocol` references none of them (ADR 0091).
2. **The ASP.NET Core shared framework** (`Microsoft.AspNetCore.App`, by
   `FrameworkReference`) is what `Varve.Protocol` and `Varve.Server` build on:
   - Kestrel, routing, minimal APIs, the authorisation middleware, problem
     details, `MultipartReader`;
   - the configuration-binding source generator;
   - `System.Text.Json`'s source generator.

   It is part of the runtime, not a package. The register records it as the
   framework both packages target, with no version of its own: it is the
   runtime's (ADR 0008).
3. **Test-time: no new package.**
   - The in-process server and the in-process OIDC issuer run on Kestrel on
     loopback, from the shared framework.
   - `Microsoft.AspNetCore.TestHost`, a NuGet package, is not taken. Kestrel
     tests what it would fake: trailers, aborted connections, chunking.
   - Tokens are signed with the BCL's RSA, so there is no JWT library in the
     tests.
4. **The identity providers of the CI legs are containers the workflow starts**:
   - `ghcr.io/navikt/mock-oauth2-server`;
   - Zitadel and PostgreSQL;

   each pinned by digest in a compose file, from a registry without an
   anonymous pull limit (ghcr.io, and ECR Public's mirror of the official
   PostgreSQL image; never Docker Hub, whose limit a shared runner's address
   exhausts). They are not packages: the register lists them in a comment,
   with tag and licence, and the compose files hold the digests, which
   Dependabot watches. **There is no Testcontainers** or other package that
   starts a container from a test: a test that needs a provider reads its
   address from the environment and skips when it is absent. The workflow,
   and the devcontainer through Docker-in-Docker, start them (ADR 0100).

## Alternatives considered

- **Our own bearer handler** over the shared framework's
  `AuthenticationHandler<T>`, the BCL's RSA and ECDsa, and `System.Text.Json`
  source generation: OIDC discovery, JWKS with rotation, an `alg` allow-list,
  `iss`, `aud`, `exp` and `nbf` with skew. It needs no package, and it is
  security-critical code that every deployment would trust and that would be
  ours alone to get right. It is the fallback if the condition fails, not the
  choice.
- **`Microsoft.Identity.Web`.** Rejected by ADR 0037, unchanged.
- **Testcontainers for .NET.** Starts containers from the test, so a developer
  runs one command. It is a package tree with a Docker client, it couples the
  test to a container runtime, and it cannot run in the cloud sandbox, where
  the tests must skip cleanly anyway. The workflow already owns the
  environment.
- **`Microsoft.AspNetCore.TestHost`.** An in-memory server; faster to start.
  It is a package, and it does not exercise the transport behaviour ADR 0095
  depends on.

## Consequences

- `dotnet run eng/dependency-register.cs` lists the new runtime packages
  against this ADR.
- The AOT publish job of `Varve.Server` (ADR 0101) is this decision's check.
- ADR 0037 carries a dated amendment that points here.

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0009**: the register;
  - **0037**: its consequence corrected, its decision unchanged;
  - **0036**: the devcontainer;
  - **0063**: build-time packages, none new.

  No conflict.
- **Layer ownership.** `Varve.Server`, layer 6.
- **Analyzer rule.** None. The register gate already enforces it.
- **Open questions owned.** None.

## Amendment, 2026-10-15 — the storage services, from MCR and GHCR by digest

Filed by milestone Object storage of #92, unaccepted until the maintainer
accepts it (ADR 0066). Point 4's list of containers the workflow starts gains
the storage job's two, in `.github/storage/compose.yaml`, each pinned by
digest:

- **Azurite** 3.37.0 from `mcr.microsoft.com`. Microsoft Container Registry
  has no anonymous pull limit; ADR 0111 already takes the base image from it.
- **RustFS** 1.0.1 from `ghcr.io`, Apache-2.0, anonymous pull. It is the
  S3-compatible server the contract suite chose over SeaweedFS (ADR 0134).
  MinIO's images are no longer published for anonymous pull.

Point 1's register gains the server's references to `Varve.Store.ObjectStorage`,
`Varve.Store.Azure` and `Varve.Store.S3`, all project references; no package
is added (ADR 0134 decided REST over `HttpClient`). The rule stands: the
workflow starts a container, and a test reads where it is and skips when it is
absent, unless `VARVE_STORAGE_LEGS_REQUIRED` names its leg.

**The ledger.** The rulings are ADR 0134's `the-object-store-contract` set.

## Amendment, 2026-10-19 — CI's test containers from this repository's mirror

Filed by #118, unaccepted until the maintainer accepts it (ADR 0066).

**Why.** Point 4 takes the test containers from "a registry without an
anonymous pull limit", and ECR Public's anonymous data cap is one:
- on 2026-10-16 it refused `container (linux-x64)`'s PostgreSQL pull on `main`
  (run 38890772587);
- from 2026-10-18 10:16 to 2026-10-19 06:41 it refused `auth (zitadel)`'s pull
  on every run, on every runner, and on the maintainer's machine.

The job count that shares a runner's address keeps growing.

**The decision.**
- **One list.** `.github/ci-images.yaml` lists every third-party test container
  CI starts. Each row gives:
  - the upstream reference;
  - the manifest digest, which pins every platform;
  - the licence;
  - the jobs that start it;
  - why.
- **One mirror.** `mirror-ci-images.yml` runs on `main` only, when the list
  changes or by dispatch.
  - It copies each row by digest, a carbon copy of the index, to a private
    package of this repository: `ghcr.io/hafeok/varve-ci/<name>`.
  - It checks that the copy's digest is the pinned one, and that the package
    is not readable anonymously.
  - The packages are linked to this repository and readable by its Actions.
    Nothing is redistributed.
- **CI pulls only from the mirror**, after `docker login ghcr.io` with
  `GITHUB_TOKEN` (`packages: read`). The exceptions:
  - **A row marked `mirror: false` with a stated reason** is pulled upstream at
    its digest. That is only the Event Hubs emulator: its licence is a
    proprietary EULA, a hosted copy would raise a redistribution question, and
    `mcr.microsoft.com` has no anonymous limit.
  - **`ghcr.io/hafeok/varve`**, this repository's own image, which CI builds.
  - **Not test containers, so not in the list:** the devcontainer's base image,
    and the server Dockerfile's chiseled base. The chiseled base is the
    shipped image's, and its provenance and SBOM name its registry (ADR 0111).
- **Code that starts a container itself** names the upstream image pinned by
  the row's digest, so that anyone can run it. The CI job that runs it
  overrides it to the mirror, and the row's `allowances` name the file and
  that job. Two such places exist:
  - the Aspire sample's issuer (`Varve:IssuerImage`, ADR 0117);
  - `Varve.Aspire`, which starts only `ghcr.io/hafeok/varve`.
- **A mirror may be seeded by hand from any registry at the pinned digest.**
  The digest is the image's identity, and the source is not. CI itself still
  never pulls from Docker Hub. The mirror workflow checks a package already
  present as it checks a copy. PostgreSQL was seeded this way on 2026-10-19,
  from `docker.io/library/postgres`, while ECR refused every caller.
- **A new row comes out public on its first copy.** A package a workflow
  creates in a public repository inherits its visibility. So the first run
  after a new row is red, by design. The maintainer then makes the package
  private in its settings and re-runs the workflow, which finds the package
  present and passes. This is the expected sequence for every new row.
- **Updates come from the list, not from Dependabot.** Its docker-compose
  ecosystem is switched off for these files, since it cannot read the mirror.
  `ci-images-upstream.yml` runs weekly. It compares each upstream tag's
  digest with the pinned one, and opens or updates one issue listing the
  differences. A bump is a pull request to the list and to the reference.
- **Local runs, the devcontainer's included,** use the upstream references at
  the same digests. `dotnet run eng/ci-images.cs -- --local <compose file>`
  writes the override (`CONTRIBUTING.md`).
- **The gate** is `eng/ci-images.cs`, a job in `eng/ci.cs`. Every image
  reference under `.github/`, and every image C# names, must be the mirror's
  at the listed digest, or one of the exceptions above. Its fixture, which
  references an upstream image directly, must fail it.

**#118's done-when, narrowed.** Every third-party test container CI starts
comes from `ghcr.io/hafeok/varve-ci/`, or is a `mirror: false` row. The two
base images above are the stated exceptions.

The rest of point 4 stands:
- the workflow starts the containers, and no test or package does;
- every image is pinned by digest;
- CI never pulls from Docker Hub.

**The ledger.** The rulings of this block are `TestContainersFromTheMirror`
and `MirrorSeededByDigest`, unaccepted until the maintainer accepts them.
