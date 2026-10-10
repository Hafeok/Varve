# 0111 — The container image: `ghcr.io/hafeok/varve`, Native AOT per architecture, attested

## Status

**Accepted — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Decided by the maintainer on the Operability plan: GHCR only,
Docker Hub a roadmap note. Acceptance is the maintainer's act on the pull
request.

## Context

The roadmap's Operability item left the registry and the base image to the
maintainer. ADR 0099 recorded why Docker Hub is not pulled from in CI: its
anonymous pull limit is per address, and a shared runner's address exhausts
it. ADR 0105 made the Native AOT single file the gate artefact. The 1.0
definition asks for "signed multi-arch container images with an SBOM".

## Decision

1. **The image is `ghcr.io/hafeok/varve`**, on GitHub Container Registry
   alone. Docker Hub is a roadmap note: a mirror on demand under an
   org-owned namespace, never a second source of truth.
2. **Native AOT per architecture on native runners.** `publish.yml` publishes
   `Varve.Server` on `ubuntu-24.04` (`linux-x64`) and `ubuntu-24.04-arm`
   (`linux-arm64`), builds one image per architecture from that binary, and
   joins the two by digest into one multi-architecture manifest. There is no
   cross-compilation and no emulation: the binary the gate ran is the binary
   in the image.
3. **The base is `mcr.microsoft.com/dotnet/runtime-deps`, chiseled**
   (`10.0-noble-chiseled`), the one pull outside GHCR and ECR Public. MCR is
   acceptable where Docker Hub is not: it has no anonymous rate limit of the
   kind that bit CI, it is the runtime's own distribution channel, and the
   chiseled image carries no shell and no package manager. A distroless
   image from another registry would add a second vendor for the same bytes.
4. **The image runs as the non-root `app` user, on a read-only root
   filesystem, with datasets on a volume at `/var/lib/varve`, listening on
   port 8080.** `Varve:DatasetsRoot` defaults to that path in the image and
   nowhere else, by `VARVE__DATASETSROOT`; `ASPNETCORE_URLS` is
   `http://+:8080`. The server writes nothing outside the datasets root, and
   the CI run proves it with `--read-only`.
5. **No `HEALTHCHECK` in the image.** The orchestrator probes
   `GET /health/ready` (ADR 0113); an image-level health check runs a second
   process inside a container that has no shell to run it with, and its
   interval is the orchestrator's decision.
6. **Tags.** The release descriptor's version (`v0.1.0-preview.3`), and
   `latest` only for a version with no prerelease part. A pull request's CI
   publishes `pr-<sha>` for its own suites and nothing else; a `pr-` tag is
   not a release and may be deleted.
7. **Attestations.** `publish.yml` attests build provenance
   (`actions/attest-build-provenance`) and an SBOM (`anchore/sbom-action`
   producing SPDX, `actions/attest-sbom`) for the manifest's digest, and
   then verifies both with `gh attestation verify
   oci://ghcr.io/hafeok/varve@<digest> --owner hafeok`. GitHub's
   attestations need no new account or key: Sigstore signs with the
   workflow's OIDC identity. This gives 1.0 its "signed with an SBOM" line
   now.
8. **The pull request's CI exercises the image it built**: the W3C protocol
   suites and both auth legs run against a container pulled from `pr-<sha>`,
   with `--read-only`, a volume, and the non-root user, before anything is
   published (ADR 0100's legs, ADR 0092's suites).
9. **Nothing is pulled from Docker Hub anywhere**, as ADR 0099 already
   requires of the test containers.

## Alternatives considered

- **Docker Hub beside GHCR.** Two registries to publish to, two sets of
  credentials, and a rate limit on the one that is not GitHub's. A mirror on
  demand is the note.
- **A framework-dependent image on `mcr.microsoft.com/dotnet/aspnet`.** Six
  times the size, a runtime to patch, and not the artefact the gate runs.
- **QEMU emulation for arm64 on one runner.** Native AOT under emulation is
  slow and has failed in ways the native runner does not; GitHub provides the
  runner.
- **Cosign with a key.** A key to keep. Keyless Sigstore through GitHub's
  attestation API is the same signature scheme with no secret.

## Consequences

- An operator runs `docker run -v varve:/var/lib/varve -p 8080:8080
  ghcr.io/hafeok/varve:v0.1.0-preview.3 serve --auth-mode Oidc …`, and
  `docs/operator/run.md` shows it beside the systemd unit.
- `publish.yml` gains `packages: write`, `id-token: write` and
  `attestations: write`; `ci.yml` gains the container jobs, declared required
  in `.github/repo-standard.yaml` as every gate is (ADR 0088).
- The Aspire integration (ADR 0117) names this image.

## Checks

- **Checked against the accepted ADRs** (0001–0109). Touches **0099**
  (registries), **0101** and **0105** (the artefact), **0102** (the tag is
  the descriptor's version). No conflict.
- **Layer ownership.** None; a packaging of `Varve.Server`.
- **Analyzer rule.** None.
- **Open questions owned.** None.
