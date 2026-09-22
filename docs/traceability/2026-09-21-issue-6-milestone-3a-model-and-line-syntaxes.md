# Milestone 3a — RDF model, IRI, N-Triples and N-Quads

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
| **Dates** | 2026-09-21 |
| **Tool** | Claude Code |
| **Model** | Claude Opus 5 |
| **Session identifier** | `session_018BjxJ5dCwJr7vHVMfxNyBA` |
| **Commits** | 14 |

## The prompt

**Not recoverable from this repository.** Held by the maintainer.

## What was produced

`Varve.Iri` (layer 0), `Varve.Rdf` (layer 1) and the line-based syntaxes in
`Varve.Turtle` (layer 2), to a full W3C suite pass. The first packable
projects, which is the first time milestone 1's packaging mechanisms stopped
being inert: public API baselines, banned symbols, the Native AOT smoke build
that CI *runs* rather than only publishes, and the WASM smoke build.

Produced ADRs 0024–0027.

**Two findings about the RDF 1.1 N-Triples specification** are recorded in
`docs/spec/n-triples.md`: its `PN_CHARS_U` production contradicts its own test
suite over the colon, and RDF 1.2 has since resolved it the way the suite
already assumed.

## Commits

```
fa71ea8  docs: erasure mode is milestone 9, after SHACL
0c3690c  docs(research): the Varve log is already the write-ahead log
36789a5  docs: drop the email address from NOTICE
7ec6e4b  docs(spec): IRIs, the RDF model, and the N-Triples and N-Quads grammars
a71ef76  docs(adr): 0024 RDF term representation
4a3b8db  docs(adr): 0025 CsCheck, 0026 the HotPath attribute, 0027 benchmarking
5f52f48  feat(iri): Varve.Iri — RFC 3987 validation and RFC 3986 resolution
28a50f4  feat(rdf): Varve.Rdf — the term model and the quad source contract
4c851ae  feat(turtle): Varve.Turtle — the N-Triples and N-Quads reader and writer
9316bc0  test(conformance): the adapter, the full baseline, and ratchet exemptions
676eae1  test: zero allocation per quad, and the off-the-shelf analyzers proven
b0b2138  test: Native AOT and browser smokes — and ADR 0020 fails its condition
3625edf  perf: benchmarks against dotNetRDF, with the machine stated
6628835  docs: reconcile CLAUDE.md, the roadmap and the spec index with milestone 3a
```

## What this record does not contain

The prompt, the alternatives considered and discarded during the work, and the
points at which the maintainer redirected it. The ADRs record which options
lost and why, which is the most important part of that reasoning; the rest is
in the transcript.
