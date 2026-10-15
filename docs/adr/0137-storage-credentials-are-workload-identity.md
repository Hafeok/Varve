# 0137 — Storage credentials are the workload's identity; a key only on loopback

## Status

**Accepted — filed unaccepted by milestone Object storage of #92, 2026-10-15**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the Object storage plan, 2026-10-15: three Azure token
sources and no others; for an S3-compatible store web identity plus the
loopback key, and nothing else; no key file on a private network.

## Context

ADR 0037 refuses API keys for Varve's callers, permanently: a key is a
long-lived secret, copied into configuration and scripts, that identifies a
string rather than a principal. The object-storage adapters are the opposite
direction — Varve calling a store — but every reason carries over. An Azure
account key, a SAS token, a connection string or a static S3 access key is a
secret that lives in configuration, `--print-config` would have to redact,
and that does not expire or name who used it.

The places Varve runs already give the workload an identity: a managed
identity on Container Apps, a federated workload identity on Kubernetes or
in GitHub Actions, an `az login` on a workstation, an OIDC token projected
into a pod for an S3-compatible store with STS.

## Decision

**1. Outbound workload identity.** The adapters authenticate as the workload,
by an identity it already holds. This is outbound, and leaves ADR 0037's
rule for callers untouched: Varve still accepts only OIDC bearer tokens.

**2. Azure: three token sources, in this order, and no others.**

1. **The Container Apps managed-identity endpoint.** When `IDENTITY_ENDPOINT`
   and `IDENTITY_HEADER` are set: `GET {IDENTITY_ENDPOINT}?resource=https://storage.azure.com/&api-version=2019-08-01`
   with `X-IDENTITY-HEADER`, and `client_id` when `AZURE_CLIENT_ID` names a
   user-assigned identity.
2. **Workload identity.** When `AZURE_FEDERATED_TOKEN_FILE`, `AZURE_TENANT_ID`
   and `AZURE_CLIENT_ID` are set: the file's token sent to
   `{AZURE_AUTHORITY_HOST}{tenant}/oauth2/v2.0/token` as a client assertion
   (`client_credentials`, `scope=https://storage.azure.com/.default`). The
   file is read on every exchange, because the platform rotates it.
3. **`az account get-access-token`**, on a workstation only: when neither of
   the above is configured, the command line's own sign-in, run as a child
   process with `--resource https://storage.azure.com/`.

**3. Tokens stay in the adapter.** A token is cached until five minutes
before its stated expiry and then fetched again; the expiry decides when to
fetch a credential, never anything about the store. A token is never
written to a log, never put in a problem body, never put in an exception's
message and never put on a span or a metric. A test sends a recognisable
token through every path that could carry one and asserts it reaches none.

**4. S3-compatible stores: web identity, and nothing else.** When
`AWS_WEB_IDENTITY_TOKEN_FILE` and `AWS_ROLE_ARN` are set, `Varve.Store.S3`
calls `AssumeRoleWithWebIdentity` at the store's STS endpoint (`sts.amazonaws.com`
for Amazon S3, the server's own for a self-hosted one) and signs with the
temporary credentials it returns, renewing them before they expire. A server
without STS cannot be used, except under decision 5.

**5. The loopback exception, checked against the endpoint.** A static
development key is accepted only when the endpoint's host is a loopback
address (`127.0.0.0/8`, `::1`, `localhost`), and that check is made on the
endpoint itself, not on a flag that could be set anywhere:

- **Azurite**: the published `devstoreaccount1` key, which Microsoft states
  "is intended only for testing … It does not serve any security purpose".
  The adapter knows it; nothing is configured.
- **An S3-compatible server on the same host**: an access key and secret in
  configuration — CI's test server, or a Raspberry Pi whose object store
  runs on the Pi itself.

**6. Keys are refused by configuration.** Configuration validation refuses
an account key, a SAS token, a connection string, or an S3 access key for a
non-loopback endpoint, with a message naming this ADR's rule, and the server
exits 2 as for any refused setting (ADR 0115). There is no switch that admits
one.

## Alternatives considered

- **`DefaultAzureCredential`.** It is the SDK's (ADR 0134, decision 3), and
  its chain includes environment secrets, Visual Studio and others this
  milestone does not need. The three sources above are the ones Varve's
  deployments use.
- **A key file outside configuration, for a NAS on a private network.**
  Refused by the maintainer: identity-only means only identity. A typical NAS
  deployment falls outside both decision 4 and decision 5, and the operator
  guide says so plainly.
- **SAS tokens with a short expiry.** Still a bearer secret in configuration,
  minted by something holding a key.

## Consequences

- `Varve.Server`'s settings for an object-storage dataset hold an endpoint
  and a container or bucket, and on loopback an S3 test key; nothing else
  secret. `--print-config` redacts the test key by name (`*Key`, ADR 0115).
- The Container Apps Bicep gives the app a system-assigned identity and
  `Storage Blob Data Contributor` on the container; nothing else.
- The optional CI leg against a real account uses a GitHub OIDC federated
  credential in an environment only `main` can use, as `entra-obo.yml` does.
- The Raspberry Pi page documents the file backend as the supported small
  machine, and an object store on a NAS only where it has STS or runs on the
  Pi.

## Checks

- **Checked against the accepted ADRs** (0001–0133). Touches **0037** (its
  rule is for callers; this one is outbound, and adds no credential scheme
  for callers), **0115** (validation refuses; redaction by name), **0127**
  (the same "OIDC only" stance for the on-behalf-of sample's federated
  credential), **0133** (no acquiring package ships: the token sources are
  plain HTTP and a child process). No conflict.
- **Layer ownership.** The token sources: `Varve.Store.Azure` and
  `Varve.Store.S3`, layer 5. Validation: `Varve.Server`, layer 6.
- **Analyzer rule.** None.
- **Open questions owned.** None.
