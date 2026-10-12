# The container image

`ghcr.io/hafeok/varve` (ADR 0111): `Varve.Server`'s Native AOT single file on
`mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled`, one image per
architecture (`linux/amd64`, `linux/arm64`) built on a native runner from the
binary the gate ran, joined by digest into one manifest, with build
provenance and an SPDX SBOM attested through GitHub.

- Non-root (`app`, uid 1654), a read-only root filesystem, datasets on a
  volume at `/var/lib/varve`, port 8080. No `HEALTHCHECK`: probe
  `GET /health/ready`.
- Tags: the release descriptor's version; `latest` for a version with no
  prerelease part; `pr-<sha>-<arch>` from a pull request's CI, for its own
  suites, never a release.
- The pull request's CI runs the W3C protocol suites and both auth legs
  against the container it built (`ci.yml`, the `container` jobs); the
  release publishes, attests and verifies (`publish.yml`, the `image` and
  `manifest` jobs).

`docs/operator/run.md` has the operator's `docker run`.
