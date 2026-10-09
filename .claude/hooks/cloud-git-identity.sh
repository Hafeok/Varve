#!/usr/bin/env bash
# Cloud sessions sign their commits with the maintainer's agent key (#79).
#
# A cloud sandbox starts with a fresh home directory and a signing key the
# project does not control (ADR 0034). This hook runs at session start, only
# in the cloud, and only when the environment carries the key: it writes the
# key, then makes git author, commit and sign as the agent identity that
# eng/identities.json lists, Claude (Emil) <claude+emil@okkels-klein.dk>.
# The public half is a signing key on the maintainer's GitHub account.
#
# A local session is left alone: it signs with the maintainer's own key
# (ADR 0034 point 6). A cloud session without the variable is left alone
# too, and says so, rather than failing the session.
set -u

[ "${CLAUDE_CODE_REMOTE:-}" = "true" ] || exit 0

if [ -z "${CLAUDE_GIT_SIGNING_KEY:-}" ]; then
    echo "cloud-git-identity: CLAUDE_GIT_SIGNING_KEY is not set; commits in this session are unsigned (ADR 0034)." >&2
    exit 0
fi

if ! command -v ssh-keygen >/dev/null 2>&1; then
    echo "cloud-git-identity: ssh-keygen is missing, which git needs for SSH signing; commits in this session are unsigned." >&2
    exit 0
fi

key="$HOME/.ssh/claude-signing"
mkdir -p "$HOME/.ssh" && chmod 700 "$HOME/.ssh"
umask 077
printf '%s\n' "$CLAUDE_GIT_SIGNING_KEY" > "$key"

if ! ssh-keygen -y -f "$key" > "$key.pub" 2>/dev/null; then
    rm -f "$key" "$key.pub"
    echo "cloud-git-identity: CLAUDE_GIT_SIGNING_KEY is not a readable private key; commits in this session are unsigned." >&2
    exit 0
fi

git config --global user.name "Claude (Emil)"
git config --global user.email "claude+emil@okkels-klein.dk"
git config --global gpg.format ssh
git config --global user.signingkey "$key"
git config --global commit.gpgsign true
git config --global tag.gpgsign true

echo "cloud-git-identity: commits are signed as Claude (Emil) <claude+emil@okkels-klein.dk> with $(ssh-keygen -lf "$key.pub" | cut -d' ' -f2)."
