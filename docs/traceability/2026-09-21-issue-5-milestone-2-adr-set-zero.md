# Milestone 2 — ADR set zero, the log and projection model

> **Reconstructed, not recorded.** No rule required a traceability record when
> this work was done; [ADR 0033](../adr/0033-commit-traceability.md) introduced
> one on 2026-09-22. What follows is rebuilt from the git history and the pull
> request body, and it is evidence of *what was produced*, not of what was asked
> for. The prompts are not recoverable from this repository.
>
> **The session boundaries are not recoverable either.** All 69 non-merge
> commits on `main` carry the same `Claude-Session` trailer, so the history
> cannot say where one session ended and the next began. These records are
> therefore **one per milestone**, which is the finest division the evidence
> supports. The maintainer holds the transcripts and can divide them properly.

| | |
|---|---|
| **Issue** | [#5](https://github.com/Hafeok/Varve/issues/5) |
| **Dates** | 2026-09-21 |
| **Tool** | Claude Code |
| **Model** | Claude Opus 5 |
| **Session identifier** | `session_018BjxJ5dCwJr7vHVMfxNyBA` |
| **Commits** | 13 |

## The prompt

**Not recoverable from this repository.** Held by the maintainer.

## What was produced

`docs/spec/log-and-projection-model.md` as the functional specification and
the authority for `Varve.Store` behaviour, with one ADR per decision it
presupposes — 0010 to 0023 — and the research note on managed storage engines.

Nine open questions (Q1–Q9) were **recorded and deliberately left unresolved**,
each owned by the ADR it falls out of and each with a due milestone. That was
the point of the milestone rather than a shortfall in it: the specification
presupposes decisions that cannot honestly be made before there is an
implementation to measure.

**The design conversation for this set happened outside the repository.** See
`topic-log-and-projection-model.md`.

## Commits

```
b6ac2f0  docs(adr): 0010 commit model and effective deltas
4151df4  docs(adr): 0011 concurrency, one sequencer with optional expected position
03df6da  docs(adr): 0012 term dictionary and id scheme (Proposed, options only)
d0066bd  docs(adr): 0013 records versus commits, and bulk load
51ebae7  docs(adr): 0014 header chain and divergence detection
8e2b654  docs(adr): 0015 checkpoints, pinned reads, as-of reads, archive horizon
67f5170  docs(adr): 0016 projection contract, synchronous default, erasure in projections
693801f  docs(adr): 0017 pre-commit validator contract and the overlay quad source
b6dbef8  docs(adr): 0018 storage abstraction (Proposed, options only)
6320d28  docs(adr): 0019 erasure by crypto-shredding
aae515d  docs(adr): 0020 cipher for erasure mode (Proposed, options only)
9a2377e  docs(research): managed storage engines
922cb65  docs: reconcile the roadmap, CLAUDE.md and both indexes with ADR set zero
```

## What this record does not contain

The prompt, the alternatives considered and discarded during the work, and the
points at which the maintainer redirected it. The ADRs record which options
lost and why, which is the most important part of that reasoning; the rest is
in the transcript.
