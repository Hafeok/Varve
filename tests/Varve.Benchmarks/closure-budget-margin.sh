#!/usr/bin/env bash
# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# The closure budget's margin (ADR 0195, amended 2026-11-18, point 3): a
# from-scratch build must finish under a hard heap limit of the heap held
# before it plus MARGIN of what its budget counted at peak. It fails when a
# build needs more than the budget assumes, less the margin it keeps.
#
#   closure-budget-margin.sh <benchmarks binary> <S|M|L> <rdfs|owl2rl> <per-graph|cross> [bisect]
#
# With "bisect", it also finds the least heap limit the build finishes under,
# to 4 MiB, and prints need over counted. MARGIN defaults to 0.9: the budget
# counts at least a ninth more than a build needs.
set -uo pipefail

bin=$1 size=$2 profile=$3 mode=$4 bisect=${5:-}
margin=${MARGIN:-0.9}

probe=$("$bin" --closure-budget "$size" "$profile" "$mode" 2>&1 | grep '^budget')
if [ -z "$probe" ]; then
  echo "closure-budget-margin: the probe run of $size $profile $mode did not finish" >&2
  exit 2
fi
echo "$probe"
before=$(sed -E 's/.*live before ([0-9]+).*/\1/' <<<"$probe")
peak=$(sed -E 's/.*; peak ([0-9]+).*/\1/' <<<"$probe")

limit=$(python3 -c "print(int($before + $margin * $peak))")
if DOTNET_GCHeapHardLimit=$(printf '0x%X' "$limit") "$bin" --closure-budget "$size" "$profile" "$mode" >/dev/null 2>&1; then
  echo "margin: $size $profile $mode finished under $limit bytes, the heap before and $margin of the budget's peak"
else
  echo "margin: $size $profile $mode did NOT finish under $limit bytes, the heap before and $margin of the budget's peak: it needs more than the budget assumes (#335)"
  exit 1
fi

if [ "$bisect" = bisect ]; then
  lo=$(( before / 1048576 )) hi=$(( limit / 1048576 + 1 ))
  while [ $(( hi - lo )) -gt 4 ]; do
    mid=$(( (lo + hi) / 2 ))
    if DOTNET_GCHeapHardLimit=$(printf '0x%X' $(( mid * 1048576 ))) "$bin" --closure-budget "$size" "$profile" "$mode" >/dev/null 2>&1; then hi=$mid; else lo=$mid; fi
  done
  echo "need: $size $profile $mode least heap $hi MiB; need over counted $(python3 -c "print(round(($hi * 1048576 - $before) / $peak, 3))")"
fi
