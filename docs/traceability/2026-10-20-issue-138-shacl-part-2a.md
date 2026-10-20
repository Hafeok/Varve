# Milestone 8, SHACL, part 2a: commit-time gating and incremental validation

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The opening message, the
> brief, the plan as submitted and the approval are verbatim. The transcript
> itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#138](https://github.com/Hafeok/Varve/issues/138), the first half of SHACL's part 2, under milestone 8 ([#13](https://github.com/Hafeok/Varve/issues/13)) |
| **Date** | 2026-10-20 |
| **Tool** | Claude Code 2.1.304, a cloud session started from the Android app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata (`configured_model`, `session_context.model`, `last_served_model`) |
| **Session identifier** | `session_01RFtkE4cRmAMRARHDu5Y9ZA` |
| **Branch** | `ccr-bf4bdc29-n67y35`, from `main` at f7d25da (`v0.1.0-preview.9`), with `main` at 02a7f5a (#134, Management plane part 1) merged in |
| **Machine** | a cloud container, Intel Xeon @ 2.80 GHz, 4 cores, 15 GiB, Ubuntu 24.04.5 LTS, .NET SDK 10.0.401, runtime 10.0.12 |

## The prompts

### The message that started the session

> @"/root/.claude/uploads/28a20273-4d3d-5b6b-a6ca-37e30769cba2/b7893ff9-varve-shacl-part2.zip" Unzip the attached varve-shacl-part2.zip outside the repository and read varve-claude-code-shacl-part2.md in full. It's the session prompt for SHACL part 2, milestone #13, in github.com/Hafeok/Varve. Work from current main. AGENTS.md applies in full. Read #13 and the ADRs the prompt names, including #134's management ADRs on its branch if it hasn't landed, then post the plan answering its eight plan questions and wait for approval before writing code.

### The brief, `varve-claude-code-shacl-part2.md`

> # Session: SHACL, part 2 — commit-time gating, incremental validation, the REC gate (#13)
>
> Part 1 (#120, `v0.1.0-preview.9`) landed the standalone validator: `Varve.Shacl` at layer 3, `Varve.Shacl.Sparql` at layer 4, and ADRs 0148–0157. This session lands the rest of milestone #13 and closes it:
> - commit-time gating through the pre-commit validator;
> - incremental validation as a projection;
> - the classification of which shapes are incrementally maintainable;
> - the REC 4 gate;
> - the close-out.
>
> It's in 1.0 (#15).
>
> **Starting point.** Current `main`. In flight:
> - **Management plane part 1 (#134)** adds `varve:ShapesBinding` to the management vocabulary, recorded but with no effect until this milestone decides what it does. It also adds the management position as a commit attachment (its ADR 0165) and the code validator for declarations. If #134 hasn't landed when you reach the binding slice, build against its vocabulary as written in its PR and wire it last.
> - **Extent maintenance (#135)** touches nothing here.
>
> **Read before planning:**
> - #13, its amendments, and #120's traceability record;
> - ADRs 0003, 0017 (the validator contract and the overlay quad source) and 0058 (dataset validators and the staging view): the commit-time contract you implement;
> - ADRs 0148–0157, above all 0150 (stratified recursion, greatest fixpoint), 0151 (inputs, failures, SHACL instances), 0152 (paths) and 0154 (the deterministic report);
> - #134's ADRs 0163, 0165 and 0166 on its branch (`claude/hopeful-pascal-mnkbb4`) or on `main` if landed: the binding term, attachments, and the declaration validator;
> - ADR 0157 (shapes for event declarations) and #93's ADRs 0144 and 0145;
> - the REC reference domain ADR (0141) and `testdata/rec/`;
> - the shacl-rs repository (`Hafeok/rust-shacl`, read-only, outside the Varve repository) for anything it says about incremental validation. Re-derive, don't port.
>
> `AGENTS.md` applies in full. Plan first, and wait for approval. Your ADRs start at **0169**: Extent maintenance holds 0158–0162 and the Management plane 0163–0168, with 0167 reserved for its part 2.
>
> ## A. Decisions (Accepted unless stated; each becomes an ADR or a dated amendment)
>
> 1. **Packages.** The store integration is `Varve.Shacl.Store` at layer 5, as ADR 0148 named it. It implements ADR 0017's validator contract over `Varve.Shacl` and `Varve.Shacl.Sparql`. `Varve.Store` learns nothing about SHACL.
>
> 2. **What a binding is.** `varve:ShapesBinding` is a management declaration.
>    - It names the shapes graph: a named graph **in the same dataset**, so shapes are data, versioned and readable as of any position.
>    - It names the data graphs it validates, as a graph set in the grant vocabulary's terms: all, IRIs or prefixes.
>    - It names a mode:
>      - **`reject`:** a commit that leaves any affected focus node non-conforming at severity `sh:Violation` is refused with `422`, under a problem type (propose its name) carrying the report;
>      - **`report`:** the commit is accepted, and its report is attached;
>      - **`off`:** recorded only.
>    - Propose the terms, and extend #134's vocabulary by amendment, not by a parallel one.
>
> 3. **What the validator reads.** The pending transaction overlaid on the pinned position (ADR 0017), and nothing else. The shapes in force are the shapes graph as of that overlay. A transaction that changes the shapes and the data together validates its data against its new shapes.
>
> 4. **Commit-time validation is incremental.** A commit validates only the focus nodes its delta affects, computed by the same engine as point 6. A full validation per commit would make write latency O(dataset). This is why classification (point 5) is part of the gate.
>    - In `reject` mode, a binding whose shapes include any that are only fully re-evaluable is refused at apply time (`422`, naming the shape and the construct), unless the binding declares `varve:allowFullEvaluation true`. That flag states the cost in the operator guide.
>    - A change to the shapes graph itself is never refused because of existing data. It's accepted, and the projection (point 6) rebuilds and reports what became non-conforming. Say so in the ADR, and in the problem detail when a shapes change and a data change share a commit.
>
> 5. **Which shapes are incrementally maintainable.** This is the question open since milestone 2. Propose the classification, with its affected-node rule per construct. My starting point:
>    - **Incremental, by a local rule:**
>      - targets (`sh:targetNode`, `sh:targetSubjectsOf` and `sh:targetObjectsOf`; `sh:targetClass` over `rdf:type/rdfs:subClassOf*` with the subclass edges' own delta);
>      - predicate, inverse, sequence and alternative paths of bounded length, by reverse traversal from each changed triple;
>      - every value-type, cardinality, value-range, string, language, property-pair and closed constraint;
>      - `sh:and`, `sh:or`, `sh:xone`, `sh:not`, `sh:node` and qualified value shapes, through a shape dependency graph.
>    - **Scoped re-evaluation:**
>      - zero-or-more and one-or-more paths, by the reverse closure from the changed triples;
>      - recursive shapes, by re-running the stratum's fixpoint (ADR 0150) over the component the delta reaches;
>      - SPARQL-based constraints, re-evaluated for the focus nodes of their shape's targets that the delta reaches through the constraint's triple patterns, if the SPARQL can be analysed that way.
>    - **Full re-evaluation only:** SPARQL-based constraints that can't be analysed (for example, anything using `NOW()`, `SERVICE` or a subquery whose patterns don't mention `$this`), and anything else the analysis can't bound.
>
>    The ADR states the rule for each construct and cites the SHACL section. The classification is computed when a binding is applied and is served with it.
>
> 6. **Incremental validation as a projection.**
>    - Per binding, the current validation report is a projection: rebuildable from the log, tracking its own position, maintained from exact deltas.
>    - It's readable through SPARQL as a reserved named graph (propose the IRI), as of any position, deterministic as ADR 0154 requires.
>    - Its lag is on readiness.
>    - A shapes change rebuilds the binding's projection in the background, never serving a mixed report: the old one until the new one is complete.
>    - The report at position p must equal a full validation at p, byte for byte.
>
> 7. **The engine is not SHACL-private.** #13 requires that Inference (#95) can reuse the delta-driven maintenance: the affected-node computation from exact deltas, and semi-naive maintenance. Continuous queries will too. Propose where the generic part lives: package, layer and name, under the cohesion rule (no `Common`, no `Incremental` grab-bag) and checked against VARVE0003–0005. Keep what is SHACL-specific in `Varve.Shacl`.
>
> 8. **The report as commit metadata.** In `report` mode, and for every accepted commit under a binding, the commit records which report applied. Use #134's attachment mechanism (ADR 0165) or ADR 0017's validator attachment, whichever exists on `main`. Say which, and whether the attachment names the projection's report at that position (by reference) or carries a digest. Don't store the report twice.
>
> 9. **Shapes replace code for Varve's own declarations** (the close-out).
>    - **Management declarations** (#105): `Varve.Management`'s code validator is replaced at runtime by shapes shipped with Varve. If #134 shipped `shapes/management.ttl`, use it.
>    - **Event declarations** (#93): ADR 0157's shapes become the runtime validator.
>    - In both cases, the code validators stay in the test projects as the oracle of the existing agreement properties. `declaration-invalid` and the event problems keep their wire shape: each finding by focus node and path, built from the report.
>    - If #134 hasn't landed by then, do the events half and leave the management half for the Management plane's part 2, saying so in the PR.
>
> 10. **The REC gate.** The pinned REC 4 shapes (`testdata/rec/`) validate generated REC portfolios, sizes S and M:
>     - standalone;
>     - at commit time in `reject` mode;
>     - incrementally through the projection, with the three results equal at every position of a generated history.
>
>     Report which SHACL features REC uses, which class each falls in, and any shape that can't be evaluated. A REC defect goes upstream with a written exemption.
>
> 11. **Performance and hosts.**
>     - Commit latency with a `reject` binding, against no binding, on the file backend, at REC S and M, measured with BenchmarkDotNet. State the overhead per commit, and per affected focus node.
>     - Projection lag and rebuild time for a shapes change at REC M.
>     - What runs per affected node is `[HotPath]`.
>     - The browser host gains no store integration. `Varve.Shacl` stays usable standalone there, unchanged.
>
> ## B. Definition of done
>
> - At every position of generated histories with retractions and shapes changes, the incremental report equals a full validation, byte for byte. This holds for every construct classed incremental or scoped. Give iteration counts and any counterexample.
> - At commit time, a `reject` binding refuses exactly the commits whose affected nodes violate, against a full-validation oracle over generated commits. A `report` binding attaches the report that the projection then serves at that position.
> - A binding with a full-only shape in `reject` mode is refused at apply time, and accepted with `varve:allowFullEvaluation`.
> - The REC gate of point 10.
> - Management and event declarations are validated by shapes at runtime, and the agreement properties hold against the code oracles.
> - The W3C SHACL suites stay under the ratchet, with nothing regressed.
> - The benchmarks of point 11, with the slower rows first.
> - The public API baseline diff in the PR body, which should be small.
> - The close-out:
>   - README Status, `docs/roadmap.md` (SHACL complete), AGENTS.md's State, `eng/status.cs` and the changelog;
>   - the operator guide's page on shapes bindings;
>   - `docs/problems/` for the new problem types;
>   - the traceability record.
>
>   Chapter 3's SHACL section is the Learning track's (#94), since it lands second.
>
> ## How this lands
>
> - **One PR,** red only on `CS0618`, unless the plan shows it is too large. If so, propose two releases:
>   - first: points 1–5 and 7–8, the engine, classification and commit-time gating;
>   - second: points 6, 9 and 10 and the close-out, the projection, shapes for declarations, REC and the close-out.
>
>   The last descriptor closes #13, and an earlier part gets its own issue, as #120 did.
> - **The descriptor** is at the next free `0.1.0-preview.N` when it lands (ADR 0102). It carries `accepted-by: mailto:emil@okkels-klein.dk` (the descriptor's line, `docs/releases.md` point 6) and `Closes #13` as a trailer on the descriptor commit. Use bare #N elsewhere, and no closing keyword in the PR description (#108).
> - **Commits** are signed with the agent SSH key, reference #13, and never name a model.
> - A `docs/traceability/` record (ADR 0033).
> - No suppression or downgrade of DD or VARVE rules.
> - **CI images** only from `.github/ci-images.yaml`.
> - **Size property-test iteration counts for the land gate,** and report each new test project's duration per OS (#126). Put long runs behind an environment variable that a nightly run sets.
> - **Land through `land/`.** If #134 or #135 lands first, merge `main` and reconcile.
>
> ## Plan questions
>
> 1. The binding vocabulary and its amendment to #134's ADR 0166.
> 2. The classification of point 5, construct by construct, with the affected-node rule and the SHACL section for each.
> 3. The generic engine's package, layer and contract (point 7), and what Inference reuses unchanged.
> 4. How the projection is stored, rebuilt and read as of a position, and its reserved graph IRI.
> 5. The commit metadata of point 8.
> 6. The problem types: the commit refused by a binding, and the binding refused at apply time.
> 7. One PR or two, by size.
> 8. ADR numbers from 0169, and amendments (0017 and 0058 certainly; 0166, 0157 and 0154 if touched).

### The plan, as submitted

Posted on #13 ([comment](https://github.com/Hafeok/Varve/issues/13#issuecomment-6287814146)).

> ## Plan: SHACL part 2 (#13): commit-time gating, incremental validation, the REC gate
>
> From `main` at `f7d25da` (preview.9). #134 hasn't landed, so I read its ADRs 0163, 0165 and 0166 on `claude/hopeful-pascal-mnkbb4` at `02a7f5a`. Sections cited are SHACL 1.2 Core as pinned by ADR 0149: `shacl12-core` at data-shapes `4cd4698`. The section numbers match the ones the part 1 ADRs cite (§6.6.3, §7.9.5).
>
> ### What I found that changes the brief
>
> 1. **rust-shacl has nothing on incremental validation.** It has no delta input, no affected-node computation, no retraction handling, and no analysis of SPARQL dependencies (HEAD `c0c4873`, read in full for these terms). Everything below is derived from SHACL itself.
> 2. **`NOW()`, `RAND()`, `UUID()` and `STRUUID()` can't be classed "full only".** The prompt classes `NOW()` as full-only. But a constraint that reads the clock makes the report a function of wall time, not of position. Even a full validation at p then isn't reproducible, so B's "report at p equals a full validation at p, byte for byte" can't hold for it. **I propose refusing these functions in any binding whose mode isn't `off`,** with the shape named. "Full only" stays for deterministic SPARQL the analysis can't bound. `SERVICE` is already refused by pre-binding (ADR 0148's `PreBinding` checker), so it's a refusal, not a class.
> 3. **The apply-time refusal can't be a validator on the system dataset.** ADR 0017 lets a validator read only `Overlay(G_head, δ)` and δ, and the shapes graph lives in the *data* dataset. So the check runs above the store, in the apply path, before the management commit is submitted, which is where 0017's alternatives put cross-dataset checks. It has a race (the shapes change between the check and the commit), and the data side closes it by failing closed (Q2, rule R6).
> 4. **The report graph is computed, not stored.** ADR 0130 point 5 says system graphs are ordinary data in the log. This one is a projection, so that point needs an amendment, and the graph needs a grant rule (Q4).
> 5. **REC:**
>    - **Loading:** its three files import each other in a cycle, and Varve doesn't follow `owl:imports` (ADR 0151). So the gate loads `rec.ttl`, `Brick+patches.ttl` and `recimports.ttl` into one shapes graph.
>    - **Subclass triples:** the class hierarchy is in the shapes files, so REC needs `subClassOfInShapesGraph` (§6.4). The binding gets that term (Q1).
>    - **Implicit targets:** the 1,445 Brick shapes typed `owl:Class, sh:NodeShape` get no implicit class target (§3.1.3.3), because no file states `owl:Class rdfs:subClassOf rdfs:Class`. That's REC's composition, not a Varve defect. The gate adds that one OWL triple to its shapes graph and says so. If you'd rather, it goes upstream with an exemption instead.
>    - **SHACL-SPARQL:** REC needs it for 3 constraints (`rdf:type/rdfs:subClassOf*` paths with `FILTER NOT EXISTS`, all analysable, class *scoped*).
>    - **Rules:** its 6,025 `sh:rule` triples are SHACL-AF, which belongs to Inference (ADR 0149). I'll verify they're ignored and report it.
>    - **Recursion:** none. No `sh:node`, every `sh:property` is a blank node, and the `sh:or`/`sh:not` members are inline.
> 6. **A full-validation oracle at every position of REC M can't run on the land gate:** M is about 5,000,000 quads over 25,000 commits. Q7 sizes it.
>
> ---
>
> ### Q1. The binding vocabulary, by amendment to ADR 0166
>
> `varve:ShapesBinding` stays in `varve:manage/dataset/{enc(name)}`, an IRI node as 0166 requires. The amendment gives it these properties:
>
> | Term | Value | Default |
> |---|---|---|
> | `varve:boundGraph` | **the shapes graph**: exactly one absolute IRI, a named graph of the *same* dataset. This gives 0166's existing term its meaning for this binding kind; `TBoxBinding` keeps its own meaning for #95. | required |
> | `varve:validates` | a node carrying the grant vocabulary's graph-set terms: `varve:allGraphs true`, `varve:graph` (repeatable, including `varve:defaultGraph`) and `varve:graphPrefix` (repeatable) | required |
> | `varve:validationMode` | `varve:Reject`, `varve:Report` or `varve:Off` | required |
> | `varve:allowFullEvaluation` | `xsd:boolean` | `false` |
> | `varve:subClassOfInShapesGraph` | `xsd:boolean` (SHACL 1.2 Core §6.4) | `false` |
>
> - **Graph sets.** `varve:graphTemplate` is refused inside `varve:validates`, because a template expands per caller and a binding has no caller. The graph-set terms are 0166's own, so there's no parallel vocabulary. In code, the set maps to `Varve.Rdf.GraphScope`.
> - **The data graph is the union of the bound graphs.** It always excludes the shapes graph and every graph under `https://w3id.org/varve/ns#`. SHACL validates one data graph (§6.2), and a union is what makes `sh:class` work across REC's per-site graphs. The alternative, one report per graph, is listed in the ADR.
> - **What 0166's code validator gains:** it refuses a `ShapesBinding` without exactly one `boundGraph`, `validates` and `validationMode`; a template in `validates`; a `boundGraph` inside its own `validates`; and two bindings with the same IRI. The remaining checks need the data dataset, so they run at apply time (Q2, R6).
> - **Which binding is in force for a commit:** the one at the management position the commit was authorised under (ADR 0165). An embedded dataset has no management plane, so it passes its bindings in code through `Varve.Shacl.Store`.
>
> ### Q2. The classification (point 5)
>
> **The rule.**
> - **The footprint.** For a shape s and a node n, the footprint F(s, n) is the set of data triples the validation of n against s can read (§6.6, with each component's validator in §7).
> - **Which nodes a delta affects.** The pair (s, n) is affected by δ = (A, R) when either:
>   - F(s, n) in the post-state meets A, or F(s, n) in the pre-state meets R; or
>   - n entered or left s's targets.
> - **The anchors.** An affected node is found by **reverse traversal from each changed triple**. Assertions are traversed in the overlay and retractions in the pinned state. Both states are needed, because a retracted triple's path exists only before the commit.
> - **Re-validation.** Affected pairs are re-validated. Every other pair keeps its result, because a pair's result is a function of its footprint.
> - **Over-approximation is sound.** A spurious anchor only re-validates a node to the same result, which is what lets SPARQL be analysed by its join graph.
>
> **Classes.**
> - **Incremental:** anchors are found by a bounded local rule.
> - **Scoped:** anchors are found by a closure whose size depends on the data.
> - **Full:** re-evaluate every target.
> - **A shape's class is the worst class of its targets, path and constraints,** and of the shapes it reaches in the dependency graph.
>
> | Construct (section) | Class | The anchor rule for a changed triple (x p y) |
> |---|---|---|
> | `sh:targetNode` (§3.1.3.1) | incremental | none: the targets don't depend on data |
> | `sh:targetSubjectsOf` / `sh:targetObjectsOf` (§3.1.3.4–5) | incremental | x (or y) when p is the predicate |
> | `sh:targetClass` and implicit class targets (§3.1.3.2–3), on an `rdf:type` change | incremental | x when y is the class or one of its subclasses |
> | … on an `rdfs:subClassOf` change in the data graph | scoped | every instance of the subtree below x. (Under `subClassOfInShapesGraph`, a subclass triple in the shapes graph is a shapes change, so rule R5 applies.) |
> | `sh:shape` explicit targets (§3.1.3.7) | incremental | x when p is `sh:shape` |
> | `sh:targetWhere` (§3.1.3.6) | the class of its shape | nodes whose conformance to the where-shape may change, plus x and y when the triple added or removed a node of the graph |
> | predicate, inverse, sequence and alternative paths (§4.1–4.4, 4.7) | incremental | for each step whose predicate is p: reverse-evaluate the path's prefix from that step's start (x forward, y inverse) |
> | `sh:zeroOrMorePath` / `sh:oneOrMorePath` (§4.5–4.6) | scoped | as above, with the closure step's prefix reverse-evaluated as a reverse closure |
> | `sh:datatype`, `sh:nodeKind`, ranges, lengths, `sh:pattern`, `sh:singleLine`, `sh:languageIn`, `sh:in`, `sh:hasValue` (§7.1.2–3, 7.3, 7.4.1–5, 7.9.2–3) | incremental | the path's anchors only, since these read the value term alone |
> | `sh:minCount` / `sh:maxCount`, `sh:uniqueLang` (§7.2, 7.4.6) | incremental | the path's anchors |
> | `sh:class` (§7.1.1) | incremental (`rdf:type`), scoped (`subClassOf`) | x as a value node, reverse-traversed through the path to its focus nodes |
> | `sh:equals`, `sh:disjoint`, `sh:subsetOf`, `sh:lessThan`, `sh:lessThanOrEquals` (§7.6) | incremental | the anchors of both paths |
> | `sh:closed`, `sh:ignoredProperties` (§7.9.1) | incremental | x, from any triple with subject x. `sh:ByTypes` also reads x's types, which is scoped on a `subClassOf` change. |
> | `sh:node`, `sh:property`, `sh:and`, `sh:or`, `sh:xone`, `sh:not`, `sh:someValue`, qualified value shapes including `…Disjoint` siblings (§7.7, 7.8.1–4) | the class of the shapes referenced, if acyclic | through the **shape dependency graph**: when (s′, v) is affected, the focus nodes that reach v by s's path are affected for s. Propagation is polarity-blind, since a flip either way propagates. |
> | `sh:reifierShape` (§7.8.5) | incremental | as `sh:node`, plus the `rdf:reifies` triples of v |
> | `sh:memberShape`, list lengths, `sh:uniqueMembers` (§7.5) | scoped | the reverse `rdf:rest*` closure from x to the list's head, then as a value node |
> | `sh:rootClass` (§7.9.4) | scoped | the reverse `subClassOf*` closure from x, then as a value node |
> | `sh:uniqueValuesFor` (§7.9.5) | incremental | the targets whose path reaches y, old or new: a reverse path from the value, not from the triple's subject |
> | a recursive strongly connected component (§6.6.3, ADR 0150) | scoped | the affected pairs, closed upward through the component's edges, are reset to true, and the component's greatest fixpoint is re-run over them, with every other pair fixed. That's exact because no other pair depends on them. |
> | SPARQL-based constraints and components (SHACL-SPARQL §§3–4), analysable | scoped | see below |
> | … unanalysable but deterministic | **full** | every target of the shape |
> | … using `NOW()`, `RAND()`, `UUID()`, `STRUUID()` | **refused** | Finding 2 |
> | severity, messages, `sh:deactivated` (§3.1.4–6) | static | none |
>
> **SPARQL analysis** lives in `Varve.Shacl.Sparql`, over the algebra it already parses.
> - **The join graph.** Its edges are the query's triple patterns and paths, including those under `OPTIONAL`, `MINUS`, `FILTER (NOT) EXISTS`, `GRAPH $this`-free blocks and sub-`SELECT`s. Its vertices are variables and constants.
> - **When it's analysable.** A query is analysable when every pattern is joined to `$this` (or `$value` for ASK validators).
> - **The anchors.** A changed triple matching a pattern anchors `$this` by reverse evaluation along the join path, dropping filters, which over-approximates.
> - **What makes it full only:**
>   - a pattern unconnected to `$this`;
>   - a sub-`SELECT` that doesn't project a variable joined to `$this`;
>   - an aggregate whose group isn't joined to `$this`;
>   - a path the reverse rule doesn't cover.
>
> **The rules at commit time** go in ADR 0169.
> - **R1, reject.** The commit is refused when an affected focus node is non-conforming at `sh:Violation` after the commit, even when it was already non-conforming before.
>   - **The consequence, stated:** after a shapes change, every existing violator is "sticky", and a commit that touches it must fix it.
> - **R2, report.** The commit is accepted with the attachment (Q5).
> - **R3, off.** Nothing runs, and nothing is attached.
> - **R4, apply time.** A `reject` binding is refused at apply time when its shapes include a *full* shape and `allowFullEvaluation` isn't set. It's also refused when the shapes graph is refused (ill-formed, unstratifiable, a missing extension, a refused function).
> - **R5, shapes changes.**
>   - **Existing data never refuses one.** A commit that changes the shapes graph is never refused because of existing data.
>   - **Shapes changed with data.** When the same commit changes data too, the nodes affected by the *data* part are validated against the *new* shapes. The problem detail says the shapes changed in the commit and that the projection will report the rest.
>   - **What still refuses a shapes commit:** one that makes the bound shapes refused, or (under `reject` without the flag) full-only. That refusal is about the shapes, never about the data.
> - **R6, failing closed.** When a `reject` binding's shapes classify as full-only without the flag, every data commit under it is refused with `binding-not-incremental` until the binding or the shapes are fixed. That can happen through the apply-time race.
>
> The classification is computed at apply time and served at `GET /manage/datasets/{name}/bindings/{enc(binding)}`: each shape, its class, and the construct and section that decided it.
>
> ### Q3. The generic engine (point 7)
>
> **`Varve.Derivation`, at layer 2.**
> - **What it references:** `Varve.Rdf` and nothing else.
> - **What it's for:** keeping a relation derived from a quad source equal to its definition under that source's exact deltas.
> - **Where it sits:** layer 3, where `Varve.Shacl`, Inference and continuous queries' evaluator live, can reach it.
> - **Why the name.** "Derivation" names its one reason to change, which is how derived facts follow deltas. It isn't a grab-bag, and it doesn't collide with "maintenance", the existing name for format migration.
> - **What changes for layer 2.** 0003 describes layer 2 as syntax and the algebra's shape. So 0003 is amended to say that layer 2 is what depends on the model alone, and the `AGENTS.md` table is updated. That passes VARVE0005 and `DD0001`.
>
> **What it holds:**
> - **`DependencyGraph`**: vertices with edges labelled by polarity (positive, negative, both, target), Tarjan SCC, a stratification, and the cycle through a negative or target edge, named. It moves down from `Varve.Shacl`'s `Stratification`, and SHACL keeps the table of polarity per parameter (amends 0150).
> - **`PathExpression`**: predicate, inverse, sequence, alternative, `*`, `+` and `?`. It has set-semantics forward evaluation, reverse evaluation, and **`Anchors(path, changed, pre, post)`**. `Varve.Shacl` compiles `ShapePath` into it. The SHACL syntax and the SPARQL rendering stay in `Varve.Shacl`, and 0152's differential property runs unchanged (amends 0152).
> - **`LeastFixpoint`**: a semi-naive frontier driver. It serves the closures and the reverse closures, and it's the operator Inference's insertion maintenance uses.
> - **`Propagation`**: a worklist over (vertex, key) seeds. Each edge carries a caller-supplied reverse map, and the closure follows the stratification. The greatest-fixpoint reset for a recursive component is a mode of it.
>
> **Inference reuses unchanged:** `DependencyGraph` (rule strata), `PathExpression` with its anchors (the `subClassOf*` and transitive closures of tier 1), `LeastFixpoint` and `Propagation`.
>
> **Inference adds:** deletion for least fixpoints (DRed or counting) and anchors for join-shaped rule bodies.
>
> **Not built here:** DRed has no consumer in this milestone, so building it now would be scaffolding. The ADR states its contract slot, so Inference adds it without changing anything here.
>
> **What stays in `Varve.Shacl`:** SHACL's footprint per component (Q2's table), the classification, and the per-focus re-validation. The additive public members (0155 point 3) are:
> - `ShaclValidator.Validate(…, FocusSelection)`;
> - `ShapesGraph.Classify() → ShapesClassification`;
> - `ShapesGraph.AffectedBy(IQuadSource pre, IQuadSource post, QuadDelta) → AffectedFocusNodes`.
>
> The extension contract gains an optional `IShaclConstraintFootprint`, which `Varve.Shacl.Sparql` implements. The code that runs per changed quad and per affected node is `[HotPath]`.
>
> ### Q4. The projection
>
> - **Where it lives:** `Varve.Shacl.Store` (layer 5), as an `IProjection` per binding, driven by `CatchUpAsync` and `RebuildAsync`.
> - **How it's stored:** under `derived/validation/{enc(binding)}/`, in the local cache only, like runs. Any instance rebuilds it from the log, and it's never shared on an object store.
> - **Its rows:** one per result. A row is keyed by (shape, focus node) and holds the result's fields as canonical N-Triples strings, so no handle space leaks into it. Each row carries a validity interval `[asserted, retracted)`, in ADR 0138's style.
> - **Generations:**
>   - **What starts one:** a binding change or a shapes change, at position s.
>   - **How it's built:** in the background, under a manifest replaced whole after the generation is complete.
>   - **What's served meanwhile:** the projection's position stays below s until then. Reads at positions under s are served, and reads at s or above get `503 validation-behind`, so a mixed report is never served.
>   - **The cost:** only the shapes that changed, and those depending on them, are re-evaluated. That's not normative, and the property is the guarantee.
> - **As of p:** the rows valid at p are read into a `ValidationReport`, then `ToCanonicalNQuads()` (ADR 0154). The property is that it equals `ShaclValidator.Validate` over `AsOfAsync(p)`, byte for byte.
> - **Reserved graph:** `https://w3id.org/varve/ns#validation/{enc(binding IRI)}`, with `enc` as the catalogue graphs use it.
>   - **How it's served:** at a pinned position, through the staging view (ADR 0058), as an overlay of provisional handles, so the dictionary isn't touched. The server composes it through a new `Varve.Protocol` seam. Protocol can't reference `Varve.Shacl.Store`, since both are at layer 5.
>   - **Writes to it** are `graph-not-writable`.
>   - **Reads:** it's readable only by a caller who can read *every* graph the binding validates, because a report names those graphs' nodes and values.
>   - **Amendments:** 0130 point 5 (a computed system graph) and 0107 (that grant rule).
> - **Readiness:** each binding's lag (head minus projection position) is reported on readiness, beside the default projection's lag. A binding past `Varve:Health:ReadyLag` makes the dataset "behind".
>
> ### Q5. The commit metadata (point 8)
>
> - **Which mechanism:** ADR 0017's validator attachment, which is on `main` (`ValidationVerdict.Accept(RdfTerm)`). #134's host attachment isn't needed for this. There's one validator per binding, so a commit has one attachment per binding.
> - **The attachment:** the triple term `<<( <binding> varve:affectedReport "sha256:<hex>" )>>`. The hex is SHA-256 over the canonical N-Quads of the report **restricted to the commit's affected focus nodes** at that position.
> - **Both a reference and a digest.**
>   - **The reference:** the projection's report at (binding, p) is the full one.
>   - **The digest:** it pins exactly which slice of it the commit decided on. Restricting the projection's report at p to those focus nodes reproduces the digest.
>   - **The property** for B's "attaches the report the projection then serves" is exactly that check.
>   - **Where the report lives:** it's stored once, in the projection.
> - **Under which modes:** every accepted commit under a `report` or `reject` binding carries one.
> - **The cost, stated:** one dictionary term per commit per binding, unbounded, unlike 0165's management position. The alternative is the binding IRI alone: bounded, but nothing to check.
> - **Amendments:** 0154 (the restricted report and its digest) and 0017 (what a SHACL attachment is).
> - **Fallback:** if the store refuses a triple term as an attachment, a typed literal `varve:affectedReport`, decided in the ADR.
>
> ### Q6. The problem types (each with a `docs/problems/` page and a `ProblemCatalogue` entry, and added to `IsValidatorType`; amends 0119)
>
> | Type | Status | Members | When |
> |---|---|---|---|
> | `shapes-violated` | 422 | `binding`, `shapesGraph`, `findings` (each finding has `focusNode`, `path`, `value`, `sourceShape`, `component`, `severity`, `messages`), `report` (the restricted report, canonical N-Quads), `shapesChanged` | a commit refused under R1, or R5's data part |
> | `binding-not-incremental` | 422 | `binding`, `shapes` (each with `shape`, `construct`, `section`) | R4 at apply time, and R6 at commit time; `detail` names `varve:allowFullEvaluation` and its cost |
> | `shapes-invalid` | 422 | `binding`, `problems` (each with `kind`, `shape`, `focusNode`, `message`, `cycle`) | the bound shapes graph is refused: at apply time, or by a shapes commit |
> | `validation-behind` | 503 | `binding`, `position`, `projectedPosition` | a read of the report graph above the projection's position, after one bounded wait |
>
> `declaration-invalid` (#134) and the event problems keep their wire shape when shapes replace the code (point 9). Each finding is built from a report result: its focus node, and the path rendered by ADR 0152's SPARQL form.
>
> ### Q7. One PR or two
>
> **Two.** Part A is about 9–10k lines with tests, and part B about 7–8k. Each one alone is #134's size.
>
> **Part A, preview.N (its own issue, as #120 had): points 1–5, 7 and 8:**
> - `Varve.Derivation`, the classification, the affected-node engine, the `Varve.Shacl` additions and the SPARQL analysis;
> - `Varve.Shacl.Store`'s commit validator, the binding vocabulary, the apply-time check and three of the problem types;
> - the commit attachment;
> - the commit-time oracle property;
> - the commit-latency benchmarks at S and M;
> - REC **standalone**, as an early signal that finds REC defects before part B depends on them.
>
> **Part B, preview.N+1 (the descriptor's trailer closes #13): points 6, 9 and 10, and the close-out:**
> - the projection, its reserved graph, readiness and `validation-behind`;
> - shapes as the runtime validator of declarations;
> - the full REC gate;
> - the projection lag and the rebuild benchmarks at M;
> - README, the roadmap, `AGENTS.md`'s State, `eng/status.cs`, the changelog, the operator page `docs/operator/shapes.md`, and the traceability record.
>
> **#134's timing:**
> - **Part A's server wiring** (bindings read from the system dataset, the route, the apply check) is built last against #134's vocabulary, and wired if #134 has landed.
> - **The management half of point 9** moves to the Management plane's part 2 if #134 hasn't landed by part B. The PR says so.
> - **Shapes for management declarations:** #134 ships none (`shapes/management.ttl` isn't on the branch), so part B writes them.
>
> **Iteration counts on the land gate:**
> - **Incremental against full validation at every position:** 200 generated histories, each up to 40 commits with retractions and shapes changes, over every incremental and scoped construct.
> - **Commit-time oracle:** 500 generated commits.
> - **REC S, every position:** 1,000 positions. If S at every position is too slow for the land gate, every 25th position plus the last, with all of them nightly behind `VARVE_LONG_PROPERTIES`.
> - **REC M, sampled:** 100 positions, nightly.
>
> Each new test project's duration per OS goes in each PR (#126).
>
> ### Q8. ADRs and amendments
>
> | ADR | Part | Title |
> |---|---|---|
> | 0169 | A | `Varve.Shacl.Store`: commit-time gating under a shapes binding (points 1, 3 and 4, R1–R6, the refused functions) |
> | 0170 | A | The shapes binding vocabulary (point 2) |
> | 0171 | A | Which shapes are incrementally maintainable: footprints, anchors and classes (point 5; the ADR the issue's Decisions section expects) |
> | 0172 | A | `Varve.Derivation`: delta-driven maintenance at layer 2 (point 7) |
> | 0173 | A | The validation report on the commit: a reference and a digest (point 8) |
> | 0174 | B | The validation projection and its reserved graph (point 6) |
> | 0175 | B | Shapes validate Varve's own declarations at runtime (point 9) |
> | 0176 | B, only if needed | The REC gate's shapes composition (the OWL triple and `subClassOfInShapesGraph`), unless you'd rather that be an upstream exemption |
>
> **Dated amendments:**
> - **0003:** the layer-2 wording and the new packages.
> - **0017:** what a SHACL validator reads (shapes in the overlay), what its attachment is, and that the apply check runs above the store.
> - **0058:** answering its "which shapes graph gates a dataset … for milestone 8": in the system dataset, applied at the commit's management position.
> - **0107:** the report graph's grant rule.
> - **0119:** the problem catalogue's validator types.
> - **0130:** a computed system graph.
> - **0150:** stratification moves to `Varve.Derivation`.
> - **0152:** path evaluation moves there too.
> - **0154:** the restricted report and its digest.
> - **0155:** the additive members.
> - **0157 and 0145:** shapes become the runtime validator.
> - **0166:** the binding properties (on #134's branch, or on `main` once it lands).
>
> Every ruling is filed unaccepted, so the PRs are red on `CS0618` only.
>
> ### Decisions I need from you
>
> 1. **Refusing clock- and random-dependent SPARQL,** in place of classing it full-only (Finding 2).
> 2. **The data graph as the union of the bound graphs,** not one report per graph.
> 3. **The REC composition:** add the OWL triple to the gate's shapes graph, or go upstream with an exemption.
> 4. **The attachment** as reference plus digest, at one term per commit per binding.
> 5. **Two PRs,** with part A's issue opened by me on approval.
>
> ---
> _Generated by [Claude Code](https://claude.ai/code)_

### The approval of the plan

> Approved, with two changes and these rulings.
>
> **Changes**
> 1. **R1 refuses new violations, not existing ones.** A `reject` binding refuses a commit when, for its affected focus nodes, the post-state has a validation result not present at the pinned position. A result's identity is (focus node, source shape, component, path, value). ADR 0169 states the guarantees: under `reject`, the set of violations never grows, and from a conforming start the dataset stays conforming. The sticky-violator rule is listed as the rejected alternative, because it turns an unrelated edit of a non-conforming entity into a refusal after every shapes tightening. R5 is unchanged. The oracle property is adjusted to match.
> 2. **A validation projection's lag never affects readiness.** It's reported per binding on readiness, as #110's retained bytes are, but the dataset never turns "behind" or unready because of it. Only reads of the report graph get `validation-behind`. Otherwise a shapes change on a large dataset would take it out of rotation while the report rebuilds.
>
> **Rulings**
> 1. Refuse `NOW()`, `RAND()`, `UUID()` and `STRUUID()` in any binding not `off`, and also `BNODE()` wherever its value can reach a result.
> 2. The data graph is the union of the bound graphs.
> 3. The REC gate adds `owl:Class rdfs:subClassOf rdfs:Class` to its shapes graph. It's OWL's own vocabulary, not a REC patch. ADR 0176 stays, scoped to the gate's composition. `docs/operator/shapes.md` tells users binding REC or Brick shapes to include it. Report it to REC upstream as a note, with no exemption.
> 4. The attachment is reference plus digest, one term per commit per binding. ADR 0173 says a digest mismatch after an upgrade is how an engine change shows up, and that a release changing SHACL results says so in its notes.
> 5. Two PRs. Open part A's issue, on the pattern of #120, before writing code.
>
> **Also**
> - ADR 0174's consequences: the projection lives in each instance's local cache, so every follower rebuilds it on cold start. Sharing it as extents is a later decision.
> - `Varve.Derivation` at layer 2, with the ADR 0003 amendment as proposed, is approved.
> - ADR numbers 0169–0176 stand.

#138 was opened on the pattern of #120, as a sub-issue of #13, before any
code was written. The session's context was summarised once, mid-way; the
work continued from the summary and the files.

## The report

### What shipped, by ADR

- **0169, the gate.** `Varve.Shacl.Store` at layer 5 is an
  `ICommitValidator` over the bindings it is given. It reads the overlay and
  the delta, and nothing else. It keeps each binding's loaded shapes until a
  delta touches the shapes graph. Its modes:
  - `reject` refuses a commit whose affected focus nodes have a
    `sh:Violation` result whose identity (focus, source shape, component,
    path, value) is not in the pinned position's report. Provenance is
    checked up the `sh:property` chains in the pre-state, so a result from an
    unaffected pair is never taken for a new one.
  - `report` accepts the commit and attaches the digest.
  - `off` does nothing.
  - A shapes commit is never refused for existing data (R5).
  - A `reject` binding whose shapes classify full without
    `varve:allowFullEvaluation` refuses its data commits (R6).
  - The R4 check runs in `ManagementEndpoints.CommitAsync`, before the
    management commit is submitted, through `IManagementPlane.CheckBindings`.
  - `NOW()`, `RAND()`, `UUID()` and `STRUUID()` are refused when a binding's
    shapes are loaded, and so is a `BNODE()` whose value can reach a result.
  - `ValidationVerdict.Accept(IReadOnlyList<RdfTerm>)` gives one attachment
    per binding.
  - Three problem types: `shapes-violated`, `binding-not-incremental` and
    `shapes-invalid`. Each has a page in `docs/problems/`, is in the
    catalogue, and is honoured as a validator type.
- **0170, the vocabulary.**
  - Terms: `varve:boundGraph` (the shapes graph), `varve:validationMode`
    with `varve:Reject`, `varve:Report` and `varve:Off`,
    `varve:allowFullEvaluation`, and `varve:subClassOfInShapesGraph`.
  - The grant vocabulary's graph-set terms sit on the binding node itself.
  - `DeclarationReader` reads all of these into `BindingDeclaration`.
  - The code validator refuses a binding with no shapes graph, no mode or no
    graph set, with a template, or one that validates its own shapes graph.
  - The data graph is the union of the bound graphs, always without the
    shapes graph and `https://w3id.org/varve/ns#` graphs.
- **0171, incremental validation.**
  - `IncrementalValidation.Compare` validates the pairs a delta affects, in
    the state after it and in the state before it. The pre-state is the
    overlay with the delta inverted.
  - Footprints are anchored by reverse traversal from each changed triple,
    in both states, and propagated along the shapes' dependency graph to a
    least fixpoint.
  - `ShapesGraph.Classify()` classes each shape incremental, scoped or full,
    with the construct and section that decided it.
  - `IShaclFootprint` lets an extension state its own footprint.
    `Varve.Shacl.Sparql` implements it by the query's join graph:
    - the roots are `$this`, and `$value` when it is pre-bound;
    - a sub-select is scoped;
    - `GRAPH`, a negated property set or an unjoined pattern makes the
      constraint full.
- **0172, `Varve.Derivation`** (layer 2):
  - `DependencyGraph` and `Stratification` (Tarjan, with the refused cycle
    named);
  - `RegularPath` compiled to a Thompson `PathAutomaton`, evaluated forwards,
    in reverse, and from a changed edge;
  - `Propagation`, semi-naive.

  `Varve.Shacl` now stratifies and walks paths through it.
- **0173, the attachment.** `<<( binding varve:affectedReport
  "sha256:<hex>" )>>` is attached on every accepted commit under `report` or
  `reject`, but not on a bulk commit. The hex is the SHA-256 of the restricted
  report's canonical N-Quads. A blank node new in the commit is labelled by
  the identifier it is committed with, so the digest computed after the
  commit reproduces it.
- **Over a fleet.**
  - `Fleet.OrderOf` adds the dataset's gate to the validators of a write, at
    the management position it is authorised under.
  - The server keeps one gate per dataset while its bindings stay the same.
  - `GET /manage/datasets/{name}/bindings/{binding}` describes a binding with
    its shapes' classification.
- **Dated amendments** (2026-10-20) to 0003, 0017, 0058, 0119, 0150, 0152,
  0154, 0155 and 0166.

### Where the work departed from the plan or the decisions, and why

- **No `varve:validates` node.** The plan put the graph-set terms on a
  `varve:validates` node. ADR 0166's code validator refuses every blank node
  in a management graph, so that node would have to be a second IRI per
  binding. The terms sit on the binding node instead, as they sit on a grant
  node, and ADR 0170 says so.
- **The public surface took different names.** The plan named
  `ShaclValidator.Validate(…, FocusSelection)` and `ShapesGraph.AffectedBy`.
  What shipped is one call, `IncrementalValidation.Compare`, which returns
  the affected focus nodes, both restricted reports and the results
  introduced. The gate needs all of these together, and each is cheap only
  when computed with the others. `ShapesGraph.Classify()` and the footprint
  contract (`IShaclFootprint`, not `IShaclConstraintFootprint`) are as
  planned. ADR 0155's amendment lists the members.
- **`Varve.Derivation` has no separate `LeastFixpoint`.** `Propagation` is
  the semi-naive driver, and the closures run on it. The plan's
  `PathExpression` is `RegularPath`, compiled to `PathAutomaton`. ADR 0172
  has the names.
- **REC's SPARQL constraints were misread in the plan.** The plan said REC
  needs SHACL-SPARQL for three analysable constraints. In fact Brick's three
  `sh:sparql` constraints name their prefixes by `sh:prefixes rdf:`,
  `brick:`, `rdfs:` and `ref:`, and none of the three vendored files has a
  `sh:declare`. Under SHACL 1.2 SPARQL §5.2.1 they cannot be parsed, so the
  shapes graph is refused as it stands: three `ExtensionFailure`s for an
  undeclared prefix. The first one also uses `owl:` without naming it. This
  is recorded below as a finding; part 2b decides the gate's composition.
- **A performance fix found by the benchmark.** Under
  `subClassOfInShapesGraph`, every validation run walked `rdfs:subClassOf` in
  the shapes graph again, a scan of the in-memory shapes per step. The steps
  are now kept on the loaded shapes. At REC S the gate's time per commit fell
  from 13.2 ms to 4.5 ms.

### Findings outside this repository

- **REC 4.1.0 / Brick 1.3 (as vendored by ADR 0141): undeclared SPARQL
  prefixes.** Three constraints carry `sh:prefixes` pointing at namespace
  nodes that have no `sh:declare`:
  - `bsh:DeprecationRuleForInstances` (`rdf:`, `brick:`; its query also uses
    `owl:`);
  - the "Only Brick Points can have external timeseries references" rule
    (`rdf:`, `rdfs:`, `ref:`);
  - the "Only meters can have the isVirtualMeter property be true" rule
    (`rdf:`, `rdfs:`, `brick:`).

  Upstream Brick declares them in its own build, and REC's merged file
  dropped them. A strict processor refuses the shapes graph; a lenient one
  falls back on the file's Turtle prefixes, which SHACL does not license. To
  be reported to REC upstream as a note, beside the
  `owl:Class rdfs:subClassOf rdfs:Class` note of the ruling. No exemption is
  taken: `RecStandaloneTests` pins the refusal.
- **The generated REC portfolio has no `rec:leasor`.** Under REC's other
  shapes, the S head's only violations are 141 `sh:minCount` results on
  `rec:leasor`, one per lease. That is the generator's (ADR 0141), not
  REC's. Under a `reject` binding a commit that adds a lease is refused,
  which the benchmark reports.

### Properties and tests

| Property | Per CI run | Long run (`VARVE_LONG_PROPERTIES`) | Counterexamples |
|---|---:|---:|---|
| Incremental comparison against full validation over generated shapes and histories (`Varve.Shacl.Tests`) | 500 histories | 5,000 (1,686 histories and 7,697 positions were run in the long check) | none. Three sabotages each failed it: no path anchors, an empty transfer, and no provenance |
| SHACL-SPARQL's join-graph analysis against full validation (`Varve.Shacl.Sparql.Tests`) | 300 | 3,000 | one while it was being written: `?value` taken for a root in `SELECT` queries, where it is bound by the query, not pre-bound. Fixed, none since |
| The gate's verdicts and digests against a full-validation oracle, with shapes changed alone and with data (`Varve.Shacl.Store.Tests`) | 500 commits | 5,000 | none |
| Regular paths: the automaton against a naive evaluator, forwards, in reverse and from a changed edge (`Varve.Derivation.Tests`) | 3,000 | — | none |
| Stratification against a naive fixpoint, cycles through negation refused (`Varve.Derivation.Tests`) | 3,000 | — | none |

Example tests cover each rule:
- R1 and R5;
- R4's refusals, at apply time through the management plane;
- R6 failing closed;
- the refused functions, `BNODE()` reaching a result and not;
- several bindings, ordered;
- a bulk commit without an attachment;
- the three problem types, triggered end to end through the server's
  catalogue;
- the binding declarations read and refused;
- the server's gate per dataset;
- REC standalone.

The W3C SHACL suites are unchanged: 628 passing and the same 28 exempt cases
over the two subjects.

### Durations of the new test projects

Linux, the machine below, `dotnet test -c Release`. The other operating
systems' durations are CI's, added to the pull request when its run reports
them (#126).

| Project | Tests | Duration |
|---|---:|---:|
| `Varve.Derivation.Tests` (new) | 8 | 2.2 s |
| `Varve.Shacl.Store.Tests` (new) | 14, one skipped without `VARVE_LONG_PROPERTIES` | 16.5 s |
| `Varve.Shacl.Tests` | 34 | 7.2 s |
| `Varve.Shacl.Sparql.Tests` | 13 | 7.0 s |

With `VARVE_LONG_PROPERTIES`, the gate oracle's 5,000 commits ran in 18 s
and the REC S full validation in about 1 m 30 s.

### Benchmarks: commit latency under a `reject` binding

BenchmarkDotNet 0.15.8 on the file backend, REC 4.1.0's shapes without
Brick's three SPARQL constraints, the last 220 commits of the portfolio's
history after the rest was loaded. The workload and the reading are in
`tests/Varve.Benchmarks/README.md`. Slower rows first:

| REC | Binding | Mean per commit | StdDev | Allocated per commit |
|---|---|---:|---:|---:|
| M (≈5 M quads) | `reject` | 8.24 ms | 2.15 ms | 2,482.7 KB |
| S (≈203 k quads) | `reject` | 8.24 ms | 3.41 ms | 2,079.7 KB |
| M | none | 0.57 ms | 0.10 ms | 30.1 KB |
| S | none | 0.53 ms | 0.09 ms | 20.4 KB |

| REC | In the gate per commit | Affected focus nodes per commit (max) | Per affected focus node |
|---|---:|---:|---:|
| M | 6.30 ms | 2.6 (9) | 2.39 ms |
| S | 4.98 ms | 2.6 (8) | 1.93 ms |

The overhead does not grow with the dataset; it is the affected pairs'. 51 of
220 commits at M and 40 at S were refused, for the leases' missing
`rec:leasor`.

### Public API

New packages:
- `Varve.Derivation` (69 lines in `PublicAPI.Unshipped.txt`);
- `Varve.Shacl.Store` (26).

Changed, all additive:
- `Varve.Shacl` (43: `IncrementalValidation`, `IncrementalOutcome`,
  `IncrementalComparison`, the classification model, `IShaclFootprint`,
  `FootprintContext`, `ShapesGraph.Classify`,
  `ShaclOptions.RefuseNonDeterministicFunctions`);
- `Varve.Store` (2: `ValidationVerdict.Accept(IReadOnlyList<RdfTerm>)` and
  `Attachments`);
- `Varve.Protocol` (23: three problem types, `BindingDescription`,
  `IManagementPlane.CheckBindings` and `DescribeBinding`);
- `Varve.Management` (12: `ShapesMode`, and the binding declaration's
  graphs, mode and flags).

Nothing is removed. The pull request carries the diff.

### The pipeline

`dotnet build Varve.slnx -c Release` with `CS0618` as a warning is clean.
The gates `register`, `licence-headers`, `decision-sets`, `banned-symbols`,
`issue-refs`, `dco`, `release-pending`, `changelog` and `status` pass. The
tests of every project this change touches pass. `release` fails, and only
because `v0.1.0-preview.10`'s descriptor is on `main` but its tag is not cut:
with two proposed descriptors it attributes #134's ADRs 0163–0166 and 0168
to this release and this part's 0169–0173 to preview.10. It clears when
preview.10 is tagged.

### What this environment could and could not do

- BenchmarkDotNet builds Varve projects itself, with warnings as errors.
  While the decisions are unaccepted, it ran with `WarningsNotAsErrors=CS0618`
  in the environment.
- Only the `data-shapes` submodule was initialised. The conformance project's
  other suites fail here for want of their files, and CI runs them.
- Durations per OS other than Linux come from CI's run of the pull request.

### What the maintainer is asked for

- **Accept the decisions**, so `CS0618` clears. That means the five sets of
  ADRs 0169–0173.
- **Cut `v0.1.0-preview.10`'s tag**, so `release` passes here.
- **The REC prefix finding**: report it upstream with the OWL note, and
  decide in part 2b whether the REC gate composes the declarations, leaves
  the three constraints out, or waits for upstream.
