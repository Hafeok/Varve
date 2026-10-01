# Adopting `DecisionDriven.Analyzers`, session 3 closed — the operators, the store's inner loops, three wrappers

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer. Developed with AI assistance
> under human review.

| | |
|---|---|
| **Issue** | [#43](https://github.com/Hafeok/Varve/issues/43), the adoption; session 3's remainder |
| **Date** | 2026-10-01 |
| **Tool** | Claude Code 2.1.286, a cloud session, the same session as sessions 2 and 3 and as #56 |
| **Model** | Not recorded in the repository: the tool is configured to keep model identifiers out of pushed artifacts. The session's own metadata and the transcript hold it |
| **Session identifier** | `session_012aNo8XhYGVT6rgfvDN69P7` |
| **Branch** | `claude/analyzers-layers-0-2-cl3hky`, restarted from `main` after #57 merged |

## The prompt

> Open the PR for c96f631; I accept its two decisions on the branch and merge. Then finish session 3 in this session:
>
> 1. Mark the operators in place; rewrite as an explicit enumerator only where VARVE0003 finds a per-row allocation that is the iterator's own; file a decision only for a cost that survives. Allocation benchmarks before and after.
> 2. Mark the store's write and replay inner loops as listed; orchestration stays unmarked.
> 3. ColumnIndex wrapper on the result surface; MaxRecordBytes becomes ByteCount; ImplicitTimezoneOffsetMinutes becomes a TimeSpan.
> 4. Full CI with no override, allocation numbers, the session record, close #43's session-3 box, and the close-out items from the prompt (AGENTS.md and CONTRIBUTING.md rule sections, the roadmap line for milestone 6, the union of upstream issues and unaccepted decisions, the argued list of rules whose fallout questions the rule).

"As listed" is the session's plan, which the maintainer approved:

> - LogFormat encoding and decoding: write, replay at open, and Checkpoints.
> - TermDictionary interning.
> - Resolver's per-term resolution.
> - StagingView's per-quad overlay.
> - Feed's per-commit delivery loop.
> - Dataset's commit orchestration stays unmarked; only its inner loops get marks.

## Report

### The state, in one paragraph

Session 3 of #43 is done. Every operator in the evaluator is held to `VARVE0003`
through `Operator`, marked as a whole. Six per-row or per-left-solution
allocations it found are gone. Two costs that remain are filed as decisions.
The store's inner loops are marked: the quad encode and decode loops, the
log reader, interning, a request's term resolution, staging, and the
subscription filter. Its orchestration is not. `SolutionResults` takes a
`ColumnIndex`, `DatasetOptions.MaxRecordBytes` is a `ByteCount`, and the
implicit timezone is a `TimeSpan`. **The pipeline is red on `CS0618` alone**,
at 25 citations of the three keys this session filed and cited. With `CS0618`
kept from failing the build, every job passes.

### 1. The operators

`Operator` is marked `[HotPath]` as a whole, so every override of `Open` is
checked, with the iterator it is and the helpers it calls. The first build
reported 108 `VARVE0003` findings. They sorted into four groups.

**Unmarked plumbing, now marked.** This covers the operators' own properties,
`Substitutable`, `ScansBindGraph`, `GroupKeySpec`, `AggregateSpec`'s three
read properties, and `Semantics.ToRef`, `.OrderCompare`, `.Rank` and
`.CompareTerms`. It also covers `Exec.SameSolution` and `.IsNamedGraph`,
`XsdInteger.Zero`, and `Solutions.Empty`, which is now one stateless
`NoSolutions` and not an empty array's enumerator.

**Per-row allocations, fixed:**

| Where | Was | Now |
|---|---|---|
| `GroupOperator.Open` | a new `TermRef[]` key per solution | written in place in the group table's probe; copied only for a new group |
| `OrderByOperator.Open` | a new `Value[]` of keys per solution | the keys side by side, in chunks that double in size and are never copied |
| `OrderByOperator.Open` | a lambda capturing the keys, per `Open` | the sort table is the `IComparer<int>` |
| `GROUP_CONCAT` | `Encoding.UTF8.GetBytes(separator)` per solution | encoded once per accumulator |
| hash join, hashed `OPTIONAL` | a new empty list for a probe with no bucket | none; `Candidates` returns `null` |
| `BgpOperator.Open` | a new `BgpCursor`, six arrays and a `ScanCursor` per pattern, per `Open`: per left solution under a bind join | the cursor is handed back on `Dispose` and restarted; one per execution |

The last is the explicit-enumerator case the instruction named. `BgpCursor`
was already an explicit enumerator. What `VARVE0003` found was that it was
made per `Open`, and the bind join opens its right side per left solution.
It now has an owner, a `Restart`, and a `Dispose` that hands it back. The
empty BGP (`{}`) was `Solutions.Once`, an array and an array enumerator. It is
now the same cursor with no patterns, so it is reused too.

**Interfaces, admitted by member.** Every operator advances its input through
`IEnumerator.MoveNext` and `IEnumerator<T>.Current`. `.editorconfig` now
admits those two members and `List<T>`'s indexer and `Count`, by member
(`HotPathScope.AllowListAddsEnumeratorAndListReads`, filed). It is not cited
from code, so it is not a `CS0618`.

**Costs that survive, filed:**

- `EvaluationHotPathScope.OperatorStateIsMadeOncePerExecution`, cited at five
  sites: the first BGP cursor of an execution, the join keys (cached on the
  operator; they were a `List` and an array per hash join), `VALUES`'
  resolved rows, a `GRAPH` IRI's handle (looked up per `Open` before; now once
  per execution), and the named-graph list.
- `EvaluationHotPathScope.BlockingOperatorsHoldTheirInput`, cited at eleven
  sites. It covers what `DISTINCT`, `ORDER BY`, `MINUS`, a hash join's right
  side, a `DISTINCT` aggregate and `GROUP_CONCAT` hold before they answer.

Accepted keys answer the rest. `ServiceResultsJoined` covers `SERVICE`'s
handler call. `ClosuresByAlp` covers the path search. `HashAggregationWithoutSpill`
covers `GROUP`'s table. `ExtensionFunctionsAreTheCallersCode` covers a custom
aggregate. `TermBuildingExpressionsAllocate` covers a bound computed term.
`SolutionCostsItsRow` covers `Rows.Merge` and `Exec.NewRow`.
`FromNamedIsASetLookup` and `ScanOpensTheSourcesCursor` cover the two halves
of `IsNamedGraph`.

**Correctness.** The cursor reuse is the change with a correctness risk: a
cursor disposed twice after being handed out again would close its next
user's scans. Every consumer disposes once, through `using`, and an operator
tree is compiled per evaluation. Conformance is 6,018 of 6,018 and the ratchet
is unchanged at 2,803.

### Allocation, before and after

Each run was in-process BenchmarkDotNet with `--memory`, 3 warm-up and 8
measured iterations, one invocation each, on the same container, one after
another. "Before" is `c96f631`, built from a worktree. "After" is this branch.
Times are given for scale only: their error bars overlap throughout.

**`FilterBenchmarks`**, one million quads, allocated per query:

| Arm | Case | Before | After |
|---|---|---:|---:|
| InlineAccessor | `eq`, `gt-1%`, `gt-50%`, `gt-99%` | 45.78–45.79 MB | 45.78–45.79 MB |
| InlineAccessor | `order` | 180.78 MB | **145.60 MB** |
| Externalise | `eq`, `gt-*` | 342.56 MB | 342.56 MB |
| Externalise | `order` | 649.44 MB | **614.25 MB** |
| Materialise | `eq`, `gt-*` | 691.62 MB | 691.62 MB |
| Materialise | `order` | 826.62 MB | **791.44 MB** |

- **The filters are unchanged.** Their 48 bytes per row is the solution row
  (`SolutionCostsItsRow`).
- **`ORDER BY` holds 35 bytes less per row:** each row's `Value[]` of keys is
  gone. Collections fell too: on the inline arm, Gen0 from 8,000 to 2,000 and
  Gen1 from 7,000 to 1,000.
- **A first version held the keys in one flat list.** That measured 225.6 MB,
  higher than before, because a list allocates its contents again at every
  doubling. The keys are now in chunks that are never copied (`2013638`).

**`BsbmBenchmarks`**, the two Varve arms, allocated per query:

| Query | Store before | Store after | Dataset before | Dataset after |
|---|---:|---:|---:|---:|
| Q1 | 84.95 KB | 85.43 KB | 68.27 KB | 68.75 KB |
| Q2 | 26.97 KB | 26.02 KB | 27.25 KB | 23.95 KB |
| Q3 (`OPTIONAL`) | 127.43 KB | **110.15 KB** | 97.44 KB | **80.16 KB** |
| Q4 (`UNION`) | 181.84 KB | 184.59 KB | 131.65 KB | 134.41 KB |
| Q5 | 1,782.62 KB | 1,784.11 KB | 1,132.23 KB | 1,133.72 KB |
| Q7 | 41.75 KB | **37.89 KB** | 36.59 KB | **32.73 KB** |
| Q8 | 31.53 KB | **28.60 KB** | 28.44 KB | **25.51 KB** |
| Q10 | 1,854.36 KB | 1,839.74 KB | 1,156.55 KB | 1,141.93 KB |
| Qa | 3,332.32 KB | **3,044.69 KB** | 2,307.55 KB | **2,019.91 KB** |

**The falls are the BGP cursor reused under a bind join.** The largest are
Q3, −17.3 KB on both arms, and Qa, −288 KB. In those queries a BGP is the
right side of a nested join or `OPTIONAL`.

**Q4 rose by 2.8 KB on both arms, and Q1 by 0.5 KB.** Both sort. The likely
cause, not verified, is the chunked layout's floor: the first chunk holds 16
keys, and a sort of a few rows leaves most of it spare. It was not chased
further.

### 2. The store's inner loops

| Marked | What runs per item |
|---|---|
| `LogFormat.EncodeQuads`, `.DecodeQuads` | a commit's quads, 32 bytes each, write and replay |
| `LogFormat.Reader`'s `Remaining`, `Byte`, `UInt32`, `UInt64`, `Bytes`, `Fixed`, `End` | every field read at replay and in a checkpoint's dictionary check |
| `TermDictionary.TryFind`, `TermIds.TryInline`, `.IsProvisional`, `.Inline` | interning a term |
| `Resolver.Resolve` (both) | a request's terms, four per quad |
| `StagingView.Stage`, `.StageTriple`, `.TryInternalise`, `.IsStaged`, `.Known`; `TermView`'s members; `DatasetView.TryInternalise` | staging and looking up a term |
| `RequestTerm`'s four properties | read per term by the resolver |
| `Dataset.Keep` | the subscription filter, per quad of a delivered commit |
| `XsdInteger.IsCanonical`, `XsdBoolean.IsCanonical` | called by `TryInline` |

**Unmarked, as orchestration:**

- opening, appending, and a commit's sequencing;
- `WriteQuads` and `ReadQuads`, which get the span and allocate the commit's
  array around the marked loops;
- a body's term entries (`WriteAllocation`, `ReadAllocation`), which are per
  new term and allocate the term;
- the checkpoint encoder, whose quads are one bulk write per order and whose
  loop is per term;
- the set of ids a delivered commit mentions.

`Checkpoints.cs` gained no mark: it has no per-quad loop of its own, and its
reads go through the marked `Reader`.

**What the marking found.** `TermIds.TryInline` parsed an inline integer with
`Utf8Parser`, which is not on the allow-list. The lexical form is already
checked canonical and at most 18 characters, so it now reads the digits
itself. The delivery filter collected passing quads in two `List<Quad>`, and
now copies them into two arrays sized to the commit.

**Filed:** `StoreHotPathScope.TermLookupsAreHashLookups`, cited at nine
private helpers, one per hash table lookup or insert:

- `TermDictionary.TryFindTerm` and `.TryFindTriple`;
- `Resolver.Label`, `.Triple` and `.Fresh`;
- `StagingView.StageValue`, `.StageParts`, `.TryFindStaged` and
  `.TryFindStagedParts`.

`Varve.Store` now has 66 `[HotPath]` marks, up from 33, and 13
`[DesignDecision]` citations, up from 4. `Varve.Sparql.Evaluation` has 104
marks, up from 69, and 52 citations, up from 22.

### 3. Three primitives

ADR 0069 gains a block dated 2026-10-01 (ADR 0068), and its Status names it.
The three keys are filed unaccepted in `ModelNamespacesForLayers3To5`.

- **`ColumnIndex`** is a `readonly record struct` over `int` in
  `Varve.Sparql.Evaluation.Model`, with a constructor that refuses a negative
  value and `Value`. `SolutionResults.IsBound`, `TryGetHandle` and `TryGetTerm`
  take it. `Varve.Sparql.Store`'s request execution maps variable names to
  `ColumnIndex`.
- **`DatasetOptions.MaxRecordBytes`** is a `ByteCount`. `OpenAsync` refuses
  less than 64 bytes, as before, and more than `int.MaxValue`, which a
  record's length field cannot carry.
- **`EvaluationOptions.ImplicitTimezoneOffset`** is a `TimeSpan`. Its `init`
  refuses an offset that is not whole minutes, or that is outside −14:00 to
  +14:00. The comparisons read the minutes from an internal property set at
  `init`, so the hot path makes no `TimeSpan` call. The specification's
  options table is updated.

**API diff.** `Varve.Sparql.Evaluation`:

- `ColumnIndex` and its record members are added;
- three `SolutionResults` signatures change from `int` to `ColumnIndex`;
- `ImplicitTimezoneOffsetMinutes` is replaced by `ImplicitTimezoneOffset`.

`Varve.Store`: `MaxRecordBytes.get` returns `ByteCount`. All of these are in
`PublicAPI.Unshipped.txt`. Two tests are new: the timezone's range, and a
negative column.

### Gates

**`dotnet run eng/ci.cs`, no override:**

- **`build` fails, on `CS0618` alone.** There are 25 sites:
  `BlockingOperatorsHoldTheirInput` 11, `OperatorStateIsMadeOncePerExecution`
  5, `TermLookupsAreHashLookups` 9. This is the expected red of ADR 0066.
- The jobs before `build` pass: `register`, `licence-headers`,
  `decision-sets`, `banned-symbols`, `issue-refs`, `restore` and
  `native-assets`.

**`CS0618` kept from failing the build** (`WarningsNotAsErrors=CS0618`, the
local override ADR 0066 allows), the same pipeline:

| Job | Result |
|---|---|
| `build` | pass, every other diagnostic an error |
| `test-analyzers` | 96 of 96 |
| `test-iri`, `test-rdf`, `test-turtle`, `test-xsd` | 109, 116, 388, 213 |
| `test-store` | 54 of 54 |
| `test-sparql`, `test-sparql-results` | 36, 1,058 |
| `test-sparql-evaluation` | 28 of 28 (two new) |
| `test-sparql-store` | 13 of 13 |
| `repo-standard-build`, `repo-standard-test` | pass, 89 |
| `conformance` | 6,018 of 6,018; ratchet 2,803, no regression, nothing missing |
| `pack` | pass |
| `decision-report` | pass; baseline refreshed: 652 citations of 71 decisions, from 542 of 65 |

`build-benchmarks` was run apart from the rest, because a benchmark was
running from that project's output. It built with no error as the first step
of the "after" run.

### Decisions filed this session, unaccepted

| Decision | Cited at |
|---|---|
| `EvaluationHotPathScope.OperatorStateIsMadeOncePerExecution` | 5 sites, above |
| `EvaluationHotPathScope.BlockingOperatorsHoldTheirInput` | 11 sites, above |
| `StoreHotPathScope.TermLookupsAreHashLookups` | 9 sites, above |
| `HotPathScope.AllowListAddsEnumeratorAndListReads` | `.editorconfig`; the `VARVE0003` page |
| `ModelNamespacesForLayers3To5.ColumnIndexIsAWrapper` | ADR 0069's amendment; `ColumnIndex`'s remarks |
| `ModelNamespacesForLayers3To5.MaxRecordBytesIsAByteCount` | ADR 0069's amendment |
| `ModelNamespacesForLayers3To5.ImplicitTimezoneIsATimeSpan` | ADR 0069's amendment |

### Every unaccepted decision in the ledger

The seven above, and no others. #57's two keys,
`VarveConfigurationAndHotPathRules.AllowListNamesMembers` and
`HotPathScope.ImmutableArrayAndCancellationTokenByMember`, were accepted on
that branch (`beaabee`) and came in with its merge. Every key sessions 1 to 3
filed before this one has `accepted-by`. `eng/decision-sets.cs`: 79 sets, 522
decisions, 515 accepted.

### Every upstream issue the adoption filed

In [hafeok/decision-driven-analyzers](https://github.com/Hafeok/decision-driven-analyzers).
"Fixed" means a merged pull request references the issue. The issues are
still open there.

| Issue | Finding | State |
|---|---|---|
| [#43](https://github.com/Hafeok/decision-driven-analyzers/issues/43) | the changelog has no 0.1.0-preview.3 section | open |
| [#44](https://github.com/Hafeok/decision-driven-analyzers/issues/44) | the README does not say what `DdLedgerDirectory` does | open |
| [#45](https://github.com/Hafeok/decision-driven-analyzers/issues/45) | the README's quick start omits `IncludeAssets` | open |
| [#46](https://github.com/Hafeok/decision-driven-analyzers/issues/46) | a `[DomainModel]` prefix always includes sub-namespaces | open |
| [#47](https://github.com/Hafeok/decision-driven-analyzers/issues/47) | DD0019 does not see mutation through methods | open |
| [#48](https://github.com/Hafeok/decision-driven-analyzers/issues/48) | rule proposal: a family project must declare `ArchLayer` | open |
| [#49](https://github.com/Hafeok/decision-driven-analyzers/issues/49) | `dd_banned_names` replaces the list, and the docs say "configurable" | open |
| [#50](https://github.com/Hafeok/decision-driven-analyzers/issues/50) | DD0008 reports a package's content files | fixed, #57 |
| [#51](https://github.com/Hafeok/decision-driven-analyzers/issues/51) | `CS0436` across `InternalsVisibleTo` | fixed, #56 |
| [#52](https://github.com/Hafeok/decision-driven-analyzers/issues/52) | a key equal to its set's class name is `CS0542` | fixed, #55 |
| [#53](https://github.com/Hafeok/decision-driven-analyzers/issues/53) | DD0004 reports a `static readonly UTF8Encoding` | fixed, #79 |
| [#54](https://github.com/Hafeok/decision-driven-analyzers/issues/54) | DD0017 and a record's copy constructor | fixed, #58 |
| [#59](https://github.com/Hafeok/decision-driven-analyzers/issues/59) | DD0013 reports `object` overrides | fixed, #65 |
| [#60](https://github.com/Hafeok/decision-driven-analyzers/issues/60) | DD0016 could exempt a wrapper's own `bool` | open, not blocking |
| [#61](https://github.com/Hafeok/decision-driven-analyzers/issues/61) | `[HotPath]` cannot mark an interface | open; Varve marks members |
| [#62](https://github.com/Hafeok/decision-driven-analyzers/issues/62) | DD0013 reports private nested types | fixed, #66 |
| [#63](https://github.com/Hafeok/decision-driven-analyzers/issues/63) | DD0010 offers `[Contract]` for a struct or enum | fixed, #68 |
| [#64](https://github.com/Hafeok/decision-driven-analyzers/issues/64) | DD0016's citation is unreachable on a positional record | fixed, #67 |
| [#69](https://github.com/Hafeok/decision-driven-analyzers/issues/69) | rule request: no `dynamic` in an `ArchLayer` project | open |
| [#71](https://github.com/Hafeok/decision-driven-analyzers/issues/71) | DD0010 checks a `[Contract]` class's internals | fixed, #77 |
| [#72](https://github.com/Hafeok/decision-driven-analyzers/issues/72) | `[HotPath]` cannot mark a constructor | fixed, #75 |
| [#73](https://github.com/Hafeok/decision-driven-analyzers/issues/73) | DD0017 and a `private protected` hierarchy from another assembly | fixed, #76 |
| [#74](https://github.com/Hafeok/decision-driven-analyzers/issues/74) | DD0016 reports `Dispose(bool)` | fixed, #78 |
| [#80](https://github.com/Hafeok/decision-driven-analyzers/issues/80) | DD0004 reports a computed static property | open; reaches only the benchmark |

This session filed none. Its findings are about `VARVE0003`, which is Varve's
own (below).

### Rules whose fallout questions the rule

This session's own, then the earlier sessions', so the list is whole.

1. **`VARVE0003` sees an explicit enumerator's allocation and not an
   iterator's.** `new BgpCursor(...)` per `Open` was reported. The compiler's
   state machine for `FilterOperator.Open`, `JoinOperator.BindJoin` and every
   other `yield` method is the same allocation, once per call, and is not.
   The rule therefore makes a hand-written cursor look worse than an iterator
   that costs the same. Under a bind join, each iterator operator on the right
   side still costs one object per left solution, and nothing but
   `OperatorStateIsMadeOncePerExecution`'s prose records it. The case: report
   a call from a hot path to an iterator method or local function as an
   allocation. That is a change to Varve's own analyzer, and ADR 0064's to
   make.
2. **`VARVE0003` lets a cited container exempt its members' bodies, but not
   make them callable.** `HotPath.IsExempted` walks a member's containers.
   `IsDeclaredHotPathSafe`, which decides whether a callee may be called, looks
   only at the callee and its property. So each BCL call a decision covers
   needs its own one-line private method to carry the citation. This session
   wrote about twenty: `FirstTime`, `Hold`, `TryFindTerm`, `StageValue` and
   the rest.
   Each is honest about where the exception is, but the code reads worse for
   it. The case: one rule for both, so that a citation on a small private
   type admits its members as callees.
3. **The allow-list is repository-wide.** `IEnumerator.MoveNext` is admitted to
   serve the evaluator's operators, and it is now admitted for every hot path
   in every project, where nothing says what implements the enumerator.
   `.editorconfig` can scope the option by path, but a section replaces the
   whole list rather than adding to it. The case: an additive per-path option,
   or allow-list entries that name the assembly they hold for.
4. **`DD0013` sees only model namespaces, so public options and cursors are
   not checked.** `MaxRecordBytes`, `ImplicitTimezoneOffsetMinutes` and
   `SolutionResults`' `int` column were naked primitives on public surfaces.
   No rule reported them, because `DatasetOptions`, `EvaluationOptions` and
   `SolutionResults` are engine types, neither model nor `[Contract]`. The
   audit found them by reading. The case is against the configuration, not the
   rule: a public options type or result cursor is a surface a caller codes
   against, and could be declared as one.
5. **Earlier, each filed upstream:**
   - DD0013 on hand-written equality (#59), which contradicts DD0014;
   - DD0016 on positional records (#64) and `Dispose(bool)` (#74);
   - DD0010 on structs and enums (#63) and a class's internals (#71);
   - DD0017 across assemblies (#73);
   - DD0004 on `UTF8Encoding` (#53) and computed statics (#80);
   - `[HotPath]`'s targets (#61, #72);
   - `dd_banned_names` replacing rather than extending (#49).
6. **Earlier, Varve's own:** ADR 0064's allow-list was too short for span code
   and then for exact arithmetic, and two `HotPathScope` keys widened it.
   `BannedApiAnalyzers` cannot ban the `dynamic` keyword (#69 upstream).

### #43

Its session-3 box is ticked with this pull request. Sessions 1 and 2 merged
as #44–#53. The acceptance criteria hold as follows:

- **Green except `CS0618`.** Holds, above.
- **The conformance ratchet is unchanged.** Holds, at 2,803.
- **The public API diff is wrapper types, namespace moves and attributes.**
  Holds. This session's surface changes are `ColumnIndex`, a `ByteCount` and
  a `TimeSpan`.

### Also changed

- **`AGENTS.md`** has a *Rules: `DD` and `VARVE`* section, and its *Never*
  list names suppressing a `DD` or `VARVE` rule, a session writing
  `accepted-by`, and a local workaround for an analyzer false positive.
- **`CONTRIBUTING.md`**: *Adding a rule* says where a rule belongs, and a new
  *When a `DD` or `VARVE` rule fires* gives the two paths, filing a decision,
  hot paths, and upstream issues.
- **`docs/roadmap.md`**: milestone 6 is written under the rules from its first
  line.
- **The `VARVE0003` page** names the new allow-list key.
