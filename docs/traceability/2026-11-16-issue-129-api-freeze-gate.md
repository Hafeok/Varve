# The public API freeze gate: each package against its published nupkg, and the members only tests use

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#129](https://github.com/Hafeok/Varve/issues/129), its public API freeze part |
| **Date** | 2026-11-16 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRoynRxPSi7vx7A5Yq` |
| **Branch** | `claude/api-freeze-129`, from `main` at 48a1fd5, beside #247 (CLI distribution), on which it does not depend |

## The prompts

The session's first message and its session prompt are in
[the #119 record](2026-10-27-issue-119-supply-chain-transparency.md). Its plan
for this step is #129's comment of 2026-10-27, and the maintainer approved it
on #119 with, for this step:

> F1: the freeze gate rule as stated.

The message that started this step:

> Start step 3 now, on its own branch, beside #247: the public API freeze gate
> from #129. Post your plan on #129 first. Before 1.0 it reports, and fails
> only where a breaking change isn't marked. It covers every Varve.* package,
> Varve.Protocol.Client and the management API. The pull request lists every
> public member whose only consumer is a test.

The refreshed plan is #129's comment of 2026-11-16. It asks two questions,
M1 and A1, and says the session goes ahead on its proposals unless told
otherwise.

## What was done

- **ADR 0227**, filed unaccepted, with a dated amendment to ADR 0035 points
  2 and 4, and the decision set `api-freeze`.
- **`eng/api-freeze.cs`:**
  - packs each library package against its nupkg at the newest release tag
    that is an ancestor of HEAD;
  - reads the findings package validation writes, and judges them by F1:
    before 1.0, a finding fails only where no `!` commit since the tag
    touches the package and no proposed descriptor marks it `(breaking)`;
    from 1.0, it fails unless the major moves;
  - `--fixtures` runs the rule's cases;
  - `--test-only` lists the members only tests reference.
- **`Directory.Build.props`** turns validation on for the one project being
  packed.
- **`ci.yml`:** the `api freeze` job, which also writes the test-only report
  to the step summary. **`.github/repo-standard.yaml`** requires it.
- **`eng/ci.cs`:** `api-freeze-fixtures`.
- **`tests/fixtures/api-freeze/`:** the fixture package in three surfaces,
  and twelve cases.
- **`CONTRIBUTING.md`:** a breaking change says so.

## What was measured

- **Against v0.1.0-preview.21:** the 28 libraries report no finding.
  `Varve.Server` is reported as not a library. The run took 3 min 14 s under
  `CI=true` on 4 cores, locked restore included.
- **It fires on real history:**
  - `Varve.Protocol.Client` against preview.2: 5 × `CP0002`.
  - Against preview.15:
    - `Varve.Store` reports `CP0001 T:Varve.Store.Log.CommitRequest` and
      `CP0002 Dataset.CommitAsync(CommitRequest, …)`, which pass, marked by
      `d8ebfbc4` (`refactor(store)!:`) and `c86f9e4e` (`feat(store)!:`);
    - `Varve.Protocol.Client` reports
      `CP0002 M:Varve.Protocol.Client.Model.BearerToken.Fixed(System.String)`,
      which **fails**: `0be712ac` moved it to `BearerTokens.Fixed` with no
      `!`, and preview.20's descriptor names the move without `(breaking)`.
      It shipped. The gate would have stopped it.
- **The baseline as a global property** fails restore with `NU1102` for every
  referenced Varve project. That is why validation is turned on per project.
- **The fixtures:** 12 of 12 cases give their expected result, in about
  28 s, with no network.
- **`--test-only`**, over 65 assemblies (31 of them tests), takes 1.4 s once
  everything is built:
  - 45 test-only types and 428 test-only members across the 28 packages;
  - the pull request lists them.
  - Spot checks against the source held. For example,
    `Dataset.CatchUpAsync` is named outside `Varve.Store` only in a doc
    `cref` and by an unrelated private method.

## What is left

- **The maintainer's decisions:**
  - M1, whether "the management API" means more than the `Varve.Management`
    package and the management calls of `Varve.Protocol.Client`;
  - A1, ADR 0227 rather than an amendment alone;
  - each test-only member, justified or made internal before the freeze.
- **The ruleset:** requiring `api freeze` takes effect when the maintainer
  applies `.github/repo-standard.yaml`.
- **Accept:** ADR 0227, the amendment to 0035, and their ledger keys.
- **Not read by `--test-only`:** the sample tests, `Varve.Store.BrowserTests`
  and `Varve.WasmSmoke`, which the job does not build. They are tests, so
  missing them can only move a member from test-only to unreferenced, never
  the other way.
