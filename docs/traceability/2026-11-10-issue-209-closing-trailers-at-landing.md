# Closing trailers checked at landing

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#209](https://github.com/Hafeok/Varve/issues/209); [#257](https://github.com/Hafeok/Varve/issues/257) and [#108](https://github.com/Hafeok/Varve/issues/108) referenced |
| **Date** | 2026-11-10 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* The session's metadata reports the model, but this tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYQ7rmZCcSgsvBFhZMm` |
| **Branch** | `claude/dazzling-darwin-8irixp-step2`, on step 1's branch at c2e965e |
| **ADRs** | none decided; [0088](../adr/0088-only-checked-commits-reach-main.md) and [0102](../adr/0102-a-release-is-a-descriptor.md) applied |

## The prompt

> ## Step 2 — #209: closing trailers checked at landing
>
> Implement #209 as written: the `Completes:` line in the PR template, `land.cs -- N` listing the issues the push will close and refusing a declared issue with no closing trailer, a missing `Completes:` line refused, the descriptor's own trailer counting, and the four fixtures under `tests/fixtures/land/` run from `eng/ci.cs`.
>
> One addition, which goes on #209 as a comment before you build it: **after `land.cs -- N --main` has fast-forwarded `main`, it checks that every issue named in a closing trailer of the pushed commits is closed.** For any still open, it prints the issue and the commit, and exits non-zero. It never closes anything itself; closing by hand stays a human or session act with a comment. A fixture covers it: a pushed trailer whose issue is still open is reported. If the GitHub state can't be read (no token, rate limit), it says so and exits non-zero, rather than reporting success.
>
> Document the `Completes:` line and the after-landing check in the step 1 section of `AGENTS.md` (the **Landing** and **After landing** bullets), so the rule and the script describe the same thing.
>
> Commit trailer: `Closes #209`.

The maintainer agreed the plan posted on #209 ("All three plans are agreed. Go ahead.").

## The report

- **The addition went on #209 as a comment** before the code, with the plan.
- **`eng/land.cs`:**
  - At both steps it lists every issue a closing trailer line in
    `main..head` closes, with its title. A trailer is recognised as
    `eng/issue-refs.cs` recognises one, and the subject line is skipped.
  - It reads the pull request's `Completes:` line, ignoring HTML comments,
    so the template's guidance doesn't count. It refuses a missing,
    duplicated or malformed line, and a declared issue no trailer closes.
  - When the pull request's body can't be read, it refuses (exit 2) rather
    than skipping the check.
  - A `--commit` landing has no pull request, so it only lists.
  - After `--main` it asks GitHub for each issue's state, again for up to a
    minute while one is open, since GitHub closes issues a little after the
    push. An issue still open is printed with its commit (exit 1), and
    GitHub unreadable is exit 2.
  - The decisions are pure functions over plain values, so `--fixtures`
    judges them with no git or GitHub.
- **Nine fixtures:** #209's four, the descriptor's trailer, a malformed
  line, and three after landing (all closed, one open, unreadable). The
  `land-fixtures` job runs them. The failure path was proven: a `pass-` case
  copied as `fail-` gave "10 cases, 1 judged wrong", exit 1.
- **A real dry run**, `land.cs -- 253 --print`, listed #251 with its title
  and refused #253 for having no `Completes:` line. **Every pull request
  open when this lands needs the line added before it lands.** The template
  starts new ones with `Completes: none`.
- **`AGENTS.md`'s Landing and After landing bullets** describe the line, the
  refusal and the check after landing.
