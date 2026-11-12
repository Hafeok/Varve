---
set: provider-legs-by-federated-credentials
namespace: varve
adr: 0223
decisions:
  - key: EntraAndGoogleBySecret
    statement: "Entra ID and Google have one end-to-end test each, on pushes to main and not a required check, reached by the job's own GitHub OIDC token as a federated credential and by no stored secret, and skipped unless their identifiers exist in a main-restricted environment; moved from ADR 0100 by ADR 0223's supersession in part, restated"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-10T00:00:00Z
  - key: EntraByFederatedClientAssertion
    statement: "The Entra leg sends GitHub's OIDC token for the audience api://AzureADTokenExchange as the client_assertion of a client-credentials grant to the v2 token endpoint, the client's federated credential trusting the subject repo:Hafeok/Varve:environment:auth-providers, and asserts oid as the subject and the app role in roles"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-10T00:00:00Z
  - key: GoogleByWorkloadIdentityFederation
    statement: "The Google leg exchanges GitHub's OIDC token at Google STS for a federated access token, which calls IAM Credentials generateIdToken for the service account with an audience of our choosing, the pool's provider restricted by an attribute condition to this repository and the auth-providers environment, and grants the permission to the email claim"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-10T00:00:00Z
  - key: ExchangesAreHttpCallsInTheTest
    statement: "No third-party action does either exchange; they are plain HTTP calls in the test, and id-token write is granted to the auth (providers) job only"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-10T00:00:00Z
  - key: ProviderLegConfigurationIsIdentifiers
    statement: "A provider leg's configuration is identifiers only, the tenant, client id and audience for Entra and the workload identity provider's resource name and service account email for Google, and no client secret or service-account key is ever added"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-10T00:00:00Z
  - key: ConfiguredLegNeverSkips
    statement: "A provider leg with none of its identifiers skips naming every one missing; with any of them it is configured, and a missing identifier, token or failed exchange fails it and is never turned into a skip"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-10T00:00:00Z
---

The rulings of [ADR 0223](../adr/0223-provider-legs-by-federated-credentials.md),
filed unaccepted by #101 (ADR 0066) and accepted on #242 on 2026-11-10, at
the maintainer's word, by his delegate (ADR 0239).

`EntraAndGoogleBySecret` moves here from
[ADR 0100's set](authentication-tested-in-three-layers.md) under its key,
restated (ADR 0068 point 4). A key is never renamed, so it keeps a name that
no longer describes it: the leg holds no secret.
