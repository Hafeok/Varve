# Roadmap

Milestones are from `docs/brief.md`. This file adds only the deferred items and
the point at which each becomes due.

Each milestone is tracked by an issue, and every commit references one
(ADR 0033). The **Varve roadmap** project board carries them as cards in this
order; the **Varve work** board carries only what is next. `GOVERNANCE.md`
describes the flow between the two.

| Milestone | Issue | | Milestone | Issue |
|---|---|---|---|---|
| 1 — Foundation | [#4](https://github.com/Hafeok/Varve/issues/4) | | 6 — Durable storage | [#10](https://github.com/Hafeok/Varve/issues/10) |
| 2 — ADR set zero | [#5](https://github.com/Hafeok/Varve/issues/5) | | 7 — Server and CLI | [#11](https://github.com/Hafeok/Varve/issues/11) |
| 3a — Model and line syntaxes | [#6](https://github.com/Hafeok/Varve/issues/6) | | Operability | [#12](https://github.com/Hafeok/Varve/issues/12) |
| 3b — Turtle and TriG | [#7](https://github.com/Hafeok/Varve/issues/7) | | 8 — SHACL | [#13](https://github.com/Hafeok/Varve/issues/13) |
| 4 — In-memory log | [#8](https://github.com/Hafeok/Varve/issues/8) | | 9 — Erasure mode | [#14](https://github.com/Hafeok/Varve/issues/14) |
| 5 — SPARQL | [#9](https://github.com/Hafeok/Varve/issues/9) | | 1.0 definition | [#15](https://github.com/Hafeok/Varve/issues/15) |
| Commit path | [#91](https://github.com/Hafeok/Varve/issues/91) | | Object storage | [#92](https://github.com/Hafeok/Varve/issues/92) |
| Client and integration | [#93](https://github.com/Hafeok/Varve/issues/93) | | Learning track | [#94](https://github.com/Hafeok/Varve/issues/94) |
| Inference | [#95](https://github.com/Hafeok/Varve/issues/95) | | Web UI | [#96](https://github.com/Hafeok/Varve/issues/96) |
| Replication | [#97](https://github.com/Hafeok/Varve/issues/97) | | Management plane | [#105](https://github.com/Hafeok/Varve/issues/105) |

## 1 — Foundation *(complete)*

Repository layout, licence, NuGet prefix reservation for `Varve.*`, build
infrastructure with the off-the-shelf analyzers at error severity,
`Varve.Analyzers` with the layer rule, CI with the W3C manifests wired in and
failing.

Delivered: everything above except the NuGet prefix reservation, which is a
manual request to nuget.org and cannot be automated. It is not on the critical
path until milestone 3 produces the first packable project.

## 2 — ADR set zero *(complete)*

`docs/spec/log-and-projection-model.md` is the functional specification and the
authority for `Varve.Store` behaviour. ADRs [0010–0020](adr/README.md) record the
decisions it presupposes.

### The brief's tensions, and where each stands

| Tension | Resolution |
|---|---|
| Log growth, compaction, and what time travel survives it | **Resolved** — ADR 0015. There is no compaction in the destructive sense; checkpoints give bounded read cost and the log is retained. |
| Blank node identity across transactions and across time | **Resolved in shape** — ADR 0012: store-scoped identity allocated at commit, request labels request-scoped. The external form is **Q1**: in process, by handle (ADR 0044); across protocols, at milestone 7. |
| Retraction of a non-existent quad; re-assertion | **Resolved** — ADR 0010. Neither event nor error; the log records the effective delta. |
| SPARQL Update semantics and one request to one commit | **Resolved** — ADR 0005 places Update at layer 5; it evaluates `WHERE` against a pinned position and submits the delta as one commit (ADRs 0010, 0011). |
| Bulk load as one logical commit | **Resolved** — ADR 0013: a commit is one or more records, closed by a flag in the log. Remaining: **Q2**, **Q3**. |
| Single writer versus optimistic concurrency | **Resolved** — ADR 0011: both. One sequencer, optional expected position per request. |
| Managed storage engine for the projections | **Open** — `docs/research/managed-storage-engines.md` narrows it; ADR 0018 states the contract requirements. Decided at milestone 6. |
| GDPR-style hard deletion in an append-only model | **Resolved** — ADR 0023: crypto-shredding, opt-in per dataset, access by key id; ADR 0028: the cipher, after 0020 failed its browser condition. 0028 is conditional on an external cryptographic review before milestone 9 ships. Remaining: **Q4**, **Q5**, **Q8**, **Q9**. |
| Commit-time validation cost versus write latency, and what a validator may read | **Resolved** — ADR 0017: the overlay of the pending delta on the pinned state, and nothing else; the hook is inside the sequencer, so validation is write latency by construction. |
| Incremental SHACL — which shapes are incrementally maintainable | **Open** — not addressed by this set. Due milestone 8. |

The pre-commit validator hook is specified here even though no validator exists
until milestone 8, and the key store, classifier and selector contracts are
specified here even though erasure is not implemented until the milestone
proposed below.

## 3 — RDF model, IRI, XSD datatypes, N-Triples and N-Quads *(complete)*

**3a** *(complete)*: `Varve.Iri`, `Varve.Rdf`, and N-Triples and N-Quads in
`Varve.Turtle`, to a full suite pass. **3b** *(complete)*: Turtle and TriG,
reader and writer, to a full suite pass — 883 cases, no exemptions. `Varve.Xsd`
and RDFC-1.0 canonicalisation come later — neither is needed for any syntax
suite.

Two things 3b delivered that were not asked for, and are worth keeping:

- **The chunk-boundary oracle.** Parse every manifest input whole, then again
  split at each byte offset, and require the same answer. It found nine defect
  classes in the Turtle reader — two of which produced *wrong quads rather than
  errors*, and one of which only the Windows run could see — and it is now a
  standing rule for every syntax package
  (`docs/testing.md` §2), enforced by a guard rather than remembered.
- **ADR 0007's exit criterion, met two milestones early.** The conformance
  harness reads its own manifests with `Varve.Turtle`; `dotNetRdf.Core` remains
  only in the benchmark project, and a test rather than a note keeps it out of
  the harness.

**`Varve.Xsd` brings value equality to the evaluator, not to the term model.**
Literal term equality is character by character over lexical form, datatype IRI
and language tag (RDF 1.1 Concepts §3.3) and stays that way permanently; value
comparison is SPARQL 1.1 §17.3 and §17.4.1.7, and lives at layer 3. An earlier
draft of this file said `Varve.Xsd` would "unblock value equality in the term
model", which would have been a change to what a graph contains rather than to
what a query answers. See ADRs 0022 and 0024.

The first packable projects, and therefore the first time several milestone 1
mechanisms stopped being inert. All of the following are **delivered**:

- **Public API baselines.** Every public member of the three packages is a line
  in a `PublicAPI.Unshipped.txt` beside its project, added by hand. A fixture
  in `tests/fixtures/public-api/` proves `RS0016` fires on a member that is
  not.
- **Banned symbols.** A fixture in `tests/fixtures/banned-api/` proves `RS0030`
  fires on `System.Uri` and that the message names ADR 0004. **The `System.Uri`
  entry is not narrowed** — it stays repository-wide until layer 5 exists.
- **Native AOT smoke build.** `tests/Varve.AotSmoke` publishes with
  `PublishAot` and `IlcTreatWarningsAsErrors`, and CI **runs** the binary on
  ubuntu and windows rather than only publishing it: the interesting AOT
  failures are at run time and silent.
- **WASM smoke build.** `tests/Varve.WasmSmoke`, a `browser-wasm` app on
  `wasm-experimental` — **not** Blazor, which would add an ASP.NET Core package
  tail unrelated to the claim. CI publishes it warning-free; it was run in
  headless Chromium here, and **what it found superseded ADR 0020** — see 0028
  and the Q6 note below.
- **Conformance is green.** All 157 cases of `rdf/rdf11/rdf-n-triples` and
  `rdf/rdf11/rdf-n-quads` pass, and the close-out added `rdf/rdf12`'s
  N-Triples and N-Quads **syntax** suites — 29 and 27 more — because the reader
  and writer had shipped RDF 1.2's base direction and triple terms with no
  suite behind them. That made 213; 3b's Turtle and TriG suites took it to
  **883**, still with `baseline/exemptions.txt` empty. The ratchet gained an exemptions
  mechanism: an exempt case is neither required to pass nor reported as newly
  passing, and an exemption with no written justification fails the run.
  Gating 1.2 found three real bugs, two of which were wrong under 1.1 as well —
  see the PR for `fix/3a-closeout`.
- **The first packages have metadata and a publish workflow** (ADR 0029),
  versioned from the git tag by MinVer, published through trusted publishing on
  a `v*` tag, and packed as a dry run on every pull request. **Nothing is
  published yet**; the first tag is `v0.1.0-preview.1`.
- **The native-asset ban is a gate** rather than a sentence (`eng/native-assets.cs`),
  now that ADR 0009's amendment has scoped it to shipped artifacts.

Also delivered, beyond what this section asked for: **zero bytes allocated per
quad**, asserted on all five entry points as the difference between a
500-quad and a 4,000-quad parse rather than as an absolute figure; and
benchmarks against dotNetRDF with the machine stated
(`tests/Varve.Benchmarks/README.md`).

**Two findings about the RDF 1.1 N-Triples specification** are recorded in
`docs/spec/n-triples.md`: its `PN_CHARS_U` production contradicts its own test
suite over the colon, and RDF 1.2 has since resolved it the way the suite
already assumed. The reader and writer also carry RDF 1.2's base direction and
triple terms, which the model held from the start and the syntax would
otherwise be unable to express.

**Not in 3a, and not attempted:** `Varve.Xsd`, canonicalisation, Turtle and
TriG, the store, the first NuGet publication itself, and `VARVE0006` (the
`[HotPath]` rule — the attribute exists and is applied, the analyzer does not).

**RDF 1.2 Turtle and TriG are deliberately later, and nothing of them is
accepted.** RDF 1.2 Turtle is a W3C Working Draft of 14 September 2026 and TriG
of 15 September 2026, and implementing reifiers, annotations, triple terms and
a version directive against a draft that recent, under a ratchet, is churn a
milestone should absorb rather than a session.

Their suites are 167 cases, and this is what wiring them would mean today:

| Suite | Cases | |
|---|---:|---|
| `rdf12/rdf-turtle/syntax` | 74 | 41 positive, 33 negative |
| `rdf12/rdf-turtle/eval` | 32 | |
| `rdf12/rdf-trig/syntax` | 35 | 24 positive, 11 negative |
| `rdf12/rdf-trig/eval` | 26 | |
| **total** | **167** | **123 positive** |

Almost every positive case exercises a construct that is not implemented, so
wiring them now would file roughly a hundred exemptions — which is not gating a
feature, it is recording that it is absent in a file nobody reads twice.

**What is enforced instead is that none of it half-works.** The reader rejects
triple terms, reifiers, annotations and both spellings of the version
directive, and `Rdf12NotInTurtleTests` is the closed door: each construct has a
case asserting the rejection, in Turtle and in TriG. Adding RDF 1.2 Turtle
therefore means wiring its suites, not discovering that part of it already
parsed.

One construct **was** half-working and is now closed: the reader accepted
`"x"@en--ltr` and the writer emitted it, with no suite behind either. A base
direction is RDF 1.2's `LANG_DIR` [154s]; `"en--ltr"` is not an RDF 1.1
`LANGTAG` [144s], which wants each subtag after a `-` to be alphanumeric. The
Turtle reader now rejects it and the Turtle writer refuses a literal carrying
one rather than emitting a document its own reader would reject. The **term
model still carries base direction**, and N-Triples and N-Quads still read and
write it, where the `rdf12` syntax suites gate it — 29 and 27 cases, both
passing.

## 4 — In-memory log and default quad projection *(complete)*

As-of reads, and the property test that a rebuilt projection equals an
incrementally maintained one.

Delivered: `Varve.Store` at layer 4 — the log in a provisional encoding
(ADR 0045), the sequencer, the dictionary with its three allocated classes and
inline values, the default projection as immutable sorted runs (ADR 0041),
pinned and as-of reads, `Diff`, checkpoints, settings, subscriptions (ADR 0042),
the projection contract and the failed state — over `MemoryStorage` in the same
package (ADR 0040). In `Varve.Rdf`, the overlay and the delta. Every §10
property that does not concern erasure or the file backend runs against a
reference model (ADR 0043); a scan allocates zero bytes per quad; AOT and the
browser run the store. The specification moved to **1.2** (ADR 0046).

Found on the way, and reported rather than patched: **§6's claim that deltas
form a monoid is false** for arbitrary deltas and true over the exact chains a
log holds, with a counterexample as its witness; and I3 cannot hold literally
over RDF 1.2 triple terms. The maintainer accepted both as specification 1.3
(ADR 0047). And the records property found a real recovery bug on its first
run: a crash inside a new segment's preamble made the dataset unopenable after
one more commit. Fixed, and kept as a named test.

Due here: **Q1** (blank node identity at the API boundary, ADR 0012), and the
banned-symbols entry for ambient clock and randomness under `Varve.Store` that
§10's determinism test depends on (ADR 0011). Q1 was **split** by ADR 0044: the
in-process form is decided here, and the protocol form moves to milestone 7.

## 5 — The SPARQL parser and algebra, then the evaluator *(complete)*

The evaluator runs over the in-memory projection through the abstract quad
source contract. Too large for one session, so it is three slices, each
landing on the trunk before the next starts:

| Slice | Delivers | Gate |
|---|---|---|
| **5a** *(complete)* | The five ADRs below, `Varve.Xsd`, the SPARQL algebra, parser and serialiser (`Varve.Sparql`, layer 2), cardinality estimates and the typed-value accessor on `IQuadSource` | The W3C SPARQL 1.0, 1.1 and 1.2 syntax suites, query and update, in the ratchet — **554 of 554, no exemption** |
| **5b** *(complete)* | ADRs 0053–0056; the optimiser and evaluator (`Varve.Sparql.Evaluation`, layer 3) over the in-memory projection and over `InMemoryDataset`; **the result-format readers** (`Varve.Sparql.Results`, layer 2), brought forward from 5c because the suite's expected results come in those formats; ADR 0022's benchmark per ADR 0050 | The SPARQL 1.0 and 1.1 query evaluation suites and the loadable SPARQL 1.2 ones, over two subjects — **544 cases, 1,088 of 1,088, no exemption**; 41 blocked on 6b |
| **5c** *(complete)* | ADRs 0057–0059; the result-format **writers**; `Varve.Sparql.Store`, the SPARQL Update integration package at layer 5 — one request, one commit — with dataset validators and a staging view in the store; RDFC-1.0 canonicalisation in `Varve.Rdf`, now the harness's dataset comparison | The update evaluation suites — **94 of 94**, each one commit or none, asserted; `rdf-canon` — **86 of 86**; the results-format cases written — **10 of 10**; no exemption, ratchet **2,721** |

**The positions the maintainer took at the end of milestone 4 are ADRs**, all
accepted at the start of 5a:

- [0048](adr/0048-optimiser-and-evaluator-one-package-algebra-in-algebra-out.md) —
  the optimiser's output is algebra, and the optimiser and evaluator share one
  layer 3 package. Closes ADR 0003's open question 2, by amendment there.
- [0049](adr/0049-cardinality-estimates-on-the-quad-source.md) — `IQuadSource`
  gains a cardinality estimate that may be exact, estimated or unknown.
- [0050](adr/0050-typed-value-accessor-and-the-benchmark-for-adr-0022.md) — a
  typed-value accessor beside the handle for inline values, and the three-arm
  benchmark with a verdict rule fixed before the numbers exist, run in 5b.
- [0051](adr/0051-varve-xsd-scope-and-precision.md) — `Varve.Xsd`'s scope,
  precision policy (`Int128` fixed-point decimal, `Int64` integer), canonical
  forms owned there and called from the store, and the implicit-timezone
  total order for dateTime comparison, verified in 5b.
- [0052](adr/0052-pinned-read-lifetime.md) — a pinned read lives for one query
  execution, owned by the caller, with a maximum lifetime the server enforces
  at milestone 7.

**5a landed** with `docs/spec/xsd.md`, `docs/spec/sparql-algebra.md` and
`docs/spec/sparql-grammar.md` as the specifications; `Varve.Xsd` gated by its
property tests and the XSD 1.1 examples until 5b's evaluation suite can gate
it; the parser holding the round-trip identity over generated algebra and the
whole corpus, allocating the tree and nothing else, and running under Native
AOT and in the browser. What 5b inherits is in the traceability record
(`docs/traceability/2026-09-24-issue-9-milestone-5a-xsd-and-sparql-algebra.md`):
the aggregate extraction and the sequence-path rewrite the algebra leaves to
the evaluator, ADR 0050's three-arm benchmark, and ADR 0051's dateTime order
to verify.

**5b landed** with `docs/spec/sparql-evaluation.md` and
`docs/spec/sparql-results.md`, ADRs 0053–0056, and every evaluation case that
is not blocked passing over `InMemoryDataset` and over the store, in all
three of ADR 0050's arms. No dateTime case disagrees with ADR 0051; `xsd:date`
and the other seven-property types take XSD's partial order, as the suite
requires. ADR 0022's revisit condition was judged and does not fire. The
optimiser holds its equivalence property, the store answers as a dataset of
its own quads at any position, and AOT and the browser evaluate queries. What
5c inherits is in the traceability record
(`docs/traceability/2026-09-25-issue-9-milestone-5b-evaluation.md`).

**5c landed** with `docs/spec/sparql-update-store.md` and `docs/spec/rdf-canon.md`,
the writers in `docs/spec/sparql-results.md`, and ADRs 0057 (update as one
commit), 0058 (dataset validators and the staging view) and 0059 (RDFC-1.0 in
`Varve.Rdf`, bounded by a work limit; supersedes ADR 0030's deletion clause).
No update case needed an exemption for empty graphs. The flaky allocation
test (#32) was a garbage collection and tier-1 escape analysis moving
readings, now taken inside a no-GC region. RDFC-1.0 was found to give some
isomorphic datasets with blank graph names different canonical forms
(`rdf-canon.md` §6). What milestone 6 needs decided before it starts is in the
traceability record
(`docs/traceability/2026-09-25-issue-9-milestone-5c-update-and-canonicalisation.md`).

Turtle and TriG were planned for this milestone and shipped in 3b instead,
which is why ADR 0007's exit criterion — the conformance harness reading its
own manifests without dotNetRDF — is already met.

## 6 — Durable managed storage backend *(complete)*

**Written under the `DD` and `VARVE` rules from its first line** (#43, ADRs
0062–0069): each new contract is a `[Contract]` citing its decision, each new
log value is a wrapper in `Varve.Store.Log`, and each backend's write, read and
replay inner loops are `[HotPath]` before they are benchmarked.

Bulk loader against the decisions from milestone 2. Not a compaction strategy:
ADR 0015 decides there is none in the destructive sense, and a storage engine
that merges and discards superseded records is right for `derived/` and wrong
for `log/`. `docs/research/managed-storage-engines.md` is the note that informs
the choice, and it argues the two halves should be decided separately.

**A risk carried from milestone 4: `IQuadSource` is synchronous and the storage
contract is asynchronous.** ADR 0018 made storage asynchronous throughout,
because the browser needs it; ADR 0022's quad source walks a cursor with a
synchronous `MoveNext`. In memory the two never meet — a checkpoint is a slice
already in hand (ADR 0041). On the file and browser backends, a scan over a
checkpoint that does not fit in memory needs pages the cursor cannot await.
Milestone 6 has to choose: page the checkpoint in before the scan starts, give
the file backend a synchronous read path beside the asynchronous one, or make
the cursor asynchronous — which is a superseding change to ADR 0022 and reaches
the evaluator.

Due here: **Q2** and **Q3** (bulk load against I2, and an overlay that does not
fit in memory — ADR 0013), and the version discriminator in the storage format
that ADR 0014 requires from the first byte written.

**The first durable format reserves erasure's shape even though erasure is not
built until milestone 9**, because retrofitting any of it would be a format
change: the **private id class** (ADR 0012), the **private entry layout** —
`(KeyId, ciphertext)` covering the whole term encoding — and the file backend's
**refusal of a key store path inside the dataset directory** (ADR 0023). None of
them costs anything while erasure mode is off.

### 6a — the file backend, format version 1, failure injection *(ADRs 0070–0077, specification 1.5)*

Issue [#10](https://github.com/Hafeok/Varve/issues/10). **Delivered:**

- **`FileStorage`**: a dataset directory of plain files, `log/` and
  `derived/`, durability `Synchronised` (ADR 0073), one process at a time by an
  exclusive handle (ADR 0075), and a key store refused inside the directory by
  path (ADR 0074).
- **Format version 1** (ADR 0072, `docs/spec/storage-format.md`), read for
  ever from the first prerelease that writes it: self-hashing headers, a
  sealed trailer, chunked bodies, commit headers carrying the dictionary's
  counters; recovery walks the chain across segments and refuses only what no
  crash or copy produces. Opening reads bodies only after the newest
  checkpoint.
- **The storage engine is our own** (ADR 0070): the default projection's runs
  on disk, read in blocks through a synchronous blob read (ADR 0071), flushed
  and merged by maintenance off the sequencer.
- **The risk above is closed for the desktop** by ADR 0071: derived data is read
  synchronously; writes stay asynchronous; the cursor and ADR 0052 are
  unchanged.
- **Q2 and Q3 decided** (ADRs 0076, 0077); the format carries what a bulk
  commit needs.
- **The failure-injection gate**: process crashes at every operation and every
  byte of the log's writes, power loss with writes torn and reordered up to the
  flush barrier, lost directory entries, crashes during recovery, `derived/`
  deleted or stale, and copies taken mid-write. It found one defect, fixed: a
  seal's trailer could outlive the records before it.

## 6b — RDF 1.2 Turtle and TriG, RDF/XML, JSON-LD *(complete)*

Issue [#10](https://github.com/Hafeok/Varve/issues/10), ADRs 0121–0123, filed
unaccepted. Added at milestone 5b, when the SPARQL 1.2 evaluation suites
turned out to need it: 41 of their cases loaded their data from RDF 1.2
Turtle or TriG, which `turtle.md` §9 refused. **Delivered:**

- **RDF 1.2 Turtle and TriG** (ADR 0121): reified triples, triple terms,
  annotations, reifiers, both spellings of the version directive and
  directional language tags, read and written; `turtle.md` §9 rewritten; the
  four `rdf12` suites pass whole; the 41 blocked cases run over all four
  subjects, 37 passing everywhere and the other four exempt over the store,
  its scope and the protocol, with Oxigraph's agreement, until the store
  answers `IQuadSource.TryGetTripleTermComponents` (ADR 0044's blank node
  identity), which is [#87](https://github.com/Hafeok/Varve/issues/87), due
  before milestone 8.
  The one contradiction between the editions, a surrogate pair as two
  escapes, is an `RdfVersion` option.
- **`Varve.RdfXml`** (ADR 0122, `docs/spec/rdf-xml.md`): RDF/XML over
  `System.Xml`, both suites whole with no exemption, the cost of `XmlReader`
  measured and stated; `tests/fixtures/w3c-rdfxml/` deleted and the harness
  reads the originals, closing ADR 0027's note.
- **`Varve.JsonLd`** (ADR 0123, `docs/spec/json-ld.md`): JSON-LD 1.1 over
  `Utf8JsonReader` as a tree in an arena — expand, toRdf and fromRdf, the
  three suites whole under a stated rule for the 1.0-only cases; the
  direction is RDF 1.2's by default. Compaction, flattening and framing are
  out, recorded.
- **Over HTTP** (ADR 0092, amended 2026-10-12): `Varve.Protocol` negotiates
  graphs in RDF/XML and JSON-LD beside the four line syntaxes, reads them as
  Graph Store bodies and names them in the service description; a graph the
  chosen format cannot spell is a `406` that says why. Landed after
  milestone Operability, as the session landing second.
- AOT and the browser read and write all three; the ratchet holds 5,507
  lines.

## 6c — the second storage session *(ADRs 0078–0084)*

Called 6b in the 6a brief; renumbered by the maintainer on the 6a pull
request, since 6b is the RDF 1.2 syntaxes above. Issues
[#10](https://github.com/Hafeok/Varve/issues/10) and
[#61](https://github.com/Hafeok/Varve/issues/61). **Delivered:**

- **The soak's working set, separated** (#61): the causes measured one at a
  time — checkpoints built in memory; as-of reads replaying the log from far
  back; fences rebuilt on the large object heap at every merge; and twelve
  kept checkpoints each holding the dataset's fences — and fixed: checkpoints
  and merges stream from the runs, a **checkpoint policy** (ADR 0078) bounds
  as-of distance, and a reader holds directories in chunks and a
  checkpoint's sparsely (ADR 0080). The soak gate is a band (ADR 0082): the
  hour's working set, 153 → 200 MB with peaks to 218 MB where 6a's reached
  2.8 GB, holds the ±25% band and misses the 10% drift (+20.6%), the
  collector's headroom over a commit table of 144 bytes a commit. The commit
  table then became derived, paged state (ADR 0089): the drift holds
  (+1.1%), and the band is missed by two samples of a hundred, excursions of
  the collector's committed memory, which a 128 MB heap limit removes. #61
  stays open.
- **The dictionary on disk** (ADR 0079): carried by the runs, read by id and by
  term through the synchronous blob read; opening loads none.
- **Derived format 2**: keys compressed in their blocks (ADR 0080), answering
  6a's compression question.
- **The bulk loader** (ADRs 0076, 0077, 0081): any parser's quads, sorted
  outside memory within a stated bound, merged with the dataset in one
  sequential pass, one commit; validators read the delta on disk; crash-tested
  at every operation; gated at a hundred million quads.
- **Replica bootstrap** by copying files (ADR 0083).
- **The browser backend** (ADR 0084): `Varve.Store.Browser`, OPFS through
  synchronous access handles in a worker — ADR 0071's revisit condition is
  not met — and IndexedDB elsewhere, tested in headless Chromium, with
  `log/` byte-identical to the desktop's. Maintenance stays off by default.

Left for later: archive (T3), which lets a replica leave the log's prefix
behind; a per-run filter if cold term lookups ever dominate commits (ADR
0079's revisit condition); and a bulk loader that resolves terms on several
threads.

## 7 — Server and CLI *(complete)*

SPARQL 1.1 Protocol, Graph Store Protocol, service description, federation, and
the endpoints for the event-sourced features.

**Split in two.**

- **7a** (ADRs 0091–0101): `Varve.Protocol` and `Varve.Server`;
  - the SPARQL 1.1 Protocol, the Graph Store Protocol and the service
    description;
  - time travel over HTTP, the change feed and the diff;
  - authentication tested in three layers;
  - the server's configuration, Native AOT, graceful shutdown and readiness.
- **7b** (ADRs 0103–0109): `Varve.Protocol.Client`, `SERVICE` and `LOAD`
  over HTTP, the `varve` command line, the admin API, graph-level
  authorisation, the bulk loader on worker threads and per-run term filters
  (derived format 3), the operator's guide. The release `0.1.0-preview.2`
  follows 7b, not 7a.

Q1's protocol half is **closed by ADR 0098** (spec 1.6): stable labels out,
fresh labels in, no skolem IRIs. The paragraph below is kept as the record of
what was due.

**Q1's protocol half is due here** (ADR 0044): the skolem IRI scheme by which a
store blank node crosses a protocol boundary, decided with the server that is
its first consumer.

Layer 5 exists from here, which makes the `System.Uri` ban in
`eng/BannedSymbols.txt` due for narrowing (ADR 0004): a server speaks HTTP and
needs transport addresses that are not RDF IRIs.

**Authentication and authorisation arrive with the server**, decided ahead of
the milestone by [ADR 0037](adr/0037-server-authentication.md). OIDC bearer
tokens validated with `JwtBearer` from the shared framework — no
`Microsoft.Identity.Web`, no MSAL, and therefore no new register entry.
Authorisation is claim-based and maps to three permissions per dataset,
`read`, `write` and `admin`. Anonymous mode exists for development, must be
enabled explicitly, and warns on every start. The commit agent is the caller's
stable subject identifier from the token, so provenance survives the identity
provider.

`Varve.Store` never receives a principal, a claim or a token: all of this is
layer 5, and the layer rule already prevents the alternative.

**API keys are rejected permanently**, in any form, and ADR 0037 states that it
is not to be superseded by a decision that adds one. A caller that cannot obtain
an OIDC token is not a supported caller.

## Operability *(complete)* — what a server needs before anyone runs it

Between 7 and 8, and new with the adoption of the Mind Over Machine stewardship
standard. Milestone 7 produces a server that answers queries; it does not
produce a server anyone should operate. Every item here is a consequence of the
server existing, and none of them should wait for a validator. Closed by
[#12](https://github.com/Hafeok/Varve/issues/12) with ADRs 0110–0120 and the
release `v0.1.0-preview.3`.

- **Query and resource governance** — timeouts, result-size limits, memory
  ceilings per request, and a defined answer for what happens when one is hit.
  On an event-sourced store an as-of read can touch an unbounded amount of log,
  so this is not a nicety. *Done at 7a* (ADR 0095: `Varve:Limits`, every read
  pinned and bounded, cut with an error trailer).
- **OpenTelemetry** — traces, metrics and logs, to the semantic conventions for
  a database server. *Done at Operability* (ADR 0112: the BCL instruments in
  the libraries, the server exports over OTLP when an `OTEL_*` endpoint is
  set; `docs/operator/observe.md`).
- **Health and readiness**, which are different questions here: the process
  being up is not the dataset being open and the projections having caught up.
  *Done at 7a*, moved under `/health/` with a readiness property, a lag
  tolerance and a drain at Operability (ADR 0113).
- **Graceful shutdown** — in-flight requests drained, the sequencer quiesced,
  the lease file in `derived/` released. A lease left behind by a killed process
  is an operator's problem on the next start. *Done at 7a* (ADR 0101); the
  lease waited for, never broken, and `varve lease` at Operability (ADR 0116).
- **Configuration** — ADR 0037's surface, plus dataset paths and limits, from
  file, environment and command line. *Done at 7a and 7b*; precedence
  stated, `--print-config`, unknown keys refused at Operability (ADR 0115;
  `docs/operator/configure.md`).
- **Container image** for the server. *Done at Operability* (ADR 0111:
  `ghcr.io/hafeok/varve`, Native AOT per architecture on the chiseled
  `runtime-deps` base, non-root and read-only, the suites and the auth legs
  run against it in CI, provenance and an SBOM attested on release; Docker
  Hub a mirror on demand, never a second source of truth).
- **Resource governance, the rest** — a memory ceiling per query by counting
  rows, a bound on as-of distance, live tails per client, concurrent reads
  with a queue, each with its problem type. *Done at Operability* (ADR 0114).
- **The shipped runtime configuration** — workstation concurrent GC with
  `ConserveMemory` 5, and the soak gate judged under it, its drift against
  minutes 30–40. *Done at Operability* (ADR 0110, superseding ADR 0082 in
  part); the gate itself does not yet hold over two hours (+20.3%), and
  [#61](https://github.com/Hafeok/Varve/issues/61) stays open.
- **`Varve.Aspire`** — `AddVarve`, `WithOidc`, `WithDataset`; a sample
  AppHost tested in CI. *Done at Operability* (ADR 0117).
- **API alignment while everything is preview** — state, settings and commits
  as resources, idempotent `PUT`; headers by read kind and the problem
  catalogue. *Done at Operability* (ADRs 0118, 0119).
- **CLI as a `dotnet tool`**. *Done at 7b* (ADR 0105: `Varve.Server` is the
  tool, command `varve`).
- **The operator's guide** — `docs/operator/`: run, configure, authenticate,
  back up and restore, upgrade. *Written at 7b*; observe, the container, the
  systemd unit, the full Entra and generic-issuer examples, the limits and
  the stale lease at Operability.

## From preview.4 to 1.0 and after

Milestones 1 to 7 and Operability above stay as they are, marked complete by
their release descriptors. Every milestone below is one issue, one or more PRs,
and one release descriptor naming that issue (ADR 0102); a milestone is
complete when its descriptor is cut, and not before.

### At a glance

Release numbers are not fixed in advance: each milestone takes the next free
`0.1.0-preview.N` when its descriptor is cut (ADR 0102), so the order of
landing decides the number. Only released and in-flight numbers are shown.

| # | Milestone | Release | Depends on | Status |
|---|---|---|---|---|
| 6b | RDF 1.2 Turtle/TriG, RDF/XML, JSON-LD | `0.1.0-preview.4` | — | released |
| — | **Commit path**: group commit, per-graph concurrency | `preview.5` | 6b landed | released |
| — | **Client and integration**, part 1: token provider, on-behalf-of, `Varve.Delta`, follow, setup, CORS | `preview.6` | Commit path | issue #103, PR #102 (landed) |
| — | **Object storage**: the log and the projections on any object store, Azure first | next free | Commit path | issue #92 (built; PR pending) |
| — | **Extent maintenance**: background merges and extent deletion | next free | Object storage | issue #110 |
| — | **Client and integration**, part 2: events, catalogue, schema registry, AsyncAPI | next free | part 1 | issue #93 |
| — | **Management plane**, part 1: fleet system dataset, grants as code, `varve apply`, forwarding | `preview.10` | Commit path, Object storage, Client and integration | issue #131 (part 1 landed) |
| — | **Management plane**, part 2: event schemas in the management plane | next free | part 1 | issue #105 |
| — | **Learning track**: Varve for SQL developers and DDD practitioners | next free | Client and integration, Management plane | issue #94 |
| 8 | SHACL: validator, commit-time gating, incremental validation | next free | — (#87 landed) | issue #13 |
| — | **Inference**: RDFS by rewriting, OWL 2 RL and rules as a projection | next free | 8 | issue #95 |
| — | **Web UI**: operator console, then data explorer | next free | Management plane (console); 8 and Inference (explorer) | issue #96 |
| — | **Replication**: three tiers, handover, promotion, fleet-level active-active | next free | Object storage, Extent maintenance, Commit path, Management plane | issue #97 |
| 9 | Erasure mode and row-level access | next free | 8, Replication, Management plane | issue #14 |
| 1.0 | The definition in #15, amended to include replication, inference, the management plane and extent maintenance | `1.0.0` | all above | issue #15 |
| — | **Continuous queries**: standing SPARQL queries as projections, result deltas as events | a minor after 1.0 | 1.0 (Inference, Management plane, Client and integration) | issue #106 |
| — | **Entity export**: pseudonymised entity histories as replayable fixtures | a minor after 1.0 | 1.0 (Management plane, Client and integration, Erasure) | issue #107 |

Object storage and Client and integration can run in parallel; they share
only `deploy/azure/` and `Varve.Aspire`, and the one that lands second merges
`main` and owns reconciling both. SHACL depends on nothing still open (#87 has landed) and can run in
parallel with everything from Object storage to the Learning track; the
chapter 3 SHACL section is written by whichever of SHACL and the Learning
track lands second (maintainer, 2026-10-15).

Standing issues that ride along with whichever milestone touches their area:
#61 (soak with a bounded vocabulary, decides ADR 0082's "dataset's own"),
#35 (`INSERT DATA` throughput, open after Commit path), #89 (called-workflow permissions gate), #88 (signing hook on resume), the four
upstream reports #36–#39.

---

## Commit path *(complete)* — group commit and per-graph concurrency

**Why first.** Both change the sequencer, which is the one component whose
correctness everything else assumes; they are reviewed together and apart
from anything that only adds surface. Object storage needs group commit
(an object write is milliseconds), and the client milestone exposes graph
versions.

**Scope.** Group commit for every backend: requests arriving while a write is
in flight are normalised in order and written as one batch; each keeps its
own commit record and position. Per-graph optimistic concurrency: an expected
position gains a scope of graphs; "no commit in (p, head] touched any graph
in S", today's check being S = all graphs; a graph-version index in
`derived/`; Graph Store `ETag` and `If-Match` per graph;
`Varve-If-Graphs-Match` for SPARQL Update; inferred scope from what a
request read and wrote.

**Gate.** A batched history equals the unbatched one commit for commit;
serialisability per scope over generated concurrent histories (disjoint
scopes never conflict, overlapping ones always do when they should); the
eight-writer benchmark with one graph per writer at zero conflicts; the write
benchmark before and after on the file backend; every existing property and
the protocol suites unchanged.

## Object storage *(complete)* — the log and the projections on any object store, Azure first

**Why.** Container Apps has no block disk, and Azure Files breaks the file
backend's lease, `fsync` and memory-mapped reads. This is the deployment story
for Azure, and with a provider-neutral design, for any object store. And
storage must scale past one machine's disk: following Azure Data Explorer's
extents (ADX boundary ADR, T7), the projections live in the store as
immutable segments and local disk is only a cache.

**Scope.** `Varve.Store.ObjectStorage` over a five-operation object-store
contract (create-if-absent, replace-if-unchanged, ranged read, list, delete);
`Varve.Store.Azure` as the first adapter; `Varve.Store.S3` against RustFS in CI. The
log as write-once chunk objects, consolidated on seal with the log's bytes
unchanged; a single writer by an epoch-fenced manifest, no leases. The
default projection and the dictionary lookups as immutable, sorted,
compressed segments in the store, each over a position range with per-segment
statistics; a projection catalog swapped only by the writer under the epoch
fence; background merges; local disk as a segment cache whose eviction never
changes a result; cold start as catalog plus lazy attach plus tail; the same
segment format on the file backend. The REC portfolio generator (REC
reference domain ADR) with sizes S, M and L as the stated datasets for this
and later benchmarks. The projection's immutable objects are called extents; "segment" is the
log segment only. By the cut accepted on 2026-10-15, background merges and
the extent deletion rule move to Extent maintenance (#110); the cache, lazy
attach and follower attach stay here, and the operator guide states a
checkpoint budget until merges land. The epoch lives in a writer record
beside the write-once `log/MANIFEST`; the fence is a log slot. The
S3-compatible server in CI is RustFS, which passed the contract suite
(MinIO's community images are no longer published).
Identity-only credentials. `deploy/azure/` Bicep for Container Apps and
`Varve.Aspire`'s Azure variant.

**Gate.** The storage contract suite on Azurite and RustFS (real account on an
optional leg); every milestone 4 and 6a property on the new backend; failure
injection at every request (errors, throttling, lost responses, a superseded
writer, a killed process, a bad checkpoint, a lost segment write or catalog
swap); a segment-backed projection equals the in-memory one at every position
across merges and any eviction schedule; a follower attaching the writer's
catalog serves the writer's results; determinism across backends;
directory-to-container migration; cold-start time and request count measured
and documented at REC sizes S, M and L.

**Small self-hosted, documented and measured.** A Raspberry Pi 5 (arm64,
8 GB) as a first-class target: the file backend on an NVMe SSD (not an SD
card, whose `fsync` and write endurance do not suit a log), and the same Pi
against an S3-compatible server on a NAS (where it supports web identity) with the segment cache local. The operator guide gains
the setup, the runtime limits for 8 GB, and measured commit latency, query
throughput and cold start on the device; `linux-arm64` stays a CI target.

## Extent maintenance — background merges and extent deletion

**Why.** Object storage lands extents without merges. Every read merges
across every extent set, and the count grows by one per checkpoint, so
read cost grows without bound. Added by the maintainer on 2026-10-15, in
1.0.

**Scope.** Background merges (new extents, then one catalog swap under
the epoch fence), a stated merge policy with no elapsed-time trigger, and
the deletion rule: an extent is deleted only when no catalog version a
live reader may hold still names it. Followers keep serving across merges.

**Gate.** A merged projection equals the in-memory one at every position
across any merge and eviction schedule; failure injection at every extent
write, catalog swap and deletion; a superseded writer's swap refused; read
latency at REC M after 10, 50 and 100 checkpoints against Object storage's
table.

## Client and integration *(complete)* — on-behalf-of, setup, follow, events

**Why.** The server is ready to be used by applications; this is what an
application installs. It also makes the commits resource a contract other
systems can integrate against, in RDF and in plain JSON.

**Scope.** In two releases (maintainer, 2026-10-13). Part 1 (#103): a
per-request token provider in `Varve.Protocol.Client` and the on-behalf-of
chain in the Entra guide, Bicep and a sample, with the attribution rule
stated; `DatasetSetup`, migrations recorded as data, `AddVarve` and
`varve setup apply`; `FollowAsync` with a checkpoint; `Varve.Delta` and the
delta format under the read-forever rule; CORS in the server. Part 2 (#93):
domain events as quads in an events graph, rendered as CloudEvents JSON
through declared `SELECT` mappings, with an event catalogue, a JSON types
list, AsyncAPI 3.0, a server-wide schema registry with versioned schemas and
refusal of breaking changes; the Event Hubs bridge sample.

**Gate.** Setup idempotence and partial failure; at-least-once resumption from
every position; byte-identical event rendering on replay and rendering with
the declaration in force at each position; catalogue, list and AsyncAPI agree
at every position and respect grants; every breaking schema change refused;
the on-behalf-of sample through the test issuer in CI.

**Reconciled by Management plane.** `DatasetSetup` keeps data-shaped work
(seed data, data migrations, the setup graph); declarations such as settings,
bindings and event schemas move to the management plane. `varve apply` is
the management plane's desired-state convergence; setup's command is
`varve setup apply`, so the name is never reused with a different meaning.

## Management plane — fleet system dataset, grants as code, `varve apply`

**Why.** Grants and settings must be declared in the application's
repository, reviewed, applied by CI and audited, and a fleet of servers
sharing one store must enforce the same grants. Today grants live in host
configuration and configuration shares the data plane's API. Added by the
maintainer on 2026-10-13, per fleet from the start.

**Scope.** Management routes under `/manage/` with a separate token audience
and the operator or dataset-admin role; the dataset state and settings routes
move there. A system dataset per fleet on the fleet's store, one writer, the
other servers tier-1 followers; any instance accepts a write and forwards it
to the writer of its dataset (ADR 0168, which superseded the `307` of the
first draft). A read-only tier-1 follower that Replication builds on, and
placement of each dataset's writer changed by events. Grants evaluated at the
server's applied management position, stated on every response, with
`Varve-Min-Management-Position` and `503 management-behind`; each commit
records the position that authorised it. TriG declarations in the `varve:`
vocabulary, one graph per dataset plus a fleet graph, principals as agent
IRIs, roles or groups, graph sets as IRIs or caller templates.
`varve apply --plan` and `apply` as an RDFC-1.0 diff committed under
`Varve-If-Graphs-Match`, drift checked in CI with workload identity
federation. Bootstrap operators in host configuration, recorded on first
write. Embedded hosts have no management plane. In two releases: part 1
(#131, `preview.10`, landed) is everything above (ADRs 0163–0166, 0168),
with event schemas still in the catalogue graphs and refused in management
graphs; part 2 moves them to the management plane (ADR 0167) and closes the
milestone.

**Gate.** `apply` idempotent; stale saved plans refused; disjoint applies
never conflict; over generated grant histories, no response at position p
exposes a graph the grants at p forbid, the minimum-position header is
honoured, and as-of grant queries equal the grants in force; each plane
refuses the other's tokens; a bootstrap mismatch fails readiness; the data
plane keeps serving with the system writer killed; a two-server fleet runs
the suite on Azurite and RustFS.

## Learning track — Varve for SQL developers and DDD practitioners

**Why.** Most users think in SQL; the difficulty is modelling habits, not
syntax. And Varve's fit with domain-driven design is its strongest argument
and needs to be shown, not claimed.

**Scope.** Eleven chapters in `docs/learn/`, each with a runnable sample in
`samples/learn/` that CI executes and whose output the page quotes, all on
one RealEstateCore portfolio (REC reference domain ADR), starting from a
hand-written slice and growing: rows as resources, links and patterns,
schema without migrations, commits, deletion and history, temporal and
audit, CDC and the outbox (with sensor telemetry shown going to an
Eventhouse, not to Varve), multi-tenancy and row-level security (tenants and
their collections, with grants declared as code and applied by
`varve apply`), named graphs, from embedded to server, and Varve and DDD
(Topology and Leasing built state-first, Operations built events-first). An
appendix of SQL habits that hurt here.

**Gate.** Every sample runs in CI and its output matches the page; the
maintainer has read chapters 1 and 2 as a first reader and the draft changed
accordingly; chapter 5 carries a marker updated by milestone 9, and chapter
3's SHACL section is written here if milestone 8 has landed first, otherwise
a marker that milestone 8 replaces.

## 8 — SHACL

As issue #13 states: the standalone validator to the W3C SHACL suite, then
commit-time gating through the pre-commit validator, then incremental
validation as a projection. Its one dependency, #87, has landed. The gate adds the pinned
REC 4 shapes validating generated portfolios standalone, at commit time and
incrementally; the session reports which SHACL features REC uses. Close-out
adds: chapter 3's SHACL section if the learning track has landed first;
event declarations may be validated by shapes; management declarations are
validated by shapes shipped with Varve instead of by code.

## Inference — RDFS by rewriting, OWL 2 RL and rules as a projection

**Why after SHACL.** It reuses SHACL's incremental-maintenance engine and its
rule language (`sh:rule`), and consistency checking uses the same pre-commit
hook SHACL gating uses. The reference design is GraphDB's, with the
differences below.

**Scope.** Ontologies stay ordinary data in a TBox graph, versioned and
queryable as of any position. Three tiers matched to the OWL 2 profiles:
(1) query-time rewriting for RDFS and OWL 2 QL, selected through the SPARQL
1.1 entailment regimes and advertised as `sd:entailmentRegime`, default
none; (2) OWL 2 RL and custom rules (SPARQL `CONSTRUCT`, SHACL `sh:rule`)
materialised as a projection: never written to the log, incrementally
maintained from exact deltas (semi-naive for assertions, DRed or support
counting for retractions), rebuilt in the background on a TBox change scoped
to the affected predicates, lag visible on readiness, each entailment
carrying its rule and premises so "why is this here" is a query; (3) OWL 2 RL
inconsistency rules as a pre-commit validator. Borrowed from GraphDB:
`varve:explicit` and `varve:implicit` views so a query can ask for asserted
or inferred statements only, and `owl:sameAs` as equivalence classes with a
representative rather than full materialisation. Different from GraphDB on
purpose: the log stays asserted-only (writes do not pay for the closure; the
closure is rebuildable and readable as of any position), and rules are
standards (OWL 2 RL, SPARQL, SHACL), not a proprietary rule language. Out of
scope: OWL 2 DL and EL, which belong to external reasoners fed from the
commits stream. TBox bindings are management declarations.

**The decision that needs an ADR first.** Inference against graph-level
grants: an entailment whose premises span graphs must not reveal its
conclusion to a caller who cannot read every premise graph. Default:
inference within a graph, with the TBox graph readable by all; opt-in:
cross-graph entailments visible only to callers who can read every premise
graph, checked against the recorded premises.

**Gate.** The RDFS and OWL 2 RL entailment test cases (W3C OWL 2 test suite,
RL profile; SPARQL 1.1 entailment regime tests where applicable) under the
ratchet; incrementally maintained closure equals a from-scratch closure at
every position of generated histories with retractions and TBox changes;
as-of reads of entailments equal the closure computed at that position; the
grant property: no response to a caller exposes an entailment whose premises
it cannot all read; the `sameAs` closure bounded on a generated entity-
resolution dataset; tier 1 rewriting over the REC and Brick class
hierarchies on generated portfolios.

**Close-out.** The learning track's chapter 12 ("Ontologies and inference") replaces its placeholder; `docs/why.md`'s inference row becomes shipped.

## Web UI

A Blazor WebAssembly client of the public API, with no endpoints of its own (#96): the operator console first (after Management plane, whose resources it
reads), the data explorer after SHACL and Inference, so validation reports
and entailment provenance are in it. Every screen reads a resource the API
already serves.

## Replication — before 1.0

Issue #97 and the writer handover ADR hold the detail. In short: tier 1 followers on the same store (the follower itself built by Management
plane), tier 2 a region away by the store's own geo-replication, tier 3 log
shipping between any two stores or a directory. Promotion across copies is
an operator's act; writer handover within a store may be automatic,
triggered by abdication or probe failure, never by time. Promotion is
rehearsed in the gate with the old primary returning and the header chain
refusing the divergence; handover is tested over generated schedules of
triggers, partitions and concurrent candidates. Active-active at the fleet
level by dataset, never two writers on one dataset. The takeover request
targets the management route. Moved into 1.0 by the maintainer on
2026-10-11: a 1.0 meant to replace a team's data stores must answer machine
loss with more than restoring a copy. The 1.0 definition's "No replication"
exclusion is removed by an amendment to #15 and the roadmap's 1.0 section.

## 9 — Erasure mode and row-level access

As issue #14 states, together with row-level access through the erasure
classifier. Designed with replicas in place: only ciphertext is replicated
and the key store is outside every dataset, so destroying a key erases on
every replica at once; the gate proves it on a tier-1 and a tier-3 follower.
Key store configuration and classifier bindings are management declarations.
Gated on ADR 0028's external cryptographic review. Close-out: the learning
track's chapter 5 gains its erasure section.

## 1.0

What has to be true before the major version, under strict SemVer (ADR 0035). Issue #15 is the authority and its amendments are dated there; this section mirrors it.

#### Must be true

- [ ] **Failure-injection and soak tests for the file backend** — torn writes, full disks, killed processes, and a soak run long enough to be evidence
- [ ] **Format versioning with a read-forever commitment** — every format version Varve has ever written stays readable
- [ ] **Public API freeze** — `PublicAPI.Shipped.txt` becomes the contract; the baselines are the evidence for what is a breaking change (ADR 0035)
- [ ] **Operator guide** — including the complete Entra example and a generic OIDC issuer example (ADR 0037)
- [x] **Signed multi-arch container images with an SBOM** — `ghcr.io/hafeok/varve`, linux/amd64 and linux/arm64, build-provenance attestation and SPDX SBOM on the manifest digest (`publish.yml`), shipped in `v0.1.0-preview.3`
- [ ] **Package-manager distribution for the CLI** — `winget` and Homebrew, beyond the `dotnet tool` from the Operability milestone
- [ ] **Docs site with Mermaid rendering**
- [ ] **Replication** — tiers 1 to 3, writer handover within a store tested over generated schedules, and promotion across copies rehearsed (#97)
- [ ] **Inference** — tier 1 query-time rewriting (RDFS, OWL 2 QL), tier 2 OWL 2 RL and custom rules as a projection, tier 3 inconsistency as a pre-commit gate, all under the grant property; the OWL 2 RL and RDFS entailment suites under the ratchet (#95)
- [ ] **Public API freeze covers `Varve.Protocol.Client`** (#93; its first part is #103)
- [ ] **The delta format under the read-forever rule** (#103, `Varve.Delta`, `delta-format: 1` in the descriptor)
- [ ] **The CloudEvents rendering under the read-forever rule** (#93, second part)
- [ ] **The learning track complete for every shipped feature** (#94)
- [ ] **The soak gate holds under the shipped configuration** (#61)
- [ ] **Extent maintenance** — background merges and the extent deletion rule, so read cost stays bounded under a checkpoint policy (#110)
- [ ] **Management plane** — grants and settings as code in a per-fleet system dataset, `varve apply`, the fleet grant properties (#105)
- [ ] **Public API freeze covers the management API**, and the `varve:` management vocabulary is stable (#105)

#### Excluded from 1.0, deliberately

- **Erasure mode ships as preview only.** ADR 0028's external review governs whether it ships at all.
- **No OWL 2 DL or EL reasoning, and no inference across datasets.** External reasoners are fed from the commits stream.
- **No multiple writers on one dataset.** Active-active is by dataset across a fleet (#97).
- **No branching or merging of datasets.** Milestone 2 was required not to block it; that is not the same as building it.
- **Continuous queries (#106) and entity export (#107) are after 1.0.** They do not amend this definition.

What the milestones above added to it (replication moved in on 2026-10-11, inference on 2026-10-12, the management plane on 2026-10-13, extent maintenance on 2026-10-15, all by the maintainer; #15 is amended to match): the public API
freeze covers `Varve.Protocol.Client` and the management API; the delta format and the CloudEvents
rendering are under the read-forever rule; the `varve:` management vocabulary is stable; the learning track is complete
for every shipped feature; the soak gate holds under the shipped
configuration (#61 closed); replication tiers 1 to 3 with handover tested and promotion rehearsed; extent maintenance with read cost bounded under a checkpoint policy; inference tiers 1 to 3 with the grant property; grants as code with the fleet grant properties; erasure ships as preview (unchanged).

---

## After 1.0

Milestones here are planned but outside the 1.0 definition in #15. They do not change it.

### Continuous queries — standing SPARQL queries as projections

**Why.** The architectural thesis promises continuous SPARQL queries and materialised views, and no milestone up to 1.0 delivers them. ADX and Eventhouse show what teams expect of a real-time store:
- views that stay fresh;
- dashboards that update;
- alerts when a condition starts to hold.

Inference already builds the engine this needs: maintenance from exact deltas, semi-naive evaluation for assertions, DRed or support counting for retractions, background rebuild and lag on readiness. The ADX boundary ADR records what is copied from ADX and what is not.

**Why after 1.0.** It adds surface and does not answer a "can a team trust it as their store" question. It reuses three things that must be stable first:
- Inference's maintenance engine;
- Management plane declarations;
- Client and integration's event rendering.

**Scope.**
- **Declarations.** A standing query is a management declaration: a SPARQL `SELECT` or `CONSTRUCT` bound to a dataset, applied by `varve apply`.
- **Maintainability is classified when the query is declared.**
  - Incremental: basic graph patterns, joins, `FILTER`, `BIND`, `VALUES`, `COUNT`/`SUM`/`AVG` under retraction, and `MIN`/`MAX`/`DISTINCT` with support counts.
  - Scoped re-evaluation: `OPTIONAL`, `MINUS`, `NOT EXISTS`, property paths, keyed by the affected bindings.
  - Refused: queries with `NOW()`, `RAND()`, `UUID()` or `STRUUID()`, `SERVICE`, and `LIMIT`/`OFFSET` without `ORDER BY`.
  - A refusal names the construct and returns `422`.
- **Windows.** Time windows are written against the timestamp of the commit at the evaluated position, through a `varve:` function. The result is then a function of the position.
- **Results.**
  - A projection readable as of any position: materialised at its applied position q, plus the log delta (q, p] for a read at p. A result is exact at its stated position or refused.
  - `CONSTRUCT` results are also readable as a reserved named graph from SPARQL.
- **Deltas.**
  - For each commit that changes a result, an event carries the position and the solutions or triples added and removed.
  - Delivered over SSE and as CloudEvents through the existing rendering.
  - Resumable from a checkpoint, at least once.
  - The delta format is under the read-forever rule.
  - Alerting is consuming these events. Acting on them stays outside Varve.
- **Grants.**
  - A caller can read a result only if the grants at its applied management position let it read every graph the query reads, the premise rule from Inference.
  - Queries over caller-template graph sets are refused in this milestone.
- **Inference.** A standing query can read explicit, implicit or both, through the `varve:explicit` and `varve:implicit` views.
- **Operations.**
  - Backfill on declaration, and rebuild on a query change.
  - Lag per standing query on readiness.
  - A per-dataset limit on standing queries, enforced at apply time.

**Gate.**
- At every position of generated histories with retractions, the incrementally maintained result equals a from-scratch evaluation. This covers every incremental and scoped-re-evaluation construct.
- As-of reads of a result equal the query evaluated at that position.
- Concatenating the delta stream from q to p equals the difference between the results at q and p, and resumption from every position yields it again.
- Every refused construct is refused at apply time, `NOW()` included.
- The grant property holds over generated grant histories.
- A query change rebuilds without serving a mixed result.
- Commit-to-delta latency and the write overhead per standing query are measured with BenchmarkDotNet on the file backend and on Azurite, over REC portfolios S and M.
- The CloudEvents rendering of deltas is byte-identical on replay.

**Close-out.**
- The learning track gains a chapter: from a SQL indexed view and an ADX materialised view to a standing query.
- `docs/why.md`'s continuous-queries row becomes shipped.

### Entity export — pseudonymised entity histories as replayable fixtures

**Why.** Application failures come from specific accumulated state. Varve holds that exact history. One entity's history, exported from production and pseudonymised, replays as a regression test. A team then keeps a library of its failures and edge cases in its own Git repository, instead of copying production datasets into test environments. Independent of Continuous queries; the order may swap.

**Scope.**
- **Boundary.** The entity is a declared boundary around an IRI: CBD by default, or a per-class `CONSTRUCT` template or SHACL node shape. Over a range, it is the union of the boundary at every position.
- **Fixture.**
  - a manifest;
  - the state as of p₀;
  - the boundary-restricted commits in (p₀, p₁] in delta format v1, with timestamp, cause and original position;
  - the entity's events as CloudEvents;
  - RDFC-1.0 labels, and byte-deterministic output.
- **Redaction is mandatory.**
  - Declarations choose keep, pseudonymise (keyed HMAC, per-library key through managed identity), generalise or drop, per predicate or per erasure class.
  - Agents are always pseudonymised.
  - There is no export without a declaration.
- **Grants and audit.** Grants are checked at every position of the range. Each export is recorded as a management act.
- **Rejected writes.** Write problem responses state the position they were evaluated against, so a refused write can become a fixture together with the request the application supplies. Varve stores no rejected bodies.
- **Replay.** A test package replays a fixture step by step in the embedded host; `varve import` replays into a server.

**Gate.**
- Deterministic export.
- Replay equals the boundary-restricted as-of state at every step.
- No redacted term in any byte of the fixture.
- Stable pseudonyms per key.
- The grant property at every position.
- Refusal without a declaration.
- Every export recorded.
- Stale fixtures flagged.
- A sample application's CI replays a library of REC building and room fixtures.

**Close-out.** The learning track gains "Turning production failures into tests".

## Not scheduled

GeoSPARQL and full-text are out of scope until the core passes conformance;
reasoning is the Inference milestone above. Branching and merging of datasets is a later possibility that
milestone 2 must not block.

Whole-graph coupling metrics (instability, `I = Ce / (Ca + Ce)`) cannot be an
analyzer, because an analyzer sees one compilation at a time. If we want them
they are a CI report, never a gate.
