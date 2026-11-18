# CLI distribution: six attested Native AOT binaries, a keyless Homebrew tap, winget manifests

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#129](https://github.com/Hafeok/Varve/issues/129), its CLI distribution part; the API freeze gate follows as its own pull request |
| **Date** | 2026-11-06 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRoynRxPSi7vx7A5Yq` |
| **Branch** | `claude/cli-distribution-129`, from `main` at 5638f01 once #179 landed. First pushed as `claude/trusting-mayer-9igjzx` (#240), and moved at the maintainer's request so that #179's `land/` names and run history stay its own |

## The prompts

The session's first message and its session prompt are in
[the #119 record](2026-10-27-issue-119-supply-chain-transparency.md). The
plan for this step is #129's comment of 2026-10-27, and the maintainer
approved it on #119 with these answers for #129:

> W1 (option 1): CI generates, validates and smoke-installs the winget
> manifest and attaches it to the release; the maintainer submits it with
> `wingetcreate submit` through an interactive sign-in. No App, no PAT, no
> stored secret. Write this into ADR 0226, with the installation-token
> finding as the reason. The CLI box is ticked when the first submission is
> merged. H1: the tap follows the newest release, prereleases included, until
> 1.0.0, then stable releases only. H2: `Hafeok/homebrew-varve` added with
> push access. F1: the freeze gate rule as stated.

The message that started this step, once #179 had landed:

> Agreed. Start #129 from a fresh `main`. Two things while preview.17 runs:
>
> * Preview.17's Release run (42189587179) is queued behind Dependabot's first runs, not stalled. When it finishes, post the run ids and the nuget.org and GHCR results on #119, then check that #119 is closed. If it failed, post the root cause on #119 before fixing anything.
> * Leave Dependabot's SDK PRs red. Running `dotnet run eng/lockfiles.cs` on them is a manual step on purpose. Say so in the dependency section of `docs/supply-chain.md` if it isn't there already.

## What was done

- **`docs/supply-chain.md`**: a Dependabot SDK update stays red until a
  maintainer runs `dotnet run eng/lockfiles.cs`, on purpose. Its own commit,
  under #119.
- **ADR 0226**, filed unaccepted, with a dated amendment to ADR 0105 point 6
  and the decision set `cli-distribution`.
- **`publish.yml`**:
  - a `binaries` job, one per RID on a runner of that RID;
  - `SHA256SUMS` moved to `release-assets`, over the packages, the archives
    and the winget manifests, and attested there;
  - the push to nuget.org checks the packages against that list;
  - `verify` verifies each archive and `SHA256SUMS`;
  - a `winget` job validates the manifests, and at a tag installs from them.
- **`eng/winget.cs`** writes the manifests from the release's zips.
- **`eng/release.cs`** requires a `binary:` line per RID, with the fixture
  `missing-binary-line`.
- **`ci.yml`**: the `native aot` job runs on all six runners, and the SBOM
  steps read the RIDs from `Directory.Build.props`.
- **`Hafeok/homebrew-varve`**: the tap's workflow, scripts, README and
  licence, on branch `claude/trusting-mayer-9igjzx`.

## What was measured

- **linux-x64, here:** a publish of `Varve.Server` with a version override
  took 2 min 39 s on 4 cores. `varve --version` prints
  `0.1.0-preview.99+<sha>`, so the smoke compares the part before `+`. The
  publish leaves `.pdb` and `.xml` files beside the executable, which is why
  the archive takes the executable alone.
- **`eng/winget.cs`:** writes valid YAML for both Windows zips, and exits 1
  naming the missing zip when one is absent.
- **The tap's scripts:**
  - `pick-release.sh` against Hafeok/Varve returns `v0.1.0-preview.16`,
    since preview.17's GitHub release does not exist yet;
  - `formula.sh` writes a formula that `ruby -c` accepts, and writes nothing
    when an archive is missing from `SHA256SUMS`.
- **The other four RIDs, winget and Homebrew cannot run in this container.**
  The pull request's CI and its `publish.yml` dry run are their first runs.

## What is left

- **The winget submission** is the maintainer's, after the first release
  with binaries. #129's and #15's CLI box is ticked when it is merged.
- **The tap's default branch.** The first push to the empty repository made
  `claude/trusting-mayer-9igjzx` its default branch. Renaming it to `main`
  is the maintainer's. The scheduled workflow runs on the default branch,
  whatever its name.
- **The glibc floor** of the Linux binaries, 2.39, is stated in ADR 0226 and
  `run.md`. Lowering it is a later decision.
- **Accept:** ADR 0226, the amendment to 0105, and their ledger keys.
