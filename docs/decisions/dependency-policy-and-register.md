---
set: dependency-policy-and-register
namespace: varve
adr: 0009
decisions:
  - key: PreferBcl
    statement: "A package enters only when the BCL does not do the job"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: NoNativeAssetInShippedClosure
    statement: "No package reaching a published Varve artifact contributes a native asset; build-time and test-only packages are exempt, and eng/native-assets.cs enforces it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: ThreeDependencyClasses
    statement: "Dependencies are runtime, build-time or test-only, admitted on different bars, and a test-only stopgap states its exit criterion"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: PublishedDependencyListEmpty
    statement: "A published Varve package's dependency list is empty unless an ADR says otherwise"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: VersionsCentralAndResolved
    statement: "Every version lives in Directory.Packages.props, resolved from nuget.org when added or changed, and the default is the latest stable"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: RoslynFloor
    statement: "Microsoft.CodeAnalysis.CSharp and its Workspaces package are pinned to the 5.0.0 floor, the Roslyn of the first .NET 10 SDK, and a floor is raised only with a reason"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: RegisterCitesAdr
    statement: "Every PackageVersion carries Adr naming the decision that admits it, transitive pins included, and eng/dependency-register.cs fails on a missing or dangling citation"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: MajorBumpNeedsAdrChange
    statement: "A patch or minor bump of a registered package needs nothing, and a major bump or a new package needs its cited ADR changed in the same diff"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0009](../adr/0009-dependency-policy-and-register.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

`NoNativeAssetInShippedClosure` carries 2026-09-22, the date its 2026-09-21 narrowing was
ratified by the repository owner and enforced; `MajorBumpNeedsAdrChange` carries its
amendment's date. ADR 0006, which 0009 superseded whole, has no set file: see the session 1
report of #43 for its rulings that no set carries.
