#!/usr/bin/env bash
#
# Runs once, when the container is created. ADR 0036.
#
# Everything here is something a gate will later fail on if it is missing, and
# the point of doing it at creation is that the failure never happens. The
# session that wrote this ADR started in an environment with no SDK and an
# uninitialised submodule, discovered both by watching a build fail, and that
# is the experience this file exists to remove.

set -euo pipefail

echo "--- git: line endings"

# Milestone 3b found that the Windows checkout rewrote the W3C suite's line
# endings, so the two platforms were testing different bytes and 163 cases
# reported a line number that was wrong on one runner only. .gitattributes
# normalises the working tree to LF, but core.autocrlf=true overrides that
# expectation for anything the submodule does not mark. Conformance inputs must
# be identical everywhere. CR LF handling is tested on documents this
# repository owns, deliberately, not on whatever the checkout produced.
#
# Not `input`: that normalises CR LF to LF on the way in, which would silently
# rewrite the CR LF test documents that are the regression test for the defect
# this setting exists because of.
git config core.autocrlf false

echo "--- git: the W3C conformance suite"

# Without this the suites pass by having nothing in them, which is the failure
# docs/testing.md §1 pins case counts against.
git submodule update --init --recursive

echo "--- dotnet: the WebAssembly workloads"

# Constraint 3's third host. CI builds the browser bundle and requires it to be
# warning-free, so it has to be buildable here too or the first WASM change is
# discovered in CI.
#
# Elevated, because the SDK the devcontainer feature installs lives under
# /usr/share/dotnet and a workload writes into it. The container's default user
# is not root, so without this the install fails with "Inadequate permissions"
# — which is how the first CI run of this job failed, and is the reason the job
# exists rather than a reason to remove it.
if [ "$(id -u)" -eq 0 ]; then
  dotnet workload install wasm-tools wasm-experimental
else
  sudo --preserve-env=DOTNET_ROOT,DOTNET_CLI_TELEMETRY_OPTOUT,DOTNET_NOLOGO \
    "$(command -v dotnet)" workload install wasm-tools wasm-experimental
fi

echo "--- dotnet: restore"

dotnet restore Varve.slnx

cat <<'BANNER'

Ready.

  dotnet run eng/ci.cs            the whole pipeline, as CI runs it
  dotnet run eng/ci.cs -- --list  what it consists of
  dotnet build Varve.slnx -c Release

AGENTS.md is the working method; CONTRIBUTING.md is the rules; docs/brief.md is
the authority.

BANNER
