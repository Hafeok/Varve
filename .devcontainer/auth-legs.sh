#!/usr/bin/env bash
#
# Auth test layers (b) and (c) (ADR 0100), locally, as CI runs them: the same
# compose files, the same seed script, the same tests, both legs required so
# that nothing passes by skipping. Tears the containers down afterwards.
#
#   bash .devcontainer/auth-legs.sh

set -euo pipefail
cd "$(dirname "$0")/.."

mock=.github/mock-oauth2/compose.yaml
zitadel=.github/zitadel/compose.yaml

cleanup() {
  docker compose --file "$mock" down --volumes > /dev/null 2>&1 || true
  docker compose --file "$zitadel" down --volumes > /dev/null 2>&1 || true
  rm -rf .github/zitadel/bootstrap
}
trap cleanup EXIT
cleanup

echo "--- mock-oauth2-server and Zitadel"
docker compose --file "$mock" up --detach
docker compose --file "$zitadel" up --detach --wait

for attempt in $(seq 1 60); do
  curl --silent --fail http://localhost:8080/varve/.well-known/openid-configuration > /dev/null && break
  sleep 1
done

echo "--- seed Zitadel"
dotnet run eng/zitadel-seed.cs -- --output artifacts/zitadel-seed.json

echo "--- the provider tests"
VARVE_MOCK_OAUTH2=http://localhost:8080 \
VARVE_ZITADEL_SEED="$PWD/artifacts/zitadel-seed.json" \
VARVE_AUTH_LEGS_REQUIRED=mock-oauth2,zitadel \
  dotnet test --project tests/Varve.Server.Tests/Varve.Server.Tests.csproj --configuration Release \
    --filter-class Varve.Server.Tests.ProviderTests
