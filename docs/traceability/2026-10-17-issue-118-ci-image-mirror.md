# CI's test container images, mirrored to this repository's GHCR (part 1)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#118](https://github.com/Hafeok/Varve/issues/118) |
| **Date** | 2026-10-17 |
| **Tool** | Claude Code 2.1.302, a cloud session |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_017gzKjzXyKjfJwQnN1uxTH7` |
| **Branch** | `claude/exciting-ptolemy-3fxvmb`, from `main` at 9fbdaf3 |
| **ADRs** | none new and none amended in this part; ADR 0099's amendment is part 2's |

## The prompt

> Issue #118: mirror CI's test container images into this repository's GHCR, by digest, and pull them authenticated. It lands as two PRs, because the mirror must exist before CI can pull from it. Read #118, ADR 0099, `.github/*/compose.yaml`, and every `image:` and container reference in `.github/workflows/`. `AGENTS.md` applies in full; plan briefly first and wait for approval.
>
> **PR 1: the mirror, plus a stopgap. CI keeps pulling upstream.**
> - `.github/ci-images.yaml` lists every CI image: its upstream reference, its pinned digest, its licence, and the job that uses it. Today that is PostgreSQL (ECR Public), Zitadel and mock-oauth2-server (ghcr.io), Azurite and the Event Hubs emulator (mcr.microsoft.com). Add RustFS if #116 has landed.
> - A `mirror-ci-images` workflow, triggered on push to `main` touching that file and on `workflow_dispatch` from `main` only, with `packages: write`. It copies each image by digest, all platforms, with the manifest digest unchanged, to `ghcr.io/hafeok/varve-ci/<name>`. Use `docker buildx imagetools create` or a pinned crane/skopeo action, and verify the mirrored digest equals the source. The packages are private and linked to this repository.
> - Stopgap until PR 2: wrap every `docker compose pull` (or `up`) in a bounded retry with backoff, three attempts, so one exhausted runner address doesn't fail the job.
> - `Refs #118`.
>
> **After PR 1 lands, I run the workflow once.** Then:
>
> **PR 2: CI pulls only from the mirror.**
> - Every compose file and workflow references `ghcr.io/hafeok/varve-ci/<name>@<digest>`, at the same digest as upstream, after `docker login ghcr.io` with `GITHUB_TOKEN` (`packages: read`). Remove the stopgap retries.
> - A gate in `eng/`: every image reference under `.github/` is a mirror reference whose digest is in `ci-images.yaml`. A fixture that references an upstream image directly must fail it.
> - A weekly job compares each upstream tag's current digest with the pinned one, and opens or updates one issue listing the differences, as `repo-standard check` does for drift. Dependabot's docker-compose updates for these files are switched off, because the list is now the source.
> - A dated amendment to ADR 0099. `CONTRIBUTING.md` says local runs and the devcontainer use the upstream references at the same digests.
> - The last commit ends with `Closes #118`; no closing keyword in the PR description (#108).

The session posted a plan and three questions: whether to mirror the Event
Hubs emulator, whose licence is a proprietary EULA; Zitadel's AGPL-3.0; and
whether the devcontainer's base image and the server Dockerfile's chiseled
base, both pulled from MCR in CI, were in scope. The maintainer:

> Go ahead with PR 1 as described. Answers:
>
> 1. **The Event Hubs emulator stays upstream.** Its EULA is proprietary, and a copy we host, even a private one, is a redistribution question we don't need to answer: MCR has no anonymous limit and hasn't failed us. Give the row `mirror: false`, with the EULA URL and that reason. The PR 2 gate allows an upstream reference only for a row marked `mirror: false` with a stated reason, and nothing else.
> 2. **Zitadel (AGPL-3.0):** record it and mirror it, unmodified, in a private package. Same for PostgreSQL, mock-oauth2-server and Azurite. Their licences permit it.
> 3. **The devcontainer base and the server Dockerfile's chiseled base stay out of scope, permanently, not as a follow-up.**
>    - The chiseled base is the shipped image's base. Its provenance and SBOM name MCR as the source (ADR 0111), and building it from our copy would blur that.
>    - The devcontainer base is a developer environment, not a test container.
>    - Narrow #118's done-when in PR 2's ADR 0099 amendment to "every third-party test container CI starts", and list both bases as stated exceptions.
> 4. **`ghcr.io/hafeok/varve` is exempt from the gate:** yes. It's our own artefact, built and pushed by CI.
>
> After the first mirror run, the workflow output should say which packages were created and their visibility, so I can confirm they're private. If a new package comes out public because the repository is public, the PR 1 description should say so and give the setting I change.

## The report

### What part 1 does

- **`.github/ci-images.yaml`** lists the five images CI starts. For each it records:
  - the mirror package name;
  - the upstream reference with its tag;
  - the manifest digest, as already pinned in the compose files, so nothing is bumped;
  - the licence and where it is stated;
  - the jobs, by the names they report;
  - why the job needs it.

  The Event Hubs emulator is `mirror: false`, with the maintainer's reason. RustFS is absent: #116 had not landed.
- **`.github/workflows/mirror-ci-images.yml`** runs on `main` only.
  - For each mirrored row it runs `docker buildx imagetools create` from `upstream@digest` to `ghcr.io/hafeok/varve-ci/<name>:<tag>`. With one source, that is a carbon copy of the index.
  - It then reads the mirror's digest back and fails the run if it differs from the pinned one.
  - It reads the package's visibility by an anonymous manifest request and fails the run if the package is public.
  - The run summary has one row per image: copied or already present, and the visibility.
  - The packages REST API does not take `GITHUB_TOKEN`, which is why visibility is read anonymously, as an outsider would read it.
- **Stopgap.** Each `docker compose … up` in `ci.yml` starts with a `pull`: three attempts, with 15 s and then 30 s between them. Only the pull is retried, so a container that fails its health check still fails the job at once. The five places:
  - `auth (mock-oauth2)`;
  - `auth (zitadel)`;
  - both starts in `container`;
  - `samples (eventhubs)`.
- **Each compose file** notes that its digests are also in the list.

### What was measured

From this session's container, the registries' answers for the pinned tags on 2026-10-17:
- Zitadel v4.19.4, mock-oauth2-server 6.0.4, Azurite 3.37.0 and the Event Hubs emulator 2.2.1 resolve to the digests already pinned. Each is a multi-platform index: OCI for Zitadel, Docker manifest lists for the other three.
- `public.ecr.aws` answered the PostgreSQL request with **429**, the failure #118 describes. The list keeps the compose file's digest.

There was no Docker daemon in the container, so the copy script was run
against a stubbed `docker` and `curl` in three cases:
- **a first run:** four copied, the emulator not mirrored, exit 0;
- **a package that came out public:** the run errored and exited 1;
- **a mirror whose digest differed:** each such package errored, exit 1.

`imagetools create` itself is first exercised by the maintainer's run on `main`.

### What was found

- **The sandbox's `yq` is not the runner's.** The sandbox has the Python jq wrapper, which quotes strings; ubuntu runners have mikefarah's yq. `yq -r` reads the same in both, so the workflow uses it everywhere. In mikefarah's yq, `.mirror // true` evaluates to `true` for `false`, which would have mirrored the EULA image. The workflow compares `.mirror` with `false` directly.
- **Visibility of a new package.** The repository is public. A new container package scoped to a personal account is private by default, per GitHub's documentation. The workflow still checks this rather than assuming it.

### What is left

Part 2, after the maintainer's first run of the workflow, as the prompt describes:
- the mirror references and the GHCR login;
- the gate and its fixture;
- the weekly upstream comparison;
- Dependabot's docker-compose block switched off;
- ADR 0099's amendment;
- `CONTRIBUTING.md`.

Until then, a Dependabot bump to a compose file must be matched in the list by hand.

## Addendum, 2026-10-18: #116 landed

The maintainer, after the pull request was opened:

> Yes, watch #125 and fix what comes up. First:
>
> 1. Merge `main`. #116 has landed as preview.8, so add RustFS to `.github/ci-images.yaml`, mirrored, with its digest from the compose or workflow file that #116 put on `main`, its licence (Apache-2.0), jobs and purpose. Leave nothing for PR 2 except switching the references.
> 2. If `container (linux-x64)` fails on the six `http-rdf-update` POST/GET/HEAD/DELETE tests, that's #127, a server bug not caused by this PR. Note it in the PR and don't change anything for it. Any other container failure is yours to look at.
> 3. After pushing, say the new head SHA so the maintainer can approve it.
>
> PR 2 starts once the first `mirror-ci-images` run on `main` is green and the packages are confirmed private and linked.

- **`main` merged**, without conflict. #116 added `.github/storage/compose.yaml`, which starts Azurite and RustFS for the `storage (services)` job.
- **RustFS 1.0.1 is a mirrored row** at the compose file's digest, `sha256:1803faef…6a3c`.
  - Its licence is Apache-2.0.
  - On 2026-10-18 `ghcr.io` resolved the tag to that digest, an OCI index.
- **Azurite's row** now also names `storage (services)`.
- **The storage compose start** has the same stopgap pull retry as the others: six compose starts in all.
- **The Aspire sample.** `samples/Varve.Aspire.Sample/Program.cs` starts mock-oauth2-server itself through Aspire's `AddContainer`, by tag and without a digest, in the `aspire sample` job. It is not a compose file and it is not under `.github/`, so the planned gate would not see it. It is now in the list's `jobs` for that image. Switching it is a question for the maintainer, because the sample is code users run and a private mirror is not reachable for them.
- **Not a container image:** the `bicep` job takes the Microsoft Graph Bicep extension from `mcr.microsoft.com`. It is an OCI artifact the Bicep CLI restores, not a container CI starts, so it is not in the list.

## Addendum, 2026-10-19: ECR's cap, and PostgreSQL seeded by hand

`auth (zitadel)`, a required check, failed on `85639f0` and on every re-run, from 2026-10-18 10:16 to 2026-10-19 06:41: five runs, all three pulls of each refused with `toomanyrequests: Data limit exceeded` from `public.ecr.aws`. The session's container got `429` from ECR Public for the same digest. This PR could not land while the check was red, and the mirror it adds could not copy PostgreSQL from ECR once it had landed.

The maintainer, on the Aspire sample, between those runs:

> Agreed on the Aspire sample. Do it in PR 2, not #125:
> - `samples/Varve.Aspire.Sample` reads the issuer image from configuration. The default is the upstream reference pinned with `WithImageSHA256` to the row's digest, and the `aspire sample` job sets the mirror reference.
> - `eng/ci-images.cs` also finds container images started from C# (`AddContainer` and test fixtures), not only compose and workflow files. An upstream reference is allowed outside `.github/` only when its digest matches a row and the CI job that runs it overrides it to the mirror. Each allowance names that job.
> - Check whether the shipped `Varve.Aspire` package starts any third-party container. If it does, treat it like the sample, and say so in the PR.
> - Leaving the Bicep Graph extension out is right: it's a downloaded file, not a container.
>
> Nothing more on #125. Carry on watching it.

The session proposed three ways out: seed the mirror's PostgreSQL by hand and point CI at it, copy it from Docker Hub at the same digest, or wait. It found that Docker Hub's `library/postgres:18.6-alpine` has the pinned digest exactly. The maintainer:

> Option 1, done. The maintainer seeded `ghcr.io/hafeok/varve-ci/postgres:18.6-alpine` by hand at `sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873`.
> - **Source:** copied from `docker.io/library/postgres` at that digest, because ECR Public answered 429 from the maintainer's machine too.
> - **Credential:** a `gh` sign-in scoped to `write:packages` for the push, then narrowed again. No PAT.
> - **Package settings:** private, connected to Hafeok/Varve, and Hafeok/Varve has Read under Manage Actions access.
>
> Push to #125:
> 1. The Zitadel compose file uses `ghcr.io/hafeok/varve-ci/postgres@sha256:77f5…a1873`.
> 2. `auth (zitadel)` and `container` get `packages: read` and log in to GHCR with `GITHUB_TOKEN` before pulling. No other credential.
> 3. Check that `required-checks` (#89) accepts the new permission, including on any called workflow.
> 4. On the postgres row in `ci-images.yaml`, note that it was seeded by hand on 2026-10-19 from Docker Hub at the pinned digest, because ECR Public's anonymous cap blocked CI and the maintainer's machine. The mirror workflow verifies the digest of a package that already exists, so the hand copy is checked like any other.
> 5. Record the hand seeding in the traceability record, and add a sentence to PR 2's ADR 0099 amendment: a mirror may be seeded by hand from any registry at the pinned digest, because the digest is the image's identity and the source isn't. CI itself still never pulls from Docker Hub.
>
> Then give the new head SHA.

What changed:
- **`.github/zitadel/compose.yaml`** pulls PostgreSQL as `ghcr.io/hafeok/varve-ci/postgres:18.6-alpine@sha256:77f58511…a1873`. Its header says why, and that a run must be logged in to read the private package.
- **`auth (zitadel)`** gets job permissions `contents: read, packages: read`, and logs in to `ghcr.io` with `GITHUB_TOKEN` through the `docker/login-action` pin `ci.yml` already uses.
- **`container`** already had `packages: write`, which includes read, and already logs in to `ghcr.io` before its compose starts. It is unchanged.
- **The permission against callers.** #89's gate is not built yet: `eng/required-checks.cs` has no permissions logic and the issue is open. Checked by hand instead:
  - `ci.yml`'s one caller is `release.yml`'s `gates` job;
  - that job grants `contents: read, packages: write`;
  - `auth (zitadel)` asks `contents: read, packages: read`, within the grant;
  - `required-checks.cs` passes on the change.
- **The postgres row** in `.github/ci-images.yaml` gains `seeded:`, recording the hand copy. In the mirror workflow, a package already at the pinned digest takes the "already present" branch. That branch reads the package's digest, re-tags it from itself, and checks the digest and the visibility, so it never contacts ECR.
- **The register** in `Directory.Packages.props` names the mirror the compose file pulls PostgreSQL from.
- The session read the package anonymously: `401`, so it is not public.

**For part 2's ADR 0099 amendment.** It carries this sentence: a mirror may be seeded by hand from any registry at the pinned digest, because the digest is the image's identity and the source is not; CI itself still never pulls from Docker Hub.

**Found.**
- `.devcontainer/auth-legs.sh` starts the same compose file, so a local run now needs a `docker login ghcr.io` with an account that can read the package. Part 2's `CONTRIBUTING.md` and local-run mechanism, the upstream reference at the same digest, resolves that.
- Dependabot's docker-compose ecosystem watches this file and cannot read the private package. Part 2 switches that ecosystem off for these files.
