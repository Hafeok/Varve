---
set: storage-credentials-are-workload-identity
namespace: varve
adr: 0137
decisions:
  - key: OutboundWorkloadIdentity
    statement: "The object-storage adapters authenticate as the workload by an identity it already holds; this is outbound and leaves ADR 0037's rule for callers untouched"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: AzureTokenSources
    statement: "Varve.Store.Azure takes a token from the Container Apps managed-identity endpoint, from a workload-identity token file exchanged as a client assertion, or on a workstation from az account get-access-token, and from nothing else"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: TokensStayInTheAdapter
    statement: "A token is cached until shortly before it expires and is never logged, never put in a problem body and never put in telemetry"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: S3WebIdentityOnly
    statement: "Varve.Store.S3 takes credentials from AssumeRoleWithWebIdentity with an OIDC token read from a file, and from nothing else"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: LoopbackDevelopmentKey
    statement: "A static development key is accepted only when the endpoint is a loopback address, checked against the endpoint and not a flag: Azurite's published key, or a test server's"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: KeysRefusedByConfiguration
    statement: "Account keys, SAS tokens, connection strings and static access keys are refused by configuration validation with a problem naming this rule"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: AdapterModels
    statement: "Varve.Store.Azure.Model and Varve.Store.S3.Model are the adapters' model namespaces and hold their options"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
---

The rulings of [ADR 0137](../adr/0137-storage-credentials-are-workload-identity.md), filed unaccepted by
milestone Object storage of #92 (ADR 0066).
