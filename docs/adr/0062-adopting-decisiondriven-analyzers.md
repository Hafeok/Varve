# 0062 — Adopting `DecisionDriven.Analyzers`: generic rules from a package, Varve's own rules only in `Varve.Analyzers`

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the adoption plan
(issue [#43](https://github.com/Hafeok/Varve/issues/43)). **Supersedes ADR
[0004](0004-enforcement-by-analyzers.md) in part**: its id reservation table,
and `VARVE0006`'s and `VARVE0007`'s reservations with it. The rest of 0004
stands: off-the-shelf first, the id scheme and its never-reuse rule, error
severity for architectural rules, and the suppression policy as it applies to
rules that are neither `DD` nor `VARVE`. **Supersedes ADR
[0003](0003-package-layering.md) in part**: its escape for a locally harmless
forbidden reference, "an ADR and a suppression citing it", becomes a
`[DesignDecision]` citing a filed decision, because a `DD` or `VARVE` rule
cannot be suppressed.

## Context

ADR 0004 reserved eight `VARVE` ids and said a reserved id is not a rule until
its analyzer exists. After five milestones, two exist: `VARVE0001` (layer
direction) and `VARVE0002` (layer declaration). The other six have stayed
sentences. Four of those six say nothing about Varve:

- `InternalsVisibleTo` only toward tests (`VARVE0003`);
- no grab-bag names (`VARVE0004`);
- no mutable static state (`VARVE0005`);
- a suppression must cite a decision (`VARVE0008`).

`VARVE0007`, the contract vocabulary, knows Varve only through a list of
assembly names. Only the hot-path rule (`VARVE0006`) is shaped by a Varve fact:
constraint 5's allocation budget per quad.

[`DecisionDriven.Analyzers`](https://github.com/Hafeok/decision-driven-analyzers)
implements the generic rules as a published package, `DD0001` to `DD0019`. It
was written from the same brief and the same principles. Its design is recorded
in its own drafts. The one this ADR adopts is ADR-A02,
[*Generic rules here, product-specific rules in the product's own package*](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/drafts/ADR-A02-two-packages.md),
whose decisions are in
[`docs/decisions/two-packages.md`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/decisions/two-packages.md).
That file governs where the narrative and the decisions differ. It adds what
ADR 0004 never had: code cites a decision as a type the compiler can see, and
a decision that no human has accepted cannot ship under
`TreatWarningsAsErrors`.

Two documents must never claim one id. That makes this a supersession of part
of 0004 rather than an addendum beside it. An addendum would leave 0004's
table still reserving `VARVE0003`–`VARVE0008` while the package enforced the
same rules under other ids.

## Decision

**Two analyzer packages.**

- **`DecisionDriven.Analyzers`** carries the generic rules. Varve references
  it as a published package, development-time only, under the terms of ADR
  [0063](0063-build-time-analyzer-packages.md). Varve adopts its rules by
  configuration and never by changing its code
  (`TwoPackages.ConfigurationViaMsBuildProperties`). ADR
  [0064](0064-varve-configuration-and-hot-path-rules.md) records Varve's
  configuration.
- **`Varve.Analyzers`** keeps only the rules that know a Varve fact, under
  the `VARVE` prefix. Today that is the hot-path pair, renumbered by ADR 0064.

**`VARVE0001` and `VARVE0002` are retired.** `VARVE0001` is replaced by
`DD0001` (a reference within a family points strictly downward). `VARVE0002`
is replaced only in part, and the rest is re-allocated.

- **What `DD0001` covers:** it reports a reference to a family assembly that
  declares no layer.
- **What `DD0001` does not cover:** it is silent on a project that declares no
  `ArchLayer` itself, so a packable project or a host that omits the property
  is checked by nothing. It has no view of ADR
  [0060](0060-hosts-at-layer-6-the-composition-root.md)'s rule that an
  executable is layer 6 and layer 6 is only executables.
- **Where the rest goes:** those checks are Varve's own. They encode 0060, so
  they become **`VARVE0005`**, allocated by ADR
  [0064](0064-varve-configuration-and-hot-path-rules.md).

The retired ids are never reused, as 0004's id scheme requires. Their rule
pages stay as stubs ("retired, replaced by `DD0001`" and "retired, replaced by
`DD0001` and `VARVE0005`"), so a suppression or a link naming them still
resolves to something that says what happened.

**ADR 0004's id reservation table is retired.** Each reserved rule maps to its
successor:

| Reserved | Rule | Now |
|---|---|---|
| `VARVE0001` | Layer direction | **Retired.** `DD0001`. |
| `VARVE0002` | Layer declaration | **Retired.** `DD0001` for an undeclared referenced assembly; `VARVE0005` (ADR 0064) for the rest. |
| `VARVE0003` | `InternalsVisibleTo` only toward tests | Released, never implemented. `DD0002`. |
| `VARVE0004` | No grab-bag names | Released, never implemented. `DD0005`, whose default list contains 0004's five names. |
| `VARVE0005` | No mutable static state, no static registries | Released, never implemented. `DD0004`. |
| `VARVE0006` | Hot-path discipline | Released, never implemented. **Renumbered** by ADR 0064 as `VARVE0003` and `VARVE0004`. |
| `VARVE0007` | Public contracts use `Varve.Rdf`, BCL and algebra types | Released, never implemented. `DD0010` and `DD0011`, configured by ADR 0064. |
| `VARVE0008` | A suppression's justification cites an ADR | Released, never implemented. `DD0008` for `DD` and `VARVE` rules (see below). |

"Released" means the reservation is withdrawn, and the id goes back to
`Varve.Analyzers`' sequence. `VARVE0003`, `VARVE0004` and `VARVE0005` are
reused by ADR 0064, for the hot-path rules and the layer declaration. That is not a reuse in the sense 0004 forbids: no
analyzer ever reported under either id, and no suppression anywhere names
one. The repository was checked. Every mention of `VARVE0003`–`VARVE0008` is
prose describing the reservation, in ADRs, `docs/rules/README.md`,
`CONTRIBUTING.md`, `docs/rules/VARVE0001.md`, `eng/HotPathAttribute.cs` and two
project-file comments. There is no `#pragma` and no `[SuppressMessage]`. The
non-ADR mentions are corrected in session 2 of #43, when the rules they
describe change. The ADRs keep their text, as ADR 0001 requires, and read as
history. `VARVE0001` and `VARVE0002` were implemented and have reported, so
they are retired and not released.

**Suppressions.** For a `DD` or `VARVE` rule there is no suppression: no
`#pragma`, no `[SuppressMessage]`, no `.editorconfig` downgrade. `DD0008`
reports each, with `dd_rule_id_prefixes = VARVE` bringing Varve's own rules
under it. The only exception path is `[DesignDecision(typeof(<Set>.<Key>),
Scope = …)]` on the symbol, citing a filed decision. This is stricter than
`VARVE0008` was, which asked only for a citation in the justification. For
every other rule (IL-prefixed, CA, RS, BannedApi), ADR 0004's suppression
policy stands unchanged: a justification citing an ADR, at the narrowest
scope, never a repo-wide `NoWarn`. It remains a review obligation, as it was
before. `DD0008` does not cover it, and this ADR does not claim it does.

**Decisions are types.** Every Varve ADR is enumerated into `docs/decisions/`
as the interim set files the package reads, in ledger namespace `varve`, one
set per ADR. Each ruling is a key with a one-line statement, and the ADR's own
acceptance is transcribed as `accepted-by` and `accepted-at`. The ADR stays the
narrative, and the set file is what code cites. A ruling that a later ADR
supersedes appears once, in the superseding ADR's set, under the same key.
Supersession is not obsolescence
(`DecisionsAsTypes.SupersessionIsNotObsolescence`). Transcription follows fixed rules:

- Each set file names its ADR (`adr: NNNN`). Its set id is the ADR's title in
  kebab-case, as the ADR's file name already spells it without the number:
  `0004-enforcement-by-analyzers.md` is set `enforcement-by-analyzers`.
- `accepted-by` is the maintainer's identity, `mailto:emil@okkels-klein.dk`.
- `accepted-at` is the ADR's acceptance date at `T00:00:00Z`, because an ADR
  records a date and not a time. A ruling changed by a dated amendment inside
  its own ADR carries the amendment's date instead.
- `revoked-at` is reserved for a ruling withdrawn with no successor. Every use
  is listed in the report of the change that makes it.

Until session 2 of #43 wires the set files into the generator,
`eng/decision-sets.cs` checks their front matter in the build job.

## Alternatives considered

- **Implement the six reserved rules in `Varve.Analyzers`.** The route 0004
  set out. Rejected: it writes, tests and maintains six analyzers that already
  exist, maintained, with tests, in a package built for exactly this. It also
  leaves them unusable by any other product line built to the same brief, and
  that duplication is what ADR-A02 exists to prevent.
- **Reference the package and keep `VARVE0001`/`VARVE0002` beside `DD0001`.**
  Two rules enforcing one decision, with two ids for one finding. Rejected:
  every layer violation would report twice, and the pair would drift.
- **Keep 0004's table and add a column mapping each id to its `DD` successor.**
  Rejected: it is the "two documents claim one id" failure written into one
  document. A reserved id that nothing will ever implement is not a
  reservation.
- **Vendor the analyzer source into `src/`.** No package dependency, and full
  control. Rejected: it is copied code, which constraint 6 forbids in spirit
  even though it is our own maintainer's. It also forks the rules on the first
  local edit. A package with a version is the boundary that keeps them one set
  of rules.
- **Map `VARVE0008` onto `DD0008` wholesale.** Rejected, because `DD0008` does
  not cover it. `DD0008` forbids suppressing `DD` and product rules, while
  `VARVE0008` was about every suppression, including the IL rules that must be
  suppressible with a citation. Claiming full coverage would leave the IL
  suppressions with no rule and no record that they have none.

## Consequences

- **`Varve.Analyzers` shrinks** to the hot-path pair. `LayerDeclaration`,
  `LayerDirectionAnalyzer` and their tests leave in session 2 of #43. The layer
  number moves from `VarveLayer` to the package's `ArchLayer`, one property,
  read everywhere the old one was.
- **`docs/rules/README.md` and `CONTRIBUTING.md`** point at the analyzer
  repository's `docs/rules/` for `DD` rules and at `docs/rules/` for `VARVE`
  rules (session 3 of #43).
- **The package is prerelease.** Its rules can change between previews. ADR
  0063 fixes the version policy, and a rule change arrives as a breaking entry
  in its changelog, not silently.
- **Code will cite decisions that nobody has accepted yet.** Sessions 2 and 3
  file new decisions where a finding has none. Those are unaccepted, and
  citing one is `CS0618`. ADR [0066](0066-expected-red-pull-requests.md) decides
  what a pull request carrying such a citation is.
- **The whole-graph checks 0004 left open** ("worth revisiting for the
  whole-graph checks an analyzer genuinely cannot do") become
  `DecisionDriven.Report`, as a non-gating CI job (session 3 of #43).

## Checks

- **Checked against the accepted ADRs** (0001, 0003–0005, 0007–0018,
  0021–0061). Touches:
  - **0001**: every ADR also becomes a set file, and the ADR remains the
    narrative.
  - **0003**: superseded in part (the escape clause), and `DD0001` enforces
    its downward rule under ADR 0060's table,
    unchanged in substance.
  - **0004**: superseded in part, above.
  - **0009**: the new packages, by ADR 0063.
  - **0026**: the hot-path mechanism, by ADR 0064.
  - **0060**: `ArchCompositionRoot` is set on exactly the layer-6 executables,
    by ADR 0064.

  No conflict with the specification: nothing here touches `Varve.Store`
  behaviour.
- **Layer ownership.** None. Both analyzer packages are development-time only
  and have no layer.
- **Analyzer rule.** Retires `VARVE0001` and `VARVE0002`, and releases
  `VARVE0003`–`VARVE0008`. Allocates nothing; ADR 0064 does. `VARVE0002`'s
  uncovered half is not dropped: it becomes `VARVE0005`.
- **Open questions owned.** None. ADR 0003's open question 1 is untouched.
