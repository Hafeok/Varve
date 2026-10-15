---
set: client-side-setup-and-system-graphs
namespace: varve
adr: 0130
decisions:
  - key: SetupIsDeclared
    statement: "DatasetSetup declares a dataset's name, storage, settings, ordered baseline sources and ordered named migrations"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: ApplyOrder
    statement: "ApplyAsync creates the dataset when a read answers 404, merges the declared settings under If-Match when one differs, adds each baseline by the Graph Store Protocol's POST, then applies each unrecorded migration"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: BaselineBlankNodesRefused
    statement: "A baseline source holding a blank node is refused before any request, naming the source and pointing to skolem IRIs"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: MigrationAndRecordOneCommit
    statement: "A migration's update and the INSERT DATA of its record into varve:setup are one request under If-Match, so they are one commit or none; a 409 or 412 re-reads the records"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: RecordPositionIsItsCommit
    statement: "A migration's record carries its id and no position; its position is the commit that asserted the record"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: SystemGraphsAreData
    statement: "Graph names under https://w3id.org/varve/ns# are reserved for system graphs Varve's components write; they are ordinary data under ordinary grants"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: GrantsAreNotSetup
    statement: "A setup never grants permissions; grants are the server's configuration"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: SetupFileIsAStrictYamlSubset
    statement: "The setup file is a specified YAML subset, and anything outside it is refused with its line and column"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: BaselinesOnlyAdd
    statement: "A baseline is added with the Graph Store Protocol's POST and never put, so applying or retrying it never retracts what the application wrote to its graph; retractions are recorded migrations"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: SettingsAreDesiredState
    statement: "A setup's settings are desired state, last writer wins: each declared key is compared and, when one differs, the declared keys alone are merged in under If-Match; a manual change to a declared key is overwritten on the next apply, and an undeclared key is never touched"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: CatalogueGraphsGovernedBySource
    statement: "The event catalogue graphs are system graphs governed by their source graphs, the first system graphs with a derived grant; varve:setup keeps point 5 as written"
  - key: MigrationsStateTheirGraphs
    statement: "A migration is sent under Varve-If-Graphs-Match on varve:setup and every graph its operations name, so a writer of another graph does not move it; one whose graphs cannot all be named falls back to the dataset-wide If-Match"
  - key: OneGraphStoreWriteApi
    statement: "The client writes a graph through one API: PutGraphAsync, PostGraphAsync and DeleteGraphAsync take the graph, a GraphContent of bytes in a media type and a GraphCondition, none, IfMatch with a version or IfAbsent, in place of the byte[] and string overloads"
---

The rulings of [ADR 0130](../adr/0130-client-side-setup-and-system-graphs.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration of #93
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.
