---
set: dotnetrdf-shacl-for-benchmarks-and-as-an-oracle
namespace: varve
adr: 0156
decisions:
  - key: DotNetRdfShaclTestOnly
    statement: "dotNetRdf.Shacl is referenced by the benchmarks and by one test project as a differential oracle, never by src"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-18T09:57:00Z
---

The rulings of [ADR 0156](../adr/0156-dotnetrdf-shacl-for-benchmarks-and-as-an-oracle.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
