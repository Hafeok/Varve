# Supply chain transparency: an attested SBOM per artefact, the dependency gate, the support period

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#119](https://github.com/Hafeok/Varve/issues/119), closed by this release; the CLI and freeze parts on [#129](https://github.com/Hafeok/Varve/issues/129) follow as their own pull requests |
| **Date** | 2026-10-27 |
| **Tool** | Claude Code 2.1.311, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRoynRxPSi7vx7A5Yq` |
| **Branch** | `claude/trusting-mayer-9igjzx`, from `main` at 3a8f636, with `main` at 705b268 merged in |

## The prompts

### The first message

> Read `varve-claude-code-readiness-release.md` in the attached zip (folder `varve-readiness-b`). It is your session prompt for 1.0 readiness B in Hafeok/Varve: supply chain (#119), CLI distribution and the API freeze gate (#129). `varve-adr-supply-chain.md` in the same folder is the ADR draft it files. Work on current `main` and follow `AGENTS.md` in full.
> Two corrections to the prompt:
>
> * ADR numbers: yours start at 0224, not 0223 as the prompt says. Readiness A holds 0213–0222.
> * CLI distribution: the maintainer's decisions are on #129 (comment of 2026-10-27). Homebrew is the self-updating tap `Hafeok/homebrew-varve`, with no cross-repository token. winget goes through a GitHub App whose key sits only in an environment restricted to `main`. First verify whether an app installation token can open a PR on `microsoft/winget-pkgs`. If only a classic personal access token works, stop and report the options; don't use one.
>
> Plan first. Post the plan on #119, with the CLI and freeze parts on #129, answering every plan question, and wait for approval before writing any code.

### The session prompt it names (`varve-claude-code-readiness-release.md`)

> # Session: 1.0 readiness B — what ships and how: supply chain, CLI distribution, the API freeze gate (#119, #129)
>
> This session takes the 1.0 deliverables that live in the **release pipeline**: what every shipped artefact carries, how the CLI reaches package managers, and the gate that will freeze the public API. It runs in parallel with Replication (#97), Inference (#95), the Web UI (#96) and readiness A, which covers correctness and the read-forever formats. Most of its work is in `publish.yml`, `release.yml`, `eng/` and `releases/`, which no feature session touches.
>
> **Starting point.** Current `main`. Some of #119 already exists:
> - `eng/dependency-register.cs` and `eng/native-assets.cs`;
> - an `Adr="NNNN"` attribute and a `Scope` on every `PackageVersion` in `Directory.Packages.props`;
> - the container's SPDX SBOM and build-provenance attestation in `publish.yml`.
>
> **Start by establishing what exists against #119's scope**, and plan only the gaps.
>
> **In flight:**
> - **The Web UI (#167)** adds a release asset (`varve-webui-<version>.zip`) whose digest #119's descriptor field must carry. Coordinate on the field's shape, and post it on #167 once it's filed.
> - **#173 (the DecisionDriven bump)** touches `Directory.Packages.props`. Merge `main` after it lands.
>
> **Read before planning:**
> - #119 in full, #129's CLI distribution and API freeze deliverables, and #15 with its amendments;
> - the attached ADR draft `varve-adr-supply-chain.md`. File it as an ADR, reconciled with what already exists: what is built, it records; what isn't, it decides;
> - ADRs 0029 (publishing and versioning), 0035 (semantic versioning), 0063 (the dependency ADR record), 0085, 0099 (CI images by digest), 0102 (the release descriptor), and the ADRs for `eng/dependency-register.cs` and `eng/native-assets.cs`;
> - `.github/workflows/publish.yml`, `release.yml` and `scorecard.yml`, `docs/releases.md`, and `releases/`.
>
> `AGENTS.md` applies in full. **Plan first and wait for approval.** Post the plan on #119, and the CLI and freeze parts on #129. Your ADRs start at **0223**; readiness A holds 0213–0222. Renumber at landing if a range overflows.
>
> ## Steps, in order. Each is one PR, red only on `CS0618`.
>
> **1. Supply chain (#119),** as the draft decides, with what exists recorded rather than rebuilt:
> - **An SPDX SBOM per shipped artefact:** each `Varve.*` nupkg, the CLI tool, the container, and the Web UI bundle when it lands.
>   - generated from locked restores, and deterministic under `SOURCE_DATE_EPOCH`;
>   - embedded in each nupkg, attached to the release, and attested keylessly.
> - **Lockfiles everywhere, and locked restore in CI.** Show one Dependabot PR keeping `packages.lock.json` current with this layout.
> - **The register gate's failure cases,** each with a deliberately broken fixture. Extend `eng/dependency-register.cs` rather than add a second gate.
> - **`NuGetAudit` as errors.** Exemptions only with an OpenVEX statement, published with the release.
> - **The rest:** private vulnerability reporting, `SECURITY.md` with the support period (latest preview only before 1.0, and the 1.0 policy proposed for the maintainer), and `docs/supply-chain.md` written for a supplier assessment.
> - **The descriptor's `sbom:` field,** by amendment to ADR 0102. Design it to also carry the release assets' digests (the Web UI zip, the CLI binaries), so #167 and step 2 use one field, not three.
> - **`gh attestation verify`** in CI against the published artefacts after a release.
>
> **2. CLI distribution** (#129).
> - An ADR decides what is distributed. My lean: native AOT single-file binaries for win-x64, win-arm64, osx-arm64, osx-x64, linux-x64 and linux-arm64, built by `publish.yml`, each attested and with an SBOM under step 1.
> - **First, prove the CLI publishes under native AOT on every RID with zero warnings.** If something in its closure isn't AOT-clean, that is a finding against constraint 2, reported, not worked around.
> - **Homebrew and winget:**
>   - a tap in its own repository, and the winget manifest submitted to `microsoft/winget-pkgs`;
>   - both generated from the release descriptor, and submitted by CI through OIDC-based workflows;
>   - **no personal access tokens and no API keys.** If either channel can't be reached without a long-lived token, stop and say so, with the options. Don't store one.
> - **An install smoke on each OS** runs `varve --version` from the package manager.
>
> **3. The public API freeze gate** (#129, ADR 0035).
> - A gate compares each package's public surface with the last released preview's, from the published nupkg. It reports every removal or signature change as breaking.
> - **Before 1.0 it reports, and fails only where a commit isn't marked breaking (`!`) or the descriptor doesn't say so.** After 1.0, a breaking change fails unless the major version changes.
> - It covers every `Varve.*` package, `Varve.Protocol.Client` and the management API (#105) included.
> - **The PR lists every public member whose only consumer is a test,** so the maintainer can decide each one before the freeze. Don't make members internal in this PR.
> - The `PublicAPI.Unshipped.txt` files are emptied at 1.0, not now.
>
> ## Definition of done
>
> - Each step's gate, with its durations per OS (#126).
> - #15's matching boxes ticked by the PR that completes them (signed artefacts with SBOMs, CLI distribution), and #129's likewise. The freeze box stays open until 1.0, when the gate starts failing on breaking changes.
> - `docs/releases.md` and the operator guide's install page updated.
> - A `docs/traceability/` record per PR (ADR 0033).
>
> ## Plan questions
>
> 1. What of #119 exists, and the gap list.
> 2. The SBOM generator, its ADR, SPDX version and determinism.
> 3. The `sbom:` and assets field, and how the Web UI and the CLI binaries use it.
> 4. The CLI under native AOT on each RID: does it publish clean today?
> 5. Homebrew and winget: the keyless path for each, or the honest finding that none exists.
> 6. The freeze gate: how it reads the last preview's surface, and what it reports today.
> 7. ADR numbers from 0223, and amendments (0102 certainly; 0029 and 0035 likely).
>
> ## How this lands
>
> - **One PR per step.** Each ships in the next free `0.1.0-preview.N` (ADR 0102), or carries its own descriptor where it changes what a release contains (step 1 certainly). A descriptor carries `accepted-by: mailto:emil@okkels-klein.dk` and a `Closes` trailer for its issue. Use bare #N in PR descriptions, with no closing keyword (#108).
> - **Commits:** signed with the agent SSH key as `Claude (Emil) <claude+emil@okkels-klein.dk>`, DCO signed off, referencing #119 or #129. No model *name* in a commit message (the trailer is `Co-Authored-By: Claude <noreply@anthropic.com>`). Model ids are allowed, and the traceability record names the model.
> - **Secrets only in environments restricted to `main`. No approval by PR comment. No Docker Hub pulls in CI.** CI images only from `.github/ci-images.yaml`.
> - **No suppression or downgrade of DD or VARVE rules.**
> - **Land through `land/`.**

The ADR draft it files, `varve-adr-supply-chain.md`, is ADR 0224's starting
text; what changed from it is in ADR 0224's Status and Context.

### The approval

> Approved, with these answers.
> #119:
>
> * D1: `Directory.Packages.props` stays the one register. Transitive packages are admitted by their parent's ADR, but the gate checks each one's licence and native assets, and the lockfile diff shows them in review.
> * D2: keep `Varve.Aspire`'s exception under ADR 0117. Its natives are listed in its SBOM, and `docs/supply-chain.md` names it as a development-time package that carries them.
> * D3: yes, the descriptor's date at 00:00Z.
> * D4: 12 months of security fixes for the previous major after `2.0.0`, and the latest `1.minor` gets fixes. State it in `SECURITY.md`.
> * D5: change `global.json` to `rollForward: latestPatch`. A feature band changes the compiler and analyzers; a patch doesn't.
> * D6: noted.
> * `Microsoft.NETCore.Platforms` 1.1.0: don't state it as MIT. Record the licence the package itself declares, and name it as a build-only exception in ADR 0224: it ships nothing, and it reaches us through `netstandard2.0` analyzers.
> * Findings: confirm the container SBOM's gap on preview.15's attestation, then merge your generator's .NET part with Syft's OS part (ADR 0111 amended). Pin the base image by digest. Rewrite `SECURITY.md`'s stale claims. Set `private_vulnerability_reporting: true` and `dependabot_alerts: true` in `repo-standard.yaml`; I apply them.
> * `sbom:` and descriptor format 2 as proposed. Post the shape on #167 now.
>
> #129:
>
> * W1: option 1. CI generates, validates and smoke-installs the manifest and attaches it to the release, and I submit it with `wingetcreate submit` through an interactive sign-in. No App, no PAT, no stored secret. Write that into ADR 0226, with your finding on installation tokens as the reason. The CLI box is ticked when my first submission is merged.
> * H1: yes. The tap follows the newest release, prereleases included, until `1.0.0`, then stable releases only.
> * H2: yes. Add `Hafeok/homebrew-varve` with push access.
> * F1: yes, as stated.

## The report

The plan is on [#119](https://github.com/Hafeok/Varve/issues/119#issuecomment-6441618658)
and [#129](https://github.com/Hafeok/Varve/issues/129#issuecomment-6441631814).
The descriptor's shape is on [#167](https://github.com/Hafeok/Varve/issues/167#issuecomment-6441982300).
This pull request is step 1. Steps 2 and 3 are their own.

### Measured before planning

- **The CLI under Native AOT, linux-x64:** 0 warnings and 0 errors, in
  2m46s on 4 cores. The binary is 28.6 MB. Two clean publishes of 3a8f636
  were byte-identical (`8a55a554…`).
- **The public surface:** the SDK's package validation against
  `v0.1.0-preview.15` reports nothing for any of the 27 packages. Against
  `v0.1.0-preview.2`, `Varve.Protocol.Client` reports five removals. That is
  step 3's material.
- **winget:** an installation token cannot open the pull request on
  `microsoft/winget-pkgs`, and the maintainer chose manual submission (W1).
  That is step 2's.

### What step 1 found

- **The container's SBOM had no .NET component.** The preview.15 attestation
  on `sha256:f3c1651a…` lists ten packages: nine Ubuntu packages of the base,
  and the image. The image is the Native AOT binary, which has no
  `.deps.json` for Syft to read. The image's SBOM is now `eng/sbom.cs`'s, with
  the base's OS packages from Syft.
- **nuget.org's copy of a package is never the attested bytes.** nuget.org
  adds `.signature.p7s` to every package it accepts, so
  `gh attestation verify` on a nuget.org download fails by construction.
  - The release now carries the attested packages.
  - The `verify` job checks those with `gh attestation verify`.
  - It checks nuget.org's copies entry by entry (`eng/nuget-served.cs`, run
    against a real nuget.org package and a tampered one), and their
    signature with `dotnet nuget verify`.
- **Neither off-the-shelf generator fit.**
  - Syft 1.55.0's output has a random namespace and the run's time. It writes
    no NuGet checksum, and cannot tell an analyzer from a runtime package in a
    lockfile.
  - Microsoft's sbom-tool writes a `Guid.NewGuid()` and re-zips the nupkg.
  - `actions/attest` accepts SPDX 2.x only.
  - Hence `eng/sbom.cs`, SPDX 2.3 (ADR 0225).
- **The base image was referenced by tag.** It is pinned by digest now.
- **`SECURITY.md` was stale**, and `repo-standard.yaml` declared private
  vulnerability reporting off.
- **`NuGetAudit` finds nothing:** zero NU1901–1904 against nuget.org's
  database of 2026-10-26. The VEX document is empty.
- **One correction to the plan:** `Varve.Protocol.Client` has eight
  third-party runtime packages, not ten.

### What the gates show

- **65 lockfiles, 213 resolved packages, of which 36 are register entries.**
  - 245 package-version pairs are checked, 82 of them runtime.
  - Seven packages declare a licence file or URL. Each text was read: six
    MIT, and Fractions BSD-2-Clause.
  - `Microsoft.NETCore.Platforms` 1.1.0 is the one build-only exception.
  - `Varve.Aspire`'s `Hex1b` is the one native-asset exception.
  - All 137 Action references are pinned by SHA.
- **All ten register fixtures fail, each for its own reason,** in 15 s.
- **The SBOMs** (`sbom`, 4.9 s for 35 documents, generated twice): every one
  is byte-identical across runs. All 29 checked with the SPDX project's
  `pyspdxtools` are valid, and a deliberately broken copy is rejected. The
  image SBOM's digest came out the same under Syft 1.54.0 and 1.55.0.
- **The descriptor's lines** are `eng/sbom.cs`'s at `0.1.0-preview.16` and
  2026-10-27, with Syft v1.54.0, the version `publish.yml` pins.

**The whole pipeline** (`dotnet run eng/ci.cs -- --base origin/main`) passes
on Linux, in this 4-core container. It stopped twice on the way, and each stop
was rerun from the job that stopped:
- **`test-sparql-results`:** the clone had no W3C submodules (AGENTS.md's
  `git submodule update --init --recursive`), which is this environment, not
  the change.
- **`pack`:** a real defect, fixed in this pull request. `Directory.Build.targets`
  named each SBOM by `$(PackageId)`, which is not yet set where that file is
  evaluated, so every pack looked for `nupkg/.spdx.json`. It is the project's
  name now, which no project overrides. This is the first run that packed with
  the SBOMs, and the gate caught it before any package was published.

Durations, in seconds (#126):

| Job | s | Job | s | Job | s |
|---|---:|---|---:|---|---:|
| `register` | 0.5 | `register-fixtures` | 14.0 | `restore` | 28.5 |
| `register-packages` | 0.7 | `sbom` | 2.5–5.8 | `release` | 0.7 |
| `build` | 121.5 | `build-benchmarks` | 85.1 | `test-analyzers` | 48.4 |
| `test-store` | 366.3 | `test-rec` | 72.2 | `test-shacl-store` | 66.9 |
| `test-protocol` | 50.4 | `conformance` | 48.9 | `test-sparql-results` | 44.8 |
| `pack` | 8.9 | `decision-report` | 67.9 | every other job | under 40 each |

`register` runs before restore, in under a second. The new jobs together add
about 20 s to a run. The Windows leg and the release workflow run on the pull
request and at the tag; this container cannot run them.

**Found by reading the change back, and fixed before the push:**
- A licence statement that matched only the benchmarks' or the samples'
  packages was reported as stale wherever only the solution was restored.
  That would have failed `ci.yml`'s build job on both runners.
- On Windows, the captured version kept a carriage return, which would have
  given every SBOM a version its package lacks.
- The provenance attestation ran after the push, and now runs before it.
- `eng/required-checks.cs` cannot expand a matrix value that comes from
  `fromJSON`, so the attestation job's name is fixed.

### The review round, 2026-10-28

The maintainer's review on the pull request asked for three changes, and
each is made:
- **The attestations precede the push.** nuget.org's trusted publishing names
  the workflow file, never the job, so the push became its own job under the
  `release` environment, and it pushes only the packages whose digests are in
  the attested `SHA256SUMS`.
- **`publish.yml` has a dry run** on a pull request that touches the release
  path: every job, skipping only the pushes and the GitHub release.
- **CI installs exactly the SDK `global.json` pins.** CI failed on the
  devcontainer's SDK 10.0.401 against lockfiles written with 10.0.403, since
  the SDK's patch fixes ILLink's and ILCompiler's versions. The 32 setup steps
  are one composite action, `.github/actions/setup-dotnet`, which names no
  version. Dependabot bumps `global.json`, `eng/lockfiles.cs` regenerates
  every lockfile, and its `--check` fails a lockfile from another patch before
  the restore, naming the script. Regenerating on 10.0.403 gave no diff.

Found in CI and fixed:
- `dotnet publish -r` restored the whole graph for one RID and failed the
  locked restore (NU1004), so each RID publish restores first.
- Since .NET 10 a tool project with RuntimeIdentifiers packs a pointer package
  with no assembly. RuntimeIdentifiers is now set only while restoring, and
  `eng/package-metadata.cs` fails a tool package without its entry point.

### What is left

- **The maintainer applies** `private_vulnerability_reporting` and
  `dependabot_alerts` (`repo-standard`).
- **The Dependabot pull request that keeps a lockfile current is shown after
  this lands.** The first weekly run on this layout is the evidence asked for,
  and the next session on #119 or #129 links it.
- **The first release in format 2 runs `publish.yml`'s pushes and the
  tag's verification.** The dry run on this pull request runs every other job.
- **#167** adds its `asset:` line when its zip exists.
- **Accept on this pull request:**
  - ADRs 0224 and 0225;
  - the amendments to 0009, 0029, 0102 and 0111;
  - their ledger keys.

### Acceptance, 2026-10-28

A later session (Claude Code, model `claude-fable-5-1`), on the maintainer's
instruction to accept every decision on the open pull requests, added
`accepted-by` and `accepted-at` to the 25 keys this branch files over
main: dependency-policy-and-register (1 key, ADR 0009), publishing-and-versioning (1 key, ADR 0029), a-release-is-a-descriptor (1 key, ADR 0102), the-container-image (1 key, ADR 0111), supply-chain-transparency (16 keys, ADR 0224), the-sbom-generator (5 keys, ADR 0225). The acceptance is the maintainer's, who had read them; the
session only wrote it at their direction, as the acceptance of ADRs
0127–0133 was written. The ADRs' status text and the index rows are left as
filed, as in that precedent.
