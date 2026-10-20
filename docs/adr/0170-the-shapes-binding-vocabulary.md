# 0170 — The shapes binding vocabulary

## Status

**Accepted — filed unaccepted by SHACL, part 2a (#138, under milestone 8,
#13), 2026-10-20** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 2 plan (#13, 2026-10-20),
including the data graph as the union of the bound graphs. Amends **0166**
(#134's vocabulary) by a dated amendment, once that ADR is on `main`.

## Context

ADR 0166 (Management plane, part 1) records `varve:ShapesBinding` with one
property, `varve:boundGraph`, and leaves its effect to this milestone. ADR
0169 needs a binding to name a shapes graph, the data graphs it validates and
a mode. ADR 0058 left "which shapes graph gates a dataset, as data that
travels with the log" to milestone 8.

## Decision

1. **A binding is a management declaration**, in the dataset's declaration
   graph `varve:manage/dataset/{enc(name)}` (ADR 0166), an IRI node of class
   `varve:ShapesBinding`. Its properties, all in
   `https://w3id.org/varve/ns#`:

   | Term | Value | Default |
   |---|---|---|
   | `varve:boundGraph` | **the shapes graph**: exactly one absolute IRI, a named graph of the same dataset | required |
   | `varve:validates` | a node carrying the grant vocabulary's graph-set terms: `varve:allGraphs true`, `varve:graph` (repeatable; an IRI or `varve:defaultGraph`) and `varve:graphPrefix` (repeatable) | required |
   | `varve:validationMode` | `varve:Reject`, `varve:Report` or `varve:Off` | required |
   | `varve:allowFullEvaluation` | `xsd:boolean` | `false` |
   | `varve:subClassOfInShapesGraph` | `xsd:boolean`, SHACL 1.2 Core §6.4's parameter | `false` |

   `varve:boundGraph` keeps ADR 0166's name and gains its meaning for this
   class; `varve:TBoxBinding` keeps it for Inference (#95). The graph-set terms
   are ADR 0166's own, so there is no parallel vocabulary.
2. **Shapes are data.** The shapes graph is a named graph of the dataset it
   gates, so it is versioned with it, readable as of any position, and changed
   by commits like any graph — never a file the server reads.
3. **The data graph is the union of the bound graphs**, as one data graph
   (SHACL 1.2 Core §6.2), always without the shapes graph and without any
   graph under `https://w3id.org/varve/ns#`. A result does not name its graph;
   `sh:class` and `sh:node` work across the bound graphs, as REC's portfolio
   needs (one graph per site, ADR 0141).
4. **What ADR 0166's code validator gains.** It refuses a `ShapesBinding`
   without exactly one `varve:boundGraph`, one `varve:validates` and one
   `varve:validationMode`; a `varve:graphTemplate` inside `varve:validates` (a
   template expands per caller, and a binding has no caller); a shapes graph
   inside its own data graph set; and an unknown mode. The rest — whether the
   shapes graph loads, and its classification — needs the data dataset and is
   ADR 0169's R4.
5. **In code**, `Varve.Shacl.Store.ShapesBinding` is the value: the binding's
   IRI, the shapes graph, a `GraphScope` (ADR 0107) for the data graphs, the
   mode, and the two flags. The host maps a declaration to it.

## Alternatives considered

- **One report per bound graph.** Each graph validated alone. Rejected by the
  maintainer: a link across graphs — a room in one site's graph, its building
  in another — would be a violation, and a binding would need one report and
  one attachment per graph.
- **A `varve:shapesGraph` term beside `varve:boundGraph`.** Clearer for this
  class alone, but a second term for the same role and an unused one on the
  binding.
- **The binding in the dataset's settings** (ADR 0021), in the data log. The
  settings are the dataset's, and a binding is the fleet's declaration, applied
  at a management position like grants (ADR 0163).

## Consequences

- `docs/vocabulary.md` gains the five terms and the three mode individuals.
- The operator's guide's page on shapes bindings (part 2b) shows a binding in
  TriG.

## Checks

- **Checked against the accepted ADRs** (0001–0157, and #134's 0163–0168).
  Touches **0058** (answered), **0107** (graph sets), **0163** and **0166**
  (amended). No conflict.
- **Layer ownership.** The vocabulary is `Varve.Management`'s (4, #134); the
  binding value is `Varve.Shacl.Store`'s (5).
- **Analyzer rule.** None.
- **Open questions owned.** None.
