# 0149 — The SHACL conformance baseline: the 2017 suites gate, the 1.2 drafts pinned

## Status

**Accepted — filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13), 2026-10-17** (ADR 0066). Decided by the maintainer on the part 1 plan:
- SHACL 1.0 Core and SHACL-SPARQL are gated by the W3C suites, under the
  ratchet;
- the SHACL 1.2 suite runs as well, under the same ratchet, with every case
  out of scope exempted by name and reason;
- where SHACL 1.0 and the 1.2 drafts conflict, Varve follows 1.2;
- `sh:rule` belongs to Inference (#95).

## Context

The brief gives SHACL its own gate, the W3C SHACL test suite, which "does not
block core conformance", and puts SHACL 1.2 in the model from the start, as
RDF 1.2 was. SHACL 1.2 is a set of Working Drafts, not a Recommendation, and
a draft moves: a feature can be added, renamed or dropped between two
publications. A baseline that says "1.2" without saying which 1.2 cannot be
checked.

## Decision

### The pin

This baseline is the drafts and the suite as they stood when read:

| What | Version |
|---|---|
| SHACL 1.2 Core | W3C Working Draft, 14 October 2026 (`https://www.w3.org/TR/shacl12-core/`) |
| SHACL 1.2 SPARQL-Related Features | W3C Working Draft, 11 October 2026 (`https://www.w3.org/TR/shacl12-sparql/`) |
| SHACL 1.2 Node Expressions | W3C Working Draft, 21 July 2026 — read for scope only |
| Test suites | `w3c/data-shapes` at commit `4cd469867a1cc6d2fff80e8a40723b26c695d67a` |
| SHACL 2017 | W3C Recommendation, 20 July 2017 (`https://www.w3.org/TR/shacl/`) |

Section numbers in Varve's SHACL ADRs and code are those of these versions.
**The pin is revisited when SHACL 1.2 Core reaches Candidate
Recommendation**: the session that moves it reads the changes appendix, moves
the submodule, and reports every case whose outcome changed.

### The suites

- **`w3c/data-shapes` is a submodule at `tests/w3c/data-shapes`**, pinned at
  the commit above, as `rdf-tests` is (ADR 0007). Test data is never fetched
  at test time. Its licence is the W3C Software and Document License
  (`LICENSE.md` in that repository).
- **`data-shapes-test-suite/tests/`** — the 2017 suite — **gates**: `core`
  (98 cases) and `sparql` (22 cases), every case `sht:approved` and reachable
  from the root manifest. The proposed `nodeValidator-001`, which no manifest
  includes, is not run.
- **`shacl12-test-suite/tests/`** runs as well: `core` and `sparql`, the
  `sht:Validate` entries only (`sht:Infer` and `sht:EvalNodeExpr` belong to
  the inference rules and node expressions documents, which are out of
  scope). `node-expr/`, `inference-rules/` and `sparql-rl/` are not run.
- **Two subjects**, `dataset` (`InMemoryDataset`) and `store` (`Varve.Store`
  over a dataset loaded with the same quads), so that a store-backed source
  is exercised by every case.
- **The comparison is the suite's "full compliance"** (`index.html`, *Validate
  RDF data*): the actual report is normalised — `sh:detail` dropped, node types
  plain, report and result nodes blank, shared `sh:resultPath` structures
  cloned, only `rdf:type`, `sh:result`, `sh:conforms`, `sh:focusNode`,
  `sh:resultPath` (and its structure), `sh:resultSeverity`,
  `sh:sourceConstraint`, `sh:sourceConstraintComponent`, `sh:sourceShape` and
  `sh:value` kept, and `sh:resultMessage` kept only where the expected graph
  has the same object — and is compared to the expected graph by isomorphism.
  `mf:result sht:Failure` passes when validation fails, which in Varve is a
  refused shapes graph or a failed validation (ADR 0151).
- **The ratchet** (`eng/ratchet.cs`) holds every passing case in
  `baseline/passing.txt` and every failing one in `baseline/exemptions.txt`
  with its reason.

### Where 2017 and 1.2 conflict, 1.2 wins

Four 2017 cases test rules the 1.2 SPARQL document removes or narrows, and
are exempted over both subjects with that reason:

- `sparql/pre-binding/pre-binding-006` and `sparql/pre-binding/unsupported-sparql-004`
  expect a failure for a sub-`SELECT`. SPARQL-Related Features, Appendix F,
  removes that restriction (Issue 159).
- `sparql/pre-binding/shapesGraph-001` uses the pre-bound `$shapesGraph` and
  `$currentShape`. Appendix F removes them (Issue 426), and Varve's shapes and
  data are two sources (ADR 0151), so there is no graph name to bind.
- `sparql/pre-binding/unsupported-sparql-002` expects a failure for any
  `VALUES`. Appendix A of the 1.2 document forbids only a `VALUES` that
  mentions a pre-bound variable, which this one does not. Found while
  implementing, and added here before the pull request.

Any other conflict found while implementing is added here by a dated
amendment, never decided in passing.

### SHACL 1.2 features in scope

From SHACL 1.2 Core (TR, 14 October 2026):

- **§3.1.3.3** implicit class targets with `sh:ShapeClass`; a non-IRI shape
  that is also a class is ill-formed.
- **§3.1.3.6** `sh:targetWhere`, and **§3.1.3.7** explicit shape targets,
  `sh:shape` in the data graph.
- **§3.1.4–§3.1.6** severity, message and deactivation per constraint, through
  reifiers of the shape–parameter triple; `sh:Trace` and `sh:Debug`.
- **§5.1, §5.2** constant node expressions (IRIs and literals) where Core
  admits a node expression: `sh:targetNode` and `sh:deactivated`.
- **§6.4** the graph for `rdfs:subClassOf` triples: the parameter the draft
  calls `subClassOfInShapesGraph`, as `ShaclOptions.SubClassOfInShapesGraph`.
  False by default, so `rdfs:subClassOf` is read from the data graph; true
  reads it from the shapes graph as well. `rdf:type` is always the data
  graph's.
- **§6.7** conformance checking against the disallowed severities, and
  **§6.8.1.2** `sh:conformanceDisallows`, as an option
  (`ShaclOptions.ConformanceDisallows`) whose default is the draft's:
  `sh:Violation`, `sh:Warning`, `sh:Info`.
- **§7.1.1–§7.1.3** `sh:class`, `sh:datatype` and `sh:nodeKind` with a list
  value, which is a union: a value node passes when it matches any member.
  Several values of `sh:class` remain a conjunction, one result per class
  missed. The node kind `sh:TripleTerm`.
- **§7.4.4** `sh:singleLine` (at risk in the draft); **§7.4.6**
  `sh:uniqueLang` over `rdf:dirLangString`.
- **§7.5.1–§7.5.4** `sh:memberShape`, `sh:minListLength`, `sh:maxListLength`,
  `sh:uniqueMembers`.
- **§7.6** property pair components over a path; **§7.6.3** `sh:subsetOf`.
- **§7.8.3** `sh:someValue`; **§7.8.5** `sh:reifierShape` and
  `sh:reificationRequired`.
- **§7.9.1** `sh:closed sh:ByTypes`; **§7.9.4** `sh:rootClass`; **§7.9.5**
  `sh:uniqueValuesFor`.

From SHACL 1.2 SPARQL-Related Features (TR, 11 October 2026): `sh:severity`
on a SPARQL-based constraint, the narrowed
pre-binding restrictions of Appendix A (`VALUES` only when it mentions a
pre-bound variable; sub-`SELECT` allowed; `SERVICE` refused, which the draft
permits a processor to do with a failure), and prefixes declared on an
`sh:ShapesGraph` when a query has no `sh:prefixes`.

### Out of scope, each with its reason

- **SHACL 1.2 Node Expressions** beyond constants — `sh:expression`,
  `sh:nodeByExpression`, the node expression library — and **`sh:values` and
  `sh:defaultValue`** with anything but a constant: a separate document, its
  own milestone if the maintainer wants it.
- **SPARQL-based node expressions and functions** (SPARQL-Related Features §6,
  §7), and **`sh:SPARQLTarget`**, which 1.2 drops.
- **Result annotations** (SPARQL-Related Features §5): optional for a
  processor (§1.3), no suite case uses them, and they would put arbitrary
  properties on the typed result; they come with the public shapes model
  if a consumer needs them (ADR 0155).
- **`owl:imports` closure** (Core §6.1): not followed, by ADR 0151.
- **`sh:usedDataGraph`, `sh:usedShapesGraph`, `sh:usedConfiguration`**
  (§6.8.1.5–§6.8.1.8): optional, and Varve's inputs are sources, not graphs
  with names (ADR 0151).
- **§6.8.3, incomplete data.** Read: it defines `sh:incomplete` and
  `sh:incompleteURIStart` in the data graph and says, normatively, that "the
  presence of incomplete nodes does not modify the validation results,
  including the value of `sh:conforms`". The rest of the section is
  non-normative and lets a *tool* hide or downgrade results in presentation.
  Varve presents nothing, so it conforms to §6.8.3 by leaving every result as
  it is; a caller who presents results can query `sh:incomplete` itself. The
  shapes-graph form of incompleteness needs `sh:values` or `sh:defaultValue`,
  out of scope above.
- **§8 non-validating characteristics** (`sh:name`, `sh:intent`,
  `sh:codeIdentifier`, `sh:unit`, …): read and ignored, as they do not
  validate.
- **`sh:rule` and the inference rules document**: Inference (#95).

### The outcome

Over both subjects:

| Suite | Cases | Pass | Exempt |
|---|---:|---:|---:|
| 2017 `core` | 98 | 98 | 0 |
| 2017 `sparql` | 22 | 18 | 4, the conflicts above |
| 1.2 `core` | 162 | 162 | 0 |
| 1.2 `sparql` | 43 | 33 | 10: five SPARQL functions (§7), five SPARQL node expressions (§6) |

So 232 of 240 and 390 of 410 ratchet lines pass, and every exemption is
written with its reason in `baseline/exemptions.txt`. A case that fails for
another reason is a defect to fix or a new exemption with its reason, never a
silent one.

## Alternatives considered

- **The 2017 suite only.** The gate the brief names. Rejected by the
  maintainer: the 1.2 features in scope would have no external check.
- **Follow 2017 where it conflicts with 1.2.** The Recommendation is the only
  stable text. Rejected: 1.2 is in the model from the start, and the three
  rules it removes would have to be removed later, by a breaking change.
- **Fetch the suites at test time.** Forbidden by `docs/testing.md`.
- **Vendor the files** instead of a submodule. Same effect, larger diffs; the
  repository already vendors every W3C suite as a submodule.

## Consequences

- The 1.2 suite moves with the drafts. A case added upstream is not run until
  the pin moves, and moving the pin is a reviewed change.
- `baseline/exemptions.txt` carries SHACL exemptions for the first time, each
  over two subjects.

## Checks

- **Checked against the accepted ADRs** (0001–0133, 0143–0147). Touches
  **0007** (a new suite family and submodule, amended), **0025** (property
  tests beside the suite), and **0061** (RDF 1.2 in the model). No conflict.
- **Layer ownership.** The harness is `tests/Varve.Conformance.Tests`, which
  declares no layer.
- **Analyzer rule.** None.
- **Open questions owned.** None. The pin's revisit is owned here, due when
  SHACL 1.2 Core reaches Candidate Recommendation.
