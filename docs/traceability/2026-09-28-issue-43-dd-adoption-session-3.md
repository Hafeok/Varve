# Adopting `DecisionDriven.Analyzers`, session 3 — layers 3 to 5, the report

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer. Developed with AI assistance
> under human review.

| | |
|---|---|
| **Issue** | [#43](https://github.com/Hafeok/Varve/issues/43), the adoption, used by all three sessions |
| **Date** | 2026-09-28 |
| **Tool** | Claude Code 2.1.283, a cloud session, the same session as session 2 |
| **Model** | Not recorded in the repository: the tool is configured to keep model identifiers out of pushed artifacts. The session's own metadata and the transcript hold it |
| **Session identifier** | `session_012aNo8XhYGVT6rgfvDN69P7` |
| **Branch** | `claude/analyzers-layers-3-5`, off `claude/analyzers-layers-0-2-cl3hky`, one pull request of its own (ADR 0066) |

## The prompt

> Pushed the amended acceptance commit with Refs #43 and sign-off; all keys accepted except [none / the ones I name here], for which take the written alternative and remove the citation. Run the no-override build and tests for layers 0–2 and report.
>
> Session 3 goes on a new branch, claude/analyzers-layers-3-5, off this one, with its own PR. Work from issue #43's scope and the findings in #53. Three constraints beyond that: Varve.Store keeps ArchContractTypeAssemblies at the base list, Varve.Sparql is appended only in the layer-3 and integration projects' own files; the Position/TermId wrappers land in the Varve.Store.Log model namespace per ADR 0065 and Varve.Rdf does not learn about positions; and the evaluator's LINQ rewrite is a [HotPath] change, so the zero-allocation benchmark runs before and after and both numbers are in the report. DecisionDriven.Report is a non-gating CI job with its baseline committed. Same acceptance protocol: new decisions filed unaccepted, PR red on CS0618 until I accept, findings against the analyzers filed upstream as issues.

Two answers the maintainer gave during the session, to questions it asked:

> Oversight: I'll accept. *(the two layer 0–2 keys left without `accepted-by`,
> `HotPathScope.GeneratedLabelClaimsAreRecorded` and
> `SparqlAlgebraSurfaces.GrammarKeywordsAreBools`)*
>
> I'll amend and force-push. *(the acceptance commit, `590b031`, which carried
> no `Refs #43`)*

## Report

### The state, in one paragraph

Layers 3 to 5 are under the DD and VARVE rules, to the extent the rules and
the maintainer's open questions allow. The store's log values moved to
`Varve.Store.Log` with ADR 0065's wrappers, and the log's bytes are unchanged.
Every public interface in layers 3 to 5 is a `[Contract]` citing the decision
that made it. The evaluator's hot paths are held to `VARVE0003` down to
`Varve.Xsd`'s numeric, date and duration members, which session 2 had not
marked. `Varve.Analyzers` now holds an override of a hot member to the rule, as
it held an interface implementation, and no longer reports a static
initializer. `DecisionDriven.Report` runs in CI against a committed baseline
and never gates. Four new decision sets and one amendment key are filed
unaccepted. **The branch is not red on `CS0618` alone.** It is also red on
three things that are not this session's to clear:

- 220 `VARVE0003` calls to BCL members the hot-path allow-list does not admit;
- 47 struct-constructor calls that `[HotPath]` cannot mark
  (decision-driven-analyzers#72);
- the `DD0010` question of where layer 3 to 5 contract types live.

Twenty-eight further `DD0017` reports and one `DD0016` report are upstream
false positives. Each is listed below.

### Layers 0 to 2, with no override

On `claude/analyzers-layers-0-2-cl3hky` at `590b031`, the maintainer's
acceptance commit, before its amendment:

- `Varve.Iri`, `Varve.Xsd`, `Varve.Rdf` and `Varve.Sparql.Results` built
  clean.
- `Varve.Turtle` had 4 `CS0618`, on `HotPathScope.GeneratedLabelClaimsAreRecorded`.
- `Varve.Sparql` had 11 `CS0618`, on `SparqlAlgebraSurfaces.GrammarKeywordsAreBools`.

Those two keys were the ones left without `accepted-by`. The maintainer
answered "Oversight: I'll accept". The tests all passed: analyzers 83, Iri
109, Xsd 213, Rdf 116, Sparql.Results 1058, Turtle 388, Sparql 36.

The amended commit had not reached the remote when this record was written.
This branch is on `c929cce`, before the acceptance, so its own build shows the
layer 0–2 keys' `CS0618` too. It takes the amended commit by a merge once that
is pushed. It does not take `590b031`, which the amend replaces.

### Where this prompt and the accepted ADRs disagree

- **"Position/TermId wrappers."** ADR 0065 has no `TermId` type. It says
  "`TermHandle` stays as it is", and it is `Varve.Rdf`'s. This session made
  the five log wrappers ADR 0065 names (`Position`, `CommitTimestamp`,
  `SegmentId`, `ByteOffset`, `ByteCount`) in `Varve.Store.Log`, and touched no
  term identity type.
- **`Varve.Sparql` appended in the integration project's own file.**
  `Varve.Sparql.Store` (layer 5) has no contract that names a `Varve.Sparql`
  type, so its `ArchContractTypeAssemblies` stays at the base list. Appending
  it would have widened a vocabulary nothing uses. `Varve.Sparql.Evaluation`
  already appends it (session 2). `Varve.Store` stays at the base list.
- **`Durability`.** ADR 0065 lists it as engine, in `Varve.Store`. `DD0010`
  reports it on `ISegmentStore.Durability`, because it is a contract member's
  type from outside any model namespace. `DD0010` has no `[DesignDecision]`
  path, and an enum cannot be `[Contract]`. Moving it contradicts ADR 0065's
  explicit list, which ADR 0068 counts as a supersession. It is left red, a
  question below.

### What changed, by commit

| Commit | What |
|---|---|
| `2d991a0` | The log's values move to `Varve.Store.Log` (ADR 0065's list), declared `[DomainModel]` citing `LogIsTheModel` |
| `5eac224` | `Position`, `CommitTimestamp`, `SegmentId`, `ByteOffset`, `ByteCount`. The engine takes them too (`EngineTakesWrappersToo`); `SegmentInfo` gets `Open`/`Sealed` factories in place of a `bool` constructor. A determinism check (old and new tree, same commits) wrote the same log: 9 segments, 16,325 bytes, SHA-256 `4AC6AF12…EBA4` |
| `9e4fc21` | `Varve.Store` under the rules: `[Contract]` on the six abstractions; `BlobName` for the derived store's names; `RequestTerm` moves with `CommitRequest`; `Validators` init-only, `Count` a `QuadCount`; two static caches become factories; the store's hot paths held to `VARVE0003` |
| `2cd5415` | The compiler's ten LINQ calls are loops (`RS0030`); the allocation benchmark is below |
| `2f3cec5` | `Varve.Analyzers`: `VARVE0003` holds overrides of a `[HotPath]` member and skips static initializers; two tests each, 87/87 |
| `90b20e5` | `Varve.Sparql.Evaluation` under the rules; `Expr.Eval` marked, and the value path down into `Varve.Xsd` and `RdfTerm`'s accessors |
| `ef64184` | `ILoadSource` is a contract |
| `80f2520` | `DecisionDriven.Report`: tool manifest, register entry, `eng/decision-report.cs`, the baseline, a CI job, ADR 0063's dated amendment |

### Decisions filed, unaccepted, awaiting the maintainer

Each set's prose gives the alternative, which is what "take the written
alternative" would do.

| Key | Says | Cited on |
|---|---|---|
| `StoreHotPathScope.RunBuildAllocatesTheRunItReturns` | A run's six key arrays are the run, kept as long as the version | `Run.Build` |
| `StoreHotPathScope.PrivateTermComparisonResolvesValues` | With private terms, the comparer resolves a handle to its term per call (spec 1.2 §6) | `StoreTermComparer.SameValue`, `.ValueHash` |
| `StoreLogSurfaces.UnavailableReasonIsDisplayText` | `CommitResult.Reason` is display text, a `string` | `CommitResult.Reason` |
| `EvaluationSurfaces.QueryResultsIsAClosedHierarchy` | `QueryResults` is closed to the assembly, three sealed forms | `QueryResults` |
| `EvaluationSurfaces.AggregateDistinctIsTheKeyword` | `CreateAccumulator`'s `bool distinct` is the grammar keyword | `IExtensionAggregate.CreateAccumulator` |
| `EvaluationSurfaces.OperatorsAreEnumerators` | Operators are `IEnumerator<ulong[]>`; `BgpCursor.Reset` throws as iterators do | `BgpCursor.Reset` |
| `EvaluationHotPathScope.SolutionCostsItsRow` | A solution is a new row, 48 bytes | `Rows.Copy` |
| `EvaluationHotPathScope.ScanOpensTheSourcesCursor` | One source cursor per graph per scan | `ScanCursor.Match`, `.CloseSource` |
| `EvaluationHotPathScope.FromMergeRemembersTriples` | A multi-`FROM` scan remembers what it returned | `ScanCursor.ForgetSeen`, `.FirstTime` |
| `EvaluationHotPathScope.MaterialisedArmOwnsItsTerms` | The materialised arm externalises and interns | `Exec.MaterialiseFromSource`, `.InternaliseLocal` |
| `EvaluationHotPathScope.NestedPatternsExternalise` | A nested triple pattern matches the externalised triple term | `BgpCursor.UnifyExternalised`, `.UnifyTerm` |
| `EvaluationHotPathScope.ConstantsResolveOncePerExecution` | A constant is looked up once per execution | `ConstExpr.Resolve` |
| `EvaluationHotPathScope.TermBuildingExpressionsAllocate` | Functions and casts that make a term allocate it | `FunctionExpr.Eval`, `CastExpr.Eval`, `Terms.Literal(XsdNumeric)` |
| `EvaluationHotPathScope.ExistsRunsItsPattern` | `EXISTS` runs its operator per solution | `ExistsExpr.Eval` |
| `EvaluationHotPathScope.ExtensionFunctionsAreTheCallersCode` | An extension call passes a term array and runs caller code | `ExtensionExpr.Eval` |
| `BuildTimeAnalyzerPackages.ReportIsALocalToolAgainstABaseline` | The 2026-09-28 amendment to ADR 0063 | nothing; its acceptance rests on review |

Two findings were answered by **accepted** decisions without a new key:
`TypedValueAccessorAndTheBenchmarkForAdr0022.FalseMeansNotInline` on
`Exec.Materialise`, and `QuadSourceTermHandle.SourceSuppliesEquality` on the
two comparer helpers in `Exec`.

`SolutionCostsItsRow` deserves a sentence of its own. `FilterBenchmarks`
allocates 45.8 MB in the inline-accessor arm whatever the selectivity: one
48-byte row per BGP solution, 1,000,000 of them, before `FILTER` drops them.
That is allocation per quad scanned in the benchmark's shape. It is the
existing design (`AllocationTests` pins the 48 bytes), and the key records it
rather than hides it. The alternative, a row the consumer returns to a pool,
changes every operator.

### Findings in the two buckets

**Design change taken.**

- The log wrappers.
- `SegmentInfo`'s factories, where there was a `bool` constructor (DD0016).
- `BlobName`, which ADR 0065 left to this session, and `RequestTerm`'s move.
- `CommitRequest.Validators` init-only (DD0019) and `Count` a `QuadCount` (DD0013).
- `LogFormat.Genesis()` and `Runs.NoRetractions()` as factories (DD0004).
- `Terms.Datatypes` an `ImmutableArray` (DD0004).
- The compiler's loops (RS0030).
- `QuadKey`, `TermHandle` and `InlineValue` marked `[HotPath]` as types.
- About ninety members marked `[HotPath]` across Store, Evaluation, Rdf and Xsd.
- Private helpers extracted, so that a citation covers one call and not a
  whole hot method.

**Decision filed.** The table above.

**Left red, not this session's to clear.**

| What | Count | Why |
|---|---:|---|
| `VARVE0003`, a BCL callee outside the allow-list | 220 | `Int128`/`UInt128` operators in `XsdDecimal` (171), `double`/`float` (17), `ReadOnlyMemory<T>` (8), `CultureInfo` (4), `List<T>`/`HashSet<T>`/`ImmutableArray<T>` indexers and lookups, `Nullable<T>`, `CancellationToken`, `ulong.GetHashCode`. Widening `varve_hot_path_allowed_types` is the maintainer's: the session's attempt to add `UInt64`, `ReadOnlyMemory<T>` and `Nullable<T>` was refused by the environment's permission check as a CI bypass, and it did not route around the refusal |
| `VARVE0003`, a struct constructor call | 47 | `[HotPath]` cannot be put on a constructor; decision-driven-analyzers#72. Marking the whole type works where the type is small (`QuadKey`, `TermHandle`, `InlineValue`), and was not done where it would drag string and UTF-16 members in (`XsdInteger`, `XsdNumeric`, `XsdDecimal`, the date types) |
| `DD0010` on `Durability`, `QueryResultKind`, `ServiceRequest`, `ServiceResult`, `LoadedDocument` | 5 | A contract names a type from outside any model namespace. No `[DesignDecision]` path. Needs a model namespace for layers 3 and 5, or ADR 0065's list superseded for `Durability` |
| `DD0010` on `DatasetView`'s and `StagingView`'s internal constructors | 2 | False positive: decision-driven-analyzers#71 |
| `DD0017` over the algebra, from Evaluation and Sparql.Store | 28 | False positive: a `private protected` constructor is not imported from metadata; decision-driven-analyzers#73 |
| `DD0016` on `QueryResults.Dispose(bool)` | 1 | The framework's dispose pattern; decision-driven-analyzers#74 |
| `DD0004` on `FunctionExpr.Utf8` | 1 | `UTF8Encoding`; decision-driven-analyzers#53, open |

### Analyzer issues filed upstream

- [#71](https://github.com/Hafeok/decision-driven-analyzers/issues/71): DD0010
  checks the internal members of a `[Contract]` class.
- [#72](https://github.com/Hafeok/decision-driven-analyzers/issues/72):
  `[HotPath]` cannot be applied to a constructor.
- [#73](https://github.com/Hafeok/decision-driven-analyzers/issues/73): DD0017
  sees a hierarchy closed with a `private protected` constructor as open from
  another assembly.
- [#74](https://github.com/Hafeok/decision-driven-analyzers/issues/74): DD0016
  reports `Dispose(bool disposing)`.

[#53](https://github.com/Hafeok/decision-driven-analyzers/issues/53), filed
before, covers the `UTF8Encoding` report.

### Rules whose Varve fallout argues the rule or its configuration is wrong

- **The hot-path allow-list is too short for exact arithmetic.** ADR 0064's
  seven types, plus the twelve `HotPathScope.AllowListAddsNonAllocatingBclHelpers`
  added, cannot express `XsdDecimal`: it is fixed-point `Int128`
  (`DecimalIsFixedPointInt128`), and every operator on it is a BCL call. None
  of `Int128`, `UInt128`, `UInt64`, `Double`, `Single`, `Nullable<T>` or
  `ReadOnlyMemory<T>.Span` allocates. The case for adding them is the same case
  session 2 made for `Int32`. The collection members (`List<T>` indexer,
  `HashSet<T>.Contains`) are a different case: they don't allocate on lookup,
  but they are containers, and the maintainer may prefer arrays in hot code.
- **`VARVE0003` needed two fixes of its own** (`2f3cec5`). Overrides of a hot
  abstract member were unchecked, which the interface rule already covered. And
  a static property's initializer was reported as though it ran per call.

### API diff

- **`Varve.Store`**: +228 −146 lines of `PublicAPI.Unshipped.txt`.
  - The log types' namespace (`Varve.Store.Log`).
  - The five wrappers.
  - `BlobName`.
  - `SegmentInfo`'s factories.
  - `ISegmentStore`, `IDerivedStore` and `Dataset` members over the wrappers.
  - `Validators` init-only; `Count` a `QuadCount`.
  - `LogVerificationException.Position` a `Position`.
- **`Varve.Sparql.Store`**: two signatures name `Varve.Store.Log.CommitMetadata`
  and `CommitResult` where they named `Varve.Store`'s.
- **Every other package**: attributes only, no signature changes.

### Allocation, before and after the LINQ rewrite

ADR 0050's `FilterBenchmarks`: 1,000,000 quads with inline integer objects,
five cases in each of the three arms. It ran on one machine, a 4-core Intel
Xeon cloud container on the .NET 10 SDK, in one session:

- **before**: at `9e4fc21`, before the rewrite;
- **after**: at `2cd5415`, the rewrite alone;
- **after the evaluator's commit**: at `90b20e5`, the hot-path helpers.

The job was `--warmupCount 3 --iterationCount 8 --invocationCount 1
--unrollFactor 1 --inProcess --memory`. In-process on every side, because
BenchmarkDotNet's rebuild would not carry the local override this branch
needs. Allocated bytes are exact. Times are noisy on a shared container, and
the third run shared the machine with builds.

| Arm | Case | Allocated before | Allocated after | Allocated at `90b20e5` | Mean before | Mean after | Mean at `90b20e5` |
|---|---|---:|---:|---:|---:|---:|---:|
| InlineAccessor | eq | 45.78 MB | 45.78 MB | 45.78 MB | 130.4 ms | 107.7 ms | 103.9 ms |
| InlineAccessor | gt-1% | 45.79 MB | 45.79 MB | 45.79 MB | 117.3 ms | 120.7 ms | 140.6 ms |
| InlineAccessor | gt-50% | 45.79 MB | 45.79 MB | 45.79 MB | 131.4 ms | 123.1 ms | 117.6 ms |
| InlineAccessor | gt-99% | 45.79 MB | 45.79 MB | 45.79 MB | 121.7 ms | 155.0 ms | 130.7 ms |
| InlineAccessor | order | 180.78 MB | 180.78 MB | 180.78 MB | 583.8 ms | 640.5 ms | 635.8 ms |
| Externalise | eq | 342.56 MB | 342.56 MB | 342.56 MB | 237.6 ms | 217.6 ms | 220.1 ms |
| Externalise | gt-1% | 342.56 MB | 342.56 MB | 342.56 MB | 232.9 ms | 256.2 ms | 231.6 ms |
| Externalise | gt-50% | 342.56 MB | 342.56 MB | 342.56 MB | 237.2 ms | 256.3 ms | 224.3 ms |
| Externalise | gt-99% | 342.56 MB | 342.55 MB | 342.56 MB | 238.7 ms | 263.1 ms | 242.9 ms |
| Externalise | order | 649.44 MB | 649.44 MB | 649.44 MB | 2,672.0 ms | 2,918.3 ms | 3,198.5 ms |
| Materialise | eq | 691.61 MB | 691.61 MB | 691.61 MB | 2,046.1 ms | 2,000.0 ms | 2,083.1 ms |
| Materialise | gt-1% | 691.61 MB | 691.61 MB | 691.61 MB | 2,184.7 ms | 2,071.9 ms | 2,048.2 ms |
| Materialise | gt-50% | 691.61 MB | 691.61 MB | 691.61 MB | 2,052.9 ms | 2,153.7 ms | 1,959.2 ms |
| Materialise | gt-99% | 691.61 MB | 691.61 MB | 691.61 MB | 2,139.3 ms | 2,037.8 ms | 2,037.2 ms |
| Materialise | order | 826.63 MB | 826.62 MB | 826.62 MB | 3,835.6 ms | 4,092.1 ms | 3,717.3 ms |

The 0.01 MB differences in two rows are BenchmarkDotNet rounding a figure that sits on a boundary; the bytes are the same.

**Neither the rewrite nor the evaluator's commit changed an allocation.** The
compiler runs once per query, and the rows it compiles are the same; the
helpers the evaluator's commit extracted are calls, not allocations. The inline-accessor arm's 45.8 MB is the
row per BGP solution (`SolutionCostsItsRow`), on both sides.

The zero-allocation tests (`AllocationTests`: a solution costs its 48-byte row,
an unmatched quad nothing, over the dataset and over the store) passed
unchanged at every commit.

### Gates

- **Pass:** `licence-headers`, `decision-sets` (77 sets, 503 decisions, 466
  accepted), `banned-symbols`, `native-assets`, `issue-refs` (8 commits, each
  `Refs #43`), `dependency-register --base` ("DecisionDriven.Report — new, and
  ADR 0063 changed with it").
- **Pass, built under the local survey override** (warnings not errors, the
  DD and VARVE rules reported rather than failing):
  - every test project: analyzers 87, Iri 109, Xsd 213, Rdf 116, Turtle 388,
    Sparql 36, Sparql.Results 1058, Store 54, Sparql.Evaluation 26,
    Sparql.Store 13;
  - conformance: 6018/6018, ratchet 2803, no regressions;
  - the AOT smoke publish and run;
  - the benchmarks' build.
- **`build` with no override: red**, as the state paragraph says. Unique sites
  under the survey build:

| Project | `CS0618` | `VARVE0003` | `DD0017` | `DD0010` | other |
|---|---:|---:|---:|---:|---|
| `Varve.Iri` | 14 | | | | |
| `Varve.Xsd` | 216 | 240 | | | |
| `Varve.Rdf` | 55 | 2 | | | |
| `Varve.Turtle` | 28 | | | | |
| `Varve.Sparql` | 21 | | | | |
| `Varve.Sparql.Results` | 19 | | | | |
| `Varve.Store` | 37 | 7 | | 3 | |
| `Varve.Sparql.Evaluation` | 87 | 18 | 22 | 3 | `DD0004` 1, `DD0016` 1 |
| `Varve.Sparql.Store` | | | 6 | 1 | |

Layers 0 to 2's `CS0618` counts include the keys the maintainer accepted on
the other branch, which this one does not yet have.

### Questions for the maintainer

1. **Where do layer 3 to 5 contract types live?** Five `DD0010` reports:
   - `Durability` (Store);
   - `QueryResultKind`, `ServiceRequest` and `ServiceResult` (Evaluation);
   - `LoadedDocument` (Sparql.Store).

   ADR 0065 answered this for the store's log. For the evaluator and the
   update layer, the same shape would be a model namespace per package, for
   example `Varve.Sparql.Evaluation.Results` and `Varve.Sparql.Store.Model`.
   For `Durability`, it would be a supersession of ADR 0065's engine list.
2. **The hot-path allow-list.** Whether to admit `Int128`, `UInt128`,
   `UInt64`, `Double`, `Single`, `Nullable<T>` and `ReadOnlyMemory<T>`, and
   what to do about collection lookups in hot code.
3. **The new decisions.** Accept, or name the ones whose written alternative
   to take.
