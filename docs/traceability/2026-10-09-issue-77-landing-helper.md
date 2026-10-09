# A landing helper

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#77](https://github.com/Hafeok/Varve/issues/77), opened by this session |
| **Date** | 2026-10-09 |
| **Tool** | Claude Code 2.1.294, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01YbhFvg7wVgX3FdRbofvTt8` |
| **Branch** | `claude/gracious-planck-l5ugrc`, from `main` at 6f49156 |

## The prompt

> we should build a tool for the land pushing i keep forgettinng the command

## The report

`eng/land.cs` makes the two pushes of ADR 0088's `land/` route two commands:

- `dotnet run eng/land.cs -- <pr>`: fetches the pull request's head and pushes
  it to `land/<name>`. The name is `release-<version>` when the head adds
  a descriptor, so the push also dry-runs the release (ADR 0102); otherwise
  it is `pr-<N>`. It prints the next command.
- `dotnet run eng/land.cs -- <pr> --main`: pushes the same head to `main`,
  then deletes the `land/` branch.
- `--commit <rev>` lands a commit of the maintainer's own, `--name` overrides
  the name, and `--print` shows the commands and runs none.

Before pushing anything it refuses a head that `main` is not an ancestor of,
since the fast-forward could not happen. `--main` refuses unless `land/<name>`
on origin is the same commit, so `main` moves only to the commit that was
checked there. With the GitHub CLI signed in, it warns when the pull request
has no `approve <sha>` comment on the head. That check is advisory: `agent
review` is the verdict, and the rulesets and the required checks still
judge. The helper decides nothing.

It is documented in AGENTS.md's commands, CONTRIBUTING.md and
`docs/releases.md` §3, and `eng/ci.cs` gains a `land` job, which builds it and
prints its usage so that it does not rot between landings.

Proven by running it with `--print` against the live repository:
- #75's head plans `land/pr-75`;
- `--main` before the `land/` push is refused;
- a head that `main` does not descend from is refused;
- a head already on `main` is reported as nothing to land.

Developed with AI assistance under human review.
