# Changelog

Projected from `releases/` by `dotnet run eng/changelog.cs`. Do not edit by
hand: a version's notes are written in its descriptor, `releases/<version>.yaml`,
and reviewed in the pull request that proposes it
([ADR 0102](docs/adr/0102-a-release-is-a-descriptor.md), `docs/releases.md`).
What is not yet released is `dotnet run eng/changelog.cs -- --unreleased`.

The format is [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
([ADR 0035](docs/adr/0035-semantic-versioning.md)).

## [Unreleased]

## [0.1.0-preview.17] - 2026-10-27

**Inference, part 1 — RDFS, RDF and D by query rewriting, regimes over HTTP, the TBox binding**

### Inference, part 1 — tier 1: RDFS, RDF and D by query rewriting

A query can be answered under an entailment regime. This release completes
the first of Inference's three parts (#170, under #95): the rewriting, the
regimes over HTTP and the TBox binding. Nothing is materialised, and the log
stays asserted-only. The closure (OWL 2 RL, custom rules) is part 2; the
commit-time validator, `owl:sameAs`, OWL 2 QL and Brick are part 3. The
storage format and the change feed's format are unchanged. The five ADRs
are filed unaccepted and accepted on the pull request (ADR 0066).

#### Added

- **`Varve.Sparql.Entailment`** (layer 3, ADR 0191): `EntailmentRewriter`
  rewrites a query's basic graph patterns so that simple evaluation answers
  the RDF, RDFS or D regime (SPARQL 1.1 Entailment Regimes §2, §4, §5.1),
  under conditions C1 and C2 and the multiplicity rule (ADR 0193).
  - Each graph is read merged with the TBox graphs of the binding that
    covers it, given as `TBoxCover` values. A graph no binding covers is
    read over its own triples, and a named graph the source holds nothing
    of is not merged.
  - The schema is saturated per scoping graph by bound probes to a
    fixpoint, meta-modelling included, never by a scan of the data, and
    compiled into tables. `SchemaCache` keeps it across queries, bounded,
    keyed by the versions of the graphs it merges; the server keeps one
    per dataset. Under `GRAPH ?g` the named graphs are grouped by the
    schema they meet.
  - C2 is answered by existence probes at rewrite time, memoised per
    request. Classes and container membership properties are sought, one
    seek per distinct term, where a pattern needs them.
  - Rewriting does not grow with the data. `?s ?p ?o` and
    `?x a rdfs:Resource` are answer-sized. A triple that says something of
    the RDF or RDFS vocabulary itself makes the schema of every graph it is
    part of read whole, after every commit to it: a TBox binding's
    description (`GET /manage/datasets/{name}/bindings/{binding}`) names
    those graphs under `fullReads`, and `/health/ready` counts them.
  - Each pattern's union drops the branches the rewriting proves add
    nothing; `?x a ex:Student` under RDFS reads its typings once, not six
    times. The schema cache is bounded by size as well as count.
  - D matches literals by value, with Varve.Xsd's datatype map, through
    two extension functions a host registers (`LiteralValues`).
  - Property paths and `SERVICE` are evaluated without entailment (§10).
  - Usable over any `IQuadSource`, without the store; the browser build
    carries it for 42 KB under brotli against `main`.
- **`Varve.Rdf`**: `IDistinctTermSource` and `DistinctTerms`, a source's
  distinct predicates and objects by seek; `IGraphVersionSource` and
  `GraphVersion`. The store's `DatasetView` answers both, at the head,
  inside an extent set's window and as of a position, and
  `GraphScopedQuadSource` forwards them.
- **`Varve-Entailment`** (ADR 0192): a query asks for `simple`, `rdf`,
  `rdfs` or `d`, or a regime IRI in angle brackets.
  - Without it, the dataset's default from its TBox bindings applies, and
    simple entailment when none states one.
  - The response names the regime, `Vary` lists the header on every
    dataset response, and the `ETag` of a query's answer is per position
    and regime: `"<position>.<token>"`.
  - `400 entailment-unsupported` (members `value` and `supported`) answers
    anything else, and any value but `simple` on an update.
    `entailment-refused` is catalogued for the regimes of later parts.
  - The service description names the default
    (`sd:defaultEntailmentRegime`), `sd:feature varve:EntailmentHeader`,
    and the regimes of each covered named graph (`sd:entailmentRegime`).
  - CORS allows and exposes the header (ADR 0132, amended).
  - `IEntailmentBindings` is the protocol's seam; the server maps the
    declarations at the request's management position.
  - `SparqlHttpClient.QueryAsync` takes an optional regime, and
    `varve query --entailment <token>` sends it.
- **The TBox binding** (ADR 0194): `varve:TBoxBinding` reads into
  `TBoxBindingDeclaration`: one or more `varve:boundGraph` TBox graphs, a
  graph set, `varve:profile varve:Rewriting` and at most one
  `varve:defaultEntailment`.
  - The shapes in `shapes/management.ttl` validate it at runtime; the
    residual check refuses two bindings whose graphs overlap, two
    different defaults in a dataset, and the reserved terms
    (`varve:RDFS`, `varve:OWL2RL`, `varve:RulesOnly`, `varve:rulesGraph`,
    `varve:crossGraph`, `varve:sameAs`, `varve:inconsistencyMode`), each
    naming the part that gives it effect.
  - **A TBox graph is read by every caller who reads anything in its
    dataset** (ADR 0190); writing it still needs a write grant.
    `GraphScope.WithDatasetPublic` carries the rule, and
    `GrantTable.ScopesOf` applies it.
  - `ManagementPlan.DatasetPublic` lists the graphs the desired
    declarations make dataset-public, marking those an apply widens and
    narrows. `varve apply --plan` prints the list before the graph diffs,
    and the saved plan carries it.
- **Conformance.** The RDF, RDFS and D cases of `sparql11/entailment` and
  the cases of `rdf-mt` the rewriting decides run under the ratchet. The
  RIF and OWL 2 DL-only cases are blocked as out of scope (#15), listed by
  name, and counted by `eng/status.cs`.
- **The operator guide** `docs/operator/inference.md`.

#### Changed

- **`Vary` on every dataset response names `Varve-Entailment`** beside
  `Accept`, `Varve-As-Of` and `Authorization`, problems included.
- **A query's `ETag` under a regime other than simple** is
  `"<position>.<token>"`. Under simple entailment, the default when no
  binding is declared, it is the position alone, as before. An `If-Match`
  naming such a tag on a write is refused: the tag names no position.
- `varve:TBoxBinding` was recorded with exactly one `varve:boundGraph` and
  no effect (ADR 0166). It now needs a profile and a graph set, and a
  declaration without them is refused at apply.

## [0.1.0-preview.15] - 2026-10-23

**As-of reads that seek — an as-of read inside an extent set costs its matches**

### As-of reads that seek (#133, in 1.0 by #15)

An as-of read at a position inside an extent set's window no longer
narrows the whole set before it begins: it looks its keys up in the set as
a read of the present does, and classifies only the matching rows'
intervals. At REC M, inside the largest merged set (211 MiB), a quad's
as-of lookup takes 4.4 / 15.0 µs at p50 / p99, 0.29× the same lookup at
the head, where opening that view took 2.9 s. This release closes #133.
The log's bytes are unchanged at storage format 1; extents are written in
derived format 5, rebuilt and never migrated (ADR 0072). The two ADRs are
filed unaccepted and accepted on the pull request (ADR 0066).

#### Changed

- **Derived format 5, for extents only** (ADR 0177). An extent's positions
  are fixed-width columns beside its key sections, one distance a key, and
  its rows within are a thirteenth key section with a column of two
  distances a row. Its asserted and retracted sections are format 4's,
  byte for byte, and a read of the present reads nothing else. Every other
  derived blob, the catalog included, is still written as format 4.
  Storage format §7.5.
- **As-of reads seek** (ADR 0177). A lookup is three fence searches, a
  block each and a column read; a scan merges the set's rows in the
  pattern's range a key at a time; nothing is written. An as-of scan costs
  its matches: 0.56 pages a match for a subject's quads at set sizes 28×
  apart.
- **An as-of estimate inside a set may be estimated** (ADR 0049, amended):
  exact while the set holds at most 4,096 rows in the pattern's range,
  otherwise the set's count at its end, marked estimated.
- **Checkpoints, merges and drops write format 5**, from the rows format 4
  had; a merge stays a pure function of its inputs (ADR 0158). Write
  amplification at REC M is 1.86 per byte against 1.78, for extents 3.5%
  smaller; checkpoint time is unchanged, maintenance 14% longer.
- **Every block of a format-5 extent is checked** (ADR 0177). The
  directory carries a 64-bit check for each key block and each column
  block, verified when the block is read: an extent with any byte zeroed,
  flipped or cut serves the same answers or fails the read, never a
  different answer.
- **Reader records state the derived format they read** (ADR 0160,
  amended): `maxDerived`, absent read as 4, the record still version 1.
  `varve storage readers list` shows it.
- **`MaxSetBytes` bounds merges, not reads** (ADR 0178, 0159 amended). Its
  default stays 256 MiB: at REC M a 1 GiB cap made the chain longer. The
  operator guide says when to raise it.

#### Upgrading

- **A dataset written by `v0.1.0-preview.14` or before opens as it is.**
  Reads inside its format-4 sets narrow them, as before, until
  maintenance rewrites each alone as format 5, oldest first, when no merge
  is due (ADR 0178). With `ExtentMerges.Enabled` false nothing is
  rewritten.
- **Older readers decide when the rewrite starts** (ADR 0178). While any
  present reader record reads only format 4 (one of
  `v0.1.0-preview.14` or before has no `maxDerived`), no format-4 set is
  rewritten and merges of format-4 sets stay format 4; new checkpoints are
  format 5. Once the record is forgotten or its reader upgraded, the
  rewrite proceeds.
- **Followers first is recommended; either order is safe.** A follower of
  `v0.1.0-preview.14` or before behind a writer of this release finds the format-5
  extents cache misses: its chain ends at the first such set and it reads
  the log above it, serving every position correctly at the log's cost,
  and the format-4 sets it needs stay while it is announced. A writer of
  `v0.1.0-preview.14` or before (a downgrade) deletes the format-5 sets and writes
  its next checkpoint from the log.

#### Fixed

- **A key with two intervals inside one set** is looked up at its first:
  a lower bound in a run is now the first of equal keys, not whichever a
  binary search met first. Found by the damage property on review.

#### Measured

REC M, generator output version 2, on files and on Azurite
(`tests/Varve.Benchmarks/README.md`, "As-of reads that seek"). Head reads
are unchanged: over one dataset of each format, in alternating rounds,
the same lookups and scans at p50 overlap between this release and
`main`'s store.

## [0.1.0-preview.14] - 2026-10-26

**SHACL, part 2b — the validation projection, shapes for the declarations, the REC gate**

### Milestone 8, SHACL, part 2b — the validation projection, shapes for the declarations, the REC gate

A shapes binding's report is now kept for every position, and served as a
graph. Varve's own declarations are validated by SHACL shapes, and
RealEstateCore's shapes validate generated portfolios three ways that
agree. This release closes milestone 8 (#13), after part 1 (#120) and
part 2a (#138), and follows Management plane part 2 (`v0.1.0-preview.13`):
the shapes also state the terms of its event-declaration move. Storage format 1 holds. The three ADRs are filed
unaccepted and accepted on the pull request (ADR 0066).

#### Added

- **The validation projection** (`Varve.Shacl.Store`, ADR 0174):
  `ValidationProjection`, one per shapes binding.
  - **Its rows.** They are keyed by shape and focus node, with validity
    intervals, and each commit maintains them from the pairs it affects.
  - **The guarantee.** Its report at a position equals
    `ShaclValidator.Validate` over `AsOfAsync` there, byte for byte.
  - **Where it lives.** It is stored under
    `derived/validation/{enc(binding)}/` in the local cache only, never
    on an object store. Every instance rebuilds it from the head on a cold
    start. A position below that floor is answered by a full validation as
    of it.
- **The report graph**, `https://w3id.org/varve/ns#validation/{enc(binding
  IRI)}`, served at a pinned position through the staging view, so it
  takes no dictionary terms.
  - **Reading it** needs read on every graph the binding validates (ADR
    0107, amended). A grant naming the graph itself has no effect.
  - **Writing it** is `graph-not-writable`.
- **`503 validation-behind`**, with `binding`, `position` and
  `projectedPosition`, when a read of a report graph is above its
  projection after one bounded wait. Readiness reports each binding's lag,
  which never makes the dataset unready.
- **`Varve.Protocol`**: `IValidationReports`, `IValidationReportGraph`,
  `ProtocolOptions.ValidationReports`, `ReportGraphGuard` and
  `ValidationBehindException`, the seam the server composes the
  projections through.
- **`Varve.Shacl`** (additive, ADR 0155): `ShaclValidator.ValidateByPair`
  and `PairedOutcome`, `FocusPair`, `IncrementalComparison.Pairs`, and
  `ValidationReport.Compose`, `WriteTo` and `ReadFrom`.
  `ValidationOutcome.Reported` and `Failed` are added too.
- **`Varve.Rdf`**: `GraphScope.Prefixes`.
- **`Varve.Management`**: `ManagementShapes.Turtle`, the management
  vocabulary's SHACL Core shapes.
- **The REC gate** (ADR 0176). It composes REC 4.1.0's three SHACL files
  with `owl:Class rdfs:subClassOf rdfs:Class` and `sh:declare`s for the
  prefixes Brick's SPARQL constraints use. The ADR records the exact
  triples. Over generated portfolios, standalone validation, commit-time
  `reject` and the projection agree:
  - REC S at every 250th position and the last on pull requests;
  - REC S at all 1,001 positions nightly;
  - REC M at 100 positions nightly.

  `testdata/rec/gate/features.md` reports the SHACL features REC uses.
- **`docs/operator/shapes.md`**: binding shapes, the modes, the report
  graph and the REC composition.

#### Changed

- **Shapes are the runtime validator of Varve's declarations** (ADR 0175).
  - `EventDeclarationCheck.Check` validates by ADR 0157's shapes.
  - `DeclarationReader.Read` validates by `shapes/management.ttl`.
  - What SHACL Core cannot state stays a residual check in code.

  Verdicts, problem types and wire shapes are unchanged, and the code
  validators as they stood are the tests' oracle.
  - **The move's terms (ADR 0167).** The shapes also state
    `varve:eventDeclarations` (at most once, `varve:InDataset`,
    `varve:Moving` or `varve:Moved`, on the dataset's node only) and a
    moved event type's one `varve:sourceGraph`.
  - **What moves to the shapes.** A moved type's missing or doubled
    `varve:sourceGraph` is now refused at commit time as
    `declaration-invalid`, with the same message, before the moved nodes
    are translated. A stray `varve:eventDeclarations` in the fleet graph
    is refused too. `Varve.Events` and
  `Varve.Management` now reference `Varve.Shacl`.
- **`Varve.Shacl` indexes the shapes graph when it loads.** REC's
  composition loads in about 1 s instead of 14 s.
- **Incremental validation finds fewer candidate targets** (ADR 0171's
  anchor rule). An `rdf:type` triple now reaches only the shapes that
  target its class or a superclass, through an index of shapes by target
  class. A `sh:class` value node is seeded only when its type is the class
  or a subclass. Under REC's shapes, the gate's time over REC S's 1,001
  commits fell from 180 s to 21 s. The results are unchanged.

#### Performance

At REC M (25,004 commits, generator output version 2), on the session's
machine, the projection lags a commit by 3.0 ms on average (median 2.0,
p95 9.6, max 59.2). A rebuild after a shapes change takes 194 s, and a
cold start 182 s. At REC S the lag is 2.3 ms on average, a rebuild takes
3.7 s, and a cold start 7.1 s.

#### Known issues

- **REC 4.1.0 as vendored** still refuses its own shapes graph, for the
  undeclared prefixes. The gate's composition is the workaround, and an
  upstream note is drafted on the pull request.

## [0.1.0-preview.13] - 2026-10-23

**Management plane, part 2 — event schemas move to the management plane**

### Milestone Management plane, part 2 — event schemas move to the management plane

A dataset's event declarations can move from its catalogue graphs to its
declarations on the management plane, where they are written, reviewed and
applied like its grants and settings. The events every commit renders do
not change by a byte across the move. This release closes milestone
Management plane (#105), whose first part was #131 (`v0.1.0-preview.10`).
Storage format 1 holds. ADR 0167 is filed unaccepted and accepted on the
pull request (ADR 0066), with dated amendments to ADRs 0144, 0145 and 0166.

#### Added

- **The move, per dataset, as an explicit act** (ADR 0167):
  `POST /manage/datasets/{name}/events/move` and `varve manage move-events
  <name>`, by an operator or the dataset's admin, sent to any instance.
  - The system writer freezes the catalogue graphs with
    `varve:eventDeclarations varve:Moving` at m₁.
  - The dataset's writer passes a barrier through its sequencer, so the
    catalogue at its head is final. It then copies each catalogue graph into
    the dataset's graph, each type node with `varve:sourceGraph`, committed
    with `varve:Moved` at m₂ under the graph's version.
  - The move runs under the caller's token. The request is forwarded to
    the dataset's writer, and its management writes go to the system writer
    once each (ADR 0168).
  - The move is idempotent and resumes where it stopped. An instance that
    writes both the dataset and the system dataset finishes a stopped move
    by itself.
  - A catalogue that does not translate one-to-one cancels the move, and the
    answer says why.
- **The boundary is exact.** A commit renders its events from the system
  dataset exactly when the management position it records is m₂ or later.
  Every commit before that renders from the dataset's own catalogue
  history, so its events are the same bytes as before the move.
- **`GET /manage/datasets/{name}/events`**: where a dataset's event
  declarations are (`in-dataset`, `moving` or `moved`), and the moved
  event nodes as TriG on request. A move left `Moving` is reported by
  status, readiness and this resource as `{"state":"moving","since":m₁}`,
  as information, never unready. A switch finished with no request names
  the agent who authorised the freeze.
- **`409 event-catalogue-moved`**: a write to a catalogue graph after the
  freeze, at the store and over HTTP. Its `manage` member names the
  declarations' new home. The catalogue graphs' history stays readable as
  of any position.
- **`EventCatalogueHistory.ContinueFrom`**: the history after the move,
  seeded with the revisions at it, so that a type whose schema bytes did
  not change keeps its revision and its `dataschema`.
- **`ManagedEventDeclarations`** (`Varve.Protocol`): a dataset graph's event
  nodes in the catalogue graphs' change form, and the copy the other way.
- **Vocabulary:** `varve:eventDeclarations`, `varve:InDataset`,
  `varve:Moving`, `varve:Moved` and `varve:sourceGraph`.

#### Changed

- **Breaking:** `SchemaRevision.Revision` is a `SchemaRevisionId`, and
  `EventEndpoints.Schema` takes one. A revision minted after the move is
  `…/versions/{n}/revisions/m{p}`, with p the management position.
  Revisions minted before the move keep their bare integers, and every URL
  already served stays immutable (ADR 0145, amended).
- Moved event declarations are checked in their new home by the same
  entry point as before, `EventDeclarationCheck`, over their
  catalogue-graph translation. `event-declaration-invalid` and
  `event-schema-incompatible` answer as before the move.
- `ManagementValidator` allows only the move's transitions of
  `varve:eventDeclarations`: none or `InDataset` to `Moving`, `Moving` to
  `InDataset` (a cancel, its own management commit), and `Moving` to
  `Moved`, which is final.

#### Known issues

- The fleet's server processes run on Azurite only. A server reaches an
  S3 store only by web identity (ADR 0137), which RustFS in CI does not
  provide. The move is above the store.

## [0.1.0-preview.12] - 2026-10-20

**SHACL, part 2a — commit-time gating under a shapes binding, incremental validation**

### Milestone 8, SHACL, part 2a — commit-time gating, incremental validation

A dataset's commits can be gated by SHACL. A shapes binding, declared on
the management plane, names a shapes graph of the same dataset, the graphs
it validates and a mode, and every server enforces it at the management
position a write is authorised under. Only the focus nodes a commit
affects are validated. This release closes #138, the first half of SHACL's
part 2, under milestone 8 (#13); part 2b adds the validation projection,
shapes for Varve's own declarations and the REC gate, and closes the
milestone. Storage format 1 holds. The five ADRs are filed unaccepted and
accepted on the pull request (ADR 0066).

#### Added

- **`Varve.Shacl.Store`** (layer 5, ADR 0169): `ShapesGate`, a dataset
  validator over `ShapesBinding`s.
  - `reject` refuses a commit when its affected focus nodes have a
    violation not present at the pinned position, so the set of
    violations never grows, and a conforming dataset stays conforming.
  - `report` accepts and attaches the affected report's digest; `off`
    runs nothing.
  - A commit that changes the shapes is never refused for existing data.
  - A `reject` binding whose shapes are classed full is refused unless it
    declares `varve:allowFullEvaluation`, and fails closed if its shapes
    become full later.
  - `NOW()`, `RAND()`, `UUID()`, `STRUUID()`, and a `BNODE()` whose value
    can reach a result, are refused in any binding not `off`.
- **The binding vocabulary** (ADR 0170): `varve:ShapesBinding` with
  `varve:boundGraph` (the shapes graph), the grant vocabulary's graph-set
  terms, `varve:validationMode` (`varve:Reject`, `varve:Report`,
  `varve:Off`), `varve:allowFullEvaluation` and
  `varve:subClassOfInShapesGraph`. The data graph is the union of the
  bound graphs. `varve apply` refuses a binding whose shapes do not load,
  or are full under `reject` without the flag.
- **Incremental validation** (`Varve.Shacl`, ADR 0171):
  `IncrementalValidation.Compare` validates the pairs a delta affects, in
  the state before and after it. `ShapesGraph.Classify()` classes every
  shape incremental, scoped or full, with the construct and section that
  decided it. `IShaclFootprint` lets an extension state its footprint;
  SHACL-SPARQL constraints are analysed by their join graph.
- **The attachment** (ADR 0173): an accepted commit under a `report` or
  `reject` binding carries `<<( binding varve:affectedReport
  "sha256:<hex>" )>>`, the SHA-256 of the canonical N-Quads of the report
  restricted to the commit's affected focus nodes. A digest that no longer
  reproduces after an upgrade is how a change in the engine's results
  shows; a release that changes SHACL results says so in its notes.
- **Three problem types**, `422`: `shapes-violated`,
  `binding-not-incremental`, `shapes-invalid`.
- **`GET /manage/datasets/{name}/bindings/{binding}`**: a binding at the
  applied position, with its shapes' classification.
- **`Varve.Derivation`** (layer 2, ADR 0172): dependency graphs and their
  stratification, regular paths compiled to automata and evaluated
  forwards, backwards and from a changed edge, and semi-naive
  propagation, for SHACL now and Inference later.

#### Changed

- `Varve.Store`: `ValidationVerdict.Accept(IReadOnlyList<RdfTerm>)` and
  `Attachments`, so one validator records several attachments
  (additive).
- `Varve.Shacl` stratifies shapes and walks paths through
  `Varve.Derivation`; its results are unchanged.
- Validation under `subClassOfInShapesGraph` remembers the shapes graph's
  subclass steps.

#### Performance

At REC S and M on the file backend, a `reject` binding over REC 4.1.0's
shapes adds about 7.7 ms to a commit (8.2 ms against 0.5–0.6 ms), nearly
the same at both sizes: the cost is the affected focus nodes', about 2 ms
each, not the dataset's.

#### Known issues

- REC 4.1.0 as vendored cannot be bound whole: three of Brick's SPARQL
  constraints name prefixes that no file declares, so the shapes graph is
  refused. Part 2b decides the REC gate's composition.

## [0.1.0-preview.11] - 2026-10-19

**Extent maintenance — background merges and extent deletion**

### Milestone Extent maintenance — background merges and extent deletion

The writer merges extent sets in the background, so a read of the present
merges a bounded number of sets however many checkpoints a dataset takes,
and deletes a merged set only when no reader may still read it. This
release closes milestone Extent maintenance (#110). The log's bytes are
unchanged and derived format 4 holds: a set gains an optional ninth blob,
of a kind format 4 already defines. The five ADRs are filed unaccepted
and accepted on the pull request (ADR 0066).

#### Added

- **Background merges** (ADRs 0158, 0159). The writer's maintenance
  merges adjacent sets into one, writes it, then swaps the catalog once
  under the epoch fence. A merge never blocks a commit or a checkpoint, is
  a pure function of its inputs, and rebases onto the catalog if a
  checkpoint or a drop swapped it meanwhile.
  - The policy is size-tiered: `FanOut` adjacent sets of similar size
    merge, and past a bound the smallest run merges. `MaxSetBytes` caps
    a set, and so the cost of an as-of read inside one. At the defaults
    (4, 256 MiB) a chain holds at most 15 sets below half the cap.
  - `DatasetOptions.ExtentMerges` (`ExtentMergePolicy`), on the server
    `Varve:Store:ExtentMerges`.
  - At REC M after 100 checkpoints, head reads are faster than after 10
    without merges, and an as-of read inside the largest set is within
    1.16× of it. Write amplification was 1.77.
- **Extent deletion by reader announcements** (ADR 0160). Every
  instance on an object store, the writer included, announces the oldest
  and newest catalog versions it reads in a reader record,
  `readers/<id>`. The writer deletes a set only when no record's interval
  overlaps the versions that named it.
  - `IExtentReaders` in `Varve.Store`; `ObjectStorageOptions.Reader` and
    `Announcing`. The catalog version is the catalog's set sequence.
  - Announcing is required. A follower's identity needs write access to
    `readers/` only (ADR 0134 amended); the operator guide gives the Azure
    ABAC condition and the S3 policy. `ReaderAnnouncing.Unannounced` reads
    without a record, and fails a read that finds an extent gone with
    `read-unannounced-extent-gone`.
  - A reader whose record was forgotten fails with `reader-forgotten`
    and attaches again.
  - A record of a format this build does not read holds every version.
  - A tier-1 follower (ADR 0164) announces the catalog version it opened,
    so the writer deletes nothing it reads; with
    `Varve:ObjectStorage:ReaderId` set, its record is that id with
    `-follower`.
  - The reader record is specified as its own format (storage format §8).
- **The shared commit index** (ADR 0161). Each set carries its window's
  commit index as `extents/<set>/commits`, so a cold start reads no log
  below the catalog.
- **Uploads are staged through the cache.** An extent is written to a
  file in the cache and uploaded in one streamed create, and the file is
  kept as its cached copy. A merge whose output would pass the store's
  single-upload limit is refused before it starts.
- **The server** (ADR 0162):
  - `varve storage <address>` prints the catalog's version, the sets per
    permutation, the unnamed sets the store keeps, and the bytes each
    reader record retains.
  - `varve storage readers list` and `forget --reader --oldest --newest`.
  - `varve info`, `query`, `export` and `feed` read a dataset in an object
    store, announced, or with `--unannounced`.
  - `/health/ready` reports `retention`, and never turns unready because
    of it.
  - `Varve:ObjectStorage:ReaderId`.

#### Changed

- `IObjectStore` gains a streamed `CreateIfAbsentAsync(ObjectName,
  ObjectContent, CancellationToken)`, with a default implementation. The
  Azure and S3 adapters implement it, and raise
  `ObjectStoreAccessDeniedException` on a 401 or 403, which is never
  retried.
- `Varve.Protocol`: the problem types `reader-forgotten` and
  `read-unannounced-extent-gone`; the `not-ready` problem gains
  `retention`.
- The operator guide's checkpoint budget is removed.

## [0.1.0-preview.10] - 2026-10-19

**Management plane, part 1 — the fleet system dataset, grants as code, `varve apply`**

### Milestone Management plane, part 1 — the fleet system dataset, grants as code, `varve apply`

Grants, datasets and their settings are declared as TriG in the
application's repository, planned and applied by CI, and enforced alike by
every server sharing a store. This release closes #131, the first part of
milestone Management plane (#105); part 2 moves event schemas from the
catalogue graphs to the management plane (ADR 0167) and closes the
milestone. Storage format 1 holds: the management position rides in the
commit header's attachment list. The five ADRs are filed unaccepted and
accepted on the pull request (ADR 0066).

#### Added

- **`Varve.Management`** (layer 4, ADR 0166): the `varve:` management
  vocabulary; the reader and the validator, by focus and path; graph sets
  as IRIs or templates over the caller; the grant table at a management
  position; the plan as an RDFC-1.0 diff; TriG in and out.
- **The system dataset** (ADR 0163): one per fleet, at `_fleet/` on the
  fleet's store, written by the instance `Varve:Fleet:Writer` names and
  followed by the others. Datasets are opened as writer, follower or
  elsewhere by their declared placement; a placement change moves the
  writer by events, the old writer abdicating with the reason `placement`
  and the new one claiming once it sees the release. A declared-closed
  dataset is closed; an undeclared one is deleted by its writer; settings
  are converged by the writer in a commit of their own. Bootstrap
  operators from host configuration are recorded by the first writer, or
  by `varve serve --record-bootstrap`.
- **Grants at the applied position**: each request is authorised against
  the grant table at the position the instance has applied, named on every
  response as `Varve-Management-Position`. `Varve-Min-Management-Position`
  asks for a later one: the instance advances once, then answers `503
  management-behind`.
- **The management position on the commit** (ADR 0165, specification
  1.8): the position that authorised a commit is attached to its header as
  `"p"^^varve:managementPosition`, and the positions a dataset records
  never decrease.
- **The tier-1 follower** (ADR 0164): `Dataset.OpenFollowerAsync` and
  `AdvanceAsync`. A follower probes the next log slot, and never lists; an
  idle dataset makes no requests. A follower advances on demand: on a read
  once `Varve:Fleet:FollowInterval` has passed, and on
  `Varve-Min-Position`; the system dataset on the interval.
- **Forwarding** (ADR 0168): any instance accepts a write, authenticates
  the caller, and forwards the request once, whole, to its dataset's
  writer at the address in the writer record. A second hop answers `409
  writer-moved`; no reachable writer answers `503 unavailable`, with the
  reason `writer-unreachable` or `no-writer`. Request ids carry the
  instance.
- **`/manage/`**: datasets, their state, settings, checkpoints and status,
  the fleet graph, grants, plans and `/manage/sparql` for operators. Its
  own token audience (`api://varve-manage` by default), each plane
  refusing the other's tokens, and optionally its own listener,
  `Varve:Manage:Urls`.
- **`varve apply`**: `--plan`, `--exit-code` for drift in CI, `--out` and
  `--from-plan` for a reviewed plan, refused if stale, and
  `--from-config` to convert a host's old configuration. Credentials by
  client secret or a federated assertion (`--client-assertion github`).
- **Readiness** reports a `management` object, and each dataset's role
  and lag; a bootstrap mismatch fails it.
- **Deployment**: Azure Container Apps as a writer app and a public app,
  the public one writing only under `readers/`; an Entra registration for
  the management audience, with a deployer federated to GitHub. Operator
  pages for a fleet and for Kubernetes.

#### Changed

- **The dataset state, settings, checkpoints and status routes move to
  `/manage/datasets/{name}/`** (breaking), and the service description
  points there. `IDatasetAdministration` is List, Open and Close.
- **`Varve:Datasets`, `Varve:Auth:Datasets` and
  `Varve:Auth:Server:Admin` are refused at start** (breaking), each
  naming its replacement; `varve apply --from-config` converts them.
- `Varve.Protocol.Client`: management calls under `/manage/`, settings
  written as TriG. `DatasetSetup` keeps the baseline and the migrations;
  a setup file naming storage or settings is refused, naming `varve
  apply`.
- `Varve.Aspire`: `WithOperators`, `WithManagementClient`, and
  `WithDataset` declaring through a container that runs `varve apply`.
- The writer record names the claimant's address, and a release may give
  the reason `placement`.

## [0.1.0-preview.9] - 2026-10-17

**SHACL, part 1 — the standalone validator**

### Milestone 8, SHACL, part 1 — the standalone validator

A SHACL validator over any quad source. This release completes the first
part of milestone 8 (#120, under #13). Commit-time gating, the validation
projection, incremental validation and the REC gate are part 2. The storage
format and the change feed's format are unchanged. The ten ADRs are filed
unaccepted and accepted on the pull request (ADR 0066).

#### Added

- **`Varve.Shacl`** (layer 3, ADR 0148): SHACL 1.2 Core, as of the Working
  Draft of 2026-10-14, over any `IQuadSource`, with the shapes graph read
  from any `IQuadSource` (ADR 0155).
  - Every target and every core component, SHACL paths, and SHACL
    instances without entailment.
  - Where SHACL 2017 and the 1.2 draft conflict, 1.2 is followed, and the
    cases are exempted by name (ADR 0149). The W3C suites of both run under
    the ratchet: every Core case passes.
  - `ShapesGraph.Load` reads a shapes graph once and refuses an ill-formed
    one with a `ShaclProblem` naming the shape and the rule.
    `ShaclValidator.Validate` answers a `ValidationReport` or the problems
    that stopped it. A failure is never a validation result (ADR 0151).
  - Recursive shapes are stratified, each stratum the greatest fixpoint. A
    cycle through `sh:not`, `sh:xone`, a qualified maximum or a target is
    refused statically, with the cycle named (ADR 0150).
  - A shape constant is looked up in the data, never added to it, and
    validating a store leaves its dictionary as it was. `owl:imports` is
    reported (`varve:importNotFollowed`) and never fetched (ADR 0151).
  - The report is a value: results in a stated order, kept with their
    duplicates, `ToDataset()` and `ToCanonicalNQuads()` by RDFC-1.0 over the
    report's own nodes. A data blank node keeps the label its source gives
    it, so the report joins back to the data (ADR 0154).
  - `ShaclOptions.RegexTimeout` (one second) bounds `sh:pattern` on one
    value; past it, the validation fails with a problem.
- **`Varve.Shacl.Sparql`** (layer 4, ADR 0148): SHACL-SPARQL, SPARQL-based
  constraints and constraint components, evaluated by
  `Varve.Sparql.Evaluation` with SHACL 1.2's pre-binding by Values
  Insertion. It plugs into `ShaclOptions.Extensions` as
  `SparqlConstraints.Extension()`.
- **XPath regular expressions in `Varve.Xsd`** (ADR 0153): `XPathRegex`
  translates a pattern and its flags (F&O 3.1 §5.6) to .NET's `Regex`, with
  character classes computed over every plane. It refuses what XPath
  forbids with the error code and position. `sh:pattern` and the
  evaluator's `REGEX` and `REPLACE` share it.
- **`EventDeclarationShapes`** in `Varve.Events` (ADR 0157): the event
  declaration vocabulary's SHACL shapes, `varve:EventTypeShape` and
  `varve:EventFieldShape`, as Turtle. A property holds them to
  `EventDeclarations` on everything SHACL Core can state.

#### Changed

- **`REGEX` and `REPLACE` follow XPath, and are stricter** (breaking, ADR
  0153). The pattern used to reach .NET unchanged. A pattern that used a
  .NET-only construct is now an expression error: lookahead or lookbehind,
  a named group, an inline option such as `(?i)`, any `(?` but `(?:`, the
  escapes `\b`, `\B`, `\A`, `\z`, `\Z`, `\G`, `\x…`, `\u…`, `\0`, `\a`,
  `\e`, `\k` and octal ones, and an unescaped `{`, `}`, `[` or `]` used as a
  character. Also:
  - `.` no longer matches `\r`, and `$` no longer matches before a final
    newline.
  - `\w`, `\s`, `\i` and `\c` mean XPath's classes.
  - Flag `i` no longer folds a category escape such as `\p{Lu}`.
  - A character outside the Basic Multilingual Plane is one character to
    `.` and to every class.

  The W3C SPARQL query and update suites are unchanged by it, case for
  case.
- `XsdDateTime.CompareXsd` and `XsdBoolean.CompareTo` are held to the
  hot-path rule; their behaviour is unchanged.

## [0.1.0-preview.8] - 2026-10-16

**Object storage — the log, the projection and the deployment in an object store**

### Milestone Object storage — the log, the projection and the deployment in an object store

A dataset can live in Azure Blob Storage or an S3-compatible store, with
no disk of its own but a cache. This release closes milestone Object
storage (#92). The log's bytes are unchanged, so storage format 1 holds
and a dataset moves between a directory and a container by copying its
objects. The derived files go to format 4: checkpoints become extent
sets, and a dataset written in format 3 migrates as it runs. The nine
ADRs are filed unaccepted and accepted on the pull request (ADR 0066).

#### Added

- **`IObjectStore`** in `Varve.Store` (ADR 0134). Five operations: read a
  range, create if absent, replace if unchanged, list a prefix, and
  delete. A contract suite every adapter passes, including a race
  property: of N clients racing one create or one replace, exactly one
  wins.
- **`Varve.Store.ObjectStorage`** (ADRs 0135, 0136): the backend.
  - The log is write-once chunk objects, one create per commit batch. A
    seal consolidates its segment into one object, and maintenance
    deletes the chunks off the commit path, so chunks are bounded by the
    open segment.
  - One writer is enforced by an epoch in a `writer` object, fenced by
    the next log slot, with no lease and no timer. Opening for write
    claims the next epoch, and the writer record lists every claim with
    its trigger, `restart` or `operator`. A read-only open never claims.
  - A superseded writer stays open read-only and never claims again. A
    graceful shutdown abdicates.
  - `Durability.Replicated`.
  - A page cache on local disk, bounded by `CacheBytes`.
- **`Varve.Store.Azure`**: Blob Storage over its REST API, with
  `x-ms-version` pinned. The credentials are the workload's identity
  (ADR 0137): the Container Apps managed-identity endpoint, workload
  identity through `AZURE_FEDERATED_TOKEN_FILE`, or `az` on a
  workstation. Tokens are never logged, put in a problem body, or put in
  telemetry.
- **`Varve.Store.S3`**: SigV4, with web identity as its credential. An
  S3-compatible server (RustFS in CI) passed the full contract suite.
- **A store that does not enforce conditional writes is refused** (ADR
  0134). A writer's open first probes under `probe/{random}` that a
  second create-if-absent and a replace against a stale version are both
  refused. If either succeeds, the open fails with
  `ConditionalWritesNotEnforcedException`, naming the store. Garage is
  one such store. A follower does not probe and needs only read access.
  The supported stores are those the contract suite's race property has
  passed, Azure Blob Storage and RustFS; any other S3-compatible store is
  untested. A writer supplies `ObjectStorageOptions.ProbeRandomness`.
- **Static keys only on loopback** (ADR 0137): an emulator on
  `127.0.0.1` takes its development key. Anywhere else, a key, a SAS
  token or a connection string is refused at start, by name.
- **Extents** (ADRs 0138, 0139):
  - A checkpoint is a chain of extent sets, each the validity intervals
    of the quads that changed in its range, with a dictionary, named by a
    catalog under the writer's fence.
  - As-of reads inside a checkpoint's range are served from the extents
    instead of a full run per checkpoint. Head reads merge across the
    sets, so the operator guide states a checkpoint budget (at most 20)
    until Extent maintenance lands.
  - **A cold start reads the log only after the catalog.** The catalog
    records where the log continues, the backend's slot there and its
    sets' ends' commit index entries. A backend that keeps its log in
    parts implements the new `ISegmentCursor` (with `SegmentSlot`), and
    an open reads only the chunks from that slot on. The commit index
    below the catalog is read from the log on the first read that needs
    it, and kept in the local cache. Scale to zero is the deployment's
    default.
- **The server**:
  - `Varve:ObjectStorage` names one store, with each dataset under its
    own prefix.
  - `/health/ready` reports `superseded` and is false for a superseded
    dataset, while `/health/live` stays true.
  - A write to it answers `503 writer-superseded`.
  - `varve lease` shows the writer record, and `--take --epoch <n>` takes
    the dataset over as an operator.
- **Deployment** (ADR 0142):
  - `deploy/azure/container-apps/`: one replica, a storage account with
    shared-key access off, and the app's identity granted the container
    alone.
  - `Varve.Aspire`'s `WithAzureBlobStorage`.
  - Operator pages for Azure Container Apps and for a small machine. On a
    small machine the file backend on an NVMe SSD is the supported setup.
- **The reference domain** (ADR 0141): RealEstateCore 4.1.0 is vendored
  with its SHA-256s. A deterministic portfolio generator gives sizes S, M
  and L, which drive the benchmarks and the properties.
- ADR 0140 records what Varve takes from Azure Data Explorer, and what it
  does not.

#### Changed

- **Derived format 4** (breaking for a reader of `derived/`, which no
  package exposes). A format-3 dataset opens on its full checkpoints,
  starts a chain of sets at its first checkpoint under this release, and
  lets the old checkpoints be dropped, with no change to any answer.
- `Varve.Protocol`: `DatasetStorage.ObjectStorage`, and the problem type
  `writer-superseded`.
- `Varve.Protocol.Client`: `SetupStorage.ObjectStorage` in a dataset's
  setup.
- `Dataset.AsOfLogDistance` is public: the commits an as-of read replays
  from the log, none where the extents answer. ADR 0114's as-of bound
  is measured with it.

## [0.1.0-preview.7] - 2026-10-15

**Client and integration, part 2 — domain events, the event catalogue, the schema registry**

### Milestone Client and integration, part 2 — domain events, the event catalogue, the schema registry

The same events for systems that do not speak RDF. This release closes
milestone Client and integration (#93); its first part shipped in
`v0.1.0-preview.6`. The storage format and the change feed's format are
unchanged. The five ADRs are filed unaccepted and accepted on the pull
request (ADR 0066).

#### Added

- **Domain events as CloudEvents** (ADR 0143). A publisher writes an event
  as quads in an events graph, in the same commit as the change it
  describes. `Varve.Events` (layer 4) renders each one as a CloudEvent 1.0
  in structured JSON, in RFC 8785 canonical form. It uses a declared SPARQL
  `SELECT` and the declaration in force at the commit's position, so a
  replay yields the same bytes.
  - A number is written only when it survives a double round trip
    exactly.
  - An event that cannot be rendered stands in its place as a CloudEvent of
    the problem type `event-not-rendered`, never guessed and never skipped.
  - Served at `GET /datasets/{name}/events` as a batch,
    `application/cloudevents-batch+json`, and as server-sent events on an
    open range, with the commits resource's positions.
- **The event catalogue** (ADR 0144). Declarations are data in one catalogue
  graph per source graph, `varve:events-catalogue/{source}`, readable and
  writable exactly by the source graph's readers and writers.
  - The catalogue's views: `GET …/events/types`, an AsyncAPI 3.0 document at
    `…/events/asyncapi`, and SPARQL over the catalogue graphs. All three
    agree at every position and under every grant.
  - The service description links them.
- **The schema registry** (ADR 0145). JSON Schema 2020-12, generated from a
  declaration on read, at immutable versioned and revisioned URLs, held by
  golden files under the read-forever rule.
  - A breaking change is refused with `event-schema-incompatible` unless it
    raises the version.
  - A malformed declaration is refused with `event-declaration-invalid`.
  - `GET /events/types` lists the types of every dataset the caller reads.
- **`FollowEventsAsync`** in `Varve.Protocol.Client`: `FollowAsync`'s
  sibling over the events resource, handing over each commit's events as
  the canonical bytes the server rendered. Also new: `EventsAsync`,
  `EventTypesAsync`, `EventSchemaAsync`, `AsyncApiAsync` and
  `ServerEventTypesAsync`.
- **`Varve.Json.Canonical`** (layer 0, ADR 0147): RFC 8785 canonical JSON,
  the one implementation `Varve.JsonLd` and `Varve.Events` share, with a
  writer that cannot write anything else.
- **The Event Hubs bridge**, `samples/Varve.EventHubs.Sample` (ADR 0146). It
  publishes each event to Azure Event Hubs, partitioned by dataset, and is
  tested in CI against the Event Hubs emulator, resumed from every
  position.
- `Varve:Events:Source` fixes the base of event and schema URLs, and
  `Varve:Events:EventHubs` adds an Event Hubs server to the AsyncAPI
  document. The operator guide's Integrate page shows one type end to end
  and states what a replay costs.

#### Changed

- **`Varve.Protocol.Client`, the Graph Store writes** (breaking).
  `PutGraphAsync`, `PostGraphAsync` and `DeleteGraphAsync` take a
  `GraphContent` (`FromBytes`, `FromText`) and a `GraphCondition` (`None`,
  `IfMatch(version)`, `IfAbsent`). The `byte[]` and `string` overloads and
  `CreateGraphAsync` are removed; `CreateGraphAsync(g, c)` becomes
  `PutGraphAsync(g, c, GraphCondition.IfAbsent)`.
- **Setup migrations** state the graphs they name in `Varve-If-Graphs-Match`
  instead of the whole dataset's `If-Match`. So a writer of another graph
  no longer moves a migration; one whose graphs cannot be stated falls back
  to the whole dataset.
- `GraphScope` gains a governing-graph seam (ADR 0107 amended).
- `Varve.JsonLd` writes `rdf:JSON` through `Varve.Json.Canonical`. Its bytes
  are unchanged.
- **`Varve.Aspire`**: `MessagePack`, which `Aspire.Hosting` 13.6.1 brings in
  at 2.5.302, is pinned to 2.5.303, past GHSA-qhrr-8q5h-9q3h (ADR 0117
  amended).

## [0.1.0-preview.6] - 2026-10-15

**Client and integration, part 1 — on-behalf-of, the delta format, follow, setup, CORS**

### Milestone Client and integration, part 1 — on-behalf-of, the delta format, follow, setup, CORS

What an application installs to use Varve. This release closes #103, the
first part of milestone Client and integration (#93); events, the
catalogue and the schema registry follow in its second part. The storage
format is unchanged, and the change feed's format, `delta-format: 1`, is
named in a release for the first time: from here on it is read for ever.
The seven ADRs are filed unaccepted and accepted on the pull request (ADR
0066).

#### Added

- **`Varve.Delta`** (layer 3, ADR 0128): the change feed's format,
  `application/vnd.varve.delta; version=1`, as a package of its own, with
  the reader, the writer and the record model over `Varve.Rdf` and
  `Varve.Turtle`. A consumer of the feed ships no store. Version 1 is read
  for ever, and every release descriptor names the version it writes
  (`delta-format`, ADR 0102 amended).
- **On-behalf-of** (ADR 0127): `SparqlHttpClient` takes a
  `BearerTokenProvider`, called for every request, so one client serves
  many users; a token past its expiry is never sent. The request span
  carries the acting application as `varve.azp` (`Auth:ApplicationClaim`),
  never the log. The operator guide states the attribution rule: a write
  through on-behalf-of names the user, a write on the application's own
  account names the application. `deploy/azure/entra/` creates the Entra
  registrations, with federated credentials and no secret, and
  `samples/Varve.OnBehalfOf.Sample` exchanges with MSAL.
- **`FollowAsync`** (ADR 0129): the commits followed from a position, in
  pages and then live, each commit to a handler and then its position to
  an `IFollowCheckpoint` the caller owns: at least once, resumable from any
  position.
- **Dataset setup** (ADRs 0130, 0131): `DatasetSetup` and `ApplyAsync`
  declare a dataset, its settings as desired state, a baseline that only
  adds, and migrations each recorded in the system graph `varve:setup` in
  its own commit; applying again commits nothing. A setup file in a
  specified YAML subset; `AddVarve` applies it as an application starts;
  `varve setup apply` from a pipeline; `Varve.Aspire`'s `WithDataset(…, setup:)`
  in an AppHost.
- **CORS** (ADR 0132): `Varve:Cors:AllowedOrigins` and `MaxAge`, never
  credentials, the headers a browser application needs, Commit path's
  `Varve-If-Graphs-Match` among them; nothing at all when unconfigured.
- The dependency register checks a package's `Scope` (ADR 0133): a
  package admitted for `samples/` alone fails the gate anywhere else.
- The operator guide gains Datasets and Integrate.

#### Changed

- **`Varve.Protocol.Client`** (breaking, ADR 0118): `AccessToken` is
  removed in favour of the token provider; `ClientLimits.MaxResponseBytes`
  is a `long`; the package no longer references `Varve.Store`, and
  references `Varve.Delta`, `Varve.RdfXml`, `Varve.JsonLd` and the two
  `Microsoft.Extensions` abstractions packages.
- **`Varve.Protocol`** (breaking): `ChangeFeedReader`, `FeedRecord` and
  their types move to `Varve.Delta`, with the format's own header types.
- **`Varve.Aspire`**: `WithDataset` takes a setup file.
- **The `varve` command line** renews a token from the issuer before it
  expires, and gains `setup apply`.

## [0.1.0-preview.5] - 2026-10-13

**Commit path — group commit and per-graph concurrency**

### Milestone Commit path — group commit and per-graph concurrency

Concurrent writers stop paying for each other. This release closes
milestone Commit path (#91): the sequencer writes the commits that queued
behind a flush in one write and one flush, by default on every durable
backend, and a writer conflicts only with commits that touched the graphs
it read or writes. The log's bytes for a commit are unchanged: a dataset
written by `v0.1.0-preview.1` opens as it is, and storage format 1 gains
a reader rule, not a byte. The three ADRs are filed unaccepted and
accepted on the pull request (ADR 0066).

#### Added

- **Group commit** in `Varve.Store` (ADR 0124): requests queue in the
  sequencer; a lone request is written at once, with no timer, and those
  that arrive while a flush runs are staged in arrival order, each keeping
  its own commit, position, timestamp and conflict check, and written
  together, at most 1,024 records or 4 MiB. No request is answered before
  its batch is durable. A projection failure ends the batch at that
  member; a request cancelled while queued is never written.
  Unset, `DatasetOptions.GroupCommit` follows the storage's durability:
  on for files and the browser, off in memory, which has no flush to
  share (`GroupCommitOptions.For`); set, it applies on every backend.
  `Store:GroupCommit:MaxRecords` and `MaxBytes` in the server, and the
  `varve.store.batch_size` histogram. On files, eight HTTP writers of
  `INSERT DATA` go from 2,418 to 5,766 commits a second.
- **Per-graph optimistic concurrency** (ADR 0125, amending 0011 and
  0057): `CommitRequest.ExpectedGraphs` gives the expected position a
  scope, and the check conflicts only when a commit after it touched a
  graph in the scope. A `Settings` or `Erasure` commit touches every
  graph. `SparqlUpdate` infers the scope from what the request asked for —
  a graph it found absent included — and from what it writes;
  `UpdateOptions.ExpectedGraphs` states one. The graph-version index is
  part of the default projection, persisted as `index/graphs` and beside
  each checkpoint, rebuilt from the log when absent;
  `Dataset.GraphVersion`, `GraphVersionAsync` and `ConflictsAsync`. Eight
  writers of a read-modify-write, one graph each, met 87,904 conflicts on
  files and now meet none.
- **Graph versions over HTTP** (ADR 0126, amending 0094, 0096, 0114 and
  0119): a Graph Store `GET` is tagged with the graph's version, as of
  the read's position; `If-Match` and `If-None-Match` on a Graph Store
  write are the graph's, and `If-None-Match: *` on `PUT` creates only;
  `Varve-If-Graphs-Match` states graph versions for a SPARQL Update. A
  `409` names the graphs of the request's scope that changed and the
  commits that changed them, at most 100, with `graphsTruncated`; the
  server retries an inferred conflict up to three times and never one
  the client stated. The service description says so.
- **`Varve.Protocol.Client`**: `SparqlHttpClient.VersionOf` reads a
  graph's version from a response; `GetGraphAsync` with `ifNoneMatch`
  and `asOf`; `PutGraphAsync`, `PostGraphAsync` and `DeleteGraphAsync`
  with `ifMatch`; `CreateGraphAsync`, create-only; and `UpdateAsync` with
  `ifGraphsMatch`.

#### Changed

- **Breaking, `Varve.Protocol`**: `ISparqlUpdateExecutor` takes
  `UpdatePreconditions` (`Varve.Protocol.Model`) in place of an expected
  position; an implementation outside this repository changes its
  signature.
- **Storage format 1, the reader** (ADR 0072, amended): a record broken
  within the 4 MiB write window of the log's last good record reads as
  a torn tail, because a batch is one write; the bytes of a record are
  unchanged and an older log reads as before.
- **The spec** goes to 1.7: T1 step 1 is the scoped check, and the
  default projection holds the graph-version index.
- **`Status`** checks that the README's image tags name the latest
  release.

#### Not met

- **#35** stays open, unchanged by this release: `INSERT DATA` of 100,000
  quads took 427.8–472.2 ms before it and 447.6–461.2 ms after, in one
  interleaved run, allocating the same 258.88 MB, against pyoxigraph's
  393.2 ms; the phases are measured in the benchmarks README.

## [0.1.0-preview.4] - 2026-10-12

**RDF 1.2 Turtle and TriG, RDF/XML, JSON-LD**

### Milestone 6b — RDF 1.2 Turtle and TriG, RDF/XML, JSON-LD

Varve reads and writes every W3C RDF syntax. This release closes milestone
6b (#10) and brings the packages on nuget.org from fourteen to sixteen. The
storage format is unchanged: a dataset written by `v0.1.0-preview.1` opens
as it is. The three ADRs are filed unaccepted and accepted on the pull
request (ADR 0066).

#### Added

- **RDF 1.2 Turtle and TriG** in `Varve.Turtle` (ADR 0121,
  `docs/spec/turtle.md` §9 rewritten): reified triples, triple terms,
  annotations, reifiers, `@version` and `VERSION`, and directional
  language tags, read and written. The writer declares `VERSION "1.2"` on
  its own the moment it writes a construct RDF 1.1 cannot read, and
  `TurtleWriteOptions.AlwaysDeclareVersion` forces it. `RdfVersion` on the
  read options decides the one point where the editions contradict each
  other, a surrogate pair written as two `\u` escapes; the default is RDF
  1.2. The four `rdf12` Turtle and TriG suites pass whole, and the 41
  SPARQL 1.2 evaluation cases they had blocked run.
- **`Varve.RdfXml`** (layer 2, ADR 0122, `docs/spec/rdf-xml.md`): RDF/XML
  over `System.Xml`'s `XmlReader` and `XmlWriter`, with
  `rdf:parseType="Triple"`, `rdf:annotation` and `its:dir`, the RDF 1.2
  forms read where `rdf:version` announces 1.2. Both W3C suites pass whole
  with no exemption; the cost per triple is `XmlReader`'s, 16 bytes read
  and 34 written, measured and stated. The N-Triples translations of the
  SPARQL suites' RDF/XML files are deleted and the harness reads the
  originals.
- **`Varve.JsonLd`** (layer 2, ADR 0123, `docs/spec/json-ld.md`): JSON-LD
  1.1 over `Utf8JsonReader` and `Utf8JsonWriter` — expansion, toRdf and
  fromRdf — as an index-linked tree in pooled arrays, zero bytes per quad
  in a steady state. `rdf:JSON` literals are written in RFC 8785 form,
  numbers in XSD canonical form, and `@direction` is RDF 1.2's directional
  literal by default, with the specification's `i18n-datatype`,
  `compound-literal` and `null` as options. Remote contexts come only
  through the caller's document loader. The toRdf, expand and fromRdf
  suites pass whole under a stated rule for the 1.0-only cases.
  Compaction, flattening and framing are not implemented.
- **`IQuadSource.TryGetTripleTermComponents`** (`Varve.Rdf`, default
  implementation): a source may answer the components of a triple term by
  handle, which is what matching a blank node inside one needs; the BGP
  operator uses it. `InMemoryDataset` answers; the store does not yet, and
  its twelve lines are exempt with Oxigraph's agreement recorded, until
  #87, due before milestone 8.
- The AOT and browser smokes read and write all three syntaxes. The
  conformance baseline holds 5,507 lines, up from 4,122, with 24 justified
  exemptions.

#### Changed

- **`Varve.Protocol`** (ADR 0092, amended): a Graph Store `GET`, a
  `CONSTRUCT` or `DESCRIBE` and the service description are negotiated
  over `application/rdf+xml` and `application/ld+json` beside N-Triples,
  Turtle, N-Quads and TriG, which stays the default; a Graph Store write
  reads all six, and the service description names them. RDF 1.2's triple
  terms and directions travel where the syntax has them. A graph the
  chosen format cannot spell (a triple term in JSON-LD, a predicate with
  no qualified name in RDF/XML) is `406 not-acceptable`, saying why; the
  graph JSON-LD holds is charged against `Limits:MaxQueryMemory`. The
  server's Native AOT binary grows by 1.0%. `Varve.Protocol.Client`'s
  `LOAD` fetch and the `varve` command line are unchanged.
- **`Varve.Turtle`**: `TurtleOptions.OnVersion` reports a version
  directive; a triple term as subject or predicate is refused by name by
  the writer; the recovery rule counts the RDF 1.2 brackets (ADR 0030,
  amended).
- The `json-ld-api` repository is the third test submodule.

## [0.1.0-preview.3] - 2026-10-09

**Operability — the container, telemetry, governance, configuration, Aspire**

### Milestone Operability — a server somebody can run

This release closes milestone Operability (#12): what `Varve.Server`
needed before anyone operates it, in eleven ADRs (0110–0120). The storage
format is unchanged: a dataset written by `v0.1.0-preview.1` opens as it
is. **The HTTP API changes while everything is preview** (ADR 0118); the
renamed routes are listed under *Changed*, and the old paths are gone, not
aliased. One package is added, the fourteenth: `Varve.Aspire`.

#### Added

- **The container image**, `ghcr.io/hafeok/varve` (ADR 0111): the Native
  AOT binary on `mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled`,
  one image per architecture (`linux/amd64`, `linux/arm64`) built on a
  native runner and joined by digest into one manifest, non-root, a
  read-only root filesystem, datasets on a volume at `/var/lib/varve`,
  port 8080, no `HEALTHCHECK`. Build provenance and an SPDX SBOM are
  attested for the manifest's digest and verified with
  `gh attestation verify`. CI runs the W3C protocol suites and both auth
  legs against the container it built, on every pull request.
- **Telemetry in two layers** (ADR 0112): `Varve.Protocol` owns the
  `ActivitySource` `Varve.Protocol`, one span per request named by its
  operation with the database semantic-convention attributes
  (`db.query.text` only by opt-in); `Varve.Store` owns the `Meter`
  `Varve.Store`, with commit and checkpoint durations, the sequencer's
  queue, pinned reads, projection lag, log and derived bytes, every
  measurement tagged with the dataset. The server exports all three
  signals over OTLP when an `OTEL_EXPORTER_OTLP_*` endpoint is set, and
  builds no SDK otherwise. `docs/operator/observe.md` lists every name.
- **Health and readiness** under `/health/live` and `/health/ready`
  (ADR 0113): ready when every dataset that should be open is open, not
  failed and within `Varve:Health:ReadyLag` of its head; `503 not-ready`
  with each dataset's state and reason otherwise, and during the drain,
  for `Varve:Health:StopDelay` before the listener closes. Both probes
  are rate-limited per client address.
- **A stated answer for every limit** (ADR 0114): query memory bounded by
  counting rows in joins, sorts, groups and `DISTINCT`
  (`422 memory-limit-exceeded`, before the first byte or as a trailer),
  the as-of distance (`422 as-of-distance-exceeded`), live tails per
  client (`429 too-many-live-tails`), concurrent reads with a queue
  (`503 server-busy` with `Retry-After`), and paged commit ranges
  (`Link rel="next"`). The service description states the client-facing
  limits.
- **Configuration precedence** (ADR 0115): file, environment, command
  line, later winning; `varve serve` takes typed options and `--config`,
  `--set`; `--print-config` prints every effective value with its source,
  secrets redacted; **an unknown key under `Varve:` refuses to start**.
- **The lease** (ADR 0116): a lease another process holds is waited for
  (`Varve:Lease:WaitFor`, 30 s) and never broken; `varve lease <dir>`
  reports it, `--break` frees one a dead process left on a filesystem
  that did not.
- **`Varve.Aspire`** (layer 0, ADR 0117): `AddVarve`, `WithOidc` and
  `WithDataset` on an Aspire AppHost; an anonymous server runs in run
  mode and refuses to publish. A sample AppHost with a test issuer is
  tested in CI.
- **The problem catalogue** (ADR 0119): `ProblemCatalogue` fixes every
  type's title, status and members; `docs/problems/` has one page per
  type, which its IRI resolves to; `Vary`, `Varve-Request-Id` and
  `Link rel="service-desc"` on every dataset response; `Cache-Control`
  by read kind (`immutable` for a position, `no-cache` for the head);
  `Last-Modified` and `If-Modified-Since`; RFC 6750 `error` codes on
  `401` and `403`.
- **The shipped runtime configuration** (ADR 0110): the workstation
  collector, concurrent, with `System.GC.ConserveMemory` 5, in the tool
  and the AOT binary alike; the soak gate is judged under it, its drift
  measured against minutes 30–40 (ADR 0082 amended, and superseded in
  part by 0110). A two-hour soak under it did not hold that drift within
  10%; #61 stays open.
- **`Varve.Store`**: `DatasetOptions.Name`, the `db.namespace` of every
  measurement.
- **`Varve.Protocol`**: `ProtocolOptions.Telemetry` and `Logger`;
  `ProtocolLimits.MaxQueryMemory`, `MaxAsOfDistance`,
  `MaxLiveTailsPerClient`, `CommitsPageSize`; `Dataset.TimestampAt`.
- **`Varve.Sparql.Evaluation`**: `EvaluationOptions.MemoryBudget` and
  `MemoryBudgetExceededException`.
- **The operator's guide** gains *Observe*, the container and a systemd
  unit under *Run*, the full Entra and generic-issuer examples under
  *Authenticate*, every limit under *Configure*, and the stale lease.

#### Changed

- **Routes, while everything is preview** (ADR 0118): `/live` and
  `/ready` are `/health/live` and `/health/ready`; `/datasets/{name}/feed`
  is `/datasets/{name}/commits`, with `/commits/{position}` for one
  commit; `POST …/open` and `…/close` are `GET`/`PUT /datasets/{name}/state`
  with `{"state":"open"|"closed"}`; `/datasets/{name}/settings` is a
  resource (`GET` with `ETag`, `PUT` whole, `PATCH` merge patch, under
  `If-Match`); `PUT /datasets/{name}` is idempotent (`204` for one that
  exists with the same storage, `409` otherwise). The service description
  says `varve:commits` and `varve:state` where it said `varve:changeFeed`.
- **`Varve.Protocol.Client`**: `FeedAsync` is `CommitsAsync`;
  `CommitAsync(position)`, `StateAsync`, `SetStateAsync`, `SettingsAsync`,
  `PutSettingsAsync`, `PatchSettingsAsync` added.
- **`Varve.Sparql.Store`**: a request of `INSERT DATA` and `DELETE DATA`
  alone with no expected position goes straight to the commit request,
  with no pin and no staging (ADR 0120); `INSERT DATA` of 100,000 quads
  into an empty store, parsing included, is 1.3× faster and allocates a
  fifth less. #35 stays open on the ADR's own terms: the median is not
  yet at or below pyoxigraph's on the same machine.
- **Problems** carry `instance`, the request id, and only the members
  their type declares; the `410` for the archive horizon stays `404`
  (RFC 9110 §15.5.9).

## [0.1.0-preview.2] - 2026-10-08

**The server, the protocols, authentication, the command line, federation**

### Milestone 7 — the server, the protocols, authentication, the command line, federation

Varve speaks HTTP. This release closes milestone 7 (#11) in its two halves,
7a and 7b, and brings the packages on nuget.org from ten to thirteen, the
last of them an executable. The storage format is unchanged: a dataset written by
`v0.1.0-preview.1` opens as it is. The derived format moves from 2 to 3;
a format-2 run is read as before and migrates when maintenance rewrites it.

#### Added

- **`Varve.Protocol`** (layer 5): the SPARQL 1.1 Protocol, the Graph Store
  Protocol and the service description mounted on any ASP.NET Core host,
  one commit per write with `Varve-Position`, `If-Match` and `409`/`412`,
  every read pinned for its response and bounded by `Varve:Limits`, time
  travel by `Varve-As-Of` and `ETag`, the change feed
  (`application/vnd.varve.delta; version=1`) and the diff, RFC 9457
  problem details everywhere, and the admin API: `/status`, `/datasets`
  with create, open, close and delete, settings, checkpoints and
  projection status (ADRs 0091–0098, 0106).
- **`Varve.Protocol.Client`** (layer 5): the HTTP client, with the
  endpoint policy that refuses private addresses and follows no redirect
  unless told, and every request bounded (ADR 0103).
- **`SERVICE` and `LOAD` over HTTP**, through the client, under the same
  endpoint policy; a server without a `ServiceHandler` keeps refusing
  `SERVICE` and says so in its service description (ADR 0104).
- **`Varve.Server`** (layer 6): the host, as a Native AOT single file and
  as the .NET tool `varve`. OIDC bearer authentication, cumulative
  permissions, graph-level grants by graph list or prefix (ADR 0107),
  `/live` and `/ready`, graceful shutdown, configuration validated at
  start (ADRs 0099–0101, 0105).
- **The `varve` command line**: `serve`, `create`, `info`, `load`,
  `query`, `update`, `export`, `checkpoint` and `feed`, embedded over a
  directory or remote against a server, with the device-code and
  client-credential flows and a credential file; `--no-store` for CI
  (ADR 0105).
- **The operator's guide**, `docs/operator/`: run, configure,
  authenticate, back up and restore, upgrade.
- **`Varve.Store`**: the bulk load resolves and spills on worker threads,
  `BulkLoadOptions.Workers` (ADR 0108); each run carries a term filter
  consulted before its hash index, derived format 3 (ADR 0109). On the
  10 M-quad gate the load went from 82.7 s to 68.2 s, and a lookup of
  an absent term from 48.8 ms to 2.9 ms per 10,000.
- **Releases as code** (ADR 0102): a release is `releases/<version>.yaml`,
  `CHANGELOG.md` is its projection, and README's Status section is
  checked against what it describes.

#### Changed

- **`Varve.Sparql.Store`**: `UpdateOptions.ExpectedPosition`, and a
  data-only request expects no position (ADR 0057, amended). The
  evaluator's minted blank nodes are labelled `q<n>`.
- **`Varve.Store`**: a delivered commit externalises its handles and
  dispose drains the sequencer (ADR 0042, amended); the file suites run
  without the device flush (ADR 0090).
- The W3C `protocol`, `graph-store-protocol` and `service-description`
  suites pass whole on both stores and through the graph-scoped source;
  `http-rdf-update` with six justified exemptions a store; the
  conformance baseline holds 4,122 lines.

#### Fixed

- **`Varve.Sparql.Results`**: the JSON results reader counted line ends
  from the document's start at every term, so reading was quadratic in the
  document: 10,000 solutions took twenty seconds, and `SERVICE` negotiates
  JSON first. The count is incremental; 8,000 solutions read in 75 ms.

## [0.1.0-preview.1] - 2026-10-07

**The first release, milestones 1 to 6c, from the foundation to the durable store**

### Milestone 6c — the dictionary on disk, the bulk loader, the browser backend, the soak's causes

#### Added

- **store**: derived format 2: streamed merges, the dictionary in runs, compressed blocks (24e2cf63)
- **store**: a checkpoint policy, and runs deleted only once nothing reads them (428762be)
- **store**: Varve.Store.Browser, OPFS and IndexedDB storage (1c691bb7)
- **store**: replica bootstrap by copying a checkpoint and the log (3b0e8c2a)
- **store**: the bulk loader, by sort and merge-join (3acea82a)
- **store**: the commit index, derived and paged (#61) (8a1b378c)

#### Changed

- **store**: the storage contract's cases as plain methods (83bc754b)
- **adr**: 0084, the browser backend (0c427f18)
- **store**: the browser backends in headless Chromium, and log/ across hosts (97ecbe73)
- run the browser storage tests in Chromium (339de309)
- **traceability**: the browser backend session (e3189738)
- **adr**: 0078-0083, the decisions of milestone 6c (8a0b40b7)
- **spec**: storage format, derived format version 2 (4cf1629f)
- **bench**: the soak with causes separable, and the bulk gate (af7cf973)
- milestone 6c in AGENTS, the roadmap, testing and the store's README (bbbfa573)
- **store**: a bulk commit's ranks in memory until its allocations are written (7c3c5adf)
- **bench**: the bulk gate's crash check at every record, torn on a sample (9f5e3cec)
- **aot**: bulk load, checkpoint and replica ship in the native AOT smoke (d51e08eb)
- **traceability**: milestone 6c's record, the prompt (the report follows) (97e63aae)
- **bench**: the bulk gate's post-close check on an empty dataset (97a966b9)
- **bench**: the bulk gate says when it skipped the record cuts (b922c60b)
- **adr**: 0080 and 0082, fences in chunks and the dataset's own in the soak band (27e7fff6)
- **adr**: 0080 and 0082, checkpoints held sparsely; the soak reports their directories apart (d6162208)
- **store**: a seek decodes the block its range starts in once (6ff6a947)
- milestone 6c's report and benchmarks (00863f47)
- **roadmap**: 6c's soak, its four causes and the gate's result (3825a11e)
- regenerate the changelog (a89181d2)
- **decisions**: accept 0078-0084 (4d9f800d)
- **adr**: the index records 0078-0084 as accepted (9778f2a7)
- **publish**: version from the v tag, release notes, the missing gates (2157a88c)
- **adr**: 0085, the commit index is derived and paged; tests and spec (32401323)
- **store**: the commit index cites no decision in code (6c7681bd)
- **store**: the id layout is format version 1's, ADR 0072 (044c4945)
- **adr**: 0085, a release per milestone (d6b555be)
- **changelog**: --release cuts a version, --check reads the newest tag (00ba4293)
- the release pending gate (c7968017)
- **publish**: check the tag's changelog section, notes stop at the links (1631f225)
- **traceability**: the release tooling and ADR 0085 (b7b31f11)
- regenerate the changelog (e75a1779)
- **store**: the naive contract backend locks; the commit index's benchmarks (2d328351)
- **adr**: 0086, the home of the eng/ gates (176b9f1f)
- **traceability**: ADR 0086 and the port issues (3d35bc8d)
- regenerate the changelog (7f64ce2d)
- **adr**: 0087 the identity map, 0088 only checked commits reach main (01eb25d1)
- **eng**: the identity map, and issue-refs' exempt list read from it (9ac165eb)
- the dco gate, with its failing fixture (22f71e56)
- **decisions**: an agent's decision is accepted by its responsible human (4b2edd72)
- the agent review check (7cfc5c07)
- **repo-standard**: every gate a required check on ruleset 1 (4956b7e6)
- landing on main through checked commits, and the sign-off the dco gate checks (6a7b403d)
- **traceability**: the dco gate, the identity map, ADRs 0087 and 0088 (50e80192)
- regenerate the changelog (f5cc65ee)
- regenerate the changelog (cdbfd651)
- **decisions**: accept 0085-0088 (4c7e9119)
- **adr**: the index records 0085-0088 as accepted (9ff30c05)
- regenerate the changelog (1ccfacc0)
- **agent review**: a comment approval on the responsible human's own pull request (28ed70bd)
- every required check is a job that reports on pull requests (b497f58c)
- **adr**: 0087 approval by comment and the exit to a ledger Review; 0088 the required-checks gate (57ef9692)
- approval by comment on one's own pull request (8abb2e1c)
- **traceability**: the fifth round of #63's session (3a017f99)
- regenerate the changelog (c8ac6371)
- **decisions**: accept the 2026-10-06 amendments of 0087 and 0088 (38eb29a9)
- the commit index's addendum to the 6c record, the soak and its ablations (b999fe8e)
- regenerate the changelog (cba3e1a9)
- **agent review**: judge through the API only, approve by conversation comment, post the check as the gates App (f33f16e6)
- **agent review**: main's script on trusted triggers only, and the tamper test (39f8ddc1)
- **repo-standard**: the gates environment, and land/** for the maintainer only (f86005b8)
- required-checks records App pinning (75b6de28)
- **adr**: 0087, the judging script is always main's; approval by comment is not a valid workflow (4fef5117)
- approval by conversation comment, judged by main's script (a6842aa6)
- **traceability**: the sixth round of #63's session (ec2d14e9)
- regenerate the changelog (9d11b1b4)
- **adr**: the commit index's ADR is 0089 (b8696a65)
- **repo-standard**: pin agent review to the gates App; the repo-standard App's key from the gates environment (c0e4563b)
- **traceability**: the seventh round of #63's session (d638a0f4)
- regenerate the changelog (cf0d11ca)
- **repo-standard**: the land ruleset's update rule with its parameter (57391e7e)
- regenerate the changelog (dcafd4f7)
- **adr**: accept 0089, the commit index is derived and paged (d0102f09)
- **agent-review**: remove the pull_request trigger and the transitional job (dc074495)
- regenerate the changelog (a1271e92)
- the PR template states ADR 0088's landing rules, not ADR 0032's (8c3ac610)
- regenerate the changelog (f76ed53f)
- accepted (8a109048)

#### Fixed

- **store**: bulk term references by SHA-256, and sorts in parallel parts (c9a65a5f)
- **store**: a bulk load deletes each spill as soon as its pass is done (03eceb90)
- **store**: a bulk commit's passes share its memory without overlapping (617398cb)
- **store**: bound recovery after a crash during or just after a bulk load (6213757f)
- **store**: fences in chunks below the large object heap (#61) (7f4b846c)
- **store**: a checkpoint's sections held sparsely (#61) (87b692ec)
- **decisions**: 0088's key no longer collides with its set's class (DDGEN0005) (32ea8544)

### Milestone 6a — the file backend, format version 1, failure injection

#### Added

- **store**: derived blobs are written as streams and read synchronously (987f41c2)
- **store**: format version 1 of log/, and opening from the newest checkpoint (6f9c3091)
- **store**: FileStorage, a dataset directory of plain files (1bbec4ca)
- **store**: the default projection on disk, as runs in derived/ (4b8f8493)

#### Changed

- **adr**: 0070-0077 for the file backend and format version 1; spec 1.4 (6ad54c27)
- **store**: milestone 4's properties on the file backend (3d5b72c4)
- **aot**: a file-backed dataset crashed and recovered under Native AOT (e5688b51)
- **bench**: the file backend's commits, scans, sizes and soak (6a90bb5d)
- the file backend in the roadmap, AGENTS, the store's README and testing (7b8c933e)
- **store**: pool a cursor's block buffers; derived reads stay on RandomAccess (42837592)
- **bench**: milestone 6a's numbers, the soak, and ADR 0012's verdict (a1975695)
- **traceability**: milestone 6a's record, and the decision report's baseline (a8f8415a)
- regenerate the changelog (1cdf56dc)
- I6 in specification 1.5, the storage follow-up as 6c, durability per platform (5bda9919)
- **traceability**: the maintainer's decisions on the 6a pull request (aae2f582)
- **bench**: durability measured on Linux, Windows and macOS (70694b8d)
- regenerate the changelog (08affd44)
- **decisions**: accept 0070-0077 (7cd036cf)
- **decisions**: accept 0070-0077 (fcef6df3)
- **adr**: the index records 0070-0077 as accepted (3a2bace1)
- **store**: cut the log on files at its boundaries, not at every byte (9a773ccf)
- **adr**: 0071 amended for replacing an open blob on Windows (2168209f)

#### Fixed

- **store**: flush a segment before the trailer that seals it (842d3bdf)
- **store**: bytes covered by a later commit or a sealed trailer refuse (61c0f85d)
- **store**: replace an open derived blob on Windows (fcf15718)

### Adopting DecisionDriven.Analyzers — the decision ledger, the DD rules, Varve's own rules only

#### Added

- **analyzers**: VARVE0003's allow-list names members as well as types (c96f631d)
- **api**: ColumnIndex, MaxRecordBytes as a ByteCount, the implicit timezone as a TimeSpan (68c25d05)
- **eng**: decision-sets refuses an acceptance dated before its filing (621fbed6)

#### Changed

- **adr**: 0062–0067, adopting DecisionDriven.Analyzers (17852ee3)
- **eng**: decision-sets, the front-matter check for docs/decisions (b364d25d)
- **traceability**: issue 43, adoption session 1 (d8b0cadb)
- changelog (a6ddbf4f)
- **adr**: VARVE0005 keeps VARVE0002's uncovered half; 0003 superseded in part (8618312f)
- **decisions**: ADRs 0001–0010 as decision sets (3cf2966f)
- **traceability**: the enumeration of 0001–0010 (64139623)
- **decisions**: ADRs 0011–0020 as decision sets (bf92aabf)
- **decisions**: ADRs 0021–0030 as decision sets (5ae13e3b)
- **decisions**: ADRs 0031–0040 as decision sets (696675f9)
- **decisions**: ADRs 0041–0050 as decision sets (e0644aeb)
- **decisions**: ADRs 0051–0060 as decision sets (edd5825e)
- **decisions**: ADRs 0061–0067 as decision sets; the enumeration is complete (7a696a98)
- **adr**: 0068, dated amendments; 0063 amended with 0006's two rulings (f8c81793)
- **deps**: the AngleSharp pin cites 0063 (e00f7bb2)
- **decisions**: 0068's set; 0001's no-edit rule moves to it; 0063 gains two keys (95c76f76)
- **analyzers**: adopt DecisionDriven.Analyzers 0.1.0-preview.4; VARVE0003-0005 (ea044db5)
- **banned**: reflection members and the runtime binder; the citation gate (b86e82fc)
- **iri**: Varve.Iri under every DD and VARVE rule (6253f56b)
- **xsd**: Varve.Xsd under every DD and VARVE rule (5438f7f7)
- **rdf**: Varve.Rdf under every DD and VARVE rule; InMemoryDataset is a value (1dd01c8e)
- **turtle**: Varve.Turtle under every DD and VARVE rule (14dffc83)
- **sparql**: Varve.Sparql under every DD and VARVE rule (15f03427)
- **sparql-results**: Varve.Sparql.Results under every DD and VARVE rule (e8cb4829)
- **repo-standard**: immutable lookup tables for DD0004 (970b5e8f)
- **rules**: VARVE0003 and VARVE0004 pages as the rules now stand; the ledger README (a24cec38)
- **traceability**: the record of session 2 of #43 (b2cd5c0d)
- **syntax**: one model namespace per syntax package; ADR 0064 amended (2142306c)
- **traceability**: session 2 of #43, after the report (57628376)
- **analyzers**: DecisionDriven.Analyzers 0.1.0-preview.5 (869f6dcf)
- **traceability**: session 2 of #43, preview.5 adopted (c929cce4)
- **store**: the log's values move to Varve.Store.Log (2d991a09)
- **store**: positions, timestamps and storage coordinates are wrappers (5eac224c)
- **store**: Varve.Store under the DD and VARVE rules (9e4fc216)
- **evaluation**: the compiler writes its loops (RS0030) (2cd5415a)
- **evaluation**: Varve.Sparql.Evaluation under the DD and VARVE rules (90b20e5a)
- **sparql-store**: ILoadSource is a contract (ef641848)
- DecisionDriven.Report, never a gate, against a committed baseline (80f25202)
- **traceability**: the record of session 3 of #43 (5e5d0328)
- Accept layer 0-2 decisions (76b9d1fd)
- accepted (993877e4)
- accept (8f4b1fb0)
- **decisions**: drop the duplicated placeholder acceptances (fe7b4800)
- **adr**: 0069, one model namespace per package at layers 3 to 5, proposed (0dbe5114)
- **evaluation,sparql-store,store**: the model namespaces of ADR 0069 (b4447bc2)
- **analyzers**: the hot-path allow-list admits the value types; four calls fixed or cited (cb7f8db3)
- **decisions**: record acceptances for hot-path scope and model namespaces (refs #53) (1f07965c)
- **decisions**: SparqlStoreModel's acceptance sits under its own key (b12116b8)
- **analyzers**: DecisionDriven 0.1.0-preview.6; the Xsd constructors are on the hot path (34f0b79f)
- **wasm-smoke**: Position formats itself (9a7fd287)
- **hosts**: the layer 6 hosts under DD0004 (55a77b3b)
- **decisions**: accept layer-6-host-state (e438ceae)
- **report**: the decision report's baseline, from the green build (254c9d2c)
- ADR 0069 is accepted; the ledger says what was accepted and when (2613ef49)
- **decisions**: accept AllowListNamesMembers and ImmutableArrayAndCancellationTokenByMember (beaabee4)
- **evaluation**: hold every operator to VARVE0003 (c4caef5e)
- **store**: mark the write and replay inner loops (39641af6)
- **evaluation**: hold ORDER BY's keys in chunks that never copy (de8147af)
- close session 3 of #43 (94e6cc04)
- **decisions**: accept the session-3 hot-path keys Refs #43 Signed-off-by: Emil Okkels Klein <emil@okkels-klein.dk> (dbd9fde9)
- **adr**: 0033 amended: the model is named in the record, never in a commit (5efbbaf5)
- **traceability**: the tidy after session 3, and the close-out's Tool row (bf412056)
- **decisions**: date six session-3 acceptances to the day they were made (9a8085e8)
- **traceability**: the tidy's record says the six dates are corrected (2c2da128)
- Update 2026-10-02-issue-43-dd-adoption-tidy.md Refs #43 (6396abbb)
- Update commit-traceability.md (d7301b7b)
- **traceability**: split the tidy record's Model and Session rows again (280697c6)

#### Fixed

- **decisions**: a key may not equal its set's class name (2af9d86c)
- **analyzers**: VARVE0003 checks a conversion operator against the allow-list (29c09d6f)
- **analyzers**: VARVE0003 and VARVE0004 as Varve.Rdf found them (4fb4b51b)
- **analyzers**: VARVE0003 and VARVE0004 do not check test assemblies (afafcc36)
- **analyzers**: VARVE0003 holds overrides of a hot member; static initializers run once (2f3cec52)
- **ci**: the build job fetches the history decision-sets now reads (2d55f89a)

### Milestone 5c — the result writers, SPARQL Update as one commit, RDFC-1.0

#### Added

- **results**: the four result-format writers (213a1c57)
- **store**: validators bound to the dataset, and the staging view (a3f073d3)
- **sparql-store**: Varve.Sparql.Store — SPARQL Update as one commit (ac87f326)
- **rdf**: RDFC-1.0 canonicalisation, bounded by a work limit (7bbe4f77)
- **turtle**: canonical N-Triples and N-Quads are RDF 1.2's, gated by its c14n suites (3f2f08b8)

#### Changed

- **spec**: the result writers, SPARQL Update over the store, RDFC-1.0 (4bf24842)
- **adr**: 0057–0059, update as one commit, staging, canonicalisation (1f580e71)
- **conformance**: the writers against csv-tsv-res and json-res; the CSV cases (8f9fd75e)
- **conformance**: the W3C SPARQL 1.1 update evaluation suites (9ab7a442)
- **conformance**: the rdf-canon suite, the canonicalisation properties, canonical comparison (cabb35a5)
- **results**: escape two characters a test held raw (bc8e864f)
- **smoke**: update, results JSON and RDFC-1.0 under AOT and in the browser (9275b160)
- **bench**: updates and RDFC-1.0 against dotNetRDF and pyoxigraph (9b0d2c97)
- roadmap, README, AGENTS, changelog, the traceability record (474dc370)
- **adr**: 0060, hosts at layer 6 — the composition root, reserved to executables (3356f272)
- **spec**: RDFC-1.0's blank-graph-name limit, stated for callers, with the evidence corrected (4655d17c)
- build the benchmark project on every run, without running it (2b872976)
- changelog (1148f1c5)
- **adr**: 0061, canonical N-Triples and N-Quads follow RDF 1.2 (1dd3eeff)
- **conformance**: the 82 RDF 1.2 canonical-form cases join the ratchet (bfb2ff6a)
- **traceability**: the canonical-form decisions, in the 5c record (eed2926d)
- **adr**: 0057 amended — release the pin, then submit (f62f5989)
- changelog (3f5af6f7)

#### Fixed

- **test**: read allocation where no collection or tier-up can move it (f51f97be)

### Milestone 5b — the optimiser and evaluator, and the result-format readers

#### Added

- **results**: Varve.Sparql.Results — the four result-format readers (31ed038b)
- **evaluation**: Varve.Sparql.Evaluation — the optimiser and evaluator (61c82cfd)

#### Changed

- **adr**: 0053–0056, the questions 5a left to 5b (f62509d9)
- **spec**: the evaluation and result-format specifications (079f1a21)
- **evaluation**: the optimiser and store properties, allocation, MD5 (21dd9090)
- **conformance**: the W3C query evaluation suites, over two subjects (882e349e)
- **smoke**: the evaluator under Native AOT and in the browser (81c2cf1d)
- **evaluation**: ADR 0050's arms, a BSBM-style mix, and Oxigraph (f439e391)
- roadmap, README, AGENTS, changelog, the traceability record (1c9fc2e0)
- **adr**: 0051 amended — the date and time types on XSD's partial order (dc90de61)

#### Fixed

- **sparql**: a grouped SELECT expression may read an earlier alias (e97abdaf)
- **evaluation**: the materialised arm through blank nodes, and sort keys once per row (09700b05)

### Milestone 5a — XSD datatypes, the SPARQL algebra and parser

#### Added

- **xsd**: Varve.Xsd — the numeric types, boolean and string (fd23462f)
- **xsd**: the date and time family on the seven-property model (0c76b62b)
- **xsd**: durations, and duration arithmetic on dateTime and date (a3ee090f)
- **store**: the canonical checks move to Varve.Xsd (14e49f16)
- **rdf**: cardinality estimates and an inline-value accessor on the quad source (c4623191)
- **sparql**: the algebra and the rewriter (84f2d064)
- **sparql**: the query parser (f5e39d03)
- **sparql**: the update parser (ffafa6a4)
- **sparql**: the serialiser and the round-trip property (099439d4)
- **conformance**: the SPARQL syntax suites, ratcheted at their own version (7cd5ae36)

#### Changed

- **adr**: 0048–0052, the milestone 5 positions as decisions (cd3de767)
- **spec**: xsd.md — value spaces, mappings and the operator semantics (f9570905)
- **xsd**: the properties that gate the package, and the XSD 1.1 examples (6d5da09a)
- **spec**: the SPARQL grammar and algebra specifications (bcba772c)
- **sparql**: the allocation assertion, and the parser under Native AOT and in the browser (f7adf8aa)
- **sparql**: parse throughput against dotNetRDF (354039f7)
- roadmap, README, changelog, the traceability record (7ac3794d)
- **sparql**: the allocation assertion survives a trimmed array pool (7d43295a)

#### Fixed

- **store**: a merged run stays an exact delta (f013709c)

### Milestone 4 — the in-memory log and the default quad projection

#### Added

- **rdf**: QuadDelta and QuadOverlay — the delta and the overlay at layer 1 (38766d7c)
- **store**: Varve.Store — the log, the sequencer and the default projection (7042ad7e)
- **repo-standard**: export, plan, apply and check (4f874663)
- **repo-standard**: the Action, and a replayed GitHub to test it against (9a26de48)

#### Changed

- **adr**: 0040–0045, what milestone 4 decides that set zero did not (2a373984)
- **spec**: version 1.2 — settings reach every subscriber, and three clarifications (646e439d)
- **adr**: 0034 amended — a branch-bound cloud session lands through a pull request (80e612aa)
- **adr**: 0038, upstream contribution to Oxigraph and the shared suites (85262107)
- ambient clock and randomness banned where log bytes are compared (c9f1b7e5)
- **store**: the reference model, and the property that ties §10 together (6771c6a4)
- **adr**: 0039, repo-standard, and the declaration it reads (863aa7aa)
- **repo-standard**: recorded exchanges, the round trip, and a live test (3872227c)
- repo-standard's build, tests, native binary and Action (3efd2762)
- keep this repository's settings to a declaration, once there is one (c630dc8b)
- **traceability**: the repo-standard session, and the state it leaves (1494885a)
- **store**: I7 for every position, the failed state, subscriptions, and the rules one by one (94dbe65c)
- **store**: allocation — zero per quad on a scan, the six key arrays on an index update (9febaf11)
- the store under Native AOT and in the browser (9930a37a)
- **store**: commit and scan throughput, and index size, against ADR 0012 (cba409a2)
- milestone 4 — the state, the roadmap, and the revisit conditions (eaaf6657)
- **traceability**: milestone 4 — the prompt, the model, and the report (7ff34f7d)
- **spec**: version 1.3 — delta composition over chains, and I3 over triple terms (ccffd86c)
- **roadmap**: the maintainer's positions for milestone 5, recorded until they are ADRs (88123e3c)
- **changelog**: regenerated with specification 1.3 and the milestone 5 positions (4abff1ae)
- the first repo-standard declaration for this repository (38fe4c05)
- **adr**: 0032 amended — the gate runs after the push; ruleset 1 blocks nothing (997900e0)
- **repo-standard**: trunk's strict mode off, and the session record (b078c8e3)
- **repo-standard**: turn Discussions on (111c726b)

#### Fixed

- **store**: a cut inside a segment's preamble no longer bricks the next open (14502be9)
- **repo-standard**: hash the downloaded binary from stdin (d83fd30f)
- **repo-standard**: end each line as the wrapped writer does (5a7a2c9b)

### Stewardship — the Mind Over Machine standard

#### Added

- **eng**: the issue-reference gate, and the register's Dependabot amendment (cd0217e7)
- **eng**: eng/ci.cs, the devcontainer, and the same code in both (64f2f0d5)

#### Changed

- **licence**: relicense to MPL-2.0, enforced per file (93df7d50)
- **adr**: trunk-based development, traceability, signing, SemVer, containers, auth (a66552a1)
- OpenSSF Scorecard, every action pinned by peeled SHA (0dcdd681)
- README, CONTRIBUTING, GOVERNANCE, SECURITY, conduct, templates, AGENTS.md (bbd42fef)
- **changelog**: CHANGELOG.md generated from the conventional commits (ee3fa700)
- **traceability**: the backfill, the topic summaries, and this session (b9f99b72)
- **traceability**: what the first direct push to main proved (bb2ed840)

#### Fixed

- **devcontainer**: the workload install needs elevation, and CI found it (81138cfc)
- **eng**: writing about a closing keyword is still a closing keyword (1cfad663)

### Milestone 3b — Turtle and TriG

#### Added

- **turtle**: the Turtle and TriG reader (94034ae0)
- **turtle**: the Turtle and TriG writer (7ca28046)
- **turtle**: blank node naming is idempotent, and the oracle is a rule (8dcc3d70)
- **conformance**: evaluation tests and dataset isomorphism (ebc43ff6)
- **turtle**: four grammar gaps closed; the suites pass 883 of 883 (d409d9a7)
- **turtle**: TurtleReader, and the suite reads every case both ways (f54ebeca)

#### Changed

- **spec**: the Turtle and TriG grammars (36282b10)
- **adr**: 0030 — Turtle's recovery unit, prefixes, and isomorphism (81a07a3b)
- **conformance**: a chunk-boundary oracle, and what it found (dcc1ad7a)
- **conformance**: the harness reads its manifests with Varve.Turtle (f66944f0)
- **turtle**: zero bytes per quad, and Turtle under AOT and in a browser (f4a7b0ed)
- **turtle**: property tests over generated documents; ADR 0028 condition 1 (eefa775b)
- **turtle**: the numbers, and the trap in reading them per quad (f361b171)
- the counts the milestone moved, and the claim the pull reader owed (46bcc3bd)

#### Fixed

- **turtle**: no RDF 1.2 syntax is accepted, and that is now checked (0a66031a)
- **turtle**: CR LF split between its two bytes is one line, not two (7e4589e1)

### Milestone 3a close-out

#### Added

- **rdf**: the graph position takes a pattern, not a wildcard handle (1020ac34)

#### Changed

- nothing reaches main except through a pull request (8a97f85c)
- **wasm**: probe FixedTimeEquals, and read the table in two halves (766bec25)
- **adr**: 0028 — a deterministic AEAD from HMAC-SHA-256, superseding 0020 (4522f739)
- **conformance**: gate the RDF 1.2 syntax we shipped, and fix what it caught (eda7ba15)
- term equality is lexical, and stays that way (06555148)
- make ADR 0009's native-asset scope a gate (702b7773)
- package metadata, an embedded icon, and MinVer (360df8ed)
- publish on a v* tag, and pack as a dry run on every pull request (a12e60bc)
- reconcile CLAUDE.md, CONTRIBUTING.md and the roadmap with the close-out (944e8182)
- the repository check fails closed, and says why (c65c4d0f)

### Milestone 3a — RDF model, IRI, N-Triples and N-Quads

#### Added

- **iri**: Varve.Iri — RFC 3987 validation and RFC 3986 resolution (5f52f48c)
- **rdf**: Varve.Rdf — the term model and the quad source contract (28a50f4c)
- **turtle**: Varve.Turtle — the N-Triples and N-Quads reader and writer (4c851aea)

#### Changed

- specification version 1, and the ADRs brought in line (2f2d4824)
- erasure mode is milestone 9, after SHACL (fa71ea86)
- **research**: the Varve log is already the write-ahead log (0c3690c5)
- drop the email address from NOTICE (36789a58)
- **spec**: IRIs, the RDF model, and the N-Triples and N-Quads grammars (7ec6e4bc)
- **adr**: 0024 RDF term representation (a71ef76e)
- **adr**: 0025 CsCheck, 0026 the HotPath attribute, 0027 benchmarking (4a3b8db3)
- **conformance**: the adapter, the full baseline, and ratchet exemptions (9316bc01)
- zero allocation per quad, and the off-the-shelf analyzers proven (676eae16)
- Native AOT and browser smokes — and ADR 0020 fails its condition (b0b2138e)
- benchmarks against dotNetRDF, with the machine stated (3625edf1)
- reconcile CLAUDE.md, the roadmap and the spec index with milestone 3a (66288352)

### Milestone 2 — ADR set zero, the log and projection model

#### Changed

- **spec**: the log and projection model, draft 2 (191e0a05)
- **adr**: 0010 commit model and effective deltas (b6ac2f07)
- **adr**: 0011 concurrency, one sequencer with optional expected position (4151df4d)
- **adr**: 0012 term dictionary and id scheme (Proposed, options only) (03df6dae)
- **adr**: 0013 records versus commits, and bulk load (d0066bd2)
- **adr**: 0014 header chain and divergence detection (51ebae72)
- **adr**: 0015 checkpoints, pinned reads, as-of reads, archive horizon (8e2b6540)
- **adr**: 0016 projection contract, synchronous default, erasure in projections (67f51708)
- **adr**: 0017 pre-commit validator contract and the overlay quad source (693801f1)
- **adr**: 0018 storage abstraction (Proposed, options only) (b6dbef8b)
- **adr**: 0019 erasure by crypto-shredding (6320d289)
- **adr**: 0020 cipher for erasure mode (Proposed, options only) (aae515d2)
- **research**: managed storage engines (9a2377e8)
- reconcile the roadmap, CLAUDE.md and both indexes with ADR set zero (922cb653)

### Milestone 1 — Foundation

#### Added

- **analyzers**: VARVE0001 layer direction and VARVE0002 layer declaration (f529283a)
- **analyzers**: close the VARVE0002 loophole (8c2946a6)

#### Changed

- add the project brief verbatim (16e35151)
- repository skeleton (d52bad67)
- ADR set for milestone 1, and the Apache-2.0 licence (9cd6b425)
- repository-wide build infrastructure (46409cb3)
- **analyzers**: unit tests for both rules, and a real-build fixture (6fab948b)
- **conformance**: W3C N-Triples and N-Quads suites, wired and failing (bafb2ade)
- build and conformance jobs, actions pinned by commit (752080f7)
- CLAUDE.md (fed90e26)
- pin the analyzer's Roslyn to the 10.0.100 floor, not the latest (605c5fa8)
- add NOTICE and close the copyright open question (0387bf11)
- dependency policy as an enforced register (5e6f03a8)

[Unreleased]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.17...HEAD
[0.1.0-preview.17]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.15...v0.1.0-preview.17
[0.1.0-preview.15]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.14...v0.1.0-preview.15
[0.1.0-preview.14]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.13...v0.1.0-preview.14
[0.1.0-preview.13]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.12...v0.1.0-preview.13
[0.1.0-preview.12]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.11...v0.1.0-preview.12
[0.1.0-preview.11]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.10...v0.1.0-preview.11
[0.1.0-preview.10]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.9...v0.1.0-preview.10
[0.1.0-preview.9]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.8...v0.1.0-preview.9
[0.1.0-preview.8]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.7...v0.1.0-preview.8
[0.1.0-preview.7]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.6...v0.1.0-preview.7
[0.1.0-preview.6]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.5...v0.1.0-preview.6
[0.1.0-preview.5]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.4...v0.1.0-preview.5
[0.1.0-preview.4]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.3...v0.1.0-preview.4
[0.1.0-preview.3]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.2...v0.1.0-preview.3
[0.1.0-preview.2]: https://github.com/Hafeok/Varve/compare/v0.1.0-preview.1...v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/Hafeok/Varve/releases/tag/v0.1.0-preview.1
