---
set: benchmarking
namespace: varve
adr: 0027
decisions:
  - key: BenchmarkDotNetConfined
    statement: "BenchmarkDotNet is admitted for a non-packable benchmark project only, where its native and Reflection.Emit dependencies reach no published artifact"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: BenchmarksNeverGate
    statement: "Benchmarks are never a gate and are not run in CI, and a number is reported with the machine that produced it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: BenchmarkDataReproducible
    statement: "A benchmark dataset is generated from a stated seed and generator, never a downloaded corpus"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: DotNetRdfIsTheBaseline
    statement: "dotNetRdf.Core is in the register as the benchmark baseline, and nothing in the repository depends on it for an answer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: RdfXmlFixturesTranslatedOffline
    statement: "The SPARQL suites' RDF/XML files are translated once, offline, by dotNetRDF into committed hash-guarded N-Triples, deleted when Varve.RdfXml passes its own suite"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0027](../adr/0027-benchmarking.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

The native-asset scope this ADR decided is ADR 0009's `NoNativeAssetInShippedClosure`, recorded
there as the amendment it made. The benchmark project's layer is ADR 0064's
`UnlayeredAssemblies` and ADR 0060's table.
