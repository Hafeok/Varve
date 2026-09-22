# Milestone 3a close-out

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
| **Issue** | [#6](https://github.com/Hafeok/Varve/issues/6) |
| **Dates** | 2026-09-22 |
| **Tool** | Claude Code |
| **Model** | Claude Opus 5 |
| **Session identifier** | `session_018BjxJ5dCwJr7vHVMfxNyBA` |
| **Commits** | 10 |

## The prompt

**Not recoverable from this repository.** Held by the maintainer.

## What was produced

Closing the defects the milestone's own review found, the most consequential
of which was **A3: a feature shipped with no suite behind it**. The reader and
writer carried RDF 1.2's base direction and triple terms with nothing gating
them, so the `rdf12` N-Triples and N-Quads syntax suites were wired in — 29 and
27 cases — and gating 1.2 found three real bugs, two of which were wrong under
1.1 as well.

Produced ADRs 0028 and 0029.

**ADR 0020's revisit condition fired**, which is the clearest evidence in this
repository that the mechanism works. The cipher decision had been accepted
ahead of the evidence with a stated condition; the condition was tested on a
real browser build and failed — `Aes.Create()` throws on `browser-wasm` and no
symmetric cipher of any kind is available there. 0020 was not edited. 0028
supersedes it with a deterministic AEAD built from HMAC-SHA-256, and 0020 keeps
its text so the reasoning that was wrong stays readable.

## Commits

```
766bec2  test(wasm): probe FixedTimeEquals, and read the table in two halves
4522f73  docs(adr): 0028 — a deterministic AEAD from HMAC-SHA-256, superseding 0020
1020ac3  feat(rdf): the graph position takes a pattern, not a wildcard handle
eda7ba1  test(conformance): gate the RDF 1.2 syntax we shipped, and fix what it caught
0655514  docs: term equality is lexical, and stays that way
702b777  build: make ADR 0009's native-asset scope a gate
360df8e  build: package metadata, an embedded icon, and MinVer
a12e60b  ci: publish on a v* tag, and pack as a dry run on every pull request
944e818  docs: reconcile CLAUDE.md, CONTRIBUTING.md and the roadmap with the close-out
c65c4d0  build: the repository check fails closed, and says why
```

## What this record does not contain

The prompt, the alternatives considered and discarded during the work, and the
points at which the maintainer redirected it. The ADRs record which options
lost and why, which is the most important part of that reasoning; the rest is
in the transcript.
