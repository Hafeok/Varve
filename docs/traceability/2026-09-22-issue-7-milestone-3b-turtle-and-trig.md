# Milestone 3b — Turtle and TriG

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
| **Issue** | [#7](https://github.com/Hafeok/Varve/issues/7) |
| **Dates** | 2026-09-22 |
| **Tool** | Claude Code |
| **Model** | Claude Opus 5 |
| **Session identifier** | `session_018BjxJ5dCwJr7vHVMfxNyBA` |
| **Commits** | 15 |

## The prompt

**Not recoverable from this repository.** Held by the maintainer.

## What was produced

Turtle and TriG, reader and writer, to a full suite pass: **883 of 883**
cases, `baseline/exemptions.txt` empty. Produced ADR 0030.

Two things it delivered that were not asked for:

- **The chunk-boundary oracle.** Parse every manifest input whole, then again
  split at each byte offset, and require the same answer. It found **nine
  defect classes**, two of which produced *wrong quads rather than errors*, and
  one of which only the Windows run could see. It is now a standing rule for
  every syntax package, enforced by a guard rather than remembered.
- **ADR 0007's exit criterion, met two milestones early.** The conformance
  harness reads its own manifests with `Varve.Turtle`, and a test rather than a
  convention keeps another RDF implementation out of it.

The Windows-only defect is worth recording as a finding about the *process*
rather than the code: the W3C files checked out with LF on Linux and CR LF on
Windows, so the two platforms were testing different bytes. That is a
conformance result depending on how git is configured, and
[ADR 0036](../adr/0036-containerised-development.md) is the response.

## Commits

```
81a07a3  docs(adr): 0030 — Turtle's recovery unit, prefixes, and isomorphism
94034ae  feat(turtle): the Turtle and TriG reader
dcc1ad7  test(conformance): a chunk-boundary oracle, and what it found
7ca2804  feat(turtle): the Turtle and TriG writer
8dcc3d7  feat(turtle): blank node naming is idempotent, and the oracle is a rule
ebc43ff  feat(conformance): evaluation tests and dataset isomorphism
d409d9a  feat(turtle): four grammar gaps closed; the suites pass 883 of 883
0a66031  fix(turtle): no RDF 1.2 syntax is accepted, and that is now checked
f66944f  refactor(conformance): the harness reads its manifests with Varve.Turtle
f4a7b0e  test(turtle): zero bytes per quad, and Turtle under AOT and in a browser
eefa775  test(turtle): property tests over generated documents; ADR 0028 condition 1
f361b17  bench(turtle): the numbers, and the trap in reading them per quad
f54ebec  feat(turtle): TurtleReader, and the suite reads every case both ways
46bcc3b  docs: the counts the milestone moved, and the claim the pull reader owed
7e4589e  fix(turtle): CR LF split between its two bytes is one line, not two
```

## What this record does not contain

The prompt, the alternatives considered and discarded during the work, and the
points at which the maintainer redirected it. The ADRs record which options
lost and why, which is the most important part of that reasoning; the rest is
in the transcript.
