# 0063 — Build-time packages for the analyzers, and what `BannedSymbols.txt` must cite

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the adoption plan
(issue [#43](https://github.com/Hafeok/Varve/issues/43)). **Amends ADR
[0009](0009-dependency-policy-and-register.md)**. The policy, the register and
the gate are unchanged. This ADR admits two build-time packages, states the
version policy for a prerelease one, and makes the citation rule for banned
symbols explicit. It is the Varve form of the analyzer repository's draft
[ADR-A03](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/drafts/ADR-A03-build-time-dependencies.md).

## Context

ADR [0062](0062-adopting-decisiondriven-analyzers.md) adopts
`DecisionDriven.Analyzers`. Constraint 4 requires every package to name the
decision that admits it, and ADR 0009 makes that a gate. Two packages are new:

- **`DecisionDriven.Analyzers`**, the analyzers, the source generator that
  turns `docs/decisions/` into types, and the code fixes;
- **`DecisionDriven.Report`**, the whole-graph report, a .NET tool run in CI.

Everything else A03 lists is already admitted:

- `Microsoft.CodeAnalysis.CSharp` at the 5.0.0 floor, the testing harness,
  and `Microsoft.CodeAnalysis.Analyzers` (ADR 0009, the Roslyn floor decided
  at the milestone 3a close-out);
- PublicApiAnalyzers and BannedApiAnalyzers (ADR 0004);
- the SDK's trimming, AOT and single-file analyzers (ADR 0004, through
  `IsAotCompatible` and the three `Enable*Analyzer` properties in
  `Directory.Build.targets`).

The analyzer repository pins the same Roslyn floor, 5.0.0, and enforces it
with `DDBUILD0001`, so the two floors agree today.

Both new packages are **prerelease**: `0.1.0-preview.3` is current, and the
package's own README says its public surface "may change without ceremony
until `1.0`". ADR 0009's version rule does not fit such a package.

- The rule is: latest stable, and a patch or minor bump needs nothing.
- There is no stable version to take.
- Under semantic versioning a `0.x` minor bump may break, and the analyzer's
  changelog records every added rule and raised severity as breaking for a
  consumer that builds with warnings as errors.
- `eng/dependency-register.cs` reads only the major, so it waves every `0.x`
  bump through.

## Decision

**Admitted, build-time class, both `PrivateAssets="all"`:**

| Package | Used by | Purpose |
|---|---|---|
| `DecisionDriven.Analyzers` | every project in the Varve family, through `Directory.Build.props` | The `DD` rules, the decision-type generator, the attributes code cites decisions with. Never in a published package's dependencies (`TwoPackages.DevelopmentTimeOnly`). |
| `DecisionDriven.Report` | CI only, as a .NET tool | The whole-graph report. It reads built assemblies and never gates (ADR 0062). |

Their register entries in `Directory.Packages.props` cite **this ADR**
(`Adr="0063"`). They land when the packages are first referenced, in session 2
of #43, not before. A register entry for a package nothing references would
be a claim with nothing behind it.

**Version policy for the two prerelease packages: the latest preview, until
the analyzer repository ships `1.0`.**

- A preview is taken from nuget.org when it is published, never remembered
  (ADR 0009).
- The commit that bumps it carries the changelog's breaking entries for that
  preview in its body. Any finding a new or raised rule produces is fixed in
  the same change or filed as a decision. It is never suppressed (ADR 0062).
- `dependency-register.cs` will not flag these bumps, because it reads the
  major. That is a known gap, stated here rather than closed by special-casing
  two package names in the gate. The review obligation is the changelog in the
  commit body.
- At `1.0` the packages fall under ADR 0009's ordinary rule. Moving to it
  needs a dated amendment here, because the pin moves from a prerelease to a
  stable line.

**The Roslyn floor is unchanged.** `Microsoft.CodeAnalysis.CSharp` stays at
5.0.0 (ADR 0009). If a later preview raises the floor it declares, the analyzer
fails to load in any host below it, and that bump raises Varve's floor. That
needs a reason, as ADR 0009 requires. A preview that raises the floor is
therefore not taken by the ordinary bump; it waits for a dated amendment to ADR
0009.

**Every `BannedSymbols*.txt` entry cites an ADR** in its comment, as the
file's header already says, and every message ends with `ADR NNNN.` naming the
decision that bans it. That was a convention; it is now a decision. The banned
list is a decision surface like the register, and an entry nobody can trace
is a ban nobody can argue with. ADR 0064's additions follow it. It becomes a
gate when session 2 of #43 touches the file, as a check in `eng/` beside the
dependency register. A rule only in a document is not a rule (ADR 0004).

**Off-the-shelf before a `DD` rule, and a `DD` rule before a `VARVE` rule.**
Where PublicApiAnalyzers, BannedApiAnalyzers or the SDK analyzers express a
rule exactly, they are used. Where a `DD` rule does, it is configured. A
`VARVE` rule is written only for what neither can express.

## Alternatives considered

- **Pin one preview and stay on it until 1.0.** Stable ground, no churn.
  Rejected: the package is young and its fixes are for false positives and
  missing exemptions, which Varve is likely to be the first to find. Staying on
  an old preview means working around a fixed defect, and ADR 0062 forbids
  local workarounds.
- **A floating `0.1.0-*` version**, as the analyzer README's quick start shows.
  Rejected: ADR 0009 forbids a version the build does not state. A range
  resolves differently on two machines, and the dependency gate already treats
  it as unparseable.
- **Teach `dependency-register.cs` that a `0.x` minor or prerelease bump is
  major.** Correct under semantic versioning, and it would make every preview
  bump need an ADR entry. Rejected for now. Two packages do not justify a
  second classification rule in a gate, and an ADR amendment per preview is
  bookkeeping, which ADR 0009 rejected for the same reason. Revisit if a third
  `0.x` package is admitted.
- **Treat `DecisionDriven.Report` as test-only.** It runs only in CI, never in
  a compilation. Rejected: it is tooling, not a test framework, and the test
  class is the one that "may be temporary". The report is meant to stay.

## Consequences

- Two new register lines in session 2 and 3 of #43, each citing 0063.
- A preview bump is a change someone reads, not maintenance, until 1.0. That is
  the cost of adopting a prerelease package, and it is visible in each bump
  commit.
- The `BannedSymbols` citation rule gets a gate in session 2. Until then it
  is a review obligation, as it has been since ADR 0004.
- If the analyzer repository raises its Roslyn floor, Varve is told by the
  changelog. The alternative is a rule that silently fails to load, which ADR
  0009 already names as the worst outcome.

## Checks

- **Checked against the accepted ADRs** (0001, 0003–0005, 0007–0018,
  0021–0062). Touches:
  - **0004**: off-the-shelf first, unchanged.
  - **0009**: amended as above.
  - **0027**: build-time packages never reach a published artifact; the native
    asset gate still applies, and neither package ships a native asset.
  - **0029** and **0035**: Varve's own versioning is unaffected, because
    neither package appears in Varve's published dependencies.
- **Layer ownership.** None; build-time only.
- **Analyzer rule.** None new. It names a gate for the banned-symbols
  citation, due in session 2.
- **Open questions owned.** None.
