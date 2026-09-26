---
set: property-paths-normalised-then-evaluated-by-alp
namespace: varve
adr: 0054
decisions:
  - key: PathsNormalisedFirst
    statement: "A normalisation pass, always applied before the optimiser, rewrites link, inverse, sequence and alternative at the top of a path into triple patterns, swapped paths, joins and unions, recursively"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: FreshPathVariablesOutsideVarname
    statement: "Variables a path rewrite introduces are named .p0, .p1 and so on, outside VARNAME, so none collides with an author's or is ever projected"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ClosuresByAlp
    statement: "Path closures are evaluated by section 18.4's ALP, one reachability search per start node with a visited set, yielding sets of nodes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ClosureStartsFromBoundEnds
    statement: "A closure searches from its bound end, stops at a bound other end, and with both ends unbound starts from every node of the active graph"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ZeroLengthPathFromAbsentTerm
    statement: "A zero-length path from a term absent from the graph still binds that term, as section 18.4 reads"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: NegatedPropertySetIsAFilteredScan
    statement: "A negated property set is a filtered scan in each direction it names"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: PathsStayInOneGraph
    statement: "A closure runs within one active graph, and once per named graph when the graph variable is unbound"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0054](../adr/0054-property-paths-normalised-then-evaluated-by-alp.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
