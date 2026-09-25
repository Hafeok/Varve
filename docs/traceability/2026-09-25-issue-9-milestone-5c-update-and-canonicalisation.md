# Milestone 5c — the result writers, SPARQL Update as one commit, RDFC-1.0

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer. Developed with AI
> assistance under human review.

| | |
|---|---|
| **Issues** | [#9](https://github.com/Hafeok/Varve/issues/9) (the milestone), [#32](https://github.com/Hafeok/Varve/issues/32) (the allocation flake) |
| **Date** | 2026-09-25 |
| **Tool** | Claude Code 2.1.282, a cloud session |
| **Model** | `claude-opus-5-5` (Claude Opus 5.5), configured and served — confirmed from the session's own metadata, not from memory |
| **Session identifier** | `session_01QeE3JLFpiS3GNCvHQALYdq` |
| **Branch** | `claude/great-goodall-kjy0d4`, landing on `main` as one pull request (ADR 0034's amendment) |
| **Commits** | 14 |

The session ran out of context once and continued from a summary; the work
and this record are the same session's.

## The prompt

### First message

> Milestone 5c closes the query engine: the result writers, the SPARQL Update integration package that makes one update request one commit, and RDFC-1.0. It starts with the flaky allocation test. Read `docs/brief.md`, the specification, the SPARQL specs under `docs/spec/`, and every ADR before planning; `AGENTS.md` applies. Plan first, wait for approval; conventional commits, issue references (#9 for the milestone; the flake has its own issue), DCO sign-off; one PR at the end, merged by the maintainer. ADR numbers: next free, renumber on collision.
> A. First: the allocation flake
> The index-update allocation test in `Varve.Store.Tests` failed once in ten runs during 5b, 7,920 bytes over, with nothing in the store changed. Reproduce it by looping the measurement a few thousand times in a scratch test, find what allocated (tiered JIT recompiling a method during the measurement and pool growth are the usual suspects; `DOTNET_TieredCompilation` and `DOTNET_TieredPGO` switches will tell you quickly), and fix the cause or make the warm-up provably sufficient. Widening the tolerance is not a fix. Report what it was; if the same mechanism can hit the other allocation tests, fix them the same way in the same commit.
> B. Result writers
> `Varve.Sparql.Results` gains streaming writers for SPARQL Results XML, JSON, CSV and TSV, plus boolean results in each, writing UTF-8 to `IBufferWriter<byte>` and `Stream`, sync and async, with the same allocation discipline as the syntax packages: nothing per row beyond what the caller's row costs. The JSON writer must produce output the JSON reader reads back identically, and the same for each pair; that round trip is a property test over generated solution sequences, including blank nodes, language-tagged and typed literals, triple terms (SPARQL 1.2 results JSON has a defined form for them; XML too; check the 1.2 drafts and say what CSV and TSV do), and unbound variables. The W3C `csv-tsv-res` and `json-res` cases already pass on the reading side; add writer-side checks that our output for those queries matches the expected files byte for byte where the format is canonical, and by re-parse where it is not. Specification: extend `docs/spec/sparql-results.md` with the writers, citing the SPARQL 1.1 Query Results XML Format, the JSON format (RFC-style section numbers), and the CSV/TSV note.
> C. SPARQL Update: `Varve.Sparql.Store`
> Layer 5, the first consumer of the SPARQL-free store (ADR 0005). Specification first: `docs/spec/sparql-update-store.md`, then an ADR for the decisions below, checked against the log and projection model (T1, R1, R4) and ADRs 0005, 0010, 0011, 0017, 0052.
>
> * One request, one commit. An update request with several operations is one commit (ADR 0005). Operations are executed in order (SPARQL 1.1 Update §3.1: later operations see earlier ones' effects), so the integration pins the readable head, evaluates operation 1's WHERE against the pinned source, computes its delta, evaluates operation 2 against `Overlay(pinned, δ₁)` (the overlay from ADR 0017, exactly what it exists for), and so on, then submits the composed delta with `expectedPosition = pinnedPosition`. A `Conflict` from the sequencer means another writer committed in between; the integration reports it and does not retry by default (a retry policy is the caller's, stated). The pre-commit validators run as part of the commit as always.
> * Blank nodes in INSERT templates are fresh per request (spec T1 step 2); in DELETE templates they are illegal (Update §3.1.3) and the parser already rejects them; confirm.
> * Graph management. The store represents no empty graphs: a named graph exists iff it has quads. SPARQL 1.1 Update §3.2 allows this ("graph stores that do not record empty graphs"). So `CREATE` is a no-op, `DROP` and `CLEAR` retract the graph's quads, `ADD`, `MOVE`, `COPY` are quad-level, and `SILENT` changes nothing for those. Record it in the ADR, with Oxigraph's opposite choice (it records empty graphs) as the alternative and why we differ: an empty graph would need an event kind of its own in the log for no query-visible effect. If any suite case depends on empty-graph existence, it gets an exemption citing §3.2's allowance; I expect at most a handful. Report each.
> * LOAD. A contract in the package, `ILoadSource`, resolving an IRI to a document and format; the test handler serves `file:` IRIs from the suite directory; the HTTP implementation is the server's (milestone 7). `LOAD SILENT` on failure per §3.1.4.
> * USING / USING NAMED / WITH per §3.1.3, over the pinned source and the overlay.
> * Gates: the W3C SPARQL 1.1 update evaluation suites (`basic-update`, `clear`, `delete`, `delete-data`, `delete-insert`, `delete-where`, `drop`, `add`, `copy`, `move`, `update-silent`) wired into the harness with guard counts, under the ratchet; result comparison is dataset isomorphism of the store's as-of read after the request against the expected dataset. Every request in the suite must be visible as exactly one commit in the log, asserted. A property: for generated request sequences, applying them through `Varve.Sparql.Store` and applying the equivalent hand-computed deltas through the store's commit API give the same as-of state at every position, and the same number of commits.
> * Public surface: one entry point that takes a `Dataset` (or its store handle), an `Update`, options (validators come from the dataset; a `LoadSource`; the retry policy), and returns the commit outcome. Nothing else.
>
> D. RDFC-1.0
> In `Varve.Rdf` per the brief. Specification: `docs/spec/rdf-canon.md` citing RDF Dataset Canonicalization (RDFC-1.0) by section: the hash-first-degree and hash-n-degree algorithms, the canonical issuer, the permutation limit and the poison-graph guard (a configurable work limit that fails explicitly rather than running forever). SHA-256 from the BCL; the algorithm's `hashAlgorithm` parameter exposed so SHA-384 can be selected as the spec allows. Output: the canonical N-Quads form as bytes, and the issued blank-node map. Gates: the W3C `rdf-canon` test suite (`w3c/rdf-canon`; add it as a second submodule, pinned, with the ADR 0007 pattern) wired into the harness with guard counts, under the ratchet. Then two properties: `iso(A, B) ⇔ canon(A) = canon(B)` against the test project's existing isomorphism check on generated datasets, which also becomes that check's differential test; and idempotence, `canon(canon(A)) = canon(A)`. Keep the test-project isomorphism check; it is now a cross-check, not a duplicate.
> E. Gates that come due
> Baselines for the new package and the extended ones; the `System.Uri` ban holds (`file:` IRIs go through `Varve.Iri`); no new packages; AOT and browser-wasm smoke extended: run an update against an in-memory store, then a query, write the result as JSON, canonicalise a small dataset. Benchmarks, last and not gating: update throughput (INSERT DATA of N quads, DELETE/INSERT WHERE over a million-quad store) and canonicalisation of a 100,000-triple graph, against dotNetRDF and pyoxigraph, honest row first.
> Non-goals
> No server, no HTTP for LOAD or SERVICE, no Graph Store Protocol, no RDF/XML or JSON-LD, no Turtle 1.2. Nothing in `tools/`.
> Report
> The traceability record and the PR body: the allocation flake's cause and fix; suite counts for the update suites and `rdf-canon` with the ratchet total and every exemption; the empty-graph exemptions if any; the one-request-one-commit assertion; property iteration counts and counterexamples; the results-format 1.2 findings; benchmark numbers with hardware; proposed spec changes; and what milestone 6 needs from the maintainer, which is the first milestone with no W3C suite: say explicitly what you will need decided about the file format before you can start.

### The maintainer's decisions on the plan

> Decisions:
>
> 1. Dataset-level validators in the store (ADR 0058), request validators kept and run alongside.
> 2. Staging view accepted as ADR 0058's other half: it is spec T1 step 2 exposed; state the invariant that a staging handle reaches the dictionary or log only through the commit that maps it, and that rejected or empty requests leave no trace (already I3).
> 3. CREATE fails without SILENT when the graph has quads, no-op otherwise.
> 4. Canonical equality is the harness comparison for CONSTRUCT and update cases; the backtracking check runs alongside on every case and must agree, Inconclusive kept distinct; disagreement fails naming both verdicts. 0059 supersedes 0030's deletion clause with the work limit as the answer to its objection.
> 5. Commit-count assertion as reworded: exactly one commit when the net delta is non-empty, none otherwise, never more.
>
> Plan approved as presented. Proceed with #32 first.

## The report

### What was built

- **`Varve.Sparql.Results` writes.** `SparqlResultsWriter` writes SPARQL
  results XML, JSON, CSV and TSV, solutions and booleans, as UTF-8 to an
  `IBufferWriter<byte>` or a `Stream`, flushing synchronously or
  asynchronously. It is a state machine over one format writer. Terms are
  written from an `RdfTerm` or from a view, and writing views allocates nothing
  per solution. Specified in `docs/spec/sparql-results.md` §5–§6.
- **`Varve.Store` gains two things for its first layer 5 consumer (ADR 0058).**
  - `DatasetOptions.Validators` run on every commit, before the request's own
    validators.
  - `DatasetView.Stage()` opens a staging view. It is spec T1 step 2 exposed:
    fresh blank nodes and terms not yet in the dictionary get staging handles,
    which reach the dictionary or the log only through the commit that maps
    them. A rejected or empty request leaves no trace (I3).
- **`Varve.Sparql.Store`, layer 5, is new.** Its whole public surface is one
  entry point, `SparqlUpdate.ExecuteAsync(Dataset, Update, UpdateOptions,
  CancellationToken)`, which returns the commit's `CommitResult`. The options
  carry evaluation settings, commit metadata, an `ILoadSource` and a retry
  count, which defaults to none. Supporting types are the `ILoadSource`
  contract with `LoadedDocument` and a refusing default, and
  `SparqlUpdateException`, which names the failed operation's index and kind.
  Specified in `docs/spec/sparql-update-store.md`; decided in ADR 0057.
- **`Varve.Rdf` canonicalises.** `RdfCanonicaliser.Canonicalise` implements
  RDFC-1.0 over any `IQuadSource`:
  - It uses SHA-256 by default; SHA-384 and SHA-512 can be selected.
  - A work limit, 1,000 by default, throws
    `CanonicalisationLimitException` when exceeded.
  - The output is the canonical N-Quads form (Appendix A) as bytes, plus the
    map of issued identifiers.

  Specified in `docs/spec/rdf-canon.md`; decided in ADR 0059, which also
  supersedes ADR 0030's deletion clause.
- **The conformance harness compares datasets by canonical equality**, with
  the backtracking isomorphism check run beside it on every case as a
  cross-check. This applies to `CONSTRUCT` results and to update results.
  The two agreed on every case. A disagreement would fail the case naming both
  verdicts, and Inconclusive is kept distinct.
- **ADRs 0057–0059** are accepted. They cover update as one commit, the
  dataset validators and the staging view, and RDFC-1.0 in `Varve.Rdf` with
  its work limit.

**The decisions on the plan, as built.**
- `CREATE` fails without `SILENT` when the graph has quads, and is a no-op
  otherwise.
- The commit-count assertion is exactly one commit when the net delta is
  non-empty, none otherwise, and never more.
- The prompt asked me to confirm that the parser rejects blank nodes in
  `DELETE` templates. **Confirmed.** `DELETE DATA`, `DELETE WHERE` and the
  `DELETE` template of a modify are all parsed with blank nodes refused
  (`Parser.Update.cs`, `allowBlankNodes: false` in each), and
  the syntax suites' negative cases hold it.

### A — the allocation flake (#32)

**Cause.** Two effects outside the code under test move a single reading of
`GC.GetAllocatedBytesForCurrentThread`:

- **A collection in the window adds bytes.** It retires the thread's
  allocation context, and the counter keeps the retired context's unused
  tail: up to one 8 KB quantum. The flake's "7,920 bytes over" is exactly that
  tail.
  - To reproduce it, I ran a thread allocating 200 KB arrays beside the
    measurement. It gave 7,960 bytes over in 248 of 3,000 windows, and 19
    failures in 20 runs of the real test class.
  - The effect only ever adds bytes: none of 12,000 readings fell below the
    true value.
  - It does not show as a change in `GC.CollectionCount(0)`, because a
    background collection suspends the thread before the count moves. So
    rejecting readings on a count change does not work, and I tried it.
- **A tier-up subtracts bytes.** Once the measured call is rejitted with PGO
  and inlined, an object the caller discards is stack-allocated. That was the
  32-byte `IndexVersion` the test dropped, or a scan's cursor. With tiered
  compilation or PGO off, it never happens.

**Fix.** `tests/AllocationMeter.cs` is linked into the six projects that
measure allocation.
- It takes each reading inside a no-GC region, and discards the reading if
  the region broke.
- The measured action returns what it made, and the meter keeps that alive,
  so escape analysis has nothing to remove.
- A pair of readings counts only when the next round reproduces it, and that
  round is also the warm-up.

**Result.** Under the same noise, 0 of 80 runs of the store's class failed, 40
of them with tier-up forced on the first call. 120 plain runs of the six
classes passed. The assertions stay exact and no tolerance changed. Both
mechanisms could hit every allocation test, so all six projects moved to the
meter in the same commit. The one exception is the commit-budget test: it
keeps a direct reading, because a commit changes the state it is measured in,
and 8 KB over 3,500 quads is 2.3 bytes per quad against its budget.
`docs/testing.md` §4 is updated to match.

### The suites, per directory, with the ratchet total

**Update evaluation**: 94 of 94, one ratchet line per case over the store.
**62 committed exactly once and 32 made no commit.** The 32 are requests whose
net effect is empty against the case's data — among them `CLEAR`, `DROP`
and `CREATE` over absent graphs and the `SILENT` operations that fail. Each
case asserts the commit count against the log.

| Directory | Cases |
|---|---:|
| `sparql11/basic-update` | 13 |
| `sparql11/clear` | 4 |
| `sparql11/delete` | 19 |
| `sparql11/delete-data` | 6 |
| `sparql11/delete-insert` | 9 |
| `sparql11/delete-where` | 6 |
| `sparql11/drop` | 4 |
| `sparql11/add` | 8 |
| `sparql11/copy` | 6 |
| `sparql11/move` | 6 |
| `sparql11/update-silent` | 13 |
| **Total** | **94** |

**`rdf-canon`**: 86 of 86. This is `w3c/rdf-canon`, a second submodule pinned
at `15619df2fda7a4ca88308733789b6774517f9638` with ADR 0007's guard pattern.
- 64 cases compare the canonical N-Quads byte for byte; 2 of them run under
  SHA-384.
- 21 compare the issued-identifier map.
- 1 negative case, the poison graph, must be refused by the work limit, and
  is.

**Writers**: 10 of 10.
- The five CSV and TSV cases other than `tsv03` compare line for line. The
  comparison allows a bijection of blank node labels and, for CSV, CRLF
  against the file's LF.
- `tsv03` and the four `json-res` cases compare by re-reading (see the 1.2
  findings for why).

**A gap 5b left.** The three `csv-tsv-res` CSV cases (`csv01`–`03`) were never
wired, because the catalogue did not accept `mf:CSVResultFormatTest`. They
now run over both subjects: 6 lines. The guard moved from 3 to 6.

**The ratchet: 2,721 lines, exemptions empty.** That is 2,525 from 5b, plus 6
for the CSV cases, plus 10 writer checks, plus 94 update cases, plus 86
`rdf-canon` cases. **There are no empty-graph exemptions.** No update case
depends on an empty graph existing, so SPARQL 1.1 Update §3.2's allowance
never had to be cited.

### The one-request-one-commit assertion

`UpdateRunner` reads the log's head position before and after each case.
- A request whose net delta is non-empty must advance it by exactly one.
- A request whose net delta is empty must not advance it.
- More than one commit fails the case.

It holds on all 94 cases. The reference-model property below asserts the same
over generated requests.

**Its failure path was proven by running it.** I planted the natural bug:
`SparqlUpdate.ExecuteAsync` committing each operation of a request
separately. The results:
- 6 update cases failed, which is every multi-operation request in the
  suite. For example: "DELETE INSERT 1c: Committed, but the log grew by 2
  commit(s)", and "INSERTing the same bnode with two INSERT WHERE statement
  within one request is NOT the same bnode: Committed, but the log grew by 6
  commit(s)".
- The reference property failed.
- 5 of the package's unit tests failed.

The plant was then reverted.

### Properties: iteration counts and counterexamples

| Property | Iterations a run | Counterexamples |
|---|---:|---|
| Update: requests through `Varve.Sparql.Store` against a term-level model's deltas through the commit API — the same state at every position, and the same commits | 2,000 | none |
| Writers: write then read gives back what the format carries — XML, JSON, CSV, TSV | 5,000 each | none |
| RDFC-1.0, IRI graph names: `iso(A, B) ⇔ canon(A) = canon(B)` against the backtracking check | 20,000 | one, fixed: language tag case |
| RDFC-1.0, blank graph names allowed: `canon(A) = canon(B) ⇒ iso(A, B)` | 20,000 | the full equivalence has one: the algorithm's (below) |
| RDFC-1.0: `canon(canon(A)) = canon(A)` | 20,000 | none |
| RDFC-1.0: the canonical form reads back as its dataset | 20,000 | none |

**Mutation checks, deliberate.** Each property was run once against a planted
fault before being trusted.
- The update property caught a `COPY` that did not clear its target.
- The writer property caught a broken quote escape.

**Language tag case (fixed).** `"x"@EN` and `"x"@en` are the same literal, and
the isomorphism check treats them so. The canonical form wrote them as given,
so two isomorphic datasets got two forms. The canonical form now lowercases
tags, as RDF 1.2 N-Triples §3 does (Working Draft of 24 September 2026).

**RDFC-1.0's own counterexample (not fixable here).** For datasets with blank
nodes as graph names, the property found isomorphic datasets that RDFC-1.0
gives different canonical forms. The smallest instance is in
`docs/spec/rdf-canon.md` §6, with the reason:
- A related hash records the related node's position, not the reference
  node's.
- A quad with blank subject, object and graph name can therefore hide which
  position the reference node took.
- §4.4.3 step 5.4 then leaves tied results in input order.

**pyoxigraph 0.5.11 gives the same two forms, byte for byte.** So the property
is split. With IRI graph names the equivalence holds in both directions and is
asserted. With blank graph names only equal-forms-implies-isomorphic is
asserted, and the counterexample is pinned as a test. No suite case has a
blank graph name.

### The results-format 1.2 findings

- **XML and JSON define both 1.2 additions**, and they are written and round
  tripped:
  - triple terms: `<triple>` in XML, `"type": "triple"` in JSON;
  - base direction: `its:dir`.
- **TSV**: `<<( s p o )>>` (draft §4.2) and `@lang--dir`. Both are written and
  round tripped.
- **CSV**: a triple term is `<<( s p o )>>` as text (draft §3.2).
  - **The draft says nothing about base direction.** The writer drops it, as
    CSV already drops language tags and datatypes. `sparql-results.md` §5.5
    tabulates what each format cannot carry.
- **Booleans in CSV and TSV** have no form in the Recommendation. The writer
  uses the `_askResult` convention that Oxigraph and Jena share, and says so.
- **Suite findings.**
  - The CSV expected files end lines with LF, where RFC 4180, whose record
    rules the format adopts, has CRLF. The writer writes CRLF and the check
    compares lines.
  - `tsv03`'s expected file writes the double `1.0e6` as the input wrote it.
    The store holds numeric literals in canonical form and writes `1.0E6`.
    They are equal as values, so the check re-reads rather than compares
    bytes.
  - JSON's whitespace is not canonical, so JSON is always compared by
    re-reading.

### Counterexamples and defects found, and by what

- **The allocation flake**: by looping the measurement under an allocating
  neighbour thread (above).
- **The canonicalisation work limit.**
  - What went wrong: first guessed at 12. It is a multiple of the blank
    nodes without a unique first-degree hash.
  - Found by: the suite. `test044`–`046` need 279.
  - Fix: the default is 1,000, recorded in ADR 0059 as a dated amendment note
    made before acceptance. A margin test holds the worst suite case × 3 ≤
    the default.
- **The rdf-canon guard counts.** First written as 65 and 22; the guard found
  64 and 21.
- **Language tag case**: by the isomorphism property (above).
- **RDFC-1.0 and blank graph names**: by the isomorphism property (above).
- **The three unwired CSV cases**: by wiring the writers against the same
  directory.
- **The benchmark project did not build.**
  - What went wrong: it links `EvaluationRunner.cs`, which this milestone
    made depend on `DatasetComparison.cs`.
  - Why no gate saw it: the benchmark project is outside the solution, so no
    CI job builds it.
  - Found by: building the benchmarks. Fixed in the benchmark commit.
- **dotNetRDF 3.5.2's RDFC-1.0 refuses any dataset of more than 1,000 blank
  nodes** ("Recursion limit reached").
  - Found by: the benchmark.
  - Measured by bisection: 1,000 unlinked blank nodes with distinct literals
    canonicalise, and 1,001 do not. RDFC-1.0 never takes the N-degree step
    for such nodes.
  - This is a dotNetRDF defect, not ours. The benchmark reports it and adds a
    1,000-blank-node shape so that dotNetRDF has a like-for-like row.

### Benchmarks, with the machine

**Machine and software:**
- Intel Xeon @ 2.10 GHz, 4 logical and 4 physical cores, 15 GiB.
- Ubuntu 24.04.4 LTS, kernel 6.18.44, a cloud container rather than
  dedicated hardware.
- .NET SDK 10.0.401, runtime 10.0.12, X64 RyuJIT `x86-64-v4`.
- BenchmarkDotNet 0.15.8.
- Baselines: dotNetRDF 3.5.2, and pyoxigraph 0.5.11 — Oxigraph through its
  Python binding, not the Rust library.

Not a gate (ADR 0027). Full method in `tests/Varve.Benchmarks/README.md`.
Each table starts with the row where Varve does worst.

**INSERT DATA of N quads into an empty store, parsing included** (median):

| N | Varve | dotNetRDF | pyoxigraph |
|---:|---:|---:|---:|
| 10,000 | 68 ms | 104 ms | **30 ms** |
| 100,000 | 475 ms | 2,078 ms | **392 ms** |

**pyoxigraph is faster: 1.2× at 100,000 quads and 2.2× at 10,000.**
- Of Varve's 475 ms, 143 ms is parsing.
- About 175 ms is the store's commit, using milestone 4's figure on this
  clock.
- About 155 ms is the executor: staging, the asserted set, sorting, and
  building the request. That part was not optimised this milestone.

Against dotNetRDF, Varve is 4.4× faster with 2.5× less allocation.

**DELETE/INSERT WHERE over a million-quad store**, moving 49,020 quads:

| | Time |
|---|---:|
| Varve | **186 ms** (mean ± 18) |
| pyoxigraph | 205 ms (median) |
| dotNetRDF | 1,747 ms (mean ± 43) |

Varve and pyoxigraph are within the noise of each other. Against dotNetRDF,
Varve is 9.4× faster.

**RDFC-1.0 of a 100,000-triple graph, SHA-256, to the canonical document:**

| Shape | Varve | dotNetRDF | pyoxigraph |
|---|---:|---:|---:|
| 12,500 blank nodes | **255 ms** | refused | 485 ms |
| 12,500, 250 pairs needing N-degree | **270 ms** | refused | 523 ms |
| 1,000 blank nodes | **187 ms** | 1,525 ms | 337 ms |

**The three engines' canonical documents are byte-identical** on every shape
that each engine completes. The twin shape needs a work limit between 21 and
25, against the default of 1,000.

### Smokes

- **Native AOT.** The published binary commits an update to an in-memory
  store and writes a query's results as JSON. It canonicalises a three-quad
  chain of blank nodes under SHA-256 and SHA-384. Each output is compared
  whole.
- **Browser WebAssembly** (headless Chromium 141) runs the same code, shared
  as `Update.cs`, and gets the same strings.
  - The browser's crypto table gains `IncrementalHash` SHA-256 and SHA-384.
    RDFC-1.0 hashes with both, and both work.
- **The update in both smokes is composed from the store and the evaluator**
  — pin, evaluate the `WHERE`, commit at the pinned position — rather than
  run through `Varve.Sparql.Store`.
  - The smoke apps are layer 5 hosts (ADR 0003), the package is layer 5, and
    VARVE0001 forbids a same-layer reference.
  - `none` is reserved for test and benchmark assemblies by name
    (VARVE0002), and a suppression needs a recorded exception, which none
    covers.
  - I did not settle this in passing; it is question 4 below.
  - `Varve.Sparql.Store`'s own AOT readiness is held by the trimming and AOT
    analyzers, which run on every build.

### Gates

- `dotnet run eng/ci.cs` passes all 20 jobs, including `pack` for the new
  package, `register` and `issue-refs`.
- Public API baselines exist for `Varve.Sparql.Store` and are extended for
  `Varve.Sparql.Results`, `Varve.Store` and `Varve.Rdf`.
- The `System.Uri` ban holds. The suite's `LOAD` handler resolves `file:`
  IRIs with `Varve.Iri` and a percent-decoder of its own, because
  `Uri.UnescapeDataString` is banned too.
- No new packages. CsCheck, already central, is referenced from the
  conformance project. The rdf-canon submodule is test data, not a package.
- Nothing in `tools/`.

### Proposed specification changes, not patched

1. **`docs/spec/n-triples.md`'s canonical form should become RDF 1.2's.** The
   two changes are:
   - lowercase language tags;
   - ECHAR for BS, HT, LF, FF, CR, `"` and `\`, and UCHAR for the other
     controls, DEL, U+FFFE and U+FFFF.

   RDFC-1.0's Appendix A already follows that form, and `CanonicalNQuads`
   implements it separately so that the N-Triples writer is untouched.
   (`rdf-canon.md` §8 question 2.)
2. **Raise RDFC-1.0's blank-graph-name counterexample with the RDF & SPARQL
   Working Group**, under ADR 0038 D2 as a `spec-gap`, with the instance from
   `rdf-canon.md` §6. The fix is probably to fold the reference node's
   position into the related hash (§4.7.3), which would change canonical
   forms. That makes it the WG's decision, not ours. (`rdf-canon.md` §8
   question 3.)
3. **Amend ADR 0057's execution steps.**
   - The ADR lists "release the pin" after "submit".
   - The specification (`sparql-update-store.md` §3) and the code release it
     first: the request is built as terms, the view is disposed, and then the
     commit is submitted. That way a pin is never held across the
     sequencer's queue (ADR 0052).
   - The ADR is accepted, so this is proposed, not edited.
4. **ADR 0003 and ADR 0005 disagree about hosts.**
   - ADR 0003 puts integrations and hosts both at layer 5.
   - ADR 0005 has hosts reference integrations.
   - VARVE0001 forbids exactly that.

   The smoke apps hit it now, and `Varve.Server` will at milestone 7. The
   options:
   - a layer 6 for hosts: `LayerDeclaration.HighestLayer` and an ADR;
   - `none` for hosts;
   - a recorded exception per host.
5. **Two suite findings upstream**, for the `rdf-tests` maintainers:
   - The `csv-tsv-res` expected CSV files use LF where RFC 4180 has CRLF.
   - The SPARQL 1.2 CSV draft is silent on base direction.
6. **dotNetRDF's 1,000-blank-node refusal** could be reported to dotNetRDF
   under ADR 0038, if the maintainer wants it.

### What milestone 6 needs decided about the file format

Milestone 6 has no W3C suite. Its gate is the storage specification's
properties against the file backend, so the format decisions are the whole
input. I need these decided before starting.

1. **The engine for `log/` and for `derived/`, decided separately.** Read
   `docs/research/managed-storage-engines.md` with this question:
   - Is `log/` a hand-written append-only segment file (the note's lean)?
   - Is `derived/` the same, or an embedded key-value engine? An engine is a
     package, and so an ADR.
2. **The synchronous cursor over asynchronous storage.** This is the risk
   carried from milestone 4. The three options:
   - page a checkpoint in before a scan;
   - a synchronous read path beside the asynchronous one;
   - an asynchronous `IQuadSource` cursor, which supersedes ADR 0022 and
     reaches the evaluator and `Varve.Sparql.Store`.

   This is the decision with the widest reach.
3. **Record framing and the version discriminator.** ADR 0014 requires the
   discriminator from the first byte written. Three questions:
   - What the header of a segment and of a record carries.
   - Whether the checksum is per record or per segment.
   - Where the discriminator lives.
4. **ADR 0045's provisional in-memory encoding: frozen as the durable byte
   layout, or replaced.** If frozen, the in-memory logs of milestones 4 and
   5 are readable by milestone 6 unchanged. If replaced, the memory backend
   changes with it.
5. **Segment size and durability per host.** Four questions:
   - What `fsync` policy each durability level of `DatasetOptions` means on a
     file.
   - Whether a commit is durable when its future completes, or at a group
     commit.
   - What the browser's equivalent is on OPFS.
   - Whether the browser backend is in milestone 6 at all or a slice of its
     own. I would ask for a slice: its storage is asynchronous-only and its
     failure modes differ.
6. **Q2 and Q3 (ADR 0013).**
   - Q2: bulk load against I2's effective deltas. Must a bulk load normalise
     against the head, or may it assert blindly into an empty dataset?
   - Q3: an overlay that does not fit in memory. This now has a concrete
     consumer: `Varve.Sparql.Store` holds a request's asserted and retracted
     sets in memory, so an update of a hundred million quads needs an answer
     or a stated limit.
7. **The erasure reservations in the first durable format**: the private id
   class (ADR 0012), the private entry layout `(KeyId, ciphertext)`, and the
   refusal of a key store path inside the dataset directory (ADR 0023). The
   roadmap already commits to these. What needs deciding is the private
   class's bit pattern and entry framing, so that milestone 9 changes no byte
   milestone 6 wrote.

And one decision from this milestone that milestone 6 inherits: **whether
the smoke apps and the server sit above the integrations** (proposal 4
above). The file backend's smoke will want to run an update through
`Varve.Sparql.Store`.

## Addendum, 2026-09-25 — after the merge

Recorded after pull request #34 merged, in the follow-up pull request, by
the same session. It covers the decisions the maintainer took on this
report.

**The upstream findings.** The maintainer asked for them to be filed
upstream. This session cannot post in third-party repositories, and ADR 0038
D3 has the filer review and own each report. So each finding has a Varve
tracking issue carrying its report, drafted and ready to file, and the
upstream URL goes on the tracking issue once it is filed:

| Finding | Upstream destination | Varve issue |
|---|---|---|
| RDFC-1.0: isomorphic datasets with blank graph names, different canonical forms | `w3c/rdf-canon` (WG, `spec-gap`) | [#36](https://github.com/Hafeok/Varve/issues/36) |
| `csv-tsv-res` expected CSV files end lines with LF; CSV/TSV §2 says CRLF | `w3c/rdf-tests` | [#37](https://github.com/Hafeok/Varve/issues/37) |
| The SPARQL 1.2 CSV draft is silent on base direction | the CSV/TSV 1.2 repository (`spec-gap`) | [#38](https://github.com/Hafeok/Varve/issues/38) |
| dotNetRDF 3.5.2 refuses any dataset of more than 1,000 blank nodes | `dotnetrdf/dotnetrdf` | [#39](https://github.com/Hafeok/Varve/issues/39) |

**INSERT DATA throughput** is [#35](https://github.com/Hafeok/Varve/issues/35),
in the operability milestone (#12). Its bar is pyoxigraph's 392 ms.

**A correction to "pyoxigraph gives the same two forms, byte for byte".**
Re-checking this for #36 found that for the two particular inputs pinned in
`CanonPropertyTests`, pyoxigraph gives one form, not two. The finding still
holds, and the evidence is now stated properly. Over 300 random relabellings
and quad orders:
- Varve gives two forms, 151 and 149 times.
- pyoxigraph gives the same two forms, 147 and 153 times.
- dotNetRDF gives one form every time, and it is neither of the two.

Which relabelling gets which form depends on each implementation's internal
order. `rdf-canon.md` §6 and the pinned test's comment now say this. The
sentence in the report above is left as written.

**The host question** is decided by ADR 0060: hosts at layer 6. The smoke
apps now run their update through `Varve.Sparql.Store`.

## Commits

1. `f51f97b` fix(test): read allocation where no collection or tier-up can move it
2. `4bf2484` docs(spec): the result writers, SPARQL Update over the store, RDFC-1.0
3. `1f580e7` docs(adr): 0057–0059, update as one commit, staging, canonicalisation
4. `213a1c5` feat(results): the four result-format writers
5. `8f9fd75` test(conformance): the writers against csv-tsv-res and json-res; the CSV cases
6. `a3f073d` feat(store): validators bound to the dataset, and the staging view
7. `ac87f32` feat(sparql-store): Varve.Sparql.Store — SPARQL Update as one commit
8. `9ab7a44` test(conformance): the W3C SPARQL 1.1 update evaluation suites
9. `7bbe4f7` feat(rdf): RDFC-1.0 canonicalisation, bounded by a work limit
10. `cabb35a` test(conformance): the rdf-canon suite, the canonicalisation properties, canonical comparison
11. `bc8e864` style(results): escape two characters a test held raw
12. `9275b16` test(smoke): update, results JSON and RDFC-1.0 under AOT and in the browser
13. `9b0d2c9` perf(bench): updates and RDFC-1.0 against dotNetRDF and pyoxigraph
14. docs: roadmap, README, AGENTS, changelog, the traceability record

## What this record does not contain

The transcript, the plan as presented before approval, and the intermediate
states of files. The maintainer holds the transcript.
