---
set: serving-the-web-ui-and-its-content-security-policy
namespace: varve
adr: 0202
decisions:
  - key: ServerServesUnderUi
    statement: "Varve:WebUi:Path names a published Web UI bundle, checked at start, which the server serves under /ui/ on the data-plane listener with GET and HEAD only, before CORS, rate limits and authentication; unset, there is no /ui/"
  - key: RoutesGetIndexFilesGet404
    statement: "Under /ui/, a path whose last segment has no extension is answered with index.html, and a missing path with an extension is a 404 problem"
  - key: NoStaleCompressedSibling
    statement: "A .br or .gz sibling is sent only when the request accepts it and the sibling is not older than its file"
  - key: FingerprintedImmutableRestNoCache
    statement: "Fingerprinted _framework files are cached for a year as immutable, and every other Web UI file is no-cache with an ETag"
  - key: TheWebUiContentSecurityPolicy
    statement: "Every /ui/ response carries a Content Security Policy of default-src 'none' with script-src 'self' 'wasm-unsafe-eval', style-src 'self', connect-src 'self' and the issuer's origin, and frame-ancestors, form-action and object-src 'none', with nosniff, no-referrer and same-origin opener"
  - key: AnyStaticHostWithTheSamePolicy
    statement: "Another static host may serve the bundle if it sends the same policy with the server's origin in connect-src, answers routes with index.html, and the server lists its origin in Varve:Cors:AllowedOrigins"
  - key: ZipAssetNoImage
    statement: "The Web UI ships as a release zip whose digest is in the build attestation, and in the release descriptor once #119 defines the field; it is in no container image"
---

The rulings of [ADR 0202](../adr/0202-serving-the-web-ui-and-its-content-security-policy.md), one line each.
Filed unaccepted by Web UI part 1 (#167); every citation is `CS0618` until the maintainer accepts
them (ADR 0066).
