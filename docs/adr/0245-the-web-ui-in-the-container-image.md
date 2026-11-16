# 0245 — The Web UI in the container image, served by default, with its settings from the server

## Status

**Accepted — filed unaccepted by the session for #296, 2026-11-16**
(ADR 0066). Acceptance is the maintainer's act on the pull request, or his
delegate's for a decision he has read (ADR 0239).

Decided by the maintainer:
- the ruling on #296 of 2026-11-16, *the image ships the Web UI, and serves
  it by default*;
- the rulings the session prompt records as closed (points 1 to 6 below);
- his answers to the plan, recorded on #296 on the same day:
  - the off switch's name;
  - an explicit `Varve:WebUi:Settings`, and no detection by a missing file;
  - the management listener split out to #297.

**Supersedes in part [0202](0202-serving-the-web-ui-and-its-content-security-policy.md)**
point 6 (D2): the bundle is now in the server's image. The zip stays.
`ZipAssetNoImage` stays in 0202's set as accepted, marked superseded, and
its successor is `WebUiInTheImageAndTheZipStays` in this ADR's set.

**Amends, each by a dated amendment in its own file:**
- [0202](0202-serving-the-web-ui-and-its-content-security-policy.md): point 1's default, and the management listener;
- [0201](0201-the-web-uis-own-oidc-client.md): where the console's settings come from, and the mode `NotConfigured`;
- [0111](0111-the-container-image.md): what the image holds and sets;
- [0225](0225-the-sbom-generator.md): the image's SBOM holds the bundle.

Records decision set [`the-web-ui-in-the-container-image`](../decisions/the-web-ui-in-the-container-image.md).

## Context

**Before this ADR, the image did not carry the Web UI.** To see the console, an operator:
1. downloaded `varve-webui-<version>.zip` from the release;
2. unpacked it and mounted the directory into the container;
3. set `Varve:WebUi:Path`;
4. for real sign-in, also edited `appsettings.json` inside the unpacked bundle.

**ADR 0202's D2 kept the bundle out of the image for two reasons, and neither held up:**

| D2's reason | Why it does not hold |
|---|---|
| It would tie the Web UI's release to the server image. | The Web UI and the server are cut from one descriptor at one version, so there is no separate Web UI release to protect. |
| It would add the bundle to every server that never serves it. | The bundle is about 10 MB unpacked and 6.4 MB compressed. |

The first thing anyone trying Varve wants is `docker run` and a browser.

**What a file in an image cannot do.** A file inside an image is not something anyone edits, so the console's settings have to come from somewhere else. The server's configuration already holds:
- the issuer the console must sign in with: the server's own `Varve:Auth:Authority`, whose origin the CSP's `connect-src` already names;
- the mode, anonymous or OIDC.

**The console cannot tell "not configured" from "anonymous".** It treated any mode other than `Oidc` as anonymous. So an OIDC server whose console had no client configured would have shown "anonymous" on both planes. That is a false statement about the server's security.

## Decision

1. **The bundle is in the image.**
   - **Where:** at `/app/webui`, read-only and owned by root, as the binary is.
   - **What:** the zip's contents unchanged, `appsettings.json` and its compressed copies included.
   - **Release:**
     - `publish.yml`'s `image` job needs the `webui` job;
     - it unpacks the zip that job attested, from the same run, into the build context;
     - it never downloads anything from a release.
   - **Pull request:** `ci.yml`'s `container` job takes the bundle the `wasm` job published and budgeted.
   - **Built once:** the bundle does not depend on the architecture, so it is built once, on x64, and both platforms' images take the same files.
   - **The zip stays** a release asset, with its own SBOM and attestations, for anyone who hosts the Web UI elsewhere.
2. **On by default in the image, and unchanged outside it.**
   - **The image sets** `VARVE__WEBUI__PATH=/app/webui` and `VARVE__WEBUI__SETTINGS=Generated`, so `docker run ghcr.io/hafeok/varve:<version> serve --anonymous` and a browser at `/ui/` is the whole first run.
   - **`Varve:WebUi:Enabled` is the off switch.** It is `true` by default. When it is `false`, the server adds no `/ui/` at all, whatever the path, exactly as when the path is unset. Setting the path to nothing is not documented as a way to turn the console off.
   - **Outside the image nothing changes:** with `Varve:WebUi:Path` unset there is no `/ui/`.
3. **Where `/ui/appsettings.json` comes from: `Varve:WebUi:Settings`.**
   - **The two values:**
     - **`File`** is the default. It serves the bundle's own file as edited, which was the behaviour before this ADR, including for a bundle an operator unpacked and pointed `Varve:WebUi:Path` at.
     - **`Generated`** makes the server answer `/ui/appsettings.json` from its own configuration. The bundle's `appsettings.json`, `appsettings.json.br` and `appsettings.json.gz` are then never served: the first is the server's answer, and the other two are `404`.
   - **This is the exact rule. There is no detection.** Nothing looks at whether the bundle has a file. The image sets `Generated`.
   - **What is generated.** The document is built field by field from a closed record. It never copies a configuration section.

     | Field | From |
     |---|---|
     | `Varve:Server` | `Varve:WebUi:Server`; empty by default, which means the origin that served the Web UI |
     | `Varve:Auth:Mode` | `Anonymous` on an anonymous server. `Oidc` on an OIDC server when the Web UI's client is configured. Otherwise **`NotConfigured`** |
     | `Authority` | the server's own `Varve:Auth:Authority`, always: the console signs in with the issuer the server checks, whose origin is the one `connect-src` names (ADR 0202). Not overridable |
     | `RoleClaimType` | the server's own `Varve:Auth:RoleClaimType` |
     | `ClientId`, `DataScope`, `ManageScope`, `ManageGrant`, `DataAudience`, `ManageAudience` | `Varve:WebUi:Auth:*`; the last three only when set |
     | `Missing` (only with `NotConfigured`) | the settings to add: `Varve:WebUi:Auth:ClientId`, `…:DataScope`, `…:ManageScope` |

   - **A server with OIDC and no Web UI client never says `Anonymous`.** It says `NotConfigured`, and names the settings.
   - **Nothing secret is ever in the answer.** The Web UI's client is public (ADR 0201), and has no secret to carry; `Varve:WebUi:Auth:ClientSecret` is an unknown key, refused at start (ADR 0115). A test plants a unique value in every string setting the server has, and fails if any value outside the allow-listed fields appears in the answer. That covers settings added later too.
   - **The answer:**
     - it is `application/json` and `no-cache`, with an `ETag` that is a digest of its bytes;
     - it carries ADR 0202's four headers;
     - it is computed once, at start, because the configuration does not change while the server runs.
   - **Refused at start, naming the setting** (exit 2, like every bad setting):
     - `Varve:WebUi:Server` or any `Varve:WebUi:Auth:*` set while `Settings` is `File`, since it would do nothing;
     - `Settings` other than `File` or `Generated`;
     - a client half set, meaning any `Varve:WebUi:Auth:*` without all three of `ClientId`, `DataScope` and `ManageScope`;
     - a `ManageGrant` other than `refresh` or `exchange`;
     - any `Varve:WebUi:Auth:*` on an anonymous server;
     - a `Varve:WebUi:Server` that is not an absolute http or https URL.

     An OIDC server with no `Varve:WebUi:Auth:*` at all starts: the API works without the console. Its console says `NotConfigured`.
   - **Shaped for a second address.** The document is `Varve` holding `Server` and `Auth`. The management plane's address that #297 adds is one more field, beside `Server`.
4. **The console's modes** (ADR 0201 point 5, amended):
   - only the exact `Anonymous` is anonymous, and only `Oidc` signs in;
   - **`NotConfigured`, or a missing or unknown mode,** shows "Sign-in is not configured" in place of every screen:
     - it names the settings from `Missing`, or says the mode is unknown;
     - the bar shows both planes "not configured", not anonymous;
     - nothing reads the server.

   The screen is composed of existing components (`ViewHead`, `Notice`), and its picture is `docs/ui/screens/signin--notconfigured.html`.
5. **The Content Security Policy is ADR 0202's, unchanged.** The generated settings are data the runtime fetches, not script: there is no inline script and nothing is relaxed. The issuer's origin in `connect-src` still comes from `Varve:Auth:Authority`.
6. **The image's SBOM holds the bundle** (ADR 0224, 0225):
   - **What goes in:** `eng/sbom.cs` puts the Web UI's asset document into the image's document once, not per platform. That includes the Mono `browser-wasm` runtime pack, Emscripten's packs and `Microsoft.AspNetCore.App.Internal.Assets`.
   - **How it is attached:**
     - the packages are prefixed `SPDXRef-webui-`;
     - the asset document's `DESCRIBES` becomes the image's root `CONTAINS` the bundle's root;
     - Emscripten's compiler and Node.js stay `BUILD_TOOL_OF` the bundle.
   - **The check:** `ci.yml` fails a proposed release whose image SBOM lacks any package of the zip's.
   - **The workload packs:** `publish.yml`'s check that the packs the build used are the ones the SBOM names stays in the `webui` job, where the bundle is built. The image takes that job's zip.
7. **The `Varve.Server` .NET tool is out of scope;** it is #325, linked from #129, which also leaves open whether the tool serves `/ui/` by default. Carrying the bundle would cost the tool:
   - **+6.40 MB** to the `.nupkg` (16.5 to 22.9 MB) and **+10.1 MB** installed;
   - nothing in the pack job's toolchain: it can take the `webui` job's attested zip, as the image job does, and needs no wasm workloads;
   - the bundle's 45 packages in the tool's SBOM;
   - a default path relative to the tool's directory instead of a fixed one.
8. **A separate management listener is not supported by the console**, in any of its ways of hosting, until #297:
   - the console has one address for both planes;
   - `/manage` on the data-plane port answers `404` (ADR 0163 point 1).

   `docs/operator/web-ui.md` no longer says that setup works.

## Size and digest

Measured on linux-x64 with `docker save`: the same AOT binary built into `main`'s Dockerfile and into this one, and the bundle as `dotnet publish src/Varve.WebUi -c Release` writes it (153 files, 10,069,570 bytes; zipped, 6,387,784 bytes).

| | compressed | unpacked |
|---|---|---|
| before | 20,094,976 B (20.1 MB) | 64.8 MB |
| after | 26,533,376 B (26.5 MB) | 81.6 MB |
| the bundle's layer | **+6.44 MB (+32 %)** | +10.4 MB |

- **The digest:** every image digest changes, because there is one more layer.
- **The bundle's layer in the release** is a function of the zip: `zip -X` records DOS times, which unzip restores, on a UTC runner, and both platforms unpack the same zip. In `ci.yml`'s `container` job the bundle comes from a workflow artifact, so its files carry the download's times, and that layer is not reproducible.
- **The bundle's modes are set in the `layout` stage** by GNU `chmod -R u=rwX,go=rX`, then copied across. They are never left to `COPY --chmod`: one builder did not apply a symbolic `X` to directories, and the image it built could read no file below `/app/webui` (#308's first run).
- **Reproducibility is no worse than before.** The binary's layer already records its publish time, so the image as a whole is still not byte-reproducible. Making it so (`SOURCE_DATE_EPOCH` with `rewrite-timestamp`) is #326, outside the 1.0 list. Equal times would still satisfy ADR 0202's rule that a sibling older than its file is never sent, because that check is `>=`.
- **CI time:** the time `ci.yml`'s `container` job now spends waiting for `wasm` is recorded on #296 from the pull request's first run.

## Alternatives considered

- **Generate the settings when the bundle has no `appsettings.json`, and ship the image's bundle without it.**
  - For: no third setting.
  - Against: the image's bundle would then differ from the attested zip, and a rule read off a file's absence is invisible in the configuration.
  - Refused by the maintainer on the plan.
- **Turn the console off by setting `Varve:WebUi:Path` to nothing.**
  - For: it works without a new key.
  - Against: a variable set to nothing is hard to document, and easy to lose in a compose file.
  - Not documented, by the maintainer's answer.
- **Copy the configuration's `Varve:WebUi` section into the answer.**
  - For: shorter code.
  - Against: any key added there later, or bound by mistake, would reach the browser. The allow-list makes a leak impossible to write by accident, and the test holds it.
- **Let `Varve:WebUi:Auth:Authority` override the server's authority.**
  - For: flexibility.
  - Against: the console would sign in with an issuer the server does not check and the CSP does not admit.
- **Drop the `.gz` copies in the image.**
  - For: about 2 MB unpacked, since every browser with WebAssembly accepts brotli.
  - Against: the image's bundle would no longer be the zip's contents.
- **A static image of its own, or the bundle in the .NET tool.** Out of scope; the tool's cost is in point 7.

## Consequences

- **New settings** in `docs/operator/configure.md`, all in `SettingsCheck.KnownKeys`:
  - `Varve:WebUi:Enabled`;
  - `Varve:WebUi:Settings`;
  - `Varve:WebUi:Server`;
  - `Varve:WebUi:Auth:ClientId`, `DataScope`, `ManageScope`, `ManageGrant`, `DataAudience` and `ManageAudience`.
- **The guide**, `docs/operator/web-ui.md`, puts the image first, then the two earlier ways. The README's quick start runs the image and opens `/ui/`.
- **Tests:**
  - **`WebUiGeneratedSettingsTests`:**
    - the off switch;
    - `File` as before;
    - `Generated` anonymous, OIDC with a client, and OIDC without one;
    - the siblings never served;
    - the refusals;
    - the planted-values test.
  - **`SignInNotConfiguredTests`** (bUnit): the modes, and the shell with nothing read.
  - **The browser tests:** the not-configured console in-process, and `WebUiImageTests` against the running image.
  - **`ci.yml`'s `container` job** checks the image's:
    - headers and generated settings in each mode;
    - off switch;
    - console in Chromium.
- **Checks:**
  - **Against the accepted ADRs:**
    - 0111: the image gains a layer and two variables, and is still non-root, read-only and chiseled;
    - 0115: settings are validated at start, and unknown keys are refused;
    - 0132: CORS is unchanged;
    - 0163: the management listener, #297;
    - 0200, 0201, 0202 and 0206: the screen is composed of existing cases;
    - 0224 and 0225.
    - **Conflict:** only with 0202 point 6, which this supersedes in part.
  - **Layer ownership:** `Varve.Server` and `Varve.WebUi`, both layer 6.
  - **Analyzer rule:** none new.
