# Model names refused in three layers: the tool's default, the hook, the gate

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#169](https://github.com/Hafeok/Varve/issues/169) |
| **Date** | 2026-11-11 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* The session's metadata reports the model, but this tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYQ7rmZCcSgsvBFhZMm` |
| **Branch** | `claude/dazzling-darwin-8irixp-169`, on `3da8929c`, the approved head of #259–#261 |
| **ADRs** | [0241](../adr/0241-model-names-refused-identifiers-allowed.md) (implemented), [0033](../adr/0033-commit-traceability.md) |

## The prompt

> Read AGENTS.md, then #169 including the comment of 2026-11-11. Build all three layers in one pull request: the .claude/settings.json attribution, the commit-msg hook rewrite, and the gate. Post your plan on #169 first. Confirm on the issue that a cloud session honours the project-level setting.

## The report

- **The plan went on #169 first.**
- **One pattern, held once:**
  - `eng/model-names.cs`, with the families in `eng/model-families.txt`;
  - a name is a family word, optionally after the product name, followed by a
    version, matched regardless of case and only standing alone;
  - the hyphenated API identifier, the bare product name, a family word with
    no version and a version with no family word all pass.
- **The gate:**
  - every commit a pull request or a `land/` push adds over `main`, merges
    included, after `2cad18d5`, which is the lower bound in the delegate's
    comment;
  - the range is worked out as `eng/issue-refs.cs` works it out;
  - `model-names` and `model-names-fixtures` in `eng/ci.cs`;
  - the release call skips it.
- **The hook:** `eng/hooks/commit-msg` runs the script's `--message` mode
  first, for every author. A co-author trailer naming a model is rewritten to
  the allowed one, a repeat folds, and a name anywhere else refuses the commit
  with the line quoted. When the script cannot run, the hook says so and leaves
  the message to the gate.
- **The tool's default:** `.claude/settings.json` sets `attribution.commit`
  and `attribution.pr`. `AGENTS.md` states the rule and names no tool's
  settings file (ADR 0033).
- **Exercised on scratch commits, not pushed:**
  - a trailer naming a model was rewritten;
  - a name in the body was refused, exit 1;
  - a commit made with `--no-verify` was caught by the gate, exit 1, naming
    the commit and the line.
- **Fixtures:** 13 cases, 0 judged wrong. The versions are fictional, so no
  real model's identifier is in the repository.
- **Lower bound:** `2cad18d5~4..2cad18d5` reads 0 commits, and
  `2cad18d5~4..3da8929c` reads 16 commits, none naming a model, which agrees
  with the delegate's count.
- **Not in this change:** the port to the analyzer repository (#65), which is
  #169's last box.

## Addendum, 2026-11-12: the lower bound moves

A train of #236, #233, #232 and #282 went red on `model-names` alone: two
commits from #236's branch, `2d94eb94` and `195a8cdf`, made before ADR 0241,
name a model in a co-author trailer. The train landed nothing. #236, #233 and
#232 then landed as their own train, with those commits, and `main` was at
`e684009a`. At the maintainer's direction on #282, the bound moves to
`e684009a`. `main` was merged in, and over `2cad18d5..e684009a` the gate
finds exactly those two commits and nothing else.

