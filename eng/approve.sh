#!/usr/bin/env bash
# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Post the maintainer's approval on one or more pull requests.
#
#   eng/approve.sh 286            show #286's head and checks, ask, then approve
#   eng/approve.sh 280 281 282    several, one question each
#   eng/approve.sh -n 286         dry run: show everything, post nothing
#   eng/approve.sh -f 286         allow a head with failed or running checks
#
# Run it as yourself (`gh auth status` should show your own login). The comment
# it posts is the approval line and nothing else, with the full sha of the head
# the script showed you: the one form `agent review` and eng/land.cs read as an
# approval (eng/lib/Approvals.cs; ADR 0087, amended 2026-11-12, #290). If the
# head moves between the question and the post, nothing is posted. A comment is
# never edited into an approval: an edited comment approves nothing.
#
# This is the maintainer's script, and the way an approval is posted. A session
# never writes the approval line, in a comment, a description or a commit
# message (AGENTS.md); it reports a head as "head <sha>".

set -euo pipefail

REPO="${VARVE_REPO:-Hafeok/Varve}"
# Checks that are expected to be red or that never gate, so they don't block.
IGNORE='^(agent review|decision report \(never gates\))$'

dry=0
force=0
while getopts "nf" flag; do
    case "$flag" in
        n) dry=1 ;;
        f) force=1 ;;
        *) echo "usage: $0 [-n] [-f] <pull request number>..." >&2; exit 2 ;;
    esac
done
shift $((OPTIND - 1))
[ "$#" -ge 1 ] || { echo "usage: $0 [-n] [-f] <pull request number>..." >&2; exit 2; }

command -v gh >/dev/null || { echo "gh is not installed" >&2; exit 2; }
me="$(gh api user --jq .login)"
echo "Approving as: $me   repository: $REPO"

for pr in "$@"; do
    echo
    echo "=================================================================="
    head="$(gh api "repos/$REPO/pulls/$pr" --jq .head.sha)"
    gh api "repos/$REPO/pulls/$pr" --jq \
        '"#\(.number)  \(.title)\nstate: \(.state)   base: \(.base.ref)   branch: \(.head.ref)\nhead:  \(.head.sha)\nchanged: \(.changed_files) files, +\(.additions) -\(.deletions)"'

    state="$(gh api "repos/$REPO/pulls/$pr" --jq .state)"
    if [ "$state" != "open" ]; then
        echo "-> skipped: the pull request is $state"
        continue
    fi

    echo
    echo "Completes line in the description:"
    gh api "repos/$REPO/pulls/$pr" --jq '.body // ""' | grep -E '^[[:space:]]*Completes:' || echo "  (none)"

    echo
    echo "Files (lockfiles counted, not listed):"
    gh api "repos/$REPO/pulls/$pr/files" --paginate --jq \
        '.[] | select(.filename | endswith("packages.lock.json") | not) | "  \(.filename)  +\(.additions) -\(.deletions)"' \
        | awk 'NR <= 40 { print } END { if (NR > 40) print "  ... and " (NR - 40) " more file(s)" }'
    locks="$(gh api "repos/$REPO/pulls/$pr/files" --paginate --jq \
        '.[] | select(.filename | endswith("packages.lock.json")) | .filename' | wc -l)"
    echo "  ... and $locks lockfile(s)"

    checks="$(gh api "repos/$REPO/commits/$head/check-runs" --paginate --jq \
        '.check_runs[] | [.name, .status, (.conclusion // "-")] | @tsv')"
    total="$(printf '%s\n' "$checks" | grep -c . || true)"
    failed="$(printf '%s\n' "$checks" | awk -F'\t' '$3=="failure" || $3=="timed_out" || $3=="cancelled" {print $1}' | grep -Ev "$IGNORE" || true)"
    running="$(printf '%s\n' "$checks" | awk -F'\t' '$2!="completed" {print $1}' | grep -Ev "$IGNORE" || true)"

    echo
    echo "Checks on this head: $total"
    if [ -n "$failed" ]; then
        echo "  FAILED:"
        printf '%s\n' "$failed" | sed 's/^/    /'
    fi
    if [ -n "$running" ]; then
        echo "  STILL RUNNING:"
        printf '%s\n' "$running" | sed 's/^/    /'
    fi
    if [ -z "$failed" ] && [ -z "$running" ] && [ "$total" -gt 0 ]; then
        echo "  all green (apart from agent review, which waits for this approval)"
    fi

    already="$(gh api "repos/$REPO/issues/$pr/comments" --paginate --jq \
        "[.[] | select(.user.login == \"$me\") | select(.body | startswith(\"approve $head\"))] | length")"
    if [ "$already" -gt 0 ]; then
        echo "-> skipped: you have already approved this exact head"
        continue
    fi

    if { [ -n "$failed" ] || [ -n "$running" ] || [ "$total" -eq 0 ]; } && [ "$force" -eq 0 ]; then
        echo "-> skipped: not green. Use -f to approve anyway."
        continue
    fi

    echo
    echo "Review it: https://github.com/$REPO/pull/$pr/files"
    if [ "$dry" -eq 1 ]; then
        echo "-> dry run: would post \`approve $head\`"
        continue
    fi

    read -r -p "Approve #$pr at ${head:0:12}? [y/N] " answer
    case "$answer" in
        y|Y|yes) ;;
        *) echo "-> not approved"; continue ;;
    esac

    now="$(gh api "repos/$REPO/pulls/$pr" --jq .head.sha)"
    if [ "$now" != "$head" ]; then
        echo "-> not approved: the head moved to ${now:0:12} while you were reading. Run again."
        continue
    fi

    gh api "repos/$REPO/issues/$pr/comments" -f body="approve $head" --jq '"-> approved: \(.html_url)"'
done
