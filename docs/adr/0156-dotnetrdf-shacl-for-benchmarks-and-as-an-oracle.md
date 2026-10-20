# 0156 — `dotNetRdf.Shacl` for the benchmarks and as a differential oracle

## Status

**Accepted — filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13), 2026-10-17** (ADR 0066). Decided by the maintainer on the part 1 plan.

## Context

The brief names dotNetRDF as a comparison, and ADR 0027 admits
`dotNetRdf.Core` to `tests/Varve.Benchmarks` as the baseline Varve is
measured against. dotNetRDF's SHACL validator ships separately, as
`dotNetRdf.Shacl`. Constraint 6 of the brief forbids copying its code; it
does not forbid running it beside ours.

## Decision

1. **`dotNetRdf.Shacl` 3.5.2**, resolved from nuget.org into
   `Directory.Packages.props` with `Adr="0156"`. Licence MIT. It depends on
   `dotNetRdf.Core` 3.5.2, already registered under ADR 0027, and adds no
   native asset.
2. **Two references, both outside `src/`:**
   - `tests/Varve.Benchmarks`, for the SHACL benchmark: the same generated
     dataset and shapes, validated by both, on the same hardware.
   - `tests/Varve.Shacl.Sparql.Tests`, as a **differential oracle**: generated
     shapes and data that use only SHACL Core components both implement are
     validated by both, and `sh:conforms` and the set of (focus node, source
     shape, component) triples are compared. A disagreement is investigated
     against the specification and reported, never resolved by matching
     dotNetRDF.
3. **Never `src/`.** The conformance harness's guard
   (`The_harness_does_not_reference_another_rdf_implementation`) stays as it
   is: the W3C suite is judged against its own expected reports, not against
   another implementation.

## Alternatives considered

- **No oracle.** The suite and the properties are the gate. Rejected by the
  maintainer: a second implementation catches disagreements no expected
  report covers.
- **pySHACL or TopBraid as the oracle.** A process boundary, a second
  runtime in CI, and test data crossing it. dotNetRDF runs in the test
  process.

## Consequences

- The dependency register gains one test-only package.
- Benchmark results are recorded in `tests/Varve.Benchmarks/README.md` and the
  pull request, slower rows first, as ADR 0027 requires of every comparison.

## Checks

- **Checked against the accepted ADRs** (0001–0133, 0143–0147). Touches
  **0009** (the register), **0027** (benchmarks and dotNetRDF, amended for the
  second package) and **0007** (the harness guard, unchanged). No conflict.
- **Layer ownership.** Test projects only, which declare no layer, and the
  benchmark host at layer 6.
- **Analyzer rule.** None.
- **Open questions owned.** None.
