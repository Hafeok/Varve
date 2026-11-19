# The operator's guide corrected, and every procedure marked with what runs it

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#129](https://github.com/Hafeok/Varve/issues/129) (the operator guide, pull request A of [the split](https://github.com/Hafeok/Varve/issues/129#issuecomment-6978780204)); closes #161, #273, #274, #275; found: #342, #344 |
| **Date** | 2026-11-18 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS9TK9P9VYitD1bP8Z` |
| **Branch** | `claude/relaxed-wright-nv07v6`, from `main` at bb1d1423 |
| **ADRs** | [0248](../adr/0248-every-procedure-names-what-runs-it.md), filed unaccepted |

## The prompt

> For the docs session.
>
> Step 1 has landed (#324). Start step 2 of your prompt, the operator guide, on main at bb1d1423c0daf06f56cec154a29ca7e92cdd4bb7. Post the split into pull requests on #129 first. Since your prompt was written:
> - #247 landed the CLI install page; do not rewrite it.
> - #308 (the Web UI in the image) and #322 (the object-store compatibility page) are still open and own their pages; leave the marked gaps.
> - #320 landed client idempotency keys; the fleet guide already describes the retry rule.
> - Your audit's code findings (#301, #304, #307) stay issues; do not fix product code.
> Report each head as "head <sha>" with its hosted result.

The step is the session prompt's step 2 (`varve-claude-code-operator-guide-and-docs-site.md`), and the plan and the maintainer's answers of 2026-11-16 are on #129. Answer 5 bears on this pull request: "Yes to the markers and their gate."

## The report

- **The split was posted on #129 first**, as five pull requests: A, B1 (#319), B2, C and D. This is A. The docs-site deliverable was ticked in #129 and #15: `docs-site.yml` run 44301342045 deployed `bb1d1423` to https://hafeok.github.io/Varve/.
- **The corrections.** Each finding of the audit's §2 was re-checked on `bb1d1423` before it was changed. `run`, `configure`, `observe`, `fleet` and `small-machine` had moved since the audit, and some findings had been fixed meanwhile: `small-machine` now names `Store:MemtableLimit`. Each claim changed was checked against the code again:
  - the settings binder (`Settings.cs`, `SettingsCheck.cs`);
  - `--print-config`'s writer and its test;
  - the CORS lists (`ServerHost.cs`);
  - every span name (`Traced("…")`);
  - every `LoggerMessage` of `Varve.Server`;
  - the routes (`VarveEndpoints.cs`);
  - `AsOfLogDistance`;
  - `Forwarding.cs`;
  - `Lease.cs` and `FileStorage.Owner`;
  - which commands open a `Target`;
  - the soak judge's allowance.
- **Two of the audit's findings were wrong in detail, and were corrected before being written down:**
  - `MaxAsOfDistance` is measured from the nearest checkpoint, but a position at or below the newest extent set's end replays nothing (`AsOfLogDistance`). The text #161 proposed would have been wrong for that case.
  - `agent-required` is answered only by the settings route of a host with no management plane. `varve serve` always has one, so it never answers the type.
- **Found and filed:**
  - **#342.** `Idempotency-Key` is not among the CORS allowed headers, so a browser on another origin can't send one.
  - **#344.** `varve manage move-events` and `WaitForDatasetSetup` are run by no test.
- **The markers.**
  - **What ADR 0248 adds:** five rulings, filed unaccepted.
  - **The gate,** `eng/procedures.cs`, is the `procedures` job, with `procedures-fixtures` proving both paths:
    - `failing` gives seven findings and exit 1;
    - `passing` gives exit 0.
  - **What the gate counts on `bb1d1423`:** 70 blocks. 40 are run by a job, 23 by none (each with its reason), 3 are examples, and 4 on `web-ui.md` are exempt while #308 rewrites the page.
  - **Where a marker names an equivalent:** many markers name a test that runs an equivalent configuration rather than the block itself, and each such note says so.
  - **"Not run in CI" sections:** ten are generated from the markers, and two are written by hand, for the prose step lists in `upgrade.md` and `fleet.md`.
- **Not changed, by the maintainer's scope:**
  - **#247's install section**, apart from its markers.
  - **`web-ui.md`,** which is #308's. Its remaining findings are posted there.
  - **The object-store compatibility page,** which is #322's.
  - **#299–#307,** which stay issues.
- **Run locally:**
  - `procedures` and `procedures-fixtures`, `site-links`, `licence-headers` and `test-projects`;
  - `decision-sets`: 1,592 decisions, 5 unaccepted, all ADR 0248's;
  - `ProblemCatalogueTests`: 3 passed;
  - a full site build: 558 pages, 0 findings, with the markers rendered as HTML comments.

  The whole pipeline was not run here; CI runs it.
