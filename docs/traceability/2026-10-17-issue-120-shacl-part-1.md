# Milestone 8, SHACL, part 1: the standalone validator

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The opening message, the
> brief, the plan as submitted and the approval are verbatim. The transcript
> itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#120](https://github.com/Hafeok/Varve/issues/120), the first part of milestone 8, SHACL ([#13](https://github.com/Hafeok/Varve/issues/13)) |
| **Date** | 2026-10-16 to 2026-10-17 |
| **Tool** | Claude Code 2.1.301 at the start, 2.1.302 at the end, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata (`configured_model`, `session_context.model`, `last_served_model`) |
| **Session identifier** | `session_013RaJN115u9ScUuoPGxvNGB` |
| **Branch** | `claude/wonderful-shannon-szjqwx`, from `main` at 9fbdaf3 (`v0.1.0-preview.7`) |
| **Machine** | a cloud container, 4 cores, 15 GiB, .NET SDK 10.0.401, runtime 10.0.12; the WebAssembly workloads installed in the session; Chromium 1194 (Playwright's build) for the browser tests |

## The prompts

### The message that started the session

> @"/root/.claude/uploads/9dc31ca3-0a09-5ff8-acb1-ef688d69d53f/cf2577df-varve-shacl-part1.zip" Milestone SHACL, part 1 (#120, under #13). The attached `varve-shacl-part1.zip` holds this session's prompt. Extract it into an empty directory outside the repository, then:
>
> 1. Read `varve-shacl-part1/varve-claude-code-shacl-part1.md` in full. It is the session prompt and governs this session; follow it as if I had pasted it here. Never commit the zip or the extracted folder.
> 2. Before planning, confirm: #87 is closed and its change is on `main`; #120 is open; and the identity hook has run and signs as the agent. If any of these fails, stop and tell me which one.
> 3. Then plan as the prompt says, answering its six plan questions, and wait for my approval before writing code.

### The brief, `varve-claude-code-shacl-part1.md`

> # Session: SHACL, part 1 — the standalone validator (#120, under milestone #13)
>
> Milestone 8 (#13) is split in two, as Client and integration was. This session lands part 1, the standalone validator, and closes #120. Part 2 lands later as its own release and closes #13: commit-time gating, incremental validation as a projection, the REC 4 gate, and the milestone close-out. Do not start part 2's work here, but design so that part 2 needs no change to what this part makes public.
>
> **Starting point.** Current `main`. #87 has landed, so the store answers `TryGetTripleTermComponents` and nothing blocks this milestone. Client and integration part 2 (#113) and Object storage (#116) are in flight. Neither touches what this session touches, except that #113 adds the event-declaration vocabulary that point 11 below writes shapes for: if #113 has not landed when you reach that slice, leave it for last.
>
> **Read before planning:**
> - issues #13 and #120, and ADR 0003 (its open question 1 is this milestone's to close);
> - ADRs 0017 (the validator contract and the overlay quad source) and 0058 (dataset validators and the staging view), because part 2 builds on them and part 1 must not paint it into a corner;
> - ADR 0048 (`Varve.Sparql.Evaluation`), the evaluator's property path code, and its `REGEX` implementation;
> - the shacl-rs specification and ADRs in the public repository `Hafeok/rust-shacl` (clone it read-only, outside the Varve repository). The brief says SHACL's semantics, the fixpoint treatment of recursion and those ADRs carry over: re-derive the implementation for .NET, never port it;
> - the W3C SHACL Recommendation (2017) and the SHACL 1.2 Core and SPARQL drafts of the Data Shapes Working Group as they stand today. Cite sections; where you are unsure what a draft says, say so.
>
> `AGENTS.md` applies in full. Plan first, and wait for approval. One PR, red only on `CS0618`. Its descriptor is at the next free `0.1.0-preview.N` when it lands (ADR 0102), names #120, closes it with a `Closes #120` trailer on the descriptor commit, and carries `accepted-by: mailto:emil@okkels-klein.dk` (the descriptor's line, `docs/releases.md` point 6). Use bare #N everywhere else, and no closing keyword in the PR description (#108). Your ADRs start at 0148, because #116 holds 0134–0142 and #113 holds 0143–0147. Land through `land/`.
>
> ## A. Decisions (Accepted unless stated; each becomes an ADR or a dated amendment)
>
> 1. **Layering: ADR 0003's open question 1 is closed by splitting the package.**
>    - `Varve.Shacl` (layer 3) holds SHACL Core: the shapes model, targets, paths, the core constraint components, the validation report, and a small contract for constraint components that the core does not define.
>    - `Varve.Shacl.Sparql` (layer 4) holds SHACL-SPARQL. It implements that contract over `Varve.Sparql.Evaluation` and references `Varve.Shacl`. It does not reference `Varve.Store`.
>    - Part 2's store integration is `Varve.Shacl.Store` at layer 5, the same pattern as `Varve.Sparql.Store`.
>    - The reason is the one ADR 0005 gave for the store: a caller who wants Core validation must not have to ship a query engine. The ADR records the alternative, one package at layer 4, and why it was rejected.
>    - A lower layer defines the contract and a higher one implements it, so nothing learns about a higher layer.
>
> 2. **The conformance baseline.**
>    - SHACL 1.0 Core and SHACL-SPARQL are gated by the W3C SHACL test suite: core and sparql, vendored, never fetched at test time.
>    - The suite runs under the existing conformance ratchet, with every failure exempted in `baseline/exemptions.txt` and justified.
>    - SHACL 1.2 Core additions are in the model from the start, as RDF 1.2 was. The plan lists which 1.2 features the current drafts define and which of them this part implements, citing sections.
>    - SHACL 1.2 rules (`sh:rule`) are not this milestone's; Inference (#95) owns them.
>
> 3. **Recursion.**
>    - SHACL 1.0 leaves validation with recursive shapes undefined (§3.4.3). Varve gives it the semantics the shacl-rs specification defines: the fixpoint treatment and its handling of negation through recursion. Restate them in Varve's own ADR, with the formal statement, and cite the shacl-rs ADRs they come from.
>    - If a shapes graph has no defined meaning under those semantics, the validator refuses it with a problem naming the cycle. It never returns a guessed report.
>    - A property test proves the fixpoint over generated recursive shapes graphs, including the negation case.
>
> 4. **Class targets and `sh:class` use the data graph's `rdf:type/rdfs:subClassOf*`, as SHACL defines a SHACL instance.** That is part of SHACL's semantics, not inference. `sh:entailment` other than simple entailment is reported as a failure, as the specification requires, until Inference (#95) states otherwise.
>
> 5. **Paths are evaluated in `Varve.Shacl`, against `IQuadSource`.** The layer rule forbids reusing the evaluator's property path code from layer 3. Implement paths here, and prove the two implementations agree: a differential property test that evaluates generated paths with both and compares the results, run from a test project that may reference both.
>
> 6. **One regular-expression translation.** `sh:pattern` and SPARQL `REGEX` both use XPath and XQuery Functions and Operators regular expressions and flags, and .NET's `Regex` differs from them. Move the evaluator's translation down to `Varve.Xsd` (layer 0), by ADR, so both packages use the same translation. Keep the evaluator's tests green across the move, and state any XPath feature the translation refuses. Check that it stays AOT- and trimming-safe and does not force `System.Text.RegularExpressions` to load where no pattern is used.
>
> 7. **The report is deterministic RDF.** For the same shapes graph and the same data, the report is the same bytes: blank nodes labelled by RDFC-1.0, and results in a stated order. Part 2's report projection, which is read as of any position, depends on this. The report is also available as a typed summary (conforms, plus result count by severity) without materialising the graph.
>
> 8. **Inputs.**
>    - The shapes graph and the data graph are any two `IQuadSource`s, or two graphs of one source.
>    - `owl:imports` in a shapes graph is not followed: nothing is fetched, and the caller composes shapes explicitly. The report states that imports were present and not followed.
>    - Ill-formed shapes (SHACL §2 and Appendix A's syntax rules) are refused before validation, with a problem that names the shape and the rule.
>
> 9. **A minimal public API.**
>    - Public: validate a data source against a shapes source with options, and read the report.
>    - The parsed shapes model stays internal. A Graphsite-driven milestone after 1.0, "SHACL shapes model", will make it public with real consumers; until then, it is internal so that 1.0 does not freeze it unused.
>    - Design it knowing it will become public: no types that leak the parser or the evaluator.
>
> 10. **Performance and hosts.**
>     - What runs per focus node or per value node is `[HotPath]`, and allocation per quad is a defect, as everywhere else.
>     - The validator runs in the browser host: add a browser smoke that validates a small dataset in headless Chromium, and an AOT smoke.
>     - Benchmarks with BenchmarkDotNet on a stated generated dataset, against dotNetRDF's SHACL validator on the same hardware (the brief names dotNetRDF as a comparison).
>     - dotNetRDF may also be a differential-testing oracle in a test project only, admitted by ADR with its licence and scope, never referenced from `src/`.
>
> 11. **Shapes that ship with Varve.** Write SHACL shapes for #93's event-declaration vocabulary (ADRs 0143–0147 once landed), as a resource of the server or the events package. Add a property test that the shapes and #93's code validator agree on generated declarations, valid and invalid. Whether the server then validates declarations by shapes instead of by code is part 2's decision. The management vocabulary's shapes come with the Management plane (#105), which does not exist yet.
>
> ## B. Definition of done
>
> - The W3C SHACL core and sparql suites pass, or each failure is exempted and justified, under the ratchet. The PR reports the counts.
> - The recursion property, the path differential property and the report-determinism property, each with iteration counts and any counterexample found.
> - The regex move with both packages' tests green.
> - The browser and AOT smokes.
> - The benchmark table against dotNetRDF, with the slower rows shown first.
> - The public API baseline diff in the PR body, which should be small.
> - The traceability record, and README Status, `docs/roadmap.md` and AGENTS.md's State updated for part 1. The milestone heading stays incomplete, because part 2 closes #13.
>
> ## Non-goals
>
> Commit-time gating, the validation projection, incremental maintenance, the REC gate (part 2, which also needs #92's vendored REC shapes and generator), `sh:rule` and SHACL-AF (Inference), a public shapes model (after 1.0), and fetching `owl:imports`.
>
> ## Plan questions
>
> 1. The package and type boundaries of point 1, checked against VARVE0003–0005.
> 2. The SHACL 1.2 features in scope, with section citations.
> 3. The recursion semantics as you restate them, and the shacl-rs ADRs they come from.
> 4. Which W3C suite cases you expect to fail, and why.
> 5. The regex move: what moves, and what the evaluator keeps.
> 6. ADR numbers from 0148, and any amendments (0003 certainly, and 0017 or 0058 if part 1 changes what part 2 can rely on).

### The starting point

All three conditions held at the first check: #87 closed, its change
(`22f0baf`, through #112) an ancestor of `main`; #120 open; the identity hook
run, signing as Claude (Emil) with key `SHA256:xDu5…DjY`. The plan says so in
its first paragraph.

### The plan, as submitted

> # SHACL part 1 (#120): the plan
>
> All three preconditions hold. #87 is closed, and its change (`22f0baf`, via #112) is an ancestor of `origin/main` at `9fbdaf3`. #120 is open, under #13. The identity hook ran: git signs as Claude (Emil) <claude+emil@okkels-klein.dk> with key `SHA256:xDu5…DjY`, and a test signature succeeded. The prompt is extracted in the scratchpad, outside the repository.
>
> #113 has already landed: `v0.1.0-preview.7` is on `main` and ADRs 0143–0147 are accepted. Point 11 is not blocked.
>
> There are two places where the prompt's premises don't match what I found. Both are covered under questions 3 and 5, and I need your decision on them before I start.
>
> ## 1. Package and type boundaries
>
> | Package | Layer | References | Holds |
> |---|---:|---|---|
> | `Varve.Shacl` | 3 | `Varve.Rdf`, `Varve.Iri`, `Varve.Xsd` | Shapes model (internal), targets, paths, Core components, recursion, report, and the extension contract |
> | `Varve.Shacl.Sparql` | 4 | `Varve.Shacl`, `Varve.Sparql.Evaluation`, `Varve.Sparql`, `Varve.Rdf` | SHACL-SPARQL as an implementation of that contract. No `Varve.Store` |
> | `Varve.Shacl.Store` (part 2) | 5 | Store, Shacl, Shacl.Sparql | `ICommitValidator`, the validation projection |
>
> **Checked against the rules.**
> - Every reference points strictly downward. Layer 4 already holds `Varve.Store` and `Varve.Events`, and `Varve.Shacl.Sparql` references neither, so there is no same-layer reference.
> - `VARVE0005`: each project declares `ArchLayer`. Neither package is an executable, so neither is a composition root.
> - `DD0001` checks reference direction. `Varve.Shacl.Sparql`'s public signatures mention `Varve.Shacl` types, so its csproj adds `Varve.Shacl` to `ArchContractTypeAssemblies`, as `Varve.Sparql.Evaluation` does for `Varve.Sparql`.
> - `VARVE0003`/`0004`: the per-focus-node and per-value-node code is `[HotPath]`. Closures get a `[DesignDecision]` citing the paths ADR, as `PathOperator.Search` does under ADR 0054.
> - Nothing at layer 3 learns about layer 4. There is no static registry: the caller composes the extensions into the options.
>
> **Public surface of `Varve.Shacl`, kept minimal:**
> - `ShapesGraph.Load(IQuadSource, GraphPattern, ShaclOptions)`: an opaque, immutable, thread-safe value. It returns either the shapes or the problems that refuse them.
> - `ShaclValidator.Validate(ShapesGraph, IQuadSource data, GraphPattern dataGraph, ShaclOptions)`: returns a `ValidationReport`, or a failure with its problems.
> - `ValidationReport`:
>   - `Conforms`, and `Summary`, which counts results by severity without materialising anything;
>   - `Results`, in the stated order, carried as `RdfTerm`s;
>   - `ToQuads()`, and `ToCanonicalNQuads()` for the deterministic bytes.
> - The extension contract, over `Varve.Rdf` types only:
>   - At load time, an extension is offered each shape (the shapes source and the shape's node). It returns constraints, or problems.
>   - Per focus node, a constraint is given the data source, the focus node and its value nodes, and writes results or a failure into a sink.
>   - SHACL-SPARQL reads `sh:sparql`, `sh:prefixes`, component parameters and the path from the shapes source itself, so no part of the model leaks.
>
> `ShapesGraph` exists so that part 2 can load shapes once and validate on every commit. Part 2 will need a focus-node restriction, for incremental validation, and dependency information. Both are **additive** public members, and no part 1 member changes for them. Shape constants (`sh:in`, `sh:hasValue` and the like) are internalised into the data source at each validation. A term the source doesn't hold simply never matches, which makes `QuadOverlay` and the staging view (ADR 0058) correct data sources with no special case.
>
> ## 2. SHACL 1.2 features in scope
>
> I read the Core Working Draft of 14 Oct 2026 and the SPARQL-Related Features Working Draft of 11 Oct 2026, with the editor's draft at data-shapes `4cd4698`. Section numbers are from the TR versions.
>
> **In scope:**
> - Value-type components (§7.1.1–7.1.3): lists for `sh:class`, `sh:datatype` and `sh:nodeKind`, plus `sh:TripleTerm`.
> - `sh:singleLine` (§7.4.4). The draft marks it at risk.
> - `sh:uniqueLang` with `rdf:dirLangString` (§7.4.6).
> - List components (§7.5.1–7.5.4): `memberShape`, `min`/`maxListLength`, `uniqueMembers`.
> - Property pair components over paths, and `sh:subsetOf` (§7.6, §7.6.3).
> - `sh:someValue` (§7.8.3).
> - `sh:reifierShape` and `sh:reificationRequired` (§7.8.5).
> - `sh:closed sh:ByTypes` (§7.9.1), `sh:rootClass` (§7.9.4), `sh:uniqueValuesFor` (§7.9.5).
> - Targets:
>   - `sh:ShapeClass` (§3.1.3.3), `sh:targetWhere` (§3.1.3.6), and `sh:shape` (§3.1.3.7).
> - Per-constraint severity, message and deactivation through reifiers, and `sh:Trace`/`sh:Debug` (§3.1.4–3.1.6).
> - Constant node expressions only (§5.1–5.2), at `sh:targetNode` and `sh:deactivated`.
> - The subclass-graph option (§6.4).
> - `sh:conformanceDisallows` (§6.8.1.2) as an option.
> - In the SPARQL document: `sh:severity` on SPARQL constraints, the annotation properties (§5), and prefixes from `sh:ShapesGraph`.
>
> **Out of scope:**
> - The Node Expressions spec as a whole, including `sh:expression` and `sh:nodeByExpression`.
> - `sh:values` and `sh:defaultValue`.
> - SPARQL node expressions and functions (SPARQL §6–7).
> - `sh:SPARQLTarget`, which 1.2 drops.
> - `owl:imports` closure (§6.1).
> - `sh:used*Graph`.
> - `sh:rule` (#95).
> - §6.8.3, incomplete data: I haven't read it closely enough to say what it requires, so it is out until I have.
> - §8, non-validating characteristics: read and ignored.
>
> ## 3. Recursion — **the prompt's premise doesn't hold**
>
> The shacl-rs specification (`shacl-rs-functional-spec.md`, Draft v0.8 at `c0c4873`) defines no fixpoint semantics for shapes, and nothing about negation through recursion:
> - **ADR-002 and §9.1:** v1 **rejects every recursive shapes graph**. The check is a cycle search over the shape-reference graph: an edge for each shape-expecting parameter, with no polarity.
> - **§9.2:** a deferred "L3" sketch. It names the supported (Y-stratified) semantics from arXiv 2108.13063 and calls it "the least fixpoint of the one-round operator". That operator is not monotone once `sh:not` is involved, and the sketch defines neither polarity nor strata.
> - **The shipped code doesn't enforce the rejection.** Its cycle check is never called, and a depth cut-off at 1024 silently assumes conformance.
> - **Paths are the only rigorous fixpoint content** (§4.1, a least fixpoint by Knaster–Tarski). I will cite that.
>
> So I can't "restate shacl-rs's semantics with negation". **I propose Varve's own, stated formally in the ADR, and recorded as a departure from the brief's "carry over":**
> - **Dependency graph.** This is "refers to" from 2017 §3.4.3 / 1.2 §6.6.3, including `memberShape`, `reifierShape`, `someValue` and `targetWhere`, with a polarity on each edge:
>   - **positive:** `node`, `property`, `and`, `or`, `someValue`, `memberShape`, `reifierShape`, and `qualifiedValueShape` under `qualifiedMinCount`;
>   - **negative:** `not`, and `qualifiedValueShape` under `qualifiedMaxCount`;
>   - **both:** `xone`, and siblings under `qualifiedValueShapesDisjoint`;
>   - **`targetWhere`** must point to a strictly lower stratum.
> - **Stratification.** Any cycle with a negative edge, or a `targetWhere` edge on a cycle, makes the graph unstratifiable. It is refused before validation, with a problem that names the cycle as an ordered list of shapes and edges.
> - **Semantics.** Within each stratum, the assignment σ: shapes×nodes→{true, false} is the **greatest fixpoint** of the one-round operator, with lower strata fixed. That operator is monotone in σ when edges are positive, so Knaster–Tarski gives a unique answer.
>   - The greatest fixpoint is the maximal-typing reading that ShEx uses. Under it, "a Person knows only Persons" conforms on cyclic data, where a least fixpoint would wrongly fail.
>   - Refusal is static, decided on the shapes graph alone. A commit can therefore never make a shapes graph refusable, and part 2 depends on that.
>   - It also covers every graph without recursion, where it coincides with the 2017 semantics.
> - **Sources to cite:**
>   - Corman, Reutter and Savković (ISWC 2018), for the supported semantics.
>   - Andreşel et al. (WWW 2020), for stable models.
>   - arXiv 2108.13063, the paper shacl-rs cites. I believe it is Bogaerts and Jakubowski's fixpoint-semantics paper, but I haven't verified that, and I will check before citing it.
> - **The property test** compares the validator against a naive oracle on generated small recursive graphs: iterate from all-true over every pair, stratum by stratum. It also checks that every unstratifiable graph is refused, naming a real cycle.
>
> Alternatives: reject all recursion, which is what shacl-rs v1 says. Or well-founded semantics, which defines more graphs, but then refusal depends on the data.
>
> ## 4. W3C cases expected to fail
>
> The suite is `w3c/data-shapes`, vendored as a submodule like `rdf-tests` and pinned at `4cd4698`, under the W3C Software and Document License. 1.0 has **120 approved cases: 98 core and 22 sparql**. They run over two subjects, `dataset` and `store`, using the suite's full-compliance comparison (normalise, then compare as isomorphic graphs).
>
> **Expected exemptions**, if 1.2 wins where the two versions conflict:
> - `pre-binding-006` and `unsupported-sparql-004`: 1.2 removes the sub-SELECT restriction (Issue 159).
> - `shapesGraph-001`: `$shapesGraph` and `$currentShape` are removed in 1.2 (Issue 426), and the shapes and the data are separate sources here.
>
> **At risk, still to be decided case by case:**
> - `path-strange-001` and `002` (list-shaped paths with extra triples; dropped from the 1.2 suite).
> - `path-unused-001`, which could be hit by over-eager ill-formedness refusal.
> - `sparql/component/validator-001`, which has `owl:imports` of dash and should pass if nothing from dash is needed.
> - `core/complex/shacl-shacl`.
>
> **I predict at least 115 of 120 pass per subject.** The 1.0 suite has no recursive cases and no `sh:entailment` cases.
>
> ## 5. The regex move — **bigger than a move**
>
> `XPathRegex` (`src/Varve.Sparql.Evaluation/Expressions/XPathRegex.cs`) translates only the flags and the `REPLACE` replacement string. The pattern body goes to .NET unchanged. So the following divergences exist today:
> - `.` matches `\r`.
> - `$` matches before a final `\n`.
> - `\w` differs: .NET excludes symbols (`\p{S}`) that XPath includes.
> - `\i`, `\c`, `\I` and `\C` throw.
> - .NET-only syntax is accepted silently: lookaround, named groups, `\b`, `\A`, and inline `(?i)`.
>
> **What moves to `Varve.Xsd`:**
> - A real XPath / F&O 3.1 §5.6 regex translator: a parser that emits .NET syntax.
> - The flags `s`, `m`, `i`, `x` and `q`.
> - The replacement translation.
>
> It refuses with FORX0002 everything XPath forbids. Block names .NET lacks are stated in the ADR as refused.
>
> **What the evaluator keeps:**
> - the per-execution cache;
> - `EvaluationOptions.RegexTimeout`;
> - the mapping to `Value.Error`;
> - FORX0003 for `REPLACE`.
>
> **Loading:** translation returns Varve's own flags struct. The `Regex` is built in a separate method, so `System.Text.RegularExpressions` is touched only when a pattern is compiled. There is no `Compiled` and no `NonBacktracking`, since backreferences are needed. The AOT smoke gains a `REGEX` query.
>
> **Consequence:** a `REGEX` that happens to work today with .NET-only syntax becomes an error.
>
> ## 6. ADRs from 0148, and amendments
>
> | ADR | Decision |
> |---|---|
> | 0148 | `Varve.Shacl` (layer 3) / `Varve.Shacl.Sparql` (layer 4), the extension contract. Closes 0003's open question 1. Rejected alternative: one package at layer 4 |
> | 0149 | Conformance baseline: the 1.0 suites vendored and ratcheted; the 1.2 scope above; 1.2 wins where 1.0 and 1.2 conflict |
> | 0150 | Recursion semantics, as in question 3 |
> | 0151 | Inputs and failures: SHACL instances through `rdf:type/rdfs:subClassOf*` in the data graph; non-simple `sh:entailment` is a failure; `owl:imports` not followed and reported; ill-formed shapes refused |
> | 0152 | Paths in `Varve.Shacl` (set semantics) and the differential property against the evaluator |
> | 0153 | The XPath regex translator in `Varve.Xsd` |
> | 0154 | The deterministic report: RDFC-1.0 under a stated work limit; result order by canonical term order of (focus, path, shape, component, value); the typed summary |
> | 0155 | Minimal public API; internal shapes model, designed to become public |
> | 0156 | `dotNetRdf.Shacl` 3.5.2 (MIT, depends on `dotNetRdf.Core` 3.5.2, already registered under 0027): benchmarks and a differential oracle in one test project, never `src/` |
> | 0157 | Shapes for the event-declaration vocabulary, shipped as a Turtle resource of `Varve.Events`, with no reference to `Varve.Shacl` |
>
> **Dated amendments:**
> - **0003:** open question 1 is closed.
> - **0051:** `Varve.Xsd`'s scope gains the regex translator.
> - **0007:** the new suite family and its submodule.
> - **0027:** the SHACL comparison.
> - **0017 and 0058:** neither needs an amendment, since part 1 changes nothing part 2 relies on.
>
> `docs/vocabulary.md` gains `varve:importNotFollowed`. The suite's comparison keeps only listed predicates, so an extra triple in the report is harmless.
>
> **Point 11 limit:** shapes can't express every check the code makes. The SELECT has to parse, the subject variable has to occur in the SELECT, and the class-taken and version-history checks span graphs. So the agreement property will be: anything the code accepts conforms to the shapes; anything the shapes reject, the code rejects; and the two are equivalent on the problem kinds that shapes can express. The ADR lists the rest.
>
> **Testing and hosts:**
> - Test projects: `Varve.Shacl.Tests`, `Varve.Shacl.Sparql.Tests` (which holds the path differential), and a new `ShaclSuite` in the conformance project.
> - Browser smoke: added to `Varve.Store.BrowserTests`, which already runs in headless Chromium in CI. `WasmSmoke` is built in CI but never run.
> - Benchmark dataset: generated persons and organisations at 10k, 100k and 1M triples, with about 12 shapes using common components.
>
> ## Decisions I need from you
>
> 1. **Recursion:** stratified with a greatest fixpoint and static refusal (my recommendation), reject all recursion, or well-founded semantics?
> 2. **Where 1.0 and 1.2 conflict:** follow 1.2, with three justified exemptions in the 1.0 suite?
> 3. **The 1.2 suite:** also wire it (162 core and 43 sparql cases, same submodule) under the ratchet? It would measure the 1.2 features in scope, with out-of-scope cases exempted. I recommend yes.
> 4. **Regex:** accept the full translator, and the evaluator becoming stricter about .NET-only syntax?
> 5. **Event shapes:** ship them as a resource of `Varve.Events`?
>
> I'll wait for your approval before writing any code.

### The approval of the plan

> Approved. Your five decisions:
>
> 1. **Recursion: stratified, greatest fixpoint within each stratum, static refusal.** Yes. You're right that shacl-rs defines no fixpoint semantics for shapes; record the departure from the brief's "carry over" in ADR 0150. Also:
>    - Show the cost of the greatest fixpoint with one example: a self-supporting cycle conforms ("a Person knows some Person", with `a knows b knows a`). State that this is the maximal-typing reading ShEx uses, and why we prefer it to the least fixpoint.
>    - Verify the arXiv 2108.13063 citation before you cite it; drop it if it isn't what you think it is.
>    - Note in the record, as a finding, that shacl-rs's cycle check is never called and its depth cut-off assumes conformance. I'll handle that in its own repository.
> 2. **Where 1.0 and 1.2 conflict, follow 1.2,** with the three exemptions justified. ADR 0149 pins the draft dates and the data-shapes commit you read, and says we revisit when SHACL 1.2 reaches Candidate Recommendation. A draft can move, and the pin makes that visible.
> 3. **Wire the 1.2 suite** as well, under the ratchet, with out-of-scope cases exempted by name and reason. Yes.
> 4. **The full regex translator in `Varve.Xsd`:** yes, with three conditions:
>    - The W3C SPARQL query and update suites stay exactly as they are. Any case that changes is reported, not exempted silently.
>    - The descriptor's notes list the evaluator becoming stricter as a breaking change, with the .NET-only constructs that now error.
>    - `ShaclOptions` gets a regex timeout, as the evaluator has. `sh:pattern` runs shapes and data that may both be untrusted, so a pathological pattern must fail the validation with a problem, not hang it.
> 5. **Event shapes as a Turtle resource of `Varve.Events`:** yes.
>
> ## Three further conditions
>
> - **Data blank nodes in the report keep their identity.** A focus node or value node that is a blank node in the data must appear in the report with the label the data source gives it (ADR 0044's handle-derived label), not a label RDFC-1.0 assigns to the report on its own. Otherwise the report can't be joined back to the data, and part 2's as-of report graph would rename data nodes between positions. Canonicalise only the report's own result nodes. Add a test where two focus nodes are data blank nodes and the report, joined to the data, names the right ones.
> - **Shape constants are looked up, never added to the source.** Resolve `sh:in`, `sh:hasValue` and the like to handles by lookup only. A term the source doesn't hold never matches, as you say, and the validator never allocates a term in a data source's dictionary. A test should show that validating against a store leaves its dictionary unchanged.
> - **§6.8.3, incomplete data:** read it before the PR. Either implement it or exempt it with a stated reason in ADR 0149. "Not read" can't stay in an ADR.
>
> The rest stands: the boundaries and contract in question 1, the 1.2 scope in question 2, ADRs 0148–0157 with the amendments you list, the dotNetRDF test-only oracle, and the Chromium smoke in `Varve.Store.BrowserTests`.

## The report

### What shipped, by ADR

- **0148, the packages.** `Varve.Shacl` at layer 3 references `Varve.Rdf`,
  `Varve.Iri` and `Varve.Xsd`, and no SPARQL. `Varve.Shacl.Sparql` at layer
  4 implements the extension contract in `Varve.Shacl`
  (`IShaclExtension`, `IShaclConstraint`) over `Varve.Sparql.Evaluation`,
  and references no store. ADR 0003's open question 1 is closed by it, as a
  dated amendment.
- **0149, the baseline.** SHACL 1.2 Core (Working Draft of 2026-10-14) and
  SPARQL-Related Features (2026-10-11), the data-shapes repository at
  `4cd469867a1cc6d2fff80e8a40723b26c695d67a`, 1.2 followed where it conflicts
  with 2017, revisited at Candidate Recommendation. §6.8.3, incomplete data,
  was read: it leaves the results unchanged, and the ADR says why.
- **0150, recursion.** Stratified, each stratum the greatest fixpoint, a
  cycle through negation or a target refused statically with the cycle
  named. The cost is shown by the approval's example, and the citations were
  checked: Corman et al. (ISWC 2018), Andreşel et al. (WWW 2020), Bogaerts and
  Jakubowski (EPTCS 345), ShEx 2.1's complete typing. arXiv 2108.13063 is
  Pareti et al., *Satisfiability and containment of recursive SHACL*, and is
  cited for that and not for the semantics.
- **0151, inputs and failures.** Shapes and data are quad sources. A shape
  constant is looked up, never added. `owl:imports` is reported as
  `varve:importNotFollowed`, never fetched. Simple entailment only. A failure
  is a `ShaclProblem`, never a result. `sh:pattern` runs under
  `ShaclOptions.RegexTimeout`.
- **0152, paths.** `Varve.Shacl` evaluates SHACL paths itself, as sets, held
  to the evaluator's property paths by a differential property; one
  divergence by construction is stated (below).
- **0153, regular expressions.** The full XPath translator in `Varve.Xsd`,
  shared by `sh:pattern` and the evaluator's `REGEX` and `REPLACE`, which are
  stricter for it (breaking, listed in the descriptor).
- **0154, the report.** A value with its results in a stated order and kept
  with their duplicates. Canonical by RDFC-1.0 over the report's own nodes;
  the data's blank nodes keep their labels.
- **0155, the API.** `ShapesGraph`, `ShaclValidator`, `ShaclOptions`, the
  report model in `Varve.Shacl.Model`, and the extension contract. The shapes
  model is internal.
- **0156, dotNetRDF.** `dotNetRdf.Shacl` 3.5.2, MIT, test-only: the benchmark
  baseline and a differential oracle.
- **0157, the event declaration shapes.** `EventDeclarationShapes.Turtle` in
  `Varve.Events`, with the agreement property.
- **Amendments** of 0003 (open question 1 closed), 0007 (the SHACL suites in
  the harness), 0027 (the second dotNetRDF package) and 0051 (the
  translator in `Varve.Xsd`), each dated 2026-10-17.
- **`shacl-hot-path-scope`**, a decision set with no ADR, as the evaluator's
  and the store's are: what the per-focus and per-value path keeps after
  `VARVE0003`.

### Where the work departed from the plan or the decisions, and why

- **ADR 0154: results are kept with their duplicates.** The first draft
  deduplicated equal results. The suites expect one result node per result
  (§7: "new result nodes"), and `lessThan` reports one result per failing
  pair. The ADR was revised before it was filed, and a test with 400 equal
  results shows RDFC-1.0 handles them.
- **ADR 0155: no convenience overload.** The plan had a
  `Validate(string shapes, string data)` overload; the analyzers' primitive
  rules and the decision for a minimal surface removed it.
- **ADR 0157, point 2, corrected.** The plan, and the ADR as first written,
  said field names would be unique within a type by `sh:uniqueValuesFor`.
  §7.9.5 compares a node with every target of the shape, so on the field
  shape it would forbid two types from each having a field `id`. The rule
  moved to what only the code checks. Within-graph class uniqueness, which
  `sh:uniqueValuesFor` can say, was added. Point 2 now lists, line by line,
  what was checked against docs/spec/events.md §3.2 and `EventDeclarations`.
- **ADR 0152, a divergence stated.** The path differential found, on its
  first run, that from a focus node the data graph does not hold, a sequence
  through two zero-length steps reaches the node in SHACL and nothing in
  SPARQL, whose algebra makes the second step variable to variable over the
  graph's nodes. The ADR keeps SHACL's reading and states the divergence; the
  property checks it exactly. The maintainer is asked below.
- **The hot-path pass was larger than planned.** Marking
  `Constraint.Validate` held every component to `VARVE0003`: 190 findings.
  Most were fixed, among them three real per-value allocations
  (`sh:datatype` decoded the datatype IRI for every value, `sh:minLength`
  decoded every value, `sh:uniqueLang` built a string per value), and
  `sh:uniqueValuesFor` stopped being quadratic in the targets. What remains
  is answered by `shacl-hot-path-scope`, filed unaccepted.
- **The benchmark measures the store's view, not `InMemoryDataset`.**
  `InMemoryDataset` answers every match by a scan ("this source has no index
  to consult"), so validation over it is quadratic in the data: 200 ms
  against 17 ms at 10,000 triples. The row was dropped. Its agreement is
  still checked at the smallest size.

### Findings outside this repository

- **shacl-rs** (Hafeok/rust-shacl at `c0c4873`, `shacl-rs-functional-spec.md`
  Draft v0.8). Its cycle check, `shape_ref_cycle`, **is never called**:
  nothing in `validate`, `validate_turtle`, `validate_store` or the test-suite
  runner calls it, though the `validate` doc comment says callers needing
  reject-on-recursion (its ADR-002) should consult it first. So its own
  REQ-REC-1, "no silent partial validation", is not enforced by default. The
  engine's only guard is `MAX_DEPTH = 1024`: past it, `validate_focus_collect`
  returns no results and the nested `conforms(...)` returns true. **A cut-off
  that assumes conformance** is an unsound optimistic answer, greatest-fixpoint
  like for positive references, arbitrary under `sh:not`, and potentially
  exponential before it is reached. The maintainer handles it in that
  repository.
- **dotNetRDF** orders pairs that SPARQL's `<=` cannot compare, such as
  (`"x"@fr`, `"x"@fr`) and (`"abc"@en`, `"abc"`). So it reports no
  `sh:lessThanOrEquals` result where SHACL 1.2 Core §7.6.5 requires one.
  Varve's evaluator gives SPARQL's type error, and Varve.Shacl the result.
  The oracle's generator leaves the two components out, with the reason in
  the test; the suite covers them.

### Conformance

| Suite | Lines | Passing | Exempt |
|---|---:|---:|---:|
| `data-shapes-test-suite` (SHACL 2017), `core`, × 2 subjects | 196 | 196 | 0 |
| `data-shapes-test-suite` (SHACL 2017), `sparql`, × 2 subjects | 44 | 36 | 8 |
| `shacl12-test-suite` (1.2 draft), `core`, × 2 subjects | 324 | 324 | 0 |
| `shacl12-test-suite` (1.2 draft), `sparql`, × 2 subjects | 86 | 66 | 20 |
| **SHACL** | **650** | **622** | **28** |

The two subjects are `InMemoryDataset` and the store's pinned view. The
fourteen exempt cases are four where 2017's pre-binding rules and 1.2's
disagree, and ten that use SPARQL-based node expressions, out of scope beyond
constants. The ratchet's total is **6,141 in the baseline, 40 exempt, 0
regressed**. Across the regular expression move the SPARQL query and update
suites were unchanged: 5,519 in the baseline, none regressed and none newly
passing.

### Properties and tests

| Property | Per CI run | Long run | Counterexamples |
|---|---:|---:|---|
| Recursion: conformance to generated positive recursive shapes is the naive greatest fixpoint | 2,000 | 50,000 | none. A mutated oracle (existentials not self-supporting) failed within 2,000 (seed `97TyznaUDhi1`), so the property tells the greatest fixpoint from the least |
| Report determinism: the same data inserted in any order gives the same canonical bytes, under the same labels | 1,000 | 20,000 | none |
| Path differential: SHACL paths against `SELECT DISTINCT ?o { n path ?o }` | 2,000 | 100,000 | one, on the first run (seed `1ApdSFS1VMn4`), the divergence ADR 0152 now states; none after |
| dotNetRDF oracle: `sh:conforms` and the set of (focus, shape, component) | 1,000 | 20,000 | before the exclusion, only `sh:lessThanOrEquals` over incomparable pairs (seed `4revtf8fvAO4`), judged above; none after |
| Event declaration shapes against `EventDeclarations` | 2,000, and 31 named faults | 50,000 | none |

Example tests: ADR 0150's cycles, conforming and refused; two data blank focus
nodes joined back to the data; a store validated and its dictionary unchanged,
through a pinned view and a staging view; an ill-formed shape, an entailment
regime, imports, a pattern past the timeout, a missing extension; 400 equal
results canonicalised.

### The smokes

- **Native AOT.** `tests/Varve.AotSmoke` published with
  `IlcTreatWarningsAsErrors`, no IL warning. The binary validates a pinned
  view against shapes with an XPath pattern, a recursive `sh:node` and a
  SHACL-SPARQL constraint, and runs `REGEX` and `REPLACE` with XPath class
  subtraction: six results, and the report's canonical SHA-256
  `b5b5b728…06f5`.
- **Chromium.** The same file, linked into `Varve.Store.BrowserTests`, in
  headless Chromium on the worker (41 checks passed) and on the page's
  thread (23 passed). It printed the same answers and the same hash.

### Benchmarks against dotNetRDF

On the machine above, at the three sizes, with `dotNetRdf.Shacl` 3.5.2, ordered by how close Varve came; **no row is slower**. The workload is in `tests/Varve.Benchmarks/README.md`. Both engines gave the same verdict and result count at every size before timing.

| Triples | Varve, store view | dotNetRDF | Varve's time | Allocated, Varve | Allocated, dotNetRDF | Results, both |
|---:|---:|---:|---:|---:|---:|---:|
| 10,000 | 32.4 ms ± 0.4 | 1,215.3 ms ± 112.5 | 0.03× (37× faster) | 6.33 MB | 650.05 MB | 201 |
| 1,000,000 | 1,759.3 ms ± 90.4 | 131,750.9 ms ± 2,077.8 | 0.013× (75× faster) | 845.89 MB | 65,099.48 MB | 20,061 |
| 100,000 | 84.3 ms ± 15.4 | 12,288.7 ms ± 316.6 | 0.007× (146× faster) | 72.82 MB | 6,511.92 MB | 2,001 |

### Public API

New packages: `Varve.Shacl` (108 lines in `PublicAPI.Unshipped.txt`) and
`Varve.Shacl.Sparql` (3). Changed: `Varve.Xsd` gains `XPathRegex`,
`XPathRegexTranslation` and `XPathRegexError` (24 lines); `Varve.Events` gains
`EventDeclarationShapes` and its `Turtle` (2 lines). Nothing is removed from
a shipped surface. The pull request carries the diff.

### The pipeline

`dotnet build` with `CS0618` as a warning is clean. The only errors with the
default settings are `CS0618`, the unaccepted decisions. The tests of
`Varve.Shacl`, `Varve.Shacl.Sparql`, `Varve.Xsd` and `Varve.Events` pass, and
the conformance gate passes. So do `register`, `licence-headers`,
`decision-sets`, `banned-symbols` and `status`. `release --check` passes but
for the `Closes #120` trailer, which the descriptor's commit adds.

### What this environment could and could not do

- The WebAssembly workloads were not installed. The session installed them
  (`dotnet workload install wasm-tools wasm-experimental`, as CI does) and
  ran the browser tests with Playwright's Chromium.
- `eng/browser-tests.cs` and BenchmarkDotNet build Varve projects
  themselves, with warnings as errors; while the decisions are unaccepted
  they were run with `WarningsNotAsErrors=CS0618` passed through, on the
  command line or in the environment.

### What the maintainer is asked for

- **Accept the decisions**, so `CS0618` clears. That means the ten sets of
  ADRs 0148–0157 and `shacl-hot-path-scope`.
- **ADR 0152's divergence.** Keep SHACL's reading, in which a zero-length step
  reaches the node whatever the graph holds, or follow SPARQL's algebra to the
  letter? Only a focus node absent from the data graph can tell them apart.
- **`InMemoryDataset`'s scan.** Validating over it is quadratic. That is
  outside this milestone, and an index in `InMemoryDataset` would be a
  change to `Varve.Rdf`. Part 2 validates the store, which is indexed.
