# Moving event schemas

A dataset's event declarations (ADR 0144) start in its catalogue graphs,
`varve:events-catalogue/{enc(G)}`, written by the graph's writers through
any write path. Moving them to the management plane makes them declarations
like the dataset's grants and settings: reviewed in your repository, applied
by `varve apply`, and audited in the system dataset (ADR 0167). The move is
per dataset, an explicit act, and the events every commit renders do not
change by a byte.

## Before you move

- **Who may move.** An operator, or an admin of the dataset, with a token for
  the management audience.
- **What must hold.** Every catalogue graph must translate one-to-one into
  the dataset's graph: each node is in one catalogue graph, and no node of
  the catalogue is already a node of the dataset's declarations (its own
  node, a grant, a binding). A catalogue that does not translate cancels the
  move, and the answer names the node and says why. Nothing else changes.
- **The catalogue freezes at the first step.** From then on, a write to a
  catalogue graph is refused with
  [`409 event-catalogue-moved`](../problems/event-catalogue-moved.md). Plan
  the move for a time when nobody changes event types through the data
  plane.

## Moving

```bash
varve manage move-events notes --server https://varve.example.org \
  --authority https://login.example.org/v2.0 --client-id … --client-assertion github
```

The command sends `POST /manage/datasets/notes/events/move` to the server you
name, and that server forwards the request to the dataset's writer (ADR 0168).
The dataset's writer then:

1. **Freezes** the catalogue graphs: the system writer commits
   `varve:eventDeclarations varve:Moving` on the dataset's node at system
   position m₁.
2. **Passes a barrier** through the dataset's sequencer. After it, no commit
   can change a catalogue graph, so the catalogue at the head is final.
3. **Copies** each catalogue graph into the dataset's graph. Every triple goes
   as it is, and each type node gains `varve:sourceGraph`, the graph its
   catalogue graph's name carried (`varve:defaultGraph` for the default
   graph). The copy commits with `varve:eventDeclarations varve:Moved` at m₂,
   under the version of the dataset's graph.

The answer is `200` with `{"state":"moved","position":m₂,"types":n}`.
`GET /manage/datasets/notes/events` says where the declarations are now:
`in-dataset`, `moving` or `moved`. With `Accept: application/trig`, it answers
the moved event nodes.

## When a move stops

A move is idempotent: sending it again resumes it where it stopped.

- **A stop before the copy** leaves the dataset `moving`. The catalogue stays
  frozen, and nothing is lost: the copy reads the catalogue the freeze made
  final.
- **Seeing it.** The dataset's status (`GET /manage/datasets/notes/status`), the
  instance's readiness (`GET /health/ready`) and
  `GET /manage/datasets/notes/events` all report
  `"events": {"state": "moving", "since": m₁}`, m₁ being the system
  position of the freeze. It is information only: readiness stays `200`,
  and reads and data writes go on.
- **The resume.**
  - The writer of a fleet of one, and any instance that writes both the
    dataset and the system dataset (the default placement), finishes the
    move by itself on its next interval.
  - Otherwise, send the move again with
    `varve manage move-events notes …`, the same command as the move. It
    copies the frozen catalogue and switches. It also retries by itself
    when the writer changed during the request.
  - A switch the writer finished by itself is attributed to the agent who
    authorised the freeze at m₁, read from the freeze commit's header, never
    to the instance.
- **A stop after the switch** leaves the dataset `moved`. The answer was lost,
  and sending the move again answers `moved` with the same position.
- **Cancelling.** While the dataset is `moving`, replace
  `varve:eventDeclarations varve:Moving` with
  `varve:eventDeclarations varve:InDataset` on the dataset's node, in your
  declaration file, and `varve apply` it (or `PUT` the dataset's
  declarations). The cancel is its own management commit, at m₃.
  Catalogue writes authorised at m₃ or later are accepted again, and the
  event bytes of every commit are unchanged. A dataset in `InDataset` may
  move again later. `varve:Moved` is final, and cannot be cancelled.

## After the move

- **Declare event types on the management plane.** The dataset's graph holds
  them now, so your declaration file must hold them too:
  - `varve apply` owns the whole graph, and a file without the event nodes
    would remove them, which the check refuses;
  - export them once with `GET /manage/datasets/notes` (TriG) and keep them
    with the dataset's other declarations;
  - the type nodes keep their IRIs, and the marker `varve:eventDeclarations
    varve:Moved` stays on the dataset's node.
- **The same checks apply** (ADR 0145). An invalid declaration is
  `event-declaration-invalid`. An incompatible change at an unchanged version
  is `event-schema-incompatible`. A declaration is retired with
  `varve:eventRetired true`, never removed.
- **Schemas.** A type whose schema did not change keeps its revision and its
  `dataschema`. A revision minted after the move is
  `…/versions/{n}/revisions/m{p}`, with p the system position of the change.
  Every revision URL served before the move stays immutable.
- **Which declarations render a commit.** The management position the commit
  records (ADR 0165) decides:
  - a commit authorised at m₂ or later renders from the system dataset;
  - any earlier commit renders from the catalogue history, as it did before.

  The list, the AsyncAPI document and a schema at the head read the
  declarations at the instance's applied position.
- **The catalogue graphs' history** stays readable as of any position, through
  SPARQL, the Graph Store and `Varve-As-Of`, under the same grants as before
  (ADR 0144 point 3).

## In a fleet

The request may reach any instance. It is forwarded once to the dataset's
writer. If that instance does not also write the system dataset, it sends the
freeze and the copy to the system writer under your token, with the hop
header. A follower of the dataset reads the moved declarations from its own
follower of the system dataset. It renders a commit only once it has applied
the position that commit records. Until then the events request answers
`503 management-behind`, and asking again shortly is enough.
