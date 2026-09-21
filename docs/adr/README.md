# Architecture decisions

Format, numbering and the supersession rule are in
[0001](0001-record-architecture-decisions.md). An accepted ADR is not edited; a
change of mind is a new ADR whose Status names the one it supersedes.

| # | Title | Status |
|---:|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-licence.md) | Licence: Apache-2.0 | Accepted |
| [0003](0003-package-layering.md) | Package layering and the strictly downward reference rule | Accepted |
| [0004](0004-enforcement-by-analyzers.md) | Enforcement by analyzers | Accepted |
| [0005](0005-store-is-sparql-free.md) | `Varve.Store` is SPARQL-free | Accepted |
| [0006](0006-build-and-test-dependencies.md) | Build-time and test-time dependencies | Accepted |
| [0007](0007-w3c-conformance-harness.md) | W3C conformance harness | Accepted |
| [0008](0008-target-framework-and-language.md) | Target framework and language version policy | Accepted |

## Open questions recorded, not resolved

- **ADR 0003, open question 1** — `Varve.Shacl` and the SPARQL evaluator are
  both placed in layer 3, yet SHACL-SPARQL depends on the evaluator. Under the
  strict rule that reference is illegal. Due by milestone 8.
- **ADR 0003, open question 2** — the SPARQL optimiser and evaluator are both
  in layer 3. If the evaluator consumes a plan type the optimiser owns, they are
  one package or two layers. Due by milestone 5.

## Closed

- **ADR 0002** — the copyright holder. Closed 2026-09-21: Emil Okkels Klein,
  named in `NOTICE`. The `LICENSE` appendix stays unedited, because it is the
  per-file boilerplate template and not a record of ownership.

## Obligations recorded against a later milestone

- **ADR 0004** — the `System.Uri` ban is wider than the brief scopes it. Narrow
  before layer 5 exists.
- **ADR 0006 / 0007** — `dotNetRdf.Core` is a temporary test-only dependency.
  Remove when `Varve.Turtle` passes `rdf/rdf11/rdf-turtle`, at milestone 5.
