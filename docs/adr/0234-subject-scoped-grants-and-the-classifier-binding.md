# 0234 — Subject-scoped grants and the classifier binding

## Status

**Filed unaccepted by milestone 9, part 1 (#202, under #14), 2026-10-30**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
on the plan in #14 (2026-10-30), question 1 and decision 3. **Amends ADR
[0166](0166-varve-management-and-the-vocabulary.md)** by a dated amendment in
that file. `varve:ClassifierBinding` stops being reserved, and a grant gains
subject terms.

Records decision set
[`subject-scoped-grants-and-the-classifier-binding`](../decisions/subject-scoped-grants-and-the-classifier-binding.md).

## Context

ADR 0166 reserved `varve:ClassifierBinding` for #14 and defined grants by
graph set only. ADR 0230 needs two things declared:
- a binding's graphs and shapes;
- a grant's subject set.

Both must be declarative, applied by `varve apply`, and checked by shapes
(ADR 0175).

## Decision

1. **`varve:ClassifierBinding`**, a `varve:Binding` in a dataset's management
   graph. It has:
   - exactly one `varve:boundGraph`, the shapes graph of the same dataset;
   - a graph set in ADR 0166's terms (`varve:allGraphs`, `varve:graph`,
     `varve:graphPrefix`), as a shapes binding does. A `varve:graphTemplate`
     is refused, and so is naming its own shapes graph.
2. **Subject terms on a `varve:Grant`**, read and write grants only:

   | Term | Subject set |
   |---|---|
   | *(none)* | `all` |
   | `varve:subject` | an IRI; repeatable |
   | `varve:subjectPrefix` | a literal; repeatable |
   | `varve:subjectTemplate` | a `varve:SubjectTemplate`; repeatable, with `varve:template` and `varve:match` as a `varve:GraphTemplate` has, over the same variables |
   | `varve:subjectClass` | a class IRI: the data subjects with `(s rdf:type C)` in Γ_B at the read's position; repeatable |
   | `varve:subjectLink` | a `varve:SubjectLink` with one `varve:linkPredicate` IRI and one `varve:linkTarget`, either `varve:CallerAgent` or a `varve:SubjectTemplate` with `varve:Exact`: the data subjects with `(s ℓ target)` in Γ_B; repeatable |
   | `varve:createUnder` | a literal prefix or a `varve:SubjectTemplate` with `varve:Prefix`; on write grants only: where ADR 0236's W3 lets the grantee create rows |

   Every input of the two rule forms is a quad of the subject's own row,
   which ADR 0236 needs. A template that does not expand for the caller
   gives nothing, as a graph template does.
3. **`GrantTable.ScopesOf`** answers ADR 0230 point 4.
   - For each classifier binding, it gives the union of the subject sets of
     the read and write grants covering a graph of Γ_B, or `all` when one
     carries no subject terms. Templates are expanded per caller.
   - **The writable subject set** is the same, over write grants alone.
   - Admin and operator read and write every row.
   - **The answer is a `RowScope`** inside `CallerScope` (ADR 0235). A caller
     with no row restriction in any binding has `RowScope.All`, so nothing is
     built for it (ADR 0107 point 4).
4. **The apply-time preconditions.** The management validator (ADR 0166
   point 4) refuses each of these as `declaration-invalid`, and each finding
   names the grant and the binding:
   - **P1**: a grant with subject terms whose graph set does not contain the
     whole Γ_B of every classifier binding it meets. Containment is set
     inclusion over `allGraphs`, explicit graphs and prefixes. A prefix
     contains a graph or a prefix it starts.
   - **P2**: subject terms on a grant whose graph set uses a template.
   - **P3**: subject terms on a grant that meets no classifier binding.
   - **P4**: two classifier bindings whose graph sets overlap.
   - **P5**: a `reject` or `report` shapes binding, or a tier-3 inference
     binding, over a graph of a Γ_B, while a write grant with subject terms
     covers that graph. A shapes binding whose every shape is row-local is
     exempt. That exemption is #204's, and until it lands, the refusal
     holds. A refusal of a row-restricted write must not depend on another
     row (ADR 0236).
   - Subject terms on `admin` or `operator`, `createUnder` on a read grant,
     and the reserved `varve:privacy` anywhere.

   P1 to P4 are judged over the fleet graph and the dataset's graph
   together, as ADR 0190 point 7's precondition is.
5. **The plan.**
   - `ManagementPlan` names, per grant with subject terms, each binding whose
     rows it restricts and its subject set.
   - A plan that **widens** a grantee's subject set, by removing a subject
     term or a binding, says so, as ADR 0190 point 5 does for
     dataset-public graphs.
   - For each binding, the plan lists the graphs a row-restricted grantee
     reads that lie outside every binding. Their quads are governed by graph
     alone, so a subject's IRI there is shown (ADR 0230 point 5).
6. **Shapes.** `shapes/management.ttl` states the cardinalities and kinds
   above. The residual code check keeps P1 to P5, which compare
   declarations, and template syntax (ADR 0175).

## Alternatives considered

- **Subject sets on a separate declaration**, a row policy beside the grant.
  Two declarations would then have to agree on a grantee. A grant already
  says who and where. It now also says whose.
- **Arbitrary SPARQL in a grant.** It would not be declarative, and its
  inputs could be any row.
- **P1 per graph rather than per binding.** ADR 0230's alternatives say why.

## Consequences

- **`docs/vocabulary.md`** gains the terms, and `ClassifierBinding` leaves the
  reserved list. `KeyStoreBinding`, `erasureMode` and `varve:privacy` stay
  reserved until #203.
- An operator who wants a row-restricted caller confined to some graphs of a
  binding cannot have it: P1 refuses it. The answer is two bindings, or one
  binding over fewer graphs.

## Checks

- **Checked against the accepted ADRs** (0001–0190). Touches:
  - **0166**: amended;
  - **0175**: shapes first, the residual check in code;
  - **0190**: point 7's precondition shape, and point 5's plan;
  - **0169** and **0173**: P5.

  No conflict.
- **Layer ownership.** `Varve.Management` (4). `ManagementValidator` in
  `Varve.Protocol` (5) runs it.
- **Analyzer rule.** None.
- **Open questions owned.** None.
