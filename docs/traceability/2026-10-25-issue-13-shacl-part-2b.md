# Milestone 8, SHACL, part 2b: the validation projection, shapes for the declarations, the REC gate

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The opening message, the
> brief, the plan as submitted and the answers are verbatim. The transcript
> itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#13](https://github.com/Hafeok/Varve/issues/13), milestone 8, closed by this release after part 1 ([#120](https://github.com/Hafeok/Varve/issues/120)) and part 2a ([#138](https://github.com/Hafeok/Varve/issues/138)) |
| **Date** | 2026-10-23 to 2026-10-25 |
| **Tool** | Claude Code 2.1.309 at the start and 2.1.310 by the end, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata (`configured_model`, `session_context.model`, `last_served_model`) |
| **Session identifier** | `session_01L4ZXavrcpYv87dJqPTswDq` |
| **Branch** | `claude/sleepy-cori-szdjyy`, from `main` at 59b230d (`v0.1.0-preview.12`'s descriptor, #141 merged) |
| **Machine** | a cloud container, Intel Xeon @ 2.10 GHz, 4 cores, 15 GiB, Ubuntu 24.04.5 LTS, .NET SDK 10.0.401, runtime 10.0.12 |

## The prompts

### The message that started the session

> @"/root/.claude/uploads/12e0ccba-5ed4-5dae-b368-07143046472c/91d1f526-varve-part2-sessions.zip" Unzip the attached varve-part2-sessions.zip outside the repository and read varve-claude-code-shacl-part2b.md in full. It's the session prompt for SHACL part 2b, closing #13, in github.com/Hafeok/Varve. Work from current main. AGENTS.md applies in full. The design is approved on #13; post a short plan and wait for approval only on anything that departs from it.

### The brief, `varve-claude-code-shacl-part2b.md`

> # Session: SHACL, part 2b — the validation projection, shapes for Varve's declarations, the REC gate, close-out (#13)
>
> Part 2a (#138, PR #140, `v0.1.0-preview.12`) landed:
> - `Varve.Derivation` (layer 2);
> - the classification (ADR 0171);
> - commit-time gating under a shapes binding (ADRs 0169 and 0170);
> - the report on the commit (ADR 0173).
>
> This session lands the rest of the approved plan on #13 (comment of 2026-10-20) with its rulings, and closes #13.
>
> **Starting point.** Current `main`. In parallel, **Management plane part 2** moves event-schema declarations into the management plane (ADR 0167). See "Coordination" below.
>
> **Read before planning:**
> - the plan on #13 and the maintainer's rulings;
> - #140's traceability record (`docs/traceability/2026-10-20-issue-138-shacl-part-2a.md`), including its REC findings;
> - ADRs 0169–0173, 0154 (the deterministic report), 0157 (shapes for event declarations), 0141 (REC), 0130 point 5, and 0107;
> - ADRs 0163–0166 and 0168 (the Management plane);
> - `docs/spec/rdf-canon.md` §6 after #141: the classes where canonical forms are guaranteed.
>
> `AGENTS.md` applies in full. Plan briefly, since the design is approved, and wait for approval only on anything that departs from it.
>
> ## What to build (as approved; ADR numbers 0174–0176)
>
> 1. **The validation projection (ADR 0174).**
>    - One `IProjection` per binding in `Varve.Shacl.Store`, stored under `derived/validation/{enc(binding)}/` in the local cache only.
>    - Rows keyed by (shape, focus node), each with a validity interval.
>    - Generations on a binding or shapes change, swapped whole when complete.
>    - Readable as the reserved graph `https://w3id.org/varve/ns#validation/{enc(binding IRI)}`, as of any position, through the staging view and a `Varve.Protocol` seam.
>    - **The rulings:**
>      - **A projection's lag is reported per binding on readiness, and never makes the dataset unready or "behind".** Only reads of the report graph get `503 validation-behind`.
>      - **Reading the report graph** requires read on every graph the binding validates (an ADR 0107 amendment). Writing it is `graph-not-writable`.
>      - **ADR 0174's consequences** state that every instance rebuilds the projection on cold start, since it isn't shared. Sharing it as extents is a later decision.
>    - **The property:** the report at p equals `ShaclValidator.Validate` over `AsOfAsync(p)`, byte for byte, at every position of generated histories with retractions and shapes changes.
> 2. **Shapes validate Varve's own declarations at runtime (ADR 0175).**
>    - **Event declarations:** ADR 0157's shapes become the runtime validator.
>    - **Management declarations:** write `shapes/management.ttl` for the `varve:` vocabulary, since #134 shipped none, and make it the runtime validator.
>    - **The code validators** (`DeclarationValidator`, `EventDeclarationCheck`) stay in the test projects as the oracle of an agreement property over generated declarations, valid and mutated.
>    - `declaration-invalid` and the event problems keep their wire shape, each finding built from a report result.
> 3. **The REC gate (ADR 0176).**
>    - **The shapes composition.** The gate composes REC 4's shapes graph from the three files, plus two additions, both recorded in ADR 0176 with the exact triples:
>      - `owl:Class rdfs:subClassOf rdfs:Class`;
>      - the `sh:declare` prefix declarations that Brick's three SPARQL constraints need, including `owl:`.
>    - **The vendored test stays.** The test pinning "REC 4.1.0 is refused as vendored" stays.
>    - **The upstream note.** Draft the upstream issue text for REC in the PR, not in the repository: the undeclared prefixes as a defect, and the `owl:Class` triple as a note. The maintainer files it.
>    - **The generator.**
>      - Fix the REC generator so leases carry `rec:leasor`, and bump the generator's output version.
>      - The benchmark README records which generator version each published table used: #110's, #140's and this PR's.
>      - Violating commits for the `reject` tests come from an explicit mutation step in the tests, never from generator defects.
>    - **The gate itself:** REC S and M validate standalone, at commit time under `reject`, and incrementally through the projection, with all three equal.
>      - REC S: at every position, or every 25th plus the last on PRs, with all positions behind `VARVE_LONG_PROPERTIES`.
>      - REC M: 100 sampled positions, nightly.
>    - **The report:** which SHACL features REC uses, the class each falls in, and any shape that can't be evaluated.
>    - **`docs/operator/shapes.md`** tells users binding REC or Brick shapes to add both compositions.
> 4. **Benchmarks:** projection lag, and rebuild time for a shapes change at REC M, slower rows first, with the generator version stated.
> 5. **The close-out of #13:**
>    - README Status and `docs/roadmap.md` (SHACL complete), AGENTS.md's State, `eng/status.cs` and the changelog;
>    - `docs/operator/shapes.md` and `docs/problems/` (`validation-behind`);
>    - the traceability record.
>
>    Chapter 3's SHACL section is the Learning track's.
>
> ## Coordination with Management plane part 2
>
> - **Its event move puts event declarations into management graphs.** Your runtime switch (point 2) goes behind the existing validation entry points, so the move calls the same entry point whichever lands first.
> - **ADR 0157's event shapes must validate event nodes in both homes:** the dataset's catalogue graphs before the move, and the dataset's management graph after it. Target them by class, not by graph, and test both.
> - **Whichever of the two lands second merges `main`,** re-runs both agreement properties, and reconciles `shapes/management.ttl` with any vocabulary terms the move adds.
>
> ## How this lands
>
> - **One PR,** red only on `CS0618`.
> - **The descriptor** is at the next free `0.1.0-preview.N` when it lands (ADR 0102). It carries `accepted-by: mailto:emil@okkels-klein.dk` and `Closes #13` as a trailer on the descriptor commit, and names #13 and #138. Use bare #N elsewhere, and no closing keyword in the PR description (#108).
> - **Commits:**
>   - signed with the agent SSH key, as `Claude (Emil) <claude+emil@okkels-klein.dk>`, the repository's rule regardless of any generic hook;
>   - reference #13, with the DCO sign-off;
>   - never name a model.
> - **A `docs/traceability/` record** (ADR 0033).
> - **No suppression or downgrade of DD or VARVE rules.**
> - **CI images** only from `.github/ci-images.yaml`.
> - **Test time:** size land-gate iteration counts, report each new test's duration per OS (#126), and put long runs behind environment variables that the nightly workflow sets.
> - **Land through `land/`.**

### The plan, as submitted

Posted on #13 (comment 6362866468):

> ## Plan: SHACL part 2b (#13): the projection, shapes for declarations, the REC gate, close-out
>
> From `main` at `59b230d` (preview.12 descriptor, #141 merged). This is the approved plan of 2026-10-20 with its rulings. ADRs 0174–0176, filed unaccepted. Only points **A** and **B** below depart from the session brief, and I'm waiting on those two alone. The rest proceeds now.
>
> ### What I'm building, in order
>
> 1. **ADR 0174, the validation projection.** It's `ValidationProjection : IProjection` in `Varve.Shacl.Store`, one per binding.
>    - **Rows:** keyed by (shape, focus node), each holding its results as canonical terms with a validity interval `[asserted, retracted)`. Each commit is maintained by `IncrementalValidation.Compare` between `AsOf(p−1)` and `AsOf(p)`.
>    - **Generations:** a binding or shapes change starts a new generation, built beside the old one and swapped whole when complete.
>    - **Reads:** `ReportAt(p)` gives a `ValidationReport`. Above the projection's position, a read gets `validation-behind` (503, after one bounded wait).
>    - **Storage:** an `IDerivedStore` the host supplies, under `validation/{enc(binding)}/`. The server passes the local cache (or the file backend's `derived/`), never the object store. A warm restart reuses a manifest that matches. A cold start rebuilds, as the consequences say.
>    - **The reserved graph** `varve:validation/{enc(binding IRI)}` is served at a pinned position through the staging view, by a new `Varve.Protocol` seam the server composes.
>      - Reading it needs read on every graph the binding validates (0107 amended). Writing it is `graph-not-writable` (0130 point 5 amended).
>      - Readiness gives each binding's lag beside the dataset's line. Per the ruling, the lag never makes the dataset unready or "behind".
>    - **The property:** at every position of generated histories with retractions and shapes changes, the projection's canonical N-Quads equal `ShaclValidator.Validate` over `AsOfAsync(p)`, byte for byte.
> 2. **ADR 0175, shapes validate the declarations.** The switch goes behind the existing entry points, `EventDeclarationCheck.Check` and `DeclarationReader.Read(…).Findings`, so Management plane part 2 calls the same ones.
>    - **Event shapes** target by class, and are tested in a catalogue graph and in a management-shaped graph.
>    - **`declaration-invalid` and the event problems keep their wire shape,** with each finding built from a report result.
>    - **The agreement properties** run over generated declarations, valid and mutated.
> 3. **ADR 0176, the REC gate.**
>    - **The composition:** the three files, plus `owl:Class rdfs:subClassOf rdfs:Class`, plus `sh:declare` for `rdf:`, `rdfs:`, `owl:`, `brick:` and `ref:`. ADR 0176 records the exact triples, and the "refused as vendored" test stays.
>    - **The gate:** standalone, commit-time `reject` and the projection agree at every 25th position of S plus the last on PRs, every position behind `VARVE_LONG_PROPERTIES`, and 100 sampled positions of M nightly. Durations are reported per OS.
>    - **The features report:** which SHACL features REC uses, the class of each, and any shape that can't be evaluated.
>    - **The upstream note:** I'll draft it in the PR.
> 4. **Benchmarks:** projection lag, and rebuild time for a shapes change, at REC M.
> 5. **The close-out:** README Status, the roadmap, AGENTS.md's State, `eng/status.cs`, the changelog, `docs/operator/shapes.md`, `docs/problems/validation-behind.md`, the descriptor at the next free `0.1.0-preview.N`, and the traceability record.
>
> ### Where this departs from the brief
>
> **A. The generator is already fixed.** #140 shipped `rec:leasor` and output version 2 (`RecPortfolio.Version = 2`, hashes re-pinned), and its benchmarks were re-run on version 2. So this PR changes no generator and bumps no version.
> - The benchmark README will record #110's tables as version 1, #140's as version 2, and this PR's as version 2.
> - The `reject` tests' violations stay an explicit mutation step.
>
> **B. Shapes are the runtime validator, with a residual check in code.** Some checks SHACL Core can't state. For events these are ADR 0157 point 3: the SELECT parses and projects the fields, field names are unique per type, no blank nodes or unknown `varve:` terms, names across catalogue graphs, and the version history and compatibility. For management declarations the equivalents are:
> - no blank node anywhere, and no unknown or reserved `varve:` term;
> - which graph names the system dataset admits;
> - `varve:datasetName` equal to the graph's name;
> - the graph template syntax, and the "operator only in the fleet graph" rule.
>
> `shapes/management.ttl` (embedded as `src/Varve.Management/shapes/management.ttl`) states everything Core can. At runtime the shapes decide those findings, and a small residual check keeps the rest, as 0157 point 3 already does for events. The full code validators move to the test projects unchanged, as the oracle. The agreement property checks two things: shapes and oracle agree on everything the shapes state, and the residual pins the boundary.
> - **Core only.** `Varve.Shacl.Sparql` is at layer 4, beside `Varve.Management` and `Varve.Events`, so neither may reference it.
> - **The references:** both packages reference `Varve.Shacl` (layer 3). That amends 0157 point 1 and 0166.
> - **Finding messages** come from the shapes' `sh:message`. I'll keep the code's wording wherever the rule is the same.
>
> If you'd rather the residual be SHACL-SPARQL composed at layer 5, say so. Then the entry points would move to `Varve.Protocol`, which changes the coordination with Management plane part 2.
>
> ---
> _Generated by [Claude Code](https://claude.ai/code)_

### The answers to the plan

The session asked about points A and B, the two departures:

**Point B.** The question:

> Plan point B on #13: how should Varve's own declarations be validated at runtime? Management shapes are SHACL Core only (Varve.Shacl.Sparql is layer 4, beside Varve.Management/Varve.Events). What Core can't state (blank nodes, unknown varve: terms, graph names, datasetName = graph name, fleet-graph rules, template syntax; for events ADR 0157 point 3 plus version history) stays a small residual check in code, and the full code validators move to the tests as the oracle.

The answer: **"Core shapes + residual code (Recommended)"**. That option read: "As proposed: Varve.Management and Varve.Events reference Varve.Shacl; the entry points (EventDeclarationCheck.Check, DeclarationReader.Read) stay where they are, so Management plane part 2 is unaffected. Amends 0157 point 1 and 0166."

**Point A.** The question:

> Plan point A: the rec:leasor generator fix and the output-version bump already landed in #140 (version 2). OK to change no generator here, and only record the versions (#92 and #110 on version 1, #140 and this PR on version 2) in the benchmark README?

The answer: **"Yes, record only (Recommended)"**. That option read: "No generator change; the README records each table's generator version, already committed on the branch."

The answers were recorded on #13 (comment 6397331200):

> The maintainer answered points A and B of the part 2b plan in the session, and both proposals stand:
>
> - **A. No generator change.** The benchmark README records the generator version behind each table: #92's and #110's are version 1, #140's and part 2b's are version 2.
> - **B. SHACL Core shapes plus a residual code check.** `Varve.Management` and `Varve.Events` reference `Varve.Shacl` (amending ADR 0157 point 1 and 0166). The validation entry points stay where they are, and the full code validators move to the tests as the oracle.
>
> ---
> _Generated by [Claude Code](https://claude.ai/code)_

The session's context was summarised twice. Each time, the work continued
from the summary and the files.

## The report

### What shipped, by ADR

- **0174, the validation projection.** `ValidationProjection` in
  `Varve.Shacl.Store` is an `IProjection` per binding.
  - **Rows:** keyed by (shape, focus node), with validity intervals. Each
    commit is maintained from the pairs `IncrementalValidation.Compare`
    reports.
  - **Snapshots:** saved to the host's `IDerivedStore` under
    `validation/{enc(binding)}/` (`VVP1`). The server passes the local
    cache, every 1,000 positions.
  - **The report graph** is `varve:validation/{enc(binding IRI)}`. It is
    composed through `Varve.Protocol`'s `IValidationReports` seam over the
    scoped staging view, and read on first need.
    - **Reading it** needs read on every validated graph (0107 amended).
    - **Writes** to it are refused by `ReportGraphGuard` as
      `graph-not-writable` (0119 amended).
    - **Above the projection's position** a read is `503
      validation-behind`, after a wait of up to 2 s, with `Retry-After: 1`.
  - **Readiness** gives each binding's lag in a `validation` line, and never
    makes the dataset unready.
  - **Amendments:** dated amendments to 0107, 0119, 0130 and 0155.
- **0175, shapes validate the declarations.** `EventDeclarationCheck.Check`
  validates by ADR 0157's shapes, and `DeclarationReader.Read` by
  `shapes/management.ttl`, new and embedded.
  - **The residual** is a small code check of what Core cannot state.
  - **The oracle:** the code validators as they stood are frozen in the test
    projects, under `Oracle/`.
  - **Amendments:** dated amendments to 0145, 0157 and 0166.
- **0176, the REC gate.** The composition is
  `testdata/rec/gate/composition.ttl`:
  - `owl:Class rdfs:subClassOf rdfs:Class`;
  - `sh:declare` on `rdf:`, `rdfs:`, `brick:` (with `owl:`) and `ref:`
    (with `brick:`).

  `RecGateTests` runs the three ways and pins
  `testdata/rec/gate/features.md`. `.github/workflows/nightly.yml` runs the
  SHACL test projects with `VARVE_LONG_PROPERTIES`. `docs/operator/shapes.md`
  is the operator's page.

### Where the work departed from the plan, and why

- **A cold start begins at the head.** The plan had the projection replay
  from position 0. Replaying through `AsOfAsync` cost a scan per position,
  quadratic over a history, and REC M did not finish. Now:
  - **A cold start** validates in full at the head and maintains from there
    (`ColdStartAtTheHead`).
  - **While its commit is the head,** the projection reads through `Pin`.
  - **A position below the floor** is answered by a full validation as of
    it, which is the same report by the property.

  The ruling that every instance rebuilds on a cold start holds.
- **The shapes graph is indexed.** `ShapesSource` indexes the shapes graph
  by subject, predicate-object, object and predicate. REC's composition
  loaded in 14 s over an unindexed `InMemoryDataset`, and now loads in about
  1 s. The W3C SHACL suites are unchanged: 628 passing, 28 exempt.
- **REC S costs about nine minutes on a pull request.** That is more than
  part 2a's figure suggested. Brick's two `brick:Entity`-targeted SPARQL
  constraints run before and after every building commit, at about 185 ms
  each. ADR 0176 records it, and the pull request flags it.
- **The event check's agreement is on the verdict.** `EventDeclarationCheck`
  answers its first problem only, so the runtime and the oracle are compared
  on two things:
  - whether the commit is accepted, and the problem type;
  - for an incompatible change, the declaration and the fields.

  That the shapes name the node the code names is ADR 0157's property,
  which stands.

### Findings outside this repository

- **REC 4.1.0's undeclared prefixes and the `owl:Class` note.** The upstream
  issue text is in the pull request's description, for the maintainer to
  file. No exemption is taken.

### Properties and tests

| Property | Per CI run | Long run (`VARVE_LONG_PROPERTIES`) | Counterexamples |
|---|---:|---:|---|
| The projection's report at every position equals a full validation there, byte for byte, over generated histories with retractions and shapes changes (`Varve.Shacl.Store.Tests`) | 200 histories | 2,000 | none at the head |
| The event check against its oracle: same verdict and problem type, and same declaration and fields for an incompatible change (`Varve.Events.Tests`) | 2,000 | — | none. Dropping the shapes' findings at runtime fails it |
| The event shapes against the code on what the shapes state (ADR 0157, `Varve.Events.Tests`) | 2,000 | — | none |
| The management shapes against the code, and the runtime against its oracle (`Varve.Management.Tests`) | 1,000 | 10,000 | none. Dropping the shapes' findings at runtime fails it |
| REC S: standalone, commit-time `reject` and the projection agree (`Varve.Shacl.Store.Tests`) | every 25th position and the last, 41 | all 1,001 | none |
| REC M, the same | — | 100 positions | not run in this session; the nightly runs it |

Example tests cover:
- the report graph through the protocol:
  - read by a caller who reads every validated graph, and absent for any
    other;
  - `validation-behind` above the projection, and not for a read that never
    needs the report;
  - a write refused, for an administrator too;
- the same through the server, by query and by the Graph Store;
- a snapshot loaded, and another binding's snapshot ignored;
- a shapes graph that does not load, served as its problems until fixed;
- a cold start at the head;
- the event shapes in a catalogue graph and in a management graph.

### Durations

Linux, the machine above, `dotnet test -c Release --no-build`, slower rows first. Earlier in the session, REC S's test alone took 9 m 17 s.

| Project | Tests | Duration |
|---|---:|---:|
| `Varve.Shacl.Store.Tests` (REC S's agreement on its PR sample; the three long tests skipped) | 24, 3 skipped | 6 m 53 s |
| `Varve.Rec.Tests` | 6 | 55.8 s |
| `Varve.Protocol.Tests` | 116 | 31.3 s |
| `Varve.Server.Tests` | 119, 12 skipped | 10.2 s |
| `Varve.Events.Tests` | 99 | 5.5 s |
| `Varve.Shacl.Sparql.Tests` | 13 | 5.4 s |
| `Varve.Shacl.Tests` | 34 | 4.8 s |
| `Varve.Management.Tests` | 34 | 4.6 s |
| `Varve.Derivation.Tests` | 8 | 1.6 s |

### Benchmarks: the projection's lag and rebuild

`ValidationProjectionBench` (`--validation-projection S|M`), the generator's
output version 2, on the machine above. Slower rows first:

| REC | Cold start | Rebuild after a shapes change (3 runs) | Lag per commit, 200 commits: mean | median | p95 | max |
|---|---:|---:|---:|---:|---:|---:|
| M (25,004 commits) | 183.9 s | 213.4 s (209.7–218.1) | 9.12 ms | 8.06 ms | 20.00 ms | 68.89 ms |
| S (1,004 commits) | 4.3 s | 3.6 s (3.5–3.7) | 5.54 ms | 4.19 ms | 14.44 ms | 68.34 ms |

The benchmark README records the generator version behind each table: #92's
and #110's are version 1, #140's and these are version 2.

### Public API

All additive, nothing removed:
- `Varve.Protocol`, 22 lines;
- `Varve.Shacl`, 15;
- `Varve.Shacl.Store`, 14;
- `Varve.Management`, 2;
- `Varve.Rdf`, 1.

The descriptor lists the members.

### The pipeline

`dotnet build -c Release`, with `CS0618` as a warning, is clean. Eleven
gates pass at the branch's head:
- `register`, `licence-headers`, `decision-sets` and `banned-symbols`;
- `issue-refs` and `dco`;
- `ci-images`;
- `release-pending`, `changelog`, `release` and `status`.

The tests of every project this change touches pass (above). Until ADRs
0174–0176 are accepted, every job that builds the solution fails on
`CS0618` alone.

### What this environment could and could not do

- **The long runs.** REC S at every position and REC M were not run here.
  The nightly workflow runs them.
- **Other operating systems.** Their durations come from CI's run of the
  pull request (#126).

### What the maintainer is asked for

- **Accept** ADRs 0174–0176 and their decision sets, and the amendments.
- **File the REC upstream note** in the pull request's description.
- **The release number.** #147 and #148 also propose
  `v0.1.0-preview.13`, so whichever lands later renumbers.
