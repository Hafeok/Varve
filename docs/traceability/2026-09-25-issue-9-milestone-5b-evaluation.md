# Milestone 5b — the optimiser, the evaluator and the results readers

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#9](https://github.com/Hafeok/Varve/issues/9) |
| **Date** | 2026-09-25 |
| **Tool** | Claude Code 2.1.282, a cloud session |
| **Model** | `claude-opus-5-5` (Claude Opus 5.5), configured and served — confirmed from the session's own metadata, not from memory |
| **Session identifier** | `session_01RdLqdHj7QShtJ4V9DaQAaV` |
| **Branch** | `claude/amazing-hawking-bqdtr3`, landing on `main` as one pull request (ADR 0034's amendment) |
| **Commits** | 11 |

The session ran out of context once and continued from a summary; the work
and this record are the same session's.

## The prompt

### First message

> Milestone 5b: the optimiser and the evaluator, `Varve.Sparql.Evaluation` at layer 3, to a full pass of the W3C SPARQL 1.1 query evaluation suite over the in-memory projection, and the same over `InMemoryDataset`. Read `docs/brief.md`, the specification, the 5a specs (`xsd.md`, `sparql-algebra.md`, `sparql-grammar.md`) and every ADR before planning; `AGENTS.md` applies. Plan first, wait for approval; conventional commits, issue references (#9), DCO sign-off; one PR at the end, merged by the maintainer. ADR numbers: next free, renumber on collision.
> A. Decisions for the five questions 5a left open
> Write each as an ADR (Accepted) or as the stated section of the evaluation spec, checked against every accepted ADR:
>
> 1. Aggregates. Evaluation follows SPARQL 1.1 Query §18.5 literally: `Group` produces groups keyed by the group expressions' values under the source's term equality; each aggregate is an accumulator (`Count`, `Sum`, `Min`, `Max`, `Avg`, `GroupConcat`, `Sample`) fed per group, with numeric promotion per §17.3 and errors handled per §18.5.1 (an error in an aggregate makes that binding unbound, not the query fail). Hash aggregation in memory; spilling is out of scope and the ADR says the memory bound is the caller's resource governance (operability milestone). `DISTINCT` inside aggregates is a set per accumulator. No `ORDER BY` inside `GROUP_CONCAT`; it is not in the specification.
> 2. Property paths. Sequence, alternative and inverse paths are rewritten at translation into joins, unions and swapped patterns with fresh variables, per §18.2.2.4 and §18.4; the evaluator sees them only as ordinary patterns. Zero-or-more, one-or-more and zero-or-one remain path nodes and are evaluated per §18.4 with the duplicate-free semantics the spec gives (a reachability search from each start binding, visited set per start), including the `?x path* ?y` case with both ends unbound (all nodes of the graph as starts, §18.4 `ALP` with the node set from the active graph). Negated property sets are a filtered scan.
> 3. dateTime order. ADR 0051 as decided: implicit-timezone total order, timezone an evaluator setting defaulting to UTC. Run the evaluation suite with it; if any case disagrees, stop and report before changing anything. Oxigraph's behaviour is the tie-breaker where the suite is silent.
> 4. ADR 0050 benchmark. Two evaluator builds are not needed; one switch on the evaluator options (`UseInlineValues`) is enough. Run the evaluation suite's total wall time and a FILTER-heavy micro-benchmark (numeric comparisons and arithmetic over one million quads) with and without, on the same machine, and report against ADR 0022's revisit condition explicitly, with a verdict.
> 5. SERVICE. The evaluator takes an `IServiceHandler` (a contract in `Varve.Sparql.Evaluation` over `Varve.Rdf` types and the algebra), receives the `Service` node and the incoming solutions, and returns solutions. The default handler refuses. On refusal without `SILENT` the query fails with an error naming the endpoint; with `SILENT` the behaviour is exactly what SPARQL 1.1 Federated Query §2.3 prescribes for a failed SILENT service, cited by sentence. The HTTP implementation is the server's at milestone 7 and sits at layer 5.
>
> B. What is built
>
> * `Varve.Sparql.Evaluation` (layer 3), referencing `Varve.Sparql`, `Varve.Rdf`, `Varve.Xsd`, `Varve.Iri`. It evaluates a `Query` against an `IQuadSource` and produces a solution stream (`SELECT`), a boolean (`ASK`), or a quad or triple stream (`CONSTRUCT`, `DESCRIBE` with a stated, minimal description algorithm: the concise bounded description is out of scope, say so). Working over handles: query constants are internalised through the source once per execution; expressions externalise only when they need a value, and use the inline accessor first (ADR 0050). Term equality and `DISTINCT` use the source's comparer. Value comparison and arithmetic go through `Varve.Xsd`. The full §17.4 function library for SPARQL 1.1, plus the SPARQL 1.2 built-ins the algebra carries, with `RAND`, `NOW`, `UUID`, `STRUUID` drawn from injected clock and randomness (the ban is extended to this package). Extension function registration is a contract, not a registry (ADR 0003: no static registries): functions are passed in evaluator options.
> * The optimiser, a pass `Query → Query` applied by default and skippable (ADR 0048). Version 1 rewrites: filter placement (push filters down to the lowest scope where their variables are bound, never across `OPTIONAL` boundaries incorrectly, §18.2.2.7 semantics preserved), BGP triple reordering by the cardinality estimate (ADR 0049), join reordering by estimate where the algebra allows, constant folding of expressions over literals, and elimination of trivial joins with the identity. Each rewrite has a property test: for generated queries and datasets, optimised and unoptimised evaluation give the same solution multiset. This is the test that keeps the optimiser honest and it is not optional.
> * `Varve.Sparql.Results` readers (layer 2, its own package): SPARQL Results XML, JSON, CSV and TSV readers, needed now because the evaluation suite's expected results come in those forms. The writers are 5c. Streaming, UTF-8, positions on error, the same style as the syntax packages.
> * Pinned reads: the evaluator's entry point takes a source it does not own (ADR 0052), and its documentation states the contract. Cancellation token on every entry point, checked at every operator boundary, so a runaway query stops.
>
> C. Definition of done
>
> * The W3C SPARQL 1.1 query evaluation suites (the `sparql11` query directories with `QueryEvaluationTest` entries: aggregates, bind, bindings, construct, csv-tsv-res, exists, functions, grouping, json-res, negation, project-expression, property-path, subquery, and the 1.0 evaluation directories they extend) wired into the conformance harness with guard counts, under the ratchet, run over both `InMemoryDataset` and the store's default projection (two subjects, same cases). Solution comparison per the manifest's result form: `SELECT` results compared as multisets with a blank node bijection, ordered where the query has `ORDER BY`; `CONSTRUCT` results by dataset isomorphism (already in the test project); `ASK` by value. No exemptions expected; each one that exists cites the spec section and Oxigraph's behaviour.
> * SPARQL 1.2 evaluation suites where the manifests are stable enough to pin (report what you find), ratcheted; the `lang-basedir` and `codepoint-escapes` evaluation entries 5a skipped included.
> * The optimiser equivalence property, and the UTF-8/UTF-16 analogue from 5a is not needed here; instead: for generated queries, evaluating over the store at position P and over an `InMemoryDataset` loaded from an as-of read at P gives the same solutions. That is the property that ties the evaluator to the storage thesis.
> * Allocation: per-solution allocation on a BGP-only query is bounded and stated (a solution is a small array of handles; assert the slope the way milestone 4 did); a scan through the evaluator over the store allocates nothing per quad below the solution.
> * AOT and browser-wasm smoke: load a small Turtle file, run three queries (BGP, aggregate, property path), print results.
> * Benchmarks, last and not gating: the ADR 0050 measurement above, plus the evaluation suite total time and a BSBM-style or generated mixed workload against dotNetRDF's in-memory query engine on the same machine, presented the way 3a's was, honest row first.
>
> D. Gates that come due
> Public API baselines for the two new packages; the `System.Uri` and ambient clock and randomness bans extended to `Varve.Sparql.Evaluation`; no new packages expected, stop and justify if one seems needed; `[HotPath]` on the operator loops and the scan adapters, with the allocation test standing in for VARVE0006.
> Non-goals
> No result writers, no update execution, no HTTP federation, no server, no RDFC-1.0 (5c). No spilling or memory governance beyond the cancellation token. Nothing in `tools/`.
> Report
> The traceability record and the PR body: the ADRs written, the evaluation spec added (`docs/spec/sparql-evaluation.md`, citing §18 per operator and §17 per function), suite counts per directory with the ratchet total and every exemption, the dateTime order verdict, the ADR 0050 benchmark with the verdict on ADR 0022's revisit condition, the store-versus-dataset equivalence property's iteration count and any counterexample found, other counterexamples found during development, benchmark numbers with hardware, proposed spec changes, and what 5c needs from the maintainer.

### The maintainer's decisions on the plan

The plan was presented with six questions. The answer, verbatim:

> All six as you recommended:
>
> 1. Three arms; 0050 stands as written. State that the materialised arm still joins on handles, so its cost is a lower bound.
> 2. Confirmed: normalisation pass in Varve.Sparql.Evaluation, always on, top level only, fresh names outside VARNAME.
> 3. Pin the 42 Turtle-1.2-data cases as blocked on turtle.md §9; the guard names the slice that unblocks them (Turtle 1.2 with RDF/XML and JSON-LD, before milestone 7). Add that slice to the roadmap.
> 4. N-Triples fixtures with SHA-256 guards, generated by dotNetRDF; dated note on ADR 0027; deleted when Varve.RdfXml lands.
> 5. Error naming the option; docs show TimeProvider.System as the caller's one line.
> 6. Wire sparql11/service through a test IServiceHandler. Do the Oxigraph comparison now with pyoxigraph, timed inside the call, stated as the Python binding; if cheap, also run the suite queries differentially against pyoxigraph and report every disagreement.
>
> The MD5 implementation from RFC 1321 with §A.5 vectors is accepted; add its row to the WASM table. Amend VARVE0007's reserved wording to cover contracts over algebra types. Proceed.

## The report

### What was built

**Four ADRs**, 0053–0056 — aggregation (§18.5 literally, hash grouping, an
accumulator per aggregate, the error table), property paths (normalised, then
closures by `ALP`), `SERVICE` through a handler whose default refuses, and
the evaluator's options (extension functions, custom aggregates, the clock
and randomness, none with a default) — each Accepted and checked against
every accepted ADR. Dated notes on 0004 (VARVE0007's reserved wording now
admits contracts over algebra types), 0027 (dotNetRDF's one offline use, the
RDF/XML fixtures), 0048 (`Varve.Iri` among the evaluation package's
references) and **0022 (the revisit condition judged, below)**.

**Two specifications**: `docs/spec/sparql-evaluation.md` — every operator by
§18.5 and §18.6, datasets by §13, the library by §17.4 with the 1.2
additions, `ORDER BY`'s order, the optimiser's rewrites and their
preconditions, ADR 0050's arms, allocation, the tests, and the findings —
and `docs/spec/sparql-results.md`.

**`Varve.Sparql.Results`** (layer 2): one pull reader for SPARQL results
XML, JSON, CSV and TSV over UTF-8 or a `ReadOnlySequence<byte>`, handing out
term views valid until the next `Read`, allocating nothing per solution once
warm, with SPARQL 1.2's triple terms and base directions, and positions on
error. XML is read by its own non-validating reader of the format's subset,
not `System.Xml`. Tested over all 508 result documents of the pinned tree,
with the `.srx` and `.srj` of each result compared and the chunk-boundary
oracle run over every one.

**`Varve.Sparql.Evaluation`** (layer 3): `SparqlEvaluator.Evaluate(query,
source, token)` over any `IQuadSource`, with the pinned-read contract (ADR
0052) stated on it. Solutions are rows of source handles with an
execution-local table for computed terms; every operator; the full §17.4
library with the 1.2 built-ins; casts; aggregates; paths; `SERVICE`;
`CONSTRUCT` and a minimal `DESCRIBE` (the concise bounded description is out
of scope, and the spec says so); `NOW`, `RAND`, `UUID` and `STRUUID` from
injected sources, with the ambient-clock and randomness ban extended to the
package; the package's own MD5 (RFC 1321), since the browser has none;
cancellation at every operator and inside scans, closures, sorts and
groupings; `[HotPath]` on the scan and BGP cursors, the row primitives and
the hot expression nodes. **The optimiser** folds constants, removes trivial
joins, places filter conjuncts, orders join chains and triple patterns by the
source's estimates, and counts each rewrite. A `SELECT`'s own projection is
compiled away where nothing above it compares whole rows.

**One parser fix** in `Varve.Sparql`: a grouped `SELECT` expression may read
an alias an earlier one bound (`sparql12/grouping` `select-variable-reuse`).

### The suites, per directory, with the ratchet total

Every `QueryEvaluationTest` of these directories runs over **two subjects**,
`InMemoryDataset` and the store's pinned view, one ratchet line per case per
subject. The guard pins each count.

| Directory | Cases | Blocked | Lines |
|---|---:|---:|---:|
| `sparql10/` basic 27, triple-match 4, open-world 18, algebra 14, bnode-coreference 1, optional 7, optional-filter 5, graph 17, dataset 12, type-promotion 30, cast 7, boolean-effective-value 7, bound 1, expr-builtin 25, expr-ops 18, expr-equals 15, regex 21, i18n 5, construct 5, ask 4, distinct 11, sort 14, solution-seq 13, reduced 2 | 283 | 0 | 566 |
| `sparql11/` aggregates 42, bind 10, bindings 11, cast 6, construct 5, csv-tsv-res 3, exists 6, functions 75, grouping 4, json-res 4, negation 12, project-expression 7, property-path 33, service 7, subquery 14 | 239 | 0 | 478 |
| `sparql12/` codepoint-escapes 5, eval-triple-terms 38, expression 5, grouping 2, lang-basedir 10, rdf11 3 | 63 | **41** | 44 |
| **Evaluation total** | **585** | **41** | **1,088** |

**All 1,088 pass. Exemptions: none.** With the 1,437 lines of the RDF and
SPARQL syntax suites the ratchet holds **2,525 of 2,525**. A guard also runs
every case that is not blocked in ADR 0050's two other arms over the store.

**The blocked cases are 41, not 42.** The plan's count included
`eval-triple-terms`' `expr-2`, whose data is `empty.nq`; it runs and passes.
The 41 — 37 of `eval-triple-terms`, 4 of `lang-basedir` — load RDF 1.2 Turtle
or TriG, which `turtle.md` §9 refuses; the guard prints each with its error
and names roadmap slice **6b** as the one that unblocks them. The roadmap's
wording was corrected to 41.

**The RDF/XML data** of `sparql10/sort` and `sparql11/subquery` (16 files) is
read from N-Triples translations dotNetRDF generated offline
(`--convert-rdfxml`), each guarded by its original's SHA-256, to be deleted
when `Varve.RdfXml` lands.

### The dateTime order — the verdict

**No case of the suites disagrees with ADR 0051's implicit-timezone total
order for `xsd:dateTime`.** It was applied as decided, the suites were run,
and no dateTime case failed; nothing about dateTime was changed.

**One adjacent finding, reported as the instruction asked.**
`sparql10/open-world`'s `date-1` and `date-2` compare **`xsd:date`** values
— `"2006-08-23"` against `"2006-08-23Z"` and `"2006-08-23+00:00"` — and their
expected answers require those comparisons to be *indeterminate*: XSD's
partial order, not the implicit-timezone total order. ADR 0051 fixed the
order for `xsd:dateTime` and left the other seven-property types "for 5b to
map or refuse as the evaluation suite decides"; they are mapped to the
partial order (`sparql-evaluation.md` §7.4, §13.1). This is within the
delegation, but it is the kind of case the instruction was written for, so
it is stated here rather than only in the spec. Oxigraph agrees with neither
answer on `date-2` (§13.4). **Proposed**: a dated note on ADR 0051 recording
the mapping, if the maintainer wants it in the ADR as well as the spec.

### ADR 0050's benchmark — the verdict on ADR 0022

**ADR 0022's revisit condition does not fire; 0022 stands and the accessor
stays.** On the suites' wall time over the store the three arms are
indistinguishable (medians 74.6–79.8 ms across three processes, against a
62–284 ms spread within each). On a million inline integers the accessor arm
is **20–23× faster** than the materialised arm on `FILTER` at 1%, 50% and 99%
selectivity and on equality, and **6.3×** on `ORDER BY`; it allocates 46 MB
against 692 MB. The materialised arm still joins on handles, so its cost is a
lower bound on a term-based contract's. The externalise arm — 0022 as shipped
— shows what the accessor bought: 2.0–2.3× on the filters, 5.0× on the sort.
0022 carries the dated verdict; the tables are in the benchmark README.

### Properties: iteration counts and counterexamples

| Property | Iterations per run | Counterexamples |
|---|---:|---|
| Optimiser equivalence (§8.6), every rewrite required to fire, two in five answers required non-empty | 20,000 | **one**, below; none in the 200,000 iterations run after the fix |
| Store as of a generated position against an `InMemoryDataset` of that view's quads (§12.2) | 2,000 | none |
| MD5 against the platform's, lengths 0–300 | 2,000 | none |

A typical run of the optimiser property fires filter placement about 2,400
times, triple order 3,100, join order 2,300, constant folding 6,300 and
trivial joins 5,400, and about half the generated queries have an answer.

**The optimiser property's counterexample was not a rewrite.** `SELECT * {
?b :p2 ?b . ?c !:p0 ?c . ?c :p2 ?d }` over data where two triples link `:s0`
to itself gave two solutions per `?c` unoptimised and one optimised: the
negated property set counted each triple when both ends were free and each
pair once when a join had bound one. SPARQL 1.1 and 1.2 define it as a set,
`{ μ | ∃ triple … }`; each direction is now a set, and the two directions of
a mixed set join as the `alt` §18.2.2.3 makes of them. Oxigraph 0.5.11 has
the same inconsistency (two solutions free, one bound). A regression test
keeps it.

### Counterexamples and defects found, and by what

- **The results oracle**, on its first run: a JSON binding name split across
  two segments of a `ReadOnlySequence` read as empty.
- **Writing the position tests**: `JsonReaderState` carries the line count
  across rebuilt readers, so an error's line is the document's.
- **The evaluation suites**, while the evaluator was written: a
  language-tagged literal compared unequal-by-error rather than unequal;
  `GRAPH ?g` pre-bound `?g` where §18.6 joins it after the inner pattern
  (`graph-variable-scope`, `graph-optional`, `agg-empty-group-count-graph`);
  `CONCAT()` with no argument; `BNODE(str)`'s memo lost across `Extend`
  copies; the zero-length step for a variable bound from outside
  (`values_and_path`); SPARQL 1.2's EBV (`not-not`); the parser's alias
  check (`select-variable-reuse`); `xsd:date`'s order (`date-1`, `date-2`).
- **The optimiser property**: the negated property set, above.
- **The allocation test**: a `SELECT`'s own projection copied every row, two
  rows per solution; it is compiled away, and the slope is the one row.
- **ADR 0050's benchmark, on its first run**: the materialised arm failed 25
  suite cases, every join through a blank node, because the store does not
  internalise a blank node's label back to a handle — a materialised term
  now keeps the handle it came from, and a guard runs every case in every
  arm; and `ORDER BY` externalised both keys on every comparison, 5.7 s and
  10.2 GB for a million rows, now 785 ms and 181 MB. The suite time's first
  run also ran the arms one after another and read the first 2.5× slower
  for tiered compilation; they are interleaved now.
- **The BSBM-style mix**: Q10 was first written with a product constant and
  returned no rows in any engine; it was widened before the numbers were
  taken.
- **ADR 0053, while it was written**: a claim that Oxigraph drops
  `GROUP_CONCAT`'s shared language tag was checked against pyoxigraph and
  was wrong; the ADR records what Oxigraph does and why it is not followed.
- **The CI run before the pull request**: `Varve.Store.Tests`'
  `the_index_update_allocates_the_six_key_arrays_and_nothing_per_quad`
  failed once, 7,920 bytes over, and passed in all nine runs after it. Nothing
  under `src/Varve.Store` or its tests changed on this branch; it is recorded
  here and suggested as its own task rather than touched.

### The differential run against Oxigraph

The 537 cases that are not blocked and call no `SERVICE`, answered by
pyoxigraph 0.5.11 from the same files and compared by the conformance
comparator: **508 agree, 27 disagree, 2 refused by Oxigraph**. Varve passes
all 537, so every disagreement is Oxigraph against the suite's expected
answer, and none is followed. Seven behaviours: typed literals stored by value
(12 cases), a date order (2), `GRAPH ?g` pre-bound (5), `{{ }}` scoped as one
group (1), a zero-length path from a constant not in the graph (4),
`GROUP_CONCAT` keeping a shared language tag (2), `BNODE(str)` across
solutions (1). Refused: uppercase `TRUE`, and an alias read by a later
`SELECT` expression. Case by case in `sparql-evaluation.md` §13.4. Under ADR
0038 these are upstream-defect candidates; none has been filed.

### Benchmarks, with the machine

Intel Xeon @ 2.80 GHz, 4 logical and 4 physical cores, 15 GiB, Ubuntu
24.04.4 LTS, kernel 6.18, a cloud container. .NET SDK 10.0.401, runtime
10.0.12, X64 RyuJIT `x86-64-v4`, BenchmarkDotNet 0.15.8; dotNetRDF 3.5.2;
pyoxigraph 0.5.11 on CPython in a virtual environment.

- **ADR 0050**: above.
- **BSBM-style mix**, 202,373 generated triples, nine queries: Varve over the
  store is **5–27× faster than dotNetRDF** with 7–13× less allocation, and
  faster than pyoxigraph on every query (1.1× on Q10 to 7× on Q2) —
  pyoxigraph timed inside the Python call, stated as the binding and not the
  Rust library. Every engine returns the same row count on every query.
  Varve over `InMemoryDataset` is 40–220× slower than dotNetRDF because that
  source scans every quad for every pattern, by design; it is reported as
  what it is.
- **The suites' wall time**, 522 SPARQL 1.0 and 1.1 cases over the store:
  about 76 ms in each arm.

The tables are in `tests/Varve.Benchmarks/README.md`.

### Smokes

**Native AOT**: ILC publishes the smoke app with no warning, and the binary
loads a Turtle document into the store and answers a basic graph pattern with
a filter, an aggregate, a `+` closure and the five hash functions correctly.
**The browser** (headless Chromium 141): the same, and the pinned
capability table gains four rows — `SHA1`, `SHA384`, `SHA512` available, and
the platform's **`MD5` throwing `CryptographicException`**, which is why
SPARQL's `MD5()` is the package's own, tested against RFC 1321 §A.5's seven
vectors.

### Gates

Public API baselines for both new packages; the `System.Uri` ban holds; the
ambient clock and randomness ban extends to `Varve.Sparql.Evaluation`, with
its messages citing ADR 0056; `[HotPath]` on the scan adapters and operator
loops, with the allocation test standing for VARVE0006; **no new packages**.
The two Python scripts beside the benchmarks use pyoxigraph, which is not a
build dependency and is not in the register. `eng/ci.cs` runs every job but
repo-standard's clean, and the new test project is a job of its own and a
step in both workflows.

### Proposed specification changes, not patched

For the repository's own documents:

- **ADR 0051**: a dated note recording that `xsd:date`, `xsd:time` and the
  `g` types take XSD's partial order, as `sparql-evaluation.md` §7.4 now
  says (above).
- **`InMemoryDataset`**: its linear `Match` and `Estimate` make it the slow
  subject by two orders of magnitude on 200,000 triples. It says so and was
  built to; whether it should grow an index is a decision, not a fix.

For the W3C suites and Oxigraph (ADR 0038):

- **A negated property set's multiplicity** is tested by no suite case; a
  case with two triples between the same two nodes would have caught both
  this implementation and Oxigraph's.
- **SPARQL 1.2's EBV** makes an ill-typed boolean or numeric an error where
  1.1 made it false; `sparql-evaluation.md` §7.3 applies the 1.2 rule to 1.0
  and 1.1 queries too, and no 1.0 or 1.1 case depends on the difference. A
  note in the 1.2 draft saying which rule a 1.1 query gets would settle it.
- **The seven Oxigraph behaviours** of the differential run are candidates for
  upstream issues; some (typed literals stored by value) are Oxigraph's
  known design, and which to file is the maintainer's call under ADR 0038.

### What 5c needs from the maintainer

1. **The result writers' shape.** The readers hand out term views; the
   writers take what — `SolutionResults` directly, or a row of `RdfTerm`s?
   And whether a writer→reader round-trip property is the gate, as the
   parser's was.
2. **Update execution's isolation.** `DELETE`/`INSERT … WHERE` will evaluate
   the `WHERE` with this evaluator over a pinned view and commit the result:
   at which position is the pin taken, and does the commit carry an expected
   position so a concurrent commit fails it rather than interleaves?
3. **RDFC-1.0's first consumer** is the result comparison: the harness's
   graph isomorphism would move onto it. Confirm.
4. **The filter-into-scan step.** A `FILTER` over a basic graph pattern
   allocates a row for every match it then rejects (46 MB for a million
   matches); moving simple conjuncts into the scan removes it. An
   optimisation for 5c or later, not a defect — say when.
5. **The Oxigraph comparison proper.** ADR 0027 asks for Oxigraph; this
   milestone measured the Python binding. A Rust harness is owed.
6. **ADR 0038**: which of the differential's disagreements to file.
7. **Roadmap 6b** unblocks the 41 cases; nothing in 5c depends on it.

## Commits

1. `f62509d` docs(adr): 0053–0056, the questions 5a left to 5b
2. `079f1a2` docs(spec): the evaluation and result-format specifications
3. `31ed038` feat(results): Varve.Sparql.Results — the four result-format readers
4. `e97abda` fix(sparql): a grouped SELECT expression may read an earlier alias
5. `61c82cf` feat(evaluation): Varve.Sparql.Evaluation — the optimiser and evaluator
6. `21dd909` test(evaluation): the optimiser and store properties, allocation, MD5
7. `882e349` test(conformance): the W3C query evaluation suites, over two subjects
8. `81c2cf1` test(smoke): the evaluator under Native AOT and in the browser
9. `09700b0` fix(evaluation): the materialised arm through blank nodes, and sort keys once per row
10. `f439e39` perf(evaluation): ADR 0050's arms, a BSBM-style mix, and Oxigraph
11. docs: roadmap, README, AGENTS, changelog, the traceability record

## What this record does not contain

The transcript, the plan as presented before approval, and the intermediate
states of files. The maintainer holds the transcript.
