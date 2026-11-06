# 0226 — CLI distribution: Native AOT binaries for six RIDs, a keyless Homebrew tap, winget manifests submitted by the maintainer

## Status

**Filed unaccepted by readiness B for #129, 2026-11-06** (ADR 0066). Decided
by the maintainer on #129: the decisions of 2026-10-27, and the answers W1,
H1 and H2 to the plan of the same day. Acceptance is the maintainer's act on
the pull request.

**Amends** [ADR 0105](0105-the-cli.md) point 6: the release carries the
Native AOT binary. The amendment is dated 2026-11-06 in that file, and is
filed unaccepted.

**Builds on:**
- [ADR 0224](0224-supply-chain-transparency.md): every shipped artefact has
  an SBOM and is attested, and the push waits for the attestations;
- [ADR 0225](0225-the-sbom-generator.md): `eng/sbom.cs` writes a binary's
  SBOM;
- [ADR 0102](0102-a-release-is-a-descriptor.md): the descriptor's `sbom:`
  names every artefact.

Records decision set [`cli-distribution`](../decisions/cli-distribution.md).

## Context

`varve` is one executable with the server (ADR 0105). Until now it came two
ways: the framework-dependent .NET tool, and the container image, which is
the Native AOT binary on a chiseled base (ADR 0111). A machine with nothing
installed had to build the binary itself; `docs/operator/run.md` said "No
release carries it as a download". 1.0 (#15) asks for the CLI through the
platforms' own package managers.

- **Native AOT does not cross-compile between operating systems.** Each RID
  is built on a runner of its own OS and architecture. Before this ADR, CI
  had proven linux-x64, linux-arm64 and win-x64 only.
- **Homebrew.** A personal tap, `Hafeok/homebrew-varve`, decided by the
  maintainer. It must update itself without a token that crosses
  repositories.
- **winget.** The maintainer's plan was a GitHub App whose key sits only in
  an environment restricted to `main`, and asked first whether an App
  installation token can open the pull request on `microsoft/winget-pkgs`.
  **It cannot** (#129, 2026-10-27):
  - an installation token reaches only the repositories of its installation,
    and creating a pull request needs `pull_requests: write` on the base
    repository, which is Microsoft's;
  - a user access token from the same App reaches only accounts where the
    App is installed, and needs a refresh token stored and rotated by CI;
  - every submission tool (`wingetcreate`, `winget-releaser`, Komac) needs a
    classic personal access token, which constraint 3 and ADR 0037 rule out;
  - Microsoft documents no OIDC or keyless route.

## Decision

### 1. Six binaries, one per RID, built on their own runners

- **The RIDs** are `VarveNativeRuntimeIdentifiers` in `Directory.Build.props`:
  linux-x64, linux-arm64, win-x64, win-arm64, osx-x64 and osx-arm64. It is the
  one list. The lockfiles restore for it (ADR 0224). `publish.yml`,
  `release-assets`, `ci.yml`'s SBOM steps and `eng/release.cs` all read it.
  The `binaries` job's matrix maps each RID to its runner, and
  `release-assets` fails if any RID in the list has no archive.
- **The runners:** `ubuntu-24.04`, `ubuntu-24.04-arm`, `windows-2025`,
  `windows-11-arm`, `macos-15-intel` and `macos-15`. `ci.yml`'s `native aot`
  job runs the AOT smoke and the server smoke on the same six on every pull
  request, so a RID that stops building fails there before it fails a tag.
- **What a binary is:** `dotnet publish src/Varve.Server -r <rid>` after a
  RID-less locked restore. ILC warnings are errors (constraint 2). The
  executable alone is renamed `varve` (`varve.exe` on Windows); no `.pdb`,
  `.dbg` or `.xml` file ships.
- **The archive:** `varve-<version>-<rid>.tar.gz` on Linux and macOS, with
  mode 755, and `varve-<version>-<rid>.zip` on Windows. Before it is
  attested, it is unpacked and `varve --version` must print the release's
  version.
- **A Linux binary needs the glibc of the runner that built it or newer**:
  Ubuntu 24.04, glibc 2.39. That is stated, not hidden. A wider floor means
  building in an older distribution's container from the mirror (ADR 0099),
  which is left for a later decision.

### 2. Each binary is attested and named in the descriptor

- **Two attestations per archive,** on the archive's digest, keyless, made
  in the job that built it (ADR 0224 point 3):
  - SLSA v1 build provenance;
  - the SBOM that `eng/sbom.cs --binary <rid>` wrote for it in the publish
    job: the closure compiled in, and the .NET runtime (ADR 0225).
- **The descriptor names them.** `sbom:` has a
  `binary: varve-<version>-<rid>.<ext>` line per RID with its SBOM's
  SHA-256. From the first commit that carries `eng/winget.cs`,
  `eng/release.cs` requires one line per RID, and the
  `missing-binary-line` fixture fails without them. `publish.yml` checks
  each digest before anything is pushed, as it does for the packages.
- **`SHA256SUMS` moves to `release-assets`.** It is written once the
  binaries exist and covers the packages, the archives and the winget
  manifests. It is attested there. The push checks the packages against it,
  so **nothing reaches nuget.org until every artefact the release carries is
  built and attested.**
- **The verify job** runs `gh attestation verify` on each archive for both
  predicates, and on `SHA256SUMS` for its provenance, with the tag's
  `--source-ref`.

### 3. Homebrew: a tap that updates itself, keylessly

- **The tap is `Hafeok/homebrew-varve`.** Install with
  `brew install hafeok/varve/varve`.
- **Its own workflow** runs on a schedule and by `workflow_dispatch`. It
  reads Varve's releases as public reads, and Varve's workflows never push
  to the tap or dispatch to it. No token crosses repositories.
  - **Which release (H1):** the newest one, prereleases included, until a
    release `1.0.0` or later exists. After that, the newest stable release.
  - **Verification:** it downloads the four macOS and Linux archives and
    `SHA256SUMS`, checks the archives against the list, and runs
    `gh attestation verify` on each, requiring `--source-ref
    refs/tags/<tag>` of `Hafeok/Varve`.
  - **The formula,** `Formula/varve.rb`: `on_macos` and `on_linux`, `on_arm`
    and `on_intel`, each with its archive's URL and SHA-256.
  - **The install smoke** runs before the commit, on `macos-15` (arm64),
    `macos-15-intel` and `ubuntu-latest` (Homebrew on Linux):
    `brew install` from the candidate formula, then `brew test`, which
    requires `varve --version` to say the release's version.
  - **The commit** is made through the contents API with the tap's own
    `GITHUB_TOKEN`, only when the formula changed.

### 4. winget: CI prepares, the maintainer submits (W1)

- **CI writes the manifests.** `eng/winget.cs` writes the three files of a
  multi-file manifest, schema 1.10.0, for the package `Hafeok.Varve`:
  - one installer per Windows RID, each the release's zip with its SHA-256
    computed from the archive itself;
  - the portable `varve.exe` inside the zip, under the command alias `varve`.
- **CI validates them,** with `winget validate` on `windows-2025`, in the dry
  run as at the tag.
- **At the tag, CI installs from them.** Once the release's URLs exist, it
  runs `winget install --manifest` and requires `varve --version` to say the
  release's version.
- **The release carries them**, and `SHA256SUMS` lists them.
- **The maintainer submits them**, with `wingetcreate submit <dir>` or Komac,
  signed in interactively by OAuth. **No credential is stored for winget,
  anywhere.** The first submission goes through Microsoft's manual review.
  #129's and #15's CLI box is ticked when the first submission is merged.

### 5. The .NET tool stays

`dotnet tool install -g Varve.Server` is unchanged (ADR 0105 point 6).
The binary is for a machine without .NET; the tool is for one with it.

## Alternatives considered

- **A GitHub App that opens the winget pull request** (the original plan).
  Refused by GitHub's token scoping, as the context shows.
- **The App pushes the manifest branch to a fork, and the maintainer opens
  the pull request.** It keeps one long-lived secret, the App's key, for a
  step the maintainer still has to finish by hand. Option 1 needs no secret
  and the same one manual step.
- **A classic personal access token of a bot account.** Fully automatic, but
  it is a long-lived token, which constraint 3 and ADR 0037 forbid.
- **A Scoop bucket, or a self-hosted winget REST source.** Both work with
  `GITHUB_TOKEN`, but neither is `winget install` from the community
  repository, which is what 1.0 asks for.
- **Varve's release workflow pushes the formula to the tap.** It needs a
  token that crosses repositories. The maintainer ruled it out.
- **Cross-compiling every RID from Linux.** Native AOT does not support it
  across operating systems.
- **Reproducible archives**, with fixed timestamps and order. The descriptor
  carries the SBOM's digest, which is reproducible. `SHA256SUMS`, attested,
  carries the archive's. Byte-identical archives across runners would add a
  normalisation step per OS and protect nothing more.

## Consequences

- A tag's run builds on six more runners. The push to nuget.org now waits
  for the slowest binary, about 5 minutes on macOS. That is the cost of
  nothing going out partly attested.
- The next descriptor names 34 SBOMs: 26 packages, the tool, the image and
  six binaries. `eng/sbom.cs` writes the lines (`sbom.txt`), and
  `eng/release.cs --draft` names `--binary` in its hint.
- `docs/operator/run.md` installs the CLI four ways: Homebrew, winget, the
  release's archive, and the .NET tool.
- Homebrew follows prereleases until 1.0. A user who installs before 1.0
  gets every preview.
- **The winget box stays open until the first submission is merged.** That
  is in Microsoft's hands, not this repository's.

## Checks

- **Checked against the accepted ADRs** (0001–0225).
  - **0105:** point 6 amended. The binary is now a release artefact, and the
    tool is unchanged.
  - **0224:** the push still waits for every attestation, and
    `SHA256SUMS` is still attested. It is now written in `release-assets`.
  - **0037 and constraint 3:** no new secret anywhere.
  - **0099:** no new CI image.
- **Layer ownership.** None: workflows, `eng/` and documentation.
- **Open questions owned.** The glibc floor of the Linux binaries (point 1).
