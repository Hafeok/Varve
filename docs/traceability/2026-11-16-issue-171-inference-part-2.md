# Inference, part 2, continued: the answers of 2026-11-16, step 2's end, and the projection's first slice

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). It continues
> [2026-11-06-issue-171-inference-part-2.md](2026-11-06-issue-171-inference-part-2.md),
> the record of the session that stopped on 6 November. The transcript itself
> is held by the maintainer.

| | |
|---|---|
| **Issue** | [#171](https://github.com/Hafeok/Varve/issues/171), under [#95](https://github.com/Hafeok/Varve/issues/95) |
| **Date** | 2026-11-16 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and last served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRX4JikPScFa2DSHxt` |
| **Branch** | `claude/sleepy-goldberg-91th34-part-2`'s eight commits, with `main` merged in (never rebased), pushed as `claude/great-galileo-005xnx` |

## The prompts

The first, verbatim:

> Read AGENTS.md, then #171 with all its comments, including the plan and the
> maintainer's answers of 2026-11-06. The previous session stopped on 6
> November. Its work is on the branch claude/sleepy-goldberg-91th34-part-2:
> eight commits, 190 behind main. Continue from it: merge main in, never
> rebase. First post on #171 what the branch contains, what of the plan is
> done, and what main's changes since then mean for it: row-level access (ADR
> 0238), the memory work (ADRs 0240 and 0244), and ADR 0242. Wait for my
> answer before building further.
>
> ADRs 0195 to 0199 were reserved for this part; check they are still free.
> You are a cloud session, not the delegate: file decisions unaccepted and
> never land. Never write the approval line, for any hash; report a head as
> "head <sha>". The description carries a Completes: line. The co-author
> trailer is exactly Co-Authored-By: Claude <noreply@anthropic.com>. Never
> force-push.

The session posted the status
([comment 6915725081](https://github.com/Hafeok/Varve/issues/171#issuecomment-6915725081)):
the branch was 170 commits behind, not 190; 0195–0199 were free; ADRs 0240,
0242 and 0244 were on open pull requests, not yet on `main`. The maintainer's
answer, verbatim:

> Good status. Answers:
>
> * A: this part adds the cross-graph refusal to `RowPreconditions`, in the
>   same change that admits `varve:crossGraph`.
> * B: neither option. Closed rows are kept in memory up to a ceiling in
>   bytes, oldest out first, like ADR 0240's seek directory cache, with a
>   setting and a gauge. An as-of read older than what is held is computed
>   from scratch, as below the floor. No new blob format for history. Open an
>   issue for durable closure history written through the extent machinery,
>   after 1.0. Measure a from-scratch closure on REC M and state it in ADR
>   0195.
> * C: the refusal is accepted; spilling is not in this part. But a binding
>   stopped by the memory limit must not answer as retryable: say it needs
>   more memory, with no `Retry-After`, and class it as ADR 0242 ruling 2
>   does. Propose the exact problem shape on #171. State the closure's memory
>   per quad in `small-machine.md`.
> * D: drop `binding` from `entailment-not-available`, by a dated amendment to
>   ADR 0238.
> * `Varve-Inconsistent`: accepted, provided readiness reports the
>   inconsistency and explain names the clash. ADR 0196's rule (p99 on
>   `owl2rl`, 20% band): accepted as written.
> * File the validation projection's per-commit growth as its own issue,
>   linked from #222. Add the test that fails if `RowScopedQuadSource` ever
>   forwards a capability.
>
> Amend ADRs 0195, 0198 and 0199 for these, record the answers on #171, then
> continue with your build order. #268 (ADR 0240) is landing now; merge
> `main` once it and #264 are in.

## What was done

1. **Merged `main`** twice, never rebased: through `48a1fd5`, then through
   #268. One conflict, in ADR 0119's amendments, kept both in date order.
2. **Recorded the answers** on #171, with the proposed shape of `503
   inference-unavailable` (comment 6916253220). **Filed #312** (the
   validation projection's per-commit growth, linked from #222) and **#313**
   (durable closure history, after 1.0).
3. **Revised ADRs 0195, 0197, 0198 and 0199** in place (unaccepted), **amended
   0238** by a dated amendment (`entailment-not-available` without `binding`),
   and extended this part's amendment to 0119 with `inference-unavailable`.
   The REC M measurement is stated in ADR 0195 as not yet made.
4. **The row-scoped source's capability test** in `Varve.Rdf.Tests`.
5. **Recursive counting**, the third retraction candidate; the property at
   nightly depth passed (400 histories of 100–200 commits, 7 min 50 s).
6. **The clash checks**: RDFS's `dt-not-type` over its two datatypes, and
   OWL 2 RL/RDF's rules whose head is false.
7. **Management** reads the materialising profiles and their terms, and
   refuses a cross-graph binding over classified graphs while a grant with
   subject terms reads them (answer A).
8. **`Varve.Inference.Store`**, the projection's first slice: intra-graph
   placement, generations, the history ceiling and the memory limit, held to
   an oracle at every position.

## Departures and open items

- A cross-graph binding's projection is a stub, refused with
  `NotSupportedException` and reported on #171.
- Not yet built at this record: the checked state in the local cache, the
  surfaces (the seam, the views, the reserved graphs, the guard, `owl2-rl`,
  explain, readiness, the problem pages), the datatypes `Varve.Xsd`
  recognises under OWL 2 RL, the suites, and the REC M measurements.
