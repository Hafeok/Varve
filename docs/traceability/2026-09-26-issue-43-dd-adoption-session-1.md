# Adopting `DecisionDriven.Analyzers`, session 1 — ADRs and the decision ledger

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer. Developed with AI
> assistance under human review.

| | |
|---|---|
| **Issue** | [#43](https://github.com/Hafeok/Varve/issues/43), the adoption, used by all three sessions |
| **Date** | 2026-09-25 to 2026-09-26 |
| **Tool** | Claude Code 2.1.282 to 2.1.283, a cloud session |
| **Model** | `claude-opus-5-5` (Claude Opus 5.5), configured and served. Confirmed from the session's own metadata, not from memory |
| **Session identifier** | `session_01CYSLUQ4DCr1iNkbHEW4eaj` |
| **Branches** | `claude/adrs-decision-ledger-28vlvs`, then suffixed branches, one pull request each (ADR 0034's amendment) |
| **Pull requests** | the ADRs and the check; then the enumeration, ten ADRs each |

## The prompt

### First message

The maintainer attached two drafts from the analyzer repository's adoption
bundle: `ADR-A03-build-time-dependencies.md` and
`ADR-A13-varve-specific-rules.md`. Both are held by the maintainer. A03's
generic twin is in the analyzer repository at
`docs/drafts/ADR-A03-build-time-dependencies.md`.

> Session 1: ADRs and the decision ledger
> Varve adopts DecisionDriven.Analyzers (hafeok/decision-driven-analyzers, packages DecisionDriven.Analyzers and DecisionDriven.Report, 0.1.0-preview.3, development-time only). Most of Varve's architecture rules become configuration of that package; Varve.Analyzers keeps only what depends on Varve facts. This session lands the ADRs and enumerates every existing ADR into the decision ledger. No analyzer is referenced and no code changes.
> Read first, in this order: the analyzer repository's README.md, docs/decisions/*.md, docs/rules/DDnnnn.md, docs/rules/ledger-input.md and docs/report.md; then the attached ADR-A03 and ADR-A13; then Varve's docs/adr/ in full. Where this prompt and the package documentation disagree, the package documentation wins and you report the disagreement.
> AGENTS.md applies: conventional commits, Refs #N (open one issue for the adoption and use it for all three sessions), DCO sign-off, traceability record per session. Plan first, wait for approval.
> The next free ADR number is 0062 or higher; take the next free. ADR 0060 replaced 0003's layer table (hosts are layer 6, executables only); ArchLayer per project follows that table. Confirm each of these on main and report any that differ: ADR 0004 introduced Varve.Analyzers with a reserved rule table (VARVE0001 layer direction, 0002 layer declaration, 0003 InternalsVisibleTo, 0004 banned names, 0005 mutable static state, 0006 hot path, 0007 public contract types, 0008 suppression justification); VARVE0001 and VARVE0002 are implemented and nothing else is; VARVE0007's wording was amended in 5b to cover contracts over algebra types; ADR 0009 is the dependency policy with the register in Directory.Packages.props; ADR 0011 chose long for positions as a BCL primitive; ADRs 0032 and 0034 are the trunk and signing decisions; TreatWarningsAsErrors is on everywhere; there is no docs/decisions/ yet; the layer of every project in src/ and tests/ per its VarveLayer; hosts are layer 6 per ADR 0060 (the AOT and WASM smoke apps and the benchmarks declare 6; Varve.Server and the CLI are not built; test projects declare none).
> 1.2 ADRs to write, all Accepted unless stated
> Adoption of DecisionDriven.Analyzers (from the generic ADR-A02, cited by URL into the analyzer repository, not copied): two analyzer packages; Varve.Analyzers keeps Varve-specific rules only; VARVE0001 and VARVE0002 retired, ids never reused. Dated amendment to ADR 0004: superseded in part, the reserved rule table retired (0003–0008 reserved ids released; DD0002 covers InternalsVisibleTo, DD0005 banned names, DD0004 static state, DD0010–0011 contract types, DD0008 suppressions), the hot-path rule to be re-numbered by the next ADR. Two documents must never claim one id.
> ADR-A03 as a Varve ADR, an amendment to the dependency policy (ADR 0009): the build-time packages, PrivateAssets="all", the Roslyn floor already decided at the 3a close-out, and the rule that BannedSymbols.txt entries cite an ADR in their comment line. Register entries for DecisionDriven.Analyzers and DecisionDriven.Report cite this ADR. Note the two packages are prerelease and the version policy for them (latest preview until the analyzer repository ships 1.0).
> ADR-A13 as a Varve ADR: the configuration values, the [DomainModel] namespaces with the verified names (see 1.3), the BannedSymbols.txt additions, and the two Varve rules re-numbered VARVE0003 (hot-path discipline) and VARVE0004 (hot-path signature) as A13 states them. Two corrections to A13 that you must check and apply: ArchContractTypeAssemblies gains Varve.Sparql for the layer-3 projects only, set in their project files and not globally, because Varve.Sparql.Evaluation's contracts (IServiceHandler, the evaluator entry point) take algebra types (5b's VARVE0007 amendment) while Varve.Store's contract must stay SPARQL-free by ADR 0005; and the [DomainModel] list must name real namespaces, so replace A13's guesses with what src/ contains.
> Positions and ids as wrapper types, superseding ADR 0011's choice of long and retiring VARVE0007's reservation: Position, and whatever other primitives sit on Varve.Store's and Varve.Rdf's contract and model surfaces (TermHandle is already a struct; check ids, byte counts, timestamps), become readonly record struct wrappers in the store's model namespace. Varve.Rdf does not learn about positions. Expected public API diff: wrapper types only. Implemented in session 2; decided here so the enumeration can cite it.
> Expected-red pull requests, amending ADRs 0032 and 0034: a PR may arrive red when, and only when, the red is CS0618 from citing decisions filed without acceptance; never from a DD or VARVE rule; the maintainer's accepted-by edit on the branch is the review, and the PR is merged only green. A committed WarningsNotAsErrors for CS0618 is forbidden; verifying a branch locally with a command-line override is allowed and is not configuration.
> InMemoryDataset leaves the model namespace: it moves to Varve.Rdf.Datasets, a sibling namespace outside the [DomainModel] prefix in the same assembly, because it is a store of values and not a value (the same distinction the log-first model draws). Implemented in session 2.
> Each ADR checked against every accepted ADR and the specification; alternatives and consequences as always.
> 1.3 The [DomainModel] namespaces
> Verify by reading src/. Expected: Varve.Rdf (terms, quads, IQuadSource, delta, overlay), Varve.Iri, Varve.Xsd, Varve.Sparql.Algebra, and the store's model namespace for commits, positions and records (find its real name; if the log types share the root Varve.Store namespace with the engine, the ADR in 1.2 decides a model sub-namespace they move to in session 2). Varve.Shacl.Reports does not exist yet and is listed as planned. Varve.Sparql.Evaluation and Varve.Turtle are not model namespaces.
> Namespace varve. For every Varve ADR, in numeric order, docs/decisions/<set-id>.md in the interim front-matter form the analyzer README specifies: set from the ADR title in kebab-case, one decision per ruling with a key matching ^[A-Z][A-Za-z0-9]{0,63}$ unique across the namespace, a one-line statement, and accepted-by/accepted-at transcribed from the ADR's own status and date. Transcribing an existing human-accepted ADR is allowed and expected; the maintainer reviews the enumeration in the PR. Supersession is not obsolescence. A ruling that a later ADR supersedes appears once, in the superseding ADR's set file, under the same key, with that ADR's acceptance; the superseded ADR's set file holds only the rulings still in force, and if none are it has no set file. This holds for partial supersession, which is what most of Varve's dated amendments are: the amended ruling moves to the amending set under its key. revoked-at is reserved for a ruling explicitly withdrawn with no successor; it should be rare, and every use is listed in the session report.
> Ten ADRs per pull request, in order, so the enumeration is reviewable. An ADR whose rulings cannot be expressed as keys with one-line statements is a finding: file what can be, list the rest in the report, and do not pad. Do not add keys to make a later citation fit; that is session 2's two-bucket sorting.
> The set files are input to the generator only when session 2 wires them; until then DDGEN0001 cannot fire, so validate the front matter with the analyzer repository's documented rules by a small file-based C# check in eng/ that fails on duplicate keys, bad key syntax and missing fields. It runs in the build job from this session on.
> 1.5 Report
> The ADRs with numbers and what each amends or supersedes; the set/decision table with counts; every ADR that could not be fully enumerated and why; the verified facts from 1.1 with any deviation; the [DomainModel] namespaces as found; disagreements between this prompt, the drafts and the package documentation. Filed as the traceability record and as each PR body.

### The maintainer's decisions on the plan

> Approved as presented, with the three package-doc gaps filed as issues in the analyzer repository.
>
> 1. Branches: (b) on suffixed branches if the proxy accepts them; else (a), restarting from main after each merge. I merge session 1 PRs as they arrive.
> 2. accepted-by: mailto:emil@okkels-klein.dk on every transcribed acceptance.
> 3. The store's model namespace is Varve.Store.Log; ADR 0065 decides it with the reason that the log is the model, and lists the moving types as you enumerated them.

### Decided during the work

The session found that ADR 0067 as specified could not work (see
*Disagreements*, 2), and put two questions. The first rested on a claim the
session then found to be false and corrected (see *Disagreements*, 3).

> **Q:** How should 0067 decide it? **A:** Builder escape hatch.
>
> **Q (after the correction):** With that corrected, no rule forces a change to InMemoryDataset. How should ADR 0067 decide? **A:** Builder anyway.
>
> **Q:** Retiring VARVE0002 loses enforcement that DD0001 doesn't replace (an undeclared project is unchecked; ADR 0060's executable ⇔ layer 6; ArchCompositionRoot as a second declaration). How should the lost checks be handled? **A:** New VARVE0005.

## Report

### The ADRs

| # | Title | Amends or supersedes |
|---|---|---|
| [0062](../adr/0062-adopting-decisiondriven-analyzers.md) | Adopting `DecisionDriven.Analyzers` | **Supersedes 0003 in part** (the suppression escape) and **0004 in part**: its reservation table. `VARVE0001`/`0002` are retired; `0003`–`0008` are released to `DD0002`, `DD0005`, `DD0004`, the hot-path pair, `DD0010`/`DD0011` and `DD0008` |
| [0063](../adr/0063-build-time-analyzer-packages.md) | Build-time packages for the analyzers, and what `BannedSymbols.txt` must cite | **Amends 0009** |
| [0064](../adr/0064-varve-configuration-and-hot-path-rules.md) | Varve's configuration of the `DD` rules; `VARVE0003`/`VARVE0004`; `VARVE0005` layer declaration | **Supersedes 0026 in part** (where `[HotPath]` lives) and **0003 in part** (`VarveLayer` becomes `ArchLayer`). Renumbers 0004's `VARVE0006`, and takes over `VARVE0002`'s uncovered half |
| [0065](../adr/0065-wrapper-types-and-the-store-log-namespace.md) | Positions, ids and sizes as wrapper types; the log's values in `Varve.Store.Log` | **Supersedes in part** 0011 (primitive results), 0040 (member types) and 0049 (`Count`'s type) |
| [0066](../adr/0066-expected-red-pull-requests.md) | Expected-red pull requests | **Amends 0032 and 0034** |
| [0067](../adr/0067-inmemorydataset-is-a-value-built-by-a-builder.md) | `InMemoryDataset` is an immutable value, assembled by `InMemoryDatasetBuilder` | Refines 0022's in-memory dataset; supersedes nothing |

Each superseded or amended ADR's Status line names its successor, and
`docs/adr/README.md` has the rows. No accepted text was otherwise edited.

### Verified facts (1.1), on `main` at `6a65d41`

| Fact | Found |
|---|---|
| Next free ADR number | **0062.** Holds |
| ADR 0004's reservation table | Holds: `VARVE0001`–`0008` exactly as listed |
| `VARVE0001`/`0002` implemented, nothing else | Holds. `src/Varve.Analyzers` has the two analyzers and nothing more |
| `VARVE0007` amended in 5b for algebra types | Holds, as a dated amendment inside ADR 0004 (2026-09-25) |
| ADR 0009 is the dependency policy, register in `Directory.Packages.props` | Holds. The Roslyn floor is 5.0.0, the same floor the analyzer repository pins |
| **ADR 0011 chose `long` for positions** | **Differs.** 0011 names no type. It says the transaction results are expressed "over BCL primitives and `Varve.Rdf` types only". `long` is milestone 4's implementation, and ADR 0040 wrote `int`/`long`/`string` into the storage members. ADR 0065 supersedes that sentence and 0040's types, and says so |
| ADRs 0032 and 0034 are trunk and signing | Holds |
| `TreatWarningsAsErrors` everywhere | Holds: root `Directory.Build.props`, `eng/Directory.Build.props`, and `tools/repo-standard`, which imports the root props |
| No `docs/decisions/` | Holds |
| Layers | Iri 0, Xsd 0, Rdf 1, Turtle 2, Sparql 2, Sparql.Results 2, Sparql.Evaluation 3, Store 4, Sparql.Store 5; AotSmoke, WasmSmoke, Benchmarks 6; every `*.Tests` project `none`. Holds |
| Deviations in the layer list | `Varve.Analyzers` declares `none` (not in the table; build-time). The analyzer's own fixtures under `tests/fixtures/` declare 0, 1, 2 or `none` on purpose, as the rule's test cases. `tools/repo-standard` declares `none`. None is a defect |
| Package version | `0.1.0-preview.3` is the newest of both packages on nuget.org, and tag `v0.1.0-preview.3` exists. No newer preview |

### The `[DomainModel]` namespaces as found (1.3)

| Namespace | Found |
|---|---|
| `Varve.Rdf` | **One namespace for the whole assembly** (21 files): terms, quads, `TermHandle`, `IQuadSource`, `QuadDelta`, `QuadOverlay`, `InMemoryDataset`, `TermArena`, the canonicaliser. There is no sub-namespace |
| `Varve.Iri`, `Varve.Xsd` | One namespace each |
| `Varve.Sparql.Algebra` | Exists, beside `Varve.Sparql.Parsing` and `Varve.Sparql.Writing` |
| The store's model | **Does not exist.** All of `Varve.Store` (17 files, engine and log values together) is in the root namespace. ADR 0065 decides `Varve.Store.Log` and lists the types that move |
| `Varve.Shacl.Reports` | Planned; no assembly |
| Not model | `Varve.Sparql.Evaluation` and its five sub-namespaces, `Varve.Turtle`, `Varve.Sparql.Results`, `Varve.Sparql.Store` |

### Disagreements between the prompt, the drafts and the package documentation

Where the package documentation disagreed, it won.

1. **ADR 0026 is superseded in part, and the prompt did not list it.** A13 and
   the session 2 prompt use the package's generated
   `DecisionDriven.HotPathAttribute`, which takes a decision argument. ADR 0026
   fixed `Varve.HotPathAttribute` in `eng/`. ADR 0064 supersedes 0026's
   placement.
2. **`Varve.Rdf.Datasets` cannot be "outside the `[DomainModel]` prefix in the
   same assembly".** The package matches a prefix and every namespace under it
   (`DomainModelNamespaces.cs`, and the `DD0010` page). `DD0006` puts every
   public type under the assembly's root. So every namespace in `Varve.Rdf` is
   in the model. The maintainer chose the builder (ADR 0067). Filed upstream as
   [decision-driven-analyzers#46](https://github.com/Hafeok/decision-driven-analyzers/issues/46).
3. **`DD0019` would not have reported `InMemoryDataset` anyway.** The session
   first told the maintainer it would, and corrected that before the ADR was
   written. The rule skips private members and never inspects methods, so a
   class that mutates private collections through `Add` passes. The builder is
   a design decision, not a rule's demand. Filed upstream as
   [decision-driven-analyzers#47](https://github.com/Hafeok/decision-driven-analyzers/issues/47).
   `TermArena` is in the same position, and ADR 0067 leaves it to session 2.
4. **`DD0008` does not cover `VARVE0008`.** The prompt said "DD0008
   suppressions". `DD0008` forbids suppressing `DD` and product rules outright,
   while `VARVE0008` was about every suppression, including the IL and CA rules
   that remain suppressible with an ADR citation. ADR 0062 keeps ADR 0004's
   policy for those, as a review obligation, and does not claim coverage.
5. **The composition root.** A13 says `Varve.Server` and the CLI only. ADR 0060
   and the session 2 prompt say every layer-6 executable. ADR 0064 follows 0060.
6. **`dd_banned_names`.** The session 2 prompt says to set it "as ADR 0004 had
   them". The option *replaces* the package's list, which already contains all
   five of 0004's names plus five more, so setting it could only narrow the
   list. ADR 0064 leaves it unset.
7. **A13's `System.Reflection.*` ban** would ban `AssemblyMetadataAttribute`,
   which the build writes to carry every assembly's layer, and the SDK's
   assembly-information attributes. ADR 0064 bans the inspecting and invoking
   members instead.
8. **A13's LINQ ban for layers ≤ 4 is stricter than the brief**, which bans
   LINQ in marked hot paths. ADR 0064 takes it and says so. It costs one file
   in `Varve.Sparql.Evaluation`.
9. **A13 put all configuration in `Directory.Build.props`.** `ArchLayer` and
   the layer-3 widening are per project.
10. **The package's own documentation**, filed upstream as the maintainer
    asked:
    - The CHANGELOG has no `0.1.0-preview.3` section although the tag and
      packages exist:
      [#43](https://github.com/Hafeok/decision-driven-analyzers/issues/43).
    - The README does not connect `DdLedgerDirectory` to the `DdLedger`
      metadata the `.targets` globs:
      [#44](https://github.com/Hafeok/decision-driven-analyzers/issues/44).
    - The README's quick start omits the `IncludeAssets="analyzers;build"` that
      ADR-A02 prescribes:
      [#45](https://github.com/Hafeok/decision-driven-analyzers/issues/45).
11. **Retiring `VARVE0002` would have dropped enforcement.** `DD0001` is
    silent on a project that declares no `ArchLayer`, and has no view of ADR
    0060's executable ⇔ layer 6 rule. `ArchCompositionRoot` is also the second
    declaration 0060 rejected. The maintainer chose a new `VARVE0005` (ADR
    0064). The generic half is proposed upstream as
    [decision-driven-analyzers#48](https://github.com/Hafeok/decision-driven-analyzers/issues/48).
    Found while enumerating 0003, which also showed that 0064 supersedes
    0003's declaration mechanism and 0062 its suppression escape. Both now say
    so.
12. **`dependency-register.cs` reads only the major version**, so every `0.x`
    preview bump passes as maintenance. ADR 0063 states the gap and makes the
    changelog in the bump commit the review, rather than special-casing two
    package names in the gate.

### The front-matter check

`eng/decision-sets.cs` runs in the `build` job (before restore) and as the
`decision-sets` job in `eng/ci.cs`.

- **Package rules:** key syntax, a key claimed twice anywhere in the
  namespace, missing `set`/`namespace`/`key`/`statement`, acceptance fields in
  pairs, `mailto:` identities, and `xsd:dateTime` dates.
- **Varve's rules:** namespace `varve`, the file named for its set, `adr`
  naming a real ADR once, and statements double-quoted. The package's reader
  cuts an unquoted value at ` #`.
- **Unknown fields are reported** because the reader silently ignores them, and
  a misspelt `accepted-by` would read as unaccepted.

Its failure paths were run before it was trusted, against scratch fixtures:

```
decision-sets: 11 finding(s) in …/fx/bad:
  licence.md:12: unknown field 'acepted-by'. The package's reader ignores it.
  licence.md:10: key 'bad_key' does not match ^[A-Z][A-Za-z0-9]{0,63}$ (DDGEN0002).
  licence.md:11: the statement of 'bad_key' is not double-quoted.
  licence.md:13: key 'Licence' is also claimed at licence.md:6 (DDGEN0001).
  licence.md:15: accepted-at '2026-09-22' of 'Licence' is not an xsd:dateTime with a zone.
  licence.md: 'Licence' has accepted-at without accepted-by; the two go together.
  other-name.md: set id 'wrong-name' does not match the file name; it should be wrong-name.md.
  other-name.md: namespace 'ddd'; every Varve decision is in 'varve'.
  other-name.md: adr 9999 has no docs/adr/9999-*.md.
  other-name.md: no decisions. An ADR with no ruling in force has no set file.
  stray.md: no front matter. The package skips it, so nothing in it can be cited.
exit=1

decision-sets: 4 finding(s) in …/fx/bad2:
  x.md: no 'set'.
  x.md: a decision with no 'key'.
  x.md: decision 'NoStatement' has no 'statement'.
  x.md:7: accepted-by 'emil@okkels-klein.dk' of 'NoStatement' is not a mailto: identity.
exit=1   (and the revoked entry listed in the summary)

decision-sets: no decision directory at '/nonexistent'.
exit=2
```

### The enumeration

Filled in pull request by pull request.

**How rulings were chosen:**
- **What was enumerated:** the Decision section, dated amendments, and
  Consequences that bind later work.
- **What was not:** rejected alternatives, context, and open questions. They
  are not rulings.
- **Split rulings:** when a later ADR supersedes part of a ruling, the part
  still in force keeps its key in the old set, and the superseded part is a key
  in the superseding set. `SuppressionCitesAdr` in 0004 and
  `NoSuppressionOfDdOrVarveRules` in 0062 are the first case.
- **Keys moved forward:** a ruling that moves to a later set is listed in the
  body of the set it left, and it is written when that later set is.

| ADRs | Set files | Decisions | Pull request |
|---|---:|---:|---|
| 0001–0010 | 8 | 67 | part 2 |
| 0011–0020 | 8 | 65 | part 3 |

**No set file, because wholly superseded:**
- 0002, superseded by 0031. Its rulings move to 0031's set.
- 0006, superseded by 0009.
- 0019, superseded by 0023.
- 0020, superseded by 0028.

**ADRs not fully enumerated, and why:**
- **0006.** 0009 superseded it whole, replacing its package table with the
  register. Two of its rulings are restated by no later ADR and survive only
  as configuration:
  - `dotnet test` runs on Microsoft.Testing.Platform, so
    `Microsoft.NET.Test.Sdk` and the VSTest adapter are absent (`global.json`);
  - the `AngleSharp` transitive pin (the register, citing 0009).

  Neither is filed: there is no in-force ruling to transcribe, and filing one
  would be padding.
- **0001.** GOVERNANCE.md's *dated amendment* inside an accepted ADR is
  practised in at least eight ADRs, but no ADR decides it. 0001 permits only a
  Status-line edit. It is not filed as a decision, because that would be adding
  a key to fit practice. It is a question for the maintainer: an ADR amending
  0001, or a stop to the practice.
- **0003.** Open question 1 is not a ruling and is not enumerated.
- **Open questions and revisit conditions throughout.** Q2 and Q3 (0013), Q1
  (0012, answered by 0044), and the revisit conditions of 0012 and 0018 are a
  falsifier or a question, not a ruling, and are not enumerated.

**`revoked-at` uses:** none.

### Upstream issues

In `hafeok/decision-driven-analyzers`:

- [#43](https://github.com/Hafeok/decision-driven-analyzers/issues/43), [#44](https://github.com/Hafeok/decision-driven-analyzers/issues/44), [#45](https://github.com/Hafeok/decision-driven-analyzers/issues/45): documentation gaps.
- [#46](https://github.com/Hafeok/decision-driven-analyzers/issues/46): `[DomainModel]` prefix semantics.
- [#47](https://github.com/Hafeok/decision-driven-analyzers/issues/47): `DD0019`'s blind spot for mutation through methods.
- [#48](https://github.com/Hafeok/decision-driven-analyzers/issues/48): a family project must declare `ArchLayer`.
