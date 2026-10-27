# Inference: the plan, and the grants ADR (0190)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The opening brief and the
> maintainer's answers are verbatim. The plan as submitted is the comment on
> #95 linked below, which stays where it was posted. The transcript itself is
> held by the maintainer.

| | |
|---|---|
| **Issue** | [#95](https://github.com/Hafeok/Varve/issues/95), milestone Inference; this session opened its parts [#170](https://github.com/Hafeok/Varve/issues/170) and [#171](https://github.com/Hafeok/Varve/issues/171) |
| **Date** | 2026-10-27 |
| **Tool** | Claude Code 2.1.311, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYPyBP5h8tEgWKGf4on` |
| **Branch** | `claude/sleepy-goldberg-91th34`, from `main` at 6cdb402 |
| **The plan** | <https://github.com/Hafeok/Varve/issues/95#issuecomment-6437332691> |

## The prompts

### The brief, `varve-claude-code-inference.md`

The opening message attached a zip whose `BOOTSTRAP.md` named this file as
the session prompt and repeated its instruction to plan first and wait for
approval.

> # Session: Inference — RDFS by rewriting, OWL 2 RL and rules as a projection, inconsistency at commit (#95)
>
> Ontologies can be stored in Varve but not used. This milestone adds entailment in three tiers matched to the OWL 2 profiles, with GraphDB's design as the reference and **the log kept asserted-only**: nothing inferred is ever written to it. It's in 1.0 (#15).
>
> **Starting point.** Current `main`. What this builds on has landed:
> - **`Varve.Derivation`** (layer 2, ADR 0172): `DependencyGraph` with stratification, `RegularPath` with anchors, and `Propagation` for semi-naive insertion. ADR 0172 point 4 names what Inference adds: deletion for least fixpoints, and anchors for join-shaped rule bodies.
> - **The validation projection** (ADR 0174): rows with validity intervals, generations, local-cache storage, `validation-behind`, lag never on readiness. Its rulings are the precedent for the closure.
> - **Commit-time gating** (ADRs 0169 and 0173) through the pre-commit validator of ADRs 0017 and 0058.
> - **`varve:TBoxBinding`** in the management vocabulary (ADR 0166): read and recorded, one `varve:boundGraph`, with no effect until this milestone.
> - **Graph-level authorisation** (ADR 0107): `GraphScope`, `GraphScopedQuadSource`, and `IAccessScopes`.
> - **The service description** advertises `sd:defaultEntailmentRegime` simple entailment. `sparql11/entailment` is not wired (`docs/spec/sparql-evaluation.md` §8).
> - **SHACL refuses `sh:entailment`** other than simple (ADR 0151).
>
> **In flight, touching what you touch:**
> - **Replication part 1 (#97)** holds ADRs 0179–0189 and touches the writer record, followers and forwarding.
> - **#151** (DD0019's nine findings) changes `GraphScopedQuadSource._decided` to a `ConcurrentDictionary.GetOrAdd`, and touches `Varve.Shacl.Store`.
>
> Merge `main` when either lands, and reconcile.
>
> **Read before planning:**
> - #95 in full with its amendment, and #15's Inference line;
> - ADRs 0107, 0163–0166 (the Management plane and its vocabulary), 0169–0176 (SHACL part 2), 0017 and 0058, 0016 (projections), 0138 (validity intervals);
> - `docs/spec/sparql-evaluation.md` (§7.2 on literals kept as written, and §8 on the suites), `docs/spec/storage-format.md` §7, and `docs/why.md`'s inference row;
> - **The specifications:**
>   - SPARQL 1.1 Entailment Regimes: §4 RDFS, §6 OWL 2 RDF-Based, §7 OWL 2 Direct and §8 D-entailment, with their conditions on BGP answers;
>   - RDF 1.1 Semantics §8–9 (RDFS entailment rules);
>   - OWL 2 Profiles §3 (QL) and §4.3 (the OWL 2 RL/RDF rules, tables 4–9);
>   - SPARQL 1.1 Service Description §3.2;
> - **GraphDB's documentation** on rulesets, explicit and implicit statements, and `owl:sameAs` optimisation, for behaviour only.
>
> Where a specification is silent, say so instead of guessing.
>
> `AGENTS.md` applies in full. **Plan first, and wait for approval.** Post the plan on #95. Your ADRs start at **0190**. If Replication's range overflows into yours at landing, renumber, as releases do.
>
> ## A. Decisions (Accepted unless stated; each becomes an ADR or a dated amendment)
>
> 1. **The grants ADR comes first, in its own PR.**
>    - Docs only, filed unaccepted, with no descriptor. The milestone's PR cites it as a decision set.
>    - **The rule:** an entailment is visible to a caller only if the caller can read every graph its premises came from.
>    - **Intra-graph inference is the default.** A conclusion is drawn from one data graph's quads plus the bound TBox graphs, and lands in that data graph.
>    - **A TBox-bound graph is readable by every caller with read on any graph of the dataset.** Amend ADR 0107 for that. Otherwise every entailment through the TBox would leak it or vanish.
>    - **Cross-graph inference is opt-in per binding.** Propose where a cross-graph conclusion lives, and how the premise graph set is recorded, so that the read filter stays a lookup per graph handle (0107 point 3) rather than a check per quad.
>    - **The property:** over generated grants, datasets and rules, no caller sees an entailment it couldn't derive from what it can read, and every caller sees every entailment it could.
>
> 2. **Packages and layers.** Check each against VARVE0003–0005 and ADR 0003.
>    - **`Varve.Sparql.Entailment`, layer 3.** Query rewriting over the algebra (`Varve.Sparql`, layer 2) and `Varve.Derivation` (layer 2). It reads the TBox through `IQuadSource`. It is usable over any dataset, without the store.
>    - **`Varve.Inference`, layer 4.** The rule engine, OWL 2 RL/RDF as rules, custom rules, and the deletion algorithm. It is layer 4 because SPARQL `CONSTRUCT` rules need the evaluator and `sh:rule` needs `Varve.Shacl`, both at layer 3. It is usable over any in-memory dataset.
>    - **`Varve.Inference.Store`, layer 5.** The projection, the pre-commit validator for tier 3, and the TBox binding's effect. `Varve.Store` learns nothing about inference.
>    - **Two layer-5 packages can't reference each other.** That bears on point 9. Hosts compose them.
>
> 3. **Tier 1: query-time rewriting.**
>    - **RDFS.** Rewriting is selected through the SPARQL 1.1 entailment regimes and advertised in the service description: `sd:entailmentRegime` per dataset, and `sd:supportedEntailmentProfile` where it applies. **Simple entailment stays the default.**
>    - **Selecting a regime.** SPARQL 1.1 Protocol defines no request parameter for this. Propose one. My lean is a header, `Varve-Entailment`, for consistency with `Varve-As-Of`, with an amendment to ADR 0132's CORS header list. A dataset default can be set through the TBox binding.
>    - **State the fragment the rewriting is complete for.** My expectation is ρdf (`subClassOf`, `subPropertyOf`, `domain`, `range`, and `type`), with variables in every position resolved against the TBox at the pinned position.
>    - **State what it isn't complete for:** axiomatic triples, container membership, reflexive `subClassOf`, and meta-queries over the TBox. For each, say whether it's answered by the projection, refused with a problem (propose its name), or answered per the regime's restrictions on answers (Entailment Regimes §4.1–4.2).
>    - **The TBox is read as of the pinned position,** so an as-of read rewrites against the ontology as it was then.
>    - **Literals.** Varve stores typed literals by value (`sparql-evaluation.md` §7.2), which is already partly D-entailment. Say how the RDFS regime's literal conditions (RDF 1.1 Semantics §9, and Entailment Regimes §4) meet that, and which suite cases it decides.
>    - **OWL 2 QL.** Rewrite the BGPs to unions of conjunctive queries, PerfectRef-style. The rest of the algebra evaluates over the rewritten BGPs, under the OWL 2 Direct Semantics regime's conditions (§7). If the plan shows QL is the part that doesn't fit, propose moving it to a second part. Don't thin it.
>
> 4. **Tier 2: OWL 2 RL and custom rules, materialised as a projection.**
>    - **`InferenceProjection`,** one per TBox binding: an `IProjection`, rebuildable from the log, with its own position. It is never written to the log.
>    - **Rows** are entailed quads with validity intervals `[asserted, retracted)`, as ADR 0174's rows are, so the closure is readable as of any position.
>    - **Insertion is semi-naive,** with `Propagation` (ADR 0172).
>    - **Retraction: decide by measurement, in an ADR.** The candidates are:
>      - DRed;
>      - support counting, which is sound only for non-recursive strata unless extended;
>      - Backward/Forward (Motik, Nenov, Piro and Horrocks, AAAI 2015), which RDFox uses.
>
>      Measure all three on REC M histories with retractions, at a rate you state. Report closure size, time per commit, and the worst case. Whichever wins, the deletion pass is a second `Propagation`, not a change to it (ADR 0172 point 4).
>    - **Scoped rebuild on a TBox or rules change.** A commit that changes a bound TBox graph, or the rules, starts a generation, as ADR 0174 point 4 does. A generation is published whole, so a reader never sees a mixed closure. Say whether a generation can be cheaper than from scratch when only part of the TBox changed. If not, the from-scratch closure is the guarantee.
>    - **Custom rules:**
>      - SPARQL `CONSTRUCT` with a restricted body. State the restriction: no aggregates, no subqueries, and no `NOW()` or `SERVICE`, with stratified negation only through `DependencyGraph`;
>      - SHACL `sh:rule` (triple rules and SPARQL rules). Name the specification version you follow: the SHACL Advanced Features note, or the SHACL 1.2 Rules draft as of a stated date.
>
>      Rules are data, in a graph named by the binding.
>    - **Provenance: "why is this here" as a query** (`docs/why.md`). My lean is to explain on demand, by finding one rule instance whose premises hold at the read position, and to store nothing per entailment except what the grant filter needs (point 1). Propose the read surface: a SPARQL function, a reserved graph, or a route. The data explorer (#96 part 2) reads it.
>    - **Storage:** the local cache only, rebuilt on a cold start, as ADR 0174 rules for validation. Measure the cold-start rebuild at REC M and L. If it's past what an operator would accept for a restart, propose sharing the closure as writer-written extents as a separate decision. Don't build it in this milestone.
>    - **Verified on read** (#146): the closure's local cache is in #146's scope (`derived/inference/`). A damaged entry is a miss, never a wrong entailment.
>    - **Lag:**
>      - reported per binding on readiness, and never making the dataset unready, as ADR 0174 ruled;
>      - a read whose regime needs the closure at a position the projection hasn't reached is `503 inference-behind` with `Retry-After`. Propose it for the catalogue (ADR 0119).
>
> 5. **TBox bindings are management declarations** (#105).
>    - Extend ADR 0166's `varve:TBoxBinding` by amendment, not by a parallel vocabulary, with:
>      - the TBox graphs;
>      - the data graphs;
>      - the profile: `rdfs`, `owl2rl`, or rules only;
>      - the rules graph;
>      - cross-graph opt-in;
>      - the `sameAs` switch;
>      - the regime default;
>      - the tier-3 mode.
>    - Validated by shapes at runtime, as ADR 0175 does for every declaration, with the code oracle in the tests.
>
> 6. **Views.**
>    - **`varve:explicit` and `varve:implicit`** are reserved IRIs usable in `FROM` and `FROM NAMED`, as GraphDB's pseudo-graphs are. Define their meaning with named graphs precisely: the explicit or the implicit quads of each graph, and the default graph under each.
>    - Writing them is `graph-not-writable`, as for the report graph (ADR 0174).
>    - **`owl:sameAs` as equivalence classes,** with a representative per class, rather than replicating every quad across the class. GraphDB's and RDFox's approach (the OWL 2 RL `eq-*` rules). The per-dataset switch is `off` (`sameAs` is an ordinary triple) or `classes`.
>    - **"Bounded"** means the materialised size is linear in the data, not in class size squared. A property shows it on generated cliques.
>
> 7. **Tier 3: inconsistency as a pre-commit validator.**
>    - The OWL 2 RL rules with `false` heads (Profiles §4.3: `eq-diff*`, `prp-irp`, `prp-asyp`, `prp-pdw`, `prp-adp`, `cax-dw`, `cax-adc`, `cls-nothing2`, `cls-com`, `cls-maxc1`, `cls-maxqc1` and `cls-maxqc2`, and the rest the table names).
>    - They are checked incrementally over the pending transaction overlaid on the pinned position (ADR 0017), joined with the closure.
>    - **The mode is the binding's:**
>      - `reject`: `422` with the problem type `inconsistent`, carrying the rule and one witness set per violation;
>      - `report`: the finding is attached, as ADR 0173 attaches the validation report.
>    - **Only the writer gates.** Forwarded writes reach it (ADR 0168).
>    - **A TBox change** that makes existing data inconsistent is never refused because of that data, as ADR 0169 rules for a shapes change. The projection reports it.
>
> 8. **Hosts.**
>    - The server composes `Varve.Inference.Store`.
>    - **The browser host gains tier 1 only.** `Varve.Sparql.Entailment` must run under `browser-wasm`: add it to `Varve.WasmSmoke`, and report its size cost against the browser size measurements.
>    - `Varve.Inference` stays usable standalone and trimmed.
>
> 9. **SHACL and entailment.** `sh:entailment` stays simple-only in this milestone (ADR 0151). Validating over the closure would put a layer-5 package on top of another, or need a contract lower down, and it would put the closure's lag on the commit path.
>    - Record the decision.
>    - Name the seam that would let a later milestone do it: a host-composed `IQuadSource` over explicit and implicit quads, given to the validator.
>    - Say what `sh:targetClass` already covers through `rdfs:subClassOf*` (ADR 0171).
>
> 10. **REC and Brick** (#92, the REC reference domain ADR 0141).
>     - **Tier 1** rewriting over the REC and Brick class hierarchies, on generated portfolios at S and M, equals materialisation.
>     - **Brick** needs pinning in `testdata/` with an ADR for the content and its licence. Report what Brick needs beyond its hierarchy (`owl:equivalentClass` and restrictions, for instance) and which tier answers it.
>     - A REC or Brick defect goes upstream, with a written exemption.
>
> ## B. Definition of done
>
> - **The suites under the ratchet:**
>   - **RDFS:** `sparql11/entailment`, wired for the RDFS regime, and the RDF 1.1 Semantics cases (`rdf-mt`) that are RDFS;
>   - **OWL 2 RL:** the W3C OWL 2 conformance cases tagged for the RL profile (positive and negative entailment, consistency and inconsistency). Find their current maintained source and pin it. If none is maintained, say so and pin what exists.
>
>   Every failure needs a written exemption.
> - **Incremental equals from scratch** at every position of generated histories with retractions, TBox changes and rule changes. That holds byte for byte in canonical N-Quads, for each deletion candidate measured.
> - **As-of reads of the closure** equal the from-scratch closure of the log at that position.
> - **Rewriting equals materialisation** for RDFS over the stated fragment. Add the same for QL, if QL lands in this part.
> - **The grant property** (point 1).
> - **`sameAs` bounded** (point 6).
> - **Tier 3** refuses exactly the commits that make the closure inconsistent, against a from-scratch oracle.
> - **REC and Brick** (point 10).
> - **Benchmarks,** with the slower rows first:
>   - query latency under RDFS rewriting against simple entailment;
>   - closure size and materialisation time;
>   - maintenance per commit;
>   - the deletion candidates;
>   - commit latency with tier 3 in `reject` mode;
>   - cold-start rebuild at REC M and L.
> - **The public API baseline diff** in the PR body.
> - **The close-out:**
>   - README Status, `docs/roadmap.md` (Inference complete), AGENTS.md's State, `eng/status.cs` and the changelog;
>   - `docs/operator/inference.md`;
>   - `docs/why.md`'s inference row marked shipped;
>   - `docs/problems/` for `inconsistent`, `inference-behind` and any rewriting refusal;
>   - the traceability record.
>
>   **Chapter 12 of the Learning track is #94's.** It isn't on `main`, so leave a note on #94 with what the chapter needs.
>
> ## Plan questions
>
> 1. The grants ADR (point 1), including where cross-graph conclusions live.
> 2. Packages and layers (point 2).
> 3. Tier 1:
>    - the RDFS fragment, completeness, and what's refused;
>    - regime selection;
>    - literals;
>    - QL, and whether it stays in this part.
> 4. Tier 2:
>    - rows, generations and storage;
>    - the deletion candidates and how you'll measure them;
>    - the rule language;
>    - provenance's read surface.
> 5. The TBox binding's terms, amending 0166.
> 6. The views and `sameAs`.
> 7. Tier 3: the rule list, the problem, and the commit-time cost.
> 8. One PR or two, by size. If two, a natural seam is:
>    - part 1: grants, tier 1 RDFS, tier 2 OWL RL, and the binding;
>    - part 2: tier 3, `sameAs`, QL, `sh:rule`, REC and Brick, and the close-out.
>
>    The last descriptor closes #95, and an earlier part gets its own issue.
> 9. ADR numbers from 0190, and amendments: 0107, 0119, 0132, 0151 and 0166 at least; 0172 if `Varve.Derivation` gains deletion or join anchors; 0174 if the projection shares machinery with it.
>
> ## How this lands
>
> - **The grants ADR is its own PR, docs only.** Then each part is one PR, red only on `CS0618`, with a descriptor at the next free `0.1.0-preview.N` (ADR 0102).
> - **The descriptors** carry `accepted-by: mailto:emil@okkels-klein.dk` and a `Closes` trailer for their part's issue. The last one closes #95. Use bare #N elsewhere, and no closing keyword in PR descriptions (#108).
> - **Commits:**
>   - signed with the agent SSH key, as `Claude (Emil) <claude+emil@okkels-klein.dk>`;
>   - reference #95, with the DCO sign-off;
>   - never name a model.
> - **A `docs/traceability/` record** per part (ADR 0033).
> - **No suppression or downgrade of DD or VARVE rules.** What runs per rule firing, per changed quad and per rewritten pattern is `[HotPath]`.
> - **New test content** (OWL 2 cases, Brick) is pinned by hash, under an ADR for the content's licence.
> - **CI images** only from `.github/ci-images.yaml`.
> - **Test time:**
>   - size land-gate iteration counts, and put full depths in the nightly workflow (`docs/testing.md` §8);
>   - report durations per OS (#126);
>   - put the OWL 2 suites in their own named job.
> - **Land through `land/`.**

### The maintainer's answers to the plan

> Approved, with these answers and changes. Your six corrections stand. The prompt was wrong on literals and on the section numbers.
> D1: I’ll ask W3C through site-policy about the OWL 2 test cases’ licence. Don’t vendor them until it’s settled. Part 1’s RL gate is then the 28 RL cases of `sparql11/entailment` (under `rdf-tests`’ licence) plus the properties. The `owl2-conformance` job becomes part 2’s gate. If the licence is still open when part 2 is ready, stop and say so.
> D2: yes to both. The rules graph gets the TBox read rule, and `varve:Rewriting` is the fourth profile. One addition: `varve apply --plan` names every graph a binding makes dataset-public (TBox and rules graphs). The widening must be visible at apply, and a property tests it.
> D3: blocked as out of scope, not exempted. List the RIF and DL-only cases by name, cite #15’s scope line, and show their count in `eng/status.cs`.
> D4: yes, with three additions:
>
> * Gated writes wait for the closure. After a cold start, and during a generation started by a TBox or rules edit, gated writes are `503 inference-behind` until the closure catches up. Measure that write downtime at REC M (cold start, and a TBox edit near Brick’s `Point` root) and state it in `docs/operator/inference.md`.
> * A TBox or rules commit isn’t gated (R5). Its generation runs off the commit path. Data commits behind it wait, as above.
> * Amend ADR 0180 (Replication’s warm standbys, by then on `main` or on #165’s branch). A standby with a `reject` inference binding is warm only when that closure has reached its applied position. Validation projections stay out of warm, because shapes gating doesn’t read them. Tier 3 reads its closure, so it’s different.
>
> D5: three parts, not two. Part 1 as planned would be the largest PR Varve has had, and tier 1 stands on its own.
>
> * Part 1, tier 1: packages (0191), regimes over HTTP and the service description (0192), RDFS, RDF and D by rewriting (0193), and the binding vocabulary (0194), where `Rewriting` is the only profile that does anything and the materialising profiles are refused as reserved. Also the RDF, RDFS and D cases of `sparql11/entailment` and `rdf-mt` that the rewriting decides, the browser smoke, and the rewriting benchmarks. Grants (0190) are already in by then.
> * Part 2, the inferred quads as a projection: the projection (0195), retraction by measurement (0196), OWL 2 RL/RDF as rules (0197), the views and reserved graphs (0198), and explain (0199). Also the RDFS profile’s clash cases, the RL cases, the OWL 2 test content (0206, if D1 allows), and the cache-damage gate.
> * Part 3: tier 3, `sameAs` classes, OWL 2 QL, custom rules, Brick 1.4.4, the REC and Brick gates, and the close-out. Its descriptor closes #95.
>
> Open part 1’s and part 2’s issues now, as sub-issues of #95.
> D6: yes, pin Brick 1.4.4 as planned.
> ADR numbers. The Web UI holds 0200–0205, so:
>
> * “SHACL and entailment” is a dated amendment to ADR 0151, not an ADR of its own;
> * the OWL 2 test content is 0206;
> * part 3 is 0207 (inconsistency at commit), 0208 (`sameAs`), 0209 (QL), 0210 (custom rules) and 0211 (Brick 1.4.4).
>
> 0190–0199 stand as planned.
> Also:
>
> * Cross-graph growth. Measure the number of distinct premise sets, and so of reserved graphs, on a REC M cross-graph binding over per-building graphs. Report it per binding on readiness. If it grows faster than linearly in the bound graphs, say so in the ADR before choosing anything.
> * Cold starts and history. Every cold start puts the floor at the head, so as-of closure reads below it are from scratch on every instance, after every restart. The guide says so. The property already covers correctness.
> * The dictionary first. Verify that the dictionary is append-only and that erasure keeps handles stable before building rows over store handles. Report it in part 2’s plan comment.
> * Merge `main` when #165 or #151 lands.
>
> Start with the grants PR.

## The report

### The plan

The session read #95, #15, the ADRs and specifications the brief names, the
code the milestone builds on, and the vendored REC and Brick files, and
counted the `sparql11/entailment` and `rdf-mt` manifests by script. A
research agent of the same session gathered the external sources: SPARQL 1.1
Entailment Regimes, RDF 1.1 Semantics, OWL 2 Profiles, the OWL 2 test
archive, Brick's releases, GraphDB 11.5's reasoning pages, SHACL Advanced
Features and SPARQL 1.2 RL, and the three Motik et al. papers.

The plan corrected six points of the brief, all of which the maintainer
accepted:
- Varve keeps literals as written, and storing them by value is Oxigraph's
  behaviour (`sparql-evaluation.md` §13).
- The suites are in `sparql-evaluation.md` §12.1, and D-entailment is §5 of
  Entailment Regimes, where §8 is RIF.
- SHACL 1.2 Rules is now SPARQL 1.2 RL (Working Draft of 2026-10-21).
- The OWL 2 test cases are frozen, their wiki gone and their licence unstated.
- Brick 1.3.0, as REC patches it, is already vendored, and the current Brick
  is 1.4.4, with no OWL restrictions.
- #151 is, today, a dependency bump.

The maintainer approved the plan with three parts instead of two, the
answers to D1–D6, and the ADR numbers above: 0190–0199 and 0206–0211, with
point 9 as an amendment to ADR 0151.

### This pull request: ADR 0190

Docs only, filed unaccepted, with no release descriptor:
- `docs/adr/0190-entailments-under-graph-level-grants.md` and its decision
  set `docs/decisions/entailments-under-graph-level-grants.md`, 15 rulings;
- a dated amendment to ADR 0107 (dataset-public graphs, a graph governed by a
  set);
- the ADR index's new section.

D2's answers are in it: the rules graph is read as the TBox is, and the plan
at apply names every graph a binding makes dataset-public (point 5), with a
property. The cross-graph growth measurement is owned by #171, as the
maintainer asked. Nothing in `src/` changes, so no citation is `CS0618` yet.
The parts that build on it cite it.

### Left for the parts

- **#170, part 1:** tier 1, ADRs 0191–0194.
- **#171, part 2:** the projection, ADRs 0195–0199, and 0206 if D1's licence
  allows.
- **Part 3:** ADRs 0207–0211, the amendment of 0151 and the amendment of
  ADR 0180 for warm standbys under a `reject` binding (D4). Its issue is
  opened when part 2 lands. Its descriptor closes #95.

Developed with AI assistance under human review.
