# Session, 2026-11-16: the container image ships the Web UI

Developed with AI assistance under human review. The full record, with the
prompt and the maintainer's answers, is
[`2026-11-16-issue-296-web-ui-in-the-image.md`](../traceability/2026-11-16-issue-296-web-ui-in-the-image.md).

A cloud session, not the maintainer's delegate (ADR 0239 point 1). It filed
its decisions unaccepted, posts its head and asks. It approved, landed,
tagged and accepted nothing.

## Pull request

| PR | What | Closes |
|---|---|---|
| this branch, `claude/eloquent-fermi-6lgxt9` | ADR 0245; the image carries the Web UI at `/app/webui` and serves it by default; `Varve:WebUi:Enabled` and `Varve:WebUi:Settings`; the generated settings and `NotConfigured`; the image's SBOM; the guide; the descriptor `v0.1.0-preview.22` | #296 |

## What was built

- **The server** (`Varve.Server`):
  - `Varve:WebUi:Enabled`, the off switch;
  - `Varve:WebUi:Settings`, `File` or `Generated`;
  - `Varve:WebUi:Server` and `Varve:WebUi:Auth:*`, checked at start.

  With `Generated`, `/ui/appsettings.json` is written field by field from a closed record (`GeneratedWebUiSettings`). The bundle's file and its compressed copies are then never served.
- **The console** (`Varve.WebUi`):
  - only the exact `Anonymous` is anonymous;
  - `NotConfigured`, or a missing or unknown mode, shows "Sign-in is not configured", naming the settings to add, and reads nothing.
- **The image:**
  - `COPY webui /app/webui`, with `VARVE__WEBUI__PATH` and `VARVE__WEBUI__SETTINGS=Generated`;
  - `publish.yml`'s `image` job unpacks the `webui` job's attested zip;
  - `ci.yml`'s `container` job takes the `wasm` job's bundle, and checks:
    - the headers;
    - the settings in each mode;
    - the off switch;
    - the console in Chromium against the image.
- **The SBOM:** the image's document contains the bundle's once. A proposed release whose image SBOM lacks a package of the zip's is red.

## ADR

- **0245**, filed unaccepted: nine keys. `ZipAssetNoImage` moves to it from 0202's set, with the superseding statement.
- **Amended 2026-11-16, add-only and unaccepted:** 0111, 0201, 0202 and 0225.
- **Numbered 0245** after checking `main` (ends at 0243) and every open pull request:
  - 0240 is #268's;
  - 0242 is #267's;
  - 0244 is #264's;
  - #247 reuses 0226.

## Measured

| | before | after |
|---|---|---|
| image, linux-x64, compressed | 20,094,976 B | 26,533,376 B (+6.44 MB, +32 %) |
| image, unpacked | 64.8 MB | 81.6 MB (the bundle's layer +10.4 MB) |
| `Varve.Server` tool `.nupkg`, if it carried the bundle (not done) | 16,453,096 B | 22,852,690 B (+6.40 MB, +39 %) |

The CI time the `container` job's wait for `wasm` adds is posted on #296 after the pull request's first run.

## Issues

- **Opened:** #297, the Web UI cannot reach a separate management listener.
- **Closed by the pull request:** #296.
- **Left open on #296**, by ruling 5: whether the .NET tool carries the bundle.

## Test counts

New tests (before → after, in the projects they join):
- `Varve.Server.Tests`: 232 → 241 (187 run here, 54 skipped for want of an issuer or a store service). `WebUiGeneratedSettingsTests` adds 9 cases.
- `Varve.WebUi.Tests`: 233 → 241. `SignInNotConfiguredTests` adds 8 cases.
- `Varve.WebUi.Browser.Tests`: 8 → 11.
  - the not-configured console in-process;
  - `WebUiImageTests`, 2 cases, run against the image in the `container` job.

`eng/ci.cs` here, with SDK 10.0.403: all 68 jobs pass, `conformance` and `pack` included. The full result is posted with the head on the pull request.

## Questions for the maintainer

1. **The tool.** Ruling 5 leaves this open, and #296 has the measured cost. Should `Varve.Server`'s tool carry the bundle?
2. **The image's reproducibility.** `SOURCE_DATE_EPOCH` with `rewrite-timestamp` would make the whole image byte-reproducible, not only the bundle's layer. Do you want it as its own issue?
