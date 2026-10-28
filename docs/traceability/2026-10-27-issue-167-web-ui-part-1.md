# Web UI, part 1: the operator console

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer. This record grows slice by
> slice until the pull request.

| | |
|---|---|
| **Issue** | [#167](https://github.com/Hafeok/Varve/issues/167), part 1 of [#96](https://github.com/Hafeok/Varve/issues/96), opened by this session; [#168](https://github.com/Hafeok/Varve/issues/168) filed by it |
| **Date** | 2026-10-27 |
| **Tool** | Claude Code 2.1.311, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYNvBGThZuSuStrnN2Z` |
| **Branch** | `claude/happy-heisenberg-v8vg11`, from `main` at 6cdb402 |

## The prompts

### The session prompt

The session was given a zip of three files. Its `BOOTSTRAP.md`:

> Read `varve-claude-code-web-ui-console.md` in this zip: it is your session prompt for milestone Web UI part 1, the operator console, under issue #96 in Hafeok/Varve. `varve-browser-size.md` holds the browser size measurements it cites. Work on current `main` and follow `AGENTS.md` in full.
>
> Plan first. Post the plan on #96, answering every plan question, and wait for approval before writing any code. File part 1's own issue with the plan. Your ADRs start at 0200.

and its session prompt, `varve-claude-code-web-ui-console.md`, which the
message also carried as text:

> # Session: Web UI, part 1 — the operator console (#96)
>
> The server can be operated only through the API and the CLI. #96 adds a Blazor WebAssembly client of the public API, with **no endpoints of its own**. Part 1 is the operator console. Part 2, the data explorer, comes after Inference (#95), because it shows explicit against implicit and entailment provenance. The Web UI is in 1.0 (#15).
>
> **Starting point.** Current `main`. The console reads what these serve:
>
> - **The Management plane** (ADRs 0163–0168): `/manage/datasets`, `/manage/fleet`, `/manage/system`, `/manage/graphs`, `/manage/sparql` and `/manage/apply`, with the management audience `Varve:Auth:Management:Audience`. A token that names both planes' audiences is refused (ADR 0163 point 1), so **the console holds two tokens.**
> - **The data plane:**
>   - health (`/health/live`, `/health/ready`), with lag per binding (ADR 0174);
>   - the commits feed and diff (spec §8, filtered by grants, ADR 0107);
>   - the service description;
>   - `Varve-Position` and `Varve-Request-Id` on every response.
> - **Problems** (ADR 0119 and `docs/problems/`), the writer record and epoch (ADR 0136), and the follower role (ADR 0164).
> - **CORS** (ADR 0132), with no credentials mode and a token in `Authorization`.
> - **`Varve.Protocol.Client`** (layer 5): the typed client any .NET caller uses.
> - **The browser host** (ADR 0084) and `tests/Varve.WasmSmoke`. Note the smoke app's stated reason for *not* using Blazor: it would drag in an ASP.NET Core package tail unrelated to the claim that Varve runs in a browser. The console is a different claim, an application, but the ADR that admits Blazor answers that comment with numbers.
>
> **In flight:**
> - **Replication part 1 (#97)** adds the warm flag, takeover through `PUT /manage/datasets/{d}/state`, and the problems `writer-superseded` and `writer-stepped-down`.
> - **Inference (#95)** adds TBox bindings and `inference-behind`.
>
> Build against what's on `main`. Show whatever those add later through the same generic resource and problem views, and wire it when it lands.
>
> **Read before planning:**
> - #96 with its amendment, and #15's Web UI line;
> - ADRs 0037, 0099 and 0100 (authentication and its three test layers), 0107, 0119, 0132, 0163–0168, 0084 and 0060 (hosts at layer 6);
> - `docs/operator/` (what an operator does today, through the CLI);
> - the project doc `varve-browser-size.md`, if the maintainer attaches it, or its numbers here: the Varve WASM runtime floor is about 0.85 MB brotli, and the store variant 1.21 MB brotli, with a recommendation for a size budget in CI.
>
> `AGENTS.md` applies in full. **Plan first, and wait for approval.** Post the plan on #96. Your ADRs start at **0200**: Replication holds 0179–0189 and Inference 0190–0199. Renumber at landing if a range overflows.
>
> ## A. Decisions (Accepted unless stated; each becomes an ADR or a dated amendment)
>
> 1. **What it is.**
>    - **`Varve.Console`,** a host at layer 6, a composition root, never a library. It's published as static files. No `src/` package references Blazor.
>    - **It reaches the server through `Varve.Protocol.Client` only.** If a screen needs something the client doesn't expose, the client gains it as public API, in the baseline diff. **Every screen reads a resource the API already serves.** A screen that would need a new route is out of scope, and goes to an issue.
>    - **Blazor WebAssembly, by ADR** under constraint 4. The packages, and why Blazor rather than `wasm-experimental` with hand-written DOM interop (the WasmSmoke route) or a non-.NET front end (which would break constraint 1's spirit for the one UI Varve ships). The ADR answers WasmSmoke's comment with the measured size of both routes for the console's first screen.
>
> 2. **Authentication: OIDC authorisation code with PKCE,** the same flow as any public client.
>    - **No client secret,** no implicit flow, and no token in local storage beyond what the library's session storage holds. State where tokens live, and for how long.
>    - **Two access tokens,** one per plane's audience, each requested for its own scope. Show how Entra issues them (one resource per token) and how a generic issuer does. The end-to-end tests sign in through ADR 0100's standards mock (`mock-oauth2-server`, layer b), which supports the authorisation code flow with PKCE; Entra is the optional `main`-restricted leg.
>    - **The library:** `Microsoft.AspNetCore.Components.WebAssembly.Authentication` with the generic OIDC provider, not MSAL, matching ADR 0037's refusal of `Microsoft.Identity.Web` in the server. Its admission needs an ADR. If it can't hold two audiences cleanly, say so and propose the alternative.
>    - **Anonymous mode** (`Auth:Mode` `Anonymous`) works with no sign-in.
>    - **A `401`** starts sign-in. **A `403`** shows the problem, never a blank screen.
>
> 3. **Hosting.** Propose one, with its CSP. My lean:
>    - **the server optionally serves the console** from a configured directory, under `/console/`, default off (`Varve:Console:Path`). Same origin, so no CORS. A strict `Content-Security-Policy` with `script-src 'self' 'wasm-unsafe-eval'` and no inline script. Say what Blazor's boot needs;
>    - **or separately**, from any static host, with the server's CORS origins (ADR 0132). The operator guide documents both.
>    - **The bundle is a release asset,** with its digest in the descriptor's artefacts, and optionally a small static image. **It isn't baked into the server image:** the image stays what it is for operators who don't want a UI.
>
> 4. **Part 1's screens:**
>    - **Datasets:** state, settings, writer epoch and role, placement, follower lag, and the warm flag once Replication lands;
>    - **Declarations:** grants, agents, roles, groups, graph templates, and shapes and TBox bindings, rendered from `/manage/`. **Read-only,** with the Turtle source and the management position;
>    - **Health:** readiness with its reasons, and per-binding lag;
>    - **Fleet:** instances, roles and placement;
>    - **Commits and feeds:** the commit list with metadata and attachments (management position, validation digest), a commit's delta, and a live tail of the feed;
>    - **Problems:** every problem body rendered from its type, with a link to its `docs/problems/` page, and the request id.
>
> 5. **What the console may change.** A small set of operational acts, each mapping to an existing route, each needing a confirmation that names the dataset and the effect, and each showing the response's problem or position.
>    - The candidates are a dataset state change (`PUT /manage/datasets/{d}/state`), and takeover once Replication lands. Propose the list.
>    - **Declarations stay read-only in part 1.** `/manage/apply` stays the CLI's and Git's. A console that edits declarations is a later decision, because a declaration in Git is the audit trail.
>
> 6. **Position and time are first class.** Every view shows the `Varve-Position` it was read at. A dataset view can be pinned to an as-of position (`Varve-As-Of`) and says so in its header. A view never mixes positions silently.
>
> 7. **Accessibility:** WCAG 2.2 AA. Keyboard-only navigation, visible focus, no information carried by colour alone, and light and dark themes. Checked in CI by an automated audit on every screen. Propose the tool: axe-core run through the browser tests is the usual one, and needs a test-dependency ADR. The audit never replaces a keyboard walk-through, which goes in the traceability record.
>
> 8. **Size, AOT and trimming.**
>    - Published trimmed, with no trim or AOT warning.
>    - Measure the console with and without WebAssembly AOT compilation (download size, against first-screen time), and choose by the numbers.
>    - **A size budget gate in CI:** the brotli size of the published bundle, at the measured size plus a stated margin. Raising it is an ADR amendment. This is the gate the browser size measurements recommended, and WasmSmoke's store variant gets the same gate in the same job.
>
> 9. **Tests.**
>    - Component tests (propose the library, bUnit or another, by ADR) for rendering from fixtures of every resource and problem type the console reads.
>    - **End-to-end tests** in a real browser against a real `Varve.Server` started by the test, signed in through the test issuer, over every screen and every act.
>    - Playwright needs its browsers in CI: either a row in `.github/ci-images.yaml` for the Playwright image, mirrored, or the browsers installed in a job from a mirrored source. **No Docker Hub pulls in CI.**
>    - **A contract check:** each fixture is captured from a running server in the test run, not hand-written, so a server change that breaks the console fails the console's tests.
>
> ## B. Definition of done
>
> - Every screen of point 4, and every act of point 5, end to end against a real server, in OIDC and anonymous modes, with a `403` and a problem rendered.
> - Both tokens are acquired and each plane is called with its own. A test shows a dual-audience token refused by the server, and the console never requests one.
> - The accessibility audit is clean on every screen, with the keyboard walk-through recorded.
> - The size budget gate in CI, with the measured numbers and the AOT choice in the ADR.
> - Trimmed with no warnings. No `src/` package gains a Blazor reference, and the layer rule shows it.
> - The hosting variant(s) of point 3, documented in `docs/operator/console.md` with the CSP.
> - The public API baseline diff of `Varve.Protocol.Client` in the PR body.
> - The close-out for part 1: README Status, `docs/roadmap.md` (Web UI part 1), AGENTS.md's State, `eng/status.cs`, the changelog, and the traceability record. #96 stays open for part 2.
>
> ## Plan questions
>
> 1. The ADR for Blazor (point 1), with both routes' measured sizes.
> 2. Authentication: the library, two audiences, token storage, and the test issuer.
> 3. Hosting, and the CSP.
> 4. The screens, and the resource each one reads, as a table.
> 5. The acts, and their confirmations.
> 6. Accessibility tooling.
> 7. Size: AOT or not, the budget, and its margin.
> 8. Test libraries and the Playwright browsers in CI, under the image rule.
> 9. ADR numbers from 0200, and amendments: 0132 if the console needs a header added, and 0060 if a console host needs anything new there.
>
> ## How this lands
>
> - **One PR,** red only on `CS0618`, with its own issue for part 1 (as #120 was for SHACL part 1) and a descriptor at the next free `0.1.0-preview.N` (ADR 0102).
> - **The descriptor** carries `accepted-by: mailto:emil@okkels-klein.dk` and a `Closes` trailer for part 1's issue. #96 closes with part 2. Use bare #N elsewhere, and no closing keyword in PR descriptions (#108).
> - **Commits:**
>   - signed with the agent SSH key, as `Claude (Emil) <claude+emil@okkels-klein.dk>`;
>   - reference #96, with the DCO sign-off;
>   - never name a model.
> - **A `docs/traceability/` record** (ADR 0033).
> - **No suppression or downgrade of DD or VARVE rules.**
> - **CI images** only from `.github/ci-images.yaml`. A new image needs a row, a mirror run, and the maintainer making its package private.
> - **Report the browser jobs' durations per OS** (#126), and give them their own named job.
> - **Land through `land/`.**

The zip's `varve-browser-size.md` held the browser size measurements the
prompt cites.

### The plan's approval

The plan was posted on #96. The maintainer's answer:

> Approved, with these answers and changes.
> Name: `Varve.WebUi`, not `Varve.Console`. Part 2’s data explorer is the same app, so “console” names only half of it, and the change also removes the clash with `System.Console` (inside `Varve.*` namespaces, `Console` would bind to the Varve namespace first). It is served under `/ui/`, configured by `Varve:WebUi:Path`, and the package scope, ADR titles and guide page (`docs/operator/web-ui.md`) follow. #167 keeps its title (“operator console”) because it describes part 1’s screens.
> D1: your own client. The evidence from `AuthenticationService.ts` settles it. One change and four additions:
>
> * Drop the `prompt=none` second-redirect fallback for the management token. A top-level redirect reloads the app and loses the data-plane token held in memory. An iframe would depend on third-party cookies, which browsers block, and would need `frame-src`. So the refresh-token grant for the second scope is required. An issuer that can’t redeem a refresh token for another scope is unsupported for the console in OIDC mode, and `docs/operator/web-ui.md` says so, with the per-issuer table you gave. `prompt=none` stays for a reload: one top-level redirect for the data plane, then the refresh grant for management.
> * The ADR records ID-token handling: the signature is not verified in the browser (no RSA on browser WASM), relying on OIDC Core 1.0 §3.1.3.7 since the ID token comes directly from the token endpoint over TLS. `nonce`, `iss`, `aud` and `exp` are still checked. The console makes no authorisation decision from the ID token; the server validates every access token.
> * Add RFC 7636 Appendix B’s test vector for S256, and a test that the verifier and `state` come from `RandomNumberGenerator` with the stated lengths.
> * Property tests for the callback: replayed `state`, mismatched `state` and mismatched `nonce` are all rejected.
> * The client stays internal to the Web UI. None of it becomes public API in `Varve.Protocol.Client`.
>
> D2: agreed. The zip’s digest goes in the attestation now and in the descriptor when #119 defines the field. No static image in part 1.
> D3: documented only. A test user without MFA means a stored password and a weakened tenant, which goes against the no-keys rule. The Zitadel leg is the real-provider browser path. The Entra page documents the SPA registration, the two delegated scopes and the multi-resource refresh. Record one manual sign-in against Entra in the traceability record, if one is done.
> D4: close, open and checkpoint. Close and open carry the drift note. Typing the dataset’s name is required for close only.
> D5: Playwright on the runner’s Chrome. ADR 0204 must state that `Microsoft.Playwright` ships a Node.js driver as native binaries in the test closure. Confirm that `eng/native-assets.cs` scopes to shipped artefacts only, and that no `src/` project’s closure contains the driver. If the gate can’t make that distinction, stop and say so before working around it.
> D6: gaps 1 and 3 are closed by Replication part 1.
>
> * Its plan adds `epoch`, `role`, `warm` and the epoch history to `GET …/state`, and `position` per dataset to readiness. Render them through the generic views, and wire them when #97 part 1 lands. If it lands first, merge `main` and wire them in this PR.
> * File gap 2 only, framed as “fleet instances as observed”: the writer record and the reader records per dataset (ADR 0162’s readers command), served by a management route. It is not declared membership, which ADR 0163 rejected.
>
> Visual design is being done separately and lands as `docs/ui/`: CSS custom-property tokens, screen references for every screen and state, and an ADR stating that the Web UI implements from `docs/ui/` with no component library or CSS framework. Until it lands:
>
> * Use unstyled semantic markup only: no colours, type, spacing or icons.
> * Where a screen needs a visual decision (lag, failed state, lease loss, follower role, 412 on settings, the drift note), leave a marked placeholder and list it here.
> * Everything non-visual goes ahead.
> * The final PR waits for `docs/ui/` on `main`.
>
> Also:
>
> * ADR 0203: check `VarveResult<T>` against ADR 0118 and against how `SparqlHttpClient` reports a problem today. If one client throws and the other returns a value, say which convention you’re aligning to, and why.
> * mock-oauth2-server CORS: as you planned, verify it in the first slice and report before any workaround. A workaround must not be a proxy that the shipped server gains.
> * ADR numbers 0200–0205 stand. The ADR 0132 amendment for the headers is right. The `docs/ui/` ADR takes the next free number after 0205, or comes from the design side.
>
> Start with slice 1.

## The report

### Before code: what the plan rested on, and what was verified

- **The authentication library holds one token.** In
  `Microsoft.AspNetCore.Components.WebAssembly.Authentication`'s generic OIDC
  provider (`AuthenticationService.ts`, release/10.0,
  `OidcAuthorizeService.getAccessToken`, lines 152–185), a request for scopes
  the stored user lacks calls `signinSilent` and replaces the stored user. The
  maintainer took the Web UI's own client (D1).
- **mock-oauth2-server answers CORS.** Run at the pinned digest
  (`ghcr.io/navikt/mock-oauth2-server@sha256:47374fe0…`, the upstream image
  the mirror copies) with the committed configuration:
  - a preflight to `/varve/token` from `Origin: http://localhost:5000` is
    `204` with `access-control-allow-origin` echoing the origin;
  - the `POST` and the discovery document carry it too.

  No workaround is needed.
- **A refresh token redeems a token for the management audience.** Against
  the same mock:
  - an authorisation code with PKCE `S256` (`scope=openid offline_access …`)
    gave a refresh token;
  - the `refresh_token` grant with `scope=api://varve-manage` gave an access
    token whose only `aud` is `api://varve-manage`, through the existing
    scope mapping.

  The fixture will gain mappings for the Web UI's client.
- **The native-asset gate scopes to shipped artefacts (D5).**
  `eng/native-assets.cs` walks only the members of `Varve.slnx` that are
  packable. Test projects are not, so Playwright's Node.js driver in a test
  closure is outside it, and the register's scopes keep it out of `src/`.

  The Web UI is shipped but not packable, which the gate's definition missed.
  The gate now also walks a member that declares
  `<VarveShipped>true</VarveShipped>`, and its test asserts the Web UI is
  walked.
- **Gap 2 filed** as #168, "fleet instances as observed".

### Slice 1: the Web UI's first screen, built both ways, measured (ADR 0200)

**What was built:**
- **`src/Varve.WebUi`**, a Blazor WebAssembly host at layer 6:
  - unstyled semantic markup;
  - focus moved to each page's `h1`;
  - no inline script or style;
  - an `appsettings.json` naming the server for a static host.
- **The Health screen**, reading `/health/ready` through `Varve.Protocol.Client`.
  It shows:
  - the response's position facts;
  - a problem in the screen's place;
  - an answer that is neither as such;
  - members the client does not know, by name.
- **`Varve.Protocol.Client` gains** (ADR 0203, which also answers the
  convention question):
  - `VarveHealthClient`;
  - `Problem`, `ProblemType`, `ResponseFacts` and `VarveResult<T>`;
  - the readiness model and its wrappers.

  The sending under the timeout is now one internal helper.

**Measured.** In headless Chromium, under the strict CSP, against a real
server:

| | brotli | first screen, median of 10 |
|---|---:|---:|
| `wasm-experimental`, DOM by hand | 1.048 MB | 428 ms |
| Blazor | 1.675 MB | 770 ms |

**Found on the way:**
- **`TrimMode=full`** removed the app's own components (`CtorNotLocated`);
  Blazor's partial mode is the same size.
- **An incremental publish** kept a stale trim twice.
- **The plan's preliminary 1.525 MB "empty Blazor"** was a build that had
  trimmed its components away and never ran. ADR 0200 says so.
- **Blazor reads `appsettings.json`** only if it was in `wwwroot` at build
  time, so the bundle ships one.

**Tests:**
- `HealthClientTests` (4), against the server's own composition root.
- `Varve.WebUi.Tests` (8): Blazor in no `src/` closure but the Web UI's, the
  server address, and the problem page link.

**Placeholders for `docs/ui/`** (`data-ui-pending`), at slice 1's end:
- `readiness-state`;
- `role`;
- `lag`;
- `bootstrap`;
- `dataset-state`.

All five were settled by the components, below.

### The design source, and the components built against it

**The prompt:**

> Before the next slice: open a docs-only PR from the attached varve-docs-ui-0.1.zip, unpacked at the repo root, under #167. Number the ADR as the next free after your range (0206 unless taken) and fix its references and the decision set’s adr: to match. In the traceability record, name the source as the Varve design system artifact at version 1793092562-f3b6. Leave the decision set unaccepted; I accept on the PR. No code in that PR. The check that regenerates tokens.css from tokens.json, the CSS linking into wwwroot, the level rule and the contract test are ADR 0206’s points 2, 4 and 6 and belong in the milestone PR. Then build the components in the Atoms, Molecules, Organisms, Templates and Pages namespaces against docs/ui/, and keep the visual-decision placeholders only for what the screens will settle.

and, while the pull request was being opened:

> 0206 is confirmed as this ADR’s number. Inference’s planned 0206 has moved to 0212 (#171). Don’t renumber even if a branch’s ADR index still shows 0206 for Inference.

**The docs-only pull request** is #174, from
`claude/happy-heisenberg-v8vg11-docs-ui` off `main`. It carries its own record,
`2026-10-27-issue-167-web-ui-design-source.md`, naming the source: the Varve
design system artifact at version 1793092562-f3b6. That record lists the
references fixed in ADR 0206 and what the brand book says that the files do
not match. The pull request was merged into this branch, with `main`.

**The components**, one namespace per level, with markup taken from each
component's `preview.html`:
- **Atoms:** `Button`, `Field`, `StateBadge`, `PositionStamp`, `TermChip` and
  `Tail`.
- **Molecules:** `KeyValue`, `LagMeter`, `Notice`, `ViewHead`, `Triple`,
  `AsOfBanner` and `CopyButton`.
- **Organisms:** `ResourceTable`, `CommitList`, `ProblemPanel`, `SourceBlock`,
  `ConfirmDialog`, `Panel` and `TripleList`.
- **Templates:** `Shell`.
- **Pages:** Health, the layout, and `ProblemView` and `ReadStamps`.

Health is rebuilt on them. `Tones` is the one state-to-tone mapping (point 5).
`tokens.css`, `bundle.css` and the core mark are linked into `wwwroot` by
`<Content Link>`; the published `bundle.css` is byte-identical to
`docs/ui/bundle.css`. `js/webui.js`, an external file, sets `data-theme` before
the first paint and holds `copy`, `showModal` and `close`.

**Where the component specs conflict with the level rule:**
- **`ConfirmDialog` composing `ProblemPanel`.** Both are organisms, so the page
  passes the result in.
- **`ResourceTable` showing the problem.** The page renders the problem in the
  table's place.
- **`CopyButton`** composes `Button`, so it is a molecule.
- **The drift note** is a `Notice` switch, so that no molecule uses another.

**Measured:**
- The clean publish is **1.709 MB** brotli, 34 KB above slice 1, for every
  component and both stylesheets.
- In Chromium under the strict CSP, Health renders in light, dark and at 360 px
  with no CSP violation, no console error and no sideways page scroll.

**Found in `docs/ui`, reported rather than patched**, since it is the single
source of the CSS:
- `bundle.css` resets neither the body's margin nor its height, so the shell
  sits 8 px in from the window and does not fill it.
- A heading focused on navigation shows the focus ring.

**Seen on the way.** The `DD` rules reported nothing on the components' public
`string` parameters, which suggests analyzers do not see the Razor-generated
classes. That is the condition ADR 0206's Checks says to stop at before
substituting a test for the level rule. It is to be confirmed when the rule
is built.

**Tests.** `ToneMappingTests` checks:
- every type in `ProblemCatalogue`;
- every page in `docs/problems/`;
- every state word the client's wrappers and the brand book's tone table
  document.

Each has a tone. The Web UI's tests: 20, all passing.

**Placeholders left** (`data-ui-pending`), only for what the screens will
settle:
- `unreachable-placement`: where the "server did not answer" notice sits
  against the view head.
- `lag-without-bound`: a lag shown as a number because readiness serves no
  bound for a `LagMeter`.

### The review round, and whether an analyzer sees the components

**The rulings** on #174 and the components, verbatim where they concern this
branch:

> Level rule: keep it strict. A component uses only lower levels, never its own level. […] The drift note as a switch on `Notice`: no. […] `Notice` stays generic. The drift note’s wording lives with the dialog’s caller: the page passes a `Notice` into `ConfirmDialog`’s slot, the same pattern as the problem. Put the text in one place in `Pages` or `Tones`, not per page.
>
> * `unreachable-placement`: directly below the view head, above everything else, with the last data still shown below it. The reference screens follow this.
> * `lag-without-bound`: the UI never shows data the API doesn’t serve. Show the lag as a number with the readiness badge (`ready` / `behind`). Use `LagMeter` only where the bound is served. File the gap: readiness should carry each binding’s bound. That’s a small API change, and the meter can be wired up once it lands.
>
> Analyzer flag: stop as agreed, and confirm first. One more point if it turns out generated code can be analysed: opting the level rule into generated code must not opt the DD contract rules in as well. Component parameters aren’t contracts, and primitive-free rules would flood the components. Scope the opt-in to the level rule only.

**Done in the code:**
- `ConfirmDialog` takes a Note slot and a Result slot, and `ResourceTable` a
  Problem slot, which the page fills.
- `Notice` is generic, and the drift note's wording is in `Pages/DriftNote`,
  once.
- The unreachable notice sits directly below the view head.
- A lag is a number beside its readiness badge.
- No `data-ui-pending` is left. The readiness gap is filed as #176.

**The probe.** A throwaway analyzer, never committed, was attached to
`Varve.WebUi` for one build and then removed. It is two analyzers, identical
but for `ConfigureGeneratedCodeAnalysis`, each reporting:
- the `Button` component's type symbol;
- every `OpenComponent<T>` invocation, with the type that makes it.

The results:
- **Default flags (`None`): nothing.** The Razor source generator's output
  (`obj/…/RazorSourceGenerator/Atoms/Button_razor.g.cs`) is generated code,
  and an analyzer that does not opt in neither analyses it nor reports in it.
  That is why the `DD` rules were silent on the components' parameters.
- **`Analyze | ReportDiagnostics`: 35 reports.** The `Button` type symbol, and
  34 `OpenComponent<T>` edges from each component class to each component it
  renders, `App`'s included.

  The flags are per `DiagnosticAnalyzer`, so the level rule can opt in alone,
  and the `DD` rules stay out as ruled.

**Checked against the strict rule, the 34 edges hold two violations:**
`Pages.Health` uses `Pages.ProblemView` and `Pages.ReadStamps`. `DriftNote`
will make a third when a page passes it into a dialog. These page-level
parts exist so that the client's types and Varve's wording are mapped once,
at the level that may know them. Whether pages may use them is put to the
maintainer before the rule is written.

### The Parts level, the level rule, the tokens check and the markup contract

**The ruling** on the two violations, verbatim in its rule:

> Neither 1 nor 2. Add a level: `Varve.WebUi.Parts`, between `Templates` and `Pages`.
>
> * Parts are domain-aware, non-routed components: `ProblemView`, `ReadStamps`, `DriftNote`, and the like later. They may use the client’s model types (a problem body, a position, a status) and `Tones`, and hold Varve’s wording.
> * Pages are routed screens only. They are the only level that injects or calls the client’s services, i.e. reads data. They may use Parts and everything below.
> * The rule stays uniform: a component uses only lower levels, never its own. Parts don’t use Parts, pages don’t use pages. No exemption anywhere. If two parts later genuinely need each other, that’s a design conversation, not an analyzer exception.
> * The analyzer has two checks:
>    1. the level order over `Atoms < Molecules < Organisms < Templates < Parts < Pages`;
>    2. client service types (anything that calls the server) referenced only in `Pages`; client model types allowed in `Parts` and `Pages`.
>  If the client doesn’t separate service types from model types cleanly enough for the analyzer to tell them apart, say so before inventing a marker.
>
> Then the VARVE rule (analyzer, a violating and a conforming sample per level and for the service-type check, doc page linking ADR 0206), the `tokens.css` regeneration check, and the contract test with bUnit.

**The paperwork** (ADR 0206 point 4, its decision set, and the brand book's
level table) is in #174, merged into this branch. `ProblemView`, `ReadStamps`,
`DriftNote` and `MainLayout` moved to `Varve.WebUi.Parts`.

**The client's split, as found.** No marker was invented:
- `Varve.Protocol.Client.Model` holds values only: problems, response facts,
  readiness and the wrappers.
- The root namespace and `.Hosting` hold the services. They also hold a few
  values that never call the server: `ClientLimits`, `EndpointPolicy`, the
  follow and setup options, and the client's exceptions. The rule counts the
  namespace, so these are service types to it, strictly. No part names one
  today.
  If one ever should, the move is the type to `.Model`, not an exception to
  the rule.

**VARVE0006** (`src/Varve.Analyzers/ComponentLevelAnalyzer.cs`,
`docs/rules/VARVE0006.md`). The generated-code opt-in is this analyzer's
alone. Its 29 tests:
- a conforming sample at every level (each one rendering the level below);
- a violating sample at every level (each one rendering its own level);
- a component rendering a higher level;
- a service and a model in Pages (clean) and a model in Parts (clean);
- a service in Parts (reported), and a model or a service below Parts
  (reported);
- the exclusions: a non-component type, the component itself, a cref, a
  `nameof`, a type outside the levels, and a compilation without Blazor.

On this branch the Web UI builds with no VARVE0006 report.

**`tokens.css`** is generated from `tokens.json` by
`tests/Varve.WebUi.Tests/TokensCss.cs`, the authority as ruled. The test
compares the result byte for byte with the committed file, and
`VARVE_WRITE_TOKENS_CSS=1` rewrites it.
- The sent file was already identical, so nothing was rewritten.
- A one-byte edit to the committed file failed the test.
- **Found:** the type-scale classes (`.page-title`, `.label` and the rest)
  have no `vw-` prefix, unlike every other class. `bundle.css` does not use
  them. The generator keeps them as sent. This is put to the maintainer, not
  changed.

**The markup contract** (ADR 0206 point 6; ADR 0204 for bUnit 2.11.3):
- 55 cases, one or more per preview state, across all 19 components, under
  `docs/ui/components/*/cases/`.
- `ComponentContractTests` renders each case with bUnit and compares it
  semantically.
- A second test fails any component with a preview but no cases or no
  component.
- Changing one class in `Button/cases/primary.html` failed exactly that case;
  restored, all 78 tests in the project pass.

**Where a case differs from the preview,** the component is right and the
preview is a picture:
- `ViewHead`'s `h1` carries `tabindex="-1"`, for focus after navigation.
- `Shell`:
  - the navigation's label is "Web UI";
  - the brand image is `img/varve-core.svg`, linked into `wwwroot`;
  - `main` carries `id="main"` and `tabindex="-1"`.
- `ConfirmDialog` renders without `open`. It is opened by `showModal()`
  through `js/webui.js`, which the preview's static `open` stands in for.
- `SourceBlock`'s `pre` is unhighlighted and carries `tabindex="0"`, so a
  keyboard can scroll it.
- `ResourceTable`'s caption is optional and visually hidden.
- `CopyButton`'s "Copied" state follows a click, not a parameter, so no case
  states it.

### Round 3: custom properties only, the model's convention, previews equal to the cases

The ruling is verbatim in the design source's record
(`2026-10-27-issue-167-web-ui-design-source.md`, round 3), with the docs
it changed in #174. Done on this branch:
- **The client's convention** is stated in `docs/rules/VARVE0006.md`:
  - `.Model` holds values only;
  - a type a part needs moves into `.Model`, with no exception.
- **The generator** writes custom properties only. Its unit test now also
  says a type style becomes no class. `tokens.css` was regenerated from it,
  and came here with the merge of #174.
- **The components:**
  - `Shell` starts with the skip link and names its navigation "Main".
  - `MainLayout`'s own skip link is removed. It was a second one, never
    shown on focus, and it navigated.
  - `SourceBlock`'s `<pre>` is a named region.
  - `Panel` takes `TitleId`. `ResourceTable` takes `LabelledBy` and refuses
    to render a table with neither a caption nor a label.
  - `CopyButton` has a live region, and reads "Copied" for two seconds, on
    the injected `TimeProvider`.
- **The skip link and the router.** Against the base address `/ui/`, a bare
  `href="#main"` resolves to `/ui/#main`. From any other screen that is a
  navigation. `js/webui.js` takes the click in the capture phase, before
  Blazor's router, and moves focus to `main`.

  Checked in Chromium on a Release publish, loading `/ui/health` directly:
  - the link is 1×1 until focused;
  - the first Tab focuses it at 161×38;
  - Enter leaves the URL at `/ui/health` and focuses `main#main`;
  - the page reported no errors.
- **Tests:** `Varve.WebUi.Tests` passes 138. The new ones:
  - every preview shows each case verbatim, and shows nothing that is not a
    case;
  - Copy, clicked, says "Copied" and announces it, then reverts once a manual
    clock moves two seconds;
  - a table with no name is refused.

  Changing one class in `Notice/preview.html` failed exactly that preview
  case.
- **Held:** VARVE0006's third check (a `.Model` type names no service
  type). Its analyzer and tests are written and pass. On this branch it
  reports one type: `BearerToken.Fixed`, from ADR 0127, returns a
  `BearerTokenProvider`. Fixing it changes public API that an accepted ADR
  names, so it was put to the maintainer before anything is committed.

**The held check, landed.** The maintainer chose option (a) for `BearerToken.Fixed`:
- the convenience moves, unchanged, to `BearerTokens.Fixed` in
  `Varve.Protocol.Client`, beside the delegate it returns;
- `BearerToken.Fixed` is removed, not aliased; it was never in a release;
- ADR 0127 gains a dated amendment, filed unaccepted, with the ruling
  `FixedProviderBesideTheDelegate`.

The CLI's `--token` and three tests call the new name. VARVE0006's third check
is committed, and `docs/rules/VARVE0006.md` describes it. With the move, the
solution builds with no report.

The tests:
- `Varve.Analyzers.Tests` passes 135, including the model's samples: clean,
  a service named, a value outside the model named, a service naming the
  model, and the message;
- `Varve.Protocol.Client.Tests` passes 117;
- the on-behalf-of tests pass.

**Sign-offs corrected.** Four commits on these branches carried the agent's
sign-off and a trailer naming a model. Their trailers were rewritten to the
responsible human's sign-off, with the files unchanged, and the branches were
force-pushed with the maintainer's permission.

### The reference screens, and the two components they added

**The ruling**, verbatim:

> Land `varve-docs-ui-screens.zip` (unpacked at the repo root) on the milestone branch, not as a separate PR. The screens use two components that `docs/ui/` doesn’t cover yet, and their cases need the code on this branch:
>
> * `Tail` (atom): add `docs/ui/components/Tail/` with README, preview and cases for live and paused.
> * `TripleList` (organism): add `docs/ui/components/TripleList/` with cases for a delta and an empty list. Its header row `vw-triples__head` should be pinned there.
>
> Also add to `bundle.css`: `.vw-table th.vw-num { font-family: var(--font-sans); font-size: 11px; }`. Numeric column headers currently render in mono, unlike every other header. No case changes; the markup is the same.
> When the OIDC sign-in slice lands, add a `Shell` case with the Sign out button. There’s still none, because `MainLayout` has no sign-in yet.
> Treat the screens as pictures, not a contract. Where a page you build and a screen disagree, check the page against the cases and tell me, and I’ll regenerate the screens.

**Done:**
- **The screens:** `docs/ui/screens/` is added as received: 31 pages, the
  README and `screens.css`. They carry no script, and they link only
  `../tokens.css`, `../bundle.css`, `../assets/varve-core.svg` and
  `screens.css`. Every class they use has a rule in `bundle.css` or
  `screens.css`.
- **`bundle.css`:** numeric column headers are set in the sans face at 11px,
  like every other header. No case changed.
- **`Tail`:**
  - Added to `docs/ui/components/` with its README and preview.
  - It has three cases: `live`, `paused` (with three new commits waiting),
    and `paused-nothing-new`.
  - **The paused wording changed.** The component said only "Paused". The
    screens, and `CommitList`'s README ("pauses … and says so"), say
    "Paused while you read · 3 new". `Tail` had no case yet, so it now takes
    `Pending` and says that, with the count only when commits are waiting.
- **`TripleList`:** added with its README, preview and two cases, `delta` and
  `empty`. Both pin the `vw-triples__head` row. The component's markup is
  unchanged.
- **Tests:** the contract cases and the previews grow from 56 to 61.
  `Varve.WebUi.Tests` passes 148. Every other case and preview regenerated
  byte-identical.
- **Owed:** a `Shell` case with Sign out, when the OIDC sign-in slice lands.

**The Health page against its screens.** I compared the ready, not-ready,
unreachable and loading screens' main column with `Pages/Health.razor`, by
structure. The composition agrees:
- view head and stamp;
- the unreachable notice directly below the head;
- the Readiness panel with its badge and Read again;
- the management panel's five rows;
- the Datasets table's six columns and its captions;
- the three loading rows.

One difference is not in the markup a case pins. The screens give every
panel title an id (`readiness-title-2`, …), and the page sets `TitleId` on
none of them. Only a table labelled by its panel needs one, and the page's
tables have captions.

### The remaining slices: the order

**The ruling** (2026-10-28), verbatim in its order and standing rule:

> Carry on with part 1’s remaining slices, in this order unless your plan on #96 already fixes a different one:
>
> 1. Sign-in (D1): […]
> 2. The read screens, each built against its reference screen in `docs/ui/screens/`: […]
> 3. The acts: checkpoint, close and open, through `ConfirmDialog`, with the drift note and the result slot. Close requires the typed name. The 409/412 paths show the problem inside the dialog with “Reload at head”.
> 4. Gates and close-out: […]
>
> Standing rule for the design, so `docs/ui/` stays the source:
>
> * New markup gets its case in `docs/ui/components/<Comp>/cases/` first, in the same commit as the component that renders it. No component renders markup that no case pins.
> * Where a screen needs something the cases and the reference screens don’t cover, don’t invent it: ask on #96, and wait or carry on with the next slice.
> * Where a page you build and its reference screen disagree, check the page against the cases and list the difference on #96. The screens get regenerated; they don’t hold back the page.
>
> Report at the end of each slice: what landed, test counts, and the bundle size against slice 1.

The plan on #96 fixes hosting and the CSP (ADR 0202) before sign-in, as its
slice 3 before slice 4: the sign-in redirect lands on `/ui/`, and the CSP
names the issuer's origin. So hosting came first.

### Hosting and the Content Security Policy (ADR 0202)

**Landed:**
- **`Varve:WebUi:Path`.** It names the published bundle. The server checks at
  start that the path is absolute and holds `index.html`, and refuses to
  start otherwise (exit 2).
- **`src/Varve.Server/WebUi.cs`.** It serves `/ui/` on the data-plane
  listener, before CORS, rate limits and authentication:
  - only `GET` and `HEAD`;
  - `308` from `/ui` to `/ui/`;
  - extensionless routes get `index.html`, and a missing file with an
    extension gets a `404` problem;
  - a `.br` or `.gz` sibling is sent only when accepted and not older than
    its file;
  - fingerprinted `_framework` files are immutable for a year, and every
    other file is `no-cache` with an `ETag` and `304`;
  - every response carries the policy, `nosniff`, `no-referrer` and
    `same-origin` opener.
- **ADR 0202 and its decision set** are filed unaccepted. The configure
  guide has a `WebUi` section.

**One refinement of the plan, stated in the ADR.** The plan said any
non-file path gets `index.html`. It is now only a path with no extension:
the runtime expecting a module and getting HTML fails far from the cause,
and a `404` problem names it.

**Tests:**
- `WebUiHostingTests` passes 10:
  - the headers and the exact policy;
  - the redirect;
  - deep links;
  - missing and dot files, and a climb out of the bundle;
  - brotli accepted and refused (`q=0`), and a stale sibling not served;
  - immutable against `no-cache`, and `304`;
  - `GET`/`HEAD` only;
  - unset, no `/ui/`;
  - the issuer's origin in `connect-src`;
  - a bad path refused at start.
- `Varve.Server.Tests` in full: 144, none failed, 14 skipped for missing
  services, as before.

**In a browser.** I published the Web UI, ran the Release server with
`Varve:WebUi:Path` set to it, and loaded `/ui/health` in Chromium:
- the Health screen rendered in 1.5 s;
- every framework file came brotli-encoded;
- no `securitypolicyviolation`, console error or page error.

**Bundle:** `_framework` is 1.703 MB brotli-11, against slice 1's 1.675 MB.
The 28 kB since then are the components; this slice changed no Web UI code.

### Sign-in (ADR 0201, D1)

**Landed:**
- **`src/Varve.WebUi/SignIn/`:** the Web UI's own public client.
  - `OidcOptions`, `OidcRandom` and `Pkce`.
  - `Discovery` and `IdToken`.
  - `PendingSignIn`, kept in session storage and taken out once.
  - `SignInSession`: the redirect, the callback's checks, one token per plane,
    renewal, and sign-out.
  - The browser seams: `SessionSignInStore` and `NavigationBrowser`.
- **`Pages/SignInCallback` (`/signin`) and `Pages/SignedOut` (`/signedout`).**
- **`MainLayout`:**
  - the real plane states;
  - Sign out in the bar;
  - a `prompt=none` sign-in on the first screen of a tab with no token.
- **`Shell`'s `sign-out` case,** with its preview figure and README line.
- **The mock fixture** maps `varve-webui`. **ADR 0201 and its decision set**
  are filed unaccepted.

**Zitadel, measured, and put to the maintainer on #96.** Zitadel v4.19.4 was
run locally:
- the pinned image, with Postgres from ghcr because Docker Hub and public ECR
  were rate-limited here;
- the seed, plus a `varve-manage` project and a user-agent app;
- login through the session and OIDC v2 APIs, as a custom login UI does.

What the access token's `aud` held:
- Both scopes at sign-in gave both projects, and a narrowing refresh left both.
- The data scope at sign-in, then a refresh for the management scope, gave
  `invalid_scope`.
- The app in its own project changed nothing: its project is always in `aud`.
- Token exchange (RFC 8693) with `audience` gave exactly one project, for each
  plane.

So the refresh grant cannot serve Zitadel. Options 1–3 are on #96; the leg is
out until ruled.

**mock-oauth2-server:**
- It answers CORS on its token endpoint again (`Access-Control-Allow-Origin`
  echoed for `http://127.0.0.1:5081`).
- It redeems the refresh token for the management scope alone (`aud`
  `api://varve-manage`).
- It refuses a replayed code (`invalid_grant`).
- **A finding the ID-token check made itself:** the mock puts a mapping's
  claims in the ID token too. The first run in the browser failed with "The
  ID token is not for this client", because `aud` was `api://varve`. The
  mapping now names both `api://varve` and `varve-webui`.

**In a browser, against the mock.** The Release server ran with
`Auth:Mode` `Oidc`, the mock as its authority, `varve.operator` as bootstrap
operator, and `WebUi:Path` set. The bundle's `appsettings.json` was edited
after publish, and the server served the edit, not its stale brotli.
Chromium, scripted:
1. **First visit to `/ui/health`.** The authorize request carried
   `prompt=none` and `scope=openid offline_access varve-webui-data`. After the
   mock's login form, the browser landed back on `/ui/health` with both planes
   "signed in". The token requests were `authorization_code`, then
   `refresh_token` with `varve-webui-manage`.
2. **Each plane's token on the management plane:**
   - the management token: `GET /manage/fleet` answered `200`;
   - the data token: `GET /manage/system` answered `401`.

   (With `alice` as bootstrap operator, my first configuration, it was `403`:
   bootstrap operators are agent IRIs or roles, as the configure guide says.)
3. **Reload.** `prompt=none` again. The mock keeps no session, so its form
   came back, and then both planes were signed in again.
4. **Sign out.** The mock's end-session endpoint, then `/ui/signedout`, with
   both planes "signed out".

No `securitypolicyviolation`, console error or page error. The policy's
`connect-src` named `http://localhost:8080`.

**A whitespace bug the contract did not catch, and now does.** The bar read
"Data planesigned in" as text: Blazor drops whitespace-only text between an
expression and an `@if`. bUnit's default comparison normalises whitespace,
so the `Shell` cases passed. The contract test now compares with
`diff:whitespace="preserve"` on both sides. That found two components off
their cases:
- `Shell`'s plane space, now written explicitly;
- `TripleList`'s newline between the head row and the list.

Both are fixed. ADR 0206 point 6 and ADR 0204 say whitespace counts; both
are unaccepted, so they are amended in place.

**Not in the reference screens:** the sign-in callback and the signed-out
screen. They compose only cased components (`ViewHead`, `Notice`, `Button`)
and one sentence. This is listed on #96 for the screens.

**Tests:**
- `Varve.WebUi.Tests` passes 168: 18 `SignInTests`, and one more contract case
  and preview.
- `ProviderTests`' mock leg, against the updated fixture, passes its 2.

**Bundle:** 1.728 MB brotli-11, against slice 1's 1.675 MB. Sign-in adds
25 kB, mostly `System.Text.Json` and `System.Security.Cryptography`.

### The read screens

**The client (ADR 0203 point 8, completed in place).**
- **`VarveManagementClient`:**
  - `DatasetsAsync`, `LifecycleAsync`, `StatusAsync` and `SystemAsync`;
  - `DeclarationAsync`, `FleetAsync` and `GraphsAsync`;
  - `GrantsAsync`, with a management-position pin;
  - `PlacementsAsync`: one query on `/manage/sparql` for `varve:placedOn`.
- **`SparqlHttpClient`:** `ReadCommitsAsync` and `ReadCommitAsync`.
- **New model types:** wrappers for the server's words, and `Members` keeping
  what the client does not know. The per-request token logic is shared in
  `Tokens`.
- **Tests:** `ManagementClientTests` (7) and `CommitReadTests` (1). Each read
  against the server's own composition root, a problem with its request id,
  the management token on every request, and a pin on the management log.

**Screens:**
- **Datasets** (the root): placement, state, role, head, lag and storage.
- **Dataset:**
  - status, projections, recent commits and the declaration;
  - a follower's notice;
  - `?asof=p`, which hides what is served only live.
- **Fleet:**
  - this instance in the system dataset;
  - "Instances are not listed yet", as the screen has it;
  - the fleet graph as served.
- **Commits:** the latest 20, then the live tail through the client's
  `FollowAsync`. Each new commit is marked for 4 s. Scrolled away from the
  top, the tail reads "Paused while you read · n new" and shows them on
  return.
- **A commit's delta:** grouped by graph, retractions first, at most 200 per
  graph with "n of m changes".
- **Grants** (`?asof=p` on the management log) **and the declaration source,**
  behind "Declarations are read-only here".
- **New parts:**
  - `Unreachable`, `StateWord` and `ReadOnlyDeclarations`: wording once;
  - `Commits`: a feed record to a commit entry;
  - `Term`: an RDF term to a `TermChip`, with well-known prefixes compact;
  - `GrantRows`.
- **The rail** gains Fleet. Inside a dataset's screen it also shows History
  (Commits) and Declarations (Grants, Declaration).
- **One new case:** `AsOfBanner`'s `pinned-no-time`, for a management-log pin,
  which has no commit time to read.

**In a browser,** against an anonymous Release server with two datasets
declared through `varve apply`:
- every screen rendered;
- a commit made while Commits was open appeared at once, marked new;
- no CSP violation, console error or page error.

**Where a page and its reference screen differ.** The page is right by the
cases; the list is on #96 for the screens.

**Bundle:** 1.818 MB brotli-11 on a clean publish, against slice 1's
1.675 MB. A publish into a folder that held an earlier one measured 5.2 MB:
stale fingerprinted copies stay beside the new ones. Every number here is
from an empty folder.

**Tests:**
- `Varve.WebUi.Tests` passes 171;
- the client and hosting tests in `Varve.Server.Tests` pass 22;
- `Varve.Analyzers.Tests` passes 135.

### The acts: checkpoint, close and open (D4)

**Landed:**
- **The client:**
  - `VarveManagementClient.CheckpointAsync` and `SetStateAsync` (open or
    closed only), each a `VarveResult<Acknowledgement>` whose facts name the
    position;
  - `AtLeast(p)`: the same client asking every request for management
    position `p` or later (`Varve-Min-Management-Position`).
- **The Dataset screen:**
  - Take checkpoint (primary; for a follower, disabled with "Only the writer
    takes checkpoints."), and Close dataset, or Open dataset when closed;
  - each through `ConfirmDialog`. Close and open carry the drift note (the
    `DriftNote` part), and close asks for the name typed exactly;
  - the result slot shows the position, or the problem;
  - a `409` or `412` problem carries "Reload at head", which reads the view
    again;
  - after an accepted close or open, the view is read again with `AtLeast`
    the act's position.

**On `main`, close and open take no precondition.** `PUT …/state` commits
with no expected version: an `If-Match` or `Varve-If-Graphs-Match` sent
with it is ignored (checked: both answered `204`). So the `412` of the
reference screen `dataset--conflict` cannot happen against `main`. The
dialog handles it all the same, and `ActTests` drives it against a fake
server. Whether the state route should honour the declaration's version is
put on #96.

**In a browser**, against an anonymous Release server:
- Close notes: confirm disabled until `notes` was typed (`Notes` kept it
  disabled). The result was "notes is closed, at management position 6",
  and after the re-read the Open dataset button.
- Open notes: "open again".
- Take checkpoint: "Checkpoint taken at position 3", and the status's Last
  checkpoint read `at 3`.
- No CSP violation. The console logged the `404`s a closed dataset's reads
  answer, which the page shows as their problem.

**Tests:**
- `ManagementClientTests` passes 9, with close, open, checkpoint, `AtLeast`
  and `management-behind`.
- `ActTests` passes 4:
  - the typed name and the drift note;
  - `412` and `409` with "Reload at head" reading again;
  - `403` with no reload.
- `Varve.WebUi.Tests` passes 175.

### Gates: the size budget and the AOT choice (ADR 0205)

**Measured** at this milestone's head, each build from a clean tree, served by
`Varve.Server`, Chromium, cold cache, median of 10 loads of the Health screen:
- **interpreted:** 1.823 MB brotli-11, 1077 ms (963–1186);
- **AOT:** 4.108 MB, 1128 ms (1006–1283). The AOT publish took 4 min 44 s.

The plan's rule chose interpreted: AOT saves no time, and its 2.29 MB cost
about 1.8 s at 10 Mbit/s.

**The gate:** `eng/browser-size.cs` and `eng/browser-size.json`.
- **The budget:** 1,822.8 kB measured, 1,920,000 bytes.
- **The failure paths, run:**
  - a budget of 1.8 MB exited 1 with "over its budget";
  - one of 2.1 MB printed the ratchet line to lower it to 1,920,000;
  - the real budget exited 0.
- **CI:** a step of `browser wasm`, after its publish.

**Two wrong numbers caught on the way, and why.**
- **5.2 MB:** a publish into a folder that held earlier publishes; their
  fingerprinted files stayed.
- **1.55 MB:** a publish whose `obj/` an AOT publish had just written. The
  same CoreLib size, raw, but IL-stripped, so it compressed to 354 kB, not
  485 kB.

Both are why ADR 0205 point 4 says to measure from a clean tree only.

### Gates: the browser tests, the axe audit and the keyboard walk-through (ADR 0204)

**The browser tests,** `tests/Varve.WebUi.Browser.Tests`: Playwright 1.64.1
and `Deque.AxeCore.Playwright` 4.14.0, on Chrome by its path. They serve the
published bundle from `Varve.Server` in the test process, with declarations
and one commit written first. CI runs them in their own required job,
`web ui in Chromium`, after `browser wasm`, with mock-oauth2-server from the
mirror and `VARVE_BROWSER_TESTS_REQUIRED=1`.

All 5 pass locally in 43 s:
- **axe on every screen and dialog:** wcag2a, 2aa, 21a, 21aa and 22aa, with
  zero violations, in both themes. That covers 9 screens, plus the close and
  checkpoint dialogs open. A `securitypolicyviolation` watcher reported
  nothing on any of them.
- **The keyboard's first stops:** the first Tab reaches the skip link, which
  is visible, and Enter moves focus to `main` without navigating. A
  navigation inside the Web UI moves focus to the new page's `h1`.
- **Anonymous mode:** both planes say anonymous, and there is no Sign out.
- **OIDC mode against mock-oauth2-server:**
  - the sign-in completes, with one token per plane;
  - the data plane refuses the management token, and the management plane
    refuses the data token, each with 401.
- **The dual-audience refusal:** a token for `varve-dual`, whose `aud` is
  both `api://varve` and `api://varve-manage`, is refused by both planes,
  each with 401.

**A defect the tests found:** `FocusOnNavigate` focused the `h1` on the
first render too. On a fresh load the first Tab then went past the skip link
("Read again" came first). `App.razor` now renders it only after an in-app
navigation. A fresh load keeps the browser's start (WCAG 2.4.1), and a
navigation still lands on what changed (2.4.3).

**The keyboard walk-through.**
- **How it was done:** a keyboard only, in Chrome at 1280×900, on the
  anonymous server above. Each screen was loaded fresh and walked with Tab
  until focus came back round. At every stop the session read the focused
  element, its name, whether it matched `:focus-visible`, and whether its
  outline or shadow was drawn.
- **On every screen:**
  - every stop matched `:focus-visible` and drew its ring;
  - nothing focusable was hidden;
  - the order followed the reading order.

| Screen | Stops after the skip link and the shell (brand, Datasets, Health, Fleet) |
|---|---|
| Datasets | Read again, then each dataset's link (`catalog`, `notes`) |
| Health | Read again |
| Fleet | Copy, then the declaration source (a scrollable `region`) |
| A dataset | the rail (Commits, Grants, Declaration), the crumb, Take checkpoint, Close dataset, All commits, each commit's position, Copy, the source |
| A dataset as of 1 | the rail, the crumb, Return to head, the commit's position. The acts are not offered |
| Commits | the rail, the crumb, each commit's position |
| A commit | the rail, the crumb |
| Grants | the rail, the crumb |
| Declaration | the rail, the crumb, Copy, the source |

**The dialogs,** each opened with Enter on its button:
- **Close dataset:**
  - focus opens on the name field;
  - the confirm button is disabled until the name is typed, so Tab passes
    it and goes to Cancel;
  - after typing `notes`, Tab goes on from the field;
  - Escape closes the dialog and returns focus to Close dataset.
- **Take checkpoint:**
  - focus opens on Cancel, and Tab reaches Take checkpoint;
  - Escape closes it and returns focus to the button.
- **Both:** Tab stays within the dialog and the page behind is inert. The
  one stop outside the dialog is the browser's own toolbar, which is what
  `showModal` does.

**Noted for #96, not changed:**
- "All commits" on a dataset is a `button` that navigates. The reference
  screens pin it as a button, so the page follows them. A link would be the
  usual choice.

### Close-out

- **The descriptor** is `releases/v0.1.0-preview.17.yaml`. Its basis is
  #167, with ADRs 0200–0206. It is `.17` because two open pull requests, Inference part
  1 and supply chain transparency, both propose `.16`. `eng/release.cs`
  orders by precedence and needs no contiguity.
- **`CHANGELOG.md`** is projected from it by `eng/changelog.cs`.
- **The README's Status** names `.17`, and its image tags follow.
- **`docs/roadmap.md`** marks part 1 *(complete)* under Web UI, whose own
  heading stays open for part 2, as #96 does.
- **AGENTS.md's State** leads with this milestone.
- **The bundle ships with the release.** `publish.yml` gains the job that
  builds `varve-webui-<version>.zip`, runs the size gate and the browser
  tests on it, and attests it; the release attaches it (ADR 0202 point 6).
- **Open on #96 at close-out:**
  - Zitadel's management-plane token, by token exchange (sign-in's
    third issuer);
  - `If-Match` on the state route;
  - "read at" on Commits;
  - the declaration pages the reference screens add;
  - "All commits" as a button.

### Acceptance, 2026-10-28

A later session (Claude Code, model `claude-fable-5-1`), on the maintainer's
instruction to accept every decision on the open pull requests, added
`accepted-by` and `accepted-at` to the 40 keys this branch files over
main: hosts-at-layer-6-the-composition-root (1 key, ADR 0060), on-behalf-of-and-the-token-provider (1 key, ADR 0127), varve-webui-a-blazor-webassembly-host (8 keys, ADR 0200), the-web-uis-own-oidc-client (6 keys, ADR 0201), serving-the-web-ui-and-its-content-security-policy (7 keys, ADR 0202), typed-reads-in-the-protocol-client (9 keys, ADR 0203), test-dependencies-for-the-web-ui (5 keys, ADR 0204), the-web-uis-size-budget-and-no-aot (3 keys, ADR 0205). The acceptance is the maintainer's, who had read them; the
session only wrote it at their direction, as the acceptance of ADRs
0127–0133 was written. The ADRs' status text and the index rows are left as
filed, as in that precedent.

### The rulings of 2026-10-28

The maintainer accepted ADRs 0200–0205 and the part's two amendments on the
pull request (ebd7f7a). The same day, these rulings were given on #187:
- the release number;
- "All commits", with `varve-docs-ui-screens-2.zip`;
- Zitadel's management token;
- `If-Match` on the state route;
- Commits' "read at";
- the extra declaration pages.

What was done:
- **The release number.** `.17` stays. A pull request that lands after this
  one takes the next free number at landing.
- **The screens and the link.**
  - `docs/ui/screens/` now holds the zip's pages. `README.md` and
    `screens.css` are kept, because the pages link the stylesheet.
  - "All commits" is `<a href>` in the panel head, pinned by the new
    `Panel` case `head-with-link`.
  - The pages drop the panel-title ids no table uses. Only Datasets,
    Projections and Grants keep theirs, and the screens agree.
- **Commits' stamp.** The "read at" stamp is the newest position the list
  holds. It advances with each commit the live tail shows. While paused it
  stays, because the commits waiting are not in the list. A browser test
  writes a commit and watches the stamp reach it.
- **`If-Match` on close and open.**
  - `SetStateAsync` takes the version of the declaration graph read, and
    sends it as `If-Match`. A component test checks that the header is
    the declaration's ETag.
  - The state route ignores the header. That gap is filed as #189.
  - The dialog's 412 path stays as built, tested against a fake server.
- **Agents and roles, and Graph templates.**
  - Both are read from the grants resource, so no API gap was filed.
  - The client had dropped two things the wire carries: a group's claim
    type, and a template's match. They are now kept as
    `GrantEntry.GranteeClaimType` and `GraphScope.TemplateMatches`.
    DD0013 refused a second member on the `GraphTemplate` wrapper, so the
    match is a map beside the templates.
  - A principal or a template no grant names is not shown on these
    screens. The declaration's source has every node.
- **Shapes, source only.**
  - `ShapesBindingsAsync` reads each binding and its shapes graph through
    `/manage/sparql`. The dataset's name is escaped into the graph's IRI.
  - Each shapes graph is read from the data plane in Turtle and shown in a
    `SourceBlock`, with no management stamp, because it is data.
- **No new markup.** All three screens are built from `ResourceTable`,
  `SourceBlock` and the read-only note. The rail adds them in the order the
  screens show.
- **The client additions.** They are recorded as a dated amendment to ADR
  0203, with three keys filed unaccepted.
- **Zitadel by token exchange.**
  - `Varve:Auth:ManageGrant` takes `refresh` or `exchange`; there is also
    `ManageAudience`.
  - The exchange is RFC 8693, from the public client, with no secret and
    no actor token, asking for a JWT.
  - It is recorded as a dated amendment to ADR 0201, with one key filed
    unaccepted.
  - **Against Zitadel v4.19.4,** fresh from the CI compose and seeded by
    `eng/zitadel-seed.cs`:
    - the exchange runs from a public user-agent app with auth method
      `none`;
    - asked for a JWT, it answers one naming exactly the requested
      project;
    - asked for an access token, it answers an opaque one;
    - it only narrows. A data-only subject token is refused
      (`invalid_target`, then `invalid_scope` without `audience`).
  - **Result:** on Zitadel the management plane stays signed out, as the
    guide's issuer table now says. Asking for both audiences at sign-in and
    exchanging each plane's token is put to the maintainer on #187.

Measured:
- **Tests:**

  | Suite | Result |
  |---|---|
  | `Varve.WebUi.Tests` | 184 / 184 |
  | `Varve.WebUi.Browser.Tests` | 6 / 6, about 1 minute |
  | Server: Web UI hosting, management client | 19 / 19 |
- **The download:** 1,830.4 kB brotli-11, from a clean tree. That is 155.4 kB
  over slice 1's 1,675 kB, 7.6 kB over the acts, and within the 1,920,000-byte
  budget.
