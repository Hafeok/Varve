# syntax=docker/dockerfile:1
#
# The container image (ADR 0111): the Native AOT single file for one
# architecture on the chiseled runtime-deps base, which carries no shell and
# no package manager. Built per architecture on a native runner from the
# binary the gate ran; publish.yml joins the two by digest into one manifest.
#
#   dotnet publish src/Varve.Server -c Release -o artifacts/server-aot
#   docker build -f src/Varve.Server/container/Dockerfile -t varve artifacts/server-aot
#   docker run --rm --read-only -v varve:/var/lib/varve -p 8080:8080 varve serve --anonymous
#
# The build context is the publish output: the binary alone is copied.

# The datasets root, owned by the non-root user the base image runs as (uid
# 1654, `app`). Made on the full image, which has a shell, and copied: the
# chiseled image cannot mkdir or chown, and a volume mounted on a directory
# the image lacks would be root's.
#
# Both bases are pinned by digest (ADR 0111, amended 2026-10-27; ADR 0224
# point 5), the tag kept for the reader and for Dependabot, which proposes a
# new digest when the tag moves. The chiseled base's digest is also what
# publish.yml scans for the image SBOM's OS packages (ADR 0225 point 5).
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble@sha256:62028293921f5bd6db3d9f9119cd29cd926dfd3c2336da23b09f425e40c3c832 AS layout
RUN mkdir -p /var/lib/varve && chown 1654:1654 /var/lib/varve

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled@sha256:4309626281bfe550c0f6bb4242c435cd5cdabed1d05a5bc5843f659336f53296

# Varve.Server on Linux and macOS, Varve.Server.exe on Windows; here Linux.
ARG BINARY=Varve.Server
COPY --chmod=755 ${BINARY} /app/varve
COPY --from=layout --chown=1654:1654 /var/lib/varve /var/lib/varve

# Where it listens and where datasets live, in the image and nowhere else
# (ADR 0111 point 4); any VARVE__* variable or argument overrides.
ENV ASPNETCORE_URLS=http://+:8080 \
    VARVE__DATASETSROOT=/var/lib/varve

VOLUME /var/lib/varve
EXPOSE 8080
USER 1654
WORKDIR /app

# No HEALTHCHECK (ADR 0111 point 5): the orchestrator probes GET /health/ready.
ENTRYPOINT ["/app/varve"]
CMD ["serve"]
