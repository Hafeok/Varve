# A report graph read while its projection is behind answers an empty report

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#183](https://github.com/Hafeok/Varve/issues/183); #184 was its duplicate and is closed |
| **Date** | 2026-10-28 |
| **Tool** | Claude Code 2.1.312, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS5no4fFRiixjLxkxT` |
| **Branch** | `claude/awesome-hamilton-6pcks7`, from `main` at b8ac54b |

## The prompt

> Thanks. Yes, take both, each as its own PR from `main`:
>
> 1. #181, the writer-fence test. Reproduce the interleaving deterministically, cite ADR 0136’s text for what the history guarantees, and apply your assertion patch from #177. Then check it against the change #175 made to the same test, and say which is right. #175 will drop its change and take yours.
> 2. #184, the report-graph read. This is a correctness defect: an empty report reads as “everything conforms”. Reproduce it with the projection held back, fix where the read decides it may answer, and add the property in the issue: every report read is `503 validation-behind` or the report equal to `BindingValidation.Validate` at its position.
>
> #181 first, since handover depends on it.

and, interrupting the work on #181:

> Correction to my last reply: don’t take #181. The Replication session found it was a real race in the fence, not only a strict assertion, and is landing the fix itself. Take #183 instead (#184 was its duplicate and is closed), as its own PR from main, with the gate on #183.

## The report

### #181, set down

Before the correction, the session read #175's two commits on the test:
0119abc, which fixes a race in the fence (a lower epoch could take the log),
and its stronger assertion. It found that #175's assertion, which ties each
epoch in the history to the claimant that won it, is right and stronger than
the one this session proposed on #177, which only checked that the history
was gapless, held every live epoch and ended at the holder. No change for
#181 was made or pushed.

### #183: where the read decided it could answer

`ReportGraphs.TryInternalise` names a term the view does not hold, such as
`sh:focusNode`, by reading the reports the projection has reached, **without
waiting**. A report the projection had not reached yet was skipped, and the
term was answered as absent. A query names its terms before it matches its
patterns, so it then decided that the pattern
`GRAPH <report> { ?r sh:focusNode ?focus }` matched nothing. The report graph
was matched and waited for afterwards, and the wait succeeded, but the answer
was already empty: a `200` with no rows, which reads as "everything
conforms". ADR 0174 allows only the report at the position, or
`503 validation-behind` after one bounded wait.

So it needs a projection that is behind at the lookup and reaches the
position during the wait. A projection that never reaches it was already
`503`, because the waiting read throws. That is exactly a projection a commit
or two behind under load, which is why it showed about once in 200 runs.

**Evidence.**
- With temporary diagnostics (removed before commit), the class ran four
  copies at a time until a failure, in round 21 (run 81). The failing run's
  report read was `wait=True`, with the report holding one result, and no
  successful `wait=False` read before it. Passing runs read the report
  `wait=False` first.
- `a_query_of_terms_only_the_report_holds_answers_the_report_once_the_wait_reaches_it`
  holds a real projection back and reaches the head in the read's one
  bounded wait, as the server's adapter does. It answered `"focus\r\n"`
  every run before the fix.

**The fix.** When a term is not found and a report is still unread, the term
is named by a staged handle: the same handle the report stages it to when it
is read, since staging a term is idempotent. It matches nothing in the data,
and a match of the report graph still goes through `Need`, which makes the
waiting read, or is `validation-behind`. Blank nodes are left as they were:
a report's blank nodes are fresh per read and are never named by a query.
`ReportReadOnFirstNeed` still holds, because naming a term reads no report.

### The property

`every_report_read_is_validation_behind_or_the_full_validation_at_its_position`
generates up to five data commits. It holds a fresh projection back to a
generated position, and lets its bounded wait reach the head or not. It then
makes one of four reads:

- the graph-store GET of the report;
- a CONSTRUCT of the whole report graph;
- a CONSTRUCT whose predicate only the report holds;
- the conformance flag.

It is `503 validation-behind` exactly when the projection stays below the
position read. Otherwise the returned graph equals
`BindingValidation.Validate` at that position, under RDFC-1.0. It runs 100
samples. Without the fix it fails, shrinking to the read whose predicate
only the report holds, which answers an empty graph.

### The gate

- **The deterministic reproduction** fails before the fix and passes after.
  So does the property.
- **1,000 runs under contention, no failure.** The server test ran four
  copies at once, 250 rounds, on a 4-core cloud container, after the fix.
  Before the fix, the same harness failed in round 21 (run 81).
