---
set: target-framework-and-language
namespace: varve
adr: 0008
decisions:
  - key: TargetCurrentLts
    statement: "Varve packages target net10.0, the current LTS, set once in Directory.Build.props"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: SdkPinnedLatestFeature
    statement: "global.json pins the SDK to 10.0.401 with rollForward latestFeature"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: LangVersionLatest
    statement: "LangVersion is latest rather than a pinned number, deterministic because the SDK is pinned"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: AnalyzersTargetNetStandard20
    statement: "Varve.Analyzers targets netstandard2.0, the one structural exception, because it must load in the compiler and in Visual Studio"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: NullableEnabled
    statement: "Nullable reference types are enabled repository-wide"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: ImplicitUsingsDisabled
    statement: "ImplicitUsings is disabled, so a file's dependencies are visible in the file"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: WarningsAreErrors
    statement: "TreatWarningsAsErrors, AnalysisLevel latest-recommended and EnforceCodeStyleInBuild are on repository-wide"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: DocumentationFileGenerated
    statement: "GenerateDocumentationFile is on for every project"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: DeterministicBuilds
    statement: "Builds are deterministic, with ContinuousIntegrationBuild under CI and EmbedUntrackedSources, so two builds of one commit produce the same bytes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: MoveToNextLtsOnly
    statement: "Varve moves to the next LTS during its release window once the AOT and WASM smoke builds pass on it, and never targets an STS release"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: NoMultiTargeting
    statement: "The packages are not multi-targeted without an ADR of their own"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
---

The rulings of [ADR 0008](../adr/0008-target-framework-and-language.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
