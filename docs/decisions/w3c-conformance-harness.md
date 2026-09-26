---
set: w3c-conformance-harness
namespace: varve
adr: 0007
decisions:
  - key: W3cSuitesPinnedSubmodule
    statement: "W3C test data is a pinned git submodule, advanced only by a commit that says so, and never vendored or fetched at test time"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: SuiteBytesNotNormalised
    statement: ".gitattributes excludes the test-suite submodules from line-ending normalisation, because their bytes are what is tested"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: OneCasePerManifestEntry
    statement: "Each manifest entry is one test case named by its test IRI, and suites are discovered from a table"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: ManifestsReadByVarveTurtle
    statement: "The conformance harness reads manifests with Varve.Turtle and references no other RDF implementation"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: HarnessOwnsSubjectAbstraction
    statement: "The harness defines its own subject abstraction on the test side, which is not a design for the parser API"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: MissingSubmoduleFailsLoudly
    statement: "A guard test fails when a suite submodule is missing, so zero enumerated cases never pass silently"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: ConformanceRatchet
    statement: "baseline/passing.txt lists the passing test IRIs, and eng/ratchet.cs fails on a regression or a vanished entry and never on an improvement"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: RatchetGatesConformance
    statement: "CI runs the conformance suite without gating on its exit code and gates on the ratchet instead"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: EngScriptsAreFileBasedApps
    statement: "eng/ scripts are C# file-based apps run with dotnet run, one implementation for every operating system"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
---

The rulings of [ADR 0007](../adr/0007-w3c-conformance-harness.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

`ManifestsReadByVarveTurtle` is the exit criterion's outcome, recorded by the undated
"Met at milestone 3b" note inside the ADR; it carries 2026-09-22, the date of the milestone
3b record, and replaces the temporary dotNetRdf.Core reader the ADR first decided.
