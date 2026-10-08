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
