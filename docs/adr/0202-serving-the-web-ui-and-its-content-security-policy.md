# 0202 — Serving the Web UI, and its Content Security Policy

## Status

**Accepted — filed unaccepted by Web UI part 1 (#167, milestone #96),
2026-10-28** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 1 plan (#96, 2026-10-27):
- the Web UI is served under `/ui/`, from the directory
  `Varve:WebUi:Path` names;
- **D2:** the bundle ships as a zip whose digest goes in the build attestation
  now, and in the release descriptor once #119 defines the field. There is no
  static image.

Records decision set [`serving-the-web-ui-and-its-content-security-policy`](../decisions/serving-the-web-ui-and-its-content-security-policy.md).

## Context

The Web UI (ADR 0200) is static files: `index.html`, two stylesheets, one
script that sets the theme, and Blazor's `_framework`. The build writes a
brotli and a gzip copy beside each. Something has to serve them, with the
right types, caching and headers, and the application's routes
(`/ui/datasets/notes`) have to reach `index.html` on a reload or a deep link.

A browser application that holds bearer tokens is defended first by a Content
Security Policy. With no inline script or style and no third-party origin, the
policy can be close to `'none'`. Blazor's runtime compiles WebAssembly, which
needs `'wasm-unsafe-eval'` and nothing broader.

An operator either runs the server alone, or already has a static host (a CDN,
an ingress serving files) and wants the Web UI there.

## Decision

1. **Variant A: the server serves the Web UI.** `Varve:WebUi:Path` names the
   directory of a published bundle, the `wwwroot` of the zip. It is unset by
   default, and then the server adds no `/ui/` at all. Set, the server checks
   at start that it is an absolute path holding `index.html`, and refuses to
   start otherwise (exit 2, like every other setting).
   - **Where:** under `/ui/` on the data-plane listener, before CORS, rate
     limits and authentication. The files are public, and a token is never
     asked for one. With a management listener (`Varve:Manage:Urls`), `/ui/` is
     not served there.
   - **`/ui`** without the slash redirects (`308`) to `/ui/`, because the
     bundle's `<base href="/ui/">` resolves every relative URL from it.
   - **Only `GET` and `HEAD`.** Anything else is a `405` problem.
   - **Routes and missing files.** A path whose last segment has no extension
     is one of the application's routes, and is answered with `index.html`. A
     missing path with an extension (`_framework/gone.wasm`) is a `404`
     problem. Answering it with `index.html` would hand the runtime HTML where
     it expects a module. Dot files are never served; the file provider
     excludes them. A path that climbs out of the bundle is normalised by
     Kestrel before routing, so it never reaches `/ui/`.
   - **Compression.** When the request accepts `br` (or else `gzip`) and the
     file's `.br` (`.gz`) sibling exists, the sibling is sent with
     `Content-Encoding` and the original's `Content-Type`. Every response
     carries `Vary: Accept-Encoding`. **A sibling older than its file is never
     sent.** An operator who edits `appsettings.json` after unpacking leaves
     its compressed copies stale, and the server serves the edit.
   - **Caching.** A `_framework` file the build named by its content
     (`name.<10 letters or digits>.wasm|js|dat|pdb`) is
     `public, max-age=31536000, immutable`. Everything else is `no-cache`,
     revalidated by an `ETag` of the sent representation's length and time,
     with `304` on a match. That includes `index.html`, `appsettings.json`,
     `blazor.webassembly.js` and `dotnet.js`, which carries the boot manifest.
   - **Same origin as the API**, so the Web UI needs no CORS. The data plane
     and `/manage` are both reached at `'self'`, unless a management listener
     puts `/manage` on another port. Then that origin is a different one: the
     guide says to list the Web UI's origin in `Varve:Cors:AllowedOrigins`
     (ADR 0132).
2. **The headers on every `/ui/` response**, problems and redirects included:
   - **`Content-Security-Policy`:**
     ```
     default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self';
     img-src 'self'; font-src 'self'; connect-src 'self' <issuer origin>;
     manifest-src 'self'; base-uri 'self'; form-action 'none';
     frame-ancestors 'none'; object-src 'none'
     ```
     - `'wasm-unsafe-eval'` compiles the runtime. `script-src 'self'` loads
       `blazor.webassembly.js`, `js/webui.js` and the runtime's dynamic
       `import()`s. `connect-src 'self'` fetches `_framework` and calls the
       API.
     - **The issuer's origin** is `Varve:Auth:Authority`'s scheme, host and
       port, added to `connect-src` for the token requests (ADR 0201). In
       anonymous mode there is none.
     - Sign-in and sign-out are top-level navigations, which `connect-src`
       and `form-action` do not govern.
   - `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` and
     `Cross-Origin-Opener-Policy: same-origin`.
3. **What makes the policy possible is in the bundle, not the server.**
   - There is no inline script, so no import map: the project sets
     `OverrideHtmlAssetPlaceholders` to `false` and loads one external
     `<script src>` (ADR 0200).
   - There is no inline style and no `style` attribute (ADR 0206 point 3).
   - The theme is set before first paint by `js/webui.js`, a file.
4. **Variant B: any static host.** The operator copies the bundle's `wwwroot`
   to the host and:
   - edits `appsettings.json`: `Varve:Server` is the server's base address
     when the host is another origin (empty means the origin that served the
     bundle), and the sign-in settings of ADR 0201;
   - changes `<base href="/ui/">` in `index.html` if the bundle is served
     under another path;
   - lists the host's origin in `Varve:Cors:AllowedOrigins` (ADR 0132);
   - sends the same policy and headers, with the server's origin added to
     `connect-src`;
   - answers the application's routes with `index.html` and a missing file
     with `404`, as point 1 does. A sibling older than its file is deleted
     rather than served.
5. **The test that makes the policy a gate.** The Web UI's browser tests fail
   on any `securitypolicyviolation` event, on every screen (ADR 0204), with the
   bundle served by the server as in point 1.
6. **Distribution (D2).** A release publishes `varve-webui-<version>.zip`, the
   published `wwwroot`, as a release asset with build provenance. Its digest is
   in the attestation now, and goes in the release descriptor when #119 defines
   the field. It is not baked into the server's container image, and there is
   no static image of its own.

## Alternatives considered

- **ASP.NET Core's static-file middleware with a fallback route.** It serves a
  directory well, but it has no notion of a precompressed sibling outside
  `MapStaticAssets`. `MapStaticAssets` serves the build's own manifest of the
  server's assets, not a directory an operator points at. Rewriting paths to
  the siblings around it, and the fallback around both, came to more code than
  the 150 or so lines that serve the three cases this needs.
- **Every missing path answered with `index.html`.** That is the usual SPA
  rule. A missing module then fails as a parse error of HTML, far from its
  cause; a `404` problem names it.
- **The newest sibling regardless of age.** An edited `appsettings.json` would
  be shadowed by the brotli copy the build wrote, and the edit would silently
  do nothing for every browser that accepts brotli: all of them.
- **The Web UI in the server's container image.** It ties the Web UI's
  release to the server image, and adds the bundle to every server that never
  serves it. Refused on the plan (D2), as was a static image of its own.
- **`'unsafe-inline'` for the import map .NET 10 writes.** It would admit
  every inline script an injection could write. One `<script src>` costs
  nothing.

## Consequences

- **`Varve:WebUi:Path`** is a new setting, in `docs/operator/configure.md`.
- **`/ui/` is reserved** on the data-plane listener. No dataset or route is
  named `ui` at the root today, and the router never gives a dataset a root
  path.
- **The guide** (`docs/operator/web-ui.md`, written with the close-out) gives
  both variants, the policy, and the static host's duties of point 4.
- **Checks:**
  - Against the accepted ADRs: 0132 (CORS, unchanged; variant B and a
    management listener use it), 0115 (settings, validated at start), 0119
    (every refusal a problem), 0163 (the management listener), 0200, 0201
    (the issuer origin) and 0206 (no inline style). No conflict.
  - Layer ownership: `Varve.Server`, layer 6.
  - Analyzer rule: none new. `WebUiHostingTests` covers points 1 and 2, and
    the browser tests point 5.
