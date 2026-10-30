---
set: the-row-scoped-quad-source
namespace: varve
adr: 0235
decisions:
  - key: SubjectScopeAndRowScope
    statement: "SubjectScope is a subject set as a value and RowScope maps a binding to one, a binding it does not name being all; CallerScope gains ReadableRows and WritableRows"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: RowScopedSourceFiltersQuadsPassesTerms
    statement: "RowScopedQuadSource shows a quad exactly when the row rule allows it and passes every term member through, so it never denies a term"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: AllRowsSkipsTheWrapper
    statement: "A RowScope of all is served by the inner source itself; the wrapper is skipped, not a no-op layer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: RowDecisionIsTwoProbesPerQuad
    statement: "A quad of a classified graph costs a memoised binding decision per graph handle, one probe of the subject's set and, unless the predicate is a public link or the object a literal, one probe of the object's, none allocating"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: SubjectSetDecisionMemoisedPerSet
    statement: "Whether an interned subject set is within the caller's subject set is decided once per set id per source, and each member subject once, in maps filled through GetOrAdd"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: GraphFilterBeforeRowFilter
    statement: "A row-restricted read wraps the graph-scoped source, so a quad of an unreadable graph never costs a probe"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: TripleTermsFoundOncePerSection
    statement: "A term section finds its triple terms' counters once, by one pass over its entries on the first lookup of a triple term's parts, and answers every later lookup by a search of them, so a lookup of a term that is not a triple term reads no entry"
---

The rulings of [ADR 0235](../adr/0235-the-row-scoped-quad-source-and-the-read-filters-cost.md), filed unaccepted by milestone 9, part 1 ([#202](https://github.com/Hafeok/Varve/issues/202), under #14)
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
