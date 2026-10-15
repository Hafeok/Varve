---
set: on-behalf-of-and-the-token-provider
namespace: varve
adr: 0127
decisions:
  - key: TokenProviderPerRequest
    statement: "SparqlHttpClient takes a BearerTokenProvider called for every request; a token within the skew of expiry is asked for once more, and one still stale is refused before any byte is sent"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: ExpiryOnTheClientsTimeProvider
    statement: "Token expiry is measured on the TimeProvider the client is given, never an ambient clock"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: ActingApplicationOnTheSpan
    statement: "Auth:ApplicationClaim (azp by default) names the acting application, which ICallerIdentity reports and the request span carries as varve.azp, never the log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: AgentIsTheTokenSubject
    statement: "A token exchanged on behalf of a user makes commits whose agent is that user; the server does not distinguish an exchanged token from any other"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: AttributionIsTheCallersChoice
    statement: "A write through on-behalf-of records the user as agent and a write on the application's own account uses its client-credentials token and records the application; the guide states this as the caller's choice per write"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: ExchangeTestedAgainstEntraOnly
    statement: "The on-behalf-of exchange is exercised only against Entra, in a workflow_dispatch run in a main-only environment, with the calling application signed in by a GitHub federated credential and the user by an interactive device-code sign-in, so no credential is stored; CI proves Varve's side with a minted token carrying oid and azp, and no exchange is simulated"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: TokenIsOpaqueAtTheBoundary
    statement: "The access token is an opaque string and its expiry an instant, as the issuer gives them; they cross the client's boundary unwrapped, since the client reads neither and a wrapper would only rename them"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
---

The rulings of [ADR 0127](../adr/0127-on-behalf-of-and-the-token-provider.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration of #93
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.
