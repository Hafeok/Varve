---
set: register-jwtbearer-and-the-workflows-containers
namespace: varve
adr: 0099
decisions:
  - key: JwtBearerInTheServerOnly
    statement: "Varve.Server alone takes Microsoft.AspNetCore.Authentication.JwtBearer and its Microsoft.IdentityModel closure, on the condition that its Native AOT publish stays green"
  - key: SharedFrameworkIsTheRuntime
    statement: "The ASP.NET Core shared framework is referenced by FrameworkReference and is part of the runtime, not a registered package"
  - key: NoTestPackageForHosting
    statement: "The tests host the server and the OIDC issuer on Kestrel on loopback and sign tokens with the BCL, taking no TestHost or JWT package"
  - key: ContainersAreTheWorkflows
    statement: "The identity providers of the CI legs are containers pinned by digest that the workflow starts, and no test or package starts a container"
---

The rulings of [ADR 0099](../adr/0099-register-jwtbearer-and-the-workflows-containers.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
