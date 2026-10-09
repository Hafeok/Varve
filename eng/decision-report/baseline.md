# Decision-driven report

DecisionDriven.Report 0.1.0-preview.7 at `2f89035bf619`. Nothing here gates a build: a metric becomes a gate only by a decision that names its threshold and baseline.

## Layers

Instability should fall as the layer does. An assembly marked ⚠ is less stable than something above it.

| Assembly | Layer | Ca | Ce | I | A | D | |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `Varve.Iri` | 0 | 6 | 0 | 0.00 | 0.00 | 1.00 |  |
| `Varve.Xsd` | 0 | 2 | 0 | 0.00 | 0.00 | 1.00 |  |
| `Varve.Rdf` | 1 | 9 | 0 | 0.00 | 0.04 | 0.96 |  |
| `Varve.Sparql` | 2 | 5 | 2 | 0.29 | 0.08 | 0.64 |  |
| `Varve.Sparql.Results` | 2 | 3 | 1 | 0.25 | 0.07 | 0.68 |  |
| `Varve.Turtle` | 2 | 4 | 2 | 0.33 | 0.00 | 0.67 | ⚠ |
| `Varve.Sparql.Evaluation` | 3 | 4 | 4 | 0.50 | 0.08 | 0.42 | ⚠ |
| `Varve.Store` | 4 | 5 | 2 | 0.29 | 0.08 | 0.63 |  |
| `Varve.Protocol` | 5 | 1 | 7 | 0.88 | 0.07 | 0.05 |  |
| `Varve.Protocol.Client` | 5 | 1 | 7 | 0.88 | 0.00 | 0.13 |  |
| `Varve.Sparql.Store` | 5 | 1 | 5 | 0.83 | 0.08 | 0.08 |  |
| `Varve.Store.Browser` | 5 | 0 | 1 | 1.00 | 0.06 | 0.06 |  |
| `Varve.Server` | 6 | 0 | 10 | 1.00 | 0.02 | 0.02 |  |

## Contracts

Members offered against members each caller takes. A contract every caller uses a small part of is a candidate for splitting.

| Contract | Members | Implementers | Callers |
| --- | ---: | ---: | --- |
| `Varve.Protocol.IAccessScopes` | 1 | 2 | `Varve.Protocol.Endpoints.Exchange` uses 1 of 1: ScopesOf |
| `Varve.Protocol.ICallerIdentity` | 1 | 2 | `Varve.Protocol.Endpoints.Writes` uses 1 of 1: AgentOf |
| `Varve.Protocol.IDatasetAdministration` | 5 | 1 | `Varve.Protocol.Endpoints.AdminEndpoints` uses 5 of 5: CloseAsync, CreateAsync, DeleteAsync, List, OpenAsync |
| `Varve.Protocol.IDatasetResolver` | 1 | 1 | `Varve.Protocol.Endpoints.Exchange` uses 1 of 1: TryResolve |
| `Varve.Protocol.ISparqlUpdateExecutor` | 1 | 1 | `Varve.Protocol.Endpoints.UpdateRun` uses 1 of 1: ExecuteAsync |
| `Varve.Rdf.IQuadCursor` | 2 | 8 | `Varve.Protocol.Endpoints.GraphStoreEndpoint` uses 2 of 2: Current, MoveNext<br>`Varve.Rdf.GraphScopedQuadSource.Cursor` uses 2 of 2: Current, MoveNext<br>`Varve.Rdf.QuadOverlay.Cursor` uses 2 of 2: Current, MoveNext<br>`Varve.Rdf.RdfCanonicaliser.Run` uses 2 of 2: Current, MoveNext<br>`Varve.Server.Commands.Export` uses 2 of 2: Current, MoveNext<br>`Varve.Sparql.Evaluation.Execution.Exec` uses 2 of 2: Current, MoveNext<br>`Varve.Sparql.Evaluation.Operators.ScanCursor` uses 2 of 2: Current, MoveNext<br>`Varve.Sparql.Store.DefaultGraphView.Concatenated` uses 2 of 2: Current, MoveNext<br>`Varve.Sparql.Store.DefaultGraphView.Regraphed` uses 2 of 2: Current, MoveNext<br>`Varve.Sparql.Store.RequestExecution` uses 2 of 2: Current, MoveNext<br>`Varve.Store.BulkCommit` uses 2 of 2: Current, MoveNext<br>`Varve.Store.BulkDelta` uses 2 of 2: Current, MoveNext<br>`Varve.Store.IndexVersion` uses 1 of 2: MoveNext |
| `Varve.Rdf.IQuadSource` | 7 | 8 | `Varve.Protocol.ChangeFeedWriter` uses 1 of 7: TryExternalise<br>`Varve.Protocol.Endpoints.GraphStoreEndpoint` uses 2 of 7: Match, TryInternalise<br>`Varve.Rdf.GraphScopedQuadSource` uses 7 of 7: Contains, Estimate, Match, TermComparer, TryExternalise, TryGetInlineValue, TryInternalise<br>`Varve.Rdf.QuadOverlay` uses 7 of 7: Contains, Estimate, Match, TermComparer, TryExternalise, TryGetInlineValue, TryInternalise<br>`Varve.Rdf.RdfCanonicaliser.Run` uses 3 of 7: Match, TermComparer, TryExternalise<br>`Varve.Sparql.Evaluation.Compile.Compiler` uses 1 of 7: TryInternalise<br>`Varve.Sparql.Evaluation.Execution.Exec` uses 4 of 7: Match, TermComparer, TryExternalise, TryInternalise<br>`Varve.Sparql.Evaluation.Expressions.Semantics` uses 1 of 7: TryGetInlineValue<br>`Varve.Sparql.Evaluation.Operators.BgpCursor` uses 1 of 7: TryExternalise<br>`Varve.Sparql.Evaluation.Operators.GraphOperator` uses 1 of 7: TryInternalise<br>`Varve.Sparql.Evaluation.Operators.OrderByOperator` uses 1 of 7: TryGetInlineValue<br>`Varve.Sparql.Evaluation.Operators.PathOperator` uses 1 of 7: TryInternalise<br>`Varve.Sparql.Evaluation.Operators.ScanCursor` uses 1 of 7: Match<br>`Varve.Sparql.Evaluation.Optimisation.Optimiser.Estimates` uses 2 of 7: Estimate, TryInternalise<br>`Varve.Sparql.Evaluation.SparqlEvaluator` uses 2 of 7: TermComparer, TryInternalise<br>`Varve.Sparql.Store.DefaultGraphView` uses 7 of 7: Contains, Estimate, Match, TermComparer, TryExternalise, TryGetInlineValue, TryInternalise<br>`Varve.Sparql.Store.RequestExecution` uses 3 of 7: Contains, Match, TryInternalise<br>`Varve.Store.BulkDelta` uses 1 of 7: Match<br>`Varve.Store.DatasetView` uses 4 of 7: Contains, Estimate, Match, TryGetInlineValue<br>`Varve.Turtle.NQuadsWriter` uses 1 of 7: TryExternalise<br>`Varve.Turtle.TurtleWriter` uses 1 of 7: TryExternalise |
| `Varve.Sparql.Evaluation.IAggregateAccumulator` | 2 | 0 | `Varve.Sparql.Evaluation.Operators.GroupOperator.Accumulator` uses 2 of 2: Add, TryGetResult |
| `Varve.Sparql.Evaluation.IExtensionAggregate` | 1 | 0 | `Varve.Sparql.Evaluation.Operators.GroupOperator.Accumulator` uses 1 of 1: CreateAccumulator |
| `Varve.Sparql.Evaluation.IExtensionFunction` | 1 | 0 | `Varve.Sparql.Evaluation.Expressions.ExtensionExpr` uses 1 of 1: TryEvaluate |
| `Varve.Sparql.Evaluation.IRandomSource` | 1 | 2 | `Varve.Sparql.Evaluation.Execution.Exec` uses 1 of 1: NextBytes |
| `Varve.Sparql.Evaluation.IServiceHandler` | 1 | 2 | `Varve.Sparql.Evaluation.Operators.ServiceOperator` uses 1 of 1: Execute |
| `Varve.Sparql.Store.ILoadSource` | 1 | 2 | `Varve.Sparql.Store.RequestExecution` uses 1 of 1: LoadAsync |
| `Varve.Store.IBlobWriter` | 2 | 4 | `Varve.Store.BulkCommit` uses 2 of 2: PublishAsync, WriteAsync<br>`Varve.Store.CommitIndexFormat` uses 2 of 2: PublishAsync, WriteAsync<br>`Varve.Store.Dataset` uses 2 of 2: PublishAsync, WriteAsync<br>`Varve.Store.DerivedFormat` uses 2 of 2: PublishAsync, WriteAsync<br>`Varve.Store.DerivedFormat.DirectoryWriter` uses 1 of 2: WriteAsync<br>`Varve.Store.ExternalSort`1` uses 2 of 2: PublishAsync, WriteAsync<br>`Varve.Store.RecordWriter`1` uses 2 of 2: PublishAsync, WriteAsync<br>`Varve.Store.TermTable` uses 2 of 2: PublishAsync, WriteAsync |
| `Varve.Store.ICommitValidator` | 1 | 0 | `Varve.Store.Dataset` uses 1 of 1: Validate<br>`Varve.Store.ICommitValidator` uses 1 of 1: Validate |
| `Varve.Store.IDerivedStore` | 4 | 4 | `Varve.Store.BulkCommit` uses 3 of 4: CreateAsync, DeleteAsync, OpenAsync<br>`Varve.Store.CommitIndexFormat` uses 2 of 4: CreateAsync, OpenAsync<br>`Varve.Store.Dataset` uses 4 of 4: CreateAsync, DeleteAsync, ListAsync, OpenAsync<br>`Varve.Store.Dataset.CommitIndexOpener` uses 2 of 4: DeleteAsync, ListAsync<br>`Varve.Store.DerivedFormat` uses 2 of 4: CreateAsync, OpenAsync<br>`Varve.Store.ExternalSort`1` uses 1 of 4: CreateAsync<br>`Varve.Store.RankIndex` uses 1 of 4: OpenAsync<br>`Varve.Store.RecordReader`1` uses 1 of 4: OpenAsync<br>`Varve.Store.RecordWriter`1` uses 1 of 4: CreateAsync<br>`Varve.Store.SortedReader`1` uses 1 of 4: OpenAsync<br>`Varve.Store.SpillSpace` uses 1 of 4: DeleteAsync<br>`Varve.Store.TermMerge` uses 1 of 4: OpenAsync<br>`Varve.Store.TermTable` uses 1 of 4: CreateAsync |
| `Varve.Store.IProjection` | 3 | 0 | `Varve.Store.Dataset` uses 3 of 3: ApplyAsync, Position, ResetAsync |
| `Varve.Store.IReadableBlob` | 2 | 4 | `Varve.Store.CommitIndexFormat` uses 1 of 2: Read<br>`Varve.Store.CommitSegment` uses 1 of 2: Read<br>`Varve.Store.Dataset` uses 2 of 2: Length, Read<br>`Varve.Store.DerivedFormat` uses 2 of 2: Length, Read<br>`Varve.Store.DerivedFormat.DirectoryReader` uses 1 of 2: Read<br>`Varve.Store.KeySection` uses 1 of 2: Read<br>`Varve.Store.RankIndex` uses 2 of 2: Length, Read<br>`Varve.Store.RecordReader`1` uses 2 of 2: Length, Read<br>`Varve.Store.SortedReader`1` uses 2 of 2: Length, Read<br>`Varve.Store.TermRunReader` uses 2 of 2: Length, Read<br>`Varve.Store.TermSection` uses 1 of 2: Read |
| `Varve.Store.ISegmentStore` | 9 | 4 | `Varve.Store.Dataset` uses 9 of 9: AppendAsync, CreateSegmentAsync, Durability, FlushAsync, ListSegmentsAsync, ReadManifestAsync, ReadRangeAsync, SealAsync, WriteManifestAsync<br>`Varve.Store.LogChain` uses 1 of 9: ReadManifestAsync<br>`Varve.Store.LogReader` uses 2 of 9: ListSegmentsAsync, ReadRangeAsync<br>`Varve.Store.LogReader.SegmentReader` uses 1 of 9: ReadRangeAsync<br>`Varve.Store.LogWriter` uses 4 of 9: AppendAsync, CreateSegmentAsync, FlushAsync, SealAsync |
| `Varve.Store.IStorage` | 2 | 3 | `Varve.Store.Dataset` uses 2 of 2: Derived, Log |

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
| `Varve.Protocol.Client.Model.RdfDocument` | 2 | 2 |
| `Varve.Protocol.Model.AsOf` | 2 | 4 |
| `Varve.Protocol.Model.DatasetName` | 2 | 3 |
| `Varve.Protocol.Model.FeedRecord` | 3 | 3 |
| `Varve.Protocol.Model.Instants` | 2 | 4 |
| `Varve.Protocol.Model.ProblemType` | 2 | 3 |
| `Varve.Rdf.CanonicalNQuads` | 1 | 3 |
| `Varve.Rdf.CardinalityEstimate` | 3 | 8 |
| `Varve.Rdf.GraphPattern` | 2 | 7 |
| `Varve.Rdf.GraphScope` | 2 | 4 |
| `Varve.Rdf.GraphScopedQuadSource` | 2 | 11 |
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
| `Varve.Sparql.Evaluation.Model.ColumnIndex` | 1 | 1 |
| `Varve.Sparql.Evaluation.Model.ServiceResult` | 2 | 2 |
| `Varve.Sparql.Results.Model.ResultsPosition` | 1 | 6 |
| `Varve.Sparql.Results.Model.SparqlResultsError` | 1 | 6 |
| `Varve.Sparql.Store.Model.LoadedDocument` | 2 | 2 |
| `Varve.Store.Browser.Model.BrowserDatasetName` | 1 | 1 |
| `Varve.Store.Log.BlobName` | 1 | 6 |
| `Varve.Store.Log.ByteCount` | 1 | 1 |
| `Varve.Store.Log.ByteOffset` | 1 | 1 |
| `Varve.Store.Log.Commit` | 1 | 3 |
| `Varve.Store.Log.CommitRequest` | 1 | 5 |
| `Varve.Store.Log.CommitResult` | 6 | 11 |
| `Varve.Store.Log.CommitTimestamp` | 1 | 6 |
| `Varve.Store.Log.DatasetDirectory` | 1 | 1 |
| `Varve.Store.Log.DatasetId` | 2 | 3 |
| `Varve.Store.Log.DirectoryPath` | 2 | 3 |
| `Varve.Store.Log.FormatVersion` | 1 | 6 |
| `Varve.Store.Log.KeyStoreDirectory` | 1 | 2 |
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

799 citations of 91 decisions.

### Decisions with no citation

Implicit somewhere, or dead. The report does not say which.

- `dec:varve/AMilestoneIsCompleteWhenReleased` — A milestone is complete when a release descriptor names its issue in basis; the cut closes the issues its basis names, with the gates App's token
- `dec:varve/AcceptanceTranscription` — A transcribed acceptance is mailto:emil@okkels-klein.dk at the ADR's date, or at a dated amendment's own date for a ruling the amendment changed
- `dec:varve/AcceptedAdrNotEdited` — An accepted ADR's text is never edited or deleted: a change of decision is a superseding ADR, and additions are dated amendment blocks beside the text
- `dec:varve/AcceptedWhenSettledOrAgreed` — An ADR is Accepted when docs/brief.md already settles the matter or the project owner has agreed it, and Proposed otherwise
- `dec:varve/AccessByKeyId` — Access(K) is every dictionary entry under K, every quad of any commit mentioning one with the positions and timestamps it was asserted and retracted, and the metadata of commits whose agent is under K
- `dec:varve/AccessOmitsOthersAgents` — Agents of other commits appear in Access(K) only when canonical or under K, per GDPR Article 15(4)
- `dec:varve/AccessScopeSetting` — Access scope is AllHistory or Current, stated per request or taken from a dataset setting that defaults to AllHistory
- `dec:varve/AdminIsDatasetWide` — Admin is dataset-wide and reads and writes every graph; there is no graph-scoped admin
- `dec:varve/AdrFiveSections` — Every ADR has Status, Context, Decision, Alternatives considered and Consequences, in that order, and an ADR with no alternatives is a note, not a decision
- `dec:varve/AdrStatusValues` — An ADR's Status is Proposed, Accepted, Superseded by NNNN or Rejected, with the date it reached that status
- `dec:varve/AdrsNumberedNeverReused` — ADRs live in docs/adr/NNNN-kebab-title.md, numbered from 0001 in order, never reused and never renumbered
- `dec:varve/AgentDecisionsAcceptedByTheirHuman` — A decision first filed by an agent's commit is accepted only by that agent's responsible human or a delegate
- `dec:varve/AgentIsIssuerAndSubjectIri` — The commit agent is the IRI of the token's issuer, a hash, and the subject percent-encoded to RFC 3986 unreserved characters, the subject being oid for Entra and sub otherwise
- `dec:varve/AgentPullRequestsApprovedOnTheHead` — A pull request containing an agent's commits needs, for every agent in it, an approval on its current head by a holder of Approve for that agent, its responsible human or a delegate
- `dec:varve/AgentsMdHoldsTheRules` — The agent rules live in the vendor-neutral AGENTS.md, and CLAUDE.md is one line pointing at it
- `dec:varve/AggregateErrorLeavesUnbound` — An aggregate whose result is an error leaves its binding unbound and never fails the query
- `dec:varve/AggregationFollowsTheAlgebraLiterally` — Aggregation follows SPARQL section 18.5.1 literally: aggregates are extracted once per Group into slots no author can name, and a non-key variable in one reads as SAMPLE
- `dec:varve/AllScopeSkipsTheWrapper` — A request whose scope is all is served by the unwrapped view, allocating and costing what it did before; the wrapper is skipped, not a no-op layer
- `dec:varve/AllocationsReachable` — Every id in a commit's alloc is reachable from its A or its metadata, directly or as a component of an entry that is (I3)
- `dec:varve/AllowListAddsEnumeratorAndListReads` — The hot-path allow-list admits by member IEnumerator's MoveNext, IEnumerator of T's Current, and List of T's indexer getter and Count; the two List members neither allocate nor call back, and a call through the two interface members runs what implements them, which VARVE0003 checks where it is written: every solution enumerator in the evaluator is an operator's Open, held to the rule as an override of a HotPath member, or BgpCursor, which is marked
- `dec:varve/AllowListAddsNonAllocatingBclHelpers` — The hot-path allow-list also admits the BCL helpers span parsing is written with that neither allocate nor call back into user code: Index, Range, MemoryExtensions, Rune, Utf8, HashCode, Math, Int32, and the throw helpers of ArgumentException, ArgumentNullException, ArgumentOutOfRangeException and ObjectDisposedException
- `dec:varve/AllowListAddsNonAllocatingValueTypes` — The hot-path allow-list admits by type the numeric value types Byte, SByte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Int128, UInt128, Single, Double and Decimal, and Boolean, Nullable of T and ReadOnlyMemory of T, none of which allocates; until VARVE0003 can admit members (Varve issue 56) it also admits ImmutableArray of T and CancellationToken by type, of which a hot path may call only members that do not allocate, and never ImmutableArray's ToArray, Add, AddRange, Insert, InsertRange, Remove, RemoveAt, RemoveAll, RemoveRange, Replace, SetItem, Sort, ToBuilder or its enumeration through IEnumerable of T, nor CancellationToken's Register, UnsafeRegister or WaitHandle
- `dec:varve/AllowListNamesMembers` — An entry of the hot-path allow-list is a type, which admits every member of it, or a type and a member's metadata name, which admits that member and its overloads and nothing else of its type; a property is named by the accessor an access runs, both for a compound assignment, and a trailing star is a prefix for either
- `dec:varve/AmendmentRulingsCarryTheirDate` — A ruling an amendment adds or changes enters the ledger in the amended ADR's set with the amendment's date
- `dec:varve/AnalyzerNeverRuntimeDependency` — Varve.Analyzers is referenced as an analyzer, never as a library, and never appears in a published dependency list
- `dec:varve/AnalyzerReleaseTracking` — Rules are tracked in AnalyzerReleases.Shipped.md and AnalyzerReleases.Unshipped.md, as RS2008 enforces
- `dec:varve/AnalyzersTargetNetStandard20` — Varve.Analyzers targets netstandard2.0, the one structural exception, because it must load in the compiler and in Visual Studio
- `dec:varve/AngleSharpPinnedTransitively` — AngleSharp is pinned transitively above the version dotNetRdf.Core asks for, which NuGet audit flags, rather than silenced, and leaves with dotNetRdf.Core
- `dec:varve/AnonymousHasNoAgent` — In anonymous mode a commit has no agent
- `dec:varve/AnonymousModeIsExplicit` — Anonymous mode is enabled only explicitly, logs a warning on every start, and is enabled by the Aspire integration only in development
- `dec:varve/AnonymousRefusedInProduction` — Auth:Mode has no default, and anonymous mode with Auth:Production refuses to start
- `dec:varve/AppTokenForVarvesWorkflow` — Varve's own repo-standard workflow uses only a GitHub App installation token, and no long-lived credential is added
- `dec:varve/ApprovalBecomesASignedLedgerReview` — When the ledger gates ship, an approval is a signed ledger Review over the head sha by an identity holding Approve, and the GitHub review and comment forms are retired with the identity map
- `dec:varve/ApprovalIsAConversationComment` — The approval is a pull request conversation comment containing approve and at least twelve characters of the current head, by a holder of Approve for every agent in the pull request; review submissions are not consulted
- `dec:varve/ApprovedHeadLandsThroughLand` — On a push to land/, an agent's commits are admitted when the pushed head is a pull request's head carrying the gates App's successful agent review check run; any other sha is not
- `dec:varve/ArchFamilyIsVarve` — ArchFamily is Varve, set once in Directory.Build.props
- `dec:varve/ArchitecturalRulesAreErrors` — A rule that protects the package graph or a constraint of the brief is error severity, not warning
- `dec:varve/AsOfHeader` — A read may carry Varve-As-Of as position:n or time:RFC 3339 normalised to UTC with at most seven fractional digits; on a write it is 400
- `dec:varve/AsOfReadFromNearestCheckpoint` — An as-of read at a closed position P is Overlay(K_Q, net(L(Q..P])) for the greatest checkpoint Q at or below P, at cost proportional to P minus Q
- `dec:varve/AsOfReadsStructurallyStable` — As-of reads are structurally stable for ever, and erasure changes only the readability of private terms, at every position at once
- `dec:varve/AsOfTimeResolvesByI5` — An as-of time resolves to the greatest position whose timestamp is at or before it
- `dec:varve/AtLeastOnceIdempotentByPosition` — Delivery to a projection is at-least-once, and applying a commit at or below the projection's position is a no-op
- `dec:varve/AuthJobNames` — The CI jobs auth (mock-oauth2) and auth (zitadel) are required checks declared in .github/repo-standard.yaml
- `dec:varve/AuthenticationOnlyInTheServer` — Authentication lives in Varve.Server, Varve.Store never receives a principal, claim or token, and the embedded CLI performs none
- `dec:varve/AutocrlfOffEverywhere` — Every workflow checkout and the devcontainer set core.autocrlf=false, so every platform checks out the same bytes
- `dec:varve/BackfillsSayWhatWasReconstructed` — A backfilled record says plainly what was reconstructed rather than recorded
- `dec:varve/BannedDynamicAndReflectionMembers` — The banned-symbols list bans Microsoft.CSharp.RuntimeBinder and the reflection members that inspect or invoke, and not the System.Reflection namespace
- `dec:varve/BannedSymbolEntriesCiteAnAdr` — Every banned-symbols entry cites an ADR in its comment and ends its message with the ADR number, gated in eng/ once session 2 of issue 43 touches the file
- `dec:varve/BannedSymbolsFile` — Banned symbols are declared in eng/BannedSymbols.txt and enforced by BannedApiAnalyzers
- `dec:varve/BasisResolvesAtTheCommitUnderRelease` — A descriptor's basis resolves at the commit under release: each milestone issue it names closed by a trailer in the range, exactly the ADRs first shipped and each Accepted, and the storage format equal to Varve.Store's
- `dec:varve/BelowArchiveHorizonFailsLoudly` — An as-of read or diff below the archive horizon without the archive attached fails explicitly and never returns a partial answer
- `dec:varve/BenchmarkDataReproducible` — A benchmark dataset is generated from a stated seed and generator, never a downloaded corpus
- `dec:varve/BenchmarkDotNetConfined` — BenchmarkDotNet is admitted for a non-packable benchmark project only, where its native and Reflection.Emit dependencies reach no published artifact
- `dec:varve/BenchmarkSinkIsStatic` — A parse benchmark's sink, the counter its callbacks add to so that the parse is not optimised away, is a static field, because the callbacks are static lambdas and an instance sink would make each one a closure: an allocation in the arm that measures allocation
- `dec:varve/BenchmarksNeverGate` — Benchmarks are never a gate and are not run in CI, and a number is reported with the machine that produced it
- `dec:varve/BindingDecisionsAreEnforced` — A decision that binds code is enforced or it does not bind, and an ADR that can become an analyzer rule names the rule that enforces it
- `dec:varve/BlankLabelsInAreFresh` — A blank node label in a request is scoped to that request and never addresses an existing node
- `dec:varve/BlankLabelsOutAreStable` — A store blank node leaves the server as a blank node labelled from its store identity, the same label in every response and record of the dataset
- `dec:varve/BlobsAreGenerations` — On the origin private file system a derived blob is published as a new generation of its name by a flushed temporary and a move, the highest generation is the blob, and older ones are removed once no reader holds them
- `dec:varve/BodyIsAllocThenAssertThenRetract` — A commit's body is chunks of alloc, then assert, then retract, each with its own count, with asserted and retracted quads sorted; the closing record's body ends with the commit header and its length
- `dec:varve/BotCommitsExempt` — Commits by dependabot[bot] and github-actions[bot], a closed list in the gate's source, are exempt from the issue reference
- `dec:varve/BranchBoundSessionLandsByPullRequest` — A cloud session bound to a development branch lands its work through a pull request the maintainer merges
- `dec:varve/BranchNamesAreTheAuthors` — A short-lived branch's name is its author's business, except that land/ marks a branch whose head is meant for main
- `dec:varve/BrowserBackendAtLayer5` — The memory and file backends may live in Varve.Store, and the browser backend is a separate layer 5 package
- `dec:varve/BrowserBackendsDeclareCommitted` — Both browser backends declare Committed: a flush returns when the browser's storage reports it done, and what that survives is the browser's, which may also evict a best-effort origin
- `dec:varve/BrowserPrimitivesAsserted` — The browser smoke test asserts on every run which cryptographic primitives the browser has and lacks
- `dec:varve/BulkLoadCommitsOnceByChunks` — A bulk load commits its effective delta as one multi-record commit of chunks with their own counts and an incrementally computed content hash
- `dec:varve/BulkLoadHoldsTheSequencer` — A bulk load holds the dataset's sequencer from BeginBulkLoadAsync until it commits or is disposed, with the memtable flushed first, and its delta becomes a disk run of the projection
- `dec:varve/BulkLoadIsOneCommit` — A bulk load is one logical commit of as many records as it needs, written in bounded memory
- `dec:varve/BulkLoadNeedsAThread` — The call that fills a bulk load's sort buffer writes it before returning, so a host that cannot block a thread, the browser, cannot bulk-load
- `dec:varve/BulkLoadSortsAndMergeJoins` — A bulk load sorts its resolved input into a run by an external sort spilling to derived/ and computes its effective delta by a streaming merge-join against the pinned state's runs, with no index lookup per quad
- `dec:varve/BulkMemoryIsBounded` — A bulk load holds at most BulkLoadOptions.MemoryBytes and fixed read buffers, whatever the size of its input: operations and new terms are sorted outside memory in runs spilled to derived/bulk/, which open deletes
- `dec:varve/BulkSpillOrderPreserved` — Buffers carry sequence numbers, runs are named by them, and the load's result is the same at any worker count; the buffers share MemoryBytes and the new-term table is shared under a lock
- `dec:varve/BulkValidatorsReadSources` — A validator of a bulk commit is given the delta as quad sources over its run on disk through ICommitValidator.Validate(IQuadSource, BulkDelta), whose default reads the delta into memory
- `dec:varve/BulkWorkersOption` — BulkLoadOptions.Workers is the worker count, default the processor count less one and at least one; the parser's thread is never a worker
- `dec:varve/C14nCasesUnderTheRatchet` — The 82 RDF 1.2 c14n cases are under the ratchet with guard counts of 41 per manifest
- `dec:varve/CallerDisposesThePin` — The caller disposes the pin when the disposable result stream ends, and disposing it earlier is a caller error
- `dec:varve/CallerRunsProjections` — Asynchronous projections catch up and rebuild through the same reader when the caller asks, with no registry and no background task the store owns
- `dec:varve/CanonicalEqualityInTheHarness` — Canonical equality is the conformance harness's comparison for CONSTRUCT, DESCRIBE and update results
- `dec:varve/CanonicalFormIsRdf12s` — Canonical N-Triples is RDF 1.2 N-Triples section 3's form, and canonical N-Quads is the same form plus the graph label
- `dec:varve/CanonicalFormSpelling` — The canonical form puts one space after each term and one LF per line, lowercases language tags with --ltr or --rtl, writes triple terms as <<( s p o )>>, drops xsd:string, and escapes as RDF 1.2 does
- `dec:varve/CanonicalFormsDefinedInXsd` — Varve.Xsd is the one definition of a canonical lexical form, and Varve.Store's inline check calls it
- `dec:varve/CanonicalNQuadsWriterIsInternal` — The canonical N-Quads writer is Varve.Rdf's own and internal
- `dec:varve/CanonicalWritersByteIdentical` — Varve.Rdf's RDFC-1.0 term writer and Varve.Turtle's canonical writer stay two, and a property holds them byte-identical
- `dec:varve/CapturedVersionsStayReadable` — A captured commit index version resolves every position up to its head: blobs a newer version merged away are deleted only once no reader holds them, and a reader that finds one closed reads the current version, capping a position found by timestamp at its own head
- `dec:varve/CauseIsTheRequestId` — The commit cause is the request's TraceIdentifier as an xsd:string literal, echoed as Varve-Request-Id
- `dec:varve/ChainDetectsDoesNotAuthenticate` — The header chain detects accidental divergence; it does not prevent a fork and does not authenticate who wrote a commit
- `dec:varve/ChangeOfMeaningIsSupersession` — An amendment never changes what its ADR decided; a change of meaning is a superseding ADR, and doubt counts as a change of meaning
- `dec:varve/ChangelogIsTheProjectionOfReleases` — CHANGELOG.md is rendered from releases/ alone, a descriptor's date, title and summary per version, and is checked byte for byte; it is never edited by hand
- `dec:varve/CheckpointIsFoldOfLog` — A checkpoint at P is a fold of L[1..P] and nothing else (I7): immutable, directly queryable sorted runs under derived/, never a log entry
- `dec:varve/CheckpointIsOneMergedRun` — A checkpoint is the runs at P merged to one and written as one derived blob: a versioned header with the position, commit P's header hash and dictionary watermarks, the six key arrays and the dictionary entries
- `dec:varve/CheckpointNamesItsCommit` — A checkpoint whose recorded header hash differs from the log's at its position is ignored as a cache miss
- `dec:varve/CheckpointOnDemand` — POST /checkpoints takes a checkpoint at the head or at ?at= through Dataset.CheckpointAsync and answers 201 with the position
- `dec:varve/CheckpointPolicyIsMaintenance` — DatasetOptions.Checkpoints writes a checkpoint at the head every so many commits or bytes of log since the newest, keeping the newest so many, as maintenance and never in a commit; Never is the default
- `dec:varve/CheckpointScannedInPlace` — A checkpoint is scanned in place by the same code as a live run, with no restore step
- `dec:varve/CheckpointsAnyPositionAnyPolicy` — A checkpoint may be created at any closed position, by any policy, in the background
- `dec:varve/CheckpointsAreStreamedMerges` — A checkpoint, a memtable flush and a disk merge are a streaming merge of runs into the blob writer, holding a block per input section of the order being written, the output buffer and the directory's fences, never the state they materialise
- `dec:varve/CiCsIsThePipeline` — eng/ci.cs runs the jobs CI runs, with --list and --only, and CI runs it inside the devcontainer image
- `dec:varve/CiCsKeptInStepWithWorkflows` — A job added to ci.yml is added to eng/ci.cs too
- `dec:varve/ClaimBasedDatasetPermissions` — Configured claims map to read, write and admin permissions per dataset, and Varve keeps no user store
- `dec:varve/ClassificationGateIsPolicy` — The erasure-mode classification gate is a validator policy in the integration layer; the store provides the hook and takes no view
- `dec:varve/ClassifierContract` — A classifier assigns the term occurrences of pending operations to data subjects over the pinned source, free of SPARQL and SHACL, with derived implementations at layer 5
- `dec:varve/CliCommands` — The commands are create, info, load, query, update, export, checkpoint, feed and serve, each taking a dataset directory or a dataset URL where both make sense; load is embedded only
- `dec:varve/ClientIsALayer5Library` — Varve.Protocol.Client, layer 5, is the HTTP client over the BCL's HttpClient: SparqlHttpClient, HttpServiceHandler, RdfDocumentClient, EndpointPolicy and ClientLimits, referencing neither Varve.Protocol nor Varve.Sparql.Store
- `dec:varve/ClientLimitsBoundEveryRequest` — Every request is bounded by a host-configured timeout and a cap on response bytes, beyond which the response is a failure and never a truncated answer
- `dec:varve/ClockInjectedAtTheRoot` — The server injects TimeProvider.System and the random source at the composition root
- `dec:varve/ClosingFlagInTheLog` — The closing flag is in the log, so a copy of log/ made at any moment is a valid log up to its last closed commit
- `dec:varve/ClosingKeywordsOnlyAsTrailers` — A closing keyword with an issue number stands only as a trailer line of its own in a commit message, and eng/issue-refs.cs refuses it anywhere else
- `dec:varve/ClosureStartsFromBoundEnds` — A closure searches from its bound end, stops at a bound other end, and with both ends unbound starts from every node of the active graph
- `dec:varve/ColumnIndexIsAWrapper` — A column of a SELECT's solutions is a ColumnIndex, a readonly record struct over its position in SolutionResults.Variables in Varve.Sparql.Evaluation.Model, and SolutionResults takes it where it took an int
- `dec:varve/CommentApprovalIsNotAValidWorkflow` — Approval by pull request comment is not a valid workflow: it is tolerated only until the ledger's review gate exists, and the pull request that adopts that gate removes it
- `dec:varve/CommitAgentIsATermId` — A commit's agent is a TermId, never an inline string, so the agent can itself be a private term and be erased
- `dec:varve/CommitAgentIsTheTokenSubject` — The commit agent is the caller's stable subject identifier from the token, oid for Entra and sub otherwise, recorded as a term
- `dec:varve/CommitEntriesInDerived` — The store keeps an eighty-byte entry per closed commit in blobs of derived/index/commits/, in blocks of 128 with each block's first timestamp as its fence, read through the synchronous blob read, a cache miss when another version, kind or dataset, damaged, or not ending at its end hash
- `dec:varve/CommitStandsIfProjectionFails` — If the default projection fails after a commit's records are durable, the commit stands and the projection catches up by replay
- `dec:varve/CommitTimestampIsAWrapper` — A commit timestamp is a CommitTimestamp over DateTimeOffset, ordered as I5 requires
- `dec:varve/CommitsReferenceAnIssue` — Every commit on main carries Refs #N or Closes #N in its body, enforced by eng/issue-refs.cs
- `dec:varve/CompositionOverExactChains` — Delta composition has identity (empty, empty) and is associative over chains of exact deltas, including any run of a log, and not over arbitrary deltas
- `dec:varve/CompositionRootFlagAtLayer6` — ArchCompositionRoot is true on every layer 6 project and nowhere else
- `dec:varve/CompositionRootIsTheExecutable` — The composition root, where storage, clock, randomness, load source, service handler and validators are wired, is reserved to layer 6 executables, and a library takes each as a parameter
- `dec:varve/CompositionsLiveAboveStore` — The store defines contracts and compositions such as the SHACL and SPARQL Update integrations live above it, and hosts reference the integrations
- `dec:varve/ConfigurationValidatedAtStart` — Configuration is bound by the source generator and validated at start, and an invalid configuration lists every error and refuses to start
- `dec:varve/ConflictIsANormalAnswer` — Conflict(head) is a normal, retryable answer that carries the current head, not an error
- `dec:varve/ConformanceRatchet` — baseline/passing.txt lists the passing test IRIs, and eng/ratchet.cs fails on a regression or a vanished entry and never on an improvement
- `dec:varve/ContainersAreTheWorkflows` — The identity providers of the CI legs are containers pinned by digest that the workflow starts, and no test or package starts a container
- `dec:varve/ContractTypeVocabulary` — Contracts name only the BCL, the assembly's DomainModel namespaces, Contract types and Varve.Rdf, Varve.Iri and Varve.Xsd, with Varve.Sparql added in layer-3 project files only
- `dec:varve/ContractVocabularyWidenedPerProject` — Any further widening of ArchContractTypeAssemblies is in that project's own file with its reason, never global
- `dec:varve/ContractsInLowestLayer` — A contract lives in the lowest layer that can define it without knowing its implementers
- `dec:varve/ControlCommitsReachEverySubscriber` — Settings and Erasure commits are delivered to every subscriber regardless of filter, each as itself with its empty delta
- `dec:varve/CounterAllocatedIdClasses` — Canonical, blank and private ids are counters allocated by the sequencer, and canonical ids are injective over terms (I3)
- `dec:varve/CrLfTestedOnOwnDocuments` — CR LF handling in parsers is tested on documents this repository owns, never on whatever a checkout produced
- `dec:varve/CredentialFileSupersedesPlatformStore` — The refresh token is kept in one file under the user's profile, mode 0600 on Unix and refused when wider, DPAPI-protected on Windows through System.Security.Cryptography.ProtectedData, and weaker than the macOS Keychain, which the operator guide says
- `dec:varve/CsCheckForProperties` — Property-based tests use CsCheck, a test-only package chosen for having no dependencies
- `dec:varve/CutOnlyAtAnApprovedHead` — A release is cut only at a commit carrying the gates App's successful agent review check run; a descriptor landed by a merge commit is recovered by a retro-cut naming the approved head
- `dec:varve/CutReadsAreVisible` — A cut read is a 503 problem before the response starts, a Varve-Error trailer where trailers are supported, an error record in the line format, and an aborted connection otherwise
- `dec:varve/DataOnlyRequestExpectsNoPosition` — A request of INSERT DATA and DELETE DATA alone is composed from its text without normalisation against the pin and submitted with no expected position unless the caller gives one, which is always honoured; a request with any other operation keeps the check
- `dec:varve/DatasetBoundValidators` — DatasetOptions.Validators run on every Data commit in order, before the request's own validators, both seeing the overlay and the delta, and either may reject
- `dec:varve/DatasetIdIsGiven` — A dataset's 16-byte id is given by whoever creates the dataset; the store never generates one
- `dec:varve/DatasetLeasePreventsConcurrentOpen` — A lease file in derived/ prevents a CLI and a server from opening one dataset concurrently
- `dec:varve/DatasetNameIsAPathSegment` — A dataset name is 1 to 63 characters of [A-Za-z0-9._-] starting with a letter or digit, and an invalid or unknown name is the same 404
- `dec:varve/DatasetRoutes` — One server hosts many datasets, each under /datasets/{name}/ with its sparql, graphs, feed, diff and status endpoints
- `dec:varve/DatasetsCreatedByConfiguration` — Datasets are created by configuration or the admin API, never by a request to a protocol endpoint
- `dec:varve/DatasetsDiscoveredUnderRoot` — At start the server opens every configured dataset and then every directory directly under DatasetsRoot that holds a dataset; a created dataset survives a restart without a configuration edit
- `dec:varve/DateAndGTypesPartialOrder` — xsd:date, xsd:time and the g types compare by the XSD partial order, an indeterminate pair being a type error
- `dec:varve/DateTimeImplicitTimezoneOrder` — xsd:dateTime compares by the implicit-timezone total order, the implicit timezone being an evaluator setting that defaults to UTC
- `dec:varve/DatedAmendmentsAddOnly` — A dated amendment is an add-only block headed with its date that adds detail, records evidence, corrects the ADR's reasoning or states a needed consequence
- `dec:varve/DcoSignOffOnEveryCommit` — Every commit, human or AI session, carries a DCO Signed-off-by naming a person who may contribute the code
- `dec:varve/DecimalIsFixedPointInt128` — XsdDecimal is a fixed-point Int128 with 18 fractional digits that rejects forms needing more, fails on overflow and truncates division toward zero
- `dec:varve/DecisionDrivenPackagesAdmitted` — DecisionDriven.Analyzers and DecisionDriven.Report are admitted as build-time packages with PrivateAssets all, their register entries citing ADR 0063
- `dec:varve/DecisionSetsCheckedInTheBuild` — eng/decision-sets.cs checks the set files' front matter in the build job until the analyzer package's generator reads them
- `dec:varve/DefaultProjectionSynchronous` — The default quad projection is updated before Committed(P) returns, so a pin taken afterwards observes P, and every other projection lags
- `dec:varve/DefaultResultFormats` — With no Accept or */*, a solution or boolean result is JSON and a graph result is N-Triples
- `dec:varve/DefaultServiceHandlerRefuses` — The default service handler refuses every endpoint, so the default configuration opens no connection
- `dec:varve/DefenderSkipsTheBuildJobsFiles` — On windows-latest the build job excludes the workspace and the temporary folders from Defender before it checks out
- `dec:varve/DelegatesChangeOnlyInTheirHumansPullRequest` — A human's delegates change only in a pull request that human authored, judged against the base's map
- `dec:varve/DeleteOnlyWhenClosed` — DELETE of an open dataset is 409 dataset-open; close drains, disposes and releases the lease and open is its inverse; DELETE of a closed dataset removes its directory, log and derived data, and is not undoable
- `dec:varve/DeliveredAllocationsFiltered` — A delivered commit carries only the allocations its delivered delta and metadata refer to
- `dec:varve/DeliveredCommitExternalisesItsHandles` — Commit.TryExternalise names any handle the commit carries from the dataset's append-only dictionary, and answers false for a handle it does not carry
- `dec:varve/DeltaLineFormat` — The feed and the diff are written as application/vnd.varve.delta; version=1, a line format of commit, diff and error records with canonical N-Quads changes prefixed + or -
- `dec:varve/DerivedFormat3` — The filter is a section of the run file named in its directory, and the derived format version is 3; a format-2 run has no filter and is read as before, migrating when maintenance rewrites it
- `dec:varve/DerivedIsSortedRunsOnDisk` — derived/ holds the default projection as ADR 0041's immutable sorted runs: the newest in memory as a memtable, and older ones as disk runs written by a memtable flush or a tier merge
- `dec:varve/DetachAbsentUntilArchive` — Detach is absent from the storage contract until archive exists
- `dec:varve/DeterminismForCrashFreeHistories` — The determinism property holds for crash-free histories, because a tail recovery abandoned stays in the log
- `dec:varve/DeterministicBuilds` — Builds are deterministic, with ContinuousIntegrationBuild under CI and EmbedUntrackedSources, so two builds of one commit produce the same bytes
- `dec:varve/DeterministicStorageBytes` — Nothing in the storage contract admits ambient time, ambient randomness or iteration-order dependence
- `dec:varve/DevcontainerMirrorsTheCore` — The devcontainer mirrors the stewardship core's template and overlays .NET with the SDK pinned exactly to global.json and the wasm workloads
- `dec:varve/DictionaryIsDerivedInRuns` — The term dictionary is derived state carried by the default projection's runs: each run holds the entries of the canonical ids its commits allocated, a checkpoint those of every id up to its position, merged and persisted with the runs, so opening a dataset loads no dictionary
- `dec:varve/DiffEndpoint` — GET /datasets/{name}/diff writes Diff(from, to) of R3, resolved as the feed resolves, as one diff record
- `dec:varve/DiffFromTheLogAlone` — Diff(P1, P2) is net(L(P1..P2]), computed from the log alone with no state lookup
- `dec:varve/DifferentialExemptionsChecked` — The differential harness fails on an exemption with no or an unknown category, missing references or the category varve-defect
- `dec:varve/DifferentialTriage` — Every differential disagreement is exactly one of varve-defect, upstream-defect, spec-gap or intentional-divergence, and varve-defect is never exempted
- `dec:varve/DirectGraphIdentificationByRequestAddress` — Direct graph identification takes the request's own scheme, host and path, behind a proxy only through configured forwarded headers
- `dec:varve/DirectoriesAreWrappers` — DatasetDirectory and KeyStoreDirectory are wrappers over a normalised absolute path, compared segment by segment from the full path, case-insensitively on Windows only, and no string path is on FileStorage or Dataset
- `dec:varve/DiscardedTailRepresentable` — The storage contract can represent a discarded unclosed tail
- `dec:varve/DisposeWaitsForTheSequencer` — Dataset.DisposeAsync waits for the sequencer, so a commit in progress finishes before the dataset closes
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
- `dec:varve/ETagIsThePosition` — Every response that touched a dataset carries Varve-Position and a strong ETag of the head or the resolved position, with Vary on Accept and Varve-As-Of
- `dec:varve/EarlierAmendmentsRecognised` — The dated amendments made before this ADR are recognised as they stand
- `dec:varve/EffectiveDeltaInvariant` — A commit asserts nothing already present, retracts nothing absent, and never both asserts and retracts one quad
- `dec:varve/EffectiveDeltaNotRequest` — The log records the effective delta against the pinned state, not the request
- `dec:varve/EmbeddedModeOpensDirectoryWithoutAuth` — Embedded mode opens the directory with FileStorage directly and no authentication; the file permissions and the lease are the boundary, and SERVICE and LOAD take their policy from --allow-endpoint and --allow-source, empty by default
- `dec:varve/EmptyDeltaNoCommit` — An empty effective delta produces no commit but NoChange(head), with provisional term ids discarded; a Data commit's delta is never empty, Erasure excepted
- `dec:varve/EndpointPolicyAllowListDefaultNone` — Every outbound address is checked against an allow-list of IRI prefixes before a connection is opened, and the default list is empty, so SERVICE and LOAD are refused until the operator opts in
- `dec:varve/EngHoldsVarveSpecificGates` — eng/ is the home of the Varve-specific gates (the conformance ratchet and exemptions, the guard counts and harnesses, the benchmarks, the decision report, and ci.cs as the orchestrator) and hosts generic gates only until they are ported
- `dec:varve/EngScriptsAreFileBasedApps` — eng/ scripts are C# file-based apps run with dotnet run, one implementation for every operating system
- `dec:varve/EngineTakesWrappersToo` — The engine's own members take and return the wrappers too, even where no rule requires it
- `dec:varve/EntraAndGoogleBySecret` — Entra ID and Google have one end-to-end test each, skipped unless their secrets exist in a main-restricted environment
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
- `dec:varve/EvaluationRunnerReadsEachFileOnce` — The evaluation-suite runner that the conformance tests and the benchmarks share keeps what it has read of each data file in one static cache for the process, because every case of every suite loads through it and the suites' files do not change during a run
- `dec:varve/EvaluatorNeverPins` — The evaluator receives a quad source it does not own, never calls Pin() and never disposes what it is given
- `dec:varve/EvaluatorUnderDeterministicBan` — Varve.Sparql.Evaluation is under the ambient clock and randomness ban, as Varve.Store is
- `dec:varve/EveryAdrIsADecisionSet` — Every ADR is enumerated into docs/decisions as an interim set file in ledger namespace varve, one key per ruling in force, with its acceptance transcribed from the ADR
- `dec:varve/EveryGateRunsAtTheCommitUnderRelease` — Every gate of ci.yml runs at the commit under release before it is tagged, and a valid descriptor whose gates fail is not cut and leaves no tag
- `dec:varve/EveryMilestoneEndsInARelease` — Every milestone ends in a release: the pull request that closes a milestone issue carries the release descriptor whose basis names it
- `dec:varve/ExactEstimateIsExact` — An estimate marked exact equals the number of quads Match would yield for the pattern at the source's current state
- `dec:varve/ExecutableLayerAndRootAgree` — VARVE0005: an executable not at layer 6, a library at layer 6, and an ArchCompositionRoot that disagrees with layer 6 are each reported
- `dec:varve/ExecutablesAreLayer6` — An executable declares layer 6 unless it is a test assembly, only an executable declares layer 6, and benchmark assemblies are layer 6 like any host
- `dec:varve/ExhibitANoticeOnEveryFile` — Every .cs file carries the MPL-2.0 Exhibit A notice as its first three lines, enforced by eng/licence-headers.cs with generated code excepted by pattern
- `dec:varve/ExistingTermsByHandle` — A request addresses an existing blank node, or any existing term, by RequestTerm.Existing(handle), and an unknown handle fails the request with an exception and leaves no trace
- `dec:varve/ExpectedRedOnlyOnCs0618` — A pull request may arrive red only when every failure is CS0618 from citing a decision filed without acceptance, and its body lists those decisions
- `dec:varve/ExternalReviewBeforeShipping` — Nothing presents the construction as protecting data until an external cryptographic review, due before milestone 9 ships, and a rejection means erasure mode does not run in the browser
- `dec:varve/ExternalisedBlankLabelsFromIds` — TryExternalise of a blank id gives a label derived from the id, stable within the dataset and no identity across datasets
- `dec:varve/FailedDirectoryListedNotSkipped` — A directory under the root that fails to open is listed with state failed and the reason, reported by readiness, and never skipped
- `dec:varve/FailingOperationThrowsBeforeSubmit` — A failing update operation throws before the submit, releasing the pin, and nothing reaches the log or the dictionary
- `dec:varve/FederationAndLoadConfigurationSections` — The server's Federation and Load sections each hold one policy (AllowedEndpoints or AllowedSources, AllowPrivateAddresses) and one set of limits (Timeout, MaxResponseBytes); with neither, the refusing defaults stay
- `dec:varve/FeedAndDiffFilteredPerGraph` — The feed and the diff filter each delta to the readable graphs, drop commits that become empty, and deliver Settings and Erasure commits to admin only
- `dec:varve/FeedEndpoint` — GET /datasets/{name}/feed takes from or fromTime, to or toTime, graph and pattern; from is exclusive, to inclusive, and no end tails live
- `dec:varve/FeedReaderShips` — Varve.Protocol ships ChangeFeedReader, a pull reader of the format over UTF-8 that runs the chunk-boundary oracle
- `dec:varve/FeedReadsTheSubscriptionOnly` — The feed reads through Dataset.Subscribe and Commit.TryExternalise and nothing else, filtering by handle itself only while a filter term is not yet known
- `dec:varve/FeedResolutionIsAsymmetric` — A start time resolves to the earliest commit at or after it and an end time to the latest at or before it
- `dec:varve/FileBackendIsSynchronised` — The file backend declares Synchronised: a flush returns after FlushToDisk, so the appended bytes and the file's length are on the device
- `dec:varve/FileSuitesUnflushed` — The file property tests, the storage contract on files and the derived tests on disk run on a test-only file system over the real one whose flush does nothing; every other call reaches the disk
- `dec:varve/FilterLoadedWithDirectory` — A filter is loaded with the run's directory, held while the reader holds the run, and counted in the soak's dataset's-own figure
- `dec:varve/FilterRunsInTheReader` — A subscription's filter runs in the reader, before delivery
- `dec:varve/FilteredSkipsKeepTruePositions` — A subscription filter skips a commit whose filtered delta is empty, and the next delivered commit carries its true position
- `dec:varve/FlatWithinABand` — The 1.0 soak gate is the one-hour soak with the checkpoint policy on, its working set less the dataset's own (its runs' and checkpoints' directories and its commit table) over the last fifty minutes within plus or minus a quarter of its median and its last ten minutes within a tenth of minutes ten to twenty, with handles and derived files bounded
- `dec:varve/FloorRaiseWaitsForAmendment` — A preview that raises the Roslyn floor it declares is not taken until a dated amendment to ADR 0009 raises Varve's
- `dec:varve/FlushBeforeVisible` — A derived blob is flushed before it is renamed into place, a segment's header before its first record, and the manifest before any segment exists
- `dec:varve/FlushesKeptWhereTheyAreTheSubject` — FaultInjectionTests and BulkLoadTests keep the simulated file system's flushes, the durability job flushes the device, and FileStorageTests opens storage through the public FileStorage.OpenAsync, which always flushes
- `dec:varve/ForbiddenOperationsAbsent` — Truncation, positional writes and deletion of a sealed segment are absent from the storage contract's type, not forbidden in prose
- `dec:varve/ForwardedHeadersOnlyWhenConfigured` — Forwarded headers are honoured only when configured and only from configured proxies
- `dec:varve/FreshPathVariablesOutsideVarname` — Variables a path rewrite introduces are named .p0, .p1 and so on, outside VARNAME, so none collides with an author's or is ever projected
- `dec:varve/FullWidthSyntheticIv` — The synthetic IV and tag is the full 32 bytes of HMAC-SHA-256
- `dec:varve/GeneratedInteropNeedsUnsafeBlocks` — Varve.Store.Browser compiles with AllowUnsafeBlocks because the JSImport generator emits unsafe code; no unsafe code is written by hand in it, and no other shipped package turns it on
- `dec:varve/GeneratorsCountTheirCases` — The model's generators count every case they must produce, and a run fails if any case never occurred
- `dec:varve/GeneratorsReviewedAsTests` — A generator is reviewed as carefully as its property, and term generators produce escapes, surrogate pairs, directional language tags, ucschar IRIs and nested triple terms
- `dec:varve/GracefulShutdownDrains` — On SIGTERM the server refuses new writes, ends live feeds, drains in-flight requests and their pins, and disposes each dataset
- `dec:varve/GrantsAreScopedByGraphSet` — A grant is (dataset, permission, graph set), the set all, an explicit list of graph IRIs with default naming the default graph, or a set of IRI prefixes; 7a's Read and Write lists are the all case; the mapping from claims lives in the host's configuration
- `dec:varve/GraphExistsIffItHoldsAQuad` — A named graph exists if and only if it holds a quad: CREATE has no effect and fails without SILENT on a non-empty graph, and DROP and CLEAR retract its quads
- `dec:varve/GraphExistsWhenItHoldsAQuad` — A named graph exists when it holds a quad, so an empty PUT leaves it absent
- `dec:varve/GraphScopeIsADeclaration` — The named-graph scope in a commit's metadata is a declaration the store records and never enforces, and validators may enforce it
- `dec:varve/GraphStoreOperations` — The Graph Store serves GET, HEAD, PUT, POST and DELETE by direct and indirect identification and ?default, with bodies in the four syntaxes or multipart/form-data
- `dec:varve/GroupConcatComparedAsMultiset` — The optimiser's equivalence property compares GROUP_CONCAT results as multisets of their parts
- `dec:varve/GroupConcatInInputOrder` — GROUP_CONCAT has no ORDER BY, concatenates in input order and always yields a simple literal
- `dec:varve/GroupKeysByTermEquality` — A group key is its key expressions' values under the source's term equality, an error being a key value of its own that leaves the key variable unbound
- `dec:varve/GspOnUnreadableIs404OnUnwritableIs403` — A Graph Store PUT or DELETE of an unwritable graph is 403 and of an unreadable one 404, the same as a missing graph
- `dec:varve/HandleFixedWidthNotGeneric` — The handle is a fixed 64-bit type rather than a generic parameter, so there is one evaluator and no generic virtual method for AOT to resolve
- `dec:varve/HandlesStableAcrossSnapshots` — A builder only appends to its interning table, so a handle means the same term in every snapshot it produces
- `dec:varve/HarnessOwnsSubjectAbstraction` — The harness defines its own subject abstraction on the test side, which is not a design for the parser API
- `dec:varve/HeaderChainsToPrevious` — Every commit header carries prev, the hash of the previous commit's header, with a fixed value at position 1
- `dec:varve/HeaderCommitsToContent` — Every commit header carries content, the hash of (alloc, A, R), so the chain commits to every byte of the log
- `dec:varve/HeaderFieldsAndHashes` — The commit header is version, kind, position, timestamp, agent, cause, graph scope, attachments, kind payload, the dictionary's three counters after the commit, prev and content, with SHA-256 for both hashes
- `dec:varve/HeaderHashStoredBeside` — Every header in log/ and derived/ carries the SHA-256 of its own bytes, so a torn or reordered header is detected by itself
- `dec:varve/HigherVersionRefused` — Opening a log of a format version above those the build reads refuses with a message naming the version found and the versions read
- `dec:varve/HistoryNotRewrittenForSignatures` — Existing unsigned history is not rewritten to add signatures
- `dec:varve/HostNamedOnce` — PackageProjectUrl is the only value naming the host, and RepositoryUrl is derived from the git remote at pack time
- `dec:varve/HotPathAllowListIsConfiguration` — The hot-path BCL allow-list is configuration in .editorconfig, not code
- `dec:varve/HotPathAttributeFromGenerator` — The hot-path attribute is the generated DecisionDriven.HotPathAttribute citing a decision, and eng/HotPathAttribute.cs is retired
- `dec:varve/HotPathAttributeIsInternal` — The hot-path attribute is internal to each assembly, never on a public API baseline and never reaching a consumer
- `dec:varve/HotPathDiscipline` — VARVE0003: a HotPath member may not box, capture, allocate arrays or reference types, concatenate strings, make params calls, use LINQ, foreach over a class enumerator, be async, or call a non-HotPath member outside the BCL allow-list
- `dec:varve/HotPathMatchedByFullName` — The hot-path rules match the attribute by full name, not by symbol identity, a forgeable match accepted inside the repository
- `dec:varve/HotPathSignature` — VARVE0004: a HotPath member takes and returns no IEnumerable, no Task and no interface other than a Contract type itself marked HotPath
- `dec:varve/HttpServiceHandlerIsTheServers` — The HTTP service handler is the server's, at milestone 7
- `dec:varve/HttpServiceHandlerSendsSelectStar` — HttpServiceHandler implements IServiceHandler by POSTing SELECT * WHERE { P } serialised with SparqlWriter as application/sparql-query, after the endpoint policy has allowed the endpoint
- `dec:varve/HumanCommitsSigned` — Commits on main by human committers are signed, with GPG or SSH
- `dec:varve/HumanReviewGatesReleases` — Human review gates a release through the approval, on its head, of the pull request that adds its descriptor, and a pull request with an agent's commits; it is not otherwise required for a merge, and the release environment has no reviewer
- `dec:varve/HumansLandThroughALandBranchOrAPullRequest` — A human lands a change by pushing it to a land/ branch and fast-forwarding main to its checked head, or through a pull request, which needs no review when every commit is the human's own
- `dec:varve/IdentityMapNamesWhoActsForAnAgent` — eng/identities.json lists humans with their emails, login and delegates, agents with their responsible human, and exempt automation; it is the interim form of the ledger's authority model, replaced by the ledger's grants
- `dec:varve/IeeeWithXsdGrammar` — XsdDouble and XsdFloat are IEEE binary64 and binary32 with XML Schema's lexical grammar and canonical forms
- `dec:varve/IfMatchIsTheExpectedPosition` — If-Match carries the expected position: a mismatch with the head is 412 before anything is read, and a match becomes the commit's expected position
- `dec:varve/IfNoneMatchBeforePin` — If-None-Match on a read is answered with 304 before any pin is taken when it equals the position the read would describe
- `dec:varve/IlDiagnosticsAreErrors` — The IL-prefixed trimming and AOT diagnostics are error severity in .editorconfig and never enter NoWarn
- `dec:varve/ImagesFromUnlimitedRegistries` — The test images are pinned by digest in compose files and pulled from registries without an anonymous pull limit, never from Docker Hub
- `dec:varve/ImmutableArrayAndCancellationTokenByMember` — The hot-path allow-list admits ImmutableArray of T only by its indexer's getter, Length, IsEmpty, IsDefault, IsDefaultOrEmpty, AsSpan, AsMemory and its struct GetEnumerator, and CancellationToken only by IsCancellationRequested, CanBeCanceled and ThrowIfCancellationRequested, none of which allocates; this replaces their admission by type in AllowListAddsNonAllocatingValueTypes, whose interim clause ends with it
- `dec:varve/ImplicitGroupOverEmptyInput` — A Group with no keys over an empty input yields one empty group, and a Group with keys yields none
- `dec:varve/ImplicitTimezoneIsATimeSpan` — EvaluationOptions.ImplicitTimezoneOffset is a TimeSpan, a whole number of minutes from minus 14 to plus 14 hours, and replaces ImplicitTimezoneOffsetMinutes
- `dec:varve/ImplicitUsingsDisabled` — ImplicitUsings is disabled, so a file's dependencies are visible in the file
- `dec:varve/InMemoryDatasetBuilder` — A sealed InMemoryDatasetBuilder in Varve.Rdf carries the mutators, and ToDataset() returns a copied snapshot
- `dec:varve/InMemoryDatasetInternsItsOwn` — An in-memory dataset without a store brings its own interning table, and nothing about the contract presumes a log
- `dec:varve/InMemoryDatasetIsAValue` — InMemoryDataset is an immutable value in Varve.Rdf, an IQuadSource whose quads and interning table never change once made
- `dec:varve/InMemoryDatasetNeverInline` — InMemoryDataset has no inline ids and always answers false rather than parsing behind the accessor
- `dec:varve/InMemoryIdLayout` — An id's class is its top two bits, counters start at 1 so id 0 is the default graph, and an inline id carries a datatype tag in bits 61 to 56 and a 56-bit payload, frozen as the format's id layout
- `dec:varve/InProcessIssuerForEveryPermissionTest` — An in-process OIDC issuer on loopback with a key per run mints the tokens of every permission and token-validation test, on every platform
- `dec:varve/IndexBuiltAgainstTheLog` — Opening checks the commit index's blobs entry by entry against the log as its walk passes each commit and rebuilds from the first that disagrees, holding no more of the index than the cache, and deletes only the blobs it listed and could not use
- `dec:varve/IndexedDbIsTheFallback` — Where synchronous access handles do not exist the dataset is kept in IndexedDB, chunks and blobs committed by strict transactions, and an open blob is read whole into memory so that its reads are synchronous
- `dec:varve/InlineIdsForSmallValues` — Small values are encoded inline, the value being the id, with no dictionary entry
- `dec:varve/InlineOnlyCanonicalLexicalForms` — A literal may be encoded inline only when its lexical form is the canonical one for its datatype
- `dec:varve/InlineSetAndLayoutAtMilestone6` — Which datatypes qualify for inline encoding and the exact tag layout are decided with the durable format at milestone 6, and no bytes are frozen before then
- `dec:varve/InlineSetIntegerAndBoolean` — The inline set is canonical xsd:integer within range and xsd:boolean, and is part of a dataset's creation settings, so it grows only for datasets created with the larger set
- `dec:varve/IntegerIsCheckedInt64` — XsdInteger is a checked Int64, the derived integer types are range checks on it, and a form outside its range is a valid term with no value
- `dec:varve/InternaliseAndExternalise` — A source maps a term to its handle with TryInternalise and back with TryExternalise, which answers false for a shredded private term
- `dec:varve/InternalsVisibleToTestsOnly` — InternalsVisibleTo is permitted toward *.Tests assemblies only
- `dec:varve/IsoIffCanonProperty` — The property that two datasets are isomorphic exactly when their canonical forms are equal is the two implementations' differential test
- `dec:varve/IsomorphismCheckInTheHarness` — Dataset isomorphism for the conformance harness is a backtracking check in Varve.Conformance.Tests, not in Varve.Rdf
- `dec:varve/IsomorphismCheckKeptAsCrossCheck` — The backtracking isomorphism check stays in Varve.Conformance.Tests beside RDFC-1.0 and must agree on every case, an Inconclusive being no disagreement
- `dec:varve/IssueBeforeWork` — An issue exists before the work it tracks
- `dec:varve/JudgingScriptIsMains` — Agent review is judged by main's eng/agent-review.cs, on triggers whose workflow is main's, reading the pull request through the API as data; nothing from the pull request is checked out, built or run
- `dec:varve/JwtBearerInTheServerOnly` — Varve.Server alone takes Microsoft.AspNetCore.Authentication.JwtBearer and its Microsoft.IdentityModel closure, on the condition that its Native AOT publish stays green
- `dec:varve/JwtBearerIsAPackageInTheServer` — JwtBearer is a NuGet package outside the shared framework, taken by Varve.Server alone under ADR 0099
- `dec:varve/KeyBlocksAreDeltaCoded` — Keys in derived runs are stored in their blocks of 128, the first key whole and each later one as the first id that differs, its increase and the ids after it as varints, with each block's start in the directory; derived format version 2
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
- `dec:varve/LeaseContentIsDiagnostic` — The lease's process id, machine and time taken are written beside it in derived/LOCK.owner, for the refusal's message only
- `dec:varve/LeaseIsAWebLock` — The IndexedDB backend's lease is a Web Lock named after the dataset, taken if available and released by the browser when the context ends
- `dec:varve/LeaseIsAnExclusiveAccessHandle` — The origin private file system backend's lease is an exclusive synchronous access handle on derived/LOCK, released by the browser when the worker ends
- `dec:varve/LeaseIsAnExclusiveHandle` — The lease is derived/LOCK held open with FileShare.None until the storage is disposed, released by the operating system when the process ends, with no renewal and no takeover interval
- `dec:varve/LedgerGatesPortToTheAnalyzerRepository` — The ledger gates (register citations, and the decision-set checks the generator does not make) are ported to the analyzer repository beside DecisionDriven.Report, citing decision keys rather than ADR numbers
- `dec:varve/LevelsStatePowerLoss` — The documentation of each durability level states what survives power loss, and names the directory-entry exception for Synchronised
- `dec:varve/LicenceAndNoticePacked` — LICENSE and NOTICE are packed into every package, and the metadata gate requires both to be present in the archive
- `dec:varve/LifetimeBoundedByCancellation` — A query's maximum lifetime is a host setting enforced through the evaluator's CancellationToken, set per request by the server and optional for an embedded caller
- `dec:varve/ListShowsAdministeredDatasets` — GET /datasets answers the datasets the caller administers, 200 with an empty list for a caller with none, so the list never reveals names to a caller who may not know them
- `dec:varve/LiveFeedIsNotAPinnedRead` — A live feed holds no pin between commits, and PinnedReadLifetime does not apply to it
- `dec:varve/LiveTailIsServerSentEvents` — Live tailing is server-sent events with the position as the event id and Last-Event-ID as the resume point; long-polling is the recorded alternative
- `dec:varve/LoadDocumentFetchedByClientHostBindsSource` — RdfDocumentClient fetches an RDF document by IRI with content negotiation over the four syntaxes, the base the request IRI, the bytes capped, the policy consulted first; each host binds ILoadSource to it in one line
- `dec:varve/LocalOverrideIsNotConfiguration` — Verifying a branch with a command-line override for CS0618 is allowed and is not configuration
- `dec:varve/LocalSessionSignsLikeAHuman` — A session running locally with the maintainer's key signs like a human and gets no bypass
- `dec:varve/LogAndDerivedUnconfusable` — log/ and derived/ cannot be confused, so dropping everything derived is safe
- `dec:varve/LogEncodingIsProvisional` — The log's encoding is format version 1, tabulated in docs/spec/storage-format.md, and from the first prerelease tag that writes it every later version reads it
- `dec:varve/LogIsASegmentWriter` — log/ is written by an append-only segment writer of Varve's own, with no keys, no index and no compaction
- `dec:varve/LogTypesThatMove` — Commit, CommitKind, CommitMetadata, CommitOutcome, CommitResult, CommitRequest, DatasetSettings, SettingsChange, SubscriptionFilter, ValidationVerdict and SegmentInfo move to Varve.Store.Log
- `dec:varve/LookupsReadThroughTheBlob` — A dictionary lookup by id reads an entry's offsets and bytes, and a lookup by term searches a hash index sorted by a 64-bit hash of the term's key by interpolation, both through the synchronous blob read of the runs the reader holds
- `dec:varve/MainAcceptsOnlyCheckedCommits` — main accepts only a commit whose SHA already has passing required checks; ruleset 1 requires every gate by job name and has no bypass
- `dec:varve/MainIsTheTrunk` — main is the trunk, and anyone with write access lands on it through a pull request or by fast-forwarding it to a checked commit, their choice
- `dec:varve/MaintainerAcceptanceIsTheReview` — The maintainer accepts a filed decision by adding accepted-by and accepted-at on the pull request's branch in a signed commit of their own
- `dec:varve/MaintenanceIsNotDelivery` — The decision is about delivering commits; the dataset may own maintenance on derived data under an explicit option, off the sequencer and never blocking a commit, off by default in a browser until 6b
- `dec:varve/MaintenanceStaysOffInABrowser` — Maintenance stays off by default in a browser; a host drives it with MaintainAsync
- `dec:varve/MajorBumpNeedsAdrChange` — A patch or minor bump of a registered package needs nothing, and a major bump or a new package needs its cited ADR changed in the same diff
- `dec:varve/ManifestWrittenOnce` — log/MANIFEST holds the magic, format version, dataset id and creation settings with their hash, and is written once, before any segment
- `dec:varve/ManifestsReadByVarveTurtle` — The conformance harness reads manifests with Varve.Turtle and references no other RDF implementation
- `dec:varve/MarkHotPathsWhileWriting` — Hot paths are marked when they are written, before the rule that checks them exists
- `dec:varve/MaxRecordBytesIsAByteCount` — DatasetOptions.MaxRecordBytes is a ByteCount, as SegmentBytes is, and opening a dataset refuses one below 64 bytes or above what a record's 32-bit length can carry
- `dec:varve/MeaningChangeIsSupersession` — Fixing a broken link or a typo that changes no meaning is not an edit; anything that changes meaning is a supersession, and doubt counts as a supersession
- `dec:varve/MemoryBackendIsReal` — The memory backend is a real backend with durability None, not a test double
- `dec:varve/MemoryStorageLivesInStore` — MemoryStorage is a public backend in Varve.Store with durability None
- `dec:varve/MergeCommitsExempt` — Merge commits are exempt from the issue reference, by parent count
- `dec:varve/MergedOnlyGreen` — An expected-red pull request is merged only green
- `dec:varve/MissingLegFailsWhereRequired` — A provider test skips when its address is absent, and fails instead where VARVE_AUTH_LEGS_REQUIRED names its leg
- `dec:varve/MissingSubmoduleFailsLoudly` — A guard test fails when a suite submodule is missing, so zero enumerated cases never pass silently
- `dec:varve/MockOAuthServerOnLinux` — navikt mock-oauth2-server on Linux CI, with a committed configuration, exercises client credentials and the authorization code with PKCE against the real middleware
- `dec:varve/ModelIsNaiveOnPurpose` — The reference model is deliberately slow and correct by inspection
- `dec:varve/ModelNamedInTheRecordNotTheCommit` — The traceability record names the model by the identifier the session's metadata reports, filled in by the maintainer where the tool will not write it, and a commit message never carries a model's API identifier
- `dec:varve/ModelPropertyAfterEveryRequest` — After every generated request the property compares the outcome and position, G_P and the dictionary's growth, and over the run as-of reads, diffs and the settings fold
- `dec:varve/ModelSharesNoCode` — The reference model shares no code with the store, not even a helper
- `dec:varve/ModuleShippedInTheAssembly` — The JavaScript the backends call is a constant in the assembly, imported once per runtime through JSHost.ImportAsync from a data URL, so a host deploys and registers nothing
- `dec:varve/MonotoneTimestamps` — The sequencer assigns each commit's timestamp as max(clock, ts(head)), so timestamps are monotone even across a clock that steps backwards (I5)
- `dec:varve/MoveToNextLtsOnly` — Varve moves to the next LTS during its release window once the AOT and WASM smoke builds pass on it, and never targets an STS release
- `dec:varve/NameIsTheHostsIdIsTheStores` — The name is the host's and maps to a directory under the configured root; DatasetId stays the store's
- `dec:varve/NegatedPropertySetIsAFilteredScan` — A negated property set is a filtered scan in each direction it names
- `dec:varve/NetOfOrderedOperations` — The sequencer applies a request's operations in order to an overlay on the pinned state and commits the net result
- `dec:varve/NewestEntriesInMemory` — The commit index holds the newest DatasetOptions.CommitCache entries in memory, 4,096 by default, and maintenance writes the oldest half out when twice that is held and merges the newest two blobs while the newer is as large as the older
- `dec:varve/NewestRunDecides` — A scan merges the runs' ranges, and for equal keys the newest run decides
- `dec:varve/NoAmbientClockOrRandomness` — Varve.Store reads no ambient clock and no ambient random source, enforced by a banned-symbols list scoped to the deterministic projects
- `dec:varve/NoApiKeysEver` — OIDC bearer tokens are the only accepted credential, and no later decision adds API keys or another secret scheme
- `dec:varve/NoBufferPerSubscriber` — There is no buffer per subscriber: the log is the buffer, and a slow subscriber costs reads, not memory
- `dec:varve/NoCallerHeldWriteLock` — Serialisation is a property of the sequencer, and a caller never holds a write lock across a decision
- `dec:varve/NoCodeCopiedWhateverTheLicence` — No code is copied from Oxigraph or dotNetRDF, whatever their licences permit
- `dec:varve/NoCommittedCs0618Downgrade` — No committed configuration downgrades CS0618
- `dec:varve/NoDefaultClockOrRandomness` — There is no default clock or randomness, and a query that needs one fails naming the option to set
- `dec:varve/NoDestructiveCompaction` — The log before a checkpoint is retained, and no feature may depend on removing bytes from it
- `dec:varve/NoDirectoryFlush` — The file backend flushes no directory, because no managed API opens one; a lost entry of a new segment is a segment never created
- `dec:varve/NoLinqInLayersZeroToFour` — System.Linq.Enumerable is banned in projects at layers 0 to 4, test assemblies excepted
- `dec:varve/NoLongLivedBranches` — Work not ready for the trunk lives behind a feature flag or stays local, never on a long-lived branch
- `dec:varve/NoManagedEngineAdopted` — No managed storage engine is adopted, ZoneTree for owning its file I/O and FASTER for its hash model among them
- `dec:varve/NoMultiTargeting` — The packages are not multi-targeted without an ADR of their own
- `dec:varve/NoNativeAssetInShippedClosure` — No package reaching a published Varve artifact contributes a native asset; build-time and test-only packages are exempt, and eng/native-assets.cs enforces it
- `dec:varve/NoNumericPackage` — No third-party numeric library enters the register, and BigInteger and Decimal serve only as test oracles
- `dec:varve/NoOxigraphEndpoints` — No Oxigraph endpoint is imitated
- `dec:varve/NoSealOnShutdown` — The active segment is not sealed on shutdown
- `dec:varve/NoSkolemisation` — Varve mints no skolem IRIs and gives /.well-known/genid/ no meaning; skolemisation on request is the recorded extension
- `dec:varve/NoStoreFlag` — --no-store obtains a token and writes nothing to disk, the mode for CI and shared machines
- `dec:varve/NoSuppressionOfDdOrVarveRules` — A DD or VARVE rule is never suppressed by pragma, SuppressMessage or an editorconfig downgrade, and a DesignDecision citing a filed decision is the only exception path
- `dec:varve/NoTemporalIndex` — The default projection stores the current state only, and history that needs routine querying belongs in the graph as data
- `dec:varve/NoTestPackageForHosting` — The tests host the server and the OIDC issuer on Kestrel on loopback and sign tokens with the BCL, taking no TestHost or JWT package
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
- `dec:varve/OneExecutableIsServerAndCli` — Varve.Server is the tool varve: one executable, one composition root, one AOT publish; varve serve and a bare varve run the server of ADR 0101 unchanged
- `dec:varve/OneOverlayImplementation` — One overlay implementation serves as-of reads, diff and pre-commit validation
- `dec:varve/OnePointZeroIsAnApiFreeze` — 1.0 is a public API freeze, reached by the roadmap's definition and not by the number looking ready
- `dec:varve/OneProcessPerDirectory` — A dataset directory is opened by one FileStorage at a time, across processes and within one
- `dec:varve/OneSequencerPerDataset` — One sequencer per dataset processes commit requests one at a time and assigns dense, ascending positions (I1)
- `dec:varve/OnlyAppPinnedChecksCount` — A required check is matched by name and so is spoofable by any workflow a pull request adds; it holds against a pull request only when pinned to an App whose key no such workflow can reach, and agent review is pinned to the gates App
- `dec:varve/OnlyTheMaintainerMergesAndReleases` — Only the maintainer merges a pull request and only the maintainer releases
- `dec:varve/OnlyTheNewestSegmentUnsealed` — Segments are numbered by the backend in ascending order, and only the newest may be unsealed or appended to
- `dec:varve/OpenQuestionsAreArtefacts` — An open question in an ADR is a first-class artefact, written under the decision it affects and never resolved in passing
- `dec:varve/OpenReadsTheLogSinceTheLastCheckpoint` — Opening reads every record and commit header, and the bodies after the newest valid checkpoint, which carries the dictionary
- `dec:varve/OptimiserIsAlgebraToAlgebra` — The optimiser is a function from algebra to algebra with no plan type, applied by default and skippable by a caller
- `dec:varve/OptimiserNeverMovesNondeterminism` — The optimiser never folds or moves a call to RAND, NOW, UUID, STRUUID, BNODE or an extension function
- `dec:varve/OptionalExpectedPosition` — An expected position is optional per request, and a request whose expected position differs from the readable head is rejected with Conflict(head) and changes nothing
- `dec:varve/OrderedDurability` — Every record of a commit is durable before its closing record, and the closing record's durability is the commit point
- `dec:varve/OutcomeStatusCodes` — Committed and NoChange are 204, or 201 for a created graph; Conflict 409; Rejected 422 with the report; Unavailable 503 with Retry-After
- `dec:varve/OverlayIsALayer1QuadSource` — Overlay(B, (A, R)) = (B minus R) union A is a quad source in Varve.Rdf at layer 1, merged at scan time and exact under the effective-delta invariant
- `dec:varve/OverlayPlacementRestsOnOpaqueHandle` — The overlay's layer 1 placement depends on the opaque term handle of ADR 0022, so a change to 0022 is a change to it
- `dec:varve/OwnedTermEqualityIsTermEquality` — RdfTerm equality is RDF 1.1 Concepts section 3.3 term equality, permanently, and Varve.Xsd's value equality never changes it
- `dec:varve/OwnedTermIsASealedClass` — RdfTerm is a sealed class with static factories and no public constructor that owns its bytes and caches its hash
- `dec:varve/OxigraphTieBreakers` — Where the protocol is silent, Oxigraph's server behaviour decides, and each such choice is listed in ADR 0092's table
- `dec:varve/PackDryRunOnPullRequests` — Every pull request packs and eng/package-metadata.cs reads each package's nuspec back, failing closed on a missing repository element
- `dec:varve/PackableAssemblyDeclaresLayer` — VARVE0005: a Varve project that is neither a test assembly nor Varve.Analyzers declares ArchLayer, and a packable one always does
- `dec:varve/PackageLicenceExpression` — PackageLicenseExpression is the SPDX expression MPL-2.0, and eng/package-metadata.cs reads it back out of every built package
- `dec:varve/PackageMetadataSetOnce` — Package metadata is set once in Directory.Build.targets, with the licence as an SPDX expression and the icon embedded, never licenseUrl or iconUrl
- `dec:varve/PackagesAtLayers3To5HaveOneModelNamespace` — Each package at layers 3 to 5 whose contracts name its own data types has one DomainModel namespace for them, the package's root namespace plus Model; the engine namespaces themselves stay undeclared
- `dec:varve/PackagesVersionTogether` — Every package versions together from one tag and ships as a set
- `dec:varve/PaddingIsADatasetSetting` — Length-hiding padding to a multiple of 16 bytes, applied before the MAC, is a dataset setting off by default
- `dec:varve/PathsNormalisedFirst` — A normalisation pass, always applied before the optimiser, rewrites link, inverse, sequence and alternative at the top of a path into triple patterns, swapped paths, joins and unions, recursively
- `dec:varve/PathsStayInOneGraph` — A closure runs within one active graph, and once per named graph when the graph variable is unbound
- `dec:varve/PatternsEvaluateOverReadableScope` — A DELETE WHERE, a DELETE/INSERT WHERE and every other pattern of an update evaluate over the readable scope, so a caller cannot delete what it cannot read; UpdateOptions carries ReadScope and WriteScope
- `dec:varve/PerRunTermFilter` — Each run carries a blocked Bloom filter over its terms' content hashes, eight bits a term, consulted before the run's hash index on a lookup by term
- `dec:varve/PermissionsAreCumulative` — A claim value granting write grants read, one granting admin grants all three, and a grant names a configured dataset
- `dec:varve/PermissionsArePolicyNames` — Every endpoint authorises imperatively and first, through the host's IAuthorizationService given in ProtocolOptions, by the policy names varve:read, varve:write and varve:admin with the DatasetName as resource; a refusal is a challenge or a forbid with no detail
- `dec:varve/PinCapturesTheVersion` — Pin() captures the current version by reference, so later commits leave it untouched
- `dec:varve/PinContractDocumentedTwice` — The pin contract is documented on Dataset.Pin() and on the evaluator's entry point
- `dec:varve/PinHeldForTheResponse` — A read's pin or as-of view is taken when the endpoint starts and released when the response has finished, through RegisterForDisposeAsync
- `dec:varve/PinLivesForOneOperation` — A pinned read has the lifetime of one operation, because while it is held nothing it reads can be dropped or archived
- `dec:varve/PinPerQueryExecution` — A pinned read lives for one query execution, from before evaluation until the last result is consumed or the consumer stops, and is never cached or shared
- `dec:varve/PinReleasedBeforeSubmit` — The update's pin is released before the composed delta is submitted
- `dec:varve/PlaintextConfinement` — No file in log/, no checkpoint and no filtered subscription record ever holds a private term's plaintext or key material, which exist only in memory and under derived/ tagged by KeyId (I9)
- `dec:varve/PositionIsAWrapper` — Every log position is a Position over long, ordered as I1 requires, with no public arithmetic
- `dec:varve/PositionOnEveryLogResponse` — Every response that touched the log carries Varve-Position and an ETag of that position
- `dec:varve/PositionPersistedWithState` — A projection persists its position atomically with its state
- `dec:varve/PreferBcl` — A package enters only when the BCL does not do the job
- `dec:varve/PrereleaseUntilSparqlConformance` — The first tag is v0.1.0-preview.1, and versions stay 0.x prerelease until the core passes the SPARQL conformance suites
- `dec:varve/PrivateAddressesRefusedByDefault` — Only http and https; loopback, link-local, private and unspecified IP literals and localhost names are refused unless AllowPrivateAddresses is set; names are not resolved by the policy and credentials in the authority are refused
- `dec:varve/PrivateEntriesRefused` — The decoder reads private entries, Erasure payloads and the erasure-mode setting, and the store refuses to open a log holding them until erasure mode exists
- `dec:varve/PrivateEntryLayout` — A private dictionary entry is term kind 4: a 16-byte key id, a 32-byte synthetic IV and the ciphertext with its length; an Erasure commit's payload is a 16-byte key id, and settings field 2 is erasure mode
- `dec:varve/PrivateIdsIndependentOfContent` — Private ids are counters in their own class, independent of content and never interned
- `dec:varve/PrivateTermsCompareByPlaintext` — A readable private term compares by its plaintext term against private and canonical terms alike, and a shredded one is equal only to itself
- `dec:varve/PrivateTermsHashByValue` — When private terms exist, a source's comparer hashes by value for every class of id
- `dec:varve/ProblemDetailsEverywhere` — Every error response is an RFC 9457 problem whose type is an IRI under https://w3id.org/varve/problems/, and a SPARQL syntax error carries line, column and offset
- `dec:varve/ProcessGatesPortToHowWeWork` — The process gates (changelog with the release cut, licence headers, issue references, DCO, package metadata, native assets, release pending) are ported to mindovermachine-dev/how-we-work beside repo-standard
- `dec:varve/ProjectLicence` — Varve is licensed under MPL-2.0, file-level copyleft, with the canonical text verbatim in LICENSE
- `dec:varve/ProjectionRebuildEquivalence` — A projection may be dropped and rebuilt from position 0 or a checkpoint, and a rebuilt projection is observationally equal to a maintained one (I8)
- `dec:varve/ProjectionStateIsAnImmutableVersion` — The default projection's state is an immutable version of its position and runs, published by one reference write
- `dec:varve/ProjectionStateIsOneBlob` — The default projection's persisted state is one derived blob naming its runs and its position, replaced atomically
- `dec:varve/ProjectionStatusInStatus` — /status reports the default projection's position, its lag behind the head and its failed state, beside 7a's fields and the dataset's state
- `dec:varve/ProseNamesNoProduct` — Prose describes the project as developed with AI assistance under human review and names no product, while records and issues name the tool and model
- `dec:varve/ProtocolContractVocabulary` — Varve.Protocol's contract vocabulary adds Varve.Store and Varve.Sparql in its own project file, and the global value is unchanged
- `dec:varve/ProtocolIsALayer5Library` — Varve.Protocol, layer 5, holds the SPARQL Protocol, the Graph Store Protocol, the service description, the change feed and the diff as IEndpointRouteBuilder extensions with no host assumption and no authentication type
- `dec:varve/ProtocolSeamRevisitCondition` — Protocols stay at layer 5 behind host-bound seams until a third integration must be reached the same way, when the table is superseded with protocols at 6 and hosts at 7
- `dec:varve/ProtocolSuitesAsGates` — sparql11/protocol and sparql11/graph-store-protocol are gates, http-rdf-update runs as deprecated under its own guard, and service-description's three names are our own checks, all over the memory and file stores
- `dec:varve/PublicApiBaselinePerPackage` — Every packable project tracks its public API in PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt, so a new public member is a reviewable line
- `dec:varve/PublishOnTagAfterEveryGate` — publish.yml publishes on a v* tag only after the full suite and every gate pass, and never from a pull request
- `dec:varve/PublishedDependencyListEmpty` — A published Varve package's dependency list is empty unless an ADR says otherwise
- `dec:varve/PushPathAndRecordAttest` — A sandbox commit is attested by the push path and its traceability record instead of a signature
- `dec:varve/QuadCountInRdf` — A cardinality estimate's count is a QuadCount in Varve.Rdf
- `dec:varve/QuadSourceContractInRdf` — The quad source contract lives in Varve.Rdf at layer 1, not in Varve.Store, because the evaluator at layer 3 needs it
- `dec:varve/QueryOperations` — Queries are served by GET, form POST and direct POST, with default-graph-uri and named-graph-uri replacing FROM and FROM NAMED, negotiated over XML, JSON, CSV and TSV results and N-Triples, N-Quads, Turtle and TriG graphs
- `dec:varve/RatchetGatesConformance` — CI runs the conformance suite without gating on its exit code and gates on the ratchet instead
- `dec:varve/RatchetIsBehaviourEvidence` — The conformance ratchet is the behavioural half of the evidence, and an exemption added to make a suite green is an accepted behaviour change
- `dec:varve/Rdf12WinsWhereFormsDiffer` — Where the RDF 1.1 and 1.2 canonical forms differ, 1.2 wins, and everything RDF 1.1 N-Triples accepts is still read
- `dec:varve/RdfLearnsNoPositions` — Varve.Rdf learns nothing of positions, time or storage
- `dec:varve/RdfXmlFixturesTranslatedOffline` — The SPARQL suites' RDF/XML files are translated once, offline, by dotNetRDF into committed hash-guarded N-Triples, deleted when Varve.RdfXml passes its own suite
- `dec:varve/RdfcInVarveRdf` — RDFC-1.0 is public API in Varve.Rdf over IQuadSource, returning the canonical N-Quads bytes and the issued identifiers, SHA-256 by default with SHA-384 and SHA-512 selectable
- `dec:varve/RdfcRefusesBlankInTripleTerm` — RDFC-1.0 refuses a triple term with a blank node inside it
- `dec:varve/RdfcWorkLimit` — A configurable work limit counting hash calls and permutations per blank node that needs them, defaulting to 1,000 from the suite's measured maxima, throws CanonicalisationLimitException when exceeded
- `dec:varve/ReadBoundedTwice` — A read is bounded by QueryTimeout for evaluation and PinnedReadLifetime for the pin, from the injected clock, linked with the request's abort
- `dec:varve/ReadBytesAreImmutable` — Bytes returned by a segment read are immutable and may be held, and a backend that cannot promise it copies; derived bytes are copied into the reader's buffer
- `dec:varve/ReadForeverBindsTheLog` — The read-forever rule binds log/; a derived/ file of a version the store does not read, of another dataset or naming another header hash is a cache miss and is rebuilt
- `dec:varve/ReadableHeadIsLastClosedCommit` — The readable head is the position of the last closed commit, and an unclosed commit's records are invisible to every read, subscription and projection
- `dec:varve/ReadersHoldDirectoriesInChunks` — A reader holds a run's fences and block starts in chunks below the large object heap, read and written in pieces, and holds a checkpoint's sections sparsely: every sixteenth fence, its block starts and first keys read from the blob at a seek
- `dec:varve/ReadinessIsProjectionAtHead` — GET /ready is 200 only when every dataset is open, not failed and at the head of its default projection; GET /live is 200 while serving
- `dec:varve/RebuildFromNewestCheckpoint` — The default projection rebuilds from the newest valid checkpoint as its base run and applies the tail, or from the empty run
- `dec:varve/RecordLayout` — A record is a 128-byte header of body length, kind, flags with the closing flag, position, index, prev, the body's SHA-256 and the header's own hash, then the body; a record never spans segments
- `dec:varve/RecordNeverSpansSegments` — The store seals the active segment when an append would exceed the segment size, so a record never spans two segments
- `dec:varve/RecordsAndCommits` — A record is the physical unit of append, and a commit is one or more records of which the last carries a closing flag
- `dec:varve/RecordsIndependentlyDiscardable` — A record never depends on anything outside the log having been updated when it was written
- `dec:varve/RecoveryIsBoundedToo` — Opening after a crash during or just after a bulk load holds at most a bound of any one commit's body, verifying a larger one as it passes, and adopts a delta run that continues the loaded index rather than replaying the load from the log
- `dec:varve/RedirectsNotFollowed` — A 3xx answer is a failure of the request, because a redirect is an address the policy did not see
- `dec:varve/ReferenceModelFromTheSpec` — Varve.Store.Tests holds a reference model, a fold over the same requests written from the specification alone, over terms rather than ids
- `dec:varve/RefusalNamesPositionAndBranch` — A chain refusal says which position failed and where the branch point was
- `dec:varve/RefuseRatherThanGuess` — A store refuses to open a log whose chain does not verify, and refuses to continue from a head that is not its own
- `dec:varve/RegisterCitesAdr` — Every PackageVersion carries Adr naming the decision that admits it, transitive pins included, and eng/dependency-register.cs fails on a missing or dangling citation
- `dec:varve/RejectedRequestLeavesNoTrace` — A rejected or empty request leaves no commit, no dictionary allocation and no gap in positions
- `dec:varve/ReleaseNotesAreTheDescriptorSummary` — The GitHub Release's notes are the descriptor's summary and its title is the tag message, read by publish.yml before anything is pushed and published after the NuGet push
- `dec:varve/ReleasePlaintextOnlyAfterVerify` — Decryption recomputes the tag, compares it with FixedTimeEquals and releases the plaintext only on equality
- `dec:varve/ReleaseProposedByItsDescriptor` — A release is proposed by adding releases/<version>.yaml to a pull request, format 1 in docs/releases.md, and cut by landing that pull request at its approved head; no other step, credential or person is in the path
- `dec:varve/ReleasesBeforeDescriptorsAreRecorded` — A version tagged before descriptors existed is recorded by a descriptor pinned to its tagged commit, which is checked for its shape and storage format and never cut
- `dec:varve/RemoteCliUsesBearerTokens` — The CLI talking to a remote server uses bearer tokens from the device code or client credentials flow of ADR 0037 and stores nothing but the issuer's refresh token and what identifies its issuer, in the credential file of this ADR
- `dec:varve/ReplayIsOverCommits` — Replay is over commits, never over requests, because a commit's delta depends on the state it was applied to
- `dec:varve/RepoStandardBuiltHere` — tools/repo-standard holds a CLI over a YAML declaration of repository settings and a composite GitHub Action, built here and not published from this repository
- `dec:varve/RepoStandardHeldToVarveRules` — While it lives here the tool is held to Varve's rules: warnings as errors, the MPL-2.0 header, register citations, issue references, sign-off and traceability
- `dec:varve/RepoStandardIsolated` — Nothing in src/ references repo-standard and it references nothing there, with its own solution and its own Directory.Build.targets
- `dec:varve/ReportIsALocalToolAgainstABaseline` — DecisionDriven.Report is a local .NET tool pinned in .config/dotnet-tools.json and registered in Directory.Packages.props, run by eng/decision-report.cs over the shipped assemblies against a committed baseline report, in CI and never as a gate
- `dec:varve/RequestBlankNodesAreFresh` — A blank node term in a request is always fresh: each distinct label is one new node in that request, even a label TryExternalise produced
- `dec:varve/RequiredCheckNamesAreReportedJobs` — Every check ruleset 1 requires is a job name some workflow running on pull requests reports, and eng/required-checks.cs fails a change where one is not
- `dec:varve/ResultSizeCap` — ResultSizeCap bounds the bytes a read writes, and reaching it is reaching a limit
- `dec:varve/RetiredRulePagesStay` — A retired rule's page stays as a stub naming what replaced it, so the id still resolves
- `dec:varve/RoslynFloor` — Microsoft.CodeAnalysis.CSharp and its Workspaces package are pinned to the 5.0.0 floor, the Roslyn of the first .NET 10 SDK, and a floor is raised only with a reason
- `dec:varve/RowLevelIsMilestone9` — Graph-level is what the index key gives for free; row-level access per subject or classifier is milestone 9's beside erasure and is not approximated here
- `dec:varve/RuleDeliverables` — A new rule is delivered as the analyzer, its tests, its release-tracking entry and its rule page
- `dec:varve/RulePagePerRule` — Each VARVE rule has a page at docs/rules/VARVEnnnn.md that its HelpLinkUri points to and that links to the motivating ADR
- `dec:varve/RunsAreImmutable` — A run holds sorted asserted and retracted key arrays per order and is never modified, and a commit adds one run with no object per quad
- `dec:varve/RunsDeletedWhenUnread` — A run the projection retires is deleted only once its last reader has let go, by the next maintenance round, and run names are never reused
- `dec:varve/SameLayerReferenceIsViolation` — A same-layer reference is a violation, not an exception: two packages in one layer that need each other are one package or two layers
- `dec:varve/SandboxKeyNeverRegistered` — The sandbox platform's signing key is never registered as a signing key on the maintainer's account
- `dec:varve/SandboxSignatureException` — Commits from AI sessions in the cloud sandbox are exempt from the signature requirement through the pushing App's bypass, a stated deviation from the standard
- `dec:varve/ScalarValueOrders` — Numerics compare by the XPath total order over the promoted type with NaN unordered, strings by code point, and booleans false before true
- `dec:varve/ScopedReadsThroughGraphScopedQuadSource` — A read whose readable scope is not all evaluates over GraphScopedQuadSource, a layer-1 wrapper that filters every Match, Contains and Estimate by graph so that an unreadable graph is unobservable through any pattern or enumeration
- `dec:varve/ScopedWritesCheckedBeforeSubmit` — The effective delta is computed as before and every quad is checked against the writable set before the submit; one unwritable quad fails the request with 403 naming the graph and nothing commits
- `dec:varve/ScriptLeavesWhenItsPortIsConsumed` — A script leaves eng/ when its port is released and Varve consumes it from there; one issue in each destination, with the provenance on Varve's side, is the exit criterion
- `dec:varve/SdkPinnedLatestFeature` — global.json pins the SDK to 10.0.401 with rollForward latestFeature
- `dec:varve/SealIsATrailer` — Sealing appends a trailer naming the last closed position and its header hash, so a seal survives a copy and a closed trailer's successor must continue the chain
- `dec:varve/SecondBackendInTheTests` — Varve.Store.Tests carries a second storage backend written against public members only and runs the contract tests against it
- `dec:varve/SecondExecutableRevisitCondition` — A second executable moves the host wiring both would share into a layer-5 Varve.Hosting package and supersedes the one-executable ruling
- `dec:varve/SegmentPreamble` — Every segment begins with a header carrying VRVL, the format version, the dataset id, the segment id, its first position and the header hash before it, every header integer little-endian and fixed-width
- `dec:varve/SegmentSizeIsAnInput` — Segment size is an input to the storage contract, not a constant compiled into a backend
- `dec:varve/SelectorWithoutErasureMode` — Without erasure mode, access is served by an optional selector over G_head, and erasure cannot be served at all
- `dec:varve/SemVerKeptDuringZeroX` — During 0.x the versioning rules are followed anyway, and a breaking change moves the minor while the major is zero
- `dec:varve/SequencerWaitsForDefaultProjection` — The sequencer refuses the next commit with Unavailable until the default projection is at the readable head
- `dec:varve/ServerAdminGrant` — A server admin is a caller with a claim value in Varve:Auth:Server:Admin, administers every dataset, and alone creates, deletes, opens and closes datasets; the policy name is varve:server-admin
- `dec:varve/ServerIsNativeAot` — Varve.Server publishes Native AOT with minimal APIs only and source-generated JSON, and the publish is a CI gate
- `dec:varve/ServerIsTheHost` — Varve.Server, layer 6, is the executable and composition root: configuration, hosting, authentication and the wiring of datasets, clocks and validators
- `dec:varve/ServiceResultsNegotiatedJsonThenXml` — The handler asks for SPARQL results JSON then XML, parses the answer with SparqlResultsReader, and treats any other media type, status, a body over the cap or a timeout as a failure returned with the endpoint and the reason
- `dec:varve/ServiceSilentIsTheEvaluators` — The handler never consults SILENT: it returns failures and the evaluator errors or answers one empty solution per ADR 0055; incoming solutions are not pushed to the endpoint
- `dec:varve/ServiceSuiteRunsOverHttp` — sparql11/service runs end to end over HTTP against one in-process server per qt:serviceData endpoint, through HttpServiceHandler with the manifests' endpoint IRIs allowed and mapped to loopback by a test-side handler, beside its in-process run
- `dec:varve/ServiceVariablePerDistinctIri` — SERVICE ?v invokes the handler once per distinct IRI ?v takes, and an unbound or non-IRI ?v fails that invocation
- `dec:varve/SessionsNeverWriteAcceptedBy` — A session never writes accepted-by for a decision it filed, and transcribes only acceptances an ADR already records
- `dec:varve/SettingsAreACommitKind` — Settings are a commit kind beside Data and Erasure, sequenced like any other commit with an agent, a cause and a position
- `dec:varve/SettingsAreAFoldOfTheLog` — A dataset's settings at P are a fold over the Settings commits up to P, a pure function of the log that travels with it
- `dec:varve/SettingsCommitOverHttp` — POST /settings makes a Settings commit through Dataset.ChangeSettingsAsync with the caller as agent, the request id as cause and If-Match as the expected position
- `dec:varve/SettingsCommitsHaveEmptyDelta` — A Settings commit carries an empty delta and is exempt from I4, like an Erasure commit
- `dec:varve/SharedFrameworkIsTheRuntime` — The ASP.NET Core shared framework is referenced by FrameworkReference and is part of the runtime, not a registered package
- `dec:varve/ShipACheckpointAndTheLog` — A replica is bootstrapped by copying files: the manifest, the log up to the end of the shipped position's commit, and the newest checkpoint at or below it, which the replica opens at exactly that position, replaying only the log after the checkpoint; there is no protocol
- `dec:varve/ShippedBaselineIsBreakEvidence` — A removed or altered line in PublicAPI.Shipped.txt is a breaking change, and surface in PublicAPI.Unshipped.txt carries no promise until it ships
- `dec:varve/ShrunkCounterexamplesKept` — Each shrunk counterexample becomes a named regression case beside the property
- `dec:varve/SignOffByAuthorOrResponsibleHuman` — Every non-merge commit is signed off by its author by name and email, or, for an agent's commit, by its responsible human or a delegate; exempt automation is skipped
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
- `dec:varve/StatusIsACheckedProjection` — README.md's Status section and docs/roadmap.md's milestone headings are checked against the release descriptors, the conformance baseline and the projects, in CI and at the commit under release
- `dec:varve/StatusIsTheAdminEndpoint` — GET /datasets/{name}/status, the one admin endpoint of 7a, reports the dataset's id, head, durability, settings, checkpoints and failure
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
- `dec:varve/SystemCommandLineInTheHost` — System.CommandLine 2.0.x parses the command line, registered in Varve.Server alone, on the condition that the AOT publish stays green
- `dec:varve/TargetCurrentLts` — Varve packages target net10.0, the current LTS, set once in Directory.Build.props
- `dec:varve/TermComparerIsTermEquality` — TermComparer is RDF term equality, never value equality, which is the evaluator's at layer 3
- `dec:varve/TermIdClassInHighBits` — A TermId is 64 bits with its class carried in the high bits, read with a mask, so a reader knows the class without a lookup
- `dec:varve/TestsRunOnTestingPlatform` — dotnet test runs on Microsoft.Testing.Platform, selected in global.json, so Microsoft.NET.Test.Sdk and the VSTest adapter are deliberately absent
- `dec:varve/TheGatesAppTags` — The tag and the GitHub Release are created by the gates App with an installation token, never by GITHUB_TOKEN and never under a person's or a composite identity
- `dec:varve/TheLogIsTheWriteAheadLog` — derived/ has no write-ahead log of its own and recovers by replaying the log from the projection's persisted position
- `dec:varve/ThreeDependencyClasses` — Dependencies are runtime, build-time or test-only, admitted on different bars, and a test-only stopgap states its exit criterion
- `dec:varve/TieredRunMerging` — Runs are merged in tiers so a version holds O(log n) runs, and a merge into the oldest run drops its retractions
- `dec:varve/TimeTravelAdvertised` — The service description advertises the headers, both selector forms and the conditional requests
- `dec:varve/TlsTerminationIsTheDeployments` — TLS termination is the deployment's job, and the server serves a supplied certificate but manages none
- `dec:varve/ToRequestTermMapping` — ToRequestTerm maps a view's handle to RequestTerm.Existing and a provisional handle to its term, a provisional blank node taking a label unique within the staging view
- `dec:varve/TokenOnTheAuthorizationHeaderOnly` — The tool puts its GitHub token on the Authorization header only, never logs or writes it, and never sends it to an extends URL or follows a link off the API host
- `dec:varve/ToolPackageFrameworkDependentAotIsTheGate` — The tool package is framework-dependent with ToolCommandName varve, and the Native AOT single-file binary published by the native aot job on both runners, with the CLI smoke run against it, is the gate
- `dec:varve/TopicSummariesForOutsideWork` — A design conversation held outside the repository is recorded as a topic summary naming its decisions, and the maintainer holds the transcript
- `dec:varve/TornTailSealedAndSkipped` — Recovery follows the chain across segments: a torn or unclosed tail is ignored and its segment sealed with an abandoned trailer, segments beyond a copy point are abandoned, and any other break refuses to open; bytes that do not verify are a torn tail only when no record of a later position, no record after a broken segment header, and no sealed trailer verifies after them in the same file
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
- `dec:varve/UnreadableGraphIsAbsentNotAnError` — A FROM or FROM NAMED naming an unreadable graph names an empty graph, never an error, so no error text reveals a graph's existence
- `dec:varve/UnresolvableAsOfIs404` — A time before the first commit, a position after the head and a position below the archive horizon are 404, each with its own problem type
- `dec:varve/UnsilencedServiceFailureFailsQuery` — A failed SERVICE without SILENT fails the query with an exception naming the endpoint
- `dec:varve/UnwritableSettingsReported` — Settings no API can write are read and reported or left out, never half supported
- `dec:varve/UpdateConflictNotRetried` — A Conflict is returned, not retried, unless the caller sets ConflictRetries, each retry re-pinning and re-evaluating
- `dec:varve/UpdateExpectedPosition` — Varve.Sparql.Store's UpdateOptions gains ExpectedPosition, which returns Conflict without evaluation when the pin is elsewhere
- `dec:varve/UpdateIsAtMostOneCommit` — An update request is one commit of its composed delta with the expected position P, or none when its net effect is empty
- `dec:varve/UpdateOperations` — Updates are served by form POST and direct POST, with using-graph-uri and using-named-graph-uri per section 2.2.3, and naming them beside USING or WITH is 400
- `dec:varve/UpdateOverChainedOverlays` — Each update operation is evaluated in order against the overlay of the pinned staging view and the deltas before it, each delta exact against that source
- `dec:varve/UpdateSeamCarriesAccessScope` — ISparqlUpdateExecutor.ExecuteAsync takes the caller's CallerScope and the host passes it to the update options
- `dec:varve/UpdateSingleEntryPoint` — SparqlUpdate.ExecuteAsync over a Dataset is the one entry point, with UpdateOptions, ILoadSource, LoadedDocument and SparqlUpdateException
- `dec:varve/UpstreamDestination` — Upstream defects go to Oxigraph's tracker one per issue, spec gaps to the W3C suites or the working group, and design findings to Oxigraph Discussions, each linked from a Varve issue
- `dec:varve/UpstreamLicensing` — Code crosses into Oxigraph only from its copyright holders contributing directly, tests go to the W3C suites under their terms, and shared tooling stays in Varve under MPL-2.0
- `dec:varve/UriAllowedInProtocolAndServer` — The System.Uri ban moves to eng/BannedSymbols.Uri.txt, added to every packable project except one setting VarveSpeaksHttp, which Varve.Protocol alone sets; System.Uri there is for transport addresses and never for an RDF IRI
- `dec:varve/UriAllowedInProtocolClient` — Varve.Protocol.Client sets VarveSpeaksHttp too, for transport addresses only, with every IRI still a Varve.Iri value checked on its bytes before a Uri is built
- `dec:varve/UriBanNarrowedPerProject` — The repository-wide System.Uri ban is narrowed before layer 5 needs System.Uri, by a per-project banned-symbols file and not by call-site suppressions
- `dec:varve/ValidatorAcceptsOrRejects` — A validator accepts, optionally with an attachment carried in the commit's metadata, or rejects with a report, and a rejection leaves no trace
- `dec:varve/ValidatorBindingNotInTheLog` — Binding validators to a dataset is an option of the open dataset, never a fact in the log
- `dec:varve/ValidatorContractInStore` — The pre-commit validator contract is a store concern at layer 4, and a validator that uses it is a layer 5 composition
- `dec:varve/ValidatorsStatedTwice` — Validators are stated over the store's types and over the model's term sets, and their verdicts must agree before anything else is compared
- `dec:varve/ValuelessLiteralIsATypeError` — A lexical form with no value in range is a type error to the evaluator, never a parse failure
- `dec:varve/VarveIdReservations` — ADR 0004's reservation table is retired: VARVE0001 and VARVE0002 are retired for ever, and VARVE0003 to VARVE0008 are released to their DD successors or renumbered
- `dec:varve/VarveRuleIdScheme` — Varve rule ids are VARVE and four digits, allocated in order and never reused; a retired id stays retired
- `dec:varve/VarveRulesUnderDd0008` — dd_rule_id_prefixes is VARVE, and dd_banned_names is left at the package's default list
- `dec:varve/VarveVocabularyNamespace` — The service description's own terms are under https://w3id.org/varve/ns#
- `dec:varve/VersionFromTagByMinVer` — A package's version comes from its git tag through MinVer, and no Version property exists
- `dec:varve/VersionParameterAdopted` — SPARQL 1.2's version is accepted as a request parameter and as a media-type parameter, values 1.1, 1.2-basic and 1.2, the parameter winning over the text and an unknown value being 400
- `dec:varve/VersionedKeySeparation` — The MAC and encryption keys are derived from the subject key by HKDF-SHA-256 under distinct, versioned info strings
- `dec:varve/VersionsAreNeverReused` — A version comes after every v* tag by precedence and is never reused; a cut descriptor is immutable, and a correction is a new version
- `dec:varve/VersionsCentralAndResolved` — Every version lives in Directory.Packages.props, resolved from nuget.org when added or changed, and the default is the latest stable
- `dec:varve/W3cSuitesPinnedSubmodule` — W3C test data is a pinned git submodule, advanced only by a commit that says so, and never vendored or fetched at test time
- `dec:varve/WarningsAreErrors` — TreatWarningsAsErrors, AnalysisLevel latest-recommended and EnforceCodeStyleInBuild are on repository-wide
- `dec:varve/WidenContractNotMoveEvaluator` — If the quad source contract is too narrow for an optimiser, it is widened by a superseding ADR, never bypassed by moving the evaluator down a layer
- `dec:varve/WorkersResolveAndSpillBuffers` — The parser fills a ring of operation buffers and workers resolve and spill each full buffer as a sorted run; the parser blocks only when every buffer is busy
- `dec:varve/WrapperOnlyApiDiff` — The public API diff of the change is the wrapper types, the moved namespace and the retyped signatures, and nothing else
- `dec:varve/WrappersAreReadonlyRecordStructs` — Each wrapper is a readonly record struct over one primitive, with an explicit constructor, a Value property and no implicit conversion either way
- `dec:varve/WriteIsAtMostOneCommit` — Every write request is at most one commit: an update through the executor with no retries, a Graph Store PUT or DELETE pinned and expecting the pin, a POST asserting its body
- `dec:varve/XsdParsesAndFormatsSpans` — Each Varve.Xsd type parses from UTF-8 and UTF-16 spans and formats its canonical form into a span, the numeric types without allocating
- `dec:varve/XsdPartialOrderNamedApart` — The XSD partial order on date and time values is available as a second, separately named comparison
- `dec:varve/XsdStringIsFoldedAway` — An explicit xsd:string datatype is folded away as a spelling of the same term, and rdf:langString and rdf:dirLangString are refused as explicit datatypes
- `dec:varve/XsdTypesOutOfScope` — hexBinary, base64Binary, anyURI, QName, NOTATION and the types derived from xsd:string are out of scope and compare by lexical form
- `dec:varve/YamlDotNetParserAndEmitterOnly` — YamlDotNet is the tool's only runtime package, used through its parser and emitter into a JsonNode tree, with no Octokit
- `dec:varve/ZeroLengthPathFromAbsentTerm` — A zero-length path from a term absent from the graph still binds that term, as section 18.4 reads
- `dec:varve/ZitadelOnLinux` — Zitadel and PostgreSQL on Linux CI, seeded through the management API by eng/zitadel-seed.cs, are the real-provider leg with one test each for client credentials and device code

### Citations of a version that is no longer the tip

The decision changed after the code that cites it was written.

- `M:Varve.Store.DiskFileSystem.DiskFile.Read(System.Int64,System.Span{System.Byte})` [DesignDecision] cites `dec:varve/ReadPathByBenchmark` in `src/Varve.Store/FileSystem.cs`
- `N:Varve.Protocol.Client.Model` [DomainModel] cites `dec:varve/ClientModelNamespace`
- `T:Varve.Store.IDerivedStore` [Contract] cites `dec:varve/StorageContractMembers` in `src/Varve.Store/IStorage.cs`
- `T:Varve.Store.ISegmentStore` [Contract] cites `dec:varve/StorageContractMembers` in `src/Varve.Store/IStorage.cs`
- `T:Varve.Store.IStorage` [Contract] cites `dec:varve/SegmentStoreAndDerivedStore` in `src/Varve.Store/IStorage.cs`

