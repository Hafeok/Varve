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

- **0245**, filed unaccepted: nine keys. Its `WebUiInTheImageAndTheZipStays` supersedes 0202's `ZipAssetNoImage`, which stays in 0202's set as accepted, marked superseded.
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
- **Opened since, at the maintainer's direction:** #325, the .NET tool carries the bundle (linked from #129); #326, a byte-reproducible image, outside the 1.0 list.

## Test counts

New tests (before → after, in the projects they join):
- `Varve.Server.Tests`: 232 → 241 (187 run here, 54 skipped for want of an issuer or a store service). `WebUiGeneratedSettingsTests` adds 9 cases.
- `Varve.WebUi.Tests`: 233 → 241. `SignInNotConfiguredTests` adds 8 cases.
- `Varve.WebUi.Browser.Tests`: 8 → 11.
  - the not-configured console in-process;
  - `WebUiImageTests`, 2 cases, run against the image in the `container` job.

`eng/ci.cs` here, after merging `main` (SDK 10.0.404): all 70 jobs pass, `conformance`, `pack`, `sbom` and `release` included. The hosted result is posted with the head on #296.

## What the first hosted run found

#308's first run, at `5ad44884`, was red for two real reasons. The causes are on #296.

- **The descriptor was stale.**
  - It was written on the branch's base with SDK 10.0.403, and the pull request's CI tests the merge with `main`, which had moved to 10.0.404 (#293) and a new base image digest (#234).
  - The Web UI's SBOM, and the image's that contains it, changed with them.
  - The fix: `main` merged in, and `eng/release.cs --sbom` run again on the merged tree. Merging also brought ADRs 0240 and 0242 into the release's basis.
- **The image's bundle directories were `644`.**
  - The runner's builder did not apply `COPY --chmod`'s symbolic `X` to directories, so uid 1654 could read no file below `/app/webui`.
  - The fix: the modes are now set by GNU `chmod` in the `layout` stage.
  - The `container` job now requests every file of the bundle.
- **`eng/ci.cs` could not see either.** It has no container job, and it never compares a descriptor against a fresh generation.
- **The two platforms' bundle layers differ.** The layer records the `app/` directory's build time, so ADR 0245's first claim that they are one blob was wrong. Corrected in 0245, and part of #326.

## CI time

The `container` jobs now wait for `browser wasm`, and start about 4 minutes into the run.

| `container` job | `main` (`c5d148fb`) | this branch |
|---|---|---|
| linux-x64 | 4m16s | 5m30s |
| linux-arm64 | 3m51s | 4m17s |

They finish at 9m43s, about 7 minutes before the run's slowest job, so the run's wall-clock does not change.

## Questions for the maintainer

None open: the tool (#325) and reproducibility (#326) are issues of their own.
