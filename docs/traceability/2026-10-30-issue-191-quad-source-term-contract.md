# The quad source contract: a denied term is in no quad the source will yield

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#191](https://github.com/Hafeok/Varve/issues/191), after #183 |
| **Date** | 2026-10-30 |
| **Tool** | Claude Code 2.1.314, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS5no4fFRiixjLxkxT` |
| **Branch** | `claude/awesome-hamilton-6pcks7`, from `main` at 87dfe6d |

## The prompt

> Thanks, good write-up. Your audit is #191. Take it as one PR from main: the contract on IQuadSource.TryInternalise as an ADR amendment with a ruling, the audit of every lazily loading or overlaying source with a verdict for each, and the reusable contract test. The gate is in the issue: the test must fail against ReportGraphs with your #186 fix reverted. Post on #171 when the contract test exists, so Inference part 2 builds its sources against it.

## The report

### The contract

ADR 0022 is the ADR that defines `IQuadSource` (the issue names 0017's
family; 0017 is the overlay, defined over 0022's contract). The amendment of
2026-10-30 states that `TryInternalise`'s false means no quad the source will
ever yield holds the term, including quads it has not loaded yet, and that a
source that cannot know yet answers true with a staged handle. The ruling is
`DenialCoversQuadsNotYetLoaded`, filed unaccepted. The interface's
documentation says the same.

Two cases are outside the promise, and the session found the second while
running the test. A blank node, which no source looks up by label (ADR 0044).
And a part of a triple term the source cannot take apart by handle:
`InMemoryDataset` interns a triple term but not its parts, and the test first
flagged its literal `"o"` inside one. That is not a loss: when
`TryGetTripleTermComponents` is false, `BgpOperator.UnifyMaterialised` matches
the externalised triple term by value, and a `Never` position compares its
constant there. The test now walks a triple term's parts only when the source
takes it apart.

### The test

`tests/QuadSourceContract.cs`, linked as `AllocationMeter.cs` is: there is no
test-kit project, and this is the pattern the repository uses for a shared
test helper. `Holds(fresh, probes, complete)` completes one source to collect
the terms it yields. Then, for each term, it makes the source fresh, asks for
that term first, completes it with a match of every quad (and the caller's own
step), asks again, and fails if a denial is among the yielded terms.

The fresh source per term is by design, not found by a failure. Asking for
every term on one source in turn would name the report graph at some point,
and naming it reads the report, waiting. Every later lookup would then find
its terms, hiding the denial that a query asking in the other order sees.

`Varve.Protocol` gains `InternalsVisibleTo` toward `Varve.Protocol.Tests`
(ADR 0003 permits it toward `*.Tests`), so the test composes `ReportGraphs`
itself.

### The gate

- With #186's change to `ReportGraphs.cs` reverted (`git show cd22f08 --
  src/Varve.Protocol/Http/ReportGraphs.cs | git apply -R`),
  `a_report_graph_read_behind_its_projection_never_denies_a_term_its_report_holds`
  fails: "ReportGraphs denied `rdf:type` at its first lookup, and yields a
  quad holding it once loaded". The source is a fresh projection, held back,
  whose bounded wait reaches the head. With the fix restored it passes, and
  so does the case with the projection already there.
- Every source in the audit passes, after one fix.

### The audit, and the one fix

The verdicts are in the amendment's table. One source was found violating the
contract besides `ReportGraphs`: **`PendingSource`**, what a validator reads
under the pending delta. A triple term the commit creates over a part it also
creates was looked up only in the head's dictionary, and denied. The fix
looks a triple term up among the commit's allocations by its parts, each
looked up in the pending source. The contract case fails without it and
passes with it.

It is latent. The session tried to make it cost an answer and could not: a
Reject-mode binding whose `sh:hasValue` is the new triple term, and one whose
SPARQL constraint names it, both committed correctly without the fix. A
query names a triple term by its parts (a nested pattern), which the pending
source already knew, and SHACL's `DataGraph.Resolve` matches an absent
constant by value. Those two tests were dropped, since they do not exercise
the fix, and the amendment says it is latent.

### Not changed

`IndexSource` has no case of its own; it is the source of every view the
test does cover. No citation of the new ruling is added in code: it is
unaccepted, and a citation would be `CS0618` until it is accepted.

### Review: the triple-term exception, by a query

> Good audit, and the PendingSource find is worth having even though it’s latent. One addition before I accept the ruling: prove the triple-term exception with a query, not an argument. Add an evaluator test against InMemoryDataset where the literal exists only inside a triple term, and the query names it inside a triple-term pattern, for example SELECT * { ?s ex:p <<( ?a ex:q "only-here" )>> }. It must return the answer. If it doesn’t, drop the exception: the source answers with a staged handle like any other, and the contract test covers it. Then post the new head.

`EvaluatorTests.A_term_only_inside_a_triple_term_is_found_by_a_triple_term_pattern_naming_it`,
with the optimiser and without. The dataset is checked to deny `"only-here"`
and to be unable to take the triple term apart. `SELECT * { ?s :p <<( ?a :q
"only-here" )>> }` returns `?s=<s> ?a=<a>`, the fully constant pattern
returns `?s=<s>`, and a pattern naming a literal the triple term does not
hold returns nothing. To show the test depends on the path the exception
rests on, the session temporarily made `BgpOperator.UnifyTerm` answer false
for a denied part (`PositionKind.Never`), instead of comparing it by value:
both cases then failed. The change was reverted. The exception stands.

### Acceptance, 2026-10-30

A later session (Claude Code, model `claude-fable-5-1`), on the maintainer's
instruction, added `accepted-by` and `accepted-at` to
`DenialCoversQuadsNotYetLoaded`, the key ADR 0022's amendment of this branch
filed unaccepted. The acceptance is the maintainer's, who had read it and
approved the head beneath; the session only wrote it at their direction.
The amendment's status text is left as filed. The acceptance commit carries
the closing trailer for #191, which no earlier commit of the branch did.
