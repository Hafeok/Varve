---
set: entailments-under-graph-level-grants
namespace: varve
adr: 0190
decisions:
  - key: VisibleWhenAPremiseSetIsReadable
    statement: "An entailment is visible to a caller exactly when one of its minimal premise sets, the graphs its premises came from less the binding's TBox and rules graphs, is contained in the caller's readable set"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: IntraGraphByDefault
    statement: "Without varve:crossGraph a conclusion is drawn from one data graph's quads plus the binding's TBox and rules graphs and lands in that data graph, the default graph included, so the read filter is ADR 0107's lookup of its graph handle"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: SchemaOnlyConclusionsInTheirOwnGraph
    statement: "A conclusion drawn only from a binding's TBox and rules graphs lands in the reserved graph https://w3id.org/varve/ns#inferred/{enc(binding)}/tbox, read under the dataset-public rule"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: SchemaTriplesInADataGraphAreItsData
    statement: "A TBox axiom asserted in a data graph acts on that graph's closure and nowhere else"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: BindingGraphsAreDatasetPublic
    statement: "A graph a TBox binding in force names as a TBox graph or as its rules graph is readable by every caller whose readable set in the dataset is not empty; a grant naming it adds nothing, writing it needs a write grant, and the bindings in force are those at the management position the read is served under (ADR 0107, amended)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: DatasetPublicSetInTheScope
    statement: "GraphScope carries a set of dataset-public graphs, allowed exactly when the caller's readable set is not empty, composed by the host's IAccessScopes from the bindings and decided once per graph handle, never per quad"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: WideningShownAtApply
    statement: "A management plan names every graph each desired binding makes dataset-public and marks those that become dataset-public by the apply, and a property holds that it names exactly the graphs whose status the apply changes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: CrossGraphOptIn
    statement: "A binding that declares varve:crossGraph true draws conclusions from the union of its data graphs plus its TBox and rules graphs; no binding does so by default"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ConclusionsPlacedByPremiseSet
    statement: "A cross-graph conclusion whose minimal premise set has two or more graphs lands in https://w3id.org/varve/ns#inferred/{enc(binding)}/{d}, d the hex SHA-256 of the set's sorted canonical N-Triples IRIs, once per minimal premise set"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: GovernedByEveryGraphOfASet
    statement: "A graph may be governed by every graph of a set, allowed exactly when every governor is allowed by the scope's own rules, decided once per graph handle and memoised (ADR 0107, amended)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: MinimalPremiseSetsByAbsorption
    statement: "The closure is computed over pairs of a quad and a premise set, a firing's set the union of its premises' sets, and a pair is kept only when no kept pair for the quad has a subset of its set"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: NoNegationAcrossGraphs
    statement: "A cross-graph binding whose rules have a negative dependency is refused at apply time; an intra-graph binding may use stratified negation"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: RewritingReadsThroughTheScope
    statement: "Query-time rewriting reads its data and its TBox through the caller's GraphScopedQuadSource and needs no placement of its own"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: WriteOutcomeOnlyFromReadableGraphs
    statement: "A data commit's tier-3 outcome, accepted or refused, its problem body and the attachment its writer sees, depends only on graphs the writer reads, and every witness it names is inside what the writer reads"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: CrossGraphGateNeedsReadOnEveryGraph
    statement: "A cross-graph binding whose tier-3 mode is reject or report requires every write grant on any of its data graphs to carry read on all of them, checked when the binding is applied and whenever grants change, and a declaration that would break it is refused as declaration-invalid naming the binding and the grant"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: CrossGraphAttachmentReadAsAReport
    statement: "A cross-graph binding's report attachment is delivered on the commits resource, the feed and the diff only to callers who read every graph of the binding"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: TheGrantProperty
    statement: "Over generated datasets, grants, TBoxes, rules and bindings, every surface shows each caller exactly the entailments of the closure of the graphs it reads with the TBox and rules, per graph without crossGraph and over their union with it; and over pairs of datasets that differ only in graphs a writer does not read, the same write has the same outcome"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
---

The rulings of [ADR 0190](../adr/0190-entailments-under-graph-level-grants.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference (#95) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
