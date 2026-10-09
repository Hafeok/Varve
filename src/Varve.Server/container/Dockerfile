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
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS layout
RUN mkdir -p /var/lib/varve && chown 1654:1654 /var/lib/varve

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled

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
