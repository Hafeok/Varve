#!/usr/bin/env bash
# Deploys deploy/kubernetes/ to the current kind cluster, as kubernetes.yml's
# kind job runs it: Azurite first, its container made, then the sample
# rendered with the kind patches, its placeholders filled, and applied; then
# waits for every pod to be ready.
#
#   GHCR_USER=<user> GHCR_TOKEN=<token> .github/kind/deploy.sh
#
# The image ghcr.io/hafeok/varve:kind must already be loaded into the cluster
# (kind load docker-image). GHCR_TOKEN reads the private mirror (#118); it
# becomes a pull secret in the cluster, which goes when the cluster goes.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
work=$(mktemp -d)

cp "$here/kustomization.yaml" "$here/fleet-patch.yaml" "$here/handover-patch.yaml" "$work/"
cp "$root/deploy/kubernetes/fleet.yaml" "$root/deploy/kubernetes/handover.yaml" "$work/"

kubectl create namespace varve
kubectl --namespace varve create secret docker-registry ghcr \
  --docker-server=ghcr.io --docker-username="$GHCR_USER" --docker-password="$GHCR_TOKEN"

# The store, and its one container. The container is made over a port-forward,
# so the request is to a loopback address, signed with Azurite's published
# development key: the one key ADR 0137 allows, and only on loopback.
kubectl apply --filename "$here/azurite.yaml"
kubectl --namespace varve rollout status deployment/azurite --timeout=5m

kubectl --namespace varve port-forward service/azurite 10000:10000 > "$work/azurite-forward.log" 2>&1 &
forward=$!
trap 'kill $forward 2> /dev/null || true' EXIT

for attempt in $(seq 1 30); do
  (exec 3<> /dev/tcp/127.0.0.1/10000) 2> /dev/null && break
  test "$attempt" -lt 30 || { cat "$work/azurite-forward.log"; exit 1; }
  sleep 1
done

key='Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=='
date=$(LC_ALL=C TZ=GMT date '+%a, %d %b %Y %H:%M:%S GMT')
version=2025-11-05
# Shared Key's string to sign for a Put Container: the verb, eleven empty
# standard headers, the x-ms- headers, and the canonical resource, whose path
# names the account twice in Azurite's path-style address.
signed=$(printf 'PUT\n\n\n\n\n\n\n\n\n\n\n\nx-ms-date:%s\nx-ms-version:%s\n/devstoreaccount1/devstoreaccount1/datasets\nrestype:container' "$date" "$version")
hexkey=$(printf '%s' "$key" | base64 --decode | od -An -tx1 -v | tr -d ' \n')
signature=$(printf '%s' "$signed" | openssl dgst -sha256 -mac HMAC -macopt "hexkey:$hexkey" -binary | base64 --wrap=0)

status=$(curl --silent --output "$work/container.txt" --write-out '%{http_code}' --request PUT \
  --header "x-ms-date: $date" --header "x-ms-version: $version" \
  --header "Authorization: SharedKey devstoreaccount1:$signature" \
  'http://127.0.0.1:10000/devstoreaccount1/datasets?restype=container')

if test "$status" != 201; then
  echo "Azurite answered $status to the container's creation:"
  cat "$work/container.txt"
  exit 1
fi

kill $forward 2> /dev/null || true

# The sample, patched for kind and with its placeholders filled. None is
# read: anonymous mode reads no tenant, and kind has no workload identity to
# read the client ids. {pod} is the controller's own and stays.
kubectl kustomize "$work" \
  | sed -e 's/{tenant}/00000000-0000-0000-0000-000000000000/g' \
        -e 's/{storeClientId}/00000000-0000-0000-0000-000000000001/g' \
        -e 's/{operatorClientId}/00000000-0000-0000-0000-000000000002/g' \
  > "$work/rendered.yaml"

if grep --line-number --extended-regexp '\{[A-Za-z]+\}' "$work/rendered.yaml" | grep --invert-match '{pod}'; then
  echo "A placeholder above is left unfilled."
  exit 1
fi

kubectl apply --filename "$work/rendered.yaml"
kubectl --namespace varve rollout status statefulset/varve --timeout=10m
kubectl --namespace varve rollout status deployment/varve-handover --timeout=5m
kubectl --namespace varve get pods --output wide
