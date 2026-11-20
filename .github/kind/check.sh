#!/usr/bin/env bash
# Checks the fleet deploy.sh deployed, as kubernetes.yml's kind job runs it:
#
#  1. varve-0 writes the system dataset; varve-1 and varve-2 follow it.
#  2. A dataset declared through varve-2 (forwarded to the system writer),
#     placed on varve-0 with varve-1 and varve-2 its standbys, automatic
#     handover on.
#  3. A write through varve-2, forwarded to varve-0; read on varve-1, a
#     standby, at the write's position.
#  4. A hung writer: varve-0's process is stopped (SIGSTOP), so it releases
#     nothing. Its readiness fails, the controller (handover.yaml) sees the
#     pod turn not ready, and takes over: varve-1 holds the dataset and the
#     system dataset, by trigger "probe", and the controller's log says so.
#     A write through varve-2 then reaches varve-1, and varve-2 reads it.
#  5. A deleted writer: varve-1's pod is deleted. It releases for shutdown in
#     its grace, another member holds the dataset, and a write and a read
#     through varve-2 go on.
#
# Run from the runner, with kubectl on the cluster; each pod is reached over
# its own port-forward. Uses curl and jq.
set -euo pipefail

ns=varve
node=${KIND_NODE:-varve-control-plane}
work=$(mktemp -d)
forwards=()
trap 'for p in "${forwards[@]}"; do kill "$p" 2> /dev/null || true; done' EXIT

fail() {
  echo "FAIL: $*"
  exit 1
}

# forward <pod> <port>: a port-forward to the pod's 8080, once it answers.
forward() {
  kubectl --namespace $ns port-forward "pod/$1" "$2:8080" > "$work/forward-$1.log" 2>&1 &
  forwards+=($!)
  for attempt in $(seq 1 60); do
    curl --silent --max-time 2 "http://127.0.0.1:$2/health/live" > /dev/null && return 0
    sleep 1
  done
  cat "$work/forward-$1.log"
  fail "no port-forward to $1"
}

# ready <port>: the pod's readiness document, whatever its status.
ready() {
  curl --silent --max-time 5 "http://127.0.0.1:$1/health/ready" || echo '{}'
}

# until_true <seconds> <description> <command...>: the command, once a second,
# until it succeeds.
until_true() {
  local seconds=$1 what=$2
  shift 2
  for attempt in $(seq 1 "$seconds"); do
    "$@" && return 0
    sleep 1
  done
  fail "$what, after ${seconds}s"
}

management_role() { test "$(ready "$1" | jq -r '.management.role')" = "$2"; }
warm_standby() { ready "$1" | jq -e --arg d "$2" '.datasets[$d] | .role == "standby" and .tailing and .warm' > /dev/null; }
holder() { curl --silent --max-time 5 "http://127.0.0.1:$1/manage/datasets/$2/state" | jq -r '.history[-1].holder // empty'; }
holds() { test "$(holder "$1" "$2")" = "$3"; }

# update <port> <sparql update>: writes, and keeps the position it was written
# at in $work/position. INSERT DATA is idempotent, so a retry after a moved
# writer's refusal changes nothing else.
update() {
  local headers=$work/update-headers.txt status
  status=$(curl --silent --max-time 30 --dump-header "$headers" --output "$work/update-body.txt" --write-out '%{http_code}' \
    --header 'Content-Type: application/sparql-update' --data-binary "$2" "http://127.0.0.1:$1/datasets/kind/sparql") || return 1
  if test "$status" != 204; then
    echo "  the update through :$1 answered $status: $(head -c 300 "$work/update-body.txt")"
    return 1
  fi
  tr -d '\r' < "$headers" | awk -F': ' 'tolower($1) == "varve-position" { print $2 }' > "$work/position"
  test -s "$work/position"
}

# counts <port> <position> <n>: the dataset on that pod, at the position or
# later, holds n triples.
counts() {
  local body
  body=$(curl --silent --max-time 10 --header "Varve-Min-Position: $2" --header 'Accept: text/csv' \
    "http://127.0.0.1:$1/datasets/kind/sparql?query=SELECT%20(COUNT(*)%20AS%20%3Fn)%20%7B%20%3Fs%20%3Fp%20%3Fo%20%7D") || return 1
  test "$(printf '%s' "$body" | tr -d '\r' | sed -n 2p)" = "$3"
}

forward varve-0 18080
forward varve-1 18081
forward varve-2 18082

echo "1. The system dataset: varve-0 writes it, varve-1 and varve-2 follow."
until_true 120 "varve-0 is not the system writer" management_role 18080 writer
until_true 120 "varve-1 does not follow" management_role 18081 follower
until_true 120 "varve-2 does not follow" management_role 18082 follower

echo "2. Declare the dataset through varve-2."
declaration='<urn:kind:kind> a <https://w3id.org/varve/ns#Dataset> ;
  <https://w3id.org/varve/ns#datasetName> "kind" ;
  <https://w3id.org/varve/ns#storage> <https://w3id.org/varve/ns#ObjectStorage> ;
  <https://w3id.org/varve/ns#placedOn> "varve-0" ;
  <https://w3id.org/varve/ns#standbyOn> "varve-1" ;
  <https://w3id.org/varve/ns#standbyOn> "varve-2" ;
  <https://w3id.org/varve/ns#automaticHandover> true .'
status=$(curl --silent --max-time 30 --output "$work/declare.txt" --write-out '%{http_code}' --request PUT \
  --header 'Content-Type: application/trig' --data-binary "$declaration" http://127.0.0.1:18082/manage/datasets/kind)
case "$status" in 2??) ;; *) cat "$work/declare.txt"; fail "the declaration through varve-2 answered $status" ;; esac
until_true 180 "varve-0 does not hold the dataset" holds 18080 kind varve-0
until_true 180 "varve-1 is not a warm standby" warm_standby 18081 kind
until_true 180 "varve-2 is not a warm standby" warm_standby 18082 kind

echo "3. A write through varve-2, read on varve-1."
until_true 60 "the write through varve-2 did not land" update 18082 'INSERT DATA { <http://ex/one> <http://ex/p> 1 }'
position=$(cat "$work/position")
until_true 60 "varve-1 does not read the write at $position" counts 18081 "$position" 1

echo "4. A hung writer: varve-0's process is stopped."
container=$(kubectl --namespace $ns get pod varve-0 --output jsonpath='{.status.containerStatuses[?(@.name=="varve")].containerID}')
pid=$(docker exec "$node" crictl inspect --output go-template --template '{{.info.pid}}' "${container#containerd://}")
docker exec "$node" kill -STOP "$pid"
until_true 120 "varve-1 did not take over the dataset" holds 18081 kind varve-1
test "$(curl --silent --max-time 5 http://127.0.0.1:18081/manage/datasets/kind/state | jq -r '.history[-1].trigger')" = probe \
  || fail "the takeover's trigger is not probe"
until_true 120 "varve-1 is not the system writer" management_role 18081 writer
logs=$(kubectl --namespace $ns logs deployment/varve-handover)
echo "$logs"
grep --quiet 'varve-0 is not ready: taking over from it.' <<< "$logs" || fail "the controller did not log the takeover"
grep --quiet 'Taken over: kind from epoch' <<< "$logs" || fail "the controller did not take over the dataset"
until_true 60 "the write through varve-2 did not land" update 18082 'INSERT DATA { <http://ex/two> <http://ex/p> 2 }'
position=$(cat "$work/position")
until_true 60 "varve-2 does not read the write at $position" counts 18082 "$position" 2

echo "5. A deleted writer: varve-1's pod is deleted."
kubectl --namespace $ns delete pod varve-1 --wait=true
until_true 180 "nobody but varve-1 holds the dataset" bash -c "h=\$(curl --silent --max-time 5 http://127.0.0.1:18082/manage/datasets/kind/state | jq -r '.history[-1].holder // empty'); test -n \"\$h\" && test \"\$h\" != varve-1"
echo "The dataset is held by $(holder 18082 kind)."
until_true 60 "the write through varve-2 did not land" update 18082 'INSERT DATA { <http://ex/three> <http://ex/p> 3 }'
position=$(cat "$work/position")
until_true 60 "varve-2 does not read the write at $position" counts 18082 "$position" 3

kubectl --namespace $ns rollout status statefulset/varve --timeout=10m
echo "ok  the fleet in kind: forwarded writes, follower reads, a takeover from a hung writer, a deleted writer"
