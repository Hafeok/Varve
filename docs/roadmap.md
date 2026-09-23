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

## 3 — RDF model, IRI, XSD datatypes, N-Triples and N-Quads

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

## 5 — The SPARQL parser and algebra, then the evaluator

The evaluator runs over the in-memory projection through the abstract quad
source contract.

**Positions the maintainer took at the end of milestone 4**, to be written as
ADRs when milestone 5 starts:

- **The optimiser's output is algebra**, and the optimiser and the evaluator
  share one layer 3 package. That closes ADR 0003's open question 2.
- **`IQuadSource` gains a cardinality estimate** that may return *unknown*.
- **A typed-value accessor beside the handle** for inline numerics, benchmarked
  with and without it against the SPARQL evaluation suite. This is the
  measurement ADR 0022's revisit condition asks for.
- **`Varve.Xsd` owns canonical lexical forms**, and replaces the store's
  private canonical-integer check.
- **A pinned read lives for one query execution**, owned by the caller, with a
  configurable maximum lifetime.

Turtle and TriG were planned for this milestone and shipped in 3b instead,
which is why ADR 0007's exit criterion — the conformance harness reading its
own manifests without dotNetRDF — is already met.

## 6 — Durable managed storage backend

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

## 7 — Server and CLI

SPARQL 1.1 Protocol, Graph Store Protocol, service description, federation, and
the endpoints for the event-sourced features.

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

## Operability — what a server needs before anyone runs it

Between 7 and 8, and new with the adoption of the Mind Over Machine stewardship
standard. Milestone 7 produces a server that answers queries; it does not
produce a server anyone should operate. Every item here is a consequence of the
server existing, and none of them should wait for a validator.

- **Query and resource governance** — timeouts, result-size limits, memory
  ceilings per request, and a defined answer for what happens when one is hit.
  On an event-sourced store an as-of read can touch an unbounded amount of log,
  so this is not a nicety.
- **OpenTelemetry** — traces, metrics and logs, to the semantic conventions for
  a database server.
- **Health and readiness**, which are different questions here: the process
  being up is not the dataset being open and the projections having caught up.
- **Graceful shutdown** — in-flight requests drained, the sequencer quiesced,
  the lease file in `derived/` released. A lease left behind by a killed process
  is an operator's problem on the next start.
- **Configuration** — ADR 0037's surface, plus dataset paths and limits, from
  file, environment and command line.
- **Container image** for the server.
- **CLI as a `dotnet tool`**.

## 8 — SHACL

Standalone with a W3C suite pass, then commit-time gating, then the incremental
validation projection.

## 9 — Erasure mode

Crypto-shredding: the key store, classifier and selector contracts, private
terms, `T4 Erase`, access requests by key id, and the classification gate
(ADRs 0020, 0021, 0023).

**After SHACL, not before the server.** The shape-derived classifier and the
classification gate both depend on the validator, so erasure cannot be honestly
finished before milestone 8 — a classifier with no shapes to derive from can
only be hand-written, and the gate that stops unclassified personal data
reaching the log is validator policy. Most datasets will also never turn
erasure mode on, which is the second reason it does not belong earlier.

Milestone 6 reserves what a format change would otherwise cost: see there.

Due here: **Q4** (how a shredded term appears in SPARQL results and
serialisations), **Q5** (lookup by private value — scan and decrypt, or a blind
index that weakens I10), **Q8** (key granularity when one term is about two data
subjects), and **Q9** (whether the surviving structure counts as anonymous).

**Q9 is a legal question and Q4 has a legal edge.** The engineering answer to Q9
is the classifier's ability to make identifying links private; whether that is
enough is not an engineering answer at all.

**Q6 is decided by ADR 0028**, after ADR 0020's browser verification failed at
milestone 3a. `Aes.Create()` throws `PlatformNotSupportedException` on
browser-wasm under .NET 10, and `AesGcm`, `AesCcm` and `ChaCha20Poly1305` all
report `IsSupported == false` — no symmetric cipher of any kind runs in a
browser. `RandomNumberGenerator`, SHA-256, HMAC-SHA-256, HKDF and
`FixedTimeEquals` do, so 0028 builds a deterministic AEAD from HMAC-SHA-256
alone in an SIV composition, synchronous on all three hosts. It carries a second
condition that no build can settle: **external cryptographic review before
milestone 9 ships**, because it is a custom instantiation of a standard
composition rather than RFC 5297. If the review rejects it, the fallback is that
erasure mode does not run in the browser, in its own ADR. Nothing before
milestone 9 depends on any of this.

## 1.0 definition

Under strict SemVer ([ADR 0035](adr/0035-semantic-versioning.md)) the major
version is a promise about compatibility, so what it promises is decided before
it is made rather than at the moment of making it. 1.0 is reached when all of
the following are true.

- **Failure-injection and soak tests for the file backend.** Torn writes, full
  disks, killed processes mid-commit, and a soak run long enough to be evidence
  rather than a demonstration. ADR 0014's header chain exists to detect
  divergence; 1.0 is where that claim is tested by causing it.
- **Format versioning with a read-forever commitment.** Every format version
  Varve has ever written stays readable by every later version. The version
  discriminator is due at milestone 6 (ADR 0014) precisely so that this promise
  is possible to make.
- **Public API freeze.** `PublicAPI.Shipped.txt` becomes the contract, and the
  baseline diff is the evidence for what is a breaking change (ADR 0035).
- **Operator guide**, including the complete Entra example — app registration,
  app roles, managed identity — and a generic OIDC issuer example (ADR 0037).
- **Signed multi-arch container images with an SBOM.**
- **Package-manager distribution for the CLI**: `winget` and Homebrew, beyond
  the `dotnet tool` the Operability milestone delivers.
- **Docs site with Mermaid rendering.** The specifications already carry
  diagrams that only render as source today.

### Excluded from 1.0, deliberately

- **Erasure mode ships as preview only.** ADR 0028's second condition — external
  cryptographic review of a custom instantiation of a standard composition —
  governs whether it ships at all, and a feature whose cryptography is under
  review is not a feature a major version should promise.
- **No replication.**
- **No branching or merging of datasets.** Milestone 2 was required not to
  block it. That is not the same as building it, and conflating the two is how
  a 1.0 acquires a feature nobody designed.

## Not scheduled

Reasoning, GeoSPARQL and full-text are out of scope until the core passes
conformance. Branching and merging of datasets is a later possibility that
milestone 2 must not block.

Whole-graph coupling metrics (instability, `I = Ce / (Ca + Ce)`) cannot be an
analyzer, because an analyzer sees one compilation at a time. If we want them
they are a CI report, never a gate.
