---
set: client-side-setup-and-system-graphs
namespace: varve
adr: 0130
decisions:
  - key: SetupIsDeclared
    statement: "DatasetSetup declares a dataset's name, storage, settings, ordered baseline sources and ordered named migrations"
  - key: ApplyOrder
    statement: "ApplyAsync creates the dataset when a read answers 404, puts the settings under If-Match when they differ, writes each baseline by the Graph Store Protocol, then applies each unrecorded migration"
  - key: BaselineBlankNodesRefused
    statement: "A baseline source holding a blank node is refused before any request, naming the source and pointing to skolem IRIs"
  - key: MigrationAndRecordOneCommit
    statement: "A migration's update and the INSERT DATA of its record into varve:setup are one request under If-Match, so they are one commit or none; a 409 or 412 re-reads the records"
  - key: RecordPositionIsItsCommit
    statement: "A migration's record carries its id and no position; its position is the commit that asserted the record"
  - key: SystemGraphsAreData
    statement: "Graph names under https://w3id.org/varve/ns# are reserved for system graphs Varve's components write; they are ordinary data under ordinary grants"
  - key: GrantsAreNotSetup
    statement: "A setup never grants permissions; grants are the server's configuration"
  - key: SetupFileIsAStrictYamlSubset
    statement: "The setup file is a specified YAML subset, and anything outside it is refused with its line and column"
---

The rulings of [ADR 0130](../adr/0130-client-side-setup-and-system-graphs.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration of #93
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.
