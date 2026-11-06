# 0194 — The TBox binding vocabulary

## Status

**Accepted — filed unaccepted by Inference, part 1 (#170, under #95),
2026-10-27** (ADR 0066). Decided by the maintainer on the plan in #95
(2026-10-27), question 5, with the answers to D2 and D5: `varve:Rewriting` is
a fourth profile, and in this part the only one with an effect. Acceptance is
the maintainer's act on the pull request. **Amends ADR
[0166](0166-varve-management-and-the-vocabulary.md)** by a dated amendment.

## Context

ADR 0166 records `varve:TBoxBinding`, with one `varve:boundGraph`, and leaves
its effect to Inference. ADR 0170 gave `varve:ShapesBinding` its terms.

#95's tiers need a binding to name:
- the TBox graphs;
- the data graphs;
- what is materialised;
- the rules;
- whether inference crosses graphs;
- how `owl:sameAs` is treated;
- the regime a query gets by default;
- how inconsistency is handled at commit.

Tier 1 (ADR 0193) needs only the TBox graphs, the data graphs and the
default.

## Decision

1. **A TBox binding is a management declaration**: an IRI node of class
   `varve:TBoxBinding` in its dataset's declaration graph, as a shapes binding
   is (ADR 0170 point 1). Its terms are all in `https://w3id.org/varve/ns#`,
   and the existing ones are reused, so there is no parallel vocabulary:

   | Term | Value | Default | In this part |
   |---|---|---|---|
   | `varve:boundGraph` | **the TBox graphs**: one or more absolute IRIs, named graphs of the same dataset | required | in effect |
   | `varve:allGraphs`, `varve:graph`, `varve:graphPrefix` | the data graphs, in the grant graph-set terms on the binding node, as ADR 0170 has them | one required | in effect |
   | `varve:profile` | `varve:Rewriting` (tier 1 only), `varve:RDFS`, `varve:OWL2RL`, `varve:RulesOnly` | required | `Rewriting` in effect; the others refused as reserved until #171 and #95's part 3 |
   | `varve:rulesGraph` | an absolute IRI, a named graph of the dataset | none | refused as reserved |
   | `varve:crossGraph` | `xsd:boolean` | `false` | refused as reserved |
   | `varve:sameAs` | `varve:SameAsOff`, `varve:SameAsClasses` | `SameAsOff` | refused as reserved |
   | `varve:defaultEntailment` | a regime IRI of ADR 0192's table | `ent:Simple` | `Simple`, `RDF`, `RDFS` and `D` in effect; the OWL ones refused as reserved |
   | `varve:inconsistencyMode` | ADR 0170's `varve:Reject`, `varve:Report`, `varve:Off` | `Off` | refused as reserved |

   - **`varve:boundGraph`** keeps ADR 0166's name. For this class it becomes
     *one or more*.
   - **A reserved term** is refused with a message that names the part that
     gives it effect. A declaration written for a later part is then refused,
     not silently ignored.
2. **The data graphs.**
   - The data graphs are the graph set, always excluding the binding's TBox
     graphs and every graph under `https://w3id.org/varve/ns#`.
   - **Each graph of a dataset is covered by at most one TBox binding.** Two
     bindings whose graph sets overlap are refused. Overlap is judged on
     explicit graphs, prefixes (one a prefix of the other) and `allGraphs`.
     So a graph's TBox, and later its implicit part, is never ambiguous.
3. **The default regime.**
   - `varve:defaultEntailment` is the dataset's default (ADR 0192 point 2),
     whichever graph a query reads.
   - Two bindings of one dataset with different defaults are refused, so the
     default is the dataset's.
4. **What the validator refuses**, beside what ADR 0166 point 4 and ADR 0170
   point 4 refuse:
   - a `TBoxBinding` without a `varve:boundGraph`, or with a TBox graph that
     is `varve:defaultGraph` or a graph under the `varve:` namespace;
   - a binding without exactly one `varve:profile` and without a graph set;
   - a `varve:graphTemplate` on a binding, which ADR 0170 refuses for the same
     reason: a binding has no caller;
   - one of its own TBox graphs named by `varve:graph`;
   - more than one `varve:defaultEntailment`;
   - points 2 and 3;
   - the reserved terms and values of point 1.
5. **Validated by shapes at runtime**, as ADR 0175 rules for every
   declaration.
   - `shapes/management.ttl` gains the `varve:TBoxBinding` shape: the counts,
     the node kinds and the values.
   - The residual code check covers what SHACL Core cannot state: the
     overlap, the dataset-wide default, and the reserved terms.
   - The code validator as it stands, extended, is the tests' oracle. A
     property holds the shapes and the code oracle to the same findings over
     generated declarations.
6. **The dataset-public graphs at apply (ADR 0190 point 5).**
   - `ManagementPlan` lists, per dataset, every graph its TBox bindings make
     dataset-public under the desired declarations.
   - It marks those that are not dataset-public under the current
     declarations as `widened`, and those that stop being so as `narrowed`.
   - `varve apply --plan` prints the list before the graph diffs, and the
     saved plan's JSON carries it.
   - **A property:** for generated current and desired declarations, the
     plan's lists name exactly the graphs whose dataset-public status the
     apply changes.
7. **In code.**
   - `Varve.Management` reads a binding into `TBoxBindingDeclaration`.
   - `GrantTable.ScopesOf` gives each caller's readable scope its dataset's
     dataset-public graphs, which are the TBox graphs of the bindings at the
     table's position (ADR 0190 point 4).
   - The value the rewriting reads is the binding's TBox graphs and graph set.
     The server maps the declaration to it.

8. **The full reads, surfaced.** A graph that holds a triple saying something
   of the RDF or RDFS vocabulary itself makes every schema it is part of read
   its scoping graph whole (ADR 0193 point 2). The operator sees which:
   - **The binding's description.** `GET
     /manage/datasets/{name}/bindings/{binding}`, behind admin, describes a
     TBox binding: its TBox graphs, profile, default and position, and, where
     this instance holds the dataset, `fullReads`, the graphs among its TBox
     graphs and the graphs it covers that hold such a triple, the default
     graph as `null`.
   - **Readiness** gives each dataset's count as `fullReads`, and never turns
     unready for it. It names no graph: `/health/ready` is unauthenticated
     (ADR 0113 point 4), and a graph's name is what a grant guards
     (ADR 0107).
   - **Found by the saturation's own probes,** through the dataset's schema
     cache, once per head and bindings, so a readiness probe repeats
     nothing.
   - **Where such an axiom costs least.** The full read is per scoping graph,
     a graph merged with its binding's TBox. In a data graph the axiom acts in
     that graph alone, which is read whole again after every commit to it. In
     a TBox graph it acts in every graph the binding covers, and each of them
     is read whole again after every commit to it: the benchmark measures the
     two the same per commit, so a TBox graph saves nothing and spreads the
     cost. The operator guide says to keep such axioms out of a binding's
     TBox, and in the one graph that needs them where one does.

## Alternatives considered

- **A `varve:tboxGraph` term beside `varve:boundGraph`.** It would be a second
  term for the same role, and ADR 0170 chose `varve:boundGraph` for the same
  reason.
- **TBox graphs readable by grant only.** ADR 0190 rules otherwise.
- **Overlapping bindings with the union of their TBoxes.** It is workable for
  rewriting, and ambiguous for the closure: which binding's rules and modes
  would hold? So it is refused from the start, so that #171 does not have to
  tighten it.
- **The reserved terms accepted and ignored until their part lands.** A
  declaration's meaning would then change under it at an upgrade.

## Consequences

- `docs/vocabulary.md` gains the terms and the individuals.
- The operator guide's `docs/operator/inference.md` shows a binding in TriG
  (#170 writes its tier-1 part).
- `Varve.Management`'s baseline gains `TBoxBindingDeclaration` and the plan's
  list of dataset-public graphs.

## Checks

- **Checked against the accepted ADRs** (0001–0178, 0190–0193). Touches:
  - **0163** and **0165**: a binding is applied at a management position;
  - **0166**: amended;
  - **0170**: its graph-set terms and refusals, reused;
  - **0175**: shapes first, with the code as the oracle;
  - **0190**: dataset-public graphs.

  No conflict.
- **Layer ownership.** `Varve.Management` (4): the vocabulary, the validator,
  the plan and the grant table. `Varve.Server` (6): the mapping.
- **Analyzer rule.** None.
- **Open questions owned.** None.
