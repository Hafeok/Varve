# Inference, part 2: the inferred quads as a projection

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The plan this part follows
> is [#95's](https://github.com/Hafeok/Varve/issues/95#issuecomment-6437332691),
> split into three parts by the maintainer; part 1's record is
> [2026-10-27-issue-170-inference-part-1.md](2026-10-27-issue-170-inference-part-1.md).
> The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#171](https://github.com/Hafeok/Varve/issues/171), under [#95](https://github.com/Hafeok/Varve/issues/95) |
| **Date** | 2026-11-06 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01NbWSYPyBP5h8tEgWKGf4on`, the session that did part 1 |
| **Branch** | `claude/sleepy-goldberg-91th34-part-2`, from `main` at `282db9d4` (`v0.1.0-preview.18`) |

## The prompts

The work is #171 as the maintainer opened it; the issue body is the
specification. The instruction to start it, after preview.18 was tagged:
"Once preview.18 is tagged, Inference part 2 is next for you."

#171 asked for two facts before any row is built over store handles. This
session reported them in
[#171's plan comment](https://github.com/Hafeok/Varve/issues/171#issuecomment-6683991216):
the dictionary is append-only; erasure keeps handles stable but not the
closure, since private terms compare by value. It also answered the row-level
proposal (comment 6520445643). The maintainer's answer, verbatim:

> Agreed on all three sections. ADR 0195 takes point 2’s wording, plus one
> addition to the precondition: on an Erasure commit, the closure stops
> serving its old generation at once, because its conclusions include data
> derived from the shredded value. Until the new generation reaches the head,
> every tier-2 surface (the views, varve:inferred/…, owl2-rl and explain) is
> unavailable, and readiness says why. That refusal sits at the same point
> where #14 adds its check. Keep “meet or refuse” as an open question owned by
> milestone 9. Note that the maintainer leans towards refusing in part 2
> (preview) and meeting it later. ADRs 0195–0199 are free. Push the branch and
> follow your build order.

## What was done

1. **ADRs 0195–0199, filed unaccepted**, with their decision sets, and dated
   amendments to 0119, 0130, 0172, 0192 and 0194. ADR 0195 point 8 carries
   the erasure precondition with the maintainer's addition, and "meet or
   refuse" as milestone 9's open question. ADR 0196 is filed without its
   measurement, which decides it.
2. **`Varve.Inference`, first slice.** The engine over a source's handles: a
   fact store with its five indices, pattern rules joined from the changed
   fact outwards, insertion through `Propagation`, and delete-and-rederive as
   a second propagation. The RDFS profile is complete; OWL 2 RL/RDF, counting
   and Backward/Forward follow.
   - **The property** (ADR 0195 point 9) holds both strategies to a naive
     from-scratch oracle after every commit of generated histories, schema
     and meta-modelling triples included: 40 × 30–60 commits on a pull
     request, 400 × 100–200 nightly.
   - **A mutation** that drops the rederivation step fails it at commit 5.

## Departures from the plan

- **Not from the plan, proposed here:** ADR 0197's `Varve-Inconsistent`
  response header for a query answered over a graph with a clash, and ADR
  0196 point 5's decision rule (p99 on `owl2rl`, a 20% band). Both are open to
  the maintainer on review.
