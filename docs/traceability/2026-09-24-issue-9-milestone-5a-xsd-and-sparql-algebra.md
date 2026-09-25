# Milestone 5a — XSD datatypes, the SPARQL algebra and parser

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#9](https://github.com/Hafeok/Varve/issues/9) |
| **Date** | 2026-09-24 |
| **Tool** | Claude Code 2.1.281, a cloud session |
| **Model** | `claude-fable-5-1` (Claude Fable 5.1), configured and served — confirmed from the session's own metadata, not from memory |
| **Session identifier** | `session_01XRAfH8gbwVQ1SvtYnSuo3o` |
| **Branch** | `claude/milestone-5a-xsd-algebra-nerbse`, landing on `main` as one pull request (ADR 0034's amendment) |
| **Commits** | 19 |

## The prompt

### First message

> Milestone 5 is the query engine. It is too large for one session, so it is three: 5a (this session) writes the milestone's ADRs, builds Varve.Xsd, and builds the SPARQL algebra and parser to a full pass of the W3C syntax suites; 5b builds the optimiser and evaluator to a full pass of the SPARQL 1.1 query evaluation suite over the in-memory projection; 5c builds the result formats and the update integration package. Read docs/brief.md, the specification, and every ADR before planning; AGENTS.md applies. Plan first, wait for approval; conventional commits, issue references, DCO sign-off; one PR at the end, merged by the maintainer.
> Another session may be working in tools/repo-standard/. Do not touch it. Pull before every push. ADR numbers: take the next free number when you start and renumber on collision.
> A. The five ADRs, first
> Decided at the close of milestone 4; write them, Accepted, each checked against every accepted ADR and the specification, with alternatives and consequences:
> Optimiser and evaluator. The optimiser's output is algebra, a rewrite algebra → algebra; there is no separate plan type. Optimiser and evaluator share one layer-3 package; the optimiser is a pass the evaluator applies by default and a caller can skip. This closes ADR 0003's open question 2; amend 0003. Name the packages: Varve.Sparql (layer 2: algebra and parser) and Varve.Sparql.Evaluation (layer 3), unless you find a reason to differ, stated in the ADR.
> Cardinality estimates. IQuadSource gains an estimate for a pattern that may return unknown; InMemoryDataset returns exact counts, the store returns run-derived estimates. Baseline entries in Varve.Rdf and Varve.Store. Implement it in this session; it is small and 5b needs it.
> Typed-value accessor. Beside the term handle, an accessor that yields the value of an inline numeric or boolean id without externalising, and reports "not inline" otherwise. Benchmark plan for ADR 0022's revisit condition: the SPARQL evaluation suite's timing plus a FILTER-heavy micro-benchmark, with and without the accessor, run in 5b. Implement the accessor in this session.
> Varve.Xsd scope. The XSD datatypes with SPARQL 1.1 operator semantics: exact xsd:decimal, the integer family, xsd:double and xsd:float per IEEE, xsd:boolean, xsd:string, the dateTime, date, time, gYear family and both duration types with the XML Schema 1.1 value spaces, canonical lexical forms for all of them, and value comparison per the SPARQL operator mapping (SPARQL 1.1 Query section 17.3). Varve.Xsd owns canonical forms: the store's private integer check is replaced by a call into it, which means Varve.Store references Varve.Xsd (layer 0, legal). Out of scope, stated: xsd:hexBinary, xsd:base64Binary and the derived string types beyond what SPARQL needs.
> Pinned read lifetime. One query execution. The evaluator receives a pinned source it does not own; the caller disposes it when the result stream ends; a configurable maximum lifetime with cancellation is the safety net, enforced by the server at milestone 7. Contract documented on Pin() and on the evaluator's entry point.
> B. Varve.Xsd
> Layer 0, no Varve dependencies. Specification first: docs/spec/xsd.md citing XML Schema Datatypes 1.1 sections for each value space and lexical mapping, and SPARQL 1.1 sections 17.3 and 17.4 for the operators.
> Exact decimal: your own implementation over System.Int128 or System.Numerics.BigInteger with a stated precision policy (Oxigraph uses a fixed 128-bit representation with 18 fractional digits; state ours and its overflow behaviour). No third-party numeric package.
> Date and time types with optional timezone, per XML Schema 1.1 section 3.3 and the seven-property model of section D; comparison with the partial order the spec defines, and the SPARQL operator's treatment of missing timezones.
> Durations: xsd:duration, xsd:dayTimeDuration, xsd:yearMonthDuration, with the comparison rules XML Schema gives.
> Parsing from ReadOnlySpan<byte> and ReadOnlySpan<char>; formatting to canonical lexical form into a Span; allocation-free for the numeric types.
> Property tests: parse then format then parse is identity on the value; canonical form is idempotent; comparison is a total order where the spec says total and a partial order where it says partial, with the incomparable pairs generated explicitly; arithmetic against BigInteger and decimal as oracles where ranges overlap.
> The W3C suites do not test Varve.Xsd directly; the SPARQL evaluation suite in 5b is its gate. Until then the property tests and a differential test against the XML Schema 1.1 examples from the specification text are the gate.
> C. SPARQL algebra and parser
> Varve.Sparql, layer 2. Specification first: docs/spec/sparql-algebra.md, stating the algebra per SPARQL 1.1 Query section 18 (translation of graph patterns, solution modifiers, property paths, aggregates, subqueries, VALUES, BIND, MINUS, SERVICE as a node the evaluator may refuse), with SPARQL 1.2 additions (triple terms in patterns, the new built-ins) present in the algebra from the start and marked; and docs/spec/sparql-grammar.md for the parser: grammar section references, error recovery and position reporting in the style of the Turtle spec, prefix and base handling through Varve.Iri.
> The algebra is an immutable tree of types in Varve.Sparql, with a visitor or pattern-matching surface that the optimiser can rewrite without reflection. Every node carries source positions.
> The parser covers SPARQL 1.1 Query and Update grammar (Update produces an update algebra; its evaluation is 5c's integration package). UTF-8 and UTF-16 input, errors with line, column and byte offset.
> A serialiser from algebra back to SPARQL text, so that parse–serialise–parse is an identity on the algebra; property-tested with generated algebra.
> Gates: the W3C SPARQL 1.1 syntax suites for query and update, positive and negative, wired into the conformance project and the ratchet with guard counts, plus the SPARQL 1.2 syntax suites if the current W3C manifests are stable enough to pin (check and report); the ratchet must gate them, and there must be no exemptions or each must be justified against a grammar section. Extend the conformance harness's subject abstraction to "parse this query" alongside "parse this document".
> Allocation is not zero here (an algebra tree is allocated by definition), but parsing must allocate only the tree: assert no per-token garbage beyond the tree by measuring two query sizes, as before.
> [HotPath] is not expected in the parser; say so.
> D. Gates that come due
> Public API baselines for the two new packages; the System.Uri ban holds.
> AOT and browser-wasm smoke extended: parse a query and print its algebra.
> The dependency register: no new packages expected; if the parser needs one, stop and justify it.
> Benchmarks, last and not gating: parse throughput on the syntax suite corpus against dotNetRDF's parser.
> Non-goals
> No evaluator, no optimiser, no result formats, no update execution, no server, no federation. No RDFC-1.0 (it goes to 5c with the result formats, or to its own slice; say which in the roadmap). Nothing in tools/.
> Report
> The traceability record and the PR body: the ADRs as written, the specs added, the public API as shipped against the sketch presented in the plan, suite counts per syntax suite with the ratchet total, the SPARQL 1.2 manifest status found, counterexamples found during development, benchmark numbers with hardware, proposed spec changes, and what 5b needs from the maintainer.

### The maintainer's decisions on the plan

The plan was presented with four questions — whether to wire the six SPARQL
1.2 syntax suites into the ratchet or hold them in an oracle corpus, whether
to wire the SPARQL 1.0 suites, `Int64` or `Int128` for the integer, and
whether to fix the dateTime comparison in ADR 0051 now or leave it to 5b.
The answer, verbatim:

> Approved as presented, with:
>
> 1. Wire all six SPARQL 1.2 syntax suites, ratcheted. The harness parses every suite at its own version (1.1 suites with Version 1.1); the 1.2 default is for API callers only. Add a test that a 1.1-negative, 1.2-positive case exists and is negative under 1.1.
> 2. Wire the SPARQL 1.0 syntax suites, exemptions citing the 1.1 production that relaxed each rule.
> 3. Int64. Out-of-range integer literals remain valid terms (lexical equality); only value operations fail as type errors. State it in 0051.
> 4. Fix in 0051: dateTime comparison is the implicit-timezone total order per SPARQL 1.1 §17.3 via op:dateTime-less-than (XPath F&O 3.1 §10.4), implicit timezone an evaluator setting defaulting to UTC. The XSD partial order stays available in Varve.Xsd. 5b verifies against the evaluation suite; disagreement is a superseding amendment.
>
> Records for the algebra are accepted because the round-trip property needs value equality; record in 0048 that this is the exception to milestone 4's avoidance of records and why.

## The report

### What was built

**Five ADRs**, 0048–0052, each Accepted and checked against every accepted ADR
and the specification, with alternatives and consequences; ADR 0003 amended by
a dated note closing its open question 2; the ADR index, `AGENTS.md`'s layer
table and the roadmap updated. 0048 names the packages `Varve.Sparql` (layer
2) and `Varve.Sparql.Evaluation` (layer 3) and records why the algebra's nodes
are records — the one exception to milestone 4's avoidance of them. 0051 states
`Int64` with out-of-range integer literals kept as terms, and the
implicit-timezone total order for dateTime with the XSD partial order kept
under its own name.

**`Varve.Xsd`**, layer 0, no dependencies, from `docs/spec/xsd.md`:
`XsdDecimal` as an `Int128` fixed-point with eighteen fractional digits and
overflow that fails rather than rounds; `XsdInteger` over `Int64` with the
derived types as range checks; `XsdDouble` and `XsdFloat` per IEEE with the
XSD lexical grammar; `XsdBoolean`, `XsdString`; the eight date and time types
on one seven-property representation with both orders; `XsdDuration` and its
two derived types with the four-reference partial order; `XsdNumeric` for the
operator mapping's promotion. Parsing from UTF-8 and UTF-16, canonical
formatting into a span, allocation-free on the numeric types. 213 tests:
CsCheck properties (round trips, canonical idempotence, total and partial
orders with generated incomparable pairs, arithmetic against `BigInteger` and
`decimal`, dateTime arithmetic against `DateTimeOffset`) and a differential
test over the XSD 1.1 text's examples.

**The store's canonical checks moved to `Varve.Xsd`** (ADR 0051): `TermIds`
asks `XsdInteger.IsCanonical` and `XsdBoolean.IsCanonical`; the inline set is
unchanged.

**`IQuadSource` widened by two members** (ADRs 0049, 0050), implemented in all
four sources. The store's estimate is a sum over runs of a prefix range per
run, exact because each run is an exact delta; the overlay adjusts its base;
the in-memory dataset counts. The accessor decodes the store's inline ids.
`Dataset.Pin()` documents the pinned read's lifetime (ADR 0052).

**`Varve.Sparql`**, layer 2, from `docs/spec/sparql-algebra.md` and
`docs/spec/sparql-grammar.md`: the algebra as sealed records with value
equality and a `SourceSpan` outside it, over an `AlgebraList<T>` with
element-wise equality; `AlgebraRewriter` with one virtual per node and no
reflection; a recursive-descent parser over UTF-8 with UTF-16 transcoded once,
producing the algebra directly per §18.3; SPARQL 1.1 Query and Update, and
SPARQL 1.2's triple terms, reified triples, annotations, `VERSION` and the new
functions, refused by production and version when the version in force lacks
them; `SparqlWriter` writing canonical text that parses back to the identical
tree.

**Conformance**: fifteen SPARQL syntax suites as a second list beside the RDF
document suites, each parsed at its own version, with pinned counts, a
UTF-8 / UTF-16 agreement test per case, the corpus round trip inside every
positive case, and a guard that the version gate refuses a 1.2 case under 1.1.
The baseline grew from 883 to 1,437 lines.

**Gates**: public API baselines for `Varve.Xsd` and `Varve.Sparql`, the widened
`Varve.Rdf` and `Varve.Store` baselines; no new dependency; `System.Uri`
unused; the AOT and browser smoke apps parse a query and an update, print the
algebra and parse it back; the allocation assertion; the benchmark below.

### The API as shipped, against the sketch

| Sketched | Shipped | Why |
|---|---|---|
| `GraphPattern` as the algebra's base | `QueryPattern` | `Varve.Rdf.GraphPattern` already names the graph half of a match pattern, and the evaluator will use both in the same files |
| `ImmutableArray<T>` children | `AlgebraList<T>` | `ImmutableArray<T>` compares by reference to its array; the round-trip identity needs element-wise equality, and a record cannot supply it for a member type |
| `Table` (VALUES), `Path` | `Values`, `PathPattern` | Names that say what they are beside `Varve.Rdf` |
| `Group` plus `Aggregation` and `AggregateJoin` terms | `Group(Inner, Keys)` with `AggregateExpression` left where it was written above it | The draft's `AggregateJoin` needs names for the aggregates that a serialiser would have to invent, and every name it could invent is one an author could write (`sparql-algebra.md` §4.6) |
| `Bound`, `If`, `Coalesce`, `In`, `Cast`, `TripleTermExpression` as nodes | `FunctionCall` with `BuiltInFunction` members; a cast is a `CustomFunctionCall`; a triple term in an expression is `FunctionCall(Triple)` or a constant | Fewer node types with the same information; the writer and the rewriter have one case per family |
| `SparqlParser.ParseQuery(utf8, in options)` with a `Try` form | `ParseQuery(utf8)`, `ParseQuery(utf8, options)`, `TryParseQuery(utf8, options, out query, out error)`, and the same for UTF-16 and for updates | The public API analyzer's rule against several overloads with optional parameters; `in` dropped because the options struct is two fields |
| `SparqlParseOptions { BaseIri; Version }` | The same, with the enum's default meaning 1.2 so that `default` is the ordinary call | |
| `SparqlParseError { Kind; Offset; Line; Column; Message }` | The same; `SparqlErrorKind` has fifteen members | |
| `Prologue(Base, Prefixes)` | `Prologue(Base, Prefixes, Version)` | The `VERSION` declaration is part of what was written and must survive the round trip |
| `Modify` expanding `WITH` | `Modify(With, Delete, Insert, Using, Where)` as written | Update §3.1.3's expansion is a dataset effect, not a `Graph` wrapping, and belongs to the executor at layer 5 |

`[HotPath]` appears nowhere in the parser, as the plan said it would not.

### The suites, per suite, with the ratchet total

| Suite | Version | Cases | Passing |
|---|---|---:|---:|
| `sparql10/syntax-sparql1` | 1.1 | 81 | 81 |
| `sparql10/syntax-sparql2` | 1.1 | 53 | 53 |
| `sparql10/syntax-sparql3` | 1.1 | 51 | 51 |
| `sparql10/syntax-sparql4` | 1.1 | 12 | 12 |
| `sparql10/syntax-sparql5` | 1.1 | 2 | 2 |
| `sparql11/syntax-query` | 1.1 | 94 | 94 |
| `sparql11/syntax-update-1` | 1.1 | 54 | 54 |
| `sparql11/syntax-update-2` | 1.1 | 1 | 1 |
| `sparql11/syntax-fed` | 1.1 | 3 | 3 |
| `sparql12/syntax-triple-terms-positive` | 1.2 | 113 | 113 |
| `sparql12/syntax-triple-terms-negative` | 1.2 | 65 | 65 |
| `sparql12/syntax` | 1.2 | 6 | 6 |
| `sparql12/version` | 1.2 | 9 | 9 |
| `sparql12/codepoint-escapes` | 1.2 | 9 | 9 |
| `sparql12/lang-basedir` | 1.2 | 1 | 1 |
| **SPARQL** | | **554** | **554** |
| RDF suites, unchanged | | 883 | 883 |
| **Ratchet total** | | **1,437** | **1,437** |

`exemptions.txt` stays empty. The plan expected a small set of SPARQL 1.0
negative cases relaxed by 1.1, each to be exempted against the 1.1 production;
there were none — every 1.0 negative case is still negative under the 1.1
grammar. The conformance project runs 4,385 tests: each case twice (the
verdict and the UTF-8 / UTF-16 agreement) plus the guards.

### The SPARQL 1.2 manifests' status

All six 1.2 suites are `dawgt:Proposed`. The Query draft is the Working Draft
of 21 September 2026 and the Update draft of 12 June 2026; the manifests' last
content changes date from February to June 2026. The 1.2 mixed manifests
(`codepoint-escapes`, `lang-basedir`) carry evaluation entries as well, which
are not enumerated; the pinned counts say how many syntax entries that leaves
(9 and 1). The draft's own open issues in §18 (226, 229, 230, 231) do not
affect the grammar; issue 226, the recursion of the path translation, is
handled by not applying that rewrite in the parser at all.

### Counterexamples and defects found, and by what

Each of these was found by a test written in this session, not by inspection.

1. **`DaysBeforeYear` counted the year's own leap day** (`Varve.Xsd`), so the
   366-day year-zero example failed. Found by the XSD 1.1 examples test; fixed
   to leap years in `[0, year)` with ceiling division.
2. **A simple predicate allocated a `PredicatePath` per triple** on its way to
   becoming a triple pattern. Found by the allocation assertion, which asked
   for equality and got 48 bytes per pattern more; the predicate position now
   reads the IRI first and enters the path parser only when an operator
   follows.
3. **`ORDER BY` on a `SELECT` alias at a grouped level was refused**, although
   the alias is in scope there by §18.3.5.1. Found by the AOT smoke app's own
   query. Fixed with a test; `HAVING` still cannot see one, by §18.3.4.2.
4. **Blank node labels were scoped per `Bgp` node**, so a collection with a
   path inside it (`syn-pp-in-collection`) could not be written back. Found by
   the corpus round trip. The scope is now the run of triples and paths, closed
   by a non-triples element — the reading the 1.0 suite's "OPTIONAL breaks
   BGP" cases and the path cases together require.
5. **A missing dot between two triples blocks was accepted** (`syn-bad-02`,
   `-03`). Found by the 1.0 suites.
6. **A union operand written bare merged with its neighbour**, and **the
   empty negated property set `!()`** was read as `NIL`, and **an update's
   `BASE` was lost after a semicolon**, and **a trailing `VALUES` over an
   empty `WHERE` kept an unsimplified `Join`**. All found by the round-trip
   property in its first runs, each reported with the offending text.
7. **`[]` inside a triple term was rejected**, against `[120]`. Found by the
   1.2 positive suite.
8. **The generator itself was wrong three times** — a `DatasetSpec` with no
   graphs, a `PathPattern` over a bare predicate, an implicit `Group` with no
   aggregate — each an image constraint the specification now states.
9. **A merged run in the store was not an exact delta** (`Run.Merge`, from
   milestone 4): a key the older run asserted and the newer retracted was kept
   as a retraction of a key nothing older held, and the reverse pair as an
   assertion of a key the oldest run already had. A lookup reads both the
   same; ADR 0049's count does not, and was one short or one over. Found by
   the estimate property (CsCheck seed `1zI0tBTNKoy3`) on the last run of
   the pipeline before the pull request, with the milestone otherwise
   complete — which is what a property over generated histories is for.
   Both pairs now cancel in the merge, and two named cases sit beside the
   property.

### Benchmarks

Not gating (ADR 0027). Run once, on the machine stated, from
`tests/Varve.Benchmarks/SparqlBenchmarks.cs`; the full report with the caveats
is the milestone 5a section of `tests/Varve.Benchmarks/README.md`, and the raw
BenchmarkDotNet output is under `BenchmarkDotNet.Artifacts/`.

**Machine.** Intel Xeon @ 2.80 GHz, 4 logical and 4 physical cores, 15 GiB,
Ubuntu 24.04.4 LTS, kernel 6.18, a cloud container. .NET SDK 10.0.401,
runtime 10.0.12, X64 RyuJIT `x86-64-v4`. BenchmarkDotNet 0.15.8, default job,
`MemoryDiagnoser`. Baseline: dotNetRDF (`dotNetRdf.Core` 3.5.2).

**Corpus.** The 215 positive query cases of the SPARQL 1.0 and 1.1 syntax
suites that both parsers accept (dotNetRDF rejects none), 15,415 bytes; the
120 positive cases of the six SPARQL 1.2 syntax suites, 13,386 bytes, Varve
only because dotNetRDF has no 1.2; and `syntax-update-2/large-request-01.ru`,
an `INSERT DATA` of 868 quads in 12,910 bytes. Each row parses its whole
corpus once.

| | Mean | Per case | Allocated | vs. dotNetRDF |
|---|---:|---:|---:|---:|
| Varve — 1.0 and 1.1 corpus | 761 µs ± 26 | 3.5 µs | 304 KB | **8.0× faster, 13.4× less memory** |
| dotNetRDF — 1.0 and 1.1 corpus | 6,065 µs ± 115 | 28.2 µs | 4,066 KB | — |
| Varve — the same, parse and write back | 866 µs ± 28 | 4.0 µs | 304 KB | no baseline |
| Varve — 1.2 corpus | 578 µs ± 15 | 4.8 µs | 324 KB | no baseline |
| Varve — large update | 985 µs ± 29 | 1.1 µs per quad | 474 KB | **4.5× faster, 5.1× less memory** |
| dotNetRDF — large update | 4,441 µs ± 69 | 5.1 µs per quad | 2,442 KB | — |

The caveats, in the order to read them: the corpus is fifteen kilobytes and
measures the grammar's dispatch on realistic query sizes, not bulk
throughput; the allocation is the returned tree at about 1.4 KB per query,
the parser's own working set being pooled, which the allocation assertion
checks; writing back costs about a seventh of parsing; the large update is
slower per byte than the queries because every one of its 868 terms is
resolved, validated and interned, and its ratio against dotNetRDF is
narrower for the same reason. ADR 0027's Oxigraph comparison remains owed.

### Proposed specification changes, not patched

None to the repository's own specifications beyond what this session wrote:
`sparql-grammar.md` §3.6 and `sparql-algebra.md` §6 were corrected in the same
commits as the findings above, and say so.

For the W3C drafts, three observations, none patched here:

- **SPARQL 1.2 Query §19.6** says a label may not be used "in two separate
  basic graph patterns", which the algebra makes stricter than the suites
  are: a path splits a BGP in the algebra and the positive cases put labels
  across the split. The reading this parser takes is written in
  `sparql-grammar.md` §3.6.
- **§18.3.2.5** (issue 226) leaves the sequence rewrite's recursion unstated;
  this parser does not apply it, and `ppeval` gives the unrewritten form the
  same meaning.
- **§19.5**'s prohibition on redeclaring a prefix is not tested by any suite;
  the parser accepts a redeclaration with last-wins, recorded as
  `sparql-grammar.md`'s open question 1.

### What 5b needs from the maintainer

1. **The aggregate representation.** The algebra keeps `AggregateExpression`
   where it was written above a `Group`; the evaluator performs the
   extraction of §18.3.4.1. If 5b would rather have the draft's
   `AggregateJoin` shape, it is a rewriter in `Varve.Sparql.Evaluation`, not a
   parser change — say which.
2. **The sequence-path rewrite** is left to the optimiser (`sparql-algebra.md`
   §4.4). Confirm that the evaluator's `Path(x, Seq, y)` is evaluated by
   `ppeval` directly, or that the optimiser rewrites it first.
3. **ADR 0051's dateTime order** is to be verified against the evaluation
   suite; disagreement is a superseding amendment, as the approval said. The
   implicit timezone is an evaluator setting defaulting to UTC — its name and
   place in the evaluator's options are 5b's to decide.
4. **ADR 0050's three-arm benchmark** needs an evaluator option that turns the
   accessor off and one that binds owned terms throughout; both are 5b's
   surface, and the verdict rule is fixed already.
5. **`SERVICE`**: the node exists and the evaluator refuses it; the error's
   shape is 5b's.
6. **ADR 0027's Oxigraph comparison** remains owed, as it was after milestone
   4; the parser benchmark above compares against dotNetRDF only.
7. **Blank node labels in the evaluator**: a `BlankNodePattern` is a variable
   that is never projected (`sparql-algebra.md` §3.1); `sparql-evaluation.md`
   should say so in those words.

## Commits

1. `cd3de76` docs(adr): 0048–0052, the milestone 5 positions as decisions
2. `f957090` docs(spec): xsd.md — value spaces, mappings and the operator semantics
3. `fd23462` feat(xsd): Varve.Xsd — the numeric types, boolean and string
4. `0c76b62` feat(xsd): the date and time family on the seven-property model
5. `a3ee090` feat(xsd): durations, and duration arithmetic on dateTime and date
6. `6d5da09` test(xsd): the properties that gate the package, and the XSD 1.1 examples
7. `14e49f1` feat(store): the canonical checks move to Varve.Xsd
8. `c462319` feat(rdf): cardinality estimates and an inline-value accessor on the quad source
9. `bcba772` docs(spec): the SPARQL grammar and algebra specifications
10. `84f2d06` feat(sparql): the algebra and the rewriter
11. `f5e39d0` feat(sparql): the query parser
12. `ffafa6a` feat(sparql): the update parser
13. `099439d` feat(sparql): the serialiser and the round-trip property
14. `7cd5ae3` feat(conformance): the SPARQL syntax suites, ratcheted at their own version
15. `f7adf8a` test(sparql): the allocation assertion, and the parser under Native AOT and in the browser
16. `354039f` perf(sparql): parse throughput against dotNetRDF
17. `f013709` fix(store): a merged run stays an exact delta
18. `7ac3794` docs: roadmap, README, changelog, the traceability record
19. after the pull request opened: test(sparql): the allocation assertion
    survives a trimmed array pool — the first CI run reported the parse 152
    bytes over on the devcontainer runner and 152 under on Windows, a
    16-element reference array from the shared pool's smallest bucket, which
    the pool drops from its thread-local cache on a gen-2 collection under
    memory pressure; the test no longer forces that collection and takes the
    least of three readings per side

## What this record does not contain

The transcript, the plan as presented before approval, and the intermediate
states of files. The maintainer holds the transcript.
