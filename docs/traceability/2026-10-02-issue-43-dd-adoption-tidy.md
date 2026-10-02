# Adopting `DecisionDriven.Analyzers`: the tidy after session 3

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer. Developed with AI
> assistance under human review.

| | |
|---|---|
| **Issue** | [#43](https://github.com/Hafeok/Varve/issues/43), the adoption, closed by this pull request |
| **Date** | 2026-10-02 |
| **Tool** | Claude Code 2.1.287, a cloud session |
| **Model** | `claude-opus-5-5`, as the session's metadata reports it; filled in by the maintainer (ADR 0033, amended 2026-10-02) |
| **Session identifier** | `session_014b8nsHnffPP88mVP1cb9F8` |
| **Branch** | `claude/brave-ride-ssydzx`, one pull request |

## The prompts

The first, which this session answered with a read-only pass and no change:

> Adoption session 3 (Refs #43). Sessions 1 and 2 are merged, and so is the VARVE0003 per-member allow-list (#56). The audit of main after #53 stands: Varve.Store and Varve.Sparql.Evaluation already satisfy all 19 DD rules at their defaults with no override; the ADR 0065 wrappers (Position, CommitTimestamp, SegmentId, ByteOffset, ByteCount, QuadCount) are in place; model namespaces are Varve.Store.Log and Varve.Sparql.Evaluation.Model; the report job and Varve.Sparql.Store are done. Verify that in one read-only pass and report any drift before planning; then plan only the remainder below and wait for approval. AGENTS.md applies; one PR; it may be red only on CS0618 per ADR 0066.
>
> Remainder:
>
> 1. Mark the evaluator's per-solution operators [HotPath] in place: Join, LeftJoin, Filter, Union, Minus, Group, Path. Service stays unmarked (I/O). […]
> 2. Mark the store's per-quad write and replay inner loops […]
> 3. The three remaining ints […]
> 4. Close-out […]

The remainder is quoted in full in the session's transcript; it repeats the
prompt of [the session-3 close](2026-10-01-issue-43-dd-adoption-session-3-close.md).

The second, after the pass found it already done:

> Yes: tidy #43 (tick sessions 1 and 2 and both criteria with PR links, close it), fix the circular Tool row, and verify ServiceOperator is outside the [HotPath] mark, all in one docs PR. Add to eng/decision-sets.cs a check that accepted-at is never earlier than the decision's filing date, proven by a failing fixture. Settle the model-naming rule in AGENTS.md and by dated note on ADR 0038: the traceability record names tool and model exactly; commit messages never carry a model id. I correct the six acceptance dates myself.

Four answers the maintainer gave to the session's questions on that prompt:
record ServiceOperator as it is; amend ADR 0033, not 0038; take the filing
date from git history; a "model id" is the API identifier, not the name in a
co-author trailer.

## Report

### The read-only pass

Every claim of the audit held. No `DD` or `VARVE` override anywhere, the six
ADR 0065 wrappers in `Varve.Store.Log` and `Varve.Rdf`, the two model
namespaces. The drift was that **the whole remainder was already done**:
#58 merged it on 2026-10-01 (`c4caef5`, `39641af`, `68c25d0`, `de8147a`,
`94e6cc0`), and `dbd9fde` accepted its seven decisions. Four smaller findings
followed:

1. Six of those seven acceptances are dated 2026-09-29, before the keys were
   filed on 2026-10-01. The maintainer corrects them; a session never writes
   acceptance.
2. #43 ticked only session 3, and named #53 in it where session 3's first part
   was #55.
3. The session-3 records leave the model blank, against ADR 0033.
4. The close-out record's Tool row was circular.

### `ServiceOperator` is inside the `[HotPath]` mark

The prompt said "Service stays unmarked (I/O)". It is not unmarked. `Operator`
is `[HotPath]` as a whole (`Operator.cs:23`), so `ServiceOperator` is held to
`VARVE0003` with every other operator. Its `JoinOne` and `Join` answer the
findings with
`[DesignDecision(typeof(ServiceThroughAHandlerTheDefaultRefuses.ServiceResultsJoined), Scope = ExceptionScope.HotPath)]`,
a key accepted on 2026-09-25 and listed in `evaluation-hot-path-scope.md`.
The maintainer chose to record it as it is. The I/O is excused by an accepted
decision, not left outside the rule, which is the stricter of the two.

### The filing-date rule in `eng/decision-sets.cs`

**An acceptance is never dated before the commit that filed its decision.**
The ledger has no filing field, so the gate reads `git log --follow` per set
file:

- a key is filed by the commit that adds its `- key:` line;
- a key added in the same hunk where another is removed is a rename and keeps
  the earlier filing. `2af9d86` renamed `StoreIsSparqlFree` to
  `StoreReferencesNoSparql` this way, and without the rule it was a false
  finding;
- the keys of an `adr:` set's first commit are exempt. Session 1 transcribed
  them with their ADR's own acceptance date, earlier than the ledger, as ADR
  0062 requires. Without the exemption, 453 keys were false findings. A key
  added to an `adr:` set later, by amendment, is filed then (ADR 0068);
- days are compared as written, each in its own zone, so an acceptance on the
  day of filing passes;
- a shallow clone could not run (exit 2) rather than passing. CI's checkout
  already has `fetch-depth: 0`.

On `main` it reports **exactly the six**, and nothing else:
`BlockingOperatorsHoldTheirInput`, `AllowListAddsEnumeratorAndListReads`,
`ColumnIndexIsAWrapper`, `MaxRecordBytesIsAByteCount`,
`ImplicitTimezoneIsATimeSpan`, `TermLookupsAreHashLookups`.

**Proven by a failing fixture.** `DecisionSetsGateTests` builds small git
repositories with dated commits. The failing case is the real defect: a key
filed on 2026-10-01 and accepted with 2026-09-29. Run against the gate as it was
before this change, three of the five cases fail: the acceptance before
filing, the amendment-added key, and the shallow clone. Against the new gate,
all five pass.

### The model-naming rule

ADR 0038 is the upstream contribution policy, and the rule is ADR 0033's, so
0033 gains a dated amendment (ADR 0068). The record names the model by the
identifier the session's metadata reports. A tool that will not write it says
so in the record, and the maintainer fills it in. A commit message never
carries a model's API identifier, but a co-author trailer naming the product
is allowed. Its key, `CommitTraceability.ModelNamedInTheRecordNotTheCommit`,
is filed unaccepted and cited nowhere in code, so it adds no `CS0618`.
`AGENTS.md` (the record paragraph and *Never*), `CONTRIBUTING.md` and
`docs/traceability/README.md` say the same.

This record is the rule's first case: its Model row is left for the
maintainer, as the amendment provides.

### Gates

The results are in the pull request body, as they were run. The
`decision-sets` job was **red on the six acceptance dates**: the new rule
working on `main`'s own ledger, not a defect of this change. At the
maintainer's instruction, which changed their earlier "I correct the six
acceptance dates myself", the session then set the six `accepted-at` to
2026-10-02, the day of the acceptance commit `dbd9fde` and the date its
seventh key already carries. `accepted-by` is untouched. `decision-sets` then
passes, and the whole pipeline is green.

The prose in those set files still says some of these keys are unaccepted,
or accepted on 2026-09-29. That predates this pull request (`dbd9fde` changed
only the front matter), and it is left for the maintainer.
