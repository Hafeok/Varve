# Issues are the work log, and model names refused in commit messages

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#257](https://github.com/Hafeok/Varve/issues/257); [#169](https://github.com/Hafeok/Varve/issues/169) referenced |
| **Date** | 2026-11-10 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* The session's metadata reports the model, but this tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYQ7rmZCcSgsvBFhZMm` |
| **Branch** | `claude/dazzling-darwin-8irixp`, from `main` at 81fba3d |
| **ADRs** | [0241](../adr/0241-model-names-refused-identifiers-allowed.md) (filed unaccepted), [0033](../adr/0033-commit-traceability.md) (one ruling superseded) |

## The prompts

### The session prompt, step 1, as the maintainer attached it

> ## Step 1 — AGENTS.md: "Issues are the work log"
>
> No issue covers this yet. Search for an open one first; if none exists, open one titled "AGENTS.md: issues are the work log", with the text below as its proposal. That issue is what this step's commit closes.
>
> Add this section to `AGENTS.md` directly after **Every commit**, word for word unless something in it contradicts an accepted ADR, in which case say so on the issue before changing it:
>
> *(the section, as it now stands in `AGENTS.md`, apart from the landing note below)*
>
> Also correct the model-identifier rule. The maintainer's ruling of 2026-10-27, which #169's gate implements, is: **a commit message never carries a model's name; a model's identifier is allowed.** `AGENTS.md` still says the reverse in **Every commit** ("A commit message never carries a model's identifier …") and in **Never** ("… or one whose message carries a model's identifier"). Rewrite both lines to the ruling. The traceability record still names the tool and the model exactly; that line doesn't change. If a PR for #169 is open and touches the same lines, coordinate on #169 instead of conflicting with it, and say which you did.
>
> If `CLAUDE.md` or any other agent instructions file repeats either rule, keep it consistent (CLAUDE.md only points at AGENTS.md today, per ADR 0033).
>
> Commit trailers: `Closes #<the new issue>` and `Refs #169`.
>
> ## Added 2026-11-10
>
> **AGENTS.md has changed since this prompt was written.** ADR 0239 (the maintainer's delegate) amended the `accepted-by` rule: only the maintainer, or his delegate for a decision he has read, writes `accepted-by`. A cloud session is not the delegate. Fit the new section around that text. In its **Landing** and **After landing** bullets, "the session" means whichever session lands: the delegate lands and checks the closings; a cloud session posts its head, lists the issues its change touches, and asks.

### The maintainer, in the session

> Read AGENTS.md, then the session prompt in varve-claude-code-issue-log.md. Note that ADR 0239 has since changed AGENTS.md's accepted-by rule, so fit the new section around it. Add a step 3: #248, the descriptor date set at landing, and where the SBOM creation timestamp comes from. Report your plan for all three steps before writing code.

> You're right that you are not the delegate: file decisions unaccepted, post your heads, and ask. Post your plan for all three steps on their issues before writing code.

> All three plans are agreed. Go ahead. […] Step 1's amendment to ADR 0033 and its replacing key stay unaccepted, and the pull request red, until the maintainer or his delegate accepts them. Post each head when its checks other than that are green.

## The report

- **No issue covered the work**, so the session opened #257 with the section
  as its proposal, commented when it started, and posted the plan there and
  on #169.
- **The model rule contradicts an accepted decision.**
  `CommitTraceability.ModelNamedInTheRecordNotTheCommit` (ADR 0033's amendment
  of 2026-10-02, accepted that day) refused a model's API identifier in a
  commit message. The ruling of 2026-10-27 reverses it. ADR 0068 point 2 makes
  a change of meaning a supersession, not an amendment, so the plan posted
  first (an amendment and a new key) was corrected on #257: **ADR 0241**
  supersedes that one ruling, and the key moves to 0241's set unchanged in
  name, with the new statement and no `accepted-by` (ADR 0068 point 4).
  0033's Status line names the supersession. 0240 is taken on another branch.
- **What changed:** `AGENTS.md` gains *Issues are the work log* after
  **Every commit**, word for word apart from one added paragraph under
  **Landing**: "the session" is whichever session lands, as the prompt's note
  asks. **Every commit** and **Never** state the ruling. `CONTRIBUTING.md`
  repeated the old rule and is rewritten to match. `CLAUDE.md` only points
  at `AGENTS.md`, and no other instructions file states either rule.
- **Measured:** `eng/decision-sets.cs` passes with 1,468 decisions, 1,467
  accepted; the one unaccepted is this ADR's. The Release build has zero
  warnings. Nothing in code cites the key, so its being unaccepted produces no
  `CS0618`; whether the pull request is red on it is for the gates to say.
- **Found:** `eng/decision-report/baseline.md` is stale on `main`.
  Regenerating it rewrites some 250 lines this change did not cause. It does
  not gate, so it is left for a change of its own: #258.
- **#169 is coordinated, not conflicted with:** no open pull request for it
  touched these lines. Its gate, rule page and port remain its own.
