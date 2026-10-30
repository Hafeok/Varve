---
set: subject-scoped-grants-and-the-classifier-binding
namespace: varve
adr: 0234
decisions:
  - key: ClassifierBindingTerms
    statement: "varve:ClassifierBinding has one varve:boundGraph naming its shapes graph and a graph set in ADR 0166's terms without a template, not naming its own shapes graph"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: SubjectTermsOnAGrant
    statement: "A read or write grant may carry varve:subject, varve:subjectPrefix, varve:subjectTemplate, varve:subjectClass and varve:subjectLink, and a write grant varve:createUnder; a grant without subject terms has the subject set all"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: RowScopeFromTheGrantTable
    statement: "GrantTable.ScopesOf answers a RowScope per classifier binding, readable and writable, with templates expanded per caller, and RowScope.All for a caller restricted in no binding"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: RowGrantCoversTheWholeBinding
    statement: "A grant with subject terms must contain the whole graph set of every classifier binding it meets, must not use a graph template and must meet one, and two classifier bindings must not overlap"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: RowWritesExcludeNonLocalGates
    statement: "A reject or report shapes binding or a tier-3 inference binding over a classified graph is refused while a write grant with subject terms covers that graph"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: PlanNamesRowRestrictions
    statement: "The plan names each binding a grant's subject terms restrict and says when an apply widens a grantee's subject set"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: BindingsReservedForErasure
    statement: "varve:KeyStoreBinding, varve:erasureMode and varve:privacy stay reserved for erasure mode (#203) and are refused; varve:ClassifierBinding is defined by ADR 0234"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
---

The rulings of [ADR 0234](../adr/0234-subject-scoped-grants-and-the-classifier-binding.md), filed unaccepted by milestone 9, part 1 ([#202](https://github.com/Hafeok/Varve/issues/202), under #14)
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
