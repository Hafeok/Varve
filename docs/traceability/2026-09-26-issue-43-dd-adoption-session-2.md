# Adopting `DecisionDriven.Analyzers`, session 2 — the package, the Varve rules, layers 0 to 2

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer. Developed with AI assistance
> under human review.

| | |
|---|---|
| **Issue** | [#43](https://github.com/Hafeok/Varve/issues/43), the adoption, used by all three sessions |
| **Date** | 2026-09-26 |
| **Tool** | Claude Code 2.1.283, a cloud session |
| **Model** | `claude-opus-5-5` (Claude Opus 5.5), configured and served. Confirmed from the session's own metadata, not from memory |
| **Session identifier** | `session_012aNo8XhYGVT6rgfvDN69P7` |
| **Branch** | `claude/analyzers-layers-0-2-cl3hky`, one pull request (ADR 0066: a change citing unaccepted decisions reaches `main` only through one) |

## The prompt

> Session 2: reference the analyzers and fix layers 0 to 2
> Session 1 is merged: the ADRs exist and docs/decisions/ holds every ADR as decisions. This session references the package, configures it, retires VARVE0001/0002, implements VARVE0003/0004, declares the model namespaces, and brings layers 0, 1 and 2 (Varve.Iri, Varve.Xsd, Varve.Rdf, Varve.Turtle, Varve.Sparql, Varve.Sparql.Results) to a clean build under every DD rule at error severity. Layers 3 and 4 are session 3.
> Read the analyzer repository's docs/rules/ before the first fix; the Decide: messages assume it. Re-verify the package version and the rule table against the published package; report differences.
> Directory.Packages.props and Directory.Build.props: DecisionDriven.Analyzers with PrivateAssets="all", IncludeAssets="analyzers;build", register entries citing the ADR from session 1; ArchFamily=Varve; ArchLayer per project equal to its VarveLayer, then retire VarveLayer in favour of ArchLayer everywhere it is read (Directory.Build.targets metadata, Varve.Analyzers, eng/), keeping one property; ArchContractTypeAssemblies=Varve.Rdf;Varve.Iri;Varve.Xsd globally in Directory.Build.props, and Varve.Sparql appended in the layer-3 projects' own files only (Varve.Sparql.Evaluation, later Varve.Shacl), never globally: Varve.Store's contract is SPARQL-free by accepted decision and a global widening would let an algebra type onto a store contract with no diagnostic; any later addition is per project the same way; ArchCompositionRoot=true on every layer-6 project (ADR 0060: the composition root is the executable; smoke apps and benchmarks are layer 6 today, the server and CLI join at milestone 7); .editorconfig: dd_rule_id_prefixes=VARVE, dd_banned_names as ADR 0004 had them.
> The set files as AdditionalFiles with DdLedger metadata per the package .targets, pointing at docs/decisions/. Retire the session 1 front-matter check if DDGEN0001 now covers it, or keep it if it checks something the generator does not; say which.
> BannedSymbols.txt per the A13 ADR, each entry with its ADR citation in the comment (the file already uses // comments; keep that).
> Varve.Analyzers: remove VARVE0001 and VARVE0002 and their tests and rule pages (rule pages become "retired, replaced by DD0001/DD0002" stubs that stay, so the ids are documented as taken); implement VARVE0003 and VARVE0004 as A13 states them, matching [HotPath] by full name DecisionDriven.HotPathAttribute, with the BCL allow-list as configuration; tests per diagnostic: violating and conforming, including a [HotPath] member calling a non-hot-path Varve member (error) and an allow-listed BCL member (not). Rule pages for both, each citing the A13 ADR.
> tools/repo-standard/ has no ArchLayer and is not in the family; DD0018 and the other layer-independent rules still apply to it. Fix what fires there in the same way; it is leaving the repository, so file nothing new for it beyond what a rule forces.
> 2.2 Order of fixes
> Layer by layer, one package per commit, each commit ending in a green build for that package under every rule at error severity (CS0618 excepted per the expected-red ADR).
> [assembly: DomainModel(...)] for the model namespaces decided in session 1, each citing the decision that defines that model, before any [Contract] is added. DD0010 reports every signature otherwise.
> InMemoryDataset to Varve.Rdf.Datasets (session 1's ADR), with the public API baseline moved, not widened.
> DD0009: every public interface, abstract class and delegate in a layered project gets [Contract(typeof(<Set>.<Key>), Role = "...")] citing the decision that introduced it. IQuadSource cites the quad source contract decision (ADR 0022's enumeration), the overlay ADR 0017's, IParserSubject is test code and out of scope. Where no decision covers a contract, sort it: a real decision question, file it unaccepted in a new set and cite it; or the code just looked like this, take the design change. List every filed decision.
> DD0013–DD0015: naked primitives on model and contract surfaces become the wrapper types session 1's ADR names. In layers 0 to 2 expect few: Varve.Rdf's surfaces mostly carry TermHandle, spans and ReadOnlyMemory<byte>; parse and format members are exempt by the rule's boundary list. Do not wrap where the rule exempts, and do not add [DesignDecision(Scope = Boundary)] where a rename to Parse/TryParse is the honest fix.
> DD0019: any remaining mutable type in a model namespace moves out; the rule is not weakened.
> DD0004: pools get [DesignDecision(..., Scope = ExceptionScope.Pool)] citing the decision that introduced the pool (the parser's pooled builders, the term arena); every other static collection is a finding to sort.
> DD0008: the <auto-generated/> marker in eng/licence-headers.cs and anything else it reports: fix by naming the file as generated where it is, or removing the marker; never by citation.
> Everything else DD0001–DD0019 reports, sorted the same way. Never leave the ____.____ placeholder in the tree.
> Rules for every fix: no #pragma, no [SuppressMessage], no .editorconfig downgrade for a DD or VARVE rule, ever; [DesignDecision] on the symbol citing a filed decision is the only exception path. A finding about an analyzer itself (a false positive, a wrong message, a missing exemption) is an issue in the analyzer repository with the reproduction, never a local workaround; cite the issue number in the report and leave the finding unfixed if the issue blocks it, with the build red on that diagnostic and a note in the PR.
> 2.3 Hot paths, layers 0 to 2
> Mark [HotPath] on the parser cores (Varve.Turtle's line and statement scanners, the escape decoder), the term arena and the quad cursors, citing the decision that made allocation per quad a defect (the brief's constraint 5 as enumerated). Fix what VARVE0003 and VARVE0004 report. The zero-allocation tests must still pass unchanged; the allocation benchmark from 3a and 3b is re-run and reported before and after.
> 2.4 Gates and report
> The full pipeline green except the expected CS0618; the conformance ratchet unchanged at its count; the public API baseline diff for layers 0 to 2 containing only the wrapper types, the InMemoryDataset move, and the attributes. The traceability record and PR bodies: the decision table of newly filed, unaccepted decisions awaiting the maintainer; findings in the two buckets; analyzer issues filed upstream with numbers; the API diff summary; allocation before and after; any DD rule whose Varve fallout argues the rule or its configuration is wrong, argued with the cases, not suppressed.

## Report

### The state, in one paragraph

Layers 0 to 2 are done under every `DD` and `VARVE` rule **except for sites
that analyzer defects block**, and those are filed upstream. By ADR 0062's rule
they are left unfixed and red, and the build is red on them. The first such
site is in `Varve.Xsd` at layer 0 (decision-driven-analyzers#59). So on this
branch **no project above layer 0 compiles without a local override**, and CI
cannot show the rest. Everything below was verified with the command-line
override ADR 0066 allows for `CS0618`, extended *locally only* to the ids the
upstream issues block. Nothing of it is committed. Layers 3 to 5 carry
session 3's findings, now that the rules run there: the evaluator, the store
and the integration are red on `DD` and `VARVE` rules this session did not
touch, as planned.

### Verified against the published package

| Claim | Found |
|---|---|
| Package version (ADR 0063: "`0.1.0-preview.3` is current") | **Differs.** `0.1.0-preview.4` is published for both packages (tag `v0.1.0-preview.4`, `f6b62e2`, 2026-09-26). Taken, by ADR 0063's policy of the latest preview |
| The changelog records what each preview carries | **Differs.** `CHANGELOG.md` at `v0.1.0-preview.4` still files everything after preview.2 under `[Unreleased]`: DD0008's new reports, DDGEN0005, `[Embedded]` generated types. Already filed as decision-driven-analyzers#43. The breaking entries are in this branch's first commit body, as ADR 0063 asks |
| The README's rule table | DD0001–DD0019, DDBUILD0001–0002 and DDGEN0001–0004 as documented; **DDGEN0005 exists and is not in the table** |
| The rules' behaviour matches `docs/rules/` | Except where filed below |

### Disagreements between this prompt and the accepted ADRs

The accepted ADRs won in each case.

1. **`InMemoryDataset` does not move to `Varve.Rdf.Datasets`.** ADR 0067,
   accepted in session 1, replaced that plan, because a sub-namespace of a
   root-namespace model is still model (decision-driven-analyzers#46).
   `InMemoryDataset` is now an immutable value, and `InMemoryDatasetBuilder`
   fills it. The API diff below is 0067's, not a move.
2. **`VARVE0005` is implemented.** The prompt lists `VARVE0003`/`0004`. ADR
   0064 also allocates `VARVE0005` (the half of `VARVE0002` that `DD0001` does
   not cover) and says all three land in session 2.
3. **`dd_banned_names` is unset, not "as ADR 0004 had them".** ADR 0064: the
   option replaces the package's list of ten, so ADR 0004's five would narrow
   it. Already filed upstream as #49.
4. **Retired pages.** The prompt says "retired, replaced by DD0001/DD0002". ADR
   0062 says `VARVE0001` → `DD0001`, and `VARVE0002` → `DD0001` and
   `VARVE0005`. `DD0002` replaces the never-implemented `VARVE0003`
   reservation, not `VARVE0002`. The stubs follow the ADR.
5. **"The brief's constraint 5 as enumerated"** had no set: session 1
   enumerated ADRs only. Filed as `BriefHardConstraints.AllocationPerQuadIsADefect`,
   unaccepted, and every `[HotPath]` cites it.
6. **"The overlay ADR 0017's" contract.** `QuadOverlay` is a sealed class, not
   an interface or abstract class, so `DD0009` does not report it and it cites
   nothing.
7. **DD0008 and `eng/licence-headers.cs`.** There is no marker to remove.
   The `<auto-generated` in that file is a string literal the licence gate
   searches for (line 236), not a header. And `eng/`'s file-based apps are not
   analyzed at all: `eng/Directory.Build.props` deliberately does not import
   the root props. DD0008 reported nothing anywhere in this session's builds.
   Nothing was changed.
8. **`eng/` does not read `VarveLayer`.** Nothing there changed for the
   rename. It was read in `Directory.Build.props`, `Directory.Build.targets`,
   `Varve.Analyzers`, every project file, the fixtures, `AGENTS.md`,
   `CONTRIBUTING.md` and `docs/testing.md`. All now say `ArchLayer`.
9. **"The full pipeline green except the expected CS0618"** is not reachable in
   this session. The upstream-blocked sites keep the build red from layer 0
   upwards. Separately, once the package runs everywhere, layers 3 to 5 report
   what session 3 is for (table below).

### Configuration, as landed

- `Directory.Packages.props`: `DecisionDriven.Analyzers` `0.1.0-preview.4`,
  `Adr="0063"`. `DecisionDriven.Report` is not registered, because nothing
  references it yet: its CI job is session 3's (ADR 0062), and ADR 0063 says an
  entry nothing references would be a claim with nothing behind it.
- `Directory.Build.props`: the reference with `PrivateAssets="all"
  IncludeAssets="analyzers;build"`; `ArchFamily=Varve`;
  `ArchContractTypeAssemblies=Varve.Rdf;Varve.Iri;Varve.Xsd`;
  `DdLedgerDirectory=docs/decisions`, so the package's own targets add every
  set file as `AdditionalFiles` with `DdLedger="decision-set"`. `IsPackable`
  and `OutputType` are compiler-visible for `VARVE0005`.
- `ArchLayer` in every project, equal to its old `VarveLayer`. `none` is now
  unset. `ArchCompositionRoot=true` on AotSmoke, WasmSmoke and Benchmarks.
  `Varve.Sparql.Evaluation` appends `Varve.Sparql` in its own file.
  `tools/repo-standard` clears `ArchFamily`.
- `VarveLayer` is gone everywhere. The layer is written into the assembly by
  the package's generated `[assembly: ArchLayer(n)]`, and
  `Directory.Build.targets` no longer emits `Varve.Layer` metadata.
- `.editorconfig`: `dd_rule_id_prefixes = VARVE`; the `VARVE0001`/`0002`
  severities removed; `varve_hot_path_allowed_types`.

### The front-matter check: kept

`eng/decision-sets.cs` is **kept**. DDGEN0001 (duplicate key), DDGEN0002 (key
syntax) and DDGEN0005 (a key equal to its set's class) now fire in every
build, and the gate no longer checks those three. It still checks what the
generator does not, or tolerates silently: missing fields; an unknown field (a
misspelt `accepted-by` would read as unaccepted); acceptance fields in pairs,
a `mailto:` identity and an `xsd:dateTime` with a zone; quoted statements (the
reader cuts an unquoted value at ` #`); a file without front matter; and
Varve's own rules (namespace `varve`, file named `<set>.md`, one set per ADR).
It also accepts `origin: "…"` in place of `adr:`, for a set filed without an
ADR, which is every set this session files.

### Banned symbols

- ADR 0064's reflection members, every public overload of each, because
  BannedApiAnalyzers 5.6.0 matches one overload per line (checked by a scratch
  build).
- `N:Microsoft.CSharp.RuntimeBinder` for `dynamic`. **It does not catch the
  `dynamic` keyword**: the compiler emits the binder calls, and the analyzer
  sees only symbols the source names (the same scratch build). The file says
  so. Closing it needs a rule, `DD` or `VARVE`, for the maintainer to place.
- `System.Linq.Enumerable` for packable projects at `ArchLayer` 0 to 4, in
  `eng/BannedSymbols.Model.txt` (ADR 0064). It reports the one known use, in
  `Varve.Sparql.Evaluation`, which is session 3's.
- `eng/banned-symbols.cs` is ADR 0063's citation gate: every entry ends
  "ADR NNNN.", every cited ADR exists, the comment block above cites one of
  them, and no symbol is banned twice. Its first run found two comment blocks
  with no citation, and they are fixed. It is a job in `eng/ci.cs` and a step
  in `ci.yml`.

### `Varve.Analyzers`

`VARVE0001`/`0002` removed, with their tests. Their pages are retired stubs.
`VARVE0003` (hot-path discipline), `VARVE0004` (hot-path signature) and
`VARVE0005` (layer declaration) are implemented, each with a page citing ADR
0064, and 82 tests: a violating and a conforming case per finding, including a
`[HotPath]` member calling a non-hot Varve member (reported), an allow-listed
BCL member (not), a non-allow-listed BCL member (reported), and the end-to-end
fixtures (`DD0001` on an upward reference, `VARVE0005` on a packable project
with no layer). Marking layers 0 to 2 refined the hot-path rules four times,
each with tests and a line on the pages:

1. A member implementing a member of a `[HotPath]` interface is held to the
   rule, as VARVE0004's page promised. The generated attribute cannot be put on
   an interface (#61), so an interface is marked member by member.
2. Only `Scope = ExceptionScope.HotPath` exempts. The term arena's `Pool`
   citation had silenced VARVE0003 for the whole type.
3. A class's constructor and initializers are not per quad. A class made per
   quad is reported at the caller's `new`.
4. Test assemblies are not checked. Two test doubles came under the rule
   through (1).

It also found one bug in itself: a user-defined conversion was checked against
an empty allow-list.

### Decisions filed, unaccepted, awaiting the maintainer

Each is `CS0618` at every citation until accepted (ADR 0066). Each set's body
says what the question is and what the alternative would be.

| Decision | Question | Cited at |
|---|---|---|
| `BriefHardConstraints.AllocationPerQuadIsADefect` | The brief's constraint 5, transcribed; the decision every `[HotPath]` answers to | 127 marks: Iri, Rdf, Turtle, Sparql.Results; and Store and Sparql.Evaluation, whose existing marks now cite it |
| `SpanBoundaryCounts.SpanWriterCountsAreInt` | A span writer's `out int written` is the BCL convention, not a wrapper | `IriRef.TryResolve`, `.ResolveLength` |
| `XsdValueSurfaces.XsdComponentsAreSpecIntegers` | Year, month, day, hour, minute, timezone offset, a duration's months and a decimal's scale stay the integers XSD defines, or become about ten wrapper types | 45 members of the XSD types |
| `XsdValueSurfaces.XsdOrderingsReturnInt` | `Compare` and `CompareCodePoints` return `int` | 10 members |
| `XsdValueSurfaces.XsdDecimalConvertsToIeeePrimitives` | `ToDouble`/`ToSingle` return the primitive, not `XsdDouble`/`XsdFloat` | `XsdDecimal` |
| `RdfModelSurfaces.QuadCursorIsForwardOnlyAndDisposable` | `IQuadCursor`'s shape, which no ADR names | `IQuadCursor` (`[Contract]`) |
| `RdfModelSurfaces.InlineValueIsAUnionOfTypedPrimitives` | ADR 0065 deferred `InlineValue.Integer` | `InlineValue.Integer` |
| `RdfModelSurfaces.CanonicalisationWorkIsAnInteger` | ADR 0065 deferred the canonicalisation counts | `WorkLimit`, `Steps`, `Limit` |
| `RdfModelSurfaces.InMemoryTermCountIsAnInteger` | ADR 0067 kept `TermCount`; `int`, a wrapper, or remove it (only tests read it) | `InMemoryDataset.TermCount` |
| `BoolValues.BoolParameterIsTheValue` | A `bool` that is the value (DD0016's own documented path) | `XsdBoolean(bool)`, `InlineValue.FromBoolean` |
| `HotPathScope.AllowListAddsNonAllocatingBclHelpers` | ADR 0064's seven allow-listed types cannot write span code; twelve more non-allocating BCL helpers | `.editorconfig` only. **No citation in code, so no `CS0618`**: its acceptance rests on review |
| `HotPathScope.DirectivesAreNotPerQuad` | A Turtle directive allocates once per binding | `TurtleScanner.Directive`, `TurtleState.BindPrefix`, `.SetBase` |
| `HotPathScope.CallerBufferWriterIsTheSink` | Writing into the caller's `IBufferWriter<byte>` calls it per write | two private members of `ResultsOutput` |
| `HotPathScope.GeneratedLabelClaimsAreRecorded` | Honouring a document's `_:g0` allocates per claimed label | four members of `BlankNodeNaming` |
| `ParserCallbacks.ErrorHandlerIsOptInRecovery` | `n-triples.md`'s accepted ruling, which the ledger does not enumerate | `ErrorHandler` (`[Contract]`) |
| `SparqlAlgebraSurfaces.GrammarKeywordsAreBools` | `SILENT`, `DISTINCT`, `DESC`, `NOT EXISTS` as bools, or five enums | eleven algebra records |
| `SparqlAlgebraSurfaces.QueryLiteralsKeepTheirTypes` | The `GROUP_CONCAT` separator, a prefix label, `OFFSET`, `LIMIT` | `AggregateExpression`, `PrefixDeclaration`, `Slice` |
| `SparqlAlgebraSurfaces.AlgebraListCountsAndIndexesAsInt` | The list's count and indexer | `AlgebraList<T>` |

Accepted decisions this session cites, and their sites:

- **ADR 0022** `OpaqueTermHandle`: `IQuadSource`.
- **ADR 0024** `ViewsNestByArena`: `TermArena` (`Pool`) and its growth
  (`HotPath`). `MaterialiseIsTheOnlyCrossing`: `RdfTermView.Materialise`.
  `TermViewIsARefStruct`: `QuadHandler`.
- **ADR 0030** `PrefixesReportedAsDeclared`: `PrefixHandler`, `BaseHandler`.
  `StatementQuadsBuffered`: the pending buffer's growth.
- **ADR 0048** `AlgebraNodesAreSealedRecords`: seven node bases, and
  `BlankNodePattern` (`Compatibility`). `RewritingByTypeSwitch`:
  `AlgebraRewriter`.
- **ADR 0051** `XsdScope` and **ADR 0064** `DomainModelNamespaces`: the four
  `[DomainModel]` declarations.

### Findings in the two buckets

**Design changes** (the code had just looked like this):

- **Static tables, DD0004.** `IriChars.Ascii`, a writable `byte[]`, is now a
  constant `ReadOnlySpan<byte>`. `LanguageTag`'s grandfathered tags are one
  constant UTF-8 table. `DurationLexical.References` is an `ImmutableArray`.
  The ten tables in `tools/repo-standard` are `ImmutableArray`, `FrozenSet` and
  `FrozenDictionary`, and its display options are made per call.
- **`InMemoryDataset`** is a value. Its snapshot walks and searches arrays it
  owns (ADR 0067).
- **ADR 0065's wrapper.** `QuadCount` is used for `CardinalityEstimate.Count`
  and its factories, as the ADR lists, and for `InMemoryDataset.Count` and
  `QuadDelta.Count`, the two other quad counts on the surface.
- **`Query`, DD0017.** It was the only algebra base left open, so it is closed
  with a `private protected` constructor.
- **Hot-path marks.** The Turtle `Parse` entry points, which run once per
  document, are unmarked. The marks move to the per-quad cores.
  `NQuadsWriter.Write(IBufferWriter<byte>)` is unmarked too: VARVE0004's steer
  is a public API change.
- **Hot-path code.** `TurtleState`'s prefix table is arrays.
  `QuadDelta.Compare` compares inline. The pools' growth is isolated in private
  methods that cite the decision that made them pools.

**Decisions filed**: the table above.

**Left red, upstream** (the next section).

### Analyzer issues filed upstream

In [hafeok/decision-driven-analyzers](https://github.com/Hafeok/decision-driven-analyzers).
Reproductions were verified against `0.1.0-preview.4` in a scratch consumer.

| Issue | Finding | Varve sites left red |
|---|---|---|
| [#59](https://github.com/Hafeok/decision-driven-analyzers/issues/59) | DD0013 reports overrides of `object` members and framework-interface implementations (`Equals(object)`, `GetHashCode`, `ToString`, `CompareTo`). DD0014 requires two of them | Xsd 60, Rdf 17, Sparql 3 |
| [#60](https://github.com/Hafeok/decision-driven-analyzers/issues/60) | DD0016 could exempt a wrapper's own `bool`, as DD0013 does. **Not blocking**: DD0016's page gives `[DesignDecision]` for this case, and Varve took it | none |
| [#61](https://github.com/Hafeok/decision-driven-analyzers/issues/61) | `[HotPath]` cannot be applied to an interface. Varve marks the interface's members | none |
| [#62](https://github.com/Hafeok/decision-driven-analyzers/issues/62) | DD0013 reports members of private nested types | Rdf 4 (`CQuad`, `Slot`; `Order` is also #59) |
| [#63](https://github.com/Hafeok/decision-driven-analyzers/issues/63) | DD0010 offers `[Contract]` for a struct or enum, which does not compile. Also the design question it leaves (below) | Turtle 2, Sparql 1 |
| [#64](https://github.com/Hafeok/decision-driven-analyzers/issues/64) | DD0016 ignores `[DesignDecision]` on a record type, and a positional record's constructor cannot carry one | Sparql 11 |

Filed earlier in the adoption and still open: #43 (changelog), #46 (the prefix
covers sub-namespaces), #47 (DD0019 does not see mutation through methods), #49
(`dd_banned_names` replaces), #53 (`UTF8Encoding`, layer 3).

**What unblocks the build.** A preview with #59, #62 and #64 fixed turns layers
0 to 2 green but for `CS0618` and #63's three DD0010 sites. Those three need
either the analyzer change or the maintainer's answer to the question below.

**A design question for the maintainer, from #63.** `Varve.Turtle`'s
`ErrorHandler` takes `ParseError` and returns `ErrorAction`, and
`AlgebraNode.Span` is a `SourceSpan`. All three are types of their own assembly
outside any model namespace, and a struct or enum cannot be `[Contract]`.
Declaring `Varve.Turtle` model contradicts ADR 0064's list. Two things would
resolve it: the analyzer treating an enum as data, or a small model namespace
for the parsers' positions and error values. The second is a public API move,
and would amend 0064's list.

### Rules whose Varve fallout argues the rule or its configuration is wrong

- **VARVE0003's allow-list, as ADR 0064 wrote it, is too narrow to write span
  code**, and the case is concrete. `span[a..b]` converts through `Index`.
  `IndexOf` and `SequenceEqual` are on `MemoryExtensions`. UTF-8 is validated
  by `Utf8.IsValid` and decoded by `Rune`. A guard clause is
  `ArgumentOutOfRangeException.ThrowIfNegative`. Every Varve parser core called
  at least one of those. The fix is configuration, and it is filed
  (`HotPathScope.AllowListAddsNonAllocatingBclHelpers`) rather than applied
  silently. The rest of VARVE0003 held up: every other finding was a real
  unmarked callee or a real per-document path.
- **DD0013 on hand-written equality** (#59) is the rule contradicting DD0014.
- **DD0016 and positional records** (#64): the rule's own documented exception
  path is unreachable for records.
- **DD0010 on structs and enums** (#63): the second branch of its message does
  not compile.
- **BannedApiAnalyzers cannot ban the `dynamic` keyword**, so ADR 0064's
  "`dynamic`, by banning `Microsoft.CSharp.RuntimeBinder`" enforces only
  explicit use of the binder.
- **`dd_banned_names`** (#49): leaving it unset is the only way to keep the
  package's longer list.

### API diff, layers 0 to 2

- `Varve.Iri`, `Varve.Xsd`, `Varve.Turtle`, `Varve.Sparql.Results`: **none**.
  Every attribute is `internal`, and the static tables are private.
- `Varve.Rdf`:
  - **added** `QuadCount` (ADR 0065) and `InMemoryDatasetBuilder` with its
    members (ADR 0067);
  - **removed** `InMemoryDataset()`, `.Add` twice, `.Internalise` and `.Remove`
    (ADR 0067; `Remove` moved to the builder, which the ADR's list omits);
  - **changed type** `InMemoryDataset.Count`, `QuadDelta.Count` and
    `CardinalityEstimate.Count` to `QuadCount`, and `Exact`/`Estimated` to
    take one.
- `Varve.Sparql`: **removed** the protected constructor
  `Query(Prologue, DatasetSpec?)`, which is the DD0017 design change. The
  public `Deconstruct` is kept.

That is the brief's list plus two items it did not name: `QuadCount` on the two
other quad counts, and `Query`'s constructor. Both are stated above.

### Allocation, before and after

The allocation benchmark of milestones 3a and 3b (`ParseBenchmarks`,
`TurtleBenchmarks`, 100,000 quads each). It ran on one machine in one session,
before on `main` at `4b23015` and after on this branch. The machine: Intel
Xeon, 4 cores, a cloud container, .NET 10.0.401 SDK. Job: `--warmupCount 3
--iterationCount 8 --invocationCount 1 --unrollFactor 1 --inProcess`. The
in-process toolchain is on both sides, because BenchmarkDotNet's own rebuild
would not carry the local override this branch needs. Allocated bytes are
exact; the times are noisy on a shared container and are given for scale.

**N-Quads (3a)**

| | Allocated before | Allocated after | Mean before | Mean after |
|---|---:|---:|---:|---:|
| Varve — views | **856 B** | **856 B** | 122.1 ms | 117.9 ms |
| Varve — owned terms | 24,485,168 B | 24,485,168 B | 123.5 ms | 127.4 ms |
| Varve — into `InMemoryDataset` | 90,198,744 B | 103,159,200 B | 207.5 ms | 221.9 ms |
| dotNetRDF — `TripleStore` | 660,672,632 B | 660,672,528 B | 2,187.6 ms | 2,014.6 ms |

**Turtle (3b)**

| | Allocated before | Allocated after | Mean before | Mean after |
|---|---:|---:|---:|---:|
| Varve — views | **12.33 KB** | **12.33 KB** | 45.0 ms | 39.9 ms |
| Varve — owned terms | 24,777.95 KB | 24,777.95 KB | 31.9 ms | 32.0 ms |
| Varve — into `InMemoryDataset` | 56,108.9 KB | 64,546.56 KB | 70.8 ms | 80.5 ms |
| Varve — read and write back | 15,372.87 KB | 15,372.87 KB | 52.9 ms | 45.5 ms |
| dotNetRDF — `Graph` | 308,432.56 KB | 308,432.52 KB | 767.7 ms | 769.3 ms |

**The streaming paths allocate exactly what they did**, to the byte. The
marks, the rewritten tables and the new decisions changed no allocation on the
per-quad path, which is what the zero-allocation tests also say (they pass
unchanged). **"Into `InMemoryDataset`" allocates 13.0 MB and 8.4 MB more.** That
is ADR 0067's snapshot: the benchmark fills a builder and calls `ToDataset()`
inside the measured body. The call copies the interning table and holds the
quads twice, once in insertion order and once sorted for `Contains`. ADR 0067
put the one-off copy "outside every measured loop"; this benchmark measures
filling a dataset, so it is inside. The per-quad cost of querying the snapshot
fell to index loops over arrays.

### Hot paths marked, layers 0 to 2

- **Parser cores.** `Varve.Turtle`'s `LineParser` (the line scanner),
  `TurtleScanner` (the statement scanner), `EscapeDecoder`, `Escapes`, the two
  character tables and `TurtleState`.
- **Writers.** `SpanWriter`, and `NQuadsWriter.TryWrite` and its term writers.
- **The term arena.** `TermArena`, `TermSpan`, `RdfTermView`, `QuadView`.
- **The quad cursors.** `IQuadCursor`'s members, which hold every
  implementation to the rule: `InMemoryDataset`'s, the overlay's, and the
  store's in layer 4. Also `IQuadSource.Contains`.
- **The values they touch.** `Quad`, `TermHandle`'s comparison members,
  `GraphPattern.Matches`, the delta's spans, `LanguageTag`, `RdfVocabulary`.
- **The IRI scanner and resolver.** Called per term.
- **The results writers' output.** Their marks predate this session.

### Gates

Verified on the final commit, with `-p:WarningsNotAsErrors=CS0618` plus a
local, uncommitted downgrade of DD0010, DD0013 and DD0016, the three ids the
upstream issues block:

| Gate | Result |
|---|---|
| `dependency-register` | ok, 15 packages, each citing its ADR |
| `licence-headers` | ok, 394 files |
| `decision-sets` | ok, 72 sets, 484 decisions, 466 accepted (18 filed by this session) |
| `banned-symbols` | ok, 3 lists, 75 entries |
| `issue-refs` | ok, every commit references #43 |
| build, layers 0 to 2 | **red only on the upstream-blocked sites and `CS0618`** |
| build, layers 3 to 5 | red on session 3's findings (below) |
| unit tests, all layers (everything downgraded to warnings, locally) | Analyzers 83, Iri 109, Rdf 116, Turtle 388, Xsd 213, Store 54, Sparql 36, Sparql.Results 1,058, Sparql.Evaluation 26, Sparql.Store 13: all pass. The zero-allocation tests are unchanged |
| conformance | 6,018 pass. **Ratchet: 2,803 in the baseline, 0 regressed, 0 exempt, unchanged** |
| AOT smoke app | publishes under Native AOT and runs: evaluation, hashes, an update committed at position 1, canonical form |
| repo-standard | builds; 88 pass, 1 skipped as before |
| WASM smoke app | not built here: the `wasm-tools` workload is not installed in this container |

### Layers 3 to 5, for session 3

What the rules report there now that they run, with every DD and VARVE id
downgraded to a warning locally:

| Package | Findings |
|---|---|
| `Varve.Sparql.Evaluation` (3) | VARVE0003 124, DD0017 22, RS0030 10 (the LINQ ban), DD0009 6, DD0004 2, DD0012 1 |
| `Varve.Store` (4) | VARVE0003 91, DD0009 5, DD0004 2 |
| `Varve.Sparql.Store` (5) | DD0017 6, DD0009 1 |

The store's VARVE0003 count fell from 133 to 91 as layer 1's types were marked.
ADR 0065's `Varve.Store.Log` move and wrappers are session 3's.
