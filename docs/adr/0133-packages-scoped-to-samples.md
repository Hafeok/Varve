# 0133 — Packages scoped to samples, and the register's scope check

## Status

**Accepted — filed unaccepted by milestone Client and integration of #93,
2026-10-13** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Client and integration plan: the
scope is checked by a gate with a failing fixture, and a samples-only package
referenced from `src/` is red.

**Amends** ADR [0009](0009-dependency-policy-and-register.md) by a dated block
(ADR 0068): a register entry may carry a scope, and the gate enforces it.

## Context

The milestone's samples show how an application uses Varve with the
libraries an application really uses: Microsoft Identity Web for the
on-behalf-of exchange (ADR 0127) and, in its second part, the Event Hubs SDK.
Neither may reach a shipped package. `Microsoft.Identity.Web` would
contradict ADR 0037's "the server is a consumer of tokens", and the Event
Hubs SDK is an integration Varve does not ship until a second consumer wants
it.

Today the register says where a package may be used only in a comment ("in
`Varve.Server` only", ADR 0099). Nothing checks it. A comment is not a rule:
`AGENTS.md` asks for every rule to be enforced.

## Decision

1. **A register entry may carry `Scope`**, a comma-separated list of
   repository-relative directory prefixes (`samples/`, `src/Varve.Server/`).
   - A project that references the package with `PackageReference` must sit
     under one of them.
   - An entry with no `Scope` may be used anywhere, as today.
2. **`eng/dependency-register.cs` checks it.**
   - It reads every `*.csproj` under the repository (or under `--root`, for
     the fixture) and reports each reference outside the entry's scope with
     the project, the package and the scope.
   - It exits 1 on any finding.
   - The `register` job in `eng/ci.cs` runs it as before.
3. **A fixture proves the failure path.** `tests/fixtures/dependency-register/`
   holds a register and two trees:
   - `failing/`, where a project under `src/` references a package scoped to
     `samples/`;
   - `passing/`, where only a sample does.

   The job `register-fixture` asserts exit 1 and exit 0, as the DCO and
   required-checks fixtures do.
4. **Admitted under this ADR, scoped to `samples/`:** `Microsoft.Identity.Web`
   (MIT), the on-behalf-of sample's token validation and exchange. Its version
   is resolved from NuGet into the register. Its closure (MSAL,
   `Microsoft.IdentityModel.*`) comes with it into the sample and nowhere else.
5. **The existing "in `Varve.Server` only" entries gain their scope** (ADRs
   0099 and 0105): `Microsoft.AspNetCore.Authentication.JwtBearer`,
   `System.CommandLine` and `System.Security.Cryptography.ProtectedData`. Each
   names `src/Varve.Server/` and the test projects that ADR already allows.
   `YamlDotNet` names `tools/repo-standard/`. A scope that would fail on
   today's tree is not added.

## Alternatives considered

- **A separate solution for samples.** It would keep the samples out of
  `dotnet build Varve.slnx`. But a solution boundary is not a dependency
  boundary: `PackageReference` works across it, and central package
  management is repository-wide.
- **Leave scope to review.** Review has caught every case so far, but a rule
  only in a comment is not a rule. The check costs one attribute and fifty
  lines of gate. The failure it prevents is a native or token-acquiring
  package in a shipped closure.

## Consequences

- A sample can use what applications use without that becoming Varve's
  dependency.
- The register's comments that said "only" are now checked.

## Checks

- **Checked against the accepted ADRs** (0001–0132). Touches **0009**
  (amended), **0037** (no acquiring package ships), **0099** and **0105**
  (their scopes made checkable), **0117** (samples outside the packable set).
  No conflict.
- **Layer ownership.** None. `eng/`, ADR 0086.
- **Analyzer rule.** None. It is a gate, with its fixture.
- **Open questions owned.** None.
