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

### Milestone 4 — the in-memory log and the default quad projection

#### Added

- **rdf**: QuadDelta and QuadOverlay — the delta and the overlay at layer 1 (38766d7c)
- **store**: Varve.Store — the log, the sequencer and the default projection (7042ad7e)

#### Changed

- **adr**: 0040–0045, what milestone 4 decides that set zero did not (2a373984)
- **spec**: version 1.2 — settings reach every subscriber, and three clarifications (646e439d)
- **adr**: 0034 amended — a branch-bound cloud session lands through a pull request (80e612aa)
- **adr**: 0038, upstream contribution to Oxigraph and the shared suites (85262107)
- ambient clock and randomness banned where log bytes are compared (c9f1b7e5)
- **store**: the reference model, and the property that ties §10 together (6771c6a4)
- **store**: I7 for every position, the failed state, subscriptions, and the rules one by one (94dbe65c)
- **store**: allocation — zero per quad on a scan, the six key arrays on an index update (9febaf11)
- the store under Native AOT and in the browser (9930a37a)
- **store**: commit and scan throughput, and index size, against ADR 0012 (cba409a2)
- milestone 4 — the state, the roadmap, and the revisit conditions (eaaf6657)
- **traceability**: milestone 4 — the prompt, the model, and the report (7ff34f7d)
- **spec**: version 1.3 — delta composition over chains, and I3 over triple terms (ccffd86c)
- **roadmap**: the maintainer's positions for milestone 5, recorded until they are ADRs (88123e3c)

#### Fixed

- **store**: a cut inside a segment's preamble no longer bricks the next open (14502be9)

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

