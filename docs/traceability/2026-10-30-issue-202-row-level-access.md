# Milestone 9, part 1: row-level access through the classifier

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The opening message, the
> brief, the plan as submitted and the approval are verbatim. The transcript
> itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#202](https://github.com/Hafeok/Varve/issues/202), the first part of milestone 9 ([#14](https://github.com/Hafeok/Varve/issues/14)) |
| **Date** | 2026-10-30 |
| **Tool** | Claude Code 2.1.314, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata (`configured_model`, `session_context.model`, `last_served_model`) |
| **Session identifier** | `session_01NbWSYTG4DgWJfLddwojMbT` |
| **Branch** | `claude/zealous-bohr-0w9lkc`, from `main` at ea62fc6; `main` did not move during the session |
| **Machine** | a cloud container, Intel Xeon @ 2.10 GHz, 4 cores, 15 GiB, Ubuntu 24.04.5 LTS, .NET SDK 10.0.403, runtime 10.0.14 |

## The prompts

### The message that started the session

> @"/root/.claude/uploads/df0e8c26-8fb7-5b04-8f1a-c6dd7207242d/fd0da45d-varve-row-level-access.zip" Read varve-claude-code-row-level-access.md in the attached zip. It is your session prompt for milestone 9, issue #14 in Hafeok/Varve: row-level access through the classifier (1.0, stable) first, then erasure mode (preview). Work on current main and follow AGENTS.md in full. Run git config core.hooksPath eng/hooks before your first commit.
>
> Plan first. Post the plan on #14, answering every plan question, and wait for approval before writing any code. Your ADRs start at 0230.

### The brief, `varve-claude-code-row-level-access.md`

> # Session: Milestone 9 — row-level access through the classifier, then erasure mode (#14)
>
> #14 holds two things with different fates at 1.0 (#15, 2026-10-19):
> - **Row-level access** is in 1.0 and stable. The classifier, the selector contract and row-level enforcement are public API under the freeze, usable with erasure mode off, and **not gated on ADR 0028's external review**.
> - **Erasure mode** (crypto-shredding, the key store, private terms, `T4 Erase`) ships as preview only, gated on that review.
>
> This session plans both, and builds row-level access first, as its own part and release.
>
> **Starting point.** Current `main`. What exists:
> - ADRs 0019 and 0023 (erasure and access requests: the key store, classifier and selector contracts as specified, I9 and I10, `Access(K)`), 0028 (the deterministic AEAD) and 0074 (private ids reserved, the key store refused inside the dataset directory, the store refusing a log with private entries);
> - spec `docs/spec/log-and-projection-model.md` §9 and Q4, Q5, Q8 and Q9;
> - graph-level authorisation (ADR 0107, `GraphScope`, `GraphScopedQuadSource`) and its extension to entailments (ADR 0190: minimal premise sets, the read filter a lookup per graph handle, the grant property over every surface);
> - the management vocabulary reserving `varve:ClassifierBinding` and `varve:KeyStoreBinding` (ADR 0166);
> - the validation projection and its report graph (ADR 0174);
> - the change feed and diff filtered by grants (ADR 0107 point 5);
> - the `IQuadSource.TryInternalise` contract (ADR 0022 amended by #200): a source never denies a term it hasn't looked for yet.
>
> **In flight, touching what you touch:**
> - **Replication** (#165, PR #175; then #166): followers, shipping, promotion.
> - **Inference** (#170, PR #178; then #171): new quad sources (closure, views, explain) under ADR 0190.
> - **Web UI** (#167, PR #187).
> - **Readiness A and B** (#129, #119).
>
> Merge `main` as they land.
>
> **Read before planning:**
> - #14 with its two amendments, and #15's lines on row-level access and erasure;
> - ADRs 0019, 0023, 0028, 0074, 0107 with its amendments, 0190, 0174, 0163–0166, 0017 and 0058 (the pre-commit validator), 0016 (projections), and 0022 as amended;
> - `docs/spec/log-and-projection-model.md` §9;
> - the Learning track's planned chapter on multi-tenancy and row-level security (`docs/roadmap.md`, the Learning track section);
> - the shacl-rs repository and `Varve.Shacl` for anything that can derive a classifier from annotated shapes.
>
> `AGENTS.md` applies in full. **Plan first, on #14, and wait for approval.** Your ADRs start at **0230**. Earlier ranges: Replication to 0189, Inference 0190–0212, Web UI 0200–0206, readiness A 0213–0222, readiness B from 0224. Renumber at landing if a range overflows.
>
> ## A. Decisions (Accepted unless stated)
>
> 1. **Two parts, two releases, two sub-issues of #14:**
>    - **Part 1, row-level access (1.0, stable):** the classifier and selector contracts, classification without erasure mode, classifier bindings, row-scoped grants, and enforcement on every read surface.
>    - **Part 2, erasure mode (preview):** the key store and its binding, private terms, `T4 Erase`, access requests by key id, the classification gate, Q4, Q5 and Q8, the replica gate, and the external review.
>
>    Part 1 doesn't wait on Replication. Part 2's replica gate (a tier-1 and a tier-3 follower) does.
>
> 2. **What row-level access means. Propose it precisely in an ADR. My starting point:**
>    - **The classifier** assigns term occurrences in quads to **data subjects** (ADR 0019's contract). Row-level access uses that assignment without encryption: a quad is **about** the subjects its classified occurrences belong to.
>    - **A grant can be scoped to subjects** as well as to graphs: a subject set given by subject IRIs, by a rule (for example "subjects linked to the caller's agent by `ex:caseworker`"), or by a class. Propose the forms. Keep them declarative, in the grant vocabulary of ADR 0166, applied by `varve apply`.
>    - **A quad is visible to a caller** when its graph is readable (ADR 0107) and every subject it is about is in the caller's subject set. A quad about no subject is governed by its graph alone.
>    - **Writes:** propose whether a caller may write a quad about a subject it can't read. My lean is no, refused as `graph-not-writable` is today, under a row-level problem type. A caller must not be able to probe for a subject by writing about it (the lesson of ADR 0190 point 7).
>
> 3. **Classification is a projection,** not something stored in the log, so it works with erasure mode off.
>    - The classifier is a pure function of a log prefix and the bound classifier configuration. Its output (occurrence to subject) is maintained from exact deltas, rebuildable, readable as of any position, verified on read (#146), and on readiness like any projection.
>    - In erasure mode (part 2), the same assignment chooses which occurrences become private ids at commit time. Then classification also enters the log as key ids, and the projection must agree with it.
>    - **Propose** how the classifier is expressed: shape-derived (annotated SHACL shapes, layer 5, as ADR 0019 says), query-derived, or both, and which ships first.
>    - **The determinism rule:** classification at p depends only on the log up to p and the bindings in force at p.
>
> 4. **The read filter's cost.** ADR 0107 keeps the read filter a lookup per graph handle, never a check per quad. Row-level access is per quad by definition, so state its cost honestly and keep it bounded:
>    - decisions per term handle, memoised per request (is this term about a subject the caller can't read?), so a quad's check is a lookup over at most four handles;
>    - the `all` subject set builds no wrapper at all, as `all` graphs doesn't today. A caller with no row restriction pays nothing;
>    - `[HotPath]` with its allocations answered, and measured at REC M against graph-only scoping.
>
> 5. **Every surface, under the grant property.** Extend ADR 0107's and ADR 0190's properties to rows. Over generated datasets, classifier configurations and subject-scoped grants, no caller sees a quad about a subject it can't read, through any surface, and every caller sees every quad it may read. The surfaces:
>    - SPARQL query (including `GRAPH ?g`, `FROM`/`FROM NAMED` and property paths);
>    - Graph Store `GET`;
>    - the change feed, the diff and `/commits/{p}`;
>    - the service description;
>    - **the validation report graph** (ADR 0174): a report result about a subject the caller can't read is not visible. Propose whether the result is withheld, or the report graph is unreadable for that caller;
>    - **inference** (ADR 0190): an entailment is visible only if the caller could derive it from the quads it can read. Minimal premise sets per graph aren't enough once premises are per-row restricted. Propose the rule, and if the honest answer is that a binding with row-scoped grants must refuse some inference features (cross-graph, or materialised closure), say so. This needs the Inference session's agreement, so post it on #171;
>    - **explain** (Inference part 2) and the Web UI (#96);
>    - **as-of reads:** classification as of the read's position, and grants as of the management position (ADR 0165).
>
> 6. **The contract test applies.** Every new quad source this milestone adds (the row-scoped view, the classification projection's reads) passes `tests/QuadSourceContract.cs` (#200). A row-scoped source never denies a term it hasn't classified yet.
>
> 7. **Replication.** Classification is derived, so every follower computes the same assignment at the same position, and enforces the same rows. The gate shows it on a tier-1 follower in part 1. Part 2 adds ADR 0019's key-destruction gate on a tier-1 and a tier-3 follower.
>
> 8. **Part 2's open questions,** each answered in the plan or carried as an explicit question for the maintainer:
>    - **Q4:** how a shredded term appears in SPARQL results and serialisations;
>    - **Q5:** lookup by private value, by scan and decrypt or by a keyed blind index (which weakens I10);
>    - **Q8:** key granularity when one term is about two subjects;
>    - **Q9** is legal, not engineering. Record it as such, with the classifier's ability to make identifying links private as the engineering half.
>    - **The external review (ADR 0028):** what is sent, to whom, and the fallback ADR if it is rejected (erasure mode not in the browser). The session prepares the review package. It doesn't choose the reviewer.
>
> ## B. Definition of done (part 1)
>
> - The row-level grant property over every surface in point 5, with iteration counts, at land-gate and nightly depths.
> - Classification: incremental equals from scratch at every position, as-of reads of it, and cache damage gives a miss.
> - The write rule (point 2), with the probe case: a write's outcome never depends on a subject the writer can't read.
> - The read filter's cost at REC M, measured with BenchmarkDotNet and slower rows first, against graph-only scoping. A caller with an `all` subject set measures as today.
> - A tier-1 follower enforces the same rows at its reported position.
> - The management vocabulary: `varve:ClassifierBinding` and the subject-scoped grant terms, by amendment to ADR 0166, validated by shapes (ADR 0175).
> - The public API baseline diff in the PR body: these are stable 1.0 contracts, so name each new public member's reason.
> - **The close-out of part 1:**
>   - README Status, `docs/roadmap.md`, AGENTS.md's State, `eng/status.cs` and the changelog;
>   - `docs/operator/row-level-access.md`;
>   - `docs/problems/`;
>   - the traceability record;
>   - a note on #94 for the Learning track's multi-tenancy chapter.
>   - Tick #15's row-level access box when part 1 lands.
>
> ## Plan questions
>
> 1. The precise meaning of row-level visibility, the subject-set forms, and the write rule (point 2).
> 2. How the classifier is expressed, and which form ships first (point 3).
> 3. The classification projection: storage, maintenance, as-of reads, and agreement with private ids in part 2.
> 4. The read filter's design and its expected cost (point 4).
> 5. The validation report and inference under row-level grants (point 5). Propose a rule, and post the inference half on #171.
> 6. Part 2's plan: the key store, private terms, `T4`, the classification gate, Q4, Q5 and Q8, and the review package.
> 7. The split into two sub-issues, and the ADR numbers from 0230, with amendments (0107, 0166, 0190 and 0174 at least; 0019 and 0023 where the row-level use changes them).
>
> ## How this lands
>
> - **Each part is one PR,** red only on `CS0618`, with its own sub-issue of #14 and a descriptor at the next free `0.1.0-preview.N` (ADR 0102). The descriptor carries `accepted-by: mailto:emil@okkels-klein.dk`, which you write, and a `Closes` trailer for its part's issue. Part 2's descriptor closes #14. Use bare #N elsewhere, and no closing keyword in PR descriptions (#108).
> - **Commits:**
>   - signed with the agent SSH key, as `Claude (Emil) <claude+emil@okkels-klein.dk>`;
>   - **never `git commit -s`.** Set `git config core.hooksPath eng/hooks`, and the hook adds `Signed-off-by: Emil Okkels Klein <emil@okkels-klein.dk>`;
>   - `Co-Authored-By: Claude <noreply@anthropic.com>`. No model name in a commit message; model ids are allowed, and the traceability record names the model;
>   - reference #14.
> - **A `docs/traceability/` record** per part (ADR 0033).
> - **No suppression or downgrade of DD or VARVE rules.**
> - **CI images** only from `.github/ci-images.yaml`.
> - **After #179 lands,** run `dotnet run eng/lockfiles.cs` whenever you add a project or a package.
> - **If you port code from another open PR** onto your branch, say so in the PR description (the #172 lesson).
> - **Size test iteration counts** for the land gate, with full depths in the nightly (`docs/testing.md` §8), and report durations per OS (#126).
> - **Land through `land/`.**

### The plan as submitted

Posted on #14 at https://github.com/Hafeok/Varve/issues/14#issuecomment-6520440332, and the
inference half on #171 at https://github.com/Hafeok/Varve/issues/171#issuecomment-6520445643.

> # Plan — milestone 9 (#14): row-level access through the classifier, then erasure mode
>
> Plan only. No code until it is approved. Base: `main` at `ea62fc6`. ADRs from **0230**.
>
> Read for this plan: #14 with both amendments; #15's lines on row-level access and erasure; ADRs 0016, 0017, 0019, 0022 (with #200's wording of the contract), 0023, 0028, 0058, 0074, 0107 and its four amendments, 0163–0166, 0169–0174, 0190; spec §1, §6, §7, §9 and Q4–Q9; the Learning track and milestone 9 sections of `docs/roadmap.md`; `GraphScope`, `GraphScopedQuadSource` and `IProjection` in the code.
>
> **In one paragraph.** A **row** is every quad of a classified graph that has the same subject-position term, the row's **node**. The classifier assigns each node a set of **data subjects**. A quad is **about** the subjects of its row's node, and it is visible when its graph is readable and its row's subjects are all in the caller's subject set. Classification is a projection, so it works with erasure mode off. Grants gain subject sets, declared in ADR 0166's vocabulary. The read filter is one lookup per quad on the row's node, allocation-free, and it is never built when the subject set is `all`. A row-restricted writer can write only rows that are already wholly its own, so a write's outcome never depends on a row it cannot read. Two surfaces cannot keep the grant property under row-restricted grants, and are refused for those callers: the validation report graph, and the materialised closure of inference. That second refusal needs the Inference session's agreement, which I ask for on #171.
>
> ---
>
> ## Q1 — What row-level visibility means, the subject-set forms, and the write rule
>
> ### 1.1 Rows, not occurrences, decide visibility
>
> ADR 0023's classifier "assigns term occurrences to data subjects", and the starting point says a quad is about the subjects its classified occurrences belong to. I propose a stricter form of that assignment. **Every occurrence in a quad is assigned to the subjects of the quad's subject-position term.** A quad's object never makes it about anyone else. The reason is probing. If an object occurrence could add a subject, `alice ex:knows ?x` would be visible or hidden depending on whether `?x` is someone else's subject. A writer could then learn that by writing `alice ex:knows x` and watching the outcome. With rows:
>
> - `alice ex:knows bob` is in alice's row and about alice's subjects, whoever bob is. A reader of alice sees bob's IRI as alice's data. Hiding the IRI itself is erasure's job in part 2 (Q9).
> - A row is **all or nothing**: every quad with node `s` in the binding's graphs has the same about-set. So any visible quad of `s` means all of `s`'s row is visible.
>
> Definitions, over a **classifier binding** B with a graph set Γ_B. The classification is computed over the union of Γ_B as one data graph, as a shapes binding validates its graphs (ADR 0174 point 5):
>
> - **A data subject** is a node the classifier names as one. Under shape-derived classification (Q2), it is a focus node of a shape annotated `varve:dataSubject true`.
> - **subjects_p(s)** is the set of data subjects node `s` belongs to at position p. It is the least set such that:
>   - it contains `s` when `s` is a data subject;
>   - it contains subjects_p(n) for each `(s ℓ n)` in Γ_B where ℓ is a **link predicate** of the binding.
>
>   Ownership is transitive through links. A space belongs to a lease, and the lease to a tenant, as in REC. Each input of subjects_p(s) is either a quad of `s`'s own row, or the subjects of a node `s`'s row links to.
> - **about_p(q)** = subjects_p(s(q)) for q in a graph of Γ_B. A quad in any other graph is about nothing.
> - A triple term in subject position has the node of its own subject, recursively.
>
> ### 1.2 Visibility
>
> A caller C reading at position p sees quad q in graph g when both of these hold:
>
> 1. g is readable (ADR 0107, with its governors and dataset-public sets, unchanged);
> 2. g is not in any Γ_B, **or** about_p(q) ⊆ S_B(C).
>
> S_B(C) is C's **subject set for binding B**: the union of the subject sets of C's `read` and `write` grants that cover a graph of Γ_B. It is `all` when any such grant has no subject terms, so **every existing grant means what it meant**. An empty about-set ⊆ anything, so an unclassified row is governed by its graph alone, as the starting point asks.
>
> The subject set is per binding, not per graph. That matters for the precondition in 1.5, which is what lets the probe argument hold across graphs.
>
> ### 1.3 The subject-set forms
>
> These are declared on a `varve:Grant` (ADR 0166 point 3), applied by `varve apply`, and validated by `shapes/management.ttl` (ADR 0175) plus the residual code check:
>
> | Term | Subject set |
> |---|---|
> | *(none)* | `all`. Today's grants, unchanged. |
> | `varve:subject <iri>` | explicit subjects; repeatable |
> | `varve:subjectPrefix "…"` | subjects whose IRI starts with the prefix; repeatable. This is the tenant-namespace case. |
> | `varve:subjectTemplate` → `varve:SubjectTemplate` | ADR 0166's template, `{sub}`, `{agent}`, `{claim.<name>}`, `varve:Exact` or `varve:Prefix`. This is "the caller is their own subject" and "the caller's tenant's prefix". |
> | `varve:subjectClass <C>` | data subjects with `(s rdf:type C)` in Γ_B at the read's position |
> | `varve:subjectLink [ varve:linkPredicate ex:caseworker ; varve:linkTarget varve:CallerAgent ]` | data subjects with `(s ex:caseworker <the caller's agent IRI>)` in Γ_B. The target may also be a `varve:SubjectTemplate`. |
>
> Every input of the two rule forms lies in the subject's own row, which is what the write rule needs. That is why the rule form is one predicate from the subject to the caller, and not a general path. A longer path would read other rows, and its outcome could then depend on a row the caller cannot read. A general SHACL path can be added later, if the same argument is made for it.
>
> ### 1.4 The write rule
>
> **A caller may not write a quad about a subject it cannot read**, as the lean says. Going further: **a row-restricted writer writes only rows that are already wholly its own.** The refusal is `403 row-not-writable` (a new problem type), with nothing committed, alongside `graph-not-writable`.
>
> When S_B(C) for writing is `all`, only ADR 0107's graph check applies, and nothing below runs. Otherwise, for each node `s` that is the row node of a quad of the effective delta in a graph of Γ_B:
>
> - **W1, before.** subjects_p(s) ≠ ∅ and subjects_p(s) ⊆ W_B,p(C).
> - **W2, after.** Over the staged state p′: subjects_p′(s) ≠ ∅ and subjects_p′(s) ⊆ W_B,p′(C), or `s` has no row left.
> - **W3, creation.** A node with no row in Γ_B before may be written only when it is either:
>   - a fresh blank node of the request (ADR 0098: a label in a request is always fresh), or
>   - an IRI under a `varve:createUnder` namespace of the writer's grant: a literal prefix or a `varve:SubjectTemplate`.
>
>   W2 then applies.
>
> Why this is probe-free:
> - **If W1 holds,** every row that feeds subjects_p(s) is wholly visible to the writer, by 1.1's all-or-nothing rows and 1.5's precondition. The outcome is then a function of what the writer reads.
> - **If W1 fails,** the row is hidden, mixed, or unclassified. These are refused with one problem body that names neither the node's status nor a subject, so the writer cannot tell them apart.
> - **W3** closes the last gap. Without it, a named IRI that is absent would be accepted where a hidden one is refused. That would be PostgreSQL's documented row-security disclosure through unique keys. With W3, a named IRI outside a `createUnder` namespace is refused whether or not anything is there.
> - **What remains:** a collision under the writer's own `createUnder` namespace with a row that only an unrestricted writer could have put there. I state it in the ADR rather than finesse it.
> - **Effects on other rows.** A row-restricted writer's commit can change the subjects of rows that link to a row it owns. It moves them only between subject sets made from its own subjects, and its outcome does not depend on them. So this is a consequence of what a link means, and nothing checks it.
> - **Unclassified rows** are read-only to a row-restricted writer. Writing one would turn "absent versus hidden" into an observable outcome.
>
> The property (DoD): for generated pairs of datasets that differ only in rows or graphs the writer cannot read, the same write has the same outcome over both: accepted or refused, and the problem body byte for byte. The pairs include absent, hidden, mixed and unclassified nodes, nodes under and outside `createUnder`, and escalation attempts such as adding `alice ex:caseworker <me>` to a row the writer cannot read.
>
> ### 1.5 The apply-time preconditions
>
> These are refused by the management validator (ADR 0166 point 4) as `declaration-invalid`, and each finding names the grant and the binding:
>
> - **P1.** A grant with subject terms must have a graph set that **contains all of Γ_B** for every classifier binding B it meets. `varve:allGraphs` does. Without this, a row-restricted caller could read part of Γ_B, and an input of subjects(s) could sit in a graph it cannot read. That is the probe of ADR 0190 point 7, and it is removed here the same way: by refusing the case, not by masking it.
> - **P2.** A grant with subject terms but a `varve:graphTemplate` graph set is refused, because P1 cannot be judged statically.
> - **P3.** A grant with subject terms that meets no classifier binding is refused, since its subject terms would mean nothing.
> - **P4.** Two classifier bindings with overlapping graph sets are refused, so a graph has at most one classification.
> - **P5.** Two kinds of binding are refused over graphs of Γ_B while any row-restricted **write** grant covers those graphs:
>   - a `reject` or `report` shapes binding (ADR 0169/0173) with a shape that is not **row-local**;
>   - a tier-3 inference binding (ADR 0190 point 7).
>
>   The reason is that a refusal of a row-restricted write must not depend on another row. **Row-local** is defined over ADR 0171's footprint. F(s, n) must lie in n's row: predicate paths from the focus only, no value-node shape references (`sh:node`, `sh:class`, `sh:qualifiedValueShape` and so on), and targets that read only the focus's row (`sh:targetNode`, `sh:targetClass`, `sh:targetSubjectsOf`). See Q5.
>
> `varve apply --plan` names, per grant, the binding whose rows it restricts and its subject set. Following ADR 0190 point 5, a plan that widens a caller's subject set says so.
>
> ---
>
> ## Q2 — How the classifier is expressed, and which form ships first
>
> **Shape-derived ships first, and is the only form in part 1.** It is a layer-5 implementation of `Varve.Store`'s classifier contract (ADR 0023), reading **annotated SHACL shapes**: non-validating terms in the `varve:` namespace on node and property shapes of the binding's shapes graph.
>
> | Annotation | On | Means |
> |---|---|---|
> | `varve:dataSubject true` | node shape | its focus nodes are data subjects |
> | `varve:subjectLink true` | property shape with a predicate path | values of the path are link targets (1.1). It is refused on a non-predicate path. |
> | `varve:privacy varve:Private` / `varve:NotPersonal` | property shape | **reserved in part 1**, given effect in part 2: which value occurrences become private terms, and the classification gate's input |
>
> Targets are limited to the forms that read only the focus's own row: `sh:targetClass` (and implicit class targets), `sh:targetNode` and `sh:targetSubjectsOf`. Any other target on an annotated shape is refused when the binding is applied. `Varve.Shacl`'s shape model is internal (ADR 0155), and these targets and predicate paths are simple to read directly from the shapes graph. So the classifier reads its annotations and targets as RDF and does not depend on `Varve.Shacl`'s internals. Constraints in the shapes still validate, if a shapes binding also names the graph.
>
> **Why shape-derived, and why query-derived does not ship:**
> - **It is declarative and statically analysable.** With the targets limited as below, the nodes a delta reclassifies are found by a bounded local rule, in ADR 0171's sense: `rdf:type` changes for class targets, subject-predicate changes, and link-predicate changes, followed by the reverse link closure.
> - **The classification gate in part 2** asks "has a shape classified this property as private or not personal?". That is a question about shapes.
> - **A query-derived classifier cannot be held to 1.1's row locality.** An arbitrary `SELECT ?node ?subject` can read any row, so its outcome could depend on rows a caller cannot see, and the write rule's argument fails. I propose not to build one, and to record it as an alternative considered, with its loss stated. If one is wanted later, it needs a locality analysis of its own first.
>
> **The determinism rule.** classification_p = Classify(G_p ∩ Γ_B, binding_p), with binding_p taken at the management position p was committed under (ADR 0165's attachment). It is a function of the state and the binding, not of history. So incremental maintenance equals from-scratch classification at every position, which the property checks. It also means a quad's about-set can change without the quad changing: when `alice a ex:Person` is retracted, alice's rows become unclassified. Q5's feed row says what the feed does about that.
>
> ---
>
> ## Q3 — The classification projection
>
> **Contract.** `IClassifier` and `ISelector` land in `Varve.Store` (layer 4), SPARQL- and SHACL-free, as ADR 0023 places them:
> - `IClassifier`: from a pinned `IQuadSource` and a pending delta, to the node → subject-set changes the delta causes. ADR 0023's "pending operations and pinned quad source", stated over an exact delta (spec §6, R4).
> - `ISelector`: from a quad source and a subject, to the quads about it. It is implemented over the projection: the rows whose subjects contain the subject, over `G_head`. Spec §9's "without erasure mode" access then has an implementation (decision 6 at the end asks whether to expose it).
>
> **The projection.** `ClassificationProjection` is an `IProjection` (spec §7, ADR 0016), one per classifier binding. Its shape follows `ValidationProjection` (ADR 0174) on purpose:
> - **Rows** are a node handle → an interned subject-set id, with a validity interval `[asserted, retracted)` in ADR 0138's style. A node with no subjects has no row.
> - **Maintenance.** Each closed commit's exact delta gives the affected nodes: type changes, link changes, and the reverse link closure of a node whose subjects changed. That closure is computed by `Varve.Derivation`'s propagation (ADR 0172). Their subjects are recomputed over the post-state. A retraction re-derives the affected nodes rather than counting. Changing the shapes graph or the binding starts a **generation**, a full classification published whole. Both follow ADR 0174 points 3 and 4.
> - **As-of reads.** The rows valid at p. Below the projection's floor (a cold start at the head, as ADR 0174 point 7 does), the classification at p is computed from scratch over `AsOfAsync(p)` when it is read: correct, at a full classification's cost, and the guide says so.
> - **Storage.** The local cache only, under `derived/classification/{enc(binding)}/`, keyed by a snapshot identity of the binding and a format version, and **verified in full on read** (#146, PR #180's mechanism). Damage gives a miss and a rebuild, never a wrong row: that is the cache-damage gate. Memory and object-storage datasets keep it in memory and rebuild at the head on start.
> - **Readiness.** Lag per binding under the dataset, never making it unready. A read by a **row-restricted** caller above the projection's position waits once, two seconds as for reports, then answers `503 classification-behind` (a new problem type), with `Retry-After`. **An unrestricted caller never reads the projection and never waits.**
> - **Writes** need the classification at the pinned position. They wait for the projection (bounded) and then compute the staged state's changes with the classifier over the staging view, as ADR 0058 does for validators.
>
> **Agreement with private ids in part 2.** In erasure mode the same classifier, at commit time over the staged state, picks the occurrences that become private terms. The log then records key ids. So I add a second rule for erasure mode only: about_p(q) ⊇ the subjects whose key ids occur in q. A later declassification can therefore never make a private term visible beyond its subject. The property: at every position, the projection's assignment of each private occurrence equals the subject of the key id recorded in the log for it.
>
> **Properties (DoD):**
> - incremental equals from scratch, at every position, over generated histories and generated shapes, including link chains, cycles, and shape changes mid-history;
> - as-of reads equal from scratch, above and below the floor;
> - the cache-damage gate.
>
> ---
>
> ## Q4 — The read filter's design and its expected cost
>
> **Where it lives.** `RowScopedQuadSource` goes in `Varve.Rdf` (layer 1), beside `GraphScopedQuadSource`. It knows only:
> - an `IRowSubjects` lookup: a node handle → subject-set id, for one pinned position. The host supplies it from the projection;
> - a `SubjectScope`: subject-set id → allowed, for the caller.
>
> `CallerScope` (ADR 0107) gains the row part, so `Varve.Protocol` stays claim-free and classifier-free. `Varve.Protocol` cannot reference the layer-5 classification package, so the host composes the lookup through `IAccessScopes`, as it composes governors.
>
> **The cost, stated honestly.** Row-level is per quad, but it can be one lookup on one handle, not four:
> - **Per quad:** one probe of the projection's node map on the quad's subject handle. The map is immutable for the pinned position, so the probe allocates nothing. It is skipped for a graph outside Γ_B, which is a per-graph-handle decision memoised as today.
> - **Per distinct subject-set id, per request:** the decision "⊆ S_B(C)", memoised the same way as `PrefixDecisionMemoisedPerGraph`. Subject sets are interned, so their number is bounded by the distinct ownership combinations in the data, not by quads.
> - **Rule forms** (`subjectClass`, `subjectLink`) decide **per distinct subject handle**: one `Contains` on the unscoped pinned source, memoised.
> - **A pattern with a bound subject** is decided once per pattern, not per quad.
> - **`Estimate`** answers the inner bound, an over-estimate, as ADR 0049 allows. `TryInternalise` and `TryExternalise` pass through unchanged, so the source never denies a term (ADR 0022 as #200 states it). The contract test applies (Q5).
> - **`all`** builds no wrapper at all. A caller with no row restriction pays nothing and is measured as today (ADR 0107 point 4).
> - **`[HotPath]`** on `Match`'s filter and the probe. The new answers to `VARVE0003` are filed in the ADR's decision set as `RowDecisionIsOneProbeOnTheRowNode` and `SubjectSetDecisionMemoisedPerSet`, cited where the map and the memo would otherwise report. The allocation test covers both the `all` path and the row path.
>
> **The benchmark.** BenchmarkDotNet at REC M, with tenants and their leases generated as the subjects. Rows run slower first:
> - full scan, `?s ?p ?o` by graph;
> - a selective join;
> - a bound-subject lookup;
> - `GRAPH ?g` enumeration.
>
> Each is measured under unscoped, graph-scoped, and row-scoped with S = all, one tenant, 10% and 50% of tenants. The subjects come from `RecPortfolio`, whose tenants already come and go by lease. The rows join `ScanBenchmarks` beside `FullScanScopedByList` and `FullScanScopedByPrefix`, which today add about 13.6 and 14.6 ns per quad over 39.5 ms unwrapped (1M quads). **I expect the row filter to add roughly as much again per quad on scans, one hash probe, and parity on bound-subject lookups.** The ADR states the measured numbers rather than this guess, and if a scan is worse than 2×, it says so before landing.
>
> ---
>
> ## Q5 — The validation report and inference under row-level grants, and the other surfaces
>
> **Every surface, one property.** ADR 0107 point 7 and ADR 0190 point 8 are extended to rows. Over generated datasets, shapes (classifier annotations), classifier bindings and grants (all subject-set forms, including a caller with an empty subject set and one with only `createUnder`), and for every caller and every surface:
> - **no more:** no response carries an IRI or literal that occurs only in rows the caller cannot read;
> - **no less:** the result equals the query over the sub-dataset of the caller's visible quads, computed from scratch.
>
> | Surface | Rule |
> |---|---|
> | SPARQL query, `GRAPH ?g`, `FROM`/`FROM NAMED`, property paths | evaluated over `RowScopedQuadSource(GraphScopedQuadSource(pinned))`. A path crosses only visible quads, so it cannot reach through a hidden row. |
> | Graph Store `GET` | the visible quads of the graph. A readable graph whose quads are all hidden is an empty graph, not a `404`: its existence was never secret to a reader of it. |
> | service description | lists a readable graph as before. The graph list is per caller already, so its `Vary` is unchanged. |
> | change feed and diff | the delta of the caller's **view**, not the commit's delta filtered. Additions are judged by about_p, retractions by about_{p−1}. **Rows whose about-set changed at p**, which the projection lists, enter or leave the view, and are delivered as additions or retractions. A commit whose view delta is empty is dropped, and positions stay true (spec §8). Property: folding a caller's feed equals its row-scoped view at every position. |
> | `/commits/{p}` | the commit's own delta, filtered by about_{p−1} and about_p. Commit metadata is unchanged. |
> | **validation report graph** | **unreadable by a caller whose subject set is not `all`** for a binding whose graphs meet the validated graphs: no such graph, and a `404` from the Graph Store, by amendment to ADR 0174 point 9. See below. |
> | ADR 0173's commit attachment | delivered only to callers who may read the report graph, as ADR 0190 point 7 rules for cross-graph attachments |
> | **inference** | see below, for #171 |
> | explain (Inference part 2), Web UI (#96) | explain follows the inference rule. The Web UI is a client of the public API with no endpoints of its own, so it inherits every row above. One test drives the explorer as a row-restricted user. |
> | as-of reads | classification as of the read's position. Grants and subject sets as of the management position the read is served under (ADR 0165), rule forms evaluated over the data at the read's position. |
>
> **The validation report: withheld whole, not filtered per result.** I propose the report graph is unreadable for a row-restricted caller, and recommend against withholding results one by one:
> - `sh:conforms` is a statement about every row, so it would have to be recomputed over the visible results. The report would then no longer be ADR 0174's report, byte for byte.
> - A result about a visible focus node can depend on a hidden row, for example `sh:class` on a value node whose type is in another row. A visible result would leak that.
>
> Per-result withholding is possible later, for row-local shapes only (P5's definition), as its own ADR.
>
> **Inference: the rule I propose, for #171's agreement.** ADR 0190's rule stands: an entailment is visible only if the caller can derive it from what it reads. With rows, the premises of a conclusion are rows, and minimal premise sets over rows cannot be placed by graph. `bob a ex:Person` from `alice ex:knows bob` and a range axiom sits in bob's row but is derived from alice's. So, under row-restricted read grants on graphs of a classifier binding:
> - **Tier 1 (rewriting) is unchanged and exact.** It evaluates over the caller's row-scoped source, so it derives exactly what the caller can derive.
> - **Tier 2 (the materialised closure)** cannot be placed. That covers `varve:implicit`, `varve:inferred/…`, `Varve-Entailment` served by the closure, and explain. For a row-restricted caller over those graphs, it is **refused** with `403 entailment-not-available` (a new problem type), never filtered. Either filtering direction breaks the property: by about-set it leaks or vanishes, and by premise it would need provenance per quad, which ADR 0190 already refused. A regime that tier 1 serves is still served. A per-request closure over the caller's visible sub-dataset, under a work limit, is a later option, stated and not built.
> - **Tier 3** under P5: refused at apply when a row-restricted write grant covers its graphs.
> - **Cross-graph bindings** (ADR 0190 point 6) over graphs of a classifier binding, with any row-restricted read grant, are refused at apply.
>
> I'll post this half on #171 as a proposal and wait for that session's agreement before ADR 0238 is written. If they prefer another rule, this plan changes, not theirs.
>
> **The contract test.** `RowScopedQuadSource` and the projection's read source (the classification as of p) pass `tests/QuadSourceContract.cs` (#200). A row-scoped source never denies a term it hasn't classified yet, because terms pass through.
>
> **Replication (part 1's gate).** Classification is derived, so a tier-1 follower (ADR 0164) maintains its own projection from its log. The gate runs a row-restricted caller's queries, feed and diff against the writer and a tier-1 follower at the follower's reported position, and the answers must be equal. This doesn't wait on #165 or #166: tier 1 exists already.
>
> ---
>
> ## Q6 — Part 2: erasure mode (preview)
>
> This part is gated on #165/#166 for the replica gate and on ADR 0028's review for shipping as anything but preview. It is planned now and built after part 1 lands.
>
> - **The key store and its binding.**
>   - `IKeyStore` in `Varve.Store`: create a key for a subject, or for a subject set (Q8); resolve to `KeyId`; fetch; destroy.
>   - `varve:KeyStoreBinding` is a management declaration naming a key store the host configures by name. It holds no secret: a credential stays in host configuration (ADR 0037).
>   - Implementations: in memory for tests, and a file key store refused inside any dataset directory (ADR 0074's `KeyStoreDirectory.IsWithin`). A cloud key vault is the host's, not a Varve package (ADR 0023).
>   - Fetched key material is held in memory only, per process, and dropped on an `Erasure` commit. `docs/operator/erasure.md` states the backup rule: a backup that restores a destroyed key un-erases a person.
> - **Erasure mode on.** `varve:erasureMode` stops being reserved. I propose it is refused on a dataset with any data commit, which makes it a creation-time decision, as ADR 0023 expects. The alternative is going forward with the limitation stated. **Question for the maintainer.**
> - **Private terms.**
>   - At commit time, the classifier over the staged state picks the occurrences to make private: every occurrence of a value under `varve:privacy varve:Private`, and the IRI occurrences of data-subject nodes when their shape says `varve:privateNode true`.
>   - The sequencer allocates them in class `10` (ADR 0074) and encrypts with ADR 0028's AEAD under the subject's key. Private entries are never interned.
>   - ADR 0074's refusal is lifted for a dataset with erasure mode on, by amendment.
>   - Equality follows spec §6: a source with private entries hashes every handle by value.
>   - The agreement property is Q3's.
> - **`T4 Erase`.**
>   - `POST /manage/datasets/{d}/erasures` (admin), and `varve erase`.
>   - It runs step 1 by key id (with the caller's opt-out), then the `Erasure` commit, then destroys the key.
>   - Followers and subscribers drop the key on the commit, which ADR 0016 delivers regardless of filter.
> - **Access requests by key id.** `Access(K)` by log scan, with `AllHistory` or `Current` and the dataset default (ADR 0023), served to admins. `AllHistory` below an archive horizon fails explicitly.
> - **The classification gate.** A dataset validator in erasure mode refuses a commit with a literal under a property that no shape classifies as `Private` or `NotPersonal`, as `422 classification-required` (a new problem type). Its finding names the property. Its known blind spot (free text under `NotPersonal`) is stated in the guide.
> - **Q4: how a shredded term appears.**
>   - **Proposed:** as a blank node whose label is derived from its private id, in a label namespace disjoint from ADR 0098's. It is equal only to itself (spec §6) and stable across responses, and because a request's labels are fresh, it can't be named back in a query. Structure stays queryable, the residue stays unaddressable, and SPARQL's bound-variable semantics hold. That is why I prefer it to "unbound", which breaks them.
>   - **The alternative**, an opaque IRI in a reserved scheme, makes the residue addressable, which ADR 0098 recorded as a cost.
>   - **A key store that is unavailable** is `503 key-store-unavailable`, never shown as shredded. Destroyed and unreachable must not look alike.
> - **Q5: lookup by private value.**
>   - **Proposed for the preview:** scan and decrypt, with plaintext only in memory, per process, tagged with the key id and purged on `Erasure`. `TryInternalise` of a value with no canonical entry looks there, so it never denies a term it hasn't looked for (#200).
>   - **No on-disk plaintext index**, not even under `derived/`. A copy of `derived/` taken before an erasure would keep the plaintext, and I10 says "every copy … made at any time".
>   - **A keyed global blind index is refused**, since it weakens I10. If scans prove too slow, the measured follow-up is a token index keyed **per subject key**: lookup costs one HMAC per key, and destroying a key orphans its tokens. That is its own ADR, with the benchmark.
> - **Q8: one term about two subjects.**
>   - **Proposed:** a **joint key** per subject set, created by the key store on demand, and destroyed when **any** member is erased. Erasing one person shreds what is about them jointly.
>   - `Access(K)` includes the joint entries, subject to GDPR Article 15(4)'s note on the rights of others.
>   - The alternative, nested encryption under every member's key, needs several key ids per entry, which ADR 0074's format does not have.
>   - **Question for the maintainer.**
> - **Q9.** Recorded as a legal question, not an engineering one. The engineering half is `varve:privacy varve:Private` on a link property, which makes an identifying object occurrence private too. The guide says it, and no code claims anonymity.
> - **The replica gate.** On a tier-1 and a tier-3 follower: after `Erase`, every read at every sampled position, writer and followers alike, shows the shredded form for the key and unchanged structure (I10). A byte scan of every follower's files finds no marker (I9).
> - **The review package (ADR 0028 condition 2).** The session prepares it; the maintainer chooses the reviewer. It goes in `docs/review/aead-hmac-siv/`:
>   - the construction stated alone;
>   - ADR 0028;
>   - the code;
>   - test vectors with an independent reference implementation in the test project;
>   - the threat model (§2's copies; what I9 and I10 claim);
>   - the browser primitive evidence;
>   - the specific questions for the reviewer.
>
>   **The fallback ADR** (erasure mode not in the browser) is drafted and filed only if the review rejects the construction.
>
> ---
>
> ## Q7 — The split, the ADRs, and the amendments
>
> **Two sub-issues of #14**, opened when this plan is approved:
> - **Part 1, row-level access (1.0, stable):** this plan's Q1–Q5. Its descriptor is the next free `0.1.0-preview.N` at landing, with `Closes` for the part-1 sub-issue. Preview.16 is claimed by #175, #178 and #179 at once, and preview.17 by #187, so I'll renumber at landing. #14 stays open. I tick #15's row-level box when it lands.
> - **Part 2, erasure mode (preview):** Q6. Its descriptor closes the part-2 sub-issue and #14.
>
> **New ADRs, part 1:**
>
> | ADR | Decides |
> |---|---|
> | 0230 | Rows and data subjects: row-rooted classification, about-sets, visibility, the per-binding subject set. Supersedes ADR 0107 point 8's boundary by amendment. |
> | 0231 | The classifier and selector contracts in `Varve.Store`, usable with erasure mode off |
> | 0232 | The shape-derived classifier: its annotations, its targets, link predicates, and why query-derived does not ship |
> | 0233 | The classification projection: rows, maintenance, generations, as-of reads, the cache, readiness, `classification-behind` |
> | 0234 | Subject-scoped grants and the classifier binding: vocabulary, P1–P4, the plan |
> | 0235 | `RowScopedQuadSource` and the read filter's cost: the hot-path decisions, the benchmark |
> | 0236 | The write rule: W1–W3, `createUnder`, `row-not-writable`, the probe property |
> | 0237 | Rows on every surface: the feed and diff as view deltas, the commits resource, the report graph withheld, P5 and row-locality, as-of reads |
> | 0238 | Inference under row-scoped grants: tier 1 only, `entailment-not-available`, cross-graph and tier 3 refused. **Written only after #171 agrees.** |
>
> **New ADRs, part 2** (0239–0246): the key store and its binding; erasure mode on; private terms and agreement; `T4` and access by key id; the classification gate; Q4; Q5; Q8. Plus a conditional fallback ADR if the review rejects ADR 0028.
>
> **Amendments** (dated, ADR 0068):
> - **0107:** row scope in `CallerScope`, point 8's boundary closed, and point 7's properties extended to rows.
> - **0166:** the classifier binding, the subject-set terms, `createUnder`, P1–P5 in the validator, and the plan.
> - **0174:** point 9, the report graph unreadable for a row-restricted caller.
> - **0190:** points 1 and 7: tier 2 and cross-graph under rows, P5.
> - **0023:** the classifier assigns occurrences by row node; the selector is implemented by classification; about-sets include recorded key ids in erasure mode (part 2).
> - **0173:** the attachment's delivery.
> - **0119:** `row-not-writable`, `classification-behind` and `entailment-not-available`; in part 2, `classification-required` and `key-store-unavailable`.
> - **0074 (part 2):** private entries accepted in erasure mode.
> - **0019** is superseded already. Its "row-level" use is 0023's.
>
> **Packages.**
> - **`Varve.Classification`** (layer 5, new): the shape-derived classifier and the projection. It references `Varve.Rdf` (1) and `Varve.Store` (4), not `Varve.Shacl.Store` (same layer). ADR 0232 records the new package and amends ADR 0003's table.
> - **`Varve.Shacl`** gains one additive public member, by amendment to ADR 0155: whether a shape is **row-local** (P5), computed from ADR 0171's footprint analysis, which is internal today. `Varve.Management` already references `Varve.Shacl` (ADR 0166's amendment of 2026-10-25), so P5's check lives there.
> - `Varve.Rdf` gains the wrapper, `Varve.Store` the contracts, `Varve.Management` the vocabulary, `Varve.Protocol` the surfaces, and the server the composition.
> - No new NuGet dependency. `eng/lockfiles.cs` runs for the new project once #179 lands.
>
> **Public API (stable 1.0).** The PR body carries the baseline diff with a reason per member: `RowScopedQuadSource`, `IRowSubjects`, `SubjectScope`, `CallerScope`'s row members, `IClassifier`, `ISelector`, the projection's public surface, and the management model's new members.
>
> **Close-out of part 1** (DoD B):
> - README Status, `docs/roadmap.md`, AGENTS.md's State, `eng/status.cs` and the changelog;
> - `docs/operator/row-level-access.md`;
> - three `docs/problems/` pages;
> - `docs/vocabulary.md`;
> - the traceability record;
> - a note on #94 for the Learning track's multi-tenancy chapter: tenants as data subjects, leases and spaces linked to them, and `subjectPrefix` and `subjectTemplate` grants applied by `varve apply`.
>
> Property iteration counts are sized for the land gate, with full depths nightly (`docs/testing.md` §8). The new properties read `VARVE_LONG_PROPERTIES`, and the nightly `long` job gains the Classification, Protocol and Server test projects. Today's grant properties there have fixed counts. Durations are reported per OS.
>
> **What part 1 depends on that is still in flight.** I merge `main` as each lands, and I don't port any of it onto this branch ahead of time. If I have to, the PR says so (#172).
> - **#200** brings `tests/QuadSourceContract.cs`. Q5's contract test needs it.
> - **#180** brings ADR 0213's checked derived blobs. The projection's cache-damage gate uses them.
> - **#178 / #171** change `GraphScope`: the dataset-public set, and governors as a set, which are not in the code yet. They also rework `GraphScopedQuadSource`'s memo (#151). The row wrapper composes over the graph wrapper rather than inside it, so the overlap is in `CallerScope` and in `IAccessScopes`'s composition. Whichever lands second reconciles them.
> - **#175** (Replication part 1) only for part 2's gate. Part 1's tier-1 gate uses ADR 0164's follower, which is already on `main`.
>
> ---
>
> ## Decisions I need from the maintainer
>
> 1. **Rows (1.1):** a quad is about its subject node's subjects only, not its object's. This is the change from the starting point's "classified occurrences", and its reason is probe-freedom.
> 2. **The write rule (1.4):**
>    - row-restricted writers write only rows that are already wholly theirs, which makes unclassified rows read-only to them;
>    - creation is only at fresh blank nodes or under `createUnder`;
>    - or instead the relaxed rule, which accepts creation at any absent IRI and states PostgreSQL's existence disclosure.
>
>    I recommend the strict rule, because the DoD says "never".
> 3. **P1:** a row-restricted grant covers the whole of each classifier binding's graph set.
> 4. **The report graph:** withheld whole from a row-restricted caller.
> 5. **Inference:** tier 2 refused for row-restricted callers over classified graphs. This needs #171's agreement too.
> 6. **The selector:** whether part 1 exposes it as an admin-only `GET /datasets/{d}/access?subject=` (spec §9's access without erasure mode), or keeps it as a contract only until part 2.
> 7. **Part 2:** erasure mode at creation only; Q4 as a blank node; Q8 as joint keys.
>
> ---
> Developed with AI assistance under human review.
>
> ---
> _Generated by [Claude Code](https://claude.ai/code)_

### The approval

> Approved, with your seven decisions as proposed, plus one gap to close.
> Links into a hidden row. A quad is about its subject's row only, so a visible row that links to a hidden subject (`ex:acme ex:employs ex:person42`) reveals that subject's IRI. Close it one of two ways, your choice, in ADR 0231 (or wherever rows are defined):
>
> * a link whose object is a classified subject belongs to that subject's row unless the binding declares the predicate public; or
> * `varve apply` refuses a classifier binding that leaves such a link undeclared, naming the predicate.
>
> Either way, add a case to the row-level grant property: no response reveals the IRI of a subject whose row the caller can't read, unless a declared public link names it.
> On the seven:
>
> 1. Rows: yes.
> 2. Strict write rule: yes.
> 3. Covering the whole binding: yes.
> 4. The report withheld whole: yes; file a follow-up for per-result filtering.
> 5. The closure refused for row-restricted callers: yes for 1.0, pending the Inference session's agreement on #171. File a follow-up for row-local rules (premises and conclusion sharing the subject, such as `cax-sco` and `prp-dom`).
> 6. The selector: an admin-only access endpoint in part 1, named in the baseline diff.
> 7. Part 2:
>    * erasure mode only at creation;
>    * Q4 as a blank node, stable within a response and never equal to a stored blank node;
>    * Q8 as joint keys, with the consequence stated in ADR 0023's amendment and the guide: erasing either subject makes the shared term unreadable for both.
>
> Open the two sub-issues and start part 1. Merge `main` as #200, #180 and #178 land, and build on their code once it's there.

## The report

**What was built** is #202's part 1, in six commits on the branch:
- **ADRs 0230–0238** and their decision sets, filed unaccepted;
- `Varve.Rdf`'s row-scoped source and write rule;
- the classifier contracts and `Varve.Classification`;
- the management vocabulary and preconditions;
- the protocol and server surfaces;
- the tier-1 follower gate.

The pull request body has the summary and the API baseline diff.

**How the gap was closed.** It was closed by the first of the maintainer's two
options, with a write rule (W4) the session added so that writes stay
probe-free (https://github.com/Hafeok/Varve/issues/14#issuecomment-6520677500).
A quad whose object is classified is about the object's subjects too, unless
its predicate is a declared public link. A row-restricted writer may link only
to rows it reads.

**What the properties found:**
- A retraction of an *absent* quad was judged as if present. The write-probe
  property at layer 1 found it: the outcome differed between a dataset and the
  same dataset without its hidden quads.
- A graph outside every binding shows a classified node's IRI to its readers.
  The IRI property found it, and ADR 0230 point 5 states it as the binding's
  boundary.
- The feed is history. It rightly carries an IRI from a position where the
  node was visible, so the feed is held to nodes hidden at every position.
- The feed's commit namer names only the handles a commit carries.
  Reclassified quads are named through a head pin.

**Departures from the plan, said in the pull request:**
- The report graph is withheld from a caller restricted in *any* binding, not
  binding by binding.
- The access route serves N-Quads only, across every binding.
- `entailment-not-available` is not catalogued until #171's surfaces exist.
- P5's exemption for row-local shapes waits for #204.
- `ShapeClassifier.Classify` classifies on demand, so a write costs the nodes
  it touches. The projection property checks that it equals from scratch.

**Not done in this session, and said in the pull request:**
- the BenchmarkDotNet rows at REC M (rows over the 1M synthetic dataset are
  added and reported);
- the contract test of #200 and the checked blobs of #180, which had not
  landed;
- a server-level test through `varve apply`;
- the note on #94, and ticking #15's box, which are for after landing.

**Process notes:**
- `eng/hooks` does not exist on `main`, so the DCO line
  `Signed-off-by: Emil Okkels Klein <emil@okkels-klein.dk>` was written by
  hand, never with `-s`.
- The session mistyped its own identifier in the first commits' trailers, and
  rewrote them on its own unmerged branch before the pull request.

### Acceptance, 2026-10-30

A later session (Claude Code, model `claude-fable-5-1`), on the maintainer's
instruction to approve every decision on this pull request, added
`accepted-by` and `accepted-at` to the 55 keys this branch files over main:
rows-data-subjects-and-visibility (9, ADR 0230),
the-classifier-and-selector-contracts (5, ADR 0231),
the-shape-derived-classifier (6, ADR 0232), the-classification-projection
(7, ADR 0233), subject-scoped-grants-and-the-classifier-binding (7, ADR
0234), the-row-scoped-quad-source (6, ADR 0235), the-row-level-write-rule
(6, ADR 0236), rows-on-every-surface (6, ADR 0237) and
inference-under-row-scoped-grants (3, ADR 0238). The acceptance is the
maintainer's, who had read them and approved the head beneath; the session
only wrote it at their direction, as the acceptance of ADRs 0127–0133 was
written. The ADRs' status text and the index rows are left as filed.
