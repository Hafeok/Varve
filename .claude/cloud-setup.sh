#!/usr/bin/env bash
# The cloud environment's setup script (#79).
#
# Runs as root on a fresh Ubuntu 24.04 VM, before Claude Code launches, once
# per environment cache (about seven days), and never on a local machine. It
# provisions what the sandbox lacks and this repository needs. Project setup
# that must run everywhere, cloud and local, belongs in a SessionStart hook
# (.claude/settings.json), not here.
#
# To install it, set the environment's "Setup script" field at claude.ai/code to
#
#   curl -fsSL https://raw.githubusercontent.com/Hafeok/Varve/main/.claude/cloud-setup.sh | bash
#
# so this file stays the one source. A change here reaches sessions only when
# the environment cache is rebuilt, which editing the field (any edit) forces.
#
# Two platform rules shape every line: exit non-zero and the session does not
# start; take longer than about five minutes and nothing is cached. So each
# step is guarded and reports, and the session's hook checks for what it needs.
set -u
export DEBIAN_FRONTEND=noninteractive

echo "--- openssh-client: ssh-keygen, which git needs to sign with the agent key"

# The sandbox ships without it (measured 2026-10-09), so the hook that signs
# commits as the agent, .claude/hooks/cloud-git-identity.sh, had nothing to
# sign with and left the session unsigned.
if ! command -v ssh-keygen >/dev/null 2>&1; then
    (apt-get update -qq && apt-get install -y -qq openssh-client) \
        || echo "openssh-client: install failed; commits in this environment stay unsigned" >&2
fi

echo "--- dotnet: the SDK global.json pins"

# The platform documents the .NET SDK as not pre-installed. The version is the
# one global.json and .devcontainer/devcontainer.json pin, exactly: global.json
# rolls forward within the feature band only, and a lower band refuses to load.
# Keep the three in step. Not read from global.json because the repository may
# not be cloned yet when this runs.
sdk_version=10.0.401
dotnet_root=/usr/share/dotnet
if ! "$dotnet_root/dotnet" --list-sdks 2>/dev/null | grep -q "^$sdk_version "; then
    (curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
        && bash /tmp/dotnet-install.sh --version "$sdk_version" --install-dir "$dotnet_root" \
        && ln -sf "$dotnet_root/dotnet" /usr/bin/dotnet) \
        || echo "dotnet: install of $sdk_version failed; the gates cannot run" >&2
    rm -f /tmp/dotnet-install.sh
fi
cat > /etc/profile.d/dotnet.sh <<PROFILE
export DOTNET_ROOT=$dotnet_root
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
PROFILE

# Not installed here, on the five-minute budget: the WebAssembly workloads the
# devcontainer adds (dotnet workload install wasm-tools wasm-experimental). A
# session that needs the browser host installs them, and says so in its record.

echo "--- what this VM now has"
command -v ssh-keygen >/dev/null 2>&1 && echo "ssh-keygen: $(command -v ssh-keygen)" || echo "ssh-keygen: missing"
command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks || echo "dotnet: missing"
exit 0
