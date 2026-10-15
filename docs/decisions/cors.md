---
set: cors
namespace: varve
adr: 0132
decisions:
  - key: CorsOriginsConfigured
    statement: "Varve:Cors:AllowedOrigins lists the origins a browser may call from; with none, no response carries an Access-Control header"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: CorsNeverCredentials
    statement: "Access-Control-Allow-Credentials is never sent; bearer tokens need no credentials mode"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: CorsHeadersListed
    statement: "The allowed request headers and the exposed response headers are listed one by one, including Authorization, If-Match, Varve-As-Of, Varve-Position and ETag"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: CorsPreflightCached
    statement: "Varve:Cors:MaxAge, ten minutes by default, is the preflight's Access-Control-Max-Age"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: CorsBeforeEverything
    statement: "The CORS middleware runs before rate limiting and authentication, so a preflight needs no token and every problem is readable by the browser"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
---

The rulings of [ADR 0132](../adr/0132-cors.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration of #93
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.
