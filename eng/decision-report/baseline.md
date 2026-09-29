# Decision-driven report

DecisionDriven.Report 0.1.0-preview.6 at `d93036f63daf`. Nothing here gates a build: a metric becomes a gate only by a decision that names its threshold and baseline.

## Layers

Instability should fall as the layer does. An assembly marked ⚠ is less stable than something above it.

| Assembly | Layer | Ca | Ce | I | A | D | |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `Varve.Iri` | 0 | 3 | 0 | 0.00 | 0.00 | 1.00 |  |
| `Varve.Xsd` | 0 | 2 | 0 | 0.00 | 0.00 | 1.00 |  |
| `Varve.Rdf` | 1 | 6 | 0 | 0.00 | 0.05 | 0.95 |  |
| `Varve.Sparql` | 2 | 2 | 2 | 0.50 | 0.08 | 0.42 |  |
| `Varve.Sparql.Results` | 2 | 0 | 1 | 1.00 | 0.07 | 0.07 | ⚠ |
| `Varve.Turtle` | 2 | 1 | 2 | 0.67 | 0.00 | 0.33 |  |
| `Varve.Sparql.Evaluation` | 3 | 1 | 4 | 0.80 | 0.08 | 0.12 | ⚠ |
| `Varve.Store` | 4 | 1 | 2 | 0.67 | 0.09 | 0.25 |  |
| `Varve.Sparql.Store` | 5 | 0 | 5 | 1.00 | 0.08 | 0.08 |  |

## Contracts

Members offered against members each caller takes. A contract every caller uses a small part of is a candidate for splitting.

| Contract | Members | Implementers | Callers |
| --- | ---: | ---: | --- |
| `Varve.Rdf.IQuadCursor` | 2 | 6 | `Varve.Rdf.QuadOverlay.Cursor` uses 2 of 2: Current, MoveNext<br>`Varve.Rdf.RdfCanonicaliser.Run` uses 2 of 2: Current, MoveNext<br>`Varve.Sparql.Evaluation.Execution.Exec` uses 2 of 2: Current, MoveNext<br>`Varve.Sparql.Evaluation.Operators.ScanCursor` uses 2 of 2: Current, MoveNext<br>`Varve.Sparql.Store.DefaultGraphView.Concatenated` uses 2 of 2: Current, MoveNext<br>`Varve.Sparql.Store.DefaultGraphView.Regraphed` uses 2 of 2: Current, MoveNext<br>`Varve.Sparql.Store.RequestExecution` uses 2 of 2: Current, MoveNext<br>`Varve.Store.Dataset` uses 2 of 2: Current, MoveNext<br>`Varve.Store.IndexVersion` uses 1 of 2: MoveNext |
| `Varve.Rdf.IQuadSource` | 7 | 7 | `Varve.Rdf.QuadOverlay` uses 7 of 7: Contains, Estimate, Match, TermComparer, TryExternalise, TryGetInlineValue, TryInternalise<br>`Varve.Rdf.RdfCanonicaliser.Run` uses 3 of 7: Match, TermComparer, TryExternalise<br>`Varve.Sparql.Evaluation.Compile.Compiler` uses 1 of 7: TryInternalise<br>`Varve.Sparql.Evaluation.Execution.Exec` uses 4 of 7: Match, TermComparer, TryExternalise, TryInternalise<br>`Varve.Sparql.Evaluation.Expressions.Semantics` uses 1 of 7: TryGetInlineValue<br>`Varve.Sparql.Evaluation.Operators.BgpCursor` uses 1 of 7: TryExternalise<br>`Varve.Sparql.Evaluation.Operators.GraphOperator` uses 1 of 7: TryInternalise<br>`Varve.Sparql.Evaluation.Operators.OrderByOperator` uses 1 of 7: TryGetInlineValue<br>`Varve.Sparql.Evaluation.Operators.PathOperator` uses 1 of 7: TryInternalise<br>`Varve.Sparql.Evaluation.Operators.ScanCursor` uses 1 of 7: Match<br>`Varve.Sparql.Evaluation.Optimisation.Optimiser.Estimates` uses 2 of 7: Estimate, TryInternalise<br>`Varve.Sparql.Evaluation.SparqlEvaluator` uses 2 of 7: TermComparer, TryInternalise<br>`Varve.Sparql.Store.DefaultGraphView` uses 7 of 7: Contains, Estimate, Match, TermComparer, TryExternalise, TryGetInlineValue, TryInternalise<br>`Varve.Sparql.Store.RequestExecution` uses 2 of 7: Match, TryInternalise<br>`Varve.Store.DatasetView` uses 4 of 7: Contains, Estimate, Match, TryGetInlineValue<br>`Varve.Turtle.NQuadsWriter` uses 1 of 7: TryExternalise<br>`Varve.Turtle.TurtleWriter` uses 1 of 7: TryExternalise |
| `Varve.Sparql.Evaluation.IAggregateAccumulator` | 2 | 0 | `Varve.Sparql.Evaluation.Operators.GroupOperator.Accumulator` uses 2 of 2: Add, TryGetResult |
| `Varve.Sparql.Evaluation.IExtensionAggregate` | 1 | 0 | `Varve.Sparql.Evaluation.Operators.GroupOperator.Accumulator` uses 1 of 1: CreateAccumulator |
| `Varve.Sparql.Evaluation.IExtensionFunction` | 1 | 0 | `Varve.Sparql.Evaluation.Expressions.ExtensionExpr` uses 1 of 1: TryEvaluate |
| `Varve.Sparql.Evaluation.IRandomSource` | 1 | 0 | `Varve.Sparql.Evaluation.Execution.Exec` uses 1 of 1: NextBytes |
| `Varve.Sparql.Evaluation.IServiceHandler` | 1 | 1 | `Varve.Sparql.Evaluation.Operators.ServiceOperator` uses 1 of 1: Execute |
| `Varve.Sparql.Store.ILoadSource` | 1 | 1 | `Varve.Sparql.Store.RequestExecution` uses 1 of 1: LoadAsync |
| `Varve.Store.ICommitValidator` | 1 | 0 | `Varve.Store.Dataset` uses 1 of 1: Validate |
| `Varve.Store.IDerivedStore` | 4 | 1 | `Varve.Store.Dataset` uses 4 of 4: DeleteAsync, GetRangeAsync, ListAsync, PutAsync |
| `Varve.Store.IProjection` | 3 | 0 | `Varve.Store.Dataset` uses 3 of 3: ApplyAsync, Position, ResetAsync |
| `Varve.Store.ISegmentStore` | 7 | 1 | `Varve.Store.Dataset` uses 1 of 7: Durability<br>`Varve.Store.LogReader` uses 2 of 7: ListSegmentsAsync, ReadRangeAsync<br>`Varve.Store.LogWriter` uses 4 of 7: AppendAsync, CreateSegmentAsync, FlushAsync, SealAsync |
| `Varve.Store.IStorage` | 2 | 1 | `Varve.Store.Dataset` uses 2 of 2: Derived, Log |

## Model cohesion (LCOM4)

The number of groups a type's methods fall into, where methods sharing a field or calling each other are one group. One is one responsibility.

| Type | LCOM4 | Methods |
| --- | ---: | ---: |
| `Varve.Iri.IpLiteral` | 1 | 7 |
| `Varve.Iri.IriChars` | 9 | 9 |
| `Varve.Iri.IriError` | 1 | 5 |
| `Varve.Iri.IriRef` | 5 | 6 |
| `Varve.Iri.IriResolver` | 1 | 6 |
| `Varve.Iri.IriScanner` | 1 | 9 |
| `Varve.Rdf.CanonicalNQuads` | 1 | 3 |
| `Varve.Rdf.CardinalityEstimate` | 3 | 8 |
| `Varve.Rdf.GraphPattern` | 2 | 7 |
| `Varve.Rdf.InMemoryDataset` | 5 | 7 |
| `Varve.Rdf.InMemoryDatasetBuilder` | 1 | 6 |
| `Varve.Rdf.InlineValue` | 3 | 7 |
| `Varve.Rdf.LanguageTag` | 2 | 17 |
| `Varve.Rdf.Quad` | 1 | 5 |
| `Varve.Rdf.QuadDelta` | 1 | 17 |
| `Varve.Rdf.QuadOverlay` | 1 | 7 |
| `Varve.Rdf.QuadPatterns` | 1 | 1 |
| `Varve.Rdf.RdfCanonicaliser` | 1 | 1 |
| `Varve.Rdf.RdfTerm` | 6 | 10 |
| `Varve.Rdf.RdfTermView` | 1 | 1 |
| `Varve.Rdf.TermArena` | 1 | 24 |
| `Varve.Rdf.TermHandle` | 1 | 5 |
| `Varve.Rdf.TermSpan` | 3 | 7 |
| `Varve.Sparql.Algebra.AlgebraList` | 7 | 7 |
| `Varve.Sparql.Algebra.AlgebraList`1` | 7 | 7 |
| `Varve.Sparql.Algebra.AlgebraNode` | 2 | 2 |
| `Varve.Sparql.Algebra.AlgebraRewriter` | 1 | 69 |
| `Varve.Sparql.Algebra.GraphOrDefault` | 1 | 1 |
| `Varve.Sparql.Algebra.GraphTarget` | 1 | 1 |
| `Varve.Sparql.Algebra.Query` | 1 | 1 |
| `Varve.Sparql.Algebra.SourceSpan` | 1 | 6 |
| `Varve.Sparql.Algebra.SparqlParseError` | 1 | 6 |
| `Varve.Sparql.Algebra.Variable` | 1 | 1 |
| `Varve.Sparql.Evaluation.Model.ServiceResult` | 2 | 2 |
| `Varve.Sparql.Results.Model.ResultsPosition` | 1 | 6 |
| `Varve.Sparql.Results.Model.SparqlResultsError` | 1 | 6 |
| `Varve.Sparql.Store.Model.LoadedDocument` | 2 | 2 |
| `Varve.Store.Log.BlobName` | 1 | 6 |
| `Varve.Store.Log.ByteCount` | 1 | 1 |
| `Varve.Store.Log.ByteOffset` | 1 | 1 |
| `Varve.Store.Log.CommitRequest` | 1 | 5 |
| `Varve.Store.Log.CommitResult` | 6 | 11 |
| `Varve.Store.Log.CommitTimestamp` | 1 | 6 |
| `Varve.Store.Log.Position` | 1 | 7 |
| `Varve.Store.Log.RequestTerm` | 3 | 8 |
| `Varve.Store.Log.SegmentId` | 1 | 6 |
| `Varve.Store.Log.SegmentInfo` | 3 | 7 |
| `Varve.Store.Log.SubscriptionFilter` | 3 | 8 |
| `Varve.Store.Log.ValidationVerdict` | 4 | 8 |
| `Varve.Turtle.Model.ParseError` | 1 | 6 |
| `Varve.Turtle.Model.ParsePosition` | 1 | 6 |
| `Varve.Xsd.DurationLexical` | 4 | 7 |
| `Varve.Xsd.FloatingPoint` | 3 | 9 |
| `Varve.Xsd.Int256` | 2 | 2 |
| `Varve.Xsd.Lexical` | 7 | 10 |
| `Varve.Xsd.SevenProperties` | 3 | 4 |
| `Varve.Xsd.SevenPropertyModel` | 1 | 24 |
| `Varve.Xsd.TryFormatUtf8` | 3 | 3 |
| `Varve.Xsd.TryParseUtf8`1` | 3 | 3 |
| `Varve.Xsd.TryParseUtf8`2` | 3 | 3 |
| `Varve.Xsd.XsdBoolean` | 3 | 16 |
| `Varve.Xsd.XsdDatatypes` | 3 | 4 |
| `Varve.Xsd.XsdDate` | 3 | 18 |
| `Varve.Xsd.XsdDateTime` | 3 | 18 |
| `Varve.Xsd.XsdDayTimeDuration` | 2 | 19 |
| `Varve.Xsd.XsdDecimal` | 4 | 45 |
| `Varve.Xsd.XsdDouble` | 3 | 17 |
| `Varve.Xsd.XsdDuration` | 3 | 16 |
| `Varve.Xsd.XsdFloat` | 3 | 17 |
| `Varve.Xsd.XsdGDay` | 3 | 14 |
| `Varve.Xsd.XsdGMonth` | 3 | 14 |
| `Varve.Xsd.XsdGMonthDay` | 3 | 14 |
| `Varve.Xsd.XsdGYear` | 3 | 14 |
| `Varve.Xsd.XsdGYearMonth` | 3 | 15 |
| `Varve.Xsd.XsdInteger` | 4 | 34 |
| `Varve.Xsd.XsdNumeric` | 1 | 26 |
| `Varve.Xsd.XsdString` | 2 | 2 |
| `Varve.Xsd.XsdTime` | 3 | 14 |
| `Varve.Xsd.XsdYearMonthDuration` | 1 | 18 |

## Citations

542 citations of 65 decisions.

### Decisions with no citation

Implicit somewhere, or dead. The report does not say which.

- `dec:varve/AcceptanceTranscription` — A transcribed acceptance is mailto:emil@okkels-klein.dk at the ADR's date, or at a dated amendment's own date for a ruling the amendment changed
- `dec:varve/AcceptedAdrNotEdited` — An accepted ADR's text is never edited or deleted: a change of decision is a superseding ADR, and additions are dated amendment blocks beside the text
- `dec:varve/AcceptedWhenSettledOrAgreed` — An ADR is Accepted when docs/brief.md already settles the matter or the project owner has agreed it, and Proposed otherwise
- `dec:varve/AccessByKeyId` — Access(K) is every dictionary entry under K, every quad of any commit mentioning one with the positions and timestamps it was asserted and retracted, and the metadata of commits whose agent is under K
- `dec:varve/AccessOmitsOthersAgents` — Agents of other commits appear in Access(K) only when canonical or under K, per GDPR Article 15(4)
- `dec:varve/AccessScopeSetting` — Access scope is AllHistory or Current, stated per request or taken from a dataset setting that defaults to AllHistory
- `dec:varve/AdrFiveSections` — Every ADR has Status, Context, Decision, Alternatives considered and Consequences, in that order, and an ADR with no alternatives is a note, not a decision
- `dec:varve/AdrStatusValues` — An ADR's Status is Proposed, Accepted, Superseded by NNNN or Rejected, with the date it reached that status
- `dec:varve/AdrsNumberedNeverReused` — ADRs live in docs/adr/NNNN-kebab-title.md, numbered from 0001 in order, never reused and never renumbered
- `dec:varve/AgentsMdHoldsTheRules` — The agent rules live in the vendor-neutral AGENTS.md, and CLAUDE.md is one line pointing at it
- `dec:varve/AggregateErrorLeavesUnbound` — An aggregate whose result is an error leaves its binding unbound and never fails the query
- `dec:varve/AggregationFollowsTheAlgebraLiterally` — Aggregation follows SPARQL section 18.5.1 literally: aggregates are extracted once per Group into slots no author can name, and a non-key variable in one reads as SAMPLE
- `dec:varve/AllocationsReachable` — Every id in a commit's alloc is reachable from its A or its metadata, directly or as a component of an entry that is (I3)
- `dec:varve/AllowListAddsNonAllocatingBclHelpers` — The hot-path allow-list also admits the BCL helpers span parsing is written with that neither allocate nor call back into user code: Index, Range, MemoryExtensions, Rune, Utf8, HashCode, Math, Int32, and the throw helpers of ArgumentException, ArgumentNullException, ArgumentOutOfRangeException and ObjectDisposedException
- `dec:varve/AllowListAddsNonAllocatingValueTypes` — The hot-path allow-list admits by type the numeric value types Byte, SByte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Int128, UInt128, Single, Double and Decimal, and Boolean, Nullable of T and ReadOnlyMemory of T, none of which allocates; until VARVE0003 can admit members (Varve issue 56) it also admits ImmutableArray of T and CancellationToken by type, of which a hot path may call only members that do not allocate, and never ImmutableArray's ToArray, Add, AddRange, Insert, InsertRange, Remove, RemoveAt, RemoveAll, RemoveRange, Replace, SetItem, Sort, ToBuilder or its enumeration through IEnumerable of T, nor CancellationToken's Register, UnsafeRegister or WaitHandle
- `dec:varve/AmendmentRulingsCarryTheirDate` — A ruling an amendment adds or changes enters the ledger in the amended ADR's set with the amendment's date
- `dec:varve/AnalyzerNeverRuntimeDependency` — Varve.Analyzers is referenced as an analyzer, never as a library, and never appears in a published dependency list
- `dec:varve/AnalyzerReleaseTracking` — Rules are tracked in AnalyzerReleases.Shipped.md and AnalyzerReleases.Unshipped.md, as RS2008 enforces
- `dec:varve/AnalyzersTargetNetStandard20` — Varve.Analyzers targets netstandard2.0, the one structural exception, because it must load in the compiler and in Visual Studio
- `dec:varve/AngleSharpPinnedTransitively` — AngleSharp is pinned transitively above the version dotNetRdf.Core asks for, which NuGet audit flags, rather than silenced, and leaves with dotNetRdf.Core
- `dec:varve/AnonymousModeIsExplicit` — Anonymous mode is enabled only explicitly, logs a warning on every start, and is enabled by the Aspire integration only in development
- `dec:varve/AppTokenForVarvesWorkflow` — Varve's own repo-standard workflow uses only a GitHub App installation token, and no long-lived credential is added
- `dec:varve/ArchFamilyIsVarve` — ArchFamily is Varve, set once in Directory.Build.props
- `dec:varve/ArchitecturalRulesAreErrors` — A rule that protects the package graph or a constraint of the brief is error severity, not warning
- `dec:varve/AsOfReadFromNearestCheckpoint` — An as-of read at a closed position P is Overlay(K_Q, net(L(Q..P])) for the greatest checkpoint Q at or below P, at cost proportional to P minus Q
- `dec:varve/AsOfReadsStructurallyStable` — As-of reads are structurally stable for ever, and erasure changes only the readability of private terms, at every position at once
- `dec:varve/AtLeastOnceIdempotentByPosition` — Delivery to a projection is at-least-once, and applying a commit at or below the projection's position is a no-op
- `dec:varve/AuthenticationOnlyInTheServer` — Authentication lives in Varve.Server, Varve.Store never receives a principal, claim or token, and the embedded CLI performs none
- `dec:varve/AutocrlfOffEverywhere` — Every workflow checkout and the devcontainer set core.autocrlf=false, so every platform checks out the same bytes
- `dec:varve/BackfillsSayWhatWasReconstructed` — A backfilled record says plainly what was reconstructed rather than recorded
- `dec:varve/BannedDynamicAndReflectionMembers` — The banned-symbols list bans Microsoft.CSharp.RuntimeBinder and the reflection members that inspect or invoke, and not the System.Reflection namespace
- `dec:varve/BannedSymbolEntriesCiteAnAdr` — Every banned-symbols entry cites an ADR in its comment and ends its message with the ADR number, gated in eng/ once session 2 of issue 43 touches the file
- `dec:varve/BannedSymbolsFile` — Banned symbols are declared in eng/BannedSymbols.txt and enforced by BannedApiAnalyzers
- `dec:varve/BelowArchiveHorizonFailsLoudly` — An as-of read or diff below the archive horizon without the archive attached fails explicitly and never returns a partial answer
- `dec:varve/BenchmarkDataReproducible` — A benchmark dataset is generated from a stated seed and generator, never a downloaded corpus
- `dec:varve/BenchmarkDotNetConfined` — BenchmarkDotNet is admitted for a non-packable benchmark project only, where its native and Reflection.Emit dependencies reach no published artifact
- `dec:varve/BenchmarksNeverGate` — Benchmarks are never a gate and are not run in CI, and a number is reported with the machine that produced it
- `dec:varve/BindingDecisionsAreEnforced` — A decision that binds code is enforced or it does not bind, and an ADR that can become an analyzer rule names the rule that enforces it
- `dec:varve/BodyIsAllocThenAssertThenRetract` — A commit's body is alloc, then A, then R, each counted, with A and R sorted by id
- `dec:varve/BotCommitsExempt` — Commits by dependabot[bot] and github-actions[bot], a closed list in the gate's source, are exempt from the issue reference
- `dec:varve/BranchBoundSessionLandsByPullRequest` — A cloud session bound to a development branch lands its work through a pull request the maintainer merges
- `dec:varve/BranchNamesAreTheAuthors` — The branch-naming table is withdrawn, and a short-lived branch's name is its author's business
- `dec:varve/BrowserBackendAtLayer5` — The memory and file backends may live in Varve.Store, and the browser backend is a separate layer 5 package
- `dec:varve/BrowserPrimitivesAsserted` — The browser smoke test asserts on every run which cryptographic primitives the browser has and lacks
- `dec:varve/BulkLoadIsOneCommit` — A bulk load is one logical commit of as many records as it needs, written in bounded memory
- `dec:varve/C14nCasesUnderTheRatchet` — The 82 RDF 1.2 c14n cases are under the ratchet with guard counts of 41 per manifest
- `dec:varve/CallerDisposesThePin` — The caller disposes the pin when the disposable result stream ends, and disposing it earlier is a caller error
- `dec:varve/CallerRunsProjections` — Asynchronous projections catch up and rebuild through the same reader when the caller asks, with no registry and no background task the store owns
- `dec:varve/CanonicalEqualityInTheHarness` — Canonical equality is the conformance harness's comparison for CONSTRUCT, DESCRIBE and update results
- `dec:varve/CanonicalFormIsRdf12s` — Canonical N-Triples is RDF 1.2 N-Triples section 3's form, and canonical N-Quads is the same form plus the graph label
- `dec:varve/CanonicalFormSpelling` — The canonical form puts one space after each term and one LF per line, lowercases language tags with --ltr or --rtl, writes triple terms as <<( s p o )>>, drops xsd:string, and escapes as RDF 1.2 does
- `dec:varve/CanonicalFormsDefinedInXsd` — Varve.Xsd is the one definition of a canonical lexical form, and Varve.Store's inline check calls it
- `dec:varve/CanonicalNQuadsWriterIsInternal` — The canonical N-Quads writer is Varve.Rdf's own and internal
- `dec:varve/CanonicalWritersByteIdentical` — Varve.Rdf's RDFC-1.0 term writer and Varve.Turtle's canonical writer stay two, and a property holds them byte-identical
- `dec:varve/ChainDetectsDoesNotAuthenticate` — The header chain detects accidental divergence; it does not prevent a fork and does not authenticate who wrote a commit
- `dec:varve/ChangeOfMeaningIsSupersession` — An amendment never changes what its ADR decided; a change of meaning is a superseding ADR, and doubt counts as a change of meaning
- `dec:varve/CheckpointIsFoldOfLog` — A checkpoint at P is a fold of L[1..P] and nothing else (I7): immutable, directly queryable sorted runs under derived/, never a log entry
- `dec:varve/CheckpointIsOneMergedRun` — A checkpoint is the runs at P merged to one and written as one derived blob: a versioned header with the position, commit P's header hash and dictionary watermarks, the six key arrays and the dictionary entries
- `dec:varve/CheckpointNamesItsCommit` — A checkpoint whose recorded header hash differs from the log's at its position is ignored as a cache miss
- `dec:varve/CheckpointScannedInPlace` — A checkpoint is scanned in place by the same code as a live run, with no restore step
- `dec:varve/CheckpointsAnyPositionAnyPolicy` — A checkpoint may be created at any closed position, by any policy, in the background
- `dec:varve/CiCsIsThePipeline` — eng/ci.cs runs the jobs CI runs, with --list and --only, and CI runs it inside the devcontainer image
- `dec:varve/CiCsKeptInStepWithWorkflows` — A job added to ci.yml is added to eng/ci.cs too
- `dec:varve/ClaimBasedDatasetPermissions` — Configured claims map to read, write and admin permissions per dataset, and Varve keeps no user store
- `dec:varve/ClassificationGateIsPolicy` — The erasure-mode classification gate is a validator policy in the integration layer; the store provides the hook and takes no view
- `dec:varve/ClassifierContract` — A classifier assigns the term occurrences of pending operations to data subjects over the pinned source, free of SPARQL and SHACL, with derived implementations at layer 5
- `dec:varve/ClosingFlagInTheLog` — The closing flag is in the log, so a copy of log/ made at any moment is a valid log up to its last closed commit
- `dec:varve/ClosureStartsFromBoundEnds` — A closure searches from its bound end, stops at a bound other end, and with both ends unbound starts from every node of the active graph
- `dec:varve/ClosuresByAlp` — Path closures are evaluated by section 18.4's ALP, one reachability search per start node with a visited set, yielding sets of nodes
- `dec:varve/CommitAgentIsATermId` — A commit's agent is a TermId, never an inline string, so the agent can itself be a private term and be erased
- `dec:varve/CommitAgentIsTheTokenSubject` — The commit agent is the caller's stable subject identifier from the token, oid for Entra and sub otherwise, recorded as a term
- `dec:varve/CommitStandsIfProjectionFails` — If the default projection fails after a commit's records are durable, the commit stands and the projection catches up by replay
- `dec:varve/CommitTimestampIsAWrapper` — A commit timestamp is a CommitTimestamp over DateTimeOffset, ordered as I5 requires
- `dec:varve/CommitsReferenceAnIssue` — Every commit on main carries Refs #N or Closes #N in its body, enforced by eng/issue-refs.cs
- `dec:varve/CompositionOverExactChains` — Delta composition has identity (empty, empty) and is associative over chains of exact deltas, including any run of a log, and not over arbitrary deltas
- `dec:varve/CompositionRootFlagAtLayer6` — ArchCompositionRoot is true on every layer 6 project and nowhere else
- `dec:varve/CompositionRootIsTheExecutable` — The composition root, where storage, clock, randomness, load source, service handler and validators are wired, is reserved to layer 6 executables, and a library takes each as a parameter
- `dec:varve/CompositionsLiveAboveStore` — The store defines contracts and compositions such as the SHACL and SPARQL Update integrations live above it, and hosts reference the integrations
- `dec:varve/ConflictIsANormalAnswer` — Conflict(head) is a normal, retryable answer that carries the current head, not an error
- `dec:varve/ConformanceRatchet` — baseline/passing.txt lists the passing test IRIs, and eng/ratchet.cs fails on a regression or a vanished entry and never on an improvement
- `dec:varve/ContractTypeVocabulary` — Contracts name only the BCL, the assembly's DomainModel namespaces, Contract types and Varve.Rdf, Varve.Iri and Varve.Xsd, with Varve.Sparql added in layer-3 project files only
- `dec:varve/ContractVocabularyWidenedPerProject` — Any further widening of ArchContractTypeAssemblies is in that project's own file with its reason, never global
- `dec:varve/ContractsInLowestLayer` — A contract lives in the lowest layer that can define it without knowing its implementers
- `dec:varve/ControlCommitsReachEverySubscriber` — Settings and Erasure commits are delivered to every subscriber regardless of filter, each as itself with its empty delta
- `dec:varve/CounterAllocatedIdClasses` — Canonical, blank and private ids are counters allocated by the sequencer, and canonical ids are injective over terms (I3)
- `dec:varve/CrLfTestedOnOwnDocuments` — CR LF handling in parsers is tested on documents this repository owns, never on whatever a checkout produced
- `dec:varve/CsCheckForProperties` — Property-based tests use CsCheck, a test-only package chosen for having no dependencies
- `dec:varve/DatasetBoundValidators` — DatasetOptions.Validators run on every Data commit in order, before the request's own validators, both seeing the overlay and the delta, and either may reject
- `dec:varve/DatasetLeasePreventsConcurrentOpen` — A lease file in derived/ prevents a CLI and a server from opening one dataset concurrently
- `dec:varve/DateAndGTypesPartialOrder` — xsd:date, xsd:time and the g types compare by the XSD partial order, an indeterminate pair being a type error
- `dec:varve/DateTimeImplicitTimezoneOrder` — xsd:dateTime compares by the implicit-timezone total order, the implicit timezone being an evaluator setting that defaults to UTC
- `dec:varve/DatedAmendmentsAddOnly` — A dated amendment is an add-only block headed with its date that adds detail, records evidence, corrects the ADR's reasoning or states a needed consequence
- `dec:varve/DcoSignOffOnEveryCommit` — Every commit, human or AI session, carries a DCO Signed-off-by naming a person who may contribute the code
- `dec:varve/DecimalIsFixedPointInt128` — XsdDecimal is a fixed-point Int128 with 18 fractional digits that rejects forms needing more, fails on overflow and truncates division toward zero
- `dec:varve/DecisionDrivenPackagesAdmitted` — DecisionDriven.Analyzers and DecisionDriven.Report are admitted as build-time packages with PrivateAssets all, their register entries citing ADR 0063
- `dec:varve/DecisionSetsCheckedInTheBuild` — eng/decision-sets.cs checks the set files' front matter in the build job until the analyzer package's generator reads them
- `dec:varve/DefaultProjectionSynchronous` — The default quad projection is updated before Committed(P) returns, so a pin taken afterwards observes P, and every other projection lags
- `dec:varve/DefaultServiceHandlerRefuses` — The default service handler refuses every endpoint, so the default configuration opens no connection
- `dec:varve/DeliveredAllocationsFiltered` — A delivered commit carries only the allocations its delivered delta and metadata refer to
- `dec:varve/DetachAbsentUntilArchive` — Detach is absent from the storage contract until archive exists
- `dec:varve/DeterminismForCrashFreeHistories` — The determinism property holds for crash-free histories, because a tail recovery abandoned stays in the log
- `dec:varve/DeterministicBuilds` — Builds are deterministic, with ContinuousIntegrationBuild under CI and EmbedUntrackedSources, so two builds of one commit produce the same bytes
- `dec:varve/DeterministicStorageBytes` — Nothing in the storage contract admits ambient time, ambient randomness or iteration-order dependence
- `dec:varve/DevcontainerMirrorsTheCore` — The devcontainer mirrors the stewardship core's template and overlays .NET with the SDK pinned exactly to global.json and the wasm workloads
- `dec:varve/DiffFromTheLogAlone` — Diff(P1, P2) is net(L(P1..P2]), computed from the log alone with no state lookup
- `dec:varve/DifferentialExemptionsChecked` — The differential harness fails on an exemption with no or an unknown category, missing references or the category varve-defect
- `dec:varve/DifferentialTriage` — Every differential disagreement is exactly one of varve-defect, upstream-defect, spec-gap or intentional-divergence, and varve-defect is never exempted
- `dec:varve/DiscardedTailRepresentable` — The storage contract can represent a discarded unclosed tail
- `dec:varve/DistinctAggregateIsASet` — DISTINCT in an aggregate is a set per accumulator keyed by term equality, and COUNT(DISTINCT *) keys on the whole solution
- `dec:varve/DivergenceFoundByComparison` — Two logs with a common prefix and different continuations are divergent, and the branch point is the first position whose chain values differ
- `dec:varve/DocumentationFileGenerated` — GenerateDocumentationFile is on for every project
- `dec:varve/DomainSeparatedGenesis` — At position 1, prev is the SHA-256 of a fixed, domain-separated UTF-8 string rather than zeros
- `dec:varve/DotNetRdfIsTheBaseline` — dotNetRdf.Core is in the register as the benchmark baseline, and nothing in the repository depends on it for an answer
- `dec:varve/DroppingCheckpointLosesNothing` — Dropping a checkpoint loses nothing: it is a cache whose miss is slower, never wrong
- `dec:varve/DurabilityDeclared` — A storage backend declares its durability as Synchronised, Committed or None, and the contract never treats them as equal
- `dec:varve/DurabilityIsALogValue` — Durability, what a storage backend promises once a flush returns, is a value in Varve.Store.Log and not engine in Varve.Store
- `dec:varve/DurableFormatVersioned` — The durable log format carries a version discriminator, so a change to what is hashed is a stated migration rather than a corruption
- `dec:varve/DurationOrders` — Durations compare by the four-reference-dateTime order, partial for xsd:duration and total for the two derived types
- `dec:varve/EarlierAmendmentsRecognised` — The dated amendments made before this ADR are recognised as they stand
- `dec:varve/EffectiveDeltaInvariant` — A commit asserts nothing already present, retracts nothing absent, and never both asserts and retracts one quad
- `dec:varve/EffectiveDeltaNotRequest` — The log records the effective delta against the pinned state, not the request
- `dec:varve/EmptyDeltaNoCommit` — An empty effective delta produces no commit but NoChange(head), with provisional term ids discarded; a Data commit's delta is never empty, Erasure excepted
- `dec:varve/EngScriptsAreFileBasedApps` — eng/ scripts are C# file-based apps run with dotnet run, one implementation for every operating system
- `dec:varve/EngineTakesWrappersToo` — The engine's own members take and return the wrappers too, even where no rule requires it
- `dec:varve/EntraDefaultAnyIssuer` — Entra ID is the default, documented and tested issuer, and any other OIDC issuer works with the same code
- `dec:varve/EraseRetractsByKeyId` — Erase(subject) retracts from G_head every quad mentioning a term under the subject's key unless the caller opts out, appends an Erasure commit carrying the KeyId, agent and cause, then destroys the key
- `dec:varve/ErasureIsCryptoShredding` — Erasure is crypto-shredding: the data stays and the key is destroyed
- `dec:varve/ErasureModeCannotBeTurnedOff` — The sequencer refuses to turn erasure mode off while any private entry exists in the dictionary
- `dec:varve/ErasureModeNotRetroactive` — Turning erasure mode on governs what happens from its position onward and makes no earlier term private
- `dec:varve/ErasureModePerDatasetOffByDefault` — Erasure mode is per dataset, off by default and set by a Settings commit, and costs nothing but a reserved id class when off
- `dec:varve/ErasurePurgeInProjections` — A projection holding plaintext derived from private terms records the KeyId of each entry, keeps it only under derived/, and purges it on that key's Erasure commit
- `dec:varve/ErasureSoundness` — After Erase, every private entry under the destroyed key is unreadable in the log, in every checkpoint and in every copy of the dataset directory made at any time (I10)
- `dec:varve/ErrorsInMinMaxAndSample` — An error makes MIN and MAX an error, and SAMPLE returns a value that is not one, following Oxigraph where section 18.5.1 is silent
- `dec:varve/EstimateCostDocumented` — An estimate's cost is bounded by the source's documentation, and Match is not a permitted implementation in a source that claims to be an index
- `dec:varve/EstimateIsNeverACount` — An estimate counts a source's quads and is never used as the answer to a SPARQL COUNT
- `dec:varve/EstimateOnTheQuadSource` — IQuadSource in Varve.Rdf gains Estimate over the pattern Match takes, returning a CardinalityEstimate that is exact, unknown, or a count the source has reason to believe
- `dec:varve/EstimatedCountIsDocumented` — An estimate that is neither exact nor unknown is a count the source's documentation says how it derived
- `dec:varve/EvaluationOptionsCarryTheOutside` — Everything the evaluator needs from outside, extension functions, custom aggregates, the clock and randomness, arrives in the immutable EvaluationOptions
- `dec:varve/EvaluatorNeverPins` — The evaluator receives a quad source it does not own, never calls Pin() and never disposes what it is given
- `dec:varve/EvaluatorUnderDeterministicBan` — Varve.Sparql.Evaluation is under the ambient clock and randomness ban, as Varve.Store is
- `dec:varve/EveryAdrIsADecisionSet` — Every ADR is enumerated into docs/decisions as an interim set file in ledger namespace varve, one key per ruling in force, with its acceptance transcribed from the ADR
- `dec:varve/ExactEstimateIsExact` — An estimate marked exact equals the number of quads Match would yield for the pattern at the source's current state
- `dec:varve/ExecutableLayerAndRootAgree` — VARVE0005: an executable not at layer 6, a library at layer 6, and an ArchCompositionRoot that disagrees with layer 6 are each reported
- `dec:varve/ExecutablesAreLayer6` — An executable declares layer 6 unless it is a test assembly, only an executable declares layer 6, and benchmark assemblies are layer 6 like any host
- `dec:varve/ExhibitANoticeOnEveryFile` — Every .cs file carries the MPL-2.0 Exhibit A notice as its first three lines, enforced by eng/licence-headers.cs with generated code excepted by pattern
- `dec:varve/ExistingTermsByHandle` — A request addresses an existing blank node, or any existing term, by RequestTerm.Existing(handle), and an unknown handle fails the request with an exception and leaves no trace
- `dec:varve/ExpectedRedOnlyOnCs0618` — A pull request may arrive red only when every failure is CS0618 from citing a decision filed without acceptance, and its body lists those decisions
- `dec:varve/ExternalReviewBeforeShipping` — Nothing presents the construction as protecting data until an external cryptographic review, due before milestone 9 ships, and a rejection means erasure mode does not run in the browser
- `dec:varve/ExternalisedBlankLabelsFromIds` — TryExternalise of a blank id gives a label derived from the id, stable within the dataset and no identity across datasets
- `dec:varve/FailingOperationThrowsBeforeSubmit` — A failing update operation throws before the submit, releasing the pin, and nothing reaches the log or the dictionary
- `dec:varve/FilterRunsInTheReader` — A subscription's filter runs in the reader, before delivery
- `dec:varve/FilteredSkipsKeepTruePositions` — A subscription filter skips a commit whose filtered delta is empty, and the next delivered commit carries its true position
- `dec:varve/FloorRaiseWaitsForAmendment` — A preview that raises the Roslyn floor it declares is not taken until a dated amendment to ADR 0009 raises Varve's
- `dec:varve/ForbiddenOperationsAbsent` — Truncation, positional writes and deletion of a sealed segment are absent from the storage contract's type, not forbidden in prose
- `dec:varve/FreshPathVariablesOutsideVarname` — Variables a path rewrite introduces are named .p0, .p1 and so on, outside VARNAME, so none collides with an author's or is ever projected
- `dec:varve/FullWidthSyntheticIv` — The synthetic IV and tag is the full 32 bytes of HMAC-SHA-256
- `dec:varve/GatesRunAfterThePush` — Every gate runs in CI on every push to main and every pull request without stopping a push, and a red trunk is fixed forward before anything else lands
- `dec:varve/GeneratorsCountTheirCases` — The model's generators count every case they must produce, and a run fails if any case never occurred
- `dec:varve/GeneratorsReviewedAsTests` — A generator is reviewed as carefully as its property, and term generators produce escapes, surrogate pairs, directional language tags, ucschar IRIs and nested triple terms
- `dec:varve/GraphExistsIffItHoldsAQuad` — A named graph exists if and only if it holds a quad: CREATE has no effect and fails without SILENT on a non-empty graph, and DROP and CLEAR retract its quads
- `dec:varve/GraphScopeIsADeclaration` — The named-graph scope in a commit's metadata is a declaration the store records and never enforces, and validators may enforce it
- `dec:varve/GroupConcatComparedAsMultiset` — The optimiser's equivalence property compares GROUP_CONCAT results as multisets of their parts
- `dec:varve/GroupConcatInInputOrder` — GROUP_CONCAT has no ORDER BY, concatenates in input order and always yields a simple literal
- `dec:varve/GroupKeysByTermEquality` — A group key is its key expressions' values under the source's term equality, an error being a key value of its own that leaves the key variable unbound
- `dec:varve/HandleFixedWidthNotGeneric` — The handle is a fixed 64-bit type rather than a generic parameter, so there is one evaluator and no generic virtual method for AOT to resolve
- `dec:varve/HandlesStableAcrossSnapshots` — A builder only appends to its interning table, so a handle means the same term in every snapshot it produces
- `dec:varve/HarnessOwnsSubjectAbstraction` — The harness defines its own subject abstraction on the test side, which is not a design for the parser API
- `dec:varve/HashAggregationWithoutSpill` — Aggregation is one in-memory hash table per Group with no spilling, bounded only by the caller's resource governance
- `dec:varve/HeaderChainsToPrevious` — Every commit header carries prev, the hash of the previous commit's header, with a fixed value at position 1
- `dec:varve/HeaderCommitsToContent` — Every commit header carries content, the hash of (alloc, A, R), so the chain commits to every byte of the log
- `dec:varve/HeaderFieldsAndHashes` — The header is version, kind, position, timestamp, agent, cause, graph scope, attachments, kind payload, prev and content, where content hashes the body and prev the previous header with SHA-256
- `dec:varve/HeaderHashStoredBeside` — Each header's own hash is stored beside it, so a change to the last header breaks verification
- `dec:varve/HistoryNotRewrittenForSignatures` — Existing unsigned history is not rewritten to add signatures
- `dec:varve/HostNamedOnce` — PackageProjectUrl is the only value naming the host, and RepositoryUrl is derived from the git remote at pack time
- `dec:varve/HotPathAllowListIsConfiguration` — The hot-path BCL allow-list is configuration in .editorconfig, not code
- `dec:varve/HotPathAttributeFromGenerator` — The hot-path attribute is the generated DecisionDriven.HotPathAttribute citing a decision, and eng/HotPathAttribute.cs is retired
- `dec:varve/HotPathAttributeIsInternal` — The hot-path attribute is internal to each assembly, never on a public API baseline and never reaching a consumer
- `dec:varve/HotPathDiscipline` — VARVE0003: a HotPath member may not box, capture, allocate arrays or reference types, concatenate strings, make params calls, use LINQ, foreach over a class enumerator, be async, or call a non-HotPath member outside the BCL allow-list
- `dec:varve/HotPathMatchedByFullName` — The hot-path rules match the attribute by full name, not by symbol identity, a forgeable match accepted inside the repository
- `dec:varve/HotPathSignature` — VARVE0004: a HotPath member takes and returns no IEnumerable, no Task and no interface other than a Contract type itself marked HotPath
- `dec:varve/HttpServiceHandlerIsTheServers` — The HTTP service handler is the server's, at milestone 7
- `dec:varve/HumanCommitsSigned` — Commits on main by human committers are signed, with GPG or SSH
- `dec:varve/HumanReviewGatesReleases` — Human review is required for a release, through the release environment, and not for a merge
- `dec:varve/IeeeWithXsdGrammar` — XsdDouble and XsdFloat are IEEE binary64 and binary32 with XML Schema's lexical grammar and canonical forms
- `dec:varve/IlDiagnosticsAreErrors` — The IL-prefixed trimming and AOT diagnostics are error severity in .editorconfig and never enter NoWarn
- `dec:varve/ImplicitGroupOverEmptyInput` — A Group with no keys over an empty input yields one empty group, and a Group with keys yields none
- `dec:varve/ImplicitUsingsDisabled` — ImplicitUsings is disabled, so a file's dependencies are visible in the file
- `dec:varve/InMemoryDatasetBuilder` — A sealed InMemoryDatasetBuilder in Varve.Rdf carries the mutators, and ToDataset() returns a copied snapshot
- `dec:varve/InMemoryDatasetInternsItsOwn` — An in-memory dataset without a store brings its own interning table, and nothing about the contract presumes a log
- `dec:varve/InMemoryDatasetIsAValue` — InMemoryDataset is an immutable value in Varve.Rdf, an IQuadSource whose quads and interning table never change once made
- `dec:varve/InMemoryDatasetNeverInline` — InMemoryDataset has no inline ids and always answers false rather than parsing behind the accessor
- `dec:varve/InMemoryIdLayout` — An id's class is its top two bits, counters start at 1 so id 0 is the default graph, and an inline id carries a datatype tag in bits 61 to 56 and a 56-bit payload
- `dec:varve/InlineIdsForSmallValues` — Small values are encoded inline, the value being the id, with no dictionary entry
- `dec:varve/InlineOnlyCanonicalLexicalForms` — A literal may be encoded inline only when its lexical form is the canonical one for its datatype
- `dec:varve/InlineSetAndLayoutAtMilestone6` — Which datatypes qualify for inline encoding and the exact tag layout are decided with the durable format at milestone 6, and no bytes are frozen before then
- `dec:varve/InlineSetIntegerAndBoolean` — The in-memory inline set is canonical xsd:integer within range and xsd:boolean
- `dec:varve/IntegerIsCheckedInt64` — XsdInteger is a checked Int64, the derived integer types are range checks on it, and a form outside its range is a valid term with no value
- `dec:varve/InternaliseAndExternalise` — A source maps a term to its handle with TryInternalise and back with TryExternalise, which answers false for a shredded private term
- `dec:varve/InternalsVisibleToTestsOnly` — InternalsVisibleTo is permitted toward *.Tests assemblies only
- `dec:varve/IsoIffCanonProperty` — The property that two datasets are isomorphic exactly when their canonical forms are equal is the two implementations' differential test
- `dec:varve/IsomorphismCheckInTheHarness` — Dataset isomorphism for the conformance harness is a backtracking check in Varve.Conformance.Tests, not in Varve.Rdf
- `dec:varve/IsomorphismCheckKeptAsCrossCheck` — The backtracking isomorphism check stays in Varve.Conformance.Tests beside RDFC-1.0 and must agree on every case, an Inconclusive being no disagreement
- `dec:varve/IssueBeforeWork` — An issue exists before the work it tracks
- `dec:varve/KeyStoreContract` — The key store creates a key per data subject, resolves a subject to a KeyId, fetches key material by KeyId and destroys a key, and lives outside the dataset directory by construction
- `dec:varve/KeyStoreOnlyMutableComponent` — The key store is the only mutable, deletable component, and its backups must honour destruction
- `dec:varve/KnownAnswerVectorsOnEveryHost` — The implementation carries known-answer vectors that give identical bytes on CoreCLR, Native AOT and browser WebAssembly
- `dec:varve/LangVersionLatest` — LangVersion is latest rather than a pinned number, deterministic because the SDK is pinned
- `dec:varve/LatestPreviewUntilOnePointZero` — The two prerelease packages track the latest preview until the analyzer repository ships 1.0, each bump's commit carrying that preview's breaking changes
- `dec:varve/Layer6MayBePackable` — A layer 6 assembly may be packable, and nothing references layer 6
- `dec:varve/LayerDeclaredPerProject` — Each project declares ArchLayer in its own file, emitted as a generated assembly-level ArchLayer attribute, replacing VarveLayer and AssemblyMetadata
- `dec:varve/LayerExceptionEscape` — A reference the layer rule forbids is allowed only by a DesignDecision on the referencing symbol citing a filed, accepted decision
- `dec:varve/LayerTable` — Seven layers: 0 Varve.Iri and Varve.Xsd, 1 Varve.Rdf, 2 the syntaxes and Varve.Sparql, 3 Varve.Sparql.Evaluation and Varve.Shacl, 4 Varve.Store, 5 integrations, 6 hosts
- `dec:varve/LayersStrictlyDownward` — A package declares its layer, and a reference is legal only to a package in a strictly lower layer
- `dec:varve/LicenceAndNoticePacked` — LICENSE and NOTICE are packed into every package, and the metadata gate requires both to be present in the archive
- `dec:varve/LifetimeBoundedByCancellation` — A query's maximum lifetime is a host setting enforced through the evaluator's CancellationToken, set per request by the server and optional for an embedded caller
- `dec:varve/LocalOverrideIsNotConfiguration` — Verifying a branch with a command-line override for CS0618 is allowed and is not configuration
- `dec:varve/LocalSessionSignsLikeAHuman` — A session running locally with the maintainer's key signs like a human and gets no bypass
- `dec:varve/LogAndDerivedUnconfusable` — log/ and derived/ cannot be confused, so dropping everything derived is safe
- `dec:varve/LogEncodingIsProvisional` — The milestone 4 log encoding is provisional: every byte may change at milestone 6, whose format carries its own version byte
- `dec:varve/LogTypesThatMove` — Commit, CommitKind, CommitMetadata, CommitOutcome, CommitResult, CommitRequest, DatasetSettings, SettingsChange, SubscriptionFilter, ValidationVerdict and SegmentInfo move to Varve.Store.Log
- `dec:varve/MainIsTheTrunk` — main is the trunk, and anyone with write access commits to it directly or through a pull request, their choice
- `dec:varve/MaintainerAcceptanceIsTheReview` — The maintainer accepts a filed decision by adding accepted-by and accepted-at on the pull request's branch in a signed commit of their own
- `dec:varve/MajorBumpNeedsAdrChange` — A patch or minor bump of a registered package needs nothing, and a major bump or a new package needs its cited ADR changed in the same diff
- `dec:varve/ManifestsReadByVarveTurtle` — The conformance harness reads manifests with Varve.Turtle and references no other RDF implementation
- `dec:varve/MarkHotPathsWhileWriting` — Hot paths are marked when they are written, before the rule that checks them exists
- `dec:varve/MeaningChangeIsSupersession` — Fixing a broken link or a typo that changes no meaning is not an edit; anything that changes meaning is a supersession, and doubt counts as a supersession
- `dec:varve/MemoryBackendIsReal` — The memory backend is a real backend with durability None, not a test double
- `dec:varve/MemoryStorageLivesInStore` — MemoryStorage is a public backend in Varve.Store with durability None
- `dec:varve/MergeCommitsExempt` — Merge commits are exempt from the issue reference, by parent count
- `dec:varve/MergedOnlyGreen` — An expected-red pull request is merged only green
- `dec:varve/MissingSubmoduleFailsLoudly` — A guard test fails when a suite submodule is missing, so zero enumerated cases never pass silently
- `dec:varve/ModelIsNaiveOnPurpose` — The reference model is deliberately slow and correct by inspection
- `dec:varve/ModelPropertyAfterEveryRequest` — After every generated request the property compares the outcome and position, G_P and the dictionary's growth, and over the run as-of reads, diffs and the settings fold
- `dec:varve/ModelSharesNoCode` — The reference model shares no code with the store, not even a helper
- `dec:varve/MonotoneTimestamps` — The sequencer assigns each commit's timestamp as max(clock, ts(head)), so timestamps are monotone even across a clock that steps backwards (I5)
- `dec:varve/MoveToNextLtsOnly` — Varve moves to the next LTS during its release window once the AOT and WASM smoke builds pass on it, and never targets an STS release
- `dec:varve/NegatedPropertySetIsAFilteredScan` — A negated property set is a filtered scan in each direction it names
- `dec:varve/NetOfOrderedOperations` — The sequencer applies a request's operations in order to an overlay on the pinned state and commits the net result
- `dec:varve/NewestRunDecides` — A scan merges the runs' ranges, and for equal keys the newest run decides
- `dec:varve/NoAmbientClockOrRandomness` — Varve.Store reads no ambient clock and no ambient random source, enforced by a banned-symbols list scoped to the deterministic projects
- `dec:varve/NoApiKeysEver` — OIDC bearer tokens are the only accepted credential, and no later decision adds API keys or another secret scheme
- `dec:varve/NoBufferPerSubscriber` — There is no buffer per subscriber: the log is the buffer, and a slow subscriber costs reads, not memory
- `dec:varve/NoCallerHeldWriteLock` — Serialisation is a property of the sequencer, and a caller never holds a write lock across a decision
- `dec:varve/NoCodeCopiedWhateverTheLicence` — No code is copied from Oxigraph or dotNetRDF, whatever their licences permit
- `dec:varve/NoCommittedCs0618Downgrade` — No committed configuration downgrades CS0618
- `dec:varve/NoDefaultClockOrRandomness` — There is no default clock or randomness, and a query that needs one fails naming the option to set
- `dec:varve/NoDestructiveCompaction` — The log before a checkpoint is retained, and no feature may depend on removing bytes from it
- `dec:varve/NoKeysInDatasetDirectory` — No key material and nothing that must be deletable is in the dataset directory, and the file backend refuses a key store path inside it
- `dec:varve/NoLinqInLayersZeroToFour` — System.Linq.Enumerable is banned in projects at layers 0 to 4, test assemblies excepted
- `dec:varve/NoLongLivedBranches` — Work not ready for the trunk lives behind a feature flag or stays local, never on a long-lived branch
- `dec:varve/NoMultiTargeting` — The packages are not multi-targeted without an ADR of their own
- `dec:varve/NoNativeAssetInShippedClosure` — No package reaching a published Varve artifact contributes a native asset; build-time and test-only packages are exempt, and eng/native-assets.cs enforces it
- `dec:varve/NoNumericPackage` — No third-party numeric library enters the register, and BigInteger and Decimal serve only as test oracles
- `dec:varve/NoSuppressionOfDdOrVarveRules` — A DD or VARVE rule is never suppressed by pragma, SuppressMessage or an editorconfig downgrade, and a DesignDecision citing a filed decision is the only exception path
- `dec:varve/NoTemporalIndex` — The default projection stores the current state only, and history that needs routine querying belongs in the graph as data
- `dec:varve/NoTripleTermAroundExistingBlank` — A request cannot build a new triple term around an existing blank node, a stated limit
- `dec:varve/NoUpwardKnowledge` — A lower layer never learns about a higher one, including through a callback typed to a concrete higher-layer type, service location or InternalsVisibleTo
- `dec:varve/NonAssociativityWitnessRuns` — The specification's counterexample to associativity runs as a named test
- `dec:varve/NoticeNamesCopyrightHolder` — NOTICE names the copyright holder, Emil Okkels Klein, and says in plain language what file-level copyleft asks of a consumer
- `dec:varve/NowReadOncePerExecution` — NOW() reads EvaluationOptions.Clock once per execution, so every NOW() in a query agrees
- `dec:varve/NullableEnabled` — Nullable reference types are enabled repository-wide
- `dec:varve/OffTheShelfAnalyzersFirst` — The SDK trimming, AOT and single-file analyzers, PublicApiAnalyzers and BannedApiAnalyzers are used wherever they express a rule, rather than a rule of our own
- `dec:varve/OffTheShelfThenDdThenVarve` — An off-the-shelf analyzer is used where it expresses a rule exactly, a DD rule where it does, and a VARVE rule only for what neither can express
- `dec:varve/OidcBearerTokensViaJwtBearer` — The server authenticates callers by validating OIDC bearer tokens with the shared framework's JwtBearer, with no Microsoft.Identity.Web and no MSAL
- `dec:varve/OncePerCommitWorkInSequencer` — Everything that must happen exactly once per commit, from position and timestamp assignment to normalisation, validation and the synchronous default projection, happens in the sequencer
- `dec:varve/OneCasePerManifestEntry` — Each manifest entry is one test case named by its test IRI, and suites are discovered from a table
- `dec:varve/OneOverlayImplementation` — One overlay implementation serves as-of reads, diff and pre-commit validation
- `dec:varve/OnePointZeroIsAnApiFreeze` — 1.0 is a public API freeze, reached by the roadmap's definition and not by the number looking ready
- `dec:varve/OneSequencerPerDataset` — One sequencer per dataset processes commit requests one at a time and assigns dense, ascending positions (I1)
- `dec:varve/OnlyTheMaintainerMergesAndReleases` — Only the maintainer merges a pull request and only the maintainer releases
- `dec:varve/OnlyTheNewestSegmentUnsealed` — Segments are numbered by the backend in ascending order, and only the newest may be unsealed or appended to
- `dec:varve/OpenQuestionsAreArtefacts` — An open question in an ADR is a first-class artefact, written under the decision it affects and never resolved in passing
- `dec:varve/OptimiserIsAlgebraToAlgebra` — The optimiser is a function from algebra to algebra with no plan type, applied by default and skippable by a caller
- `dec:varve/OptimiserNeverMovesNondeterminism` — The optimiser never folds or moves a call to RAND, NOW, UUID, STRUUID, BNODE or an extension function
- `dec:varve/OptionalExpectedPosition` — An expected position is optional per request, and a request whose expected position differs from the readable head is rejected with Conflict(head) and changes nothing
- `dec:varve/OrderedDurability` — Every record of a commit is durable before its closing record, and the closing record's durability is the commit point
- `dec:varve/OverlayIsALayer1QuadSource` — Overlay(B, (A, R)) = (B minus R) union A is a quad source in Varve.Rdf at layer 1, merged at scan time and exact under the effective-delta invariant
- `dec:varve/OverlayPlacementRestsOnOpaqueHandle` — The overlay's layer 1 placement depends on the opaque term handle of ADR 0022, so a change to 0022 is a change to it
- `dec:varve/OwnedTermEqualityIsTermEquality` — RdfTerm equality is RDF 1.1 Concepts section 3.3 term equality, permanently, and Varve.Xsd's value equality never changes it
- `dec:varve/OwnedTermIsASealedClass` — RdfTerm is a sealed class with static factories and no public constructor that owns its bytes and caches its hash
- `dec:varve/PackDryRunOnPullRequests` — Every pull request packs and eng/package-metadata.cs reads each package's nuspec back, failing closed on a missing repository element
- `dec:varve/PackableAssemblyDeclaresLayer` — VARVE0005: a Varve project that is neither a test assembly nor Varve.Analyzers declares ArchLayer, and a packable one always does
- `dec:varve/PackageLicenceExpression` — PackageLicenseExpression is the SPDX expression MPL-2.0, and eng/package-metadata.cs reads it back out of every built package
- `dec:varve/PackageMetadataSetOnce` — Package metadata is set once in Directory.Build.targets, with the licence as an SPDX expression and the icon embedded, never licenseUrl or iconUrl
- `dec:varve/PackagesAtLayers3To5HaveOneModelNamespace` — Each package at layers 3 to 5 whose contracts name its own data types has one DomainModel namespace for them, the package's root namespace plus Model; the engine namespaces themselves stay undeclared
- `dec:varve/PackagesVersionTogether` — Every package versions together from one tag and ships as a set
- `dec:varve/PaddingIsADatasetSetting` — Length-hiding padding to a multiple of 16 bytes, applied before the MAC, is a dataset setting off by default
- `dec:varve/PathsNormalisedFirst` — A normalisation pass, always applied before the optimiser, rewrites link, inverse, sequence and alternative at the top of a path into triple patterns, swapped paths, joins and unions, recursively
- `dec:varve/PathsStayInOneGraph` — A closure runs within one active graph, and once per named graph when the graph variable is unbound
- `dec:varve/PinCapturesTheVersion` — Pin() captures the current version by reference, so later commits leave it untouched
- `dec:varve/PinContractDocumentedTwice` — The pin contract is documented on Dataset.Pin() and on the evaluator's entry point
- `dec:varve/PinLivesForOneOperation` — A pinned read has the lifetime of one operation, because while it is held nothing it reads can be dropped or archived
- `dec:varve/PinPerQueryExecution` — A pinned read lives for one query execution, from before evaluation until the last result is consumed or the consumer stops, and is never cached or shared
- `dec:varve/PinReleasedBeforeSubmit` — The update's pin is released before the composed delta is submitted
- `dec:varve/PlaintextConfinement` — No file in log/, no checkpoint and no filtered subscription record ever holds a private term's plaintext or key material, which exist only in memory and under derived/ tagged by KeyId (I9)
- `dec:varve/PositionIsAWrapper` — Every log position is a Position over long, ordered as I1 requires, with no public arithmetic
- `dec:varve/PositionPersistedWithState` — A projection persists its position atomically with its state
- `dec:varve/PreferBcl` — A package enters only when the BCL does not do the job
- `dec:varve/PrereleaseUntilSparqlConformance` — The first tag is v0.1.0-preview.1, and versions stay 0.x prerelease until the core passes the SPARQL conformance suites
- `dec:varve/PrivateIdsIndependentOfContent` — Private ids are counters in their own class, independent of content and never interned
- `dec:varve/PrivateTermsCompareByPlaintext` — A readable private term compares by its plaintext term against private and canonical terms alike, and a shredded one is equal only to itself
- `dec:varve/PrivateTermsHashByValue` — When private terms exist, a source's comparer hashes by value for every class of id
- `dec:varve/ProjectLicence` — Varve is licensed under MPL-2.0, file-level copyleft, with the canonical text verbatim in LICENSE
- `dec:varve/ProjectionRebuildEquivalence` — A projection may be dropped and rebuilt from position 0 or a checkpoint, and a rebuilt projection is observationally equal to a maintained one (I8)
- `dec:varve/ProjectionStateIsAnImmutableVersion` — The default projection's state is an immutable version of its position and runs, published by one reference write
- `dec:varve/ProseNamesNoProduct` — Prose describes the project as developed with AI assistance under human review and names no product, while records and issues name the tool and model
- `dec:varve/PublicApiBaselinePerPackage` — Every packable project tracks its public API in PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt, so a new public member is a reviewable line
- `dec:varve/PublishOnTagAfterEveryGate` — publish.yml publishes on a v* tag only after the full suite and every gate pass, and never from a pull request
- `dec:varve/PublishedDependencyListEmpty` — A published Varve package's dependency list is empty unless an ADR says otherwise
- `dec:varve/PushPathAndRecordAttest` — A sandbox commit is attested by the push path and its traceability record instead of a signature
- `dec:varve/QuadCountInRdf` — A cardinality estimate's count is a QuadCount in Varve.Rdf
- `dec:varve/QuadSourceContractInRdf` — The quad source contract lives in Varve.Rdf at layer 1, not in Varve.Store, because the evaluator at layer 3 needs it
- `dec:varve/RatchetGatesConformance` — CI runs the conformance suite without gating on its exit code and gates on the ratchet instead
- `dec:varve/RatchetIsBehaviourEvidence` — The conformance ratchet is the behavioural half of the evidence, and an exemption added to make a suite green is an accepted behaviour change
- `dec:varve/Rdf12WinsWhereFormsDiffer` — Where the RDF 1.1 and 1.2 canonical forms differ, 1.2 wins, and everything RDF 1.1 N-Triples accepts is still read
- `dec:varve/RdfLearnsNoPositions` — Varve.Rdf learns nothing of positions, time or storage
- `dec:varve/RdfXmlFixturesTranslatedOffline` — The SPARQL suites' RDF/XML files are translated once, offline, by dotNetRDF into committed hash-guarded N-Triples, deleted when Varve.RdfXml passes its own suite
- `dec:varve/RdfcInVarveRdf` — RDFC-1.0 is public API in Varve.Rdf over IQuadSource, returning the canonical N-Quads bytes and the issued identifiers, SHA-256 by default with SHA-384 and SHA-512 selectable
- `dec:varve/RdfcRefusesBlankInTripleTerm` — RDFC-1.0 refuses a triple term with a blank node inside it
- `dec:varve/RdfcWorkLimit` — A configurable work limit counting hash calls and permutations per blank node that needs them, defaulting to 1,000 from the suite's measured maxima, throws CanonicalisationLimitException when exceeded
- `dec:varve/ReadBytesAreImmutable` — Bytes returned by a storage read are immutable and may be held, and a backend that cannot promise it copies
- `dec:varve/ReadableHeadIsLastClosedCommit` — The readable head is the position of the last closed commit, and an unclosed commit's records are invisible to every read, subscription and projection
- `dec:varve/RebuildFromNewestCheckpoint` — The default projection rebuilds from the newest valid checkpoint as its base run and applies the tail, or from the empty run
- `dec:varve/RecordLayout` — A record is a payload length, flags with the closing flag in bit 0, the kind, reserved bytes, the position, its index within the commit and the payload, and a commit's body is split across its records
- `dec:varve/RecordNeverSpansSegments` — The store seals the active segment when an append would exceed the segment size, so a record never spans two segments
- `dec:varve/RecordsAndCommits` — A record is the physical unit of append, and a commit is one or more records of which the last carries a closing flag
- `dec:varve/RecordsIndependentlyDiscardable` — A record never depends on anything outside the log having been updated when it was written
- `dec:varve/ReferenceModelFromTheSpec` — Varve.Store.Tests holds a reference model, a fold over the same requests written from the specification alone, over terms rather than ids
- `dec:varve/RefusalNamesPositionAndBranch` — A chain refusal says which position failed and where the branch point was
- `dec:varve/RefuseRatherThanGuess` — A store refuses to open a log whose chain does not verify, and refuses to continue from a head that is not its own
- `dec:varve/RegisterCitesAdr` — Every PackageVersion carries Adr naming the decision that admits it, transitive pins included, and eng/dependency-register.cs fails on a missing or dangling citation
- `dec:varve/RejectedRequestLeavesNoTrace` — A rejected or empty request leaves no commit, no dictionary allocation and no gap in positions
- `dec:varve/ReleasePlaintextOnlyAfterVerify` — Decryption recomputes the tag, compares it with FixedTimeEquals and releases the plaintext only on equality
- `dec:varve/RemoteCliUsesBearerTokens` — The CLI talking to a remote server uses bearer tokens from the device code or client credentials flow and stores only the issuer's refresh token, in the platform credential store
- `dec:varve/ReplayIsOverCommits` — Replay is over commits, never over requests, because a commit's delta depends on the state it was applied to
- `dec:varve/RepoStandardBuiltHere` — tools/repo-standard holds a CLI over a YAML declaration of repository settings and a composite GitHub Action, built here and not published from this repository
- `dec:varve/RepoStandardHeldToVarveRules` — While it lives here the tool is held to Varve's rules: warnings as errors, the MPL-2.0 header, register citations, issue references, sign-off and traceability
- `dec:varve/RepoStandardIsolated` — Nothing in src/ references repo-standard and it references nothing there, with its own solution and its own Directory.Build.targets
- `dec:varve/ReportIsALocalToolAgainstABaseline` — DecisionDriven.Report is a local .NET tool pinned in .config/dotnet-tools.json and registered in Directory.Packages.props, run by eng/decision-report.cs over the shipped assemblies against a committed baseline report, in CI and never as a gate
- `dec:varve/RequestBlankNodesAreFresh` — A blank node term in a request is always fresh: each distinct label is one new node in that request, even a label TryExternalise produced
- `dec:varve/RetiredRulePagesStay` — A retired rule's page stays as a stub naming what replaced it, so the id still resolves
- `dec:varve/RoslynFloor` — Microsoft.CodeAnalysis.CSharp and its Workspaces package are pinned to the 5.0.0 floor, the Roslyn of the first .NET 10 SDK, and a floor is raised only with a reason
- `dec:varve/RuleDeliverables` — A new rule is delivered as the analyzer, its tests, its release-tracking entry and its rule page
- `dec:varve/RulePagePerRule` — Each VARVE rule has a page at docs/rules/VARVEnnnn.md that its HelpLinkUri points to and that links to the motivating ADR
- `dec:varve/RunsAreImmutable` — A run holds sorted asserted and retracted key arrays per order and is never modified, and a commit adds one run with no object per quad
- `dec:varve/SameLayerReferenceIsViolation` — A same-layer reference is a violation, not an exception: two packages in one layer that need each other are one package or two layers
- `dec:varve/SandboxKeyNeverRegistered` — The sandbox platform's signing key is never registered as a signing key on the maintainer's account
- `dec:varve/SandboxSignatureException` — Commits from AI sessions in the cloud sandbox are exempt from the signature requirement through the pushing App's bypass, a stated deviation from the standard
- `dec:varve/ScalarValueOrders` — Numerics compare by the XPath total order over the promoted type with NaN unordered, strings by code point, and booleans false before true
- `dec:varve/SdkPinnedLatestFeature` — global.json pins the SDK to 10.0.401 with rollForward latestFeature
- `dec:varve/SecondBackendInTheTests` — Varve.Store.Tests carries a second storage backend written against public members only and runs the contract tests against it
- `dec:varve/SegmentPreamble` — Every segment begins with VRVL, a version byte and three zero bytes, and every integer is little-endian
- `dec:varve/SegmentSizeIsAnInput` — Segment size is an input to the storage contract, not a constant compiled into a backend
- `dec:varve/SelectorWithoutErasureMode` — Without erasure mode, access is served by an optional selector over G_head, and erasure cannot be served at all
- `dec:varve/SemVerKeptDuringZeroX` — During 0.x the versioning rules are followed anyway, and a breaking change moves the minor while the major is zero
- `dec:varve/SequencerWaitsForDefaultProjection` — The sequencer refuses the next commit with Unavailable until the default projection is at the readable head
- `dec:varve/ServiceResultsJoined` — The evaluator joins a handler's solutions with the incoming ones, so a handler may use them to narrow its request or ignore them
- `dec:varve/ServiceVariablePerDistinctIri` — SERVICE ?v invokes the handler once per distinct IRI ?v takes, and an unbound or non-IRI ?v fails that invocation
- `dec:varve/SessionsNeverWriteAcceptedBy` — A session never writes accepted-by for a decision it filed, and transcribes only acceptances an ADR already records
- `dec:varve/SettingsAreACommitKind` — Settings are a commit kind beside Data and Erasure, sequenced like any other commit with an agent, a cause and a position
- `dec:varve/SettingsAreAFoldOfTheLog` — A dataset's settings at P are a fold over the Settings commits up to P, a pure function of the log that travels with it
- `dec:varve/SettingsCommitsHaveEmptyDelta` — A Settings commit carries an empty delta and is exempt from I4, like an Erasure commit
- `dec:varve/ShippedBaselineIsBreakEvidence` — A removed or altered line in PublicAPI.Shipped.txt is a breaking change, and surface in PublicAPI.Unshipped.txt carries no promise until it ships
- `dec:varve/ShrunkCounterexamplesKept` — Each shrunk counterexample becomes a named regression case beside the property
- `dec:varve/SilentServiceFailureIsOmegaZero` — A failed SERVICE SILENT evaluates to one empty solution mapping
- `dec:varve/SivAeadFromHmac` — Private terms are encrypted by a deterministic, misuse-resistant SIV AEAD built from HMAC-SHA-256 alone
- `dec:varve/SixKeyOrders` — A quad key is four 64-bit ids in one of six orders, SPOG, POSG, OSPG, GSPO, GPOS and GOSP, so every combination of bound positions and every graph mode is a prefix range, with the default graph as id 0
- `dec:varve/SkolemSchemeWithTheServer` — The skolem IRI scheme for blank node identity across protocols is decided at milestone 7, with the server
- `dec:varve/SlotBoundAssociatedData` — The associated data is version, key id, term id, position and allocation index, each fixed-width, and removing a slot field reintroduces the equality oracle
- `dec:varve/SparqlPackageReferences` — Varve.Sparql references Varve.Rdf and Varve.Iri, and Varve.Sparql.Evaluation references Varve.Sparql, Varve.Rdf, Varve.Xsd and Varve.Iri
- `dec:varve/SparqlPackagesOneLayerApart` — Varve.Sparql at layer 2 owns the algebra, the parser and the serialiser, and Varve.Sparql.Evaluation at layer 3 owns the evaluator over IQuadSource and the optimiser
- `dec:varve/SparqlUpdateIsLayer5Integration` — SPARQL Update lives in a layer 5 integration that evaluates WHERE against a pinned position and submits the resulting delta as one commit
- `dec:varve/SpecOverOxigraph` — Oxigraph is the tie-breaker only where the governing specification is silent or ambiguous, and where the specification decides, Varve follows it
- `dec:varve/SpecificationDecidesDisagreements` — When store and model disagree the specification decides, and a disagreement it does not settle is reported as a finding about the specification
- `dec:varve/StagingHandlesReachTheLogOnlyByCommit` — A staging handle reaches the dictionary or the log only through the commit that maps it, being drawn from a range the dictionary never issues
- `dec:varve/StagingRefusesBlankTerms` — Stage refuses a blank node term and a triple term containing one, and StageBlank gives a fresh provisional blank node
- `dec:varve/StaleExemption` — An exempted differential disagreement that no longer reproduces fails the harness, and its entry is removed
- `dec:varve/StatementIsTheRecoveryUnit` — On a syntax error the Turtle parser resumes after the next full stop at nesting depth zero outside strings and IRIs, and the failed statement yields no quads
- `dec:varve/StatusLineNamesAmendments` — An ADR's Status line names each of its amendments and each ADR superseding it, with dates
- `dec:varve/StorageMemberTypes` — The storage contract's segment ids, offsets and lengths are SegmentId, ByteOffset and ByteCount
- `dec:varve/StoreReadAndWriteContracts` — The store exposes a quad source contract for reads pinned to a log position and a transaction contract for writes that produces one commit
- `dec:varve/StoreReferencesNoSparql` — Varve.Store at layer 4 references no SPARQL package and exposes no query language
- `dec:varve/StrictSemVer` — Every published package follows SemVer 2.0.0: major for a break a conforming consumer could observe, minor for compatible new surface, patch for fixes with no surface change
- `dec:varve/StructureSurvivesErasure` — Erasure leaves structure: shredded ids and their links survive, and whether the residue is anonymous is a legal question
- `dec:varve/StuckProjectionFailsTheDataset` — A default projection that cannot reach the head puts the dataset in an explicit failed state, so nothing waits indefinitely
- `dec:varve/SubscribersReceiveCommits` — A subscriber receives commits, never records, and a replica ships records but advances its readable head only on a close
- `dec:varve/SubscriptionConsumerOwnsPosition` — Subscribe(from, filter) delivers closed commits after from, in order and at-least-once, and the consumer owns its position
- `dec:varve/SubscriptionIsALogReader` — A subscription is a reader of the log that yields closed commits and, at the readable head, awaits a head-advanced signal the sequencer completes
- `dec:varve/SubscriptionIsAnAsyncEnumerable` — A subscription is an IAsyncEnumerable of commits, cancelled by the consumer's token
- `dec:varve/SuiteBytesNotNormalised` — .gitattributes excludes the test-suite submodules from line-ending normalisation, because their bytes are what is tested
- `dec:varve/SupersededRulingMovesUnderItsKey` — A ruling a later ADR supersedes appears once, in the superseding set under the same key and with that ADR's acceptance
- `dec:varve/SuppressionCitesAdr` — A suppression of a rule that is neither DD nor VARVE carries a justification citing an ADR at the narrowest scope, and a repo-wide NoWarn for an IL rule is never allowed
- `dec:varve/SymbolPackagesEmbedSources` — Each package ships a .snupkg with embedded sources
- `dec:varve/TargetCurrentLts` — Varve packages target net10.0, the current LTS, set once in Directory.Build.props
- `dec:varve/TermComparerIsTermEquality` — TermComparer is RDF term equality, never value equality, which is the evaluator's at layer 3
- `dec:varve/TermIdClassInHighBits` — A TermId is 64 bits with its class carried in the high bits, read with a mask, so a reader knows the class without a lookup
- `dec:varve/TestsRunOnTestingPlatform` — dotnet test runs on Microsoft.Testing.Platform, selected in global.json, so Microsoft.NET.Test.Sdk and the VSTest adapter are deliberately absent
- `dec:varve/ThreeDependencyClasses` — Dependencies are runtime, build-time or test-only, admitted on different bars, and a test-only stopgap states its exit criterion
- `dec:varve/TieredRunMerging` — Runs are merged in tiers so a version holds O(log n) runs, and a merge into the oldest run drops its retractions
- `dec:varve/TlsTerminationIsTheDeployments` — TLS termination is the deployment's job, and the server serves a supplied certificate but manages none
- `dec:varve/ToRequestTermMapping` — ToRequestTerm maps a view's handle to RequestTerm.Existing and a provisional handle to its term, a provisional blank node taking a label unique within the staging view
- `dec:varve/TokenOnTheAuthorizationHeaderOnly` — The tool puts its GitHub token on the Authorization header only, never logs or writes it, and never sends it to an extends URL or follows a link off the API host
- `dec:varve/TopicSummariesForOutsideWork` — A design conversation held outside the repository is recorded as a topic summary naming its decisions, and the maintainer holds the transcript
- `dec:varve/TornTailSealedAndSkipped` — On open a torn or unclosed tail is ignored, its segment is sealed and appending resumes in a new one, and any other disorder refuses to open
- `dec:varve/TraceabilityRecordPerSession` — Every AI-assisted session files docs/traceability/YYYY-MM-DD-issue-N-slug.md with its prompt, the tool and model named exactly, its report and its issue
- `dec:varve/TrustedPublishingNoApiKey` — Publishing exchanges the workflow's OIDC token for a short-lived key, pushes immediately after login with --skip-duplicate, and stores no API key
- `dec:varve/TurtleRoundTripIsIsomorphic` — A Turtle round trip is isomorphic, not byte-identical, and byte stability belongs to canonical N-Triples
- `dec:varve/TurtleWriterKeepsNarrowEscapes` — Turtle has no canonical form, and its writer keeps RDF 1.1's narrower escape set
- `dec:varve/TwoAnalyzerPackages` — Generic rules come from the DecisionDriven.Analyzers package, adopted by configuration and never by changing it, and Varve.Analyzers keeps only rules that know a Varve fact
- `dec:varve/TypedValueAccessor` — IQuadSource gains TryGetInlineValue, true only when the handle itself encodes the term's value, which it returns as an InlineValue of kind Integer or Boolean
- `dec:varve/UnacceptedCitationsOnlyByPullRequest` — A change citing an unaccepted decision reaches main only through a pull request, never by a direct push
- `dec:varve/UnclosedTailDiscarded` — On recovery an unclosed tail is discarded, never replayed, repaired or reported as data
- `dec:varve/UndeclaredReferencedLayerIsError` — A referenced Varve assembly that declares no layer is an error in its own right, so the direction rule cannot be bypassed by omission
- `dec:varve/UnknownEstimateIsHonest` — A source that cannot estimate says unknown rather than guessing, and the consumer falls back as if the member did not exist
- `dec:varve/UnlayeredAssemblies` — Test assemblies and Varve.Analyzers declare no layer, and every other Varve project declares one
- `dec:varve/UnsilencedServiceFailureFailsQuery` — A failed SERVICE without SILENT fails the query with an exception naming the endpoint
- `dec:varve/UnwritableSettingsReported` — Settings no API can write are read and reported or left out, never half supported
- `dec:varve/UpdateConflictNotRetried` — A Conflict is returned, not retried, unless the caller sets ConflictRetries, each retry re-pinning and re-evaluating
- `dec:varve/UpdateIsAtMostOneCommit` — An update request is one commit of its composed delta with the expected position P, or none when its net effect is empty
- `dec:varve/UpdateOverChainedOverlays` — Each update operation is evaluated in order against the overlay of the pinned staging view and the deltas before it, each delta exact against that source
- `dec:varve/UpdateSingleEntryPoint` — SparqlUpdate.ExecuteAsync over a Dataset is the one entry point, with UpdateOptions, ILoadSource, LoadedDocument and SparqlUpdateException
- `dec:varve/UpstreamDestination` — Upstream defects go to Oxigraph's tracker one per issue, spec gaps to the W3C suites or the working group, and design findings to Oxigraph Discussions, each linked from a Varve issue
- `dec:varve/UpstreamLicensing` — Code crosses into Oxigraph only from its copyright holders contributing directly, tests go to the W3C suites under their terms, and shared tooling stays in Varve under MPL-2.0
- `dec:varve/UriBanNarrowedPerProject` — The repository-wide System.Uri ban is narrowed before layer 5 needs System.Uri, by a per-project banned-symbols file and not by call-site suppressions
- `dec:varve/ValidatorAcceptsOrRejects` — A validator accepts, optionally with an attachment carried in the commit's metadata, or rejects with a report, and a rejection leaves no trace
- `dec:varve/ValidatorBindingNotInTheLog` — Binding validators to a dataset is an option of the open dataset, never a fact in the log
- `dec:varve/ValidatorContractInStore` — The pre-commit validator contract is a store concern at layer 4, and a validator that uses it is a layer 5 composition
- `dec:varve/ValidatorsStatedTwice` — Validators are stated over the store's types and over the model's term sets, and their verdicts must agree before anything else is compared
- `dec:varve/ValuelessLiteralIsATypeError` — A lexical form with no value in range is a type error to the evaluator, never a parse failure
- `dec:varve/VarveIdReservations` — ADR 0004's reservation table is retired: VARVE0001 and VARVE0002 are retired for ever, and VARVE0003 to VARVE0008 are released to their DD successors or renumbered
- `dec:varve/VarveRuleIdScheme` — Varve rule ids are VARVE and four digits, allocated in order and never reused; a retired id stays retired
- `dec:varve/VarveRulesUnderDd0008` — dd_rule_id_prefixes is VARVE, and dd_banned_names is left at the package's default list
- `dec:varve/VersionFromTagByMinVer` — A package's version comes from its git tag through MinVer, and no Version property exists
- `dec:varve/VersionedKeySeparation` — The MAC and encryption keys are derived from the subject key by HKDF-SHA-256 under distinct, versioned info strings
- `dec:varve/VersionsCentralAndResolved` — Every version lives in Directory.Packages.props, resolved from nuget.org when added or changed, and the default is the latest stable
- `dec:varve/W3cSuitesPinnedSubmodule` — W3C test data is a pinned git submodule, advanced only by a commit that says so, and never vendored or fetched at test time
- `dec:varve/WarningsAreErrors` — TreatWarningsAsErrors, AnalysisLevel latest-recommended and EnforceCodeStyleInBuild are on repository-wide
- `dec:varve/WidenContractNotMoveEvaluator` — If the quad source contract is too narrow for an optimiser, it is widened by a superseding ADR, never bypassed by moving the evaluator down a layer
- `dec:varve/WrapperOnlyApiDiff` — The public API diff of the change is the wrapper types, the moved namespace and the retyped signatures, and nothing else
- `dec:varve/WrappersAreReadonlyRecordStructs` — Each wrapper is a readonly record struct over one primitive, with an explicit constructor, a Value property and no implicit conversion either way
- `dec:varve/XsdParsesAndFormatsSpans` — Each Varve.Xsd type parses from UTF-8 and UTF-16 spans and formats its canonical form into a span, the numeric types without allocating
- `dec:varve/XsdPartialOrderNamedApart` — The XSD partial order on date and time values is available as a second, separately named comparison
- `dec:varve/XsdStringIsFoldedAway` — An explicit xsd:string datatype is folded away as a spelling of the same term, and rdf:langString and rdf:dirLangString are refused as explicit datatypes
- `dec:varve/XsdTypesOutOfScope` — hexBinary, base64Binary, anyURI, QName, NOTATION and the types derived from xsd:string are out of scope and compare by lexical form
- `dec:varve/YamlDotNetParserAndEmitterOnly` — YamlDotNet is the tool's only runtime package, used through its parser and emitter into a JsonNode tree, with no Octokit
- `dec:varve/ZeroLengthPathFromAbsentTerm` — A zero-length path from a term absent from the graph still binds that term, as section 18.4 reads

### Citations of a version that is no longer the tip

The decision changed after the code that cites it was written.

None.

