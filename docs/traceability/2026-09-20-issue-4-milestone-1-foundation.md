# Milestone 1 — Foundation

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
| **Issue** | [#4](https://github.com/Hafeok/Varve/issues/4) |
| **Dates** | 2026-09-20 to 2026-09-21 |
| **Tool** | Claude Code |
| **Model** | Claude Opus 5 |
| **Session identifier** | `session_018BjxJ5dCwJr7vHVMfxNyBA` |
| **Commits** | 12 |

## The prompt

**Not recoverable from this repository.** Held by the maintainer.

## What was produced

Repository layout, the Apache-2.0 licence (since superseded by
[ADR 0031](../adr/0031-licence-mpl-2-0.md)), build infrastructure with the
off-the-shelf analyzers at error severity, `Varve.Analyzers` with the layer
rule and its tests, the W3C conformance suites wired in and failing, and the
dependency register turned from a sentence in a document into a gate.

Produced ADRs 0001–0009. ADR 0006 was superseded by 0009 within the same
milestone, when the register replaced the enumerated package table: an ADR that
enumerates will be amended forever.

## Commits

```
d52bad6  chore: repository skeleton
9cd6b42  docs: ADR set for milestone 1, and the Apache-2.0 licence
46409cb  build: repository-wide build infrastructure
f529283  feat(analyzers): VARVE0001 layer direction and VARVE0002 layer declaration
6fab948  test(analyzers): unit tests for both rules, and a real-build fixture
bafb2ad  test(conformance): W3C N-Triples and N-Quads suites, wired and failing
752080f  ci: build and conformance jobs, actions pinned by commit
fed90e2  docs: CLAUDE.md
605c5fa  build: pin the analyzer's Roslyn to the 10.0.100 floor, not the latest
8c2946a  feat(analyzers): close the VARVE0002 loophole
0387bf1  docs: add NOTICE and close the copyright open question
5e6f03a  build: dependency policy as an enforced register
```

## What this record does not contain

The prompt, the alternatives considered and discarded during the work, and the
points at which the maintainer redirected it. The ADRs record which options
lost and why, which is the most important part of that reasoning; the rest is
in the transcript.
