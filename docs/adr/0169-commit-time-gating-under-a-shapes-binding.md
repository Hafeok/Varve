# 0169 — `Varve.Shacl.Store`: commit-time gating under a shapes binding

## Status

**Accepted — filed unaccepted by SHACL, part 2a (#138, under milestone 8,
#13), 2026-10-20** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 2 plan (#13, 2026-10-20), with
two changes made at approval: `reject` refuses *new* violations, not existing
ones, and a refused function list that includes `BNODE()`. Refines **0017**
and **0058** (who validates, what an attachment is), by dated amendments.

## Context

The brief: "shapes graphs bound to a dataset or named graph can gate a
transaction (reject, or commit with the validation report attached as commit
metadata)". ADR 0017 fixed what a pre-commit validator may read — the overlay
`Overlay(G_head, δ)` and δ — and put a SHACL validator at layer 5. ADR 0058
made validators the dataset's, and left "recording *which shapes graph* gates
a dataset, as data that travels with the log" to this milestone. ADR 0148
named the package. ADR 0171 says which focus nodes a delta affects. ADR 0170
is the binding's vocabulary.

## Decision

### 1. The package

**`Varve.Shacl.Store`, layer 5**, referencing `Varve.Shacl` (3),
`Varve.Shacl.Sparql` (4), `Varve.Store` (4), `Varve.Derivation` (2) and
`Varve.Rdf` (1). It implements ADR 0017's `ICommitValidator`. `Varve.Store`
learns nothing about SHACL; `Varve.Management` is not referenced — a binding
reaches this package as a value (`ShapesBinding`), mapped by the host.

### 2. What the validator reads

The pending commit's overlay and its delta, and nothing else (ADR 0017):

- **The shapes in force** are the shapes graph — a named graph of the same
  dataset — as of the overlay. A commit that changes the shapes and the data
  together validates its data against its new shapes.
- **The data graph** is the union of the binding's graphs (ADR 0170), always
  excluding the shapes graph and every graph under
  `https://w3id.org/varve/ns#`: `GraphScopedQuadSource` (ADR 0107) over the
  overlay, matched as `GraphPattern.Any`.
- **The pre-state** is the overlay's base, the pinned position, read through
  the same scope.
- **A loaded shapes graph is kept** between commits and reloaded when a delta
  touches the shapes graph. Every `Data` commit of the dataset passes through
  the validator (ADR 0058), so no change to the shapes graph can pass unseen;
  a commit refused after the shapes were reloaded only costs one more load.
  The shapes are copied out of the overlay before loading, since a pinned view
  does not outlive its commit.

### 3. The modes

- **R1, `reject`.** A commit is refused when, for its affected focus nodes
  (ADR 0171), the post-state has a validation result of severity
  `sh:Violation` **not present at the pinned position**. A result's identity is
  (focus node, source shape, source constraint component, path, value), the
  path by its SPARQL form (ADR 0154).
  - *Present at the pinned position* means in the pinned position's whole
    report. An identity new among the affected pairs is checked against the
    pairs that could have produced it before — up the `sh:property` chains
    from its source shape, by reverse paths in the pre-state (ADR 0171's Rev)
    — so a result produced by an unaffected pair is never mistaken for a new
    one.
  - **The guarantees**: under `reject`, the set of violations never grows; from
    a conforming start, the dataset stays conforming.
- **R2, `report`.** The commit is accepted, with the attachment of ADR 0173.
- **R3, `off`.** Recorded only: nothing runs, nothing is attached.
- **R4, apply time.** A `reject` binding is refused when its shapes include a
  shape ADR 0171 classes *full* and the binding does not declare
  `varve:allowFullEvaluation true`, and any binding not `off` is refused when
  its shapes graph is refused (ill-formed, unstratifiable, a missing
  extension, a refused function). The check needs the data dataset, which a
  validator of the system dataset may not read (ADR 0017), so it runs **above
  the store**, in the apply path, before the management commit is submitted —
  where ADR 0017's alternatives put a cross-dataset check.
- **R5, shapes changes.** A commit that changes the shapes graph is **never
  refused because of existing data**. When the same commit changes data too,
  the nodes the *data* part affects are validated against the *new* shapes,
  and R1 compares the post-state with the pinned data under the new shapes —
  so tightening a shape never refuses an unrelated edit, and the projection
  reports what became non-conforming. The problem's detail says so. A shapes
  commit is still refused when it makes the bound shapes refused, or, under
  `reject` without the flag, full: a refusal about the shapes, never the data.
- **R6, failing closed.** A binding can reach R4's state without R4 seeing it —
  the shapes graph changed between the apply-time check and the binding's
  commit. A `reject` binding whose shapes then classify full without the flag
  refuses every data commit under it with `binding-not-incremental` until the
  binding or the shapes are fixed.

### 4. Refused functions

In any binding not `off`, a SPARQL-based constraint or component that calls
`NOW()`, `RAND()`, `UUID()` or `STRUUID()`, or `BNODE()` where its value can
reach a result's `sh:value`, a message or a bound result variable, is refused
when its shapes graph is loaded for the binding, naming the shape and the
function. Their value depends on the wall clock or on chance, not on the
position, so even a full validation at a position could not be repeated, and
neither a verdict nor a report would be reproducible.

### 5. Several bindings

A dataset may have several bindings; each is validated, in the order of their
IRIs, and the first refusal wins. `ValidationVerdict` gains
`Accept(IReadOnlyList<RdfTerm>)` (`Varve.Store`, additive) so one validator
records one attachment per binding.

### 6. The problem types

| Type | Status | Members |
|---|---|---|
| `shapes-violated` | 422 | `binding`, `shapesGraph`, `findings` (`focusNode`, `path`, `value`, `sourceShape`, `component`, `severity`, `messages`), `report` (the restricted report, canonical N-Quads), `shapesChanged` |
| `binding-not-incremental` | 422 | `binding`, `shapes` (`shape`, `construct`, `section`) |
| `shapes-invalid` | 422 | `binding`, `problems` (`kind`, `shape`, `focusNode`, `message`, `cycle`) |

A validator reports them as ADR 0145 does: the problem type's IRI and its
members as an `rdf:JSON` literal, honoured by `Varve.Protocol` only for the
types its catalogue lists as validator types (ADR 0119, amended).

### 7. Where bindings come from

- **Embedded**: a `ShapesGate` over the bindings the caller gives, in
  `DatasetOptions.Validators`.
- **Over a fleet**: the binding in force for a commit is the one at the
  management position the write is authorised under (ADR 0165). The server
  adds the dataset's gate to the validators a write carries at its pinned
  position, beside the order of positions, and keeps one gate per dataset
  while its bindings stay the same, so the shapes stay loaded. The apply path
  checks a binding against its dataset where the instance holds it (R4); a
  dataset held elsewhere is checked by its writer at commit time (R6).
- **Described**: `GET /manage/datasets/{name}/bindings/{binding}` serves a
  binding at the applied position with its shapes' classification.

## Alternatives considered

- **Refuse every commit that leaves an affected node non-conforming**, whether
  or not the violation was there before (the "sticky violator" rule, the
  plan's first R1). Rejected by the maintainer: after a shapes tightening,
  every unrelated edit of a non-conforming entity would be refused, so a
  shapes change would freeze the data it was meant to improve.
- **Validate the whole data graph per commit.** Exact and O(dataset) per
  write. ADR 0171 is why it is not needed.
- **Class clock-dependent SPARQL as full.** The plan's first proposal.
  Rejected: full re-evaluation of `NOW()` is still not a function of the
  position.
- **The apply-time check as a dataset validator of the system dataset.** It
  would read another dataset, which ADR 0017 forbids.

## Consequences

- Validation is write latency (ADR 0017): the cost is the affected pairs',
  measured per commit and per affected node at REC S and M.
- `Varve.Store`'s baseline gains `ValidationVerdict.Accept(IReadOnlyList<RdfTerm>)`
  and `Attachments`.
- Three problem types, each with a page in `docs/problems/`.
- The operator's guide gains a page on shapes bindings (part 2b).

## Checks

- **Checked against the accepted ADRs** (0001–0157, and #134's 0163–0168).
  Touches **0005** (`Varve.Store` stays SHACL-free), **0010** (exact deltas),
  **0017** and **0058** (amended: what this validator reads and attaches, and
  where the binding is recorded), **0107** (the scoped source), **0119**
  (amended: validator problem types), **0145** (the report's two-term form),
  **0148** (the package), **0150** (a refusal is static), **0165** (the
  management position decides the binding). No conflict.
- **Layer ownership.** `Varve.Shacl.Store`, layer 5; the verdict's new member
  is `Varve.Store`, layer 4; the problem catalogue is `Varve.Protocol`,
  layer 5.
- **Analyzer rule.** None new.
- **Open questions owned.** None.
