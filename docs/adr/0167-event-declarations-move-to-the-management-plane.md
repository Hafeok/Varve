# 0167 — Event declarations move to the management plane

## Status

**Accepted — filed unaccepted by milestone Management plane of #105, part 2,
2026-10-23** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the plan of #105 (2026-10-19), question
6, as approved with the plan's revision, including the revision identifiers
after the move (`m{p}`); and on the part 2 plan (#105, 2026-10-23), for the
credential that carries the copy (point 3) and the cancel (point 4).

**Amends** ADR [0144](0144-the-event-catalogue.md) (point 6 realised), ADR
[0145](0145-the-event-schema-registry.md) (revision identifiers) and ADR
[0166](0166-varve-management-and-the-vocabulary.md) (four terms; event nodes in a
dataset's graph), each by a dated block (ADR 0068).

**Related:** ADR [0163](0163-the-management-plane.md) (the planes), ADR
[0165](0165-the-management-position-on-the-commit.md) (the position on the
commit, which decides the boundary) and ADR
[0168](0168-write-routing-by-forwarding.md) (forwarding).

## Context

ADR 0144 point 6 promised that event-schema declarations would become
management declarations "under `/manage/`: the same terms and the same checks
(ADR 0145), in a different home", and that "the rendered events, which depend
only on the declarations in force at each position, and the as-of history of
the catalogue graphs already written" would stay.

Part 1 of the management plane (#131) built everything the move needs:
- a system dataset with a dataset's graph per dataset (ADR 0163);
- the management position each data commit records, which never decreases
  along a dataset's log (ADR 0165);
- forwarding of a write to its writer (ADR 0168).

Three things are hard about the move.
1. **The declarations in force at a commit must not depend on where they are
   read from.** A consumer pins a `dataschema`; a byte that changes across the
   move breaks it.
2. **The catalogue graphs are written by the data plane, on the dataset's
   writer, and the management graphs by the management plane, on the system
   writer.** The two writers can be different instances of the fleet, and
   either can die halfway.
3. **A schema revision is a dataset position** (ADR 0145), and a declaration
   changed on the management plane after the move has no dataset position.

## Decision

1. **The move is per dataset, an explicit act, recorded on both sides.**
   - `POST /manage/datasets/{d}/events/move`, and `varve manage move-events
     <d>`, by an operator or the dataset's admin.
   - The dataset node carries the state, `varve:eventDeclarations`, with
     `varve:Moving` or `varve:Moved`, or no value before a move.
2. **Three steps.**
   - **The freeze.** The system writer commits `varve:eventDeclarations
     varve:Moving` at m₁. The dataset's writer refuses a commit that changes a
     catalogue graph when it is authorised at a management position whose
     declarations say `Moving` or `Moved`: `409 event-catalogue-moved`.
   - **The barrier.** The dataset's writer, having applied m₁, passes one
     probe commit through the dataset's sequencer, authorised at m₁ and refused
     by design. After it, every commit is authorised at m₁ or later (ADR 0165's
     order), so no commit can change a catalogue graph, and the catalogue at
     the head is final.
   - **The copy and the switch.** The dataset's writer reads the catalogue at
     its head and translates each catalogue graph `varve:events-catalogue/
     {enc(G)}` into nodes of `varve:manage/dataset/{d}`: every triple as it
     is, and `varve:sourceGraph G` on each type node (`varve:defaultGraph` for
     the default graph). It sends them as one management write, conditional on
     the version the dataset's graph had at m₁, which replaces `varve:Moving`
     by `varve:Moved`. The system writer commits it at m₂.
3. **The credential.** A management write carries a caller's token, and ADR
   0168 has no peer identity, so the move runs in the caller's request.
   - The move route is routed to the **dataset's** writer, by ADR 0168's
     forwarder. That instance makes the freeze and the switch as management
     writes under the caller's token: locally when it also writes the system
     dataset, otherwise forwarded to the system writer with the caller's
     `Authorization` and the hop header, so that they are never forwarded
     again.
   - **The move is idempotent and resumable.** A request that finds `Moving`
     at the applied position starts at the barrier; one that finds `Moved`
     answers the move done. A writer that also writes the system dataset
     resumes a `Moving` dataset by itself when it converges, with no token.
     Otherwise the next request resumes it, and the CLI retries.
   - `GET /manage/datasets/{d}/events` and readiness name a dataset that is
     `moving`.
4. **Transitions.** The system dataset's validator allows none to `Moving`,
   `Moving` to none (a cancel), and `Moving` to `Moved`, which brings the event
   nodes. `Moved` is final. A cancel reopens catalogue writes authorised from
   then on. A copy in flight fails its version check, reads the state again
   and stops. The dataset's writer cancels the move itself when the frozen
   catalogue does not translate one-to-one, for example when one type node's
   IRI is in two catalogue graphs, and the move's answer says why.
5. **The boundary is exact.** A data commit renders its events from the
   system dataset if and only if the management position it records is m₂ or
   later. Otherwise it renders from the dataset's catalogue history, as
   before. Commits from before part 1 record no position, and render from the
   dataset. Positions never decrease along a log (ADR 0165), so the boundary
   splits each log into a prefix and a suffix.
6. **The renderer** keeps ADR 0144 point 5's history and adds a second.
   - `EventCatalogueHistory.ContinueFrom(snapshot)` seeds the post-move
     history with the catalogue at the move. A type whose generated schema
     has the same bytes across the move keeps its revision, so its
     `dataschema` is byte-identical.
   - `ManagedEventDeclarations` (`Varve.Protocol`) maps each management commit
     to the dataset's event nodes into the catalogue graphs' change form, at
     the management position, and feeds the post-move history.
   - The views at the head (the list, the AsyncAPI document, a schema) read
     the post-move history at the applied management position. An as-of view
     reads at the position the commit at that position records, the rule
     rendering uses.
7. **Revision identifiers after the move.**
   - A revision minted after the move is `…/versions/{n}/revisions/m{p}`, p
     the management position.
   - Revisions minted before the move keep their bare integers, so every URL
     already served stays immutable, and the two spaces cannot collide.
   - `SchemaRevision.Revision` becomes a `SchemaRevisionId` (breaking before
     1.0).
   - ADR 0145's golden files run unchanged. A new golden case, `moved`,
     crosses the move with an `m{p}` revision.
8. **After the move.**
   - A write to a catalogue graph is refused with `409 event-catalogue-moved`.
     Its `manage` member names `/manage/datasets/{d}/events`.
   - The catalogue graphs' history stays readable as of any position, through
     SPARQL, the Graph Store and `Varve-As-Of`, under the same governor rule
     (ADR 0144 point 3).
   - Declarations are written on the management plane, through `varve apply`
     or `PUT`/`PATCH /manage/datasets/{d}`. `apply` owns the event nodes with
     the rest of the dataset's graph. `GET /manage/datasets/{d}/events` says
     where they are (`in-dataset`, `moving` or `moved`), and answers the
     event nodes as TriG when asked for `application/trig`.
9. **Validation in the new home is the same.** On every commit to the system
   dataset, `ManagementValidator` reads the declarations by the code validator
   (ADR 0166), and checks the event nodes of each dataset's graph it touches
   by `EventDeclarationCheck` over their catalogue-graph translation, before
   and after. That is the same entry point and the same refusals as before the
   move (ADR 0145). SHACL part 2b may change what is behind either entry point
   without changing the move.

## Alternatives considered

- **The system writer copies from its follower of the dataset.** No
  credential is needed, but the system writer cannot know that the catalogue
  at its follower's head is final without a mark in the dataset's log, and a
  mark is a commit to the data.
- **A fleet peer identity, so that a dataset's writer resumes a move by
  itself.** Refused by ADR 0168.
- **Move every dataset at once, at a release.** A fleet would need a
  coordinated stop, and a dataset whose catalogue does not translate would
  block the others.
- **Renumber revisions after the move into dataset positions.** No data commit
  corresponds to a management change, and a number taken from the dataset's
  space could later collide with a revision the catalogue would have minted.
- **Keep both homes writable.** Two sources for the declarations in force
  would need a merge rule, and the boundary would not be a position.

## Consequences

- **Problem type:** `event-catalogue-moved` (`409`).
- **Vocabulary:** `varve:eventDeclarations`, `varve:Moving`, `varve:Moved`, and
  `varve:sourceGraph` (already listed by ADR 0166 for part 2).
- **Breaking before 1.0:** `SchemaRevision.Revision` and
  `EventEndpoints.Schema(…, SchemaRevisionId)`.
- **The tests:**
  - 200 generated histories cross a move at a generated point. Each data
    commit renders three ways: from the dataset history without the move (the
    oracle), through the switched path, and replayed twice. The bytes are
    equal wherever the declarations did not change after the move;
  - #93's two rendering properties re-run over the switched path;
  - catalogue writes refused after m₁, at the store and over HTTP;
  - as-of reads of the catalogue graphs equal before and after the move;
  - a crash at every step recovers to a completed move, with nothing lost or
    doubled;
  - the fleet suite moves a dataset through a non-writer.
- **The operator guide** gains "Moving event schemas".

## Checks

- **Checked against the accepted ADRs** (0001–0173). It touches:
  - **0144** (point 6 realised; a dated block);
  - **0145** (revision identifiers; a dated block);
  - **0165** (the boundary rests on its order);
  - **0166** (the terms; a dated block);
  - **0168** (the copy is a forwarded management write; no peer identity
    added);
  - **0119** (one problem type).

  No conflict.
- **Layer ownership.**
  - `Varve.Events` (4): `SchemaRevisionId` and `ContinueFrom`.
  - `Varve.Management` (4): the terms, the state and the transitions.
  - `Varve.Protocol` (5): the translation, the boundary, the freeze, the
    routes and the composed validator.
  - `Varve.Server` (6): the move's driver, the barrier and the resume.
- **Analyzer rule.** None.
- **Open questions owned.** None.
