# 0148 — `Varve.Shacl` at layer 3, `Varve.Shacl.Sparql` at layer 4

## Status

**Accepted — filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13), 2026-10-17** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 1 plan. **Closes ADR 0003's
open question 1**, by a dated amendment in that file.

## Context

ADR 0003 put `Varve.Shacl` and the SPARQL evaluator at layer 3, and the brief
says SHACL-SPARQL depends on the evaluator. A same-layer reference is a
violation, so the two statements could not both hold. 0003 recorded the
question and named a candidate resolution: SHACL Core alone at layer 3, and a
package one layer up for SHACL-SPARQL. Milestone 8 was its due date.

The brief also says the validator is "usable standalone over any quad
source", and ADR 0005 gives the reason that weighs here: a caller who wants
the log should not ship a query engine. The same holds one layer down. A
caller who wants SHACL Core should not ship the SPARQL parser, the algebra,
the optimiser and the evaluator, and in the browser that is a download.

## Decision

1. **Two packages, one layer apart.**

   | Package | Layer | References | Holds |
   |---|---:|---|---|
   | `Varve.Shacl` | 3 | `Varve.Rdf`, `Varve.Iri`, `Varve.Xsd` | SHACL Core: the shapes model (internal, ADR 0155), targets, paths (ADR 0152), the core constraint components, recursion (ADR 0150), the report (ADR 0154), and the extension contract below |
   | `Varve.Shacl.Sparql` | 4 | `Varve.Shacl`, `Varve.Sparql.Evaluation`, `Varve.Sparql`, `Varve.Rdf` | SHACL-SPARQL: SPARQL-based constraints and SPARQL-based constraint components, as an implementation of the extension contract |

   `Varve.Shacl.Sparql` does not reference `Varve.Store`. Layer 4 also holds
   `Varve.Store` and `Varve.Events`, and it references neither, so there is no
   same-layer reference.
2. **The extension contract lives in `Varve.Shacl`.** It is the lowest layer
   that can define it without knowing its implementers (ADR 0003). It is
   expressed over `Varve.Rdf` types only:
   - **`IShaclExtension`** is offered every shape of a shapes graph once, at
     load. It reads what it needs from the shapes source itself — for
     SHACL-SPARQL, `sh:sparql`, `sh:prefixes`, a component's parameters and
     validators, and the shape's `sh:path` — and adds constraints to the shape,
     or refuses the shapes graph with problems.
   - **`IShaclConstraint`** is evaluated per focus node with the data source,
     the data graph, the focus node and its value nodes as handles of the data
     source, and writes results or a failure.
   - No type of the shapes model, of the parser or of the evaluator crosses
     it, so it does not change when the model becomes public (ADR 0155).
3. **Composition is by the caller.** An extension is an option of a
   validation (`ShaclOptions.Extensions`). There is no static registry and no
   service location: a caller who wants SHACL-SPARQL passes
   `SparqlConstraints.Extension(...)` from `Varve.Shacl.Sparql`, and one who
   does not never loads it. A shapes graph that uses `sh:sparql` or declares a
   SPARQL-based component without the extension is refused with a problem that
   names the shape and the extension it needs; it is never validated as if the
   constraint were absent.
4. **Part 2's store integration is `Varve.Shacl.Store`, layer 5**, the
   pattern of `Varve.Sparql.Store`: it implements `Varve.Store`'s pre-commit
   validator contract (ADR 0017) over the overlay, with shapes loaded once
   (ADR 0155). Nothing in this part depends on it.

## Alternatives considered

- **One package at layer 4**, `Varve.Shacl` referencing the evaluator.
  One package, one install, and no contract to keep. Rejected: every caller of
  SHACL Core would link the SPARQL parser, algebra, optimiser and evaluator,
  and pay for them in binary size, trimming surface and AOT compilation time —
  ADR 0005's argument, which the brief applies to the store and which applies
  as much to a validator the brief wants usable in the browser. It would also
  put `Varve.Shacl` beside `Varve.Store` at layer 4, where a Core validator
  has nothing to be.
- **`Varve.Shacl` at layer 4 and the evaluator unchanged**, with SHACL Core
  and SPARQL in one package but a feature switch. A switch does not remove a
  reference; the trimmer may, but only where nothing reaches it, and the
  extension's whole point is that nothing does.
- **A contract expressed in SPARQL terms** — "given a query, yield bindings",
  the form 0003 sketched. Rejected: the contract would carry an algebra or a
  query string through layer 3, which is ADR 0005's rejected "pluggable query
  engine" move one layer down. The contract here knows constraints, not
  queries.
- **The evaluator moves down to layer 2.** Breaks ADR 0048's placement and
  every reason for it.

## Consequences

- SHACL is two packages, and a user who wants SHACL-SPARQL installs both. The
  README's package table and `AGENTS.md`'s layer table gain them.
- The extension contract is public API of `Varve.Shacl`, and part of its
  baseline. It is the one place SHACL Core learns that something else may
  validate.
- A SPARQL constraint pays for the evaluator only where a shapes graph uses
  one.

## Checks

- **Checked against the accepted ADRs** (0001–0133, 0143–0147). Touches
  **0003** (open question 1 closed, amended), **0005** (the same pattern, one
  layer down), **0017** and **0058** (part 2's store integration composes
  above both packages; nothing they promise changes), and **0048** (the
  evaluator is consumed, not moved). No conflict.
- **Layer ownership.** `Varve.Shacl`, layer 3; `Varve.Shacl.Sparql`, layer
  4; `Varve.Shacl.Store`, layer 5, in part 2.
- **Analyzer rule.** None new. `DD0001` enforces the direction and
  `VARVE0005` the declaration. `Varve.Shacl.Sparql` names `Varve.Shacl` among
  its contract type assemblies, as `Varve.Sparql.Evaluation` names
  `Varve.Sparql`.
- **Open questions owned.** None.
