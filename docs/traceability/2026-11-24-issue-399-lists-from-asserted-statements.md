# The list rules read lists from asserted statements (#399)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim
> below. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#399](https://github.com/Hafeok/Varve/issues/399), completed. Filed from it: [#406](https://github.com/Hafeok/Varve/issues/406) (allocations outside the byte budget) and [#408](https://github.com/Hafeok/Varve/issues/408) (a closure's work is not bounded). Related, not the same cause: [#354](https://github.com/Hafeok/Varve/issues/354). Not related: [#392](https://github.com/Hafeok/Varve/issues/392) |
| **Date** | 2026-11-24 |
| **Tool** | Claude Code 2.1.331, a cloud session. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* This tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYRX4JikPScFa2DSHxt` |
| **Branch** | `claude/list-rules-399`, from `main` at 693c328a |
| **ADRs** | [0197](../adr/0197-owl-2-rl-and-rdfs-as-rules.md) amended 2026-11-24, unaccepted |

## The prompts

The session is Inference part 2's (#171, landed as #341). It reported, on
#341, that the property test `a_closure_over_a_tbox_beneath_equals_from_scratch_at_every_commit`
runs out of memory in the list rules with seed `akRsD4X_QWj2`. The maintainer
then wrote, on 2026-11-24:

> #399 is yours, and it is urgent: `main` has a property test in
> `Varve.Inference.Tests` that runs out of memory in the list rules for
> both `Owl2Rl` cases, reproduced with seed eGI5Cz8BVtI1. […]
> 1. Reproduce it with the seed and post on #399 what allocates, with the
>    input's size.
> 2. Answer first: did the closure run under a memory budget? […]
> 3. Check #354 and #392 for the same cause and link them.
> 4. A pull request of its own, from `main`, with the seed as a fixed
>    test that fails before the fix. No retry, no raised limit, and no
>    skipped case to make the test pass.
> Post the plan on #399 before changing product code.

and then:

> Your report on the list rules is accepted. […] The decision is fix 1,
> with two conditions:
> 1. Lists are read from asserted statements only. A cell is a list cell
>    when the asserted statements give it exactly one `rdf:first` and one
>    `rdf:rest`. An inferred `rdf:first` or `rdf:rest` neither makes nor
>    breaks a list. Say on #399 how this keeps the rules monotone for
>    DRed and for the from-scratch equivalence, and what a change to an
>    asserted list does to a closure.
> 2. A cell that is not treated as a list is reported: the closure's
>    status names it with the reason, and the profile's page says that
>    only well-formed asserted lists are read. Nothing is skipped in
>    silence.
> Also:
> 3. Both seeds (akRsD4X_QWj2 and eGI5Cz8BVtI1) as fixed tests that fail
>    before the fix, plus the one-commit case
>    `ex:next rdfs:subPropertyOf rdf:rest`.
> 4. The budget: list every place in `Varve.Inference` that can allocate
>    in proportion to its input outside the closure's byte budget, and
>    say for each whether it is bounded. […] report any other gap as its
>    own issue.
> 5. A dated amendment to ADR 0197's point on list rules, filed
>    unaccepted, with its ruling cited from the code.
> 6. The entailment and rdf-mt counts before and after, and the ratchet.
> 7. Link #354 and #392 if either shares the cause.

Asked how `eGI5Cz8BVtI1` should go in, once it no longer ran out of memory
but still did not finish, the maintainer answered:

> eGI5Cz8BVtI1 does not go in as a fixed full-history test. After fix 1
> it reads no list, so it is not #399's regression.
> 1. #399's fixed tests are akRsD4X_QWj2 and the one-commit `ex:next`
>    case. Show each failing on `main` and passing with the fix.
> 2. Open an issue with eGI5 as its reproduction: a closure's work is not
>    bounded by the byte budget. […] Link it from #15 as a candidate.
> 3. The property test: split it. […] No wall-clock discard of slow
>    samples. Run each 50 times and report the slowest sample and its seed.
> 4. If shrinking gives a form of eGI5 that finishes in under ten
>    seconds, add it as a fixed case of the second property. If not,
>    leave it on the new issue.
> 5. In ADR 0197's amendment, say plainly that the byte budget bounds
>    what a closure holds and not how long it works, and name the new
>    issue.

## The report

The pull request's description is the report: what allocated, the ruling and
how it keeps the rules monotone, what a change to an asserted list does, the
reporting, the tests and what each did on `main`, the budget audit, the
conformance counts and the ratchet, and the open questions for the
maintainer. The comments on #399, #406 and #408 hold the measurements.
