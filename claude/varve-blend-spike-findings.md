# Blend spike: can a layered library's allowed decisions be generated?

Findings of the spike in [#128](https://github.com/Hafeok/Varve/issues/128), on branch
`claude/determined-newton-sfkpio` (never merged). The data, the code and every
attempt are in [`experiments/blend-spike/`](../experiments/blend-spike/); every
number below comes from a file in its `out/` directory.

## Verdict

**By the test as stated, the result is "keep blending", narrowly, with conditions
that the evidence makes as prominent as the verdict.**

Two accepted rulings come out of a blend rule and carry a constraint that is in
neither input. All three criteria were computed by a query
(`check/criteria.rq`), not judged by hand:

| Ruling | Rule | Inputs | Emergent constraint |
|---|---|---|---|
| 0140 `NoWallClockFreshness`, its NOW() half | R2 determinism closure | 0140 `EveryDerivedResultStatesItsPosition`, 0056 `NowReadOncePerExecution` | NOW() is forbidden in a standing query's result |
| 0124 `BatchBoundaryIsInNoContract` | R5 cost-only grouping | 0124 `StepsPerRequestWritePerBatch`, 0011 `OneSequencerPerDataset` | the batch is in no contract |

The same rules also *generate* rulings that no ADR states, which an enumerated
list would not contain:

- RAND() and UUID() are forbidden in standing queries. 0140 names only NOW(),
  yet its own reason, a result exact at its position, excludes them too.
- An erasure made over HTTP is one Erasure commit.
- Bulk-load spill runs are in no contract, which agrees with 0108's "the
  result is the same at any worker count".

The conditions:

1. **The control is not derived.** ADR 0005's `StoreReferencesNoSparql` is the
   accepted constraint that layering *permits*: Varve.Store (layer 4) may
   reference Varve.Sparql (2) and Varve.Sparql.Evaluation (3) under
   `LayersStrictlyDownward`. No rule derives it. R1 gives the opposite verdict.
   The only other candidates break criterion 3 or the level invariant (row 2
   below). **The one constraint that most needs to be generated, because layering
   would never produce it, is enumeration.**
2. **Both emergent derivations depend on rules and mapping spaces this session
   proposed.** None of them is accepted, so by the plan's own acceptance
   semantics nothing they derive is in force: `out/in-force.tsv` lists 97
   blends in force (all R1) and 23 not in force (every R2 to R6 blend). Until
   a human accepts R2, R5 and the spike's mapping spaces, the "generated"
   rulings are proposals.
3. **I wrote the rules and the space encodings with the ADRs in view.**
   Criterion 1 limits that bias but does not remove it:
   - R2 fires on four input pairs, producing 11 constraints, and reproduces
     three accepted rulings across three ADRs written over a month (0011,
     0056, 0140).
   - R5 fires on only two pairs.
   - Each encoding sits beside the statement it encodes, in
     `ontology/spaces.trig`, so that it can be checked.
4. **The criteria do not separate emergence from instantiation.** All 97 R1
   layer verdicts also pass all three criteria (`out/criteria.tsv`). Under a
   SPARQL CONSTRUCT, every output is an instance of the rule's template. What
   the spike demonstrates is that *general rules over accepted spaces derive
   accepted rulings that no input states*. Whether to call that blending is a
   question of naming. The only output that resembles Fauconnier and Turner's
   *elaboration* is R3's: "NOW() is replaced by the timestamp of the commit at
   the evaluated position". That is 0140 N4's window sentence, which is ADR
   text and not a ruling.

Kept in both outcomes, and each earned it:

- **The code-fact projector.** It reads files only and reproduces the
  analyzer's layer verdict on 137 of 138 observed references.
- **The graph-side check.** It found two things the analyzers do not:
  - a live contradiction between two accepted rulings (0106 against 0118);
  - two `InternalsVisibleTo` grants that violate 0003 and that DD0002 cannot
    see (below).

## The ten ADRs

The guess was recorded in the plan before any attempt. "Criterion failed" is
the first of the three that the produced constraint fails, or why there is no
produced constraint. The criteria are:

1. The rule is generic: it has another firing from a different input.
2. The constraint relates a term of one input to a term of another input, and
   no input already relates them.
3. The rule's text names neither term.

Every attempt, with its reason, is in `attempts/attempts.trig`.

| # | Ruling attempted | Guess | Result | Rule, inputs | Emergent constraints | Criterion failed |
|---|---|---|---|---|---|---|
| 1 | 0003 `LayersStrictlyDownward` | derived, no emergence | **enumerated** | none: the ruling *is* R1 | — | 1: no rule produces it; it is accepted directly, as a rule |
| 1b | 0003 `SameLayerReferenceIsViolation` | derived, no emergence | **enumerated** | entailed by `LayersStrictlyDownward` alone | — | 2: the only input already relates the terms ("strictly" excludes equal) |
| 2 | 0005 `StoreReferencesNoSparql` (**control**) | enumerated | **enumerated** | R1 gives `mayReference`, the opposite; 0064 `ContractTypeVocabulary`, read generously; a host-cost rule | — | R1: none; it fires, on the other side. From 0064: criterion 2, and the level shape reports it (0064 rests on 0005). Host cost: criterion 3, since it fires only if it names Varve.Store or a "log-only consumer" |
| 3 | 0060 `CompositionRootIsTheExecutable` | derived, emergent | **enumerated** | R4 over R2's bans gives "the clock and entropy are wired above layer 4, and above layer 3" | — | 3: "layer 6 executables", for six things of which four have no determinism space, would have to be in the rule |
| 4 | 0010 `EffectiveDeltaInvariant` | enumerated | **enumerated** | none | — | 1: set zero has no accepted input to fire on; foundations are level 0 |
| 5 | 0011 `NoAmbientClockOrRandomness` | enumerated | **derived, not emergent** | R2 over 0016 `ProjectionRebuildEquivalence` and the spike's `AmbientReads` | the value-level ban (no ambient reads in the projection state) is emergent; the ruling is not | 2: Varve.Store enters through the mapping space ("Varve.Store computes the projection state"), not through an input. `MonotoneTimestamps` is enumerated (criterion 1) |
| 6 | 0124 `BatchBoundaryIsInNoContract` | derived, emergent | **derived, emergent** | R5 over 0124 `StepsPerRequestWritePerBatch` (a sibling ruling) and 0011 `OneSequencerPerDataset` | Batch `inNoContract` Contract | none. `EachRequestKeepsItsCommit` is enumerated: criterion 3, because adding it would widen R5 to fit |
| 7 | 0028 `SlotBoundAssociatedData` | not expressible | **not expressible** | — | — | its terms and its reason (an equality oracle) are a cryptographic argument, not layered-library structure; it can be held only as an opaque statement |
| 8 | 0136 `NoLeaseNoTimer` | derived, emergent ("the fence is the next slot") | **enumerated** | carried from 0116 `NothingForcesAHeldLock` | — | 2: 0116 already states it (`c:lk1` equals `c:t0136`). `FenceIsTheNextSlot`: criterion 1 (one pair: 0134 with 0135) and 3 |
| 9 | 0140 `NoWallClockFreshness` | derived, strongest candidate | **derived, emergent (NOW() half)** | R2 over 0140 `EveryDerivedResultStatesItsPosition` and 0056 `NowReadOncePerExecution`; then R3 with 0011 `MonotoneTimestamps` | NOW `forbiddenIn` StandingQueryResult; R3: NOW `replacedBy` CommitTimestamp, within StandingQueryResult | the freshness half: criterion 2, because the position space already states it |
| 10 | 0106 `SettingsCommitOverHttp` (management plane) | enumerated | **derived, not emergent** | R6 over 0094 `WriteIsAtMostOneCommit` and 0021 `SettingsAreACommitKind` | — | 2: the subject (a settings change over HTTP) comes from the mapping space, so this is a composition. `DeleteOnlyWhenClosed` is enumerated (criterion 1) |
| — | 0056 `EvaluatorUnderDeterministicBan`, not one of the ten | — | derived, not emergent | R2, as row 5 | — | 2, as row 5 |

Against the guesses:

- **Right:** rows 2, 4, 6, 7 and 9.
- **Wrong in the direction of enumeration:**
  - Row 1 is the rule, not an output.
  - Row 3's target names things no space carries.
  - Row 8 is carried, not emergent.
- **Wrong in the direction of derivation:** rows 5 and 10 are derived, though
  as compositions through a mapping.

Three things in the rules were my own bugs, found and fixed during the run.
None was in Varve:

- **R2's scoping.** A `BIND` in its own UNION branch could not see `?x`, so the
  first run produced only package-level bans.
- **Acceptances taken for versions.** Every rule picked up acceptance nodes as
  inputs, because the ledger export puts `ledger:ofDecision` on acceptances too.
- **`in-force.rq`'s FILTER.** It ran after the join that bound its variable,
  because a SPARQL filter's scope is its whole group.

The fixed rules state what the plan stated.

## The layer rule against the analyzer

R1 is one `CONSTRUCT` (`rules/R1-layer.rq`). Its inputs are two layer
declarations from the code facts, joined on a `ProjectReference`, and its
verdict is `mayReference` iff L > M. Its violation is a SHACL shape
(`shapes/layer.ttl`) whose condition is `FILTER(?l <= ?m)`, which also
covers a same-layer reference. The comparison is in `out/layer-comparison.tsv`.

DD0001's verdict is what the analyzer reported when each project was built:

- All 43 projects in `Varve.slnx` build with zero diagnostics.
- The projects outside the solution were built one by one: the samples, the
  smoke apps, the benchmarks, the layer-rule fixtures, and the spike's
  negative controls.

| R1 | DD0001 | References |
|---|---|---:|
| permitted | no diagnostic | 84 |
| forbidden | reported | 3: the existing violating fixture (layer 1 to 2), the spike's upward control (0 to 1), and the spike's **same-layer** control (1 to 1) |
| no verdict | not checked (test assembly) | 45 |
| no verdict | not checked (no `ArchLayer`) | 5 |
| permitted | not observed | 10: `Varve.WasmSmoke` and `Varve.Store.BrowserTests` need the wasm workload (NETSDK1147), which this container lacks |
| **no verdict** | **reported** | **1, the disagreement**: the upward control's *transitive* `Varve.Iri` (0 to 0) |

**The disagreement is a difference in scope, not a different rule.**

- DD0001 reads every `Varve.*` assembly in the compilation's metadata, and an
  SDK project's references are transitive.
- R1 reads `ProjectReference` items.
- The upward control references Varve.Rdf, which brings Varve.Iri along at
  the control's own layer. DD0001 reports both. R1 sees one.
- The closure can be computed from files alone (`code:references+`). I did not
  widen R1 to that path, because the task was to report the disagreement and
  not to fix it.
- In a conforming repository the closure of strictly-downward edges is itself
  strictly downward, so the two agree on every reference in `src/`.

The same-layer control is the case a naive `L >= M` rule gets wrong; R1 and
the shape both report it.

## One as-of question, end to end

`out/as-of.md`. Every first-parent commit of HEAD that touched a projected file
became one Varve commit: 81 git commits became positions 2 to 81, because one
projected to no change and therefore got no position. The working tree came
next, with the decision declarations, then the blends.

- **The reference:** `Varve.Store.Azure` → `Varve.Store`, first present at
  **position 69**, git `c26624a` (2026-10-15, "Varve.Store.Azure, the
  object-store contract over Blob Storage's REST API").
  `DiffAsync(68, 69)` holds it among 77 asserted quads.
- **R1 at that position:** derived over `AsOfAsync(69)`, it gives
  `mayReference`, from `LayersStrictlyDownward`, `SameLayerReferenceIsViolation`
  and `LayerTable`, all three accepted there.
- **In force at 69:** 925 accepted rulings in 140 sets. 33 rulings are filed
  and not accepted, among them the object-store set's 8. Those 8 include
  `ProvidersAreTheirOwnPackages` ("each provider ... is a layer-5 package of
  its own referencing only Varve.Store"), the decision the reference
  implements.
- **When that decision was accepted:** position 81, git `6c3dd7c` (2026-10-17,
  "Accept ADRs 0134-0142").

**For twelve positions, the reference rested on the accepted layering and on a
ruling of its own that was still a proposal.** An enumerated list read at the
head cannot say this; the as-of read does.

## What the projector read from files alone, and what it could not

**Read**, into 21,849 quads at the working tree:

- **The 73 project files:**
  - `ArchLayer`, `ArchCompositionRoot`, `OutputType`, `IsPackable`;
  - `ProjectReference`, with analyzer references told apart and conditional
    ones flagged;
  - `PackageReference` and `InternalsVisibleTo`.
- **`Directory.Build.props`:** `ArchFamily` and the global package references.
- **`Directory.Packages.props`:** versions and the `Adr=` that admits each.
- **The public API baselines:** line counts per project.
- **The 154 decision sets** as ledger nodes: 1,047 decisions, each with a
  version and its acceptance.
- **The 1,033 `[Contract]`, `[HotPath]` and `[DesignDecision]` citations in
  `src/`, by pattern.**

**Could not, or only by guessing:**

- **Which symbol a citation is on.** The pattern takes the next declaration
  after the attribute. For 163 of the 1,033 (16%) it found none within reach,
  and 714 are members whose type the pattern does not know. A compiler would
  know.
- **Transitive references:** computable from files, but not what the project
  file says (see the disagreement above).
- **MSBuild evaluation:** conditions, properties set in imported files, and
  item metadata from targets. No reference in the repository is conditional,
  so nothing was lost here.
- **A referenced package's layer.** DD0001 reads `[ArchLayer]` from assembly
  metadata, which a file reader does not have.
- **The public API's types**, beyond line counts, without parsing the baseline
  format.
- **`.editorconfig` and `.globalconfig`**, which change what the analyzers
  report. Not read.
- **The declarations' history.** They were read only at the working tree: all
  of `src/*.cs` at every commit is the cost of having no compiler.

**What the file reader found that the compiler misses.**

- **The violations.** `src/Varve.Store.Azure` and `src/Varve.Store.S3` grant
  `InternalsVisibleTo` to `Varve.Benchmarks`, which ADR 0003's
  `InternalsVisibleToTestsOnly` forbids. Commit `e7035f8`, 2026-10-16, added
  both grants.
- **Why the build is clean.** DD0002 configures
  `GeneratedCodeAnalysisFlags.None`, and the SDK writes an
  `<InternalsVisibleTo>` item into the generated `AssemblyInfo.cs`. The
  diagnostic's location is in generated code, so the compiler drops it.
- **The reproduction:** `negative-control/Varve.Spike.FriendItem`, with the
  grant as an item, builds clean. `Varve.Spike.FriendSource`, with the same
  grant in source, fails with DD0002 (`out/negative-control-dd0002.txt`).
- **The check:** `shapes/internals.ttl` reports all three grants from the
  project files.

**A finding in the ledger, from the graph.** 0106's `AdminEndpoints` and
`SettingsCommitOverHttp` are still in force: they name `POST /settings`, `/open`
and `/close`. 0118's accepted `SettingsAreAResource` and `StateIsAResource`
removed those paths. 0118 filed new keys instead of moving the rulings, as
`docs/decisions/README.md` says a supersession does. `shapes/contradiction.ttl`
reports the pair. Nothing in the enumerated ledger or in the analyzers can.

## Tooling, and the layout decision

- **The dataset:** an embedded Varve dataset on `FileStorage` in a scratch
  directory, written as commits whose `Cause` is the git SHA.
- **Reads:** `AsOfAsync` and `DiffAsync` answer the as-of question.
- **Rules and checks:** every rule and every check is SPARQL on
  `Varve.Sparql.Evaluation` over a pinned or as-of view.
- **SHACL:** pyshacl 0.40.1 on rdflib 7.6.0, over the head exported as
  N-Quads; #13 has not landed.
- **The layout decision.** The spike's own `Directory.Build.props` does not
  import the repository's. So the host runs with `TreatWarningsAsErrors`,
  nullable, the SDK analyzers at `latest-recommended` and code style at build,
  and without `DecisionDriven.Analyzers` or `Varve.Analyzers`. The maintainer
  approved this on the plan as *layout, not suppression*, because nothing in
  the directory is product and the DD rules exist for shipped code.
  `negative-control/` imports the repository's settings whole.
- **The gates.** No gate was suppressed, and none fires on the directory:
  - Every `.cs` file carries the MPL notice, so the repository-wide licence
    scan passes rather than being skipped.
  - CI runs only on `main`, `land/**` and pull requests.

## What Varve's embedded API lacked, as proposed issues (not filed)

**1. Find the position that asserted a quad.**
> To answer "which commit added this reference", the spike asked an ASK at
> `AsOfAsync(p)` for every position, a linear scan. The default projection
> already holds each quad's asserting and retracting positions (ADR 0138's
> validity intervals). Proposal: a read that returns a quad pattern's
> intervals, for example `Dataset.HistoryAsync(subject, predicate, object,
> graph)` yielding `(asserted, retracted?)` positions. The answer is exact
> without replay, and "when did this become true" becomes a single read. It
> needs an ADR, because it exposes the projection's intervals through a
> contract.

**2. Look a commit up by its cause.**
> The spike wrote each commit with `CommitMetadata.Cause` set to the git SHA,
> then had to keep its own map from position to SHA, because nothing reads a
> commit back by its cause. Proposal: `Dataset.CommitsByCauseAsync(cause)`,
> or a filter on `Subscribe`, so that an external identifier recorded in the
> log can be found from the log.

**3. Record the position a derived commit was computed against.**
> A derived write (here a blend computed at position p and committed at p+1)
> has nowhere in its commit to say "derived from the state at p". The spike
> wrote `blend:atPosition` into the data. ADR 0057 makes the read position of
> an update explicit to the caller, but `CommitMetadata` carries only an
> agent, a cause and a graph scope, so the position is not in the commit.
> Proposal: an optional
> `BasedOn` position in `CommitMetadata`, written to the log. That is a
> format change (ADR 0072), so it is an ADR's question before it is a field.

**4. A diff in terms.** (Minor.)
> `DiffAsync` returns a `QuadDelta` of handles. Rendering it needs a view
> that knows every handle and `TryExternalise` per term. A term-level
> enumeration on the delta, or a helper that writes it as N-Quads against a
> view, would save every caller the same loop.

**5. Disposal is not uniform.** (Minor.)
> `FileStorage` is `IAsyncDisposable` and `MemoryStorage` is not, so code
> that swaps one backend for the other changes its `using` lines.

Not a gap: SHACL is #13. CONSTRUCT into a named graph is SPARQL's limit, and
`Varve.Sparql.Store`'s update path covers it.

## For product-cli, as a proposed issue (not filed)

**Derived decision versions need a stated acceptance semantics.**
> A decision version can be the output of an accepted rule applied to accepted
> inputs, as in Varve's blend spike, where a layer rule's verdict on one
> project reference is such a version. Nobody signs it, and nobody should:
> the rule and the inputs were signed once.
>
> The protocol (section 9, export; LP-3.7, LP-4.24) assumes every version an
> acceptance covers was signed by a human. It says nothing about a version
> that may exist unsigned, and nothing about the condition under which one
> counts as accepted.
>
> Proposal:
> - **(a)** Section 9 says whether a version with
>   `prov:wasDerivedFrom` its rule's and inputs' versions may be exported
>   without a `ledger:Acceptance`.
> - **(b)** If it may, the spec states its acceptance as a query, not a
>   signature: the version is in force at a position exactly when an ASK finds
>   a live, unrevoked acceptance of the rule's version and of every input
>   version at that position, recursively through derived inputs.
> - **(c)** The spec says what an input that is a *fact* rather than a decision
>   contributes (here a project file's declared layer, whose authority is the
>   checked commit that landed it).
> - **(d)** A derivational order (a `level` strictly above every input's) is
>   checked, so that a derivation cannot rest on what rests on it.
>   `ledger:supersedes` is temporal and cannot express this.
>
> The spike's ASK is `check/in-force.rq`. Its level shape is
> `shapes/level.ttl`, which reported a candidate derivation of an earlier
> decision from a later one that cites it.

## For decision-driven-analyzers, as a proposed issue (not filed)

Per AGENTS.md, an analyzer finding is an issue in the analyzer repository, with
the reproduction and without naming the consumer:

**DD0002 does not report an `InternalsVisibleTo` declared as an MSBuild item.**
> The reproduction is a project in a family, with
> `<InternalsVisibleTo Include="Some.Library" />` in the project file, where
> `Some.Library` does not end `.Tests`.
>
> - **Expected:** DD0002.
> - **Actual:** a clean build.
>
> The SDK writes the item as
> `[assembly: InternalsVisibleTo("Some.Library")]` into the generated
> `obj/.../*.AssemblyInfo.cs`. The analyzer calls
> `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` and
> reports at `attribute.ApplicationSyntaxReference`, which is in that
> generated file, so the compiler drops the diagnostic.
>
> The same grant written in a source file is reported. The comment in
> `InternalsVisibleToAnalyzer.cs` ("a grant that arrives from a generated file
> ... still gets reported, just without a caret") describes the intent, but the
> code reports *with* the generated location.
>
> Fix: report at `Location.None` (or the project) when the syntax tree is
> generated, or analyse generated code for this one rule.

## Did the blend vocabulary earn its place?

There are twenty terms (`vocab/blend.ttl`), after the plan's two changes:
`GenericSpace` dropped and `level` added. Measured by what a rule or a check
actually reads:

**Earned.** Fifteen terms are read by something.

- **The decision types: `MentalSpace`, `BlendRule`, `Blend`.** As subclasses
  of `ledger:Decision`, they made inherited acceptance one ASK.
- **`Constraint` and `constrains`.** They are the shape every rule reads.
- **`Mapping`.** It did real work. Typing a space as a mapping kept it out of
  criterion 2, and that is what separated the composition in row 10, and the
  package scope in rows 5 and the 0056 row, from the two emergent results.
- **`query`.** Criterion 3 reads it.
- **`emergent`.** It is computed only, and it discriminated. A shape refuses
  it anywhere but the computed graph; the shape fires 110 times when pointed
  at that graph and never elsewhere.
- **`level`.** Its shape caught the one derivation that would have made 0005
  rest on 0064, which rests on 0005.
- **`atPosition`.** It is written, though only the as-of report reads a
  derivation at a position.
- **The attempt bookkeeping: `Attempt`, `result`, `forDecision`,
  `criterionFailed`, `guess`.** These are bookkeeping for a spike, not domain.

**Did not.** No query reads `Slot`, `slot`, `input`, `source` or `target`.
They describe the rules for a human; the rule's CONSTRUCT does the work.
Dropping them leaves fifteen terms, and the paper's input-space and slot
machinery reduces to "a rule names two input patterns and joins them". The
join is the generic space, as the plan's review said.

**So: the vocabulary is better described as patterns with composition rules,
plus two things blending supplied that the plain-SHACL outcome would have
lacked.** The first is the derivational `level`. The second is the computed
`emergent` test, with its mapping-versus-input distinction, which is what
classified each derivation. Whether "keep blending" survives depends on
condition 1 above: the control is not generated, and the rules that did
generate accepted rulings are not yet anyone's decision but this session's.
