# Changelog

Generated from the conventional commits by `dotnet run eng/changelog.cs`.
Do not edit by hand — the commits are the record and this is a view of them.

The format is [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
([ADR 0035](docs/adr/0035-semantic-versioning.md)).

## [Unreleased]

**Nothing has been released.** No `v*` tag exists and no package has been
published to nuget.org, so every change below is unreleased and the sections
are milestones rather than versions. The first tag is `v0.1.0-preview.1`
([ADR 0029](docs/adr/0029-publishing-and-versioning.md)).

### Adopting DecisionDriven.Analyzers — the decision ledger, the DD rules, Varve's own rules only

#### Changed

- **adr**: 0062–0067, adopting DecisionDriven.Analyzers (17852ee3)
- **eng**: decision-sets, the front-matter check for docs/decisions (b364d25d)
- **traceability**: issue 43, adoption session 1 (d8b0cadb)

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

