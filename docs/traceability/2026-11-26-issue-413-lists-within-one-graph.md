# Lists read within one graph, a header for unread lists, and no rebuild for a list no axiom reads (#413)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim
> below. The maintainer holds the transcript itself.

| | |
|---|---|
| **Issues** | [#413](https://github.com/Hafeok/Varve/issues/413), completed; the follow-up to [#409](https://github.com/Hafeok/Varve/pull/409) and [#399](https://github.com/Hafeok/Varve/issues/399) |
| **Date** | 2026-11-26 |
| **Tool** | Claude Code, a cloud session. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* This tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYRX4JikPScFa2DSHxt` |
| **Branch** | `claude/lists-within-graph-413`, from `claude/readiness-snapshot-412` at 2a88c651, #416's head, whose code it builds on |
| **ADRs** | [0197](../adr/0197-owl-2-rl-and-rdfs-as-rules.md) amended 2026-11-26, and [0119](../adr/0119-headers-and-the-problem-catalogue.md) amended 2026-11-26, both unaccepted |

## The prompts

The rulings are in #413 itself, filed on the maintainer's rulings of
2026-11-25:

> **1. Cross-graph merges read each list within one graph, with the TBox, never across graphs.**
> - One graph's statement never makes another graph's list unread: the graph is the unit of grants.
> - A list whose cells span graphs is unread, with a reason of its own.
> - To say: what this does to the premise sets. Relabelling from scratch should no longer be needed.
>
> **2. A header for unread lists.** An answer under a closure that has unread lists carries a header saying so, as `Varve-Equality: not-applied` does for equality. There is none when there are no unread lists. To say: what it reports for an as-of read.
>
> **3. A list no axiom reads rebuilds nothing.**
> - Measure on REC M what asserting a well-formed `rdf:List` in a data graph costs today when no list rule's axiom refers to it.
> - Restrict the rebuild to a change in a list an axiom reads, and say how an axiom that arrives after its list is handled.
> - Test: such a commit changes no conclusion and rebuilds nothing.

On 2026-11-26 the maintainer wrote on #416's answers: "After #416: #413…
as planned." The plan, with ruling 3's measurement, was posted on #413
before any product code changed.

## The report

**What a list no axiom reads cost** (REC M, OWL 2 RL, 4 cores, heap limit
10 GiB). The commit was a well-formed three-cell list, asserted into one
data graph and then retracted:

| Binding | Before: assert / retract | After: assert / retract |
|---|---|---|
| per graph | 3.7–5.2 s / 3.5–4.0 s | 1.2–1.7 ms / 7.4–17.2 ms |
| cross-graph | 53.0 s, then the binding stopped at its memory | 2.0–9.8 ms / 23.3–32.8 ms |

**The change.**
- A closure rebuilds for its lists only when an axiom it holds names a list
  whose reading changed.
- A cross-graph merge reads each graph's lists from that graph and the TBox
  (`Closure.ReadListsPerGraph`). Graphs that read one head differently each
  give it a list.
- An instance's cells are drawn through a graph that reads them all, in the
  premise sets and in a clash's placement.
- A rebuild relabels only from the changed lists' cells.
- New reason: `spans-graphs`.
- `Varve-Unread-Lists`, as of the answer's position, read from the view
  below the projection's position.

**What was found.**
- **The premise-set calculus did not hold for per-graph readings.** It drew an
  instance's cells through the sets of the cell statements. Another graph that
  asserts the same statements, but does not read them as that list, then
  passed for a set that entails the conclusion. The fixed test
  `a_conclusion_moves_to_another_graphs_reading_when_one_graphs_list_breaks`
  showed it. The cells are now drawn through a graph that reads them.
- **A graph's first list was compared with no lists.** Its reading includes
  the TBox's cells, so every TBox list looked changed. At REC M that rebuilt
  the merge and stopped the binding. It is now compared with the TBox's
  lists. A test fails with the old comparison.
- **The random histories never reached the relabel.** A mutation that removed
  it passed the property test. A fixed test now covers it.
- **The snapshot dropped a default-graph cell.** It is now named as
  `varve:defaultGraph`.

**Tests**, locally, with `CS0618` a warning:
- `Varve.Inference.Tests` 79/79;
- `Varve.Inference.Store.Tests` 22/22;
- `Varve.Protocol.Tests` 211/211;
- the server's inference classes 26/26;
- `Varve.Conformance.Tests`: 12,936 run, the same 40 failures as before, none of them an entailment or `rdf-mt` case.

**Left.** The acceptance of `ListsReadFromAssertedStatements` as amended. It
is filed unaccepted, so the build is red on `CS0618` until then.
