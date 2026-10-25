---
set: event-declarations-move-to-the-management-plane
namespace: varve
adr: 0167
decisions:
  - key: MoveIsPerDatasetAndExplicit
    statement: "Event declarations move to the management plane per dataset, by POST /manage/datasets/{d}/events/move, recorded as varve:eventDeclarations varve:Moving then varve:Moved on the dataset node"
  - key: FreezeBarrierCopySwitch
    statement: "The system writer commits Moving at m1; the dataset's writer refuses catalogue writes authorised there or later, passes a refused probe through its sequencer at m1, then copies its final catalogue into the dataset's graph with varve:sourceGraph, committed with Moved at m2 under the graph's version"
  - key: MoveRunsUnderTheCallersToken
    statement: "The move route is routed to the dataset's writer, which makes the freeze and the switch as management writes under the caller's token, forwarded to the system writer once; the move is idempotent and resumes from the frozen catalogue, and a switch resumed with no request names the agent of the freeze commit's header"
  - key: MoveTransitions
    statement: "A dataset's event declarations go from none or varve:InDataset to Moving, from Moving to varve:InDataset by a cancel committed on its own at m3, after which catalogue writes authorised at m3 or later are accepted, or from Moving to Moved with the event nodes; Moved is final"
  - key: MovingIsReported
    statement: "A move left Moving is reported by the dataset's status, readiness and its events resource as moving since m1, as information, never unready"
  - key: BoundaryIsTheRecordedPosition
    statement: "A data commit renders its events from the system dataset exactly when the management position it records is m2 or later, and from the dataset's catalogue history otherwise"
  - key: ContinueFromKeepsRevisions
    statement: "The post-move history is seeded with the revisions at the move, so a type whose schema bytes did not change keeps its revision and its dataschema"
  - key: RevisionsAfterTheMove
    statement: "A schema revision minted after the move is m{p}, p a management position; earlier revisions keep their bare integers, and every URL served stays immutable"
  - key: CatalogueWritesRefusedAfterTheMove
    statement: "A write to a catalogue graph authorised at a position whose declarations say Moving or Moved is refused with 409 event-catalogue-moved, naming /manage/datasets/{d}/events; the catalogue graphs stay readable as of any position"
  - key: SameChecksInTheNewHome
    statement: "Moved event declarations are checked on every system commit by EventDeclarationCheck over their catalogue-graph translation, as before the move"
---

The rulings of [ADR 0167](../adr/0167-event-declarations-move-to-the-management-plane.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Management plane, part 2, of #105
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.
