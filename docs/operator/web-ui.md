# The Web UI

The Web UI is an operator console in the browser. It is a Blazor WebAssembly
client of the public API, and has **no endpoints of its own** (ADR 0200). It
can do nothing the API and `varve` cannot.

**What it shows:**
- every dataset, with its role, head and lag;
- a dataset's status, as of a position as well as at its head;
- the fleet;
- a dataset's commits, followed live, and one commit's delta;
- the declarations: a dataset's grants and its declaration source.

**What it does:** takes a checkpoint, and closes and opens a dataset. It
asks before each, and closing needs the dataset's name typed.

A release publishes the Web UI as `varve-webui-<version>.zip`, a release
asset with build provenance. It is not in the server's container image. The
zip is the published `wwwroot`: `index.html`, `appsettings.json`,
`_framework/` and the styles. Check it before you unpack it:

```sh
gh attestation verify varve-webui-0.1.0-preview.N.zip --owner hafeok
```

There are two ways to host it.

## A — the server serves it

Unpack the zip, and point `Varve:WebUi:Path` at the directory:

```json
{ "Varve": { "WebUi": { "Path": "/opt/varve/webui" } } }
```

The server then serves the Web UI at `/ui/` on the data-plane listener.
- **At start,** the server refuses to start if the directory is not an
  absolute path holding `index.html`.
- **Same origin:** the Web UI calls the API at its own origin, so there is
  no CORS to configure. With a management listener (`Varve:Manage:Urls`),
  `/manage` is on another origin. List `https://<host>:<data port>` in
  `Varve:Cors:AllowedOrigins` (ADR 0132), and set `Varve:Server` in the
  bundle's `appsettings.json` to the data-plane origin.
- **Public files:** no token is ever asked for a file under `/ui/`, and every
  call the Web UI makes to the API carries one.
- **What the server sends** (ADR 0202):
  - `/ui` redirects to `/ui/`;
  - a route such as `/ui/datasets/notes` is answered with `index.html`, and
    a missing file with a `404` problem;
  - only `GET` and `HEAD` are answered;
  - a `.br` or `.gz` copy is sent when the browser accepts it **and** the copy
    is not older than its file, so an edited `appsettings.json` is served as
    edited;
  - `_framework` files named by their content are cached for a year, and
    everything else is revalidated.

Nothing restarts when you edit `appsettings.json`: the next load reads it.

## B — any static host

Copy the zip's contents to the host, then:
1. **In `appsettings.json`,** set `Varve:Server` to the server's base
   address, such as `https://varve.example.org/`. Empty means the origin
   that served the Web UI.
2. **In `index.html`,** change `<base href="/ui/">` if the Web UI is served
   under another path. Keep the trailing slash.
3. **On the server,** list the host's origin in `Varve:Cors:AllowedOrigins`.
4. **On the host:**
   - answer every path whose last segment has no extension with
     `index.html`;
   - answer a missing file with `404`, never with `index.html`;
   - delete the `.br` and `.gz` copies of any file you edit, or the host will
     send the old one;
   - send the headers below.

## The Content Security Policy

Every `/ui/` response from the server carries this policy, problems and
redirects included. A static host must send the same policy:

```
default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self';
img-src 'self'; font-src 'self'; connect-src 'self' <issuer origin>;
manifest-src 'self'; base-uri 'self'; form-action 'none';
frame-ancestors 'none'; object-src 'none'
```

with `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` and
`Cross-Origin-Opener-Policy: same-origin`.

- **`<issuer origin>`** is the scheme, host and port of `Varve:Auth:Authority`:
  the browser fetches the issuer's discovery document and token endpoint. In
  anonymous mode there is none.
- **On a static host,** add the server's origin to `connect-src` as well.
- **`'wasm-unsafe-eval'`** lets the browser compile the .NET runtime.
  Nothing else is relaxed:
  - the Web UI has no inline script and no inline style;
  - it sets the theme before first paint from a file.

The Web UI's browser tests fail on any policy violation, on every screen
(ADR 0204).

## Signing in

The bundle's `appsettings.json`, under `Varve:Auth` (ADR 0201):

| Setting | Meaning |
|---|---|
| `Mode` | `Oidc`, or `Anonymous` for a server in anonymous mode |
| `Authority` | the issuer: the server's `Auth:Authority` |
| `ClientId` | the Web UI's own public client at the issuer |
| `DataScope` | the scope whose token the data plane accepts (its audience is one of the server's `Auth:Audiences`) |
| `ManageScope` | the scope whose token the management plane accepts (`Auth:Management:Audience`) |

```json
{
  "Varve": {
    "Server": "",
    "Auth": {
      "Mode": "Oidc",
      "Authority": "https://login.microsoftonline.com/<tenant>/v2.0",
      "ClientId": "<the Web UI's client id>",
      "DataScope": "api://varve/access_as_user",
      "ManageScope": "api://varve-manage/access_as_user"
    }
  }
}
```

**How sign-in works:**
- **The sign-in:** authorisation code with PKCE, at the issuer's own page.
  The redirect address to register is `<web ui>/signin`, for example
  `https://varve.example.org/ui/signin`. The address after sign-out is
  `<web ui>/signedout`.
- **One token per plane.** Each plane accepts only its own audience, and
  refuses a token naming both (ADR 0163).
  - The data plane's token comes from the sign-in.
  - The management plane's token comes from a refresh-token grant that asks
    for `ManageScope` alone.
  - So the issuer must let the Web UI's refresh token be redeemed for the
    management scope. Where it will not, the shell shows the management
    plane signed out, and the data plane still works.
- **Where tokens live:** in the tab's memory, never in browser storage.
  - A reload signs in again silently (`prompt=none`) while the issuer's
    session lasts, and interactively when it has ended.
  - Sign out ends the issuer's session and returns to `/ui/signedout`.
- **What the Web UI may do:** the person's own roles decide it. The datasets
  list, the fleet and the declarations are management reads. Taking a
  checkpoint, closing and opening need an operator or the dataset's admin
  (see [Authenticate](authenticate.md)).

### Issuers

| Issuer | Registration | One token per plane |
|---|---|---|
| **Microsoft Entra ID** | a *single-page application* platform on the Web UI's registration, so the token endpoint answers CORS; a delegated scope on each API (`api://varve/access_as_user`, `api://varve-manage/access_as_user`), as in [Authenticate](authenticate.md) | yes: a refresh token is redeemed for another resource. Documented, not tested in CI |
| **mock-oauth2-server** (tests) | `tests/fixtures/auth/mock-oauth2.json` maps the `varve-webui` client to the data audience, and `varve-webui-manage` to the management one | yes, and it answers CORS. Tested in CI by the browser tests |
| **Zitadel** | a user agent application with PKCE | **not yet.** A refresh never narrows a token's audience, and refuses a new scope (`invalid_scope`). Token exchange (RFC 8693) with `audience` would give each plane its own token. It is pending a decision on #96. Until then, the management plane stays signed out |
| **Keycloak, Auth0, Okta** | two audiences, as two client scopes or two APIs | per issuer: check that a refresh token is redeemed for the management scope before you rely on it |

## Anonymous mode

A server in anonymous mode (`Varve:Auth:Mode` is `Anonymous`) needs
`"Mode": "Anonymous"` in the bundle too. The shell then says both planes are
anonymous, and there is no sign-in. It is for a laptop or a test, as the
server's anonymous mode is.

## What it costs

- **The download:** about 1.8 MB, brotli-compressed, on a first visit. The
  `_framework` files are then cached until a new version. The gate is
  `eng/browser-size.cs`, and its budget is 1,920,000 bytes (ADR 0205).
- **Not AOT-compiled:** that would make the download 4.1 MB and saved no
  load time when measured.
