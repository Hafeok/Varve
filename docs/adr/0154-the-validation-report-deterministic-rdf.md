# 0154 — The validation report is deterministic RDF, and keeps the data's blank nodes

## Status

**Accepted — filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13), 2026-10-17** (ADR 0066). Decided by the maintainer on the part 1 plan,
with a condition added at approval: a blank node of the data keeps the label
its source gives it, and only the report's own nodes are canonicalised.

## Context

SHACL's result is a validation report, an RDF graph (SHACL 1.2 Core §6.8).
Part 2 will maintain the current report as a projection that can be read as
of any position, and attach a report to a commit as metadata (ADR 0017). Both
need the same inputs to give the same bytes, or every rebuild of the
projection would look like a change and every replay a different verdict.

A report names the data's own nodes — focus nodes and values — and many of
them are blank nodes. A blank node of the data has an identity only in its
source: a store's blank node is its handle, externalised under a label derived
from it (ADR 0044). If the report relabelled those nodes, it could not be
joined back to the data, and an as-of report graph would rename the same data
node from one position to the next.

## Decision

1. **The report is a value**, `ValidationReport`, computed once per
   validation and immutable. It is readable in three forms, none of which
   requires the others:
   - **`Conforms` and `Summary`** — the result count by severity (`sh:Violation`,
     `sh:Warning`, `sh:Info`, `sh:Trace`, `sh:Debug`, and others) — read
     without building any RDF;
   - **`Results`**, the results as typed values over `RdfTerm`s, in the order
     of point 3;
   - **`ToDataset()`**, the report graph as an `InMemoryDataset`, and
     **`ToCanonicalNQuads()`**, its bytes.
2. **Blank nodes, in two kinds.**
   - **A node of the data** — a focus node, a value, a member of a triple term
     — keeps the label its data source gives it through `TryExternalise`. The
     report never renames it.
   - **A node of the report** — the report node, each result node, the blank
     nodes of a `sh:resultPath` structure (one copy per result, §6.8.2.2) and
     a shape's or constraint's blank node from a shapes graph that is not
     the data — is labelled by
     **RDFC-1.0** (ADR 0059) over the report graph with every data blank node
     held fixed: each is replaced, for the canonicalisation only, by an IRI
     no data or report can contain, the report is canonicalised, and the
     replacement is undone. The issued labels (`c14n0`, …) are given a prefix
     disjoint from every data label the report contains, chosen
     deterministically from those labels, so the two kinds never collide.
   - The work limit is RDFC-1.0's, from `ShaclOptions.Canonicalisation`.
     A report that exceeds the limit is a
     `CanonicalisationLimitException` from `ToCanonicalNQuads()`, and
     `Conforms`, `Summary` and `Results` are unaffected.
3. **The results are in a stated order, and equal results are kept.** SHACL
   1.2 Core §7 says a validator "always produce[s] new result nodes", and the
   suite holds it to that: two results with the same fields — a pair
   component over two equal comparisons, a nested property shape reached from
   two parents — are two result nodes. `Results` is ordered by focus node,
   path, value, source shape, source constraint, source constraint component,
   severity and messages, each compared by its canonical N-Triples form
   (RDF 1.2, ADR 0061) in code point order, a path by its SPARQL form, and an
   absent field first; equal results are adjacent and interchangeable. The
   order depends only on the shapes graph, the data and the data source's
   labels. Equal results do not make RDFC-1.0 expensive: each needs one Hash
   N-Degree Quads call, related only to the report node, whose first-degree
   hash is unique, and the work limit scales with the blank nodes that need
   such calls (ADR 0059).
4. **What the report graph holds**: `sh:ValidationReport`, `sh:conforms`, and
   per result `sh:focusNode`, `sh:resultPath` (a path written back as SHACL
   path syntax, ADR 0152), `sh:value`, `sh:sourceShape`,
   `sh:sourceConstraint` (for SPARQL-based constraints),
   `sh:sourceConstraintComponent`, `sh:resultSeverity` and `sh:resultMessage`;
   `sh:conformanceDisallows` when it is not the default; and
   `varve:importNotFollowed` (ADR 0151). When the shapes graph is the data
   (the same source), a shape's blank node is a data node and keeps its label.
5. **The property.** A test (`Varve.Shacl.Tests`) generates shapes and data,
   validates twice — over two sources holding the same quads with the same
   blank node labels, and with the shapes and data given in shuffled order —
   and checks the canonical bytes are equal. A second test validates data
   whose two focus nodes are blank nodes and checks that the report, joined to
   the data by label, names the right ones.

## Alternatives considered

- **RDFC-1.0 over the whole report.** One canonicalisation, the form the plan
  first named. Rejected by the maintainer: it relabels the data's blank nodes,
  so the report cannot be joined back to the data, and part 2's as-of report
  would rename data nodes between positions.
- **Skolemise data blank nodes in the report.** Joinable, but a skolem IRI
  scheme is the protocol half of ADR 0044's Q1, decided with the server, and
  a report is not a protocol.
- **Report nodes labelled by a hash of their content**, without RDFC-1.0. Also
  deterministic. Rejected: RDFC-1.0 is already the repository's canonical
  form, with its own suite, and a second scheme would be a second thing to
  trust.
- **Insertion order.** What shacl-rs does. Deterministic only for one
  implementation of one traversal, which part 2 would freeze.

## Consequences

- `ToCanonicalNQuads()` costs a canonicalisation, paid only by a caller who
  wants bytes; part 2's projection is that caller.
- The order of results is part of the contract and changes only by an
  amendment of this ADR.

## Checks

- **Checked against the accepted ADRs** (0001–0133, 0143–0147). Touches
  **0044** (data blank nodes keep their handle-derived label), **0059**
  (RDFC-1.0 and its work limit), **0061** (canonical N-Triples is RDF 1.2's),
  and **0017** (the report is the attachment a validator may return). No
  conflict.
- **Layer ownership.** `Varve.Shacl`, layer 3.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-20 — a report restricted to affected pairs

Filed by SHACL, part 2a (#138, under milestone 8, #13), unaccepted until the
maintainer accepts it (ADR 0066).

A report may be **restricted** to some of a validation's (shape, focus node)
pairs: its results are those pairs' results, a subset of the whole report's in
the same order of point 3, and `sh:conforms` is computed over them. Its bytes
follow point 2 unchanged. Commit-time gating attaches the SHA-256 of a
restricted report's canonical N-Quads (ADR
[0173](0173-the-validation-report-on-the-commit.md)), and the projection
(part 2b) restricts its report the same way to check it.

**The ledger.** The ruling is ADR 0173's `AffectedReportAttachment`.
