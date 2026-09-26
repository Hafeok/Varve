---
set: enforcement-by-analyzers
namespace: varve
adr: 0004
decisions:
  - key: OffTheShelfAnalyzersFirst
    statement: "The SDK trimming, AOT and single-file analyzers, PublicApiAnalyzers and BannedApiAnalyzers are used wherever they express a rule, rather than a rule of our own"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: IlDiagnosticsAreErrors
    statement: "The IL-prefixed trimming and AOT diagnostics are error severity in .editorconfig and never enter NoWarn"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: PublicApiBaselinePerPackage
    statement: "Every packable project tracks its public API in PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt, so a new public member is a reviewable line"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: BannedSymbolsFile
    statement: "Banned symbols are declared in eng/BannedSymbols.txt and enforced by BannedApiAnalyzers"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: VarveRuleIdScheme
    statement: "Varve rule ids are VARVE and four digits, allocated in order and never reused; a retired id stays retired"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: RulePagePerRule
    statement: "Each VARVE rule has a page at docs/rules/VARVEnnnn.md that its HelpLinkUri points to and that links to the motivating ADR"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: AnalyzerReleaseTracking
    statement: "Rules are tracked in AnalyzerReleases.Shipped.md and AnalyzerReleases.Unshipped.md, as RS2008 enforces"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: ArchitecturalRulesAreErrors
    statement: "A rule that protects the package graph or a constraint of the brief is error severity, not warning"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: SuppressionCitesAdr
    statement: "A suppression of a rule that is neither DD nor VARVE carries a justification citing an ADR at the narrowest scope, and a repo-wide NoWarn for an IL rule is never allowed"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: AnalyzerNeverRuntimeDependency
    statement: "Varve.Analyzers is referenced as an analyzer, never as a library, and never appears in a published dependency list"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: RuleDeliverables
    statement: "A new rule is delivered as the analyzer, its tests, its release-tracking entry and its rule page"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: UriBanNarrowedPerProject
    statement: "The repository-wide System.Uri ban is narrowed before layer 5 needs System.Uri, by a per-project banned-symbols file and not by call-site suppressions"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
---

The rulings of [ADR 0004](../adr/0004-enforcement-by-analyzers.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Moved to later sets: the id reservation table to ADR 0062's (`VarveIdReservations`); the
contract vocabulary reserved as VARVE0007, with its 2026-09-25 amendment, to ADR 0064's
(`ContractTypeVocabulary`); the hot-path rule reserved as VARVE0006 to ADR 0064's
(`HotPathDiscipline`); the 2026-09-21 amendment's packable-assembly check to ADR 0064's
(`PackableAssemblyDeclaresLayer`). `SuppressionCitesAdr` is the part of the suppression
policy still in force; the part ADR 0062 superseded is its `NoSuppressionOfDdOrVarveRules`.
