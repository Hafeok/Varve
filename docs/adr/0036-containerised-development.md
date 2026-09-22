# 0036 — Containerised development and the local pipeline

## Status

Accepted. 2026-09-22.

## Context

Two of the Mind Over Machine stewardship items are about where work runs:

> **Containerized Development:** Development environments are containerized
> (typically utilizing Dev Containers or similar setups).
>
> **Containerized Pipelines:** CI/CD pipelines are containerized and fully
> executable directly from within the local development environment (supporting
> a true shift-left paradigm).

The organisation publishes a pattern for the first, and the standard's
instruction is to mirror it rather than paraphrase it. That pattern is the
`init-takt-devcontainer` skill in `mindovermachine-dev/how-we-work`, with a
layering model documented in that repository's `docs/architecture.md`: a
required core skill, then exactly one `takt-stack-*` runtime overlay, then an
optional `takt-profile-*` repo-shape overlay, resolved by the precedence
profile > stack > core.

**The core skill is only partly reusable here, and the reason is worth stating
rather than asserting.** Its baseline is a standard Ubuntu LTS devcontainer with
`common-utils` and `github-cli` features, and that transfers directly. What does
not transfer is everything the core owns beyond that:

- Its verification methods are `cspell`, `prettier` and `markdownlint-cli2`,
  reached through Node and Python features. Varve's verification is `dotnet
  build` with warnings as errors, four test projects, a conformance ratchet over
  883 W3C cases, and five `eng/` gates. None of that is a lint pass.
- Its quality gate is `gh insitu run trunk-worthy`, a wave defined in `.insitu.yml`
  and executed by a third-party `gh` extension (`devx-cafe/gh-insitu`). Adopting
  it would put a `gh` extension from outside the foundation on the critical path
  of this repository's build, which constraint 4 would require an ADR to admit
  and which nothing here needs.
- Its workflow set (`on_dev`, `on_ready`, `on_semver`, `pr-to-ready`) implements
  a ready-branch promotion flow with a `READY_PUSHER` PAT. That is a different
  route to the trunk from the one ADR 0032 decided, and Varve's existing `ci.yml`
  and `publish.yml` already gate `main` through rulesets.

So the honest description is: **the core skill's devcontainer baseline is
mirrored; its process tooling is replaced by the equivalent this repository
already has.** `eng/ci.cs` is Varve's `trunk-worthy` wave. The .NET overlay
below is written in the style the `takt-stack-*` overlays are described in —
runtime provisioning and cache strategy, build/test/lint commands for the stack
— and is offerable back to `how-we-work` as one, which is the point of writing
it that way.

There is also a defect this decision has to fix, found at milestone 3b and
currently unaddressed. The Windows CI checkout rewrote the W3C suite's line
endings, so the two platforms were testing different bytes. The bug that
surfaced was real — a CR LF pair split across a buffer boundary was counted as
two lines — but it was found by luck of platform, and 163 cases were reporting
the wrong line number on one runner and not the other. **Conformance inputs must
be identical everywhere.**

## Decision

### `.devcontainer/`, mirroring the core and overlaying .NET

- **Base and features from the core skill's template**:
  `mcr.microsoft.com/devcontainers/base:ubuntu-24.04`, with `common-utils` and
  `github-cli`. `gh` is there because the standard's workflow assumes it.
- **The .NET overlay**: the SDK pinned to the exact version in `global.json`
  (10.0.401 today), and the `wasm-tools` and `wasm-experimental` workloads,
  because constraint 3's browser host is built in CI and must be buildable
  locally. A version skew between the container and `global.json` is a defect,
  not a convenience, so the pin is exact rather than a floor.
- **`core.autocrlf=false` in the container's git configuration**, for the reason
  below.
- No Node, no Python, no `cspell`, no `prettier`. They verify nothing here.

### `eng/ci.cs` — one entry point, the same jobs as CI

A file-based C# app in the shape of the other `eng/` gates, running the jobs the
workflows run: build, analyzer tests, the other three test projects, conformance
plus ratchet, the dependency register, the native-asset gate, the licence-header
check, the issue-reference check, and the pack dry run. `--list` names the jobs,
`--only <job>` runs one, and the exit codes are the house's: 0, 1, 2.

**`ci.yml` calls it inside the devcontainer image**, so the local run and the CI
run are the same code in the same container rather than two descriptions of the
same intent that drift. The pipeline job is what makes the claim true; the
existing matrix jobs stay (see the consequences).

### `core.autocrlf=false` everywhere

Every checkout step in every workflow sets it, and so does the devcontainer.
`.gitattributes` already normalises the working tree to LF and already says the
W3C submodule carries its own rules — but `core.autocrlf=true` is the Windows
default and overrides that expectation for files the submodule does not mark.
Setting it false is the one line that makes every platform check out the same
bytes.

**CR LF handling in parsers is tested on documents this repository owns**, not
on whatever the checkout happened to produce. Milestone 3b already added those:
a CR LF document at five segment sizes, and a four-statement CR LF document at
every one of its byte offsets. The conformance corpus is for conformance; line
endings are tested deliberately or not at all.

## Alternatives considered

- **Adopt `init-takt-devcontainer` wholesale, including `gh-insitu` and the
  `.insitu.yml` waves.** The fullest mirroring, and the least adaptation. Rejected
  on the substance above: it would add a third-party `gh` extension to the
  critical path to run a lint stack that checks none of this repository's
  claims, and it would install a second, parallel route to the trunk alongside
  the one ADR 0032 decided. Mirroring a pattern means taking what it is for, not
  taking files that do not apply.
- **A hand-written `Dockerfile` instead of devcontainer features.** More
  control, faster builds, one place to read. Rejected: it diverges from the
  organisation's pattern for no benefit anyone can point at, and features are
  how the core skill and every other overlay are composed. A stack overlay that
  cannot be composed is not an overlay.
- **No container; document the prerequisites in `CONTRIBUTING.md`.** What the
  repository does today. Rejected — it is the item being adopted, and this
  session is the evidence for why: the environment it started in had no .NET SDK
  and an uninitialised W3C submodule, and neither fact was discoverable without
  running a build and watching it fail.
- **Run the containerised pipeline job *instead of* the existing matrix jobs.**
  Tempting, and it would make "local and CI are the same thing" unqualified.
  **Rejected, on milestone 3b's evidence.** The container is linux-only; the
  Windows leg is what found the CR LF defect, and one of the nine defect classes
  the oracle caught was visible on Windows alone. Dropping it to make a slogan
  true would remove the coverage that earned the slogan.
- **`core.autocrlf=input` rather than `false`.** Equivalent for checkout and
  different on commit, where it normalises CR LF to LF on the way in. Rejected:
  this repository deliberately owns CR LF test documents, and a setting that
  silently rewrites them on commit would delete the regression test for the
  defect that motivated the setting.

## Consequences

- **CI gains a `pipeline` job** that builds the devcontainer image and runs
  `eng/ci.cs` in it. It is slower than the matrix legs and it is the one that
  proves the claim.
- **The Windows, AOT and WASM legs stay outside the container**, and the
  repository therefore does *not* claim that all of CI runs in the devcontainer
  — only that the pipeline is the same code and runs in it. Overclaiming here
  would be the easy thing to write and false.
- **`eng/ci.cs` is now the thing to keep in step.** A job added to `ci.yml` and
  not to `eng/ci.cs` breaks the property this decision exists for. There is no
  gate for that, and saying so is better than pretending.
- **The devcontainer cannot be built in every environment that runs an AI
  session.** The session that wrote this ADR had no Docker; it installed the
  pinned SDK directly and ran `eng/ci.cs` outside a container. That is a
  degraded mode, it is allowed, and it is why the CI job — which does build the
  image — is the proof rather than a local run.
- **The .NET overlay is written to be contributed back** to
  `mindovermachine-dev/how-we-work` as a `takt-stack-dotnet` overlay. Doing so
  is outside this repository and is listed as maintainer work, not done here.
