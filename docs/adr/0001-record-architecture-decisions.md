# 0001 — Record architecture decisions

## Status

Accepted. 2026-09-20.

## Context

`docs/brief.md` requires an ADR for every decision with alternatives considered
and consequences, and requires that new proposals are checked against accepted
ADRs rather than silently diverging from them. That obligation needs a format
and a lifecycle before there is anything to record, or the first few decisions
will each invent their own.

Two properties matter more than the format itself. A decision must be findable
from the code it governs — which is why every analyzer rule page links to its
ADR, and every banned symbol cites one. And a decision must be revisable without
losing the reasoning that produced it, because the reasoning is what tells a
later reader whether the circumstances have changed.

## Decision

ADRs live in `docs/adr/NNNN-kebab-title.md`, numbered from 0001, allocated in
order, never reused, never renumbered.

Each has five sections, in this order:

- **Status** — one of `Proposed`, `Accepted`, `Superseded by NNNN`, `Rejected`,
  with the date it reached that status.
- **Context** — the forces. What is true that makes this a question.
- **Decision** — what we will do, in the present tense.
- **Alternatives considered** — each with the reason it lost. An ADR with no
  alternatives is a note, not a decision.
- **Consequences** — what this makes easy, what it makes hard, and what it
  obliges us to do later. Costs are stated as plainly as benefits.

Status is `Accepted` when `docs/brief.md` already settles the matter or the
project owner has agreed it; otherwise `Proposed`.

**An accepted ADR is not edited.** To change a decision, write a new ADR whose
Status names the one it supersedes, and edit the superseded one's Status line —
that line only — to `Superseded by NNNN`. The reasoning stays readable as it was
written.

Two conventions carry weight beyond bookkeeping:

- An **open question** recorded in an ADR is a first-class artefact. Where the
  brief contradicts itself, the contradiction is written down under the decision
  it affects and is *not* resolved in passing. ADR 0003 has two of these.
- A decision that binds code is **enforced or it does not bind**. Where an ADR
  can be made into an analyzer rule, ADR 0004 governs how, and the ADR names the
  rule id that enforces it.

## Alternatives considered

- **A single decision log file.** One file, appended to. Rejected: supersession
  becomes an edit to shared text, and the diff of a decision stops being
  reviewable on its own.
- **MADR or Nygard's template verbatim.** Both are close to the above. The
  five-section shape here is Nygard's with *Alternatives considered* promoted
  from a paragraph to a required section, because the brief asks for alternatives
  explicitly and a required heading is harder to skip than a convention.
- **Decisions in code comments or commit messages.** Rejected: neither is
  findable by a reader who has not already found the code, and neither survives
  a refactor of the code it explains.
- **Machine-readable front matter, with a linter.** Rejected for now. It would
  make ADR 0004's own argument — a rule that is not enforced is not a rule — but
  eight ADRs do not need a linter, and the format is not yet settled enough to
  freeze in a schema. Revisit when the count passes roughly thirty.

## Consequences

Every decision costs a file. This is the intended friction: it makes the number
of decisions visible and makes an undecided question obvious by its absence.

Superseded ADRs stay in the directory forever and will accumulate. A reader
scanning `docs/adr/` must read the Status line first. In exchange, a question
that was settled and reopened reads as a sequence rather than as a mystery.

The no-edit rule has one exception in practice: fixing a broken link or a typo
that changes no meaning. Anything that changes meaning is a supersession, and if
it is unclear which one it is, it is a supersession.
