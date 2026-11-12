# The descriptor's date follows its digests

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#248](https://github.com/Hafeok/Varve/issues/248); [#209](https://github.com/Hafeok/Varve/issues/209) referenced |
| **Date** | 2026-11-10 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* The session's metadata reports the model, but this tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYQ7rmZCcSgsvBFhZMm` |
| **Branch** | `claude/dazzling-darwin-8irixp-step3`, on step 2's branch at 4051685 |
| **ADRs** | [0102](../adr/0102-a-release-is-a-descriptor.md) (amended 2026-11-10), [0224](../adr/0224-supply-chain-transparency.md) (untouched) |

## The prompts

### The maintainer, in the session

> Add a step 3: #248, the descriptor date set at landing, and where the SBOM creation timestamp comes from. Report your plan for all three steps before writing code.

> You were right about #248: its premise was wrong and the issue is corrected. The ruling is on the issue now: one field, release.cs sets date: whenever it rewrites the sbom: block, land.cs refuses a date more than three days old, a dated amendment to docs/releases.md rule 3, and ADR 0224 untouched. No sbom-created: field.

### The session prompt, step 3, as attached

> ## Step 3 — #248: the descriptor date follows the digests
>
> Implement #248 as corrected on 2026-11-10. Its first version had a wrong premise (it said the SBOM creation time departs from ADR 0224; it doesn't, D3 stands). The plan on the issue is the ruling:
> - one field, no `sbom-created:`, and ADR 0224 untouched;
> - `eng/release.cs` sets `date:` to the current UTC day whenever it rewrites the `sbom:` block, and regenerates `CHANGELOG.md`;
> - `eng/land.cs -- N` refuses a descriptor dated more than three days before the landing day, and prints the command that rewrites the digests;
> - a dated amendment to `docs/releases.md` rule 3 (from ADR 0102);
> - the gates listed on the issue.
>
> Commit trailer: `Closes #248`.

## The report

- **Where the SBOM creation time comes from**, found before the ruling:
  `publish.yml` and `ci.yml` pass the descriptor's `date` as `--created`,
  which is what ADR 0224 point 2 (D3) decides. The first version of #248 was
  wrong about that, and the maintainer corrected it.
- **Nothing rewrote the `sbom:` block before**: the digests were pasted from
  `sbom.txt` by hand. So the rewrite is a new command,
  `eng/release.cs -- --sbom <version>`. It runs `eng/sbom.cs` created at the
  current UTC day, replaces the block and sets `date:` in one edit, and
  regenerates `CHANGELOG.md`.
  - `--from <dir>` takes SBOMs already written, and refuses them unless every
    one was created on that day.
  - `--draft` points at the command.
- **`eng/land.cs`** refuses, at both steps, a descriptor the head adds or
  changes whose `date:` is more than three days before the landing day
  (UTC), or that has none, and prints the `--sbom` command.
- **Two changes to the posted plan, recorded on #248:**
  - The branch is stacked on #209's, because both add `land.cs` fixtures and
    one runner serves both.
  - ADR 0102 gets a dated amendment beside `docs/releases.md` rule 3, because
    rule 3 restates 0102's line on `date`. It adds detail and changes no
    ruling (ADR 0068 point 2). ADR 0224 is untouched.
- **Gates:**
  - `tests/fixtures/release-sbom/`: four cases, 0 judged wrong. The block and
    the date move together; a draft's placeholder is replaced; SBOMs created
    on another day are refused; a format-1 descriptor is refused.
  - `tests/fixtures/land/`: three date cases joined. Four days is refused,
    three days passes, and no date is refused. That makes 12 cases, 0 judged
    wrong.
  - A `ci.yml` step on Linux rewrites the newest format-2 descriptor, renamed
    and backdated, in a scratch directory. It requires `date:` to be today,
    every SBOM created at today 00:00Z, and `eng/sbom.cs --descriptor` to
    match. Run locally: 29 SBOMs created at 2026-11-10T00:00:00Z, and "sbom
    lines match the 29 generated SBOM(s)". Checked against 2026-11-09
    instead, every line differed and the exit was 1.
- **Preview.18 keeps 2026-10-27**: it is cut, and nothing touches it.
