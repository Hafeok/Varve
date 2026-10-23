# Architecture decisions

Format, numbering and the supersession rule are in
[0001](0001-record-architecture-decisions.md). An accepted ADR is not edited; a
change of mind is a new ADR whose Status names the one it supersedes.

`docs/spec/log-and-projection-model.md` is the authority for `Varve.Store`
behaviour. ADRs 0010–0020 record the decisions it presupposes; where they refine
or depart from `docs/brief.md`, each says so in its Context.

## Set zero — foundation

| # | Title | Status |
|---:|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted; **amended by 0068** |
| [0002](0002-licence.md) | Licence: Apache-2.0 | **Superseded by 0031** |
| [0003](0003-package-layering.md) | Package layering and the strictly downward reference rule | Accepted; layer table **superseded by 0060**; declaration **by 0064**; suppression escape **by 0062** |
| [0004](0004-enforcement-by-analyzers.md) | Enforcement by analyzers | Accepted; reservation table **superseded by 0062**; amended 2026-10-07 (the `System.Uri` ban narrowed); amended 2026-10-08 (`Varve.Protocol.Client`) |
| [0005](0005-store-is-sparql-free.md) | `Varve.Store` is SPARQL-free | Accepted |
| [0006](0006-build-and-test-dependencies.md) | Build-time and test-time dependencies | **Superseded by 0009** |
| [0007](0007-w3c-conformance-harness.md) | W3C conformance harness | Accepted |
| [0008](0008-target-framework-and-language.md) | Target framework and language version policy | Accepted |
| [0009](0009-dependency-policy-and-register.md) | Dependency policy and the enforced register | Accepted; **amended by 0063** |

## Set zero — the log and projection model

| # | Title | Status |
|---:|---|---|
| [0010](0010-commit-model-and-effective-deltas.md) | Commit model and effective deltas | Accepted |
| [0011](0011-concurrency-single-sequencer.md) | Concurrency: one sequencer, optional expected position | Accepted; primitive results **superseded by 0065** |
| [0012](0012-term-dictionary-and-id-scheme.md) | Term dictionary, id classes, id scheme, blank node identity | Accepted |
| [0013](0013-records-commits-and-bulk-load.md) | Records versus commits, and bulk load | Accepted |
| [0014](0014-header-chain-and-divergence.md) | Header chain and divergence detection | Accepted |
| [0015](0015-checkpoints-and-reads.md) | Checkpoints, pinned reads, as-of reads, archive horizon | Accepted; "No temporal index in the default projection" **superseded in part by 0138** (filed unaccepted) |
| [0016](0016-projection-contract-and-subscriptions.md) | Projection contract, synchronous default projection, erasure in projections | Accepted |
| [0017](0017-validator-contract-and-overlay.md) | Pre-commit validator contract and the overlay quad source | Accepted |
| [0018](0018-storage-abstraction.md) | Storage abstraction: memory, file, browser | Accepted; "asynchronous throughout" **superseded by 0071** |
| [0019](0019-erasure-by-crypto-shredding.md) | Erasure by crypto-shredding | **Superseded by 0023** |
| [0020](0020-cipher-for-erasure-mode.md) | Cipher for erasure mode | **Superseded by 0028** |
| [0021](0021-dataset-settings-as-a-commit-kind.md) | Dataset settings as a commit kind | Accepted |
| [0022](0022-quad-source-term-handle.md) | The quad source contract over an opaque term handle | Accepted |
| [0023](0023-erasure-and-access-requests.md) | Erasure by crypto-shredding, and access requests | Accepted |

## Milestone 3a — the model and the syntax

| # | Title | Status |
|---:|---|---|
| [0024](0024-rdf-term-representation.md) | RDF term representation | Accepted |
| [0025](0025-property-based-testing.md) | CsCheck for property-based testing | Accepted |
| [0026](0026-hotpath-attribute.md) | Where the `[HotPath]` attribute lives | Accepted; placement **superseded by 0064** |
| [0027](0027-benchmarking.md) | Benchmarking | Accepted; dotNetRDF's fixture use (note of 2026-09-25) closed 2026-10-09 by ADR 0122 |

## Milestone 3a close-out

| # | Title | Status |
|---:|---|---|
| [0028](0028-deterministic-aead-from-hmac.md) | A deterministic AEAD built from HMAC-SHA-256 | Accepted; condition 1 **discharged** 2026-09-22, condition 2 open |
| [0029](0029-publishing-and-versioning.md) | Publishing and versioning | Accepted |

## Milestone 3b — Turtle and TriG

| # | Title | Status |
|---:|---|---|
| [0030](0030-turtle-recovery-and-prefixes.md) | Turtle's recovery unit, prefix exposure, and where isomorphism lives | Accepted; deletion clause **superseded by 0059**; amended 2026-10-09 (depth over the RDF 1.2 brackets, ADR 0121) |

## Stewardship — the Mind Over Machine standard

Adopted at [#16](https://github.com/Hafeok/Varve/issues/16). Four of these
reverse a decision this repository had already made, which is why each is a
superseding ADR rather than an edit.

| # | Title | Status |
|---:|---|---|
| [0031](0031-licence-mpl-2-0.md) | Licence: MPL-2.0 | Accepted; **supersedes 0002** |
| [0032](0032-trunk-based-development.md) | Trunk-based development and non-blocking review | **Superseded by 0088**; amended 2026-09-24 (the gate runs after the push); amended by 0066 |
| [0033](0033-commit-traceability.md) | Commit traceability and AI-session records | Accepted |
| [0034](0034-commit-signing-and-the-sandbox-exception.md) | Commit signing and the sandbox exception | Accepted; revisit condition; **amended by 0066**; amended 2026-10-06 (DCO enforced from 0087) |
| [0035](0035-semantic-versioning.md) | Semantic versioning | Accepted; complements 0029 |
| [0085](0085-a-release-per-milestone.md) | A release per milestone, and the next milestone waits for it | **Superseded by 0102**; complemented 0029 and 0035 |
| [0086](0086-home-of-the-eng-gates.md) | Home of the eng/ gates | Accepted; complements 0036 and 0039 |
| [0087](0087-the-identity-map.md) | The identity map: who signs off, accepts and approves for an agent | Accepted; enforces 0034 point 3; amended 2026-10-06 (approval by comment on one's own pull request; the exit to a signed ledger Review); amended again 2026-10-06 (the judging script is always main's; only App-pinned checks count; approval by comment is not a valid workflow); amended 2026-10-08 (an approved head lands through land/) |
| [0088](0088-only-checked-commits-reach-main.md) | Only checked commits reach main | Accepted; **supersedes 0032**; amended 2026-10-06 (required check names are checked); amended 2026-10-08 (a release's review is its descriptor's approval; `HumanReviewGatesReleases` superseded by 0102) |
| [0102](0102-a-release-is-a-descriptor.md) | A release is a descriptor, and landing it cuts it | **Accepted** (filed unaccepted, ADR 0066); **supersedes 0085** and 0088's `HumanReviewGatesReleases`; amends 0087 rule 3 |
| [0036](0036-containerised-development.md) | Containerised development and the local pipeline | Accepted |


## Milestone 4 — the in-memory log

Issue [#8](https://github.com/Hafeok/Varve/issues/8). The first code against the
specification, which moved to version 1.2 with 0046 and to 1.3 with 0047.

| # | Title | Status |
|---:|---|---|
| [0040](0040-storage-contract-members-and-the-memory-backend.md) | The storage contract's members, and where the memory backend lives | Accepted; member types **superseded by 0065**; derived members **by 0071** |
| [0041](0041-sorted-runs-for-the-default-projection-and-checkpoints.md) | Sorted runs for the default projection and for checkpoints | Accepted |
| [0042](0042-subscriptions-pull-from-the-log.md) | Subscriptions pull from the log | Accepted; amended 2026-10-02 (maintenance is not delivery; proposed); amended 2026-10-07 (a delivered commit externalises its handles; proposed) |
| [0043](0043-the-reference-model-as-a-test-asset.md) | The reference model is a test asset | Accepted |
| [0044](0044-blank-node-identity-in-process.md) | Blank node identity at the in-process boundary (Q1, split) | Accepted |
| [0045](0045-the-provisional-in-memory-log-encoding.md) | The provisional log encoding, and the in-memory id layout | Accepted, provisional by design; **superseded by 0072** |
| [0046](0046-settings-commits-reach-every-subscriber.md) | Settings commits reach every subscriber; specification 1.2 | Accepted; **amends 0016** |
| [0047](0047-delta-composition-and-closure-over-triple-terms.md) | Delta composition over chains, and dictionary closure over triple terms; specification 1.3 | Accepted |

## Milestone 5a — `Varve.Xsd`, the SPARQL algebra and parser

Issue [#9](https://github.com/Hafeok/Varve/issues/9). The maintainer's
positions from the close of milestone 4, written as decisions before the
first SPARQL line; 0051 and 0052 also carry the answers to the plan's
questions.

| # | Title | Status |
|---:|---|---|
| [0048](0048-optimiser-and-evaluator-one-package-algebra-in-algebra-out.md) | Optimiser and evaluator: one package, algebra in and algebra out; closes 0003's open question 2 | Accepted |
| [0049](0049-cardinality-estimates-on-the-quad-source.md) | Cardinality estimates on the quad source | Accepted; widens 0022; `Count`'s type **superseded by 0065** |
| [0050](0050-typed-value-accessor-and-the-benchmark-for-adr-0022.md) | A typed-value accessor beside the handle, and the benchmark ADR 0022 asked for | Accepted; widens 0022, measurement due 5b |
| [0051](0051-varve-xsd-scope-and-precision.md) | `Varve.Xsd`: scope, precision policy, canonical forms, and value comparison | Accepted; dateTime order verified in 5b |
| [0052](0052-pinned-read-lifetime.md) | A pinned read lives for one query execution | Accepted; refines 0015 |

## Milestone 5b — the evaluator and the optimiser

Issue [#9](https://github.com/Hafeok/Varve/issues/9). The five questions 5a
left open, decided by the maintainer on the 5b plan. The dateTime order stays
ADR 0051's and ADR 0050's benchmark stays as written; both are carried out in
`docs/spec/sparql-evaluation.md`. Dated notes the same day: 0004 (VARVE0007's
reserved wording admits algebra types), 0027 (dotNetRDF generates the RDF/XML
fixtures offline), 0048 (the evaluation package references `Varve.Iri`).

| # | Title | Status |
|---:|---|---|
| [0053](0053-aggregation-by-hash-grouping-and-accumulators.md) | Aggregation: hash grouping, one accumulator per aggregate | Accepted |
| [0054](0054-property-paths-normalised-then-evaluated-by-alp.md) | Property paths: normalised to joins and unions, closures by `ALP`; closes `sparql-algebra.md` open question 1 | Accepted |
| [0055](0055-service-through-a-handler-the-default-refuses.md) | `SERVICE` through a handler, and the default refuses | Accepted |
| [0056](0056-evaluator-options-extension-functions-clock-and-randomness.md) | Evaluator options: extension functions, the clock and randomness are injected; extends 0011's ban | Accepted |


## Milestone 5c — result writers, SPARQL Update, canonicalisation

Decided by the maintainer on the 5c plan.

| # | Title | Status |
|---:|---|---|
| [0057](0057-sparql-update-one-request-one-commit.md) | SPARQL Update over the store: one request, one commit | Accepted; amended 2026-09-25 (release before submit); amended 2026-10-08 (a data-only request expects no position) |
| [0058](0058-staging-view-and-dataset-validators.md) | A staging view over a pinned read, and validators bound to a dataset | Accepted; refines 0017 |
| [0059](0059-rdfc-in-varve-rdf-and-its-work-limit.md) | RDFC-1.0 in `Varve.Rdf`, bounded by a work limit; the isomorphism check stays as a cross-check | Accepted; supersedes 0030's deletion clause |

## After milestone 5c — hosts and the canonical form

Decided by the maintainer on the 5c report.

| # | Title | Status |
|---:|---|---|
| [0060](0060-hosts-at-layer-6-the-composition-root.md) | Hosts at layer 6: the composition root, reserved to executables | Accepted; supersedes 0003's layer table; amended 2026-10-07 (the protocol seam's revisit condition; proposed) |
| [0061](0061-canonical-n-triples-is-rdf-1-2s.md) | Canonical N-Triples and N-Quads follow RDF 1.2; where 1.1 and 1.2 differ, 1.2 wins | Accepted |

## Adopting `DecisionDriven.Analyzers`

Issue [#43](https://github.com/Hafeok/Varve/issues/43). Decided by the
maintainer on the adoption plan. Every ADR is also a decision set in
[`docs/decisions/`](../decisions/), which code cites as a type.

| # | Title | Status |
|---:|---|---|
| [0062](0062-adopting-decisiondriven-analyzers.md) | Adopting `DecisionDriven.Analyzers`: generic rules from a package, Varve's own rules only in `Varve.Analyzers` | Accepted; supersedes 0004's reservation table and 0003's suppression escape |
| [0063](0063-build-time-analyzer-packages.md) | Build-time packages for the analyzers, and what `BannedSymbols.txt` must cite | Accepted; amends 0009; amended 2026-09-26 (0006's two config-only rulings) |
| [0064](0064-varve-configuration-and-hot-path-rules.md) | Varve's configuration of the `DD` rules; the hot-path rules `VARVE0003` and `VARVE0004`; the layer declaration `VARVE0005` | Accepted; supersedes 0026's placement and 0003's declaration; two *not a model* bullets **superseded by 0069** |
| [0065](0065-wrapper-types-and-the-store-log-namespace.md) | Positions, ids and sizes as wrapper types; the log's values in `Varve.Store.Log` | Accepted; supersedes 0011, 0040 and 0049 in part; `Durability`'s placement **superseded by 0069** |
| [0066](0066-expected-red-pull-requests.md) | Expected-red pull requests: citing a decision nobody has accepted yet | Accepted; amends 0032 and 0034 |
| [0067](0067-inmemorydataset-is-a-value-built-by-a-builder.md) | `InMemoryDataset` is an immutable value, assembled by `InMemoryDatasetBuilder` | Accepted |
| [0068](0068-dated-amendments.md) | Dated amendments: add-only, dated, never a change of meaning | Accepted; amends 0001 |
| [0069](0069-model-namespaces-for-layers-3-to-5.md) | One model namespace per package at layers 3 to 5; `Durability` is a log value | Accepted; supersedes 0065's `Durability` line and two of 0064's *not a model* bullets |

## Milestone 6a — the file backend and format version 1

Issue [#10](https://github.com/Hafeok/Varve/issues/10). Decided by the
maintainer on the 6a plan, filed unaccepted (ADR 0066); specification 1.4, and 1.5 on its pull request.

| # | Title | Status |
|---:|---|---|
| [0070](0070-the-storage-engine-is-our-own.md) | The storage engine is our own, and it is two different things | Accepted; revisit condition |
| [0071](0071-synchronous-reads-over-asynchronous-storage.md) | Synchronous reads of derived data over asynchronous storage | Accepted; amended 2026-10-05 (replacing an open blob on Windows); supersedes 0018 and 0040 in part; revisit condition; amended 2026-10-15 (page fetch on a miss, filed unaccepted) |
| [0072](0072-format-version-1.md) | Format version 1, read forever from the first prerelease that writes it | Accepted; supersedes 0045; amended 2026-10-15 (derived version 4, filed unaccepted) |
| [0073](0073-durability-per-host-declared.md) | Durability per host, declared, and what each level survives | Accepted |
| [0074](0074-private-ids-reserved-and-the-key-store-refused.md) | The private id and entry layout, reserved; the key store refused by path | Accepted |
| [0075](0075-one-process-per-dataset-by-an-os-lease.md) | One process per dataset directory, held by an exclusive handle | Accepted |
| [0076](0076-bulk-load-by-sort-and-merge-join.md) | Bulk load and I2: sort into a run, merge-join, one multi-record commit (Q2) | Accepted; implemented in 6c |
| [0077](0077-bulk-load-validators-scan-the-delta-on-disk.md) | Bulk load and validators: the overlay scans the delta on disk (Q3) | Accepted; implemented in 6c |
| [0078](0078-checkpoints-streamed-and-written-by-policy.md) | Checkpoints streamed from the runs, and written by a policy; runs deleted once unread | Accepted |
| [0079](0079-the-term-dictionary-on-disk.md) | The term dictionary on disk, carried by the runs | Accepted |
| [0080](0080-derived-blocks-compressed.md) | Derived keys compressed in their blocks; derived format 2 | Accepted; answers 6a's compression question |
| [0081](0081-the-bulk-loader.md) | The bulk loader: references by content hash, memory bounded, one commit | Accepted; builds 0076 and 0077 |
| [0082](0082-the-soak-gate.md) | The soak gate: a flat working set within a stated band | Accepted; the gate #61 blocks; amended 2026-10-09 (judged under the shipped configuration) |
| [0083](0083-replica-bootstrap-is-a-file-copy.md) | Replica bootstrap is a copy of files | Accepted |
| [0084](0084-the-browser-backend.md) | The browser backend: OPFS sync access handles in a worker, IndexedDB as the fallback | Accepted; answers 0071's revisit condition for the browser |
| [0089](0089-the-commit-index-is-derived-and-paged.md) | The commit index is derived and paged | Accepted; the soak's drift (#61) |
| [0090](0090-file-suites-without-the-device-flush.md) | The file suites run without the device flush; Defender skips the build's files | Accepted; Windows test time |

## Milestone 7 — the server

Accepted ahead of the milestone, because the decision bears on what the server
is allowed to become and the cheapest time to reject API keys is before anyone
has written one.

| # | Title | Status |
|---:|---|---|
| [0037](0037-server-authentication.md) | Authentication and authorisation for the server | Accepted; amended 2026-10-07 (`JwtBearer` is a package, ADR 0099); test consequence refined by 0100; point 8's storage clause **superseded in part by 0105**; point 3 refined by 0107 |

Filed by milestone 7a ([#11](https://github.com/Hafeok/Varve/issues/11)), unaccepted until the maintainer accepts them on its pull request (ADR 0066). Amended by 7a, likewise unaccepted: 0004 (the `System.Uri` ban narrowed), 0042 (a delivered commit externalises its handles), 0060 (the protocol seam's revisit condition).

| # | Title | Status |
|---:|---|---|
| [0091](0091-varve-protocol-and-varve-server.md) | `Varve.Protocol` and `Varve.Server`: endpoint groups, the update seam, policy names | Accepted; amended 2026-10-08 (the 7b seams) |
| [0092](0092-protocol-scope-problem-details-and-tie-breakers.md) | Protocol scope, problem details, and the Oxigraph tie-breakers | Accepted; amended 2026-10-09 (the problem catalogue) and 2026-10-12 (RDF/XML and JSON-LD negotiated) |
| [0093](0093-datasets-are-the-routing-unit.md) | Datasets are the routing unit; the name is the host's | Accepted; refined by 0106; amended 2026-10-09 (the route table) |
| [0094](0094-a-write-over-http-is-one-commit.md) | A write over HTTP is one commit: agent, cause, `Varve-Position`, `409`, `412` | Accepted; amended 2026-10-09 (the cause is never a trace id) |
| [0095](0095-a-read-over-http-is-pinned-for-its-response.md) | A read over HTTP is pinned for its response, bounded, and cut visibly | Accepted; refines 0052 |
| [0096](0096-time-travel-over-http.md) | Time travel over HTTP: `Varve-As-Of`, `ETag`, `304` | Accepted; amended 2026-10-09 (the cache headers) |
| [0097](0097-the-change-feed-and-the-diff.md) | The change feed and the diff: a line format, SSE for tailing | Accepted; `docs/spec/change-feed.md`; amended 2026-10-09 (the commits resource) |
| [0098](0098-blank-nodes-at-the-protocol-boundary.md) | Blank nodes at the protocol boundary: stable labels out, fresh in, no skolem IRIs | Accepted; closes Q1, spec 1.6 |
| [0099](0099-register-jwtbearer-and-the-workflows-containers.md) | Register: `JwtBearer` in the server; no Testcontainers; the shared framework | Accepted; conditional on the server's AOT publish; amended 2026-10-15 (storage services, filed unaccepted) |
| [0100](0100-authentication-tested-in-three-layers.md) | Authentication tested in three layers | Accepted; refines 0037 |
| [0101](0101-the-server-configuration-aot-shutdown-readiness.md) | The server: configuration, Native AOT, shutdown, readiness, status | Accepted; amended 2026-10-09 (health paths, unknown keys); amended 2026-10-15 (ObjectStorage, filed unaccepted) |

Filed by milestone 7b ([#11](https://github.com/Hafeok/Varve/issues/11)), unaccepted until the maintainer accepts them on its pull request (ADR 0066). Amended by 7b, likewise unaccepted: 0004 (`Varve.Protocol.Client` speaks HTTP), 0057 (a data-only request expects no position), 0091 (the 7b seams).

| # | Title | Status |
|---:|---|---|
| [0103](0103-varve-protocol-client.md) | `Varve.Protocol.Client`: the HTTP client at layer 5, and the endpoint policy | Accepted |
| [0104](0104-service-and-load-over-http.md) | `SERVICE` and `LOAD` over HTTP | Accepted; discharges 0055's and 0057's reservations |
| [0105](0105-the-cli.md) | The CLI `varve`: one executable with the server, embedded and remote modes, the credential file, `System.CommandLine` | Accepted; supersedes 0037 point 8's storage clause in part |
| [0106](0106-the-admin-api.md) | The admin API | Accepted; refines 0093; amended 2026-10-09 (state and settings as resources) |
| [0107](0107-graph-level-authorisation.md) | Graph-level authorisation | Accepted; refines 0037; amends 0091 |
| [0108](0108-bulk-resolve-on-worker-threads.md) | The bulk load resolves and spills on worker threads | Accepted; builds 0081's last alternative |
| [0109](0109-per-run-term-filters.md) | Per-run term filters: derived format 3 | Accepted; discharges 0079's revisit condition |

## Milestone Operability — a server somebody can run

Filed by milestone Operability ([#12](https://github.com/Hafeok/Varve/issues/12)), unaccepted until the maintainer accepts them on its pull request (ADR 0066). Amended by it, likewise unaccepted: 0082 (judged under the shipped configuration), 0092 (the problem catalogue), 0093 (the route table), 0094 (the cause is never a trace id), 0096 (the cache headers; `410` reversed), 0097 (the commits resource), 0101 (health paths, unknown keys), 0106 (state and settings as resources).

| # | Title | Status |
|---:|---|---|
| [0110](0110-the-shipped-runtime-configuration.md) | The shipped runtime configuration: workstation concurrent GC, `ConserveMemory`, no heap limit of our own | Accepted; amends 0082 and supersedes it in part (the drift's window); the two-hour soak did not hold, #61 stays open |
| [0111](0111-the-container-image.md) | The container image: `ghcr.io/hafeok/varve`, Native AOT per architecture, attested | Accepted |
| [0112](0112-telemetry-in-two-layers.md) | Telemetry in two layers: the BCL instruments, the server exports | Accepted; admits the OpenTelemetry packages to `Varve.Server` |
| [0113](0113-health-and-readiness.md) | Health and readiness are different questions: `/health/live`, `/health/ready` | Accepted; amends 0101; amended 2026-10-15 (superseded, filed unaccepted) |
| [0114](0114-resource-governance.md) | Resource governance: a stated answer for every limit | Accepted; refines 0095 |
| [0115](0115-configuration-precedence-and-print-config.md) | Configuration from file, environment and command line; `--print-config`; an unknown key refuses to start | Accepted; amends 0101; amended 2026-10-15 (ObjectStorage keys, filed unaccepted) |
| [0116](0116-the-lease-policy-and-varve-lease.md) | The lease at start: a bounded wait, never a forced takeover; `varve lease` | Accepted; refines 0075; amended 2026-10-15 (varve lease for an epoch, filed unaccepted) |
| [0117](0117-varve-aspire.md) | `Varve.Aspire`: the hosting integration | Accepted; admits `Aspire.Hosting`; amended 2026-10-15 (WithAzureBlobStorage, filed unaccepted) |
| [0118](0118-api-alignment-while-everything-is-preview.md) | API alignment while everything is preview | Accepted; amends 0093, 0097, 0106; `410` reversed, 0096 stands |
| [0119](0119-headers-and-the-problem-catalogue.md) | Headers by read kind, and a catalogue of every problem the server can emit | Accepted; amends 0092, 0094, 0096; amended 2026-10-15 (writer-superseded, filed unaccepted) |
| [0120](0120-the-insert-data-fast-path.md) | The `INSERT DATA` fast path: ground quads to the commit request | Accepted; answers #35 |

## Milestone 6b — RDF 1.2 Turtle and TriG, RDF/XML, JSON-LD

Filed by milestone 6b ([#10](https://github.com/Hafeok/Varve/issues/10)), unaccepted until the maintainer accepts them on its pull request (ADR 0066). Amended by 6b, likewise unaccepted: 0030 (the recovery depth counts the RDF 1.2 brackets); 0092 (RDF/XML and JSON-LD negotiated); 0027 carries a dated note closing its RDF/XML fixtures.

| # | Title | Status |
|---:|---|---|
| [0121](0121-rdf-1-2-turtle-and-trig.md) | RDF 1.2 Turtle and TriG are accepted; the edition decides a surrogate escape | Accepted; supersedes `turtle.md` §9's refusal; amends 0030 |
| [0122](0122-rdf-xml-over-system-xml.md) | RDF/XML over `System.Xml`, with its cost measured and a size at which to revisit | Accepted; closes 0027's fixture note |
| [0123](0123-json-ld-over-utf8-json.md) | JSON-LD 1.1 over `Utf8JsonReader`, as a tree in an arena; the direction is RDF 1.2's by default | Accepted |

## Milestone Commit path — group commit and per-graph concurrency

Filed by milestone Commit path ([#91](https://github.com/Hafeok/Varve/issues/91)), unaccepted until the maintainer accepts them on its pull request (ADR 0066). Amended by Commit path, likewise unaccepted: 0011 (the expected position has a scope); 0057 (a request's scope is what it asked for); 0094 and 0096 (a graph's tag is its version); 0114 (a write waits in the sequencer's queue); 0119 (`graphs` on `409` and `412`).

| # | Title | Status |
|---:|---|---|
| [0124](0124-group-commit.md) | Group commit: one storage write and one flush for the requests that wait | Filed unaccepted (Commit path, #91) |
| [0125](0125-per-graph-optimistic-concurrency.md) | Per-graph optimistic concurrency and the graph-version index; amends 0011 and 0057 | Filed unaccepted (Commit path, #91) |
| [0126](0126-graph-versions-over-http.md) | Graph versions over HTTP: `ETag` and `If-Match` per graph, `Varve-If-Graphs-Match`; amends 0094, 0096 and 0119 | Filed unaccepted (Commit path, #91) |

## Milestone Client and integration — on-behalf-of, setup, follow (first part)

Filed by milestone Client and integration ([#93](https://github.com/Hafeok/Varve/issues/93)), unaccepted until the maintainer accepts them on its pull request (ADR 0066). Amended by it, likewise unaccepted: 0009 (a register entry's scope is checked); 0097 (the format is `Varve.Delta`'s, read for ever); 0102 (`delta-format` in the basis); 0103 (a token provider; no store in the closure); 0112 (`varve.azp`); 0117 (`WithDataset`'s setup).

| # | Title | Status |
|---:|---|---|
| [0127](0127-on-behalf-of-and-the-token-provider.md) | On-behalf-of: the client's token provider, `azp` on the span, and the attribution rule; amends 0103 and 0112 | Filed unaccepted (Client and integration, #93) |
| [0128](0128-varve-delta-and-the-read-forever-rule.md) | `Varve.Delta`: the delta format as a package of its own, read for ever; amends 0097, 0102 and 0103 | Filed unaccepted (Client and integration, #93) |
| [0129](0129-following-the-commits-resource.md) | Following the commits resource: `FollowAsync`, a checkpoint the caller owns, at least once | Filed unaccepted (Client and integration, #93) |
| [0130](0130-client-side-setup-and-system-graphs.md) | Client-side setup: a declared dataset, migrations recorded as data, and the first system graph | Filed unaccepted (Client and integration, #93); amended 2026-10-15 (ObjectStorage, filed unaccepted) |
| [0131](0131-hosting-the-client.md) | Hosting the client: `AddVarve`, `varve setup apply`, and `WithDataset`'s setup; amends 0117 | Filed unaccepted (Client and integration, #93) |
| [0132](0132-cors.md) | CORS: configured origins, bearer tokens without credentials, and what a browser may read | Filed unaccepted (Client and integration, #93) |
| [0133](0133-packages-scoped-to-samples.md) | Packages scoped to samples, and the register's scope check; amends 0009 | Filed unaccepted (Client and integration, #93) |

## Milestone Object storage — the log on any object store, Azure first

Filed by milestone Object storage ([#92](https://github.com/Hafeok/Varve/issues/92)), unaccepted until the maintainer accepts them on its pull request (ADR 0066).

| # | Title | Status |
|---:|---|---|
| [0134](0134-the-object-store-contract.md) | The object-store contract: five operations, one package per provider, REST over `HttpClient` | Filed unaccepted (Object storage, #92) |
| [0135](0135-the-log-as-chunk-objects.md) | The log as write-once chunk objects, consolidated on seal | Filed unaccepted (Object storage, #92) |
| [0136](0136-one-writer-by-an-epoch-and-a-log-slot-fence.md) | One writer by an epoch, fenced by a log slot; no lease, no timer; refines 0075 and 0116 | Filed unaccepted (Object storage, #92) |
| [0137](0137-storage-credentials-are-workload-identity.md) | Storage credentials are the workload's identity; a key only on loopback | Filed unaccepted (Object storage, #92) |
| [0138](0138-validity-intervals-in-the-default-projection.md) | Validity intervals in the default projection's checkpoints: a checkpoint is a chain of extent sets; supersedes 0015 in part | Filed unaccepted (Object storage, #92) |
| [0139](0139-extents-and-the-projection-catalog.md) | Extents and the projection catalog; derived format 4, rebuilt not migrated; the page cache | Filed unaccepted (Object storage, #92) |
| [0140](0140-what-varve-takes-from-azure-data-explorer.md) | What Varve takes from Azure Data Explorer, and what it does not | Filed unaccepted (Object storage, #92), from the maintainer's draft |
| [0141](0141-realestatecore-as-the-reference-domain.md) | RealEstateCore as the reference domain for learning and testing | Filed unaccepted (Object storage, #92), from the maintainer's draft |
| [0142](0142-deployment-on-container-apps-and-small-machines.md) | Deployment: Azure Container Apps with an identity-only store, the Aspire Azure variant, small machines | Filed unaccepted (Object storage, #92) |

## Milestone Client and integration — events, catalogue, schema registry (second part)

Filed by milestone Client and integration, part 2 ([#93](https://github.com/Hafeok/Varve/issues/93)), unaccepted until the maintainer accepts them on its pull request (ADR 0066). Numbered from 0143: Object storage ([#92](https://github.com/Hafeok/Varve/issues/92)) holds 0134–0142. Amended by it, likewise unaccepted: 0092 (the event vocabulary); 0107 (a graph may be governed by another); 0119 (three problem types and the validator's path); 0123 (RFC 8785 moves to `Varve.Json.Canonical`); 0130 (the catalogue graphs, the first derived grant).

| # | Title | Status |
|---:|---|---|
| [0143](0143-domain-events-as-cloudevents.md) | Domain events as quads, rendered as CloudEvents in canonical JSON | Filed unaccepted (Client and integration, #93) |
| [0144](0144-the-event-catalogue.md) | The event catalogue: declarations as data, one graph per source graph, governed by it; amends 0107 and 0130 | Filed unaccepted (Client and integration, #93) |
| [0145](0145-the-event-schema-registry.md) | The event schema registry: versions, immutable revisions, compatibility checked at commit | Filed unaccepted (Client and integration, #93) |
| [0146](0146-the-register-for-events.md) | The register for events: the Event Hubs SDK in a sample, a schema validator in tests, and the emulator's images | Filed unaccepted (Client and integration, #93) |
| [0147](0147-varve-json-canonical.md) | `Varve.Json.Canonical`: one RFC 8785 implementation, at layer 0; amends 0123 | Filed unaccepted (Client and integration, #93) |

## Milestone 8 — SHACL (first part, the standalone validator)

Filed by SHACL, part 1 ([#120](https://github.com/Hafeok/Varve/issues/120), under milestone 8, [#13](https://github.com/Hafeok/Varve/issues/13)), unaccepted until the maintainer accepts them on its pull request (ADR 0066). Numbered from 0148: Object storage ([#116](https://github.com/Hafeok/Varve/issues/116)) holds 0134–0142 and Client and integration, part 2, 0143–0147. Amended by it, likewise unaccepted: 0003 (open question 1 closed); 0007 (the SHACL suites); 0027 (dotNetRDF's SHACL validator); 0051 (XPath regular expressions in `Varve.Xsd`).

| # | Title | Status |
|---:|---|---|
| [0148](0148-varve-shacl-and-varve-shacl-sparql.md) | `Varve.Shacl` at layer 3, `Varve.Shacl.Sparql` at layer 4; closes 0003's open question 1 | Filed unaccepted (SHACL part 1, #120) |
| [0149](0149-the-shacl-conformance-baseline.md) | The SHACL conformance baseline: the 2017 suites gate, the 1.2 drafts pinned | Filed unaccepted (SHACL part 1, #120) |
| [0150](0150-recursive-shapes-stratified-greatest-fixpoint.md) | Recursive shapes: stratified, the greatest fixpoint in each stratum, refused statically otherwise | Filed unaccepted (SHACL part 1, #120) |
| [0151](0151-shacl-inputs-failures-and-instances.md) | SHACL inputs, failures and SHACL instances | Filed unaccepted (SHACL part 1, #120) |
| [0152](0152-shacl-paths-in-varve-shacl.md) | SHACL paths are evaluated in `Varve.Shacl`, and agree with the evaluator's | Filed unaccepted (SHACL part 1, #120) |
| [0153](0153-xpath-regular-expressions-in-varve-xsd.md) | XPath regular expressions are translated once, in `Varve.Xsd`; amends 0051 | Filed unaccepted (SHACL part 1, #120) |
| [0154](0154-the-validation-report-deterministic-rdf.md) | The validation report is deterministic RDF, and keeps the data's blank nodes | Filed unaccepted (SHACL part 1, #120) |
| [0155](0155-a-minimal-public-shacl-api.md) | A minimal public SHACL API, and the shapes model kept internal | Filed unaccepted (SHACL part 1, #120) |
| [0156](0156-dotnetrdf-shacl-for-benchmarks-and-as-an-oracle.md) | `dotNetRdf.Shacl` for the benchmarks and as a differential oracle; amends 0027 | Filed unaccepted (SHACL part 1, #120) |
| [0157](0157-shapes-for-event-declarations.md) | SHACL shapes for the event declaration vocabulary, shipped with `Varve.Events` | Filed unaccepted (SHACL part 1, #120) |
| [0169](0169-commit-time-gating-under-a-shapes-binding.md) | `Varve.Shacl.Store`: commit-time gating under a shapes binding; amends 0017, 0058, 0119 | Filed unaccepted (SHACL part 2a, #138) |
| [0170](0170-the-shapes-binding-vocabulary.md) | The shapes binding vocabulary; amends 0166 | Filed unaccepted (SHACL part 2a, #138) |
| [0171](0171-which-shapes-are-incrementally-maintainable.md) | Which shapes are incrementally maintainable: footprints, anchors and three classes | Filed unaccepted (SHACL part 2a, #138) |
| [0172](0172-varve-derivation.md) | `Varve.Derivation`: delta-driven maintenance at layer 2; amends 0003, 0150, 0152 | Filed unaccepted (SHACL part 2a, #138) |
| [0173](0173-the-validation-report-on-the-commit.md) | The validation report on the commit: a reference and a digest; amends 0154 | Filed unaccepted (SHACL part 2a, #138) |

## Milestone Management plane — fleet system dataset, grants as code, `varve apply` (first part)

Filed by milestone Management plane, part 1 ([#105](https://github.com/Hafeok/Varve/issues/105)), unaccepted until the maintainer accepts them on its pull request (ADR 0066). Numbered from 0163: SHACL holds 0148–0157 and Extent maintenance 0158–0162; 0167, the event-schema move, is part 2's. Amended by it, likewise unaccepted: 0017 and 0058 (host attachments); 0037, 0093, 0101, 0106, 0107, 0113, 0117, 0118 and 0130 (rulings superseded by 0163); 0094 and 0142 (superseded by 0168); 0115, 0131 and 0136 (additions).

| # | Title | Status |
|---:|---|---|
| [0163](0163-the-management-plane.md) | The management plane: a fleet system dataset, declarations as code, `varve apply` | Filed unaccepted (Management plane, #105) |
| [0164](0164-the-tier-1-follower.md) | The tier-1 follower: a dataset that tails its writer's log on the same store | Filed unaccepted (Management plane, #105) |
| [0165](0165-the-management-position-on-the-commit.md) | The management position on the commit: a host attachment, no format change | Filed unaccepted (Management plane, #105) |
| [0166](0166-varve-management-and-the-vocabulary.md) | `Varve.Management` and the management vocabulary | Filed unaccepted (Management plane, #105) |
| [0168](0168-write-routing-by-forwarding.md) | Write routing by forwarding: the writer directory, the hop rule and the internal address | Filed unaccepted (Management plane, #105) |

## Milestone Management plane — the event-schema move (second part)

Filed by milestone Management plane, part 2 ([#105](https://github.com/Hafeok/Varve/issues/105)), unaccepted until the maintainer accepts it on its pull request (ADR 0066). Amended by it, likewise unaccepted: 0144 (point 6 realised), 0145 (revision identifiers after the move) and 0166 (the event declarations join).

| # | Title | Status |
|---:|---|---|
| [0167](0167-event-declarations-move-to-the-management-plane.md) | Event declarations move to the management plane: freeze, barrier, copy and switch; the boundary; `m{p}` revisions | Filed unaccepted (Management plane, #105) |

## Milestone Extent maintenance — background merges and extent deletion

Filed by milestone Extent maintenance ([#110](https://github.com/Hafeok/Varve/issues/110)), unaccepted until the maintainer accepts them on its pull request (ADR 0066). Numbered 0158–0162: SHACL holds 0148–0157 and the Management plane 0163 onward. Amended by it, likewise unaccepted: 0134 (a follower writes its reader record; the streamed create; refusals for the identity); 0138 (the chain bounded by merges, the checkpoint budget removed); 0139 (deletion and merges superseded, the shared commit index, the catalog's sequence as its version).

| # | Title | Status |
|---:|---|---|
| [0158](0158-background-extent-merges.md) | Background merges of extent sets: only the writer merges, and a merge rebases on the catalog | Filed unaccepted (Extent maintenance, #110) |
| [0159](0159-the-extent-merge-policy.md) | The extent merge policy: size-tiered with a fan-out and a cap, its bound and its write amplification | Filed unaccepted (Extent maintenance, #110) |
| [0160](0160-extent-deletion-by-reader-announcements.md) | Extent deletion by reader announcements: readers announce an interval, and no timer; supersedes 0139 in part | Filed unaccepted (Extent maintenance, #110) |
| [0161](0161-the-shared-commit-index.md) | The shared commit index: each extent set carries its window's commit index | Filed unaccepted (Extent maintenance, #110) |
| [0162](0162-storage-operations-and-the-readers-command.md) | Storage operations: `varve storage`, the readers command, readiness, and the operator guide | Filed unaccepted (Extent maintenance, #110) |

## Conformance — differential testing against Oxigraph

| # | Title | Status |
|---:|---|---|
| [0038](0038-upstream-contribution-policy.md) | Upstream contribution to Oxigraph and the shared test suites | Accepted; refines the brief's tie-breaker |

## Tooling — repo-standard

Built here and moving to a repository of its own; see
[#23](https://github.com/Hafeok/Varve/issues/23).

| # | Title | Status |
|---:|---|---|
| [0039](0039-repo-standard.md) | repo-standard: repository settings as code, built here and moving out | Accepted |

**0085 was `Proposed`** from its filing by the pre-release session of #63 until its acceptance on its pull request, both on 2026-10-06; the decision sets of 0086–0088, decided by the maintainer in that session, were accepted in the same commit (4c7e911). **0078–0084 were `Proposed`** from their filing by milestone 6c on 2026-10-05 until their acceptance on its pull request on 2026-10-06. **0070–0077 were** from their filing by milestone 6a on 2026-10-02 until their acceptance on its pull request on 2026-10-05. [0069](0069-model-namespaces-for-layers-3-to-5.md) was,
from its filing on 2026-09-29 until its acceptance on 2026-09-30. Three others were
`Proposed` once, and were completed in place rather than superseded, because
ADR 0001's no-edit rule binds accepted decisions and they had never been
accepted.

### Accepted ahead of the evidence

Six ADRs carry a **revisit condition**: a stated fact which, if it turns out to
be true, supersedes the ADR. It does not edit it.

**One has fired.** ADR 0020's condition was its acceptance condition, it was
tested on a real browser at milestone 3a, and it failed. Its successor, 0028,
carries a condition of its own that is not measurable by a build.

| ADR | Condition | Due |
|---|---|---|
| [0012](0012-term-dictionary-and-id-scheme.md) | Milestone 4 benchmarks of index size and scan throughput contradict 64-bit counter-allocated ids. No bytes are frozen before milestone 6. | **did not fire** at milestone 4: 192 bytes per quad for six orders, 37–45 M quads/s scanned (`tests/Varve.Benchmarks/README.md`). At milestone 6a, **did not fire, and the locality hypothesis holds**: sorted keys delta-encode to 5.2–7.1 bytes with counter ids against 17.3–33.9 with scattered ids; format version 1 freezes the 64-bit layout (ADR 0072) |
| [0018](0018-storage-abstraction.md) | The in-memory and browser backends cannot both implement the contract without leaking backend detail. Members fixed when the first backend is written. | members **fixed** by [0040](0040-storage-contract-members-and-the-memory-backend.md); the memory backend and a second one outside the assembly implement them with nothing leaked. The browser half waits for milestone 6 |
| [0020](0020-cipher-for-erasure-mode.md) | ~~AES-CBC, HMAC-SHA-256 and HKDF do not run on browser WASM when verified on a real build.~~ **Fired** at milestone 3a: `Aes.Create()` throws on browser-wasm and no symmetric cipher of any kind is available there. Superseded by 0028. | **fired**, superseded |
| [0028](0028-deterministic-aead-from-hmac.md) | External cryptographic review rejects the construction. The fallback is then that erasure mode does not run in the browser, in its own ADR. Its other condition — the primitives run in a browser — is measured and holds. | milestone 9, before shipping |
| [0022](0022-quad-source-term-handle.md) | Milestone 5 evaluator benchmarks show the opaque handle costs more than it saves. The accessor it named as its successor is built in 5a ([0050](0050-typed-value-accessor-and-the-benchmark-for-adr-0022.md)), which fixes the three arms and the verdict rule. | milestone 5b |
| [0070](0070-the-storage-engine-is-our-own.md) | A workload at milestone 7 where tier merges cannot keep up with the commit rate. | milestone 7 |
| [0071](0071-synchronous-reads-over-asynchronous-storage.md) | A host where no synchronous read exists. | milestone 6c, the browser |
| [0034](0034-commit-signing-and-the-sandbox-exception.md) | A route appears by which a sandbox commit is signed by a key the project controls, or by GitHub itself. Two are identified and neither is available: GraphQL `createCommitOnBranch`, blocked by the session proxy rather than by GitHub, and a per-installation signing key. The REST contents API was tested and is **not** one — it produces unsigned commits. | whenever it fires |

## Open questions recorded, not resolved

From the specification's §11, each owned by the ADR that records the decision it
falls out of:

| | Question | Owner | Due |
|---|---|---|---|
| **Q1** | External form of store-scoped blank node identity at protocol boundaries. The in-process half is **decided** by [0044](0044-blank-node-identity-in-process.md): by handle | [0012](0012-term-dictionary-and-id-scheme.md) | milestone 7, with the server |
| **Q2** | Bulk load and I2 — normalising a huge commit against a populated dataset | [0013](0013-records-commits-and-bulk-load.md) | **decided** by [0076](0076-bulk-load-by-sort-and-merge-join.md): sort and merge-join; built in 6c |
| **Q3** | Bulk load and validators — an overlay that does not fit in memory | [0013](0013-records-commits-and-bulk-load.md), with [0017](0017-validator-contract-and-overlay.md) | **decided** by [0077](0077-bulk-load-validators-scan-the-delta-on-disk.md): the overlay scans the delta on disk; built in 6c |
| **Q4** | How a shredded term appears in SPARQL results and serialisations | [0023](0023-erasure-and-access-requests.md) | milestone 9 |
| **Q5** | Lookup by private value — scan and decrypt, or a keyed blind index | [0028](0028-deterministic-aead-from-hmac.md) | milestone 9 |
| **Q6** | Cipher and availability per host | [0028](0028-deterministic-aead-from-hmac.md) | **decided**, subject to 0028's two conditions: the primitives run in a browser (measured, holds) and the construction survives external review (milestone 9) |
| **Q7** | ~~Whether an access request defaults to `G_head` or every quad ever asserted~~ | [0023](0023-erasure-and-access-requests.md) | **closed** — by key id, scope a dataset setting defaulting to `AllHistory` |
| **Q8** | Key granularity when one term is about two data subjects | [0023](0023-erasure-and-access-requests.md) | milestone 9 |
| **Q9** | Whether the structure surviving shredding counts as anonymous — **a legal question** | [0023](0023-erasure-and-access-requests.md) | milestone 9 |

From ADR 0003:

- **0003 open question 1** — `Varve.Shacl` and the SPARQL evaluator are both
  placed in layer 3, yet SHACL-SPARQL depends on the evaluator. Due milestone 8.
- **0003 open question 2** — ~~the SPARQL optimiser and evaluator are both in
  layer 3. If the evaluator consumes a plan type the optimiser owns, they are one
  package or two layers.~~ **Closed** 2026-09-24 by
  [0048](0048-optimiser-and-evaluator-one-package-algebra-in-algebra-out.md):
  no plan type, one layer 3 package.

## Obligations recorded against a later milestone

- **ADR 0038** — the differential harness enforces the four exemption checks
  (category, required references, no `varve-defect`, stale entry), each with a
  violating and a conforming test. Due with the differential harness. Its open
  question (whether a `spec-gap` entry must cite a W3C suite PR) is due then too.
- **ADR 0004** — the `System.Uri` ban is wider than the brief scopes it. Narrow
  before layer 5 exists.
- ~~**ADR 0014** — the storage format must carry a version discriminator from the
  start, so a change to what is hashed is a migration rather than a corruption.~~
  **Discharged** by [0072](0072-format-version-1.md): every header carries the
  format version.

## Closed

- **ADR 0007 / 0027** — `dotNetRdf.Core`'s *manifest-reading* use. Closed
  2026-09-22 at milestone 3b, two milestones early: `Varve.Turtle` passes
  `rdf/rdf11/rdf-turtle` 313 of 313 and `rdf/rdf11/rdf-trig` 357 of 357
  unexempted, the harness reads its own manifests, and
  `The_harness_does_not_reference_another_rdf_implementation` keeps it out. The
  package stays for the benchmark baseline on ADR 0027's separate
  justification, and its register citation moved from 0007 to 0027 with this
  closure.
- **ADR 0002** — the copyright holder. Closed 2026-09-21: Emil Okkels Klein,
  named in `NOTICE`. The `LICENSE` appendix stays unedited, because it is the
  per-file boilerplate template and not a record of ownership.
- **ADR 0004** — the known limit that `VARVE0002`'s analyzer exemption was by
  name and could not be closed. Withdrawn 2026-09-21: it was wrong on both
  counts, and the rule now checks `IsPackable` and refuses the analyzer as a
  library reference.
