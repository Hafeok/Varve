# Inference, part 1: tier 1 — RDFS, RDF and D by rewriting, regimes over HTTP, the TBox binding

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The brief, the
> maintainer's answers to the plan and the change to ADR 0190 are verbatim in
> [the grants record](2026-10-27-issue-95-inference-grants.md), which this
> session filed first; this record does not repeat them. The transcript
> itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#170](https://github.com/Hafeok/Varve/issues/170), under [#95](https://github.com/Hafeok/Varve/issues/95) |
| **Date** | 2026-10-27 |
| **Tool** | Claude Code 2.1.311, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYPyBP5h8tEgWKGf4on` |
| **Branch** | `claude/sleepy-goldberg-91th34-part-1`, from the grants PR's head (`916c08c2`), as the maintainer asked |
| **Depends on** | [Hafeok/Varve#172](https://github.com/Hafeok/Varve/pull/172), ADR 0190; `main` is merged in once it lands |

## The prompts

The work is #170 as the maintainer opened it from the approved plan: the
issue body is the specification. The instruction to start it is the
maintainer's second message, quoted whole in the grants record: "Don't wait
for acceptance to start #170. Branch it from #172's head and merge `main`
once #172 lands. It's red only on `CS0618` until 0190 is accepted, which is
expected."

The maintainer's answer on review, verbatim, which reworked the schema's
build:

> 2.9 s to rewrite at 100,000 people means the schema is being built by
> scanning the data, which the plan ruled out (Q3a: a few predicate-bound
> probes). Rework it as follows. The current scan isn’t thrown away: it
> becomes the test oracle. Schema by probes, to a fixpoint. Probe
> rdfs:subPropertyOf and saturate it. The schema predicates are subClassOf,
> subPropertyOf, domain, range and every predicate whose subPropertyOf* chain
> reaches one of them. Probe each, re-saturate, and repeat until no new schema
> predicate appears. Subproperties of rdf:type are typing data, handled by the
> rewriting, not schema. C2 by existence probes, memoised per request, for
> candidate answer terms. Don’t compute the whole vocabulary of the graph.
> Classes and container membership properties by skip-seek, and only when the
> query needs them. Use the distinct objects of (rdf:type, ?o) and the
> distinct predicates, by seeking to the next distinct key on the
> predicate-first index, one seek per distinct value. Compute them only for a
> BGP with a variable in class or predicate position, or a meta-query. If an
> index can’t seek to the next distinct key, add that capability (it’s the
> same seek format 5 uses) rather than scan. Cache the saturated schema per
> graph version (ADR 0126). A merged active graph keys on the set of its
> graphs’ versions. Bound the cache, and report its hits and misses in the
> benchmark. Gates: Equivalence: a property that the probe-built schema equals
> the scan-built schema (today’s code, moved to the tests) on generated
> graphs. The generator must cover subproperties of the schema predicates,
> schema triples inside data graphs, chains through subPropertyOf, and rdf:_n.
> Scaling: rewrite time at 10,000, 100,000 and 1,000,000 people, with a fixed
> schema. Rewriting a query that names a class must stay flat with data size,
> within the benchmark’s noise. Report the cold time (first query) and the
> warm time (cached) separately. Report what’s left: ?s ?p ?o and ?x a
> rdfs:Resource stay answer-sized scans. State that in ADR 0193 and the
> benchmark, as the only O(data) shapes. Record the change against the plan in
> ADR 0193’s context: the first build scanned, and the plan’s probe design is
> what it ships.

## The report

### What was built

- **ADRs 0191–0194**, filed unaccepted, with their decision sets and the
  dated amendments to ADRs 0003, 0119, 0132 and 0166.
- **`Varve.Sparql.Entailment`** (layer 3): the TBox merge, the schema
  saturation per scoping graph by probes, the shapes and their tables, the
  rewriting of basic graph patterns, C2 by memoised probes, D by value,
  `TBoxCover`, one per binding, and `SchemaCache`, the schemas kept per
  graph version.
- **The schema rework** (the maintainer's answer above): the scan-built
  schema moved into the tests as the oracle; `IDistinctTermSource` in
  `Varve.Rdf` and the store's seek to the next distinct key, over runs,
  extent sets and an as-of tail; `IGraphVersionSource` and `GraphVersion`,
  answered by `DatasetView` and forwarded by the scoped source; the server
  keeps one cache per dataset's bindings.
- **`Varve.Management`**: `varve:TBoxBinding` read into
  `TBoxBindingDeclaration`, its shapes and residual check, the code oracle
  extended; `GraphScope.WithDatasetPublic` in `Varve.Rdf` and
  `GrantTable.ScopesOf` applying it; `ManagementPlan.DatasetPublic`, printed
  by `varve apply --plan`.
- **`Varve.Protocol`, `Varve.Server`, the client and the CLI**:
  `Varve-Entailment`, the default from the bindings, the regime in the
  response, `Vary` and the `ETag`; `entailment-unsupported`, and
  `entailment-refused` catalogued; the service description; CORS;
  `IEntailmentBindings` and the server's mapping; `QueryAsync` with a regime
  and `varve query --entailment`.
- **The suites**: the RDF, RDFS and D cases of `sparql11/entailment` and the
  `rdf-mt` cases the rewriting decides, under the ratchet, the RIF and OWL 2
  DL-only cases blocked by name and counted by `eng/status.cs`.
- **The browser**: `Varve.WasmSmoke` answers a query under RDFS and one
  under D, run in headless Chromium. **The bench**: `EntailmentBenchmarks`.
- **Docs**: `docs/operator/inference.md`, `docs/vocabulary.md`, the problem
  pages, `sparql-evaluation.md` §12.1.

### Properties, and what they found

| Property | Cases | Where |
|---|---|---|
| rewriting equals the closure, default graph, RDF, RDFS and D | 300; 3,000 nightly | `RewritingEqualsMaterialisationTests` |
| the same in a named graph with its own schema | 300; 3,000 | the same |
| the same under `GRAPH ?g`, schema inside data graphs | 300; 3,000 | the same |
| two bindings, the default graph covered, an uncovered graph | 300; 3,000 | the same |
| the grant property's tier-1 half (ADR 0190 point 8) | 300; 3,000 | the same |
| the probe-built schema equals the scan-built one | 300; 3,000 | `ProbeBuiltSchemaTests` |
| distinct predicates and objects by seek equal the scan's, every position | per position | `ExtentSeekTests` |
| a view's graph versions equal the fold's, and refuse past an as-of position | per position | `PerGraphConcurrencyTests` |
| the shapes and the code oracle agree on TBox bindings, fifteen faults | 1,000; 10,000 | `ShapesAgreementTests` |
| the plan's dataset-public list equals the grant tables' change | 300 | `TBoxBindingTests` |

Each property found something, and each finding was fixed in the code and,
where it was a gap in the ADR, stated in it:
- a blank-node constant in the generator, and C2 for `rdf:_n` constants;
- two constants binding one role, now a branch with no answer;
- `xsd:string` checked statically under `GRAPH ?g`, now a guard at run time;
- meta-modelling (`rdfs:subClassOf` typed or ranged under the schema's own
  classes), first read by scanning for the shapes that act on the schema,
  now by ADR 0193 point 2's probes;
- σ unbounded by the specification: Varve restricts it as C2 restricts μ,
  stated in ADR 0193;
- D's value types (`"1"^^xsd:int` is also an `xsd:integer`, a decimal);
- **a named graph the caller cannot read, or that does not exist, was merged
  with its binding's TBox**, so `GRAPH <g>` answered TBox conclusions under a
  graph the caller sees as absent; found by the grant property;
- **a graph's own schema was detected by schema predicates and meta-class
  typings only**, missing a triple that acts on the schema through a shape;
  found by the two-binding property at nightly depth. The saturation now
  records every probe it makes, and `GRAPH ?g` probes with exactly those.

The rework's properties found, each fixed:
- an `rdf:_n` in class position, a literal class, a meta domain on
  `rdfs:member` reached through an `rdf:_n`, and the axioms of a query-named
  `rdf:_n` acting on other patterns, by the equivalence property;
- an `rdf:_n` subject sending a graph to the whole read, which is now kept
  for the vocabulary's other terms;
- **a group under `GRAPH ?g` read the distinct terms of its TBox's scope, not
  of its graphs**, so a graph's own `rdf:_1` lost its axioms; found by three
  properties at nightly depth. A group reads its quads; only a scoping graph
  that is exactly the graphs evaluated is sought.

### Decisions taken inside the plan

- **Several bindings per dataset.** ADR 0193 merges each graph with the TBox
  of the binding that covers it, so the rewriter takes one `TBoxCover` per
  binding rather than one TBox for every graph.
- **`Vary` names the header on every dataset response**, not only on
  queries, so ADR 0119's one list stays one list.
- **The tag of a query's answer under a regime** is `"<position>.<token>"`;
  under simple entailment it is the position, as before.
- **A local `varve query`** answers under simple entailment only: the
  bindings are a server's declarations.
- **`entailment-refused`** is catalogued and emitted by no tier-1 regime;
  ADR 0192 said nothing of it at first, and #170 names it.

### The browser and the bench

The bundle's `_framework/`, trimmed, each file compressed alone:

| Build | raw | gzip | brotli |
|---|---:|---:|---:|
| 2026-10-15, before Inference | 5.37 MB | 2.05 MB | 1.72 MB |
| `main` on 2026-10-27, re-measured | 6.42 MB | 2.42 MB | 2.03 MB |
| this part | 6.52 MB | 2.46 MB | 2.07 MB |

The benchmark's numbers are in `tests/Varve.Benchmarks/README.md`, with the
machine.

### Checks run

- `dotnet build Varve.slnx` with `CS0618` as a warning, clean; red on
  `CS0618` alone until ADRs 0190–0194 are accepted, as expected.
- The test projects of every package touched, green; the conformance run,
  no regression against the baseline (this container lacks the rdf-canon,
  JSON-LD and data-shapes submodules, which CI has).
- `eng/status.cs --check` and the release descriptor.

### Left

- **#171, part 2**, and part 3, as the grants record lists them.
- **Merge `main`** once #172 lands, and when #165 or #151 does.

Developed with AI assistance under human review.
