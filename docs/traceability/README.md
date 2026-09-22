# Traceability

One record per AI-assisted session, as
[ADR 0033](../adr/0033-commit-traceability.md) requires and as the Mind Over
Machine stewardship standard asks:

> **Full Traceability:** All commits on `main` are tied to tracked issues, and
> all generative AI chats/results utilized in development are fully documented
> and tied to issues.

## Why this directory exists

Substantially all of Varve's code and every one of its ADRs were produced in
AI-assisted sessions under the maintainer's direction. That splits the
reasoning behind a decision in two: the ADR records the conclusion and the
alternatives, and the session records how the conclusion was reached — what was
tried, what was measured, and what was rejected before anything was written
down.

A project that keeps only the first half is one where nobody can later ask why
the losing option lost, beyond what the winner chose to write.

## What a record contains

Named `YYYY-MM-DD-issue-N-<slug>.md`, and containing:

- **the prompt** the session was given,
- **the tool and the model**, named exactly,
- **the report** the session returned,
- **the issue** it belongs to, and the commits it produced.

The records name the tool and the model. Prose elsewhere — `README.md`,
`CONTRIBUTING.md`, `GOVERNANCE.md`, the documentation — says "developed with AI
assistance under human review" and names no product. That split is deliberate
and ADR 0033 argues it: a claim about the project ages badly and belongs in no
README, while a fact about a specific piece of work is evidence, and evidence
that will not say what produced it cannot be audited.

## Two kinds of file here

**Session records** (`YYYY-MM-DD-issue-N-*.md`) — one per session that produced
commits in this repository.

**Topic summaries** (`topic-*.md`) — for the design conversations that happened
**outside** this repository: the functional specification, and the positions
the ADRs settled. Each names the decisions it produced and states plainly that
**the transcript is held by the maintainer**, who attaches it. A summary naming
decisions is worth more than nothing and is honest about being less than a
transcript; claiming a record exists when it does not would be worse than
either.

## The backfill is incomplete, by construction

Records dated before 2026-09-22 are **reconstructions** from git history, the
pull request bodies, and the `Claude-Session` trailers — not contemporaneous
records, because no rule required one at the time. Each says so at the top.

Milestones 1 and 2 predate every mechanism that would have recorded them.
Nothing can fix that retroactively, and pretending otherwise would defeat the
purpose of the directory.
