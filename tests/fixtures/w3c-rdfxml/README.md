# N-Triples translations of the SPARQL suites' RDF/XML

The W3C SPARQL query evaluation suites read a few files as RDF/XML: the
expected results of `sparql10/sort` and the data of `sparql11/subquery`.
Varve has no RDF/XML parser until `Varve.RdfXml` (roadmap slice 6b), so the
conformance harness reads these translations instead.

- **Generated once, by a third party's parser**: dotNetRDF's `RdfXmlParser`,
  through the benchmark project's
  `dotnet run --project tests/Varve.Benchmarks -c Release -- --convert-rdfxml <repository root>`.
  dotNetRDF is in the register for benchmarking (ADR 0027); this is its one
  offline use besides, recorded in that ADR's dated note of 2026-09-25.
  Nothing reads dotNetRDF at test time.
- **Guarded**: beside each `X.rdf.nt` is `X.rdf.nt.sha256`, the SHA-256 of the
  original it was made from. The harness refuses a translation whose original
  has changed — after a submodule bump — so a stale fixture fails a case
  rather than passing it on old data.
- **Deleted when `Varve.RdfXml` passes its own suite**; the harness then reads
  the originals.

Blank node labels are dotNetRDF's; the comparisons are up to a blank node
bijection, so they carry no meaning.
