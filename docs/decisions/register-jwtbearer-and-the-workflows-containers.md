---
set: register-jwtbearer-and-the-workflows-containers
namespace: varve
adr: 0099
decisions:
  - key: JwtBearerInTheServerOnly
    statement: "Varve.Server alone takes Microsoft.AspNetCore.Authentication.JwtBearer and its Microsoft.IdentityModel closure, on the condition that its Native AOT publish stays green"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: SharedFrameworkIsTheRuntime
    statement: "The ASP.NET Core shared framework is referenced by FrameworkReference and is part of the runtime, not a registered package"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: NoTestPackageForHosting
    statement: "The tests host the server and the OIDC issuer on Kestrel on loopback and sign tokens with the BCL, taking no TestHost or JWT package"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ContainersAreTheWorkflows
    statement: "The identity providers of the CI legs are containers pinned by digest that the workflow starts, and no test or package starts a container"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ImagesFromUnlimitedRegistries
    statement: "The test images are pinned by digest in compose files and pulled from registries without an anonymous pull limit, never from Docker Hub"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: TestContainersFromTheMirror
    statement: "Every third-party test container CI starts is listed in .github/ci-images.yaml and pulled from this repository's private mirror, ghcr.io/hafeok/varve-ci, at the listed digest after a login with GITHUB_TOKEN; only a row marked mirror: false with a stated reason is pulled upstream at its digest, and eng/ci-images.cs checks every reference; amended 2026-10-19"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: MirrorSeededByDigest
    statement: "A mirror package may be seeded by hand from any registry at the pinned digest, because the digest is the image's identity and the source is not; CI itself never pulls from Docker Hub; amended 2026-10-19"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: WatchReadByManifestHead
    statement: "A row of .github/ci-images.yaml may name a watch reference, read by manifest HEAD only, when its upstream refuses CI's reads; Docker Hub is allowed there and nowhere else, and eng/ci-images.cs refuses docker.io in every other reference; amended 2026-10-20"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-23T00:00:00Z
---

The rulings of [ADR 0099](../adr/0099-register-jwtbearer-and-the-workflows-containers.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
