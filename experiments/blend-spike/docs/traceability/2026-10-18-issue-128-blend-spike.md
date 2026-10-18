# Blend spike: can a domain's allowed decisions be generated rather than enumerated?

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../../../../docs/adr/0033-commit-traceability.md). The prompt, the
> plan as submitted and the maintainer's approval are verbatim. The transcript
> itself is held by the maintainer. The record is kept inside the spike
> directory, as the prompt asked, rather than in `docs/traceability/`.

| | |
|---|---|
| **Issue** | [#128](https://github.com/Hafeok/Varve/issues/128), opened by this session at the maintainer's instruction, label `spike` |
| **Date** | 2026-10-18 |
| **Tool** | Claude Code 2.1.302, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata (`configured_model`, `session_context.model`, `last_served_model`) |
| **Session identifier** | `session_014te8i4MGDXQPzZsSjRTdym` |
| **Branch** | `claude/determined-newton-sfkpio`, from `main` at ce6136b; never merged |
| **Machine** | a cloud container, .NET SDK 10.0.401, Python 3.13 with pyshacl 0.40.1 and rdflib 7.6.0 in a virtual environment outside the repository |

## The prompts

### The message that started the session

> @"/root/.claude/uploads/4ea5172c-ab61-5b72-a0cf-fe07c4795dd1/2de2a88e-varve-claude-code-blend-spike.md" Read and execute the prompt attached

### The prompt, `varve-claude-code-blend-spike.md`

> # Session prompt: blend spike — can a domain's allowed decisions be generated rather than enumerated?
>
> You are working in github.com/Hafeok/Varve, on a new branch `spike/blend-decisions`. This branch is never merged. Its output is a findings report and a set of proposed issues; nothing in it ships.
>
> ## The question
>
> Decision-driven design today enumerates decisions: each ADR is an entry, code cites entries, Roslyn analyzers check the citations. The hypothesis under test is that a domain's *allowed decision space* can instead be **generated** from a small ontology of templates and composition rules, in the style of conceptual blending (Fauconnier and Turner; BWA, Erik David Johnson, 2025, doi:10.5281/zenodo.15675193): input spaces, a generic space, and a blend rule that produces a blend carrying constraints neither input had (emergent structure).
>
> If that holds, the rules leave the .NET analyzers and live in a graph; the analyzers reduce to a per-language projector of code facts into RDF, and the check is one SPARQL/SHACL evaluation over code facts plus the domain's ontology at a position.
>
> The domain for the test is the one Varve itself is: a **layered library**. The evidence is Varve's own accepted ADRs.
>
> ## Two outcomes, decided by evidence
>
> - **Keep blending:** at least one accepted ADR comes out of a blend rule and carries a constraint that is in neither input decision, so an enumerated list would not have contained it.
> - **Drop blending, keep SHACL:** no such case among the ten. Then the vocabulary is patterns with composition rules, and the report says so.
>
> In both outcomes the code-fact projector and the graph-side check are kept as findings. Do not argue for the first outcome; report what the ten ADRs show.
>
> ## Read first
>
> - `docs/adr/` (every accepted ADR; set zero in full), `docs/decisions/` or wherever the decision sets with `accepted-by` lines live, and the DD analyzer rules as Varve uses them.
> - The ledger format: github.com/mindovermachine-dev/product-cli, `docs/ledger-format-v1.md` and the vocabulary `urn:ledger:ns#`. Decision nodes in this spike must align with it (decisions, versions, `ledger:supersedes`, acceptance), so the spike's graph could later be a projection of a ledger.
> - The project prompt's three design principles and layer table.
>
> ## Plan first
>
> Post a plan and wait for approval. The plan must contain:
>
> 1. **The ten ADRs.** Choose ten accepted ADRs across at least these areas: layering and the SPARQL-free store (set zero), the commit model and group commit, crypto-erasure (ADR 0028), no leases and writer handover, no `NOW()` in derived results (the ADX boundary), the management plane. For each, your prior guess: derivable as a blend, enumerated only, or not expressible. The guess is recorded so the result can be compared against it.
> 2. **The vocabulary.** A minimal `blend:` vocabulary: `MentalSpace`, `GenericSpace`, `BlendRule`, `Blend`, slots, cross-space mappings, and `emergent` for a constraint present in a blend and absent from both inputs. Say how a `Blend` is also a `ledger:Decision`. Keep it under twenty terms; justify each.
>    **Acceptance:** a human signs the *rule* (and each space) once, as a `ledger:Acceptance`. A blend produced by an accepted rule from accepted inputs is a derived decision: it records `prov:wasDerivedFrom` the inputs and the rule and the position it was derived at, and inherits acceptance through them; no human signs each firing. A blend that no accepted rule produces is an enumerated decision and needs its own acceptance, as today. A new rule or space proposed by an agent is a proposed decision until a human accepts it. State this in the vocabulary and show one derived and one enumerated example.
> 3. **The code-fact projector.** What it reads from the repository alone: `.csproj` files (the declared layer property, `ProjectReference`, `PackageReference`, `InternalsVisibleTo`), `Directory.Build.props`, the public API baseline files, and the decision declarations on interfaces. What it emits, as N-Quads. No Roslyn in the projector for this spike; if a fact needs the compiler, record it as a gap.
> 4. **The layer rule as one blend rule.** Inputs: "a package declares layer L" and "a package declares layer M"; generic space: "a reference"; blend: the reference is permitted iff L > M. Show it as `CONSTRUCT` or `sh:rule`, and its violation as a SHACL shape.
> 5. **Tooling.** The decision graph and the code facts live in an embedded Varve dataset (in-memory or file backend), written as commits so that `DiffAsync` and as-of reads can answer "which decisions were in force at the commit that added this reference". Blend rules run as SPARQL `CONSTRUCT` on Varve's evaluator. SHACL validation may use any off-the-shelf engine (for example pyshacl over a TriG export), since #13 has not landed and the spike ships nothing. Say which.
> 6. **Layout.** `experiments/blend-spike/` on the branch: not in `Varve.sln`, not under the repo's analyzers or gates. If any gate still fires on the directory, say so in the plan rather than suppressing it.
>
> ## Work
>
> - Build the vocabulary, the layered-library ontology (spaces, generic spaces, rules) as TriG, and the projector.
> - For each of the ten ADRs, attempt the derivation and record one of: **derived** (name the rule, the inputs, and every emergent constraint), **enumerated** (expressible as a node but no rule produces it), **not expressible** (say why). Keep every attempt, including the failed ones, in the spike directory as data.
> - Run the layer rule over the projected code facts and show it reproduces the analyzer's layer verdict for every project reference in the repository. Any disagreement is a finding, not something to fix.
> - Run one as-of question end to end: pick a project reference, find the commit that introduced it, and list the decisions in force at that position.
> - Record token and time cost of nothing; this spike measures expressiveness, not performance.
>
> ## Deliverables
>
> - `claude/varve-blend-spike-findings.md`: the verdict against the two outcomes; the ten-row table (guess, result, emergent constraints); the layer-rule comparison; what the projector could and could not read from files alone; what Varve's embedded API lacked, as proposed issue texts (not filed); and whether the blend vocabulary earned its place.
> - The spike directory on the branch, pushed, with a `docs/traceability/` record for the session (ADR 0033) kept inside the spike directory.
> - No change to any Varve package, document or workflow on the branch. No release descriptor. No PR.
>
> ## Standing rules
>
> - Commits signed with the agent SSH key, as `claude+emil@okkels-klein.dk`; no model ids in commit messages.
> - No suppression or downgrade of DD or VARVE rules; the spike directory is outside them by layout, not by suppression.
> - When an ADR cannot be derived, say so with the reason. Do not widen a rule until it fits; that would be enumeration with extra steps, and the report should name it if you are tempted.

### The plan as submitted

> I've finished the research, and the plan is below. As the prompt asks, I haven't written or pushed anything yet and am waiting for your approval. Three questions at the end need your answer before I start.
>
> # Plan: blend spike
>
> ## Two things I found while reading
>
> - **The ledger format has moved.** `docs/ledger-format-v1.md` no longer exists in product-cli. It was merged into `ledger/spec/ledger-protocol.md`. The export vocabulary I'll align with comes from that file (§9) and `docs/decisions/*.nt`:
>   - `ledger:Decision`, `ledger:DecisionVersion` and `ledger:ofDecision`
>   - `ledger:statement`, `ledger:set` and `ledger:key`
>   - `ledger:supersedes` (a version points to the decision it supersedes)
>   - `ledger:Acceptance` with `ledger:signsVersion`, `ledger:scope "version"`, `prov:wasAttributedTo` and `prov:generatedAtTime`
>   - `prov:wasRevisionOf` for a version's parent
> - **Layering doesn't produce the SPARQL-free rule.** Under the layer table, `Varve.Store` (layer 4) referencing `Varve.Sparql` (2) or `Varve.Sparql.Evaluation` (3) is allowed. ADR 0005 forbids exactly that. So ADR 0005 already adds a constraint that layering doesn't give, which makes it a direct test case.
>
> ## 1. The ten ADRs and my prior guesses
>
> | # | ADR | Area | Guess |
> |---|---|---|---|
> | 1 | 0003 + 0060 table: `LayersStrictlyDownward`, `SameLayerReferenceIsViolation` | layering (set zero) | **derived**, but nothing emergent: the rule just applies its own condition |
> | 2 | 0005: `StoreReferencesNoSparql`, `QuadSourceContractInRdf` | SPARQL-free store | **enumerated**: layering allows what it forbids; the reason is binary size and AOT, outside the graph |
> | 3 | 0060: `CompositionRootIsTheExecutable` | layering, hosts | **derived**: blend of 0011 `NoAmbientClockOrRandomness` and the layer table; candidate emergent constraint: "a library takes clock/storage as a parameter" |
> | 4 | 0010: `EffectiveDeltaInvariant`, `EmptyDeltaNoCommit` | commit model (set zero) | **enumerated** |
> | 5 | 0011: `OneSequencerPerDataset`, `MonotoneTimestamps` | concurrency (set zero) | **enumerated** |
> | 6 | 0124 group commit: `EachRequestKeepsItsCommit`, `BatchBoundaryIsInNoContract` | group commit | **derived**: 0011 one sequencer combined with 0073 durability (ack only when durable); candidate emergent: "the batch boundary is in no contract" |
> | 7 | 0028 AEAD: `SlotBoundAssociatedData`, `FullWidthSyntheticIv` | crypto-erasure | **not expressible**: these follow from cryptographic analysis (the equality oracle), not from structure |
> | 8 | 0136 epoch and fence: `NoLeaseNoTimer`, `FenceIsTheNextSlot` | no leases, writer handover | **derived**: 0075 (one writer, by an OS lease) combined with 0134 (the object store has only create-if-absent and replace-if-unchanged); candidate emergent: "the fence is the next slot" |
> | 9 | 0140: `NoWallClockFreshness` (no `NOW()` in standing queries) | ADX boundary | **derived**: 0056 `NowReadOncePerExecution` combined with 0140 `EveryDerivedResultStatesItsPosition`; the strongest candidate |
> | 10 | 0106 + 0118 admin API: `DeleteOnlyWhenClosed`, `SettingsCommitOverHttp` | management plane | **enumerated**; `DeleteOnlyWhenClosed` might come out of 0075 |
>
> During the work I'll read set zero (0010–0023) in full and all ten ADRs, including their amendments.
>
> **What counts as emergent.** I'm fixing the test now so I can't adjust it later to fit:
> 1. The rule is written generically over slots and must fire on at least two different input pairs. A rule that fires only once counts as enumeration.
> 2. The constraint must relate terms from both inputs in a way neither input relates them.
> 3. The constraint's specific content must not appear in the rule's text.
>
> If getting the right answer would require writing the ADR's own constraint into the rule, the result is **enumerated**, and the report will say I was tempted.
>
> ## 2. Vocabulary (19 terms, `urn:varve-spike:blend#`)
>
> **Classes (8):**
> - `MentalSpace`: an input space, i.e. one accepted decision seen as a frame.
> - `GenericSpace`: a subclass of `MentalSpace`; the structure the two inputs share.
> - `BlendRule`: the composition. It carries its SPARQL `CONSTRUCT`.
> - `Blend`: the output.
> - `Slot`: a role inside a space, such as `layerOf`, `references`, `clock` or `position`.
> - `Mapping`: a cross-space link between slots.
> - `Constraint`: a ruling carried by a space or a blend.
> - `Attempt`: the recorded outcome of trying to derive one ADR.
>
> **Properties (11):**
> - `slot`, `input` (exactly two per rule), `generic`
> - `source`, `target` (the two ends of a `Mapping`)
> - `constrains` (a space or blend to its constraint)
> - `emergent`: always computed by a check query, never typed in by hand
> - `query`: the rule's SPARQL text
> - `atPosition`: the Varve position a blend was derived at
> - `result` (derived, enumerated or not expressible) and `forDecision` on an `Attempt`
>
> **How this fits the ledger.** `MentalSpace`, `BlendRule` and `Blend` are all subclasses of `ledger:Decision`, and each has `ledger:DecisionVersion` versions. Spaces and rules are accepted by a human once, as a `ledger:Acceptance`, and reused from the ledger rather than redefined.
>
> A **derived** blend has no acceptance of its own. It records:
> - `prov:wasDerivedFrom` pointing to each input version and to the rule version;
> - `blend:atPosition` for the position it was derived at.
>
> It counts as in force when a SPARQL check (an ASK) finds a live acceptance on the rule and on every input version at that position. An **enumerated** decision has its own `ledger:Acceptance`, as today. A rule or space an agent proposes stays unaccepted, which means proposed.
>
> I'll show two examples:
> - **Derived:** "`Varve.Rdf` may not reference `Varve.Store`", from the layer rule.
> - **Enumerated:** 0005 `StoreReferencesNoSparql`.
>
> **A gap I already know about.** The ledger has no notion of inherited acceptance. Its rule that every acceptance signs one version hash doesn't cover a version nobody signs. I'll put this in the report.
>
> ## 3. Code-fact projector (files only, no Roslyn)
>
> **What it reads:**
> - Every `*.csproj` (68 of them): `ArchLayer`, `ArchCompositionRoot`, `OutputType`, `IsPackable`, `ProjectReference`, `PackageReference` and `InternalsVisibleTo`.
> - `Directory.Build.props` and `.targets`: `ArchFamily` and the global package references.
> - `Directory.Packages.props`: versions and the `Adr=` attribute.
> - `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`: line counts and the types they name.
> - `[Contract(typeof(X.Y), Role=…)]`, `[HotPath]` and `[DesignDecision]` declarations, found by regex.
>
> **What it emits:** N-Quads in the graph `<urn:varve-spike:code>`, for example `pkg:Varve.Store spike:layer 4`, `pkg:A spike:references pkg:B`, and `spike:cites` from a type to a decision key.
>
> **Gaps I'll record rather than fill:**
> - which interface a regex-matched attribute is actually attached to;
> - whether a reference is actually used (DD0001 sees compilation references);
> - transitive references through packages;
> - a `typeof` that goes through an alias.
>
> ## 4. The layer rule as one blend rule
>
> - **Inputs:** `?a spike:layer ?l` and `?b spike:layer ?m`.
> - **Generic space:** `?a spike:references ?b`.
> - **Blend:** a `CONSTRUCT` emits a `Blend` whose verdict is `permitted` when ?l > ?m and `forbidden` otherwise.
> - **Violation:** a SHACL shape on `spike:references` with an `sh:sparql` constraint that fires when the target's layer is not strictly lower. A second shape covers a referenced Varve package that declares no layer (`UndeclaredReferencedLayerIsError`).
>
> A test assembly declares no layer, so the rule says nothing about its references. I'll report each such reference as a disagreement and won't adjust the rule to cover it.
>
> **Comparing against the analyzer:** the build has zero warnings and DD rules are never suppressed, so DD0001's verdict on every `src/` reference is "permitted". As a negative control I'll build one throwaway project inside the spike directory with a reference that points upward, and record DD0001's real diagnostic.
>
> ## 5. Tooling
>
> - **Host:** one C# console host, `experiments/blend-spike/BlendSpike/`, using `ProjectReference` to `Varve.Store`, `Varve.Sparql.Store`, `Varve.Sparql.Evaluation` and `Varve.Turtle`.
> - **Storage:** a `FileStorage` dataset in the scratch directory.
> - **History:** the ontology TriG is the first commit. For the as-of question, I'll project the code facts once per repository commit that changed a `.csproj` (bounded to that subset), and write each as a Varve commit with the git SHA as the cause.
> - **Queries:** `DiffAsync` and `AsOfAsync` answer "which decisions were in force when this reference was added"; blend rules run as `CONSTRUCT` on Varve's evaluator.
> - **SHACL:** pyshacl over a TriG export, installed in a venv in the scratch directory. The spike ships nothing, and #13 hasn't landed.
>
> ## 6. Layout and which gates reach it
>
> Everything goes under `experiments/blend-spike/`: the vocabulary, the ontology, the attempts as data (`attempts.trig`), the projector, the shapes, outputs, and `docs/traceability/YYYY-MM-DD-issue-N-blend-spike.md`. None of it goes into `Varve.slnx`.
>
> These gates would still reach the directory, and I won't suppress any of them:
> - **The licence-header scan** checks every `.cs` in the repository. Spike files will carry the MPL notice, so it passes rather than being skipped.
> - **`Directory.Build.props` is inherited.** On its own the spike project would get the DD analyzers and `TreatWarningsAsErrors`. I propose a local `Directory.Build.props` in `experiments/blend-spike/` that doesn't import the parent. That keeps the spike outside the rules by layout. Code in this host would then go unchecked by DD, and you should decide whether that counts as layout or as suppression.
> - **CI** runs only on `main`, `land/**` and pull requests, so a branch with no PR triggers none of it.
>
> The findings go to `claude/varve-blend-spike-findings.md` at the repository root, as the prompt names it. No Varve package, document or workflow changes, no release descriptor, no PR.
>
> ## Questions before I start
>
> 1. **Branch.** The prompt says `spike/blend-decisions`, but this session may only push to `claude/determined-newton-sfkpio`. Should I use the session branch, or do you allow `spike/blend-decisions`?
> 2. **Issue reference.** AGENTS.md requires `Refs #N` on every commit, and no issue covers this spike. Should you open one, should I open a tracking issue, or should the commits on this never-merged branch go without one? The traceability file name needs the number too.
> 3. **Isolation.** Do you approve the local `Directory.Build.props` that doesn't import the parent, which means no DD analyzers on the spike host? The other option is to inherit everything and leave the spike code subject to the DD rules.

### The approval

> Approved, with these changes.
> Answers.
>
> 1. Use the session branch `claude/determined-newton-sfkpio`. I’ll rename it afterwards if it’s worth keeping.
> 2. Open a tracking issue yourself (`gh api`, label `spike`, title “Blend spike: can a domain’s allowed decisions be generated”), reference it on every commit and in the traceability file name. Close it with the findings link when you’re done.
> 3. Approved: a local `Directory.Build.props` that does not import the parent. That’s layout, not suppression, because nothing in the directory is product and the DD rules exist for shipped code. Keep `TreatWarningsAsErrors`, nullable and the off-the-shelf analyzers on; drop only the DD/VARVE analyzer references. Record this choice in the findings.
>
> Vocabulary.
>
> * Drop `GenericSpace` as a class. The paper’s formalism has none, and in your own layer example the “generic space” is the join (`?a spike:references ?b`), which is a `Mapping`. Keep `Mapping` and let the shared slot types be the generic space implicitly.
> * Add `level` (an integer on spaces, rules and blends) with the invariant from the paper §3.3: a blend’s level is strictly greater than the max of its inputs. Check it with one SHACL shape. This is the derivational ordering the ledger lacks (`supersedes` is temporal).
> * Your rule that `emergent` is only ever computed, never typed, stays. Keep it.
> * Stay at or under twenty terms after these two changes.
>
> Test. Your three criteria are the test; don’t relax them. Add one line to the report per attempt saying which criterion failed, so the ten-row table is comparable.
> Row 2 (ADR 0005) is the control for the whole spike: an accepted constraint that layering permits. If no rule derives it without criterion 3 being broken, say so prominently; that’s the clearest evidence either way.
> Ledger gap. Write the inherited-acceptance gap as a proposed issue text for product-cli, not only as a note: a derived version needs a stated acceptance semantics (an ASK over rule and input acceptances at a position), and the protocol spec §9 should say whether such a version may exist unsigned.
> Comparison. The negative-control project with an upward reference is right; also include one same-layer reference, since 0060 makes that a violation and it’s the case a naive `L > M` rule gets wrong if written as `L >= M`.
> Go ahead.

## The report

The findings are [`claude/varve-blend-spike-findings.md`](../../../../claude/varve-blend-spike-findings.md):
the verdict, the ten-row table with the criterion each attempt failed, the
layer comparison, the as-of answer, the projector's reach, the proposed
issues and the vocabulary's account. In short:

- **Verdict, by the test as stated: keep blending, narrowly.**
  - Two accepted rulings come out of a rule with a constraint in neither
    input, computed by `check/criteria.rq`: 0140 `NoWallClockFreshness`'s
    NOW() half (R2) and 0124 `BatchBoundaryIsInNoContract` (R5).
  - **The control is not derived.** Nothing produces 0005's
    `StoreReferencesNoSparql` without breaking criterion 3 or the level
    invariant.
  - The rules that derived the two are this session's proposals, unaccepted,
    so nothing they derive is in force.
  - The criteria do not separate emergence from a general rule's instance:
    all 97 layer verdicts pass them too.
- **The layer rule agrees with DD0001** on 137 of the 138 references whose
  verdict was observed.
  - The one disagreement is a transitive reference DD0001 sees in metadata and
    a `ProjectReference` reader does not.
  - 10 references were not observed: the wasm workload is missing in this
    container.
- **The as-of question.** `Varve.Store.Azure → Varve.Store` entered at
  position 69 (git `c26624a`).
  - The layering rulings were accepted there.
  - The ruling the reference implements, 0134's `ProvidersAreTheirOwnPackages`,
    was a proposal until position 81 (git `6c3dd7c`).
- **The file-only graph check found two things nothing else reports.**
  - Two `InternalsVisibleTo` grants violate 0003. DD0002 misses them, because
    a grant written as an MSBuild item lands in generated code; reproduced in
    `negative-control/`.
  - 0106's `POST /settings` rulings are still in force beside 0118's removal of
    the path.
- **The vocabulary.** Of the twenty terms, five (`Slot`, `slot`, `input`,
  `source`, `target`) are read by nothing. `level` and the computed `emergent`
  test, with its mapping-versus-input distinction, did the discriminating
  work.

### Deviations and mistakes, stated

- **The ledger format document has moved.** The prompt's
  `docs/ledger-format-v1.md` no longer exists in product-cli; it was merged
  into `ledger/spec/ledger-protocol.md`, and that is what the spike aligns to.
- **Version IRIs are not the ledger's.** They are SHA-256 over set, key and
  statement, not the ledger's canonical version hash.
- **SHACL ran over an N-Quads export, not a TriG one.** It is the same
  dataset in another syntax.
- **Three bugs in my own SPARQL were found and fixed during the run**, none in
  Varve: a BIND's scope in R2, acceptance nodes taken for versions, and a
  FILTER's group scope in `in-force.rq`. The findings list them.
- **Reading:** I read ADRs 0003, 0005, 0056 and 0140 in full, the Context and
  Decision of 0136, and every ruling of the ten ADRs and of set zero through
  their decision sets. I did not read the narrative of all of 0010–0023 in
  full, as the prompt asked; their rulings are what the spike encodes.
- **The analyzer and ledger issues are written as proposed texts and are not
  filed**: the analyzer repository and product-cli are outside this session's
  scope.

### Commits

Listed in the findings' closing commit and on the branch; every commit carries
`Refs #128` and the DCO sign-off of the responsible human.
