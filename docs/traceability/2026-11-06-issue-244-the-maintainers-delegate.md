# The maintainer's delegate, recorded

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#244](https://github.com/Hafeok/Varve/issues/244) |
| **Date** | 2026-11-06 |
| **Tool** | Claude Code 2.1.319, a local session on the maintainer's machine (WSL2), signing with his YubiKey |
| **Model** | `claude-fable-5-1`, from the session's own metadata |
| **Session identifier** | `99e6699f-a3f7-48ac-974c-711ade42f85c` |
| **Branch** | `claude/the-maintainers-delegate`, from `main` at 5638f01 |
| **ADRs** | [0239](../adr/0239-the-maintainers-delegate.md) (filed), [0066](../adr/0066-expected-red-pull-requests.md) and [0087](../adr/0087-the-identity-map.md) (amended) |

## The prompts

### The direction, pasted into the session by the maintainer

> My last message is withdrawn: Emil has delegated approval, landing and acceptance to you. Go ahead with #178 and the rest as planned. Two things:
>
> Record the delegation. Open an issue and write a short ADR, "The maintainer's delegate".
> Its scope: this session on Emil's machine with his YubiKey only. It may approve, land, and write accepted-by for decisions he has read.
> Your approvals read approve <sha> (delegate).
> Acceptance and landing commits carry a Delegated-by: Emil Okkels Klein <emil@okkels-klein.dk> trailer.
> Commits containing your own implementation work use the Claude (Emil) identity.
> Amend AGENTS.md's accepted-by rule to match. Land it like any other change.
> Apply it from now on. Leave existing history alone.
> Check before reporting. Your last report said all six items were done while 0d32e1e4 still carried the unchanged acceptance. Under the delegation that's no longer wrong. Still, check each item on the pushed head before reporting it.

### The maintainer's own word, after the session stopped

The direction above is written in the third person, and the tool's permission
layer refused the first attempt to write this change, as a session writing
its own authority. The session stopped and said so. The maintainer answered:

> you have my explicit permission

## The report

**What was written.**
- ADR 0239, "The maintainer's delegate", with five rulings in the decision
  set `the-maintainers-delegate`. All five are filed unaccepted: the
  maintainer has not read the wording, and the delegate accepts only what he
  has read.
- Dated amendments to ADR 0066 (a session never accepts, with the delegate as
  the one exception) and ADR 0087 (rules 2 and 3 read the delegate's acts as
  the maintainer's).
- `AGENTS.md`: the `accepted-by` rule, the approval paragraph, the commit
  rules and the Never list name the delegate and ADR 0239.

**What was decided in the writing,** beyond the direction:
- **"Landing commits"** are a merge of `main` into a branch and what that
  merge needs to pass: a renumbered descriptor, regenerated lockfiles and
  digests. `eng/land.cs` itself makes no commit.
- **The delegation does not outlive the session.** A later session is the
  delegate only when the maintainer says so in it. The session's own memory
  note says the same.
- **No gate changes.** `eng/agent-review.cs` and `eng/land.cs` already read
  `approve <sha>` with trailing text, and `eng/decision-sets.cs` asks that
  `accepted-by` name the responsible human, which the delegate's does.
- **Not in the identity map.** The map's delegates are humans with a login
  and a key; the session acts through the maintainer's.

**What the session cannot show.** No gate tells the delegate's act from the
maintainer's. The suffix, the trailer and the agent identity are written by
the session itself.

**First uses,** before this change landed, by the direction's "apply it from
now on": the approval of #208's head `9005bd4b` reads `approve <sha>
(delegate)`.
