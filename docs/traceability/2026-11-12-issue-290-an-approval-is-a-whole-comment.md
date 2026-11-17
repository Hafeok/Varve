# An approval is a whole comment, never edited, and a session never writes it

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#290](https://github.com/Hafeok/Varve/issues/290), parts 1 and 2 |
| **Date** | 2026-11-12 |
| **Tool** | Claude Code, the maintainer's delegate session on his machine (ADR 0239) |
| **Model** | `claude-fable-5-2`, from the session's own metadata |
| **Session identifier** | `99e6699f-a3f7-48ac-974c-711ade42f85c` |
| **Branch** | `fix/approval-is-a-whole-comment`, from `main` at e684009a |
| **ADRs** | [0087](../adr/0087-the-identity-map.md) (amended 2026-11-12, filed unaccepted) |

## The prompt

> Read #290. Build parts 1 and 2 in one pull request: an approval is a comment whose whole body is the approval line with the full hash, an edited comment approves nothing, the check re-runs when a comment is edited or deleted, and one shared implementation serves agent-review.cs and land.cs. Add the tamper cases listed. Post your plan on #290 first. This is in the protected class: the maintainer approves it himself. Never write the approval phrase anywhere; report a head as “head <sha>”.

## The report

- **The plan** was posted on #290 before any code was written.
- **One reader, `eng/lib/Approvals.cs`.** A comment approves a head only when
  its body, trimmed, is the approval line and nothing else, with 40 lowercase
  hexadecimal characters, and its `updated_at` equals its `created_at`. The
  delegate's suffix is allowed. A comment with a timestamp missing approves
  nothing.
- **`eng/agent-review.cs`** reads approvals through it and compares the sha
  with the head for equality. Who wrote the comment is decided as before.
- **`eng/land.cs`** had the old pattern twice, in the warning before a single
  landing and in a train's refusal. Both now go through the reader, reading
  `body`, `created_at` and `updated_at` one comment a line.
- **The gates' messages no longer print the line for a head.** `agent review`
  said what comment it needed by writing the line with the head's sha, in a
  check a session reads; it and `land.cs` now say "an approval comment for
  head `<sha>`".
- **The workflow** runs on a comment created, edited or deleted. For an edit
  the job also reads the body before it (`changes.body.from`), so an approval
  edited into something else still starts a run.
- **Tamper cases**, in `eng/agent-review-tamper.cs`, each judged by the script
  against the stand-in and the posted check read back: the line whole passes,
  with the delegate's suffix and with a trailing newline; inside a sentence,
  in a code span, after another line, with 12 characters of the head, in
  capitals, in an edited comment and with no timestamps it fails; an approval
  then deleted, edited into something else, or edited and still the line,
  posts success and then failure. Two earlier cases flipped from success to
  failure: 12 characters of the head, and the line after "lgtm".
- **`eng/land.cs --fixtures`** gains an `approval` step and eleven cases on
  the same reader. A fixture's body uses `{line}` for the line, so no fixture
  file holds it.
- **Part 2.** `AGENTS.md` says a session never writes the approval line and
  reports a head as "head `<sha>`". The maintainer's `approve.sh` is checked
  in as `eng/approve.sh`, unchanged below its header. `docs/releases.md` and
  the scripts' and workflow's header comments state the new form.
- **ADR 0087** is amended, dated 2026-11-12, and
  `ApprovalIsAConversationComment` is restated without `accepted-by`.
- **Checked against GitHub:** a comment never edited has equal `created_at`
  and `updated_at` (the comments on #286); an edited one does not (one on
  #264); and adding and removing a reaction leaves `updated_at` unchanged
  (tried on this session's own plan comment on #290).

## Questions for the maintainer

- Accept `ApprovalIsAConversationComment` as restated. It is in the protected
  class, so the delegate writes no `accepted-by` for it.
- The rule takes effect when this is on `main`, because the workflow and the
  judging script are main's. Approvals already posted in the old short form
  on open pull requests will stop counting then; today there are none that
  are not the full line.
- `ADR 0239` point 3 still describes the delegate's line by its form. It is
  consistent with the new rule and is left as it is.

## Deferred

- Parts 3 and 4 of #290: an approval as a signed statement, and closing
  `land/**` to cloud sessions. A session that posts the line as a whole
  comment under the maintainer's login is still read as him until part 3.
