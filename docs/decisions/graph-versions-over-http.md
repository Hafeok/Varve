---
set: graph-versions-over-http
namespace: varve
adr: 0126
decisions:
  - key: GraphReadIsTaggedWithTheGraphVersion
    statement: "A Graph Store GET or HEAD carries ETag of the graph's version at the read's position, a strong tag across serialisations with Vary on Accept"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: GraphIfMatchIsTheGraphVersion
    statement: "If-Match on a Graph Store write is compared strongly with the graph's version and becomes the expected position with the graph as its scope"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: IfGraphsMatchForUpdate
    statement: "Varve-If-Graphs-Match names graphs and versions for a SPARQL Update, is 412 when one differs, and replaces the inferred scope"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: EmptyGraphHasNoRepresentation
    statement: "An empty graph matches no If-Match tag, If-None-Match star on PUT creates only, and its version still counts in a scope"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: ConflictGraphsAreInTheRequestScope
    statement: "The 409 graphs member lists at most 100 intervening commits read at most the as-of bound back, with only graphs in the request's scope"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: ClientExposesGraphVersions
    statement: "Varve.Protocol.Client exposes a graph's version and takes the conditional headers on Graph Store writes and updates"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
---

The rulings of [ADR 0126](../adr/0126-graph-versions-over-http.md), filed unaccepted by milestone Commit path of #91
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
