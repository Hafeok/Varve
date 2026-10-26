# 0145 — The event schema registry: versions, immutable revisions, compatibility checked at commit

## Status

**Accepted — filed unaccepted by milestone Client and integration, part 2, of
#93, 2026-10-15** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer in the part 2 brief, points 4 and 6, and on
its plan:
- a `$id` is an immutable revision under a version;
- an instance valid under any later revision of a version is valid under
  every earlier one, and a test holds it;
- schemas are computed on read, under the read-forever rule, with golden files.

The specification is `docs/spec/events.md` §6 and §7.

**Amended 2026-10-23** (ADR 0068), by milestone Management plane of #105,
part 2, under ADR [0167](0167-event-declarations-move-to-the-management-plane.md):
revision identifiers after the move; see the end.

## Context

A consumer of an event pins a schema: it generates a class, validates
input, or decides it can ignore a field. Two promises make that safe:
- a schema at a URL never changes;
- a producer cannot break a consumer by surprise.

The brief asks for both. It also says that adding an optional field keeps the
version. Those collide: adding a field changes the schema, so the schema at
`…/versions/{n}` would change content. The plan named the collision, and the
maintainer chose immutable revisions under a version.

The declarations are data in the log (ADR 0144). Every version ever declared
is therefore in the history, and a schema can be computed from its
declaration when asked rather than stored. A computed schema has the same
risk as any rendering: a generator change silently alters a served `$id`.

## Decision

1. **The schema is generated when read, never stored.**
   - It is JSON Schema draft 2020-12, in canonical JSON.
   - `additionalProperties` is `true`.
   - `required` lists the required field names.
   - Each field's type and format come from ADR 0143's mapping.
2. **Versions and revisions.**
   - A declaration states its version n.
   - A **revision** r of version n is a position at which the generated
     schema (its bytes without `$id`) differs from the one at the position
     before, or at which the type is first declared.
   - Under `/datasets/{name}/events/types/{T}`:
     - `…/versions/{n}/revisions/{r}` is immutable: it is every schema's `$id`,
       and the CloudEvent's `dataschema`;
     - `…/versions/{n}` resolves to the latest revision of n;
     - the bare URL resolves to the latest revision of the current version;
     - the resolving URLs answer with `Content-Location`.
   - Every version and revision ever declared stays served.
   - An event renders with the revision in force at its position.
3. **The read-forever rule for the generator.**
   - `tests/Varve.Events.Tests/fixtures/schemas/` holds, per case, a
     declaration history and the bytes each of its revisions generates.
   - A generator change that alters any byte fails the tests, and there is no
     regeneration switch in CI.
   - A served `$id` never changes content.
4. **Changes are checked at commit, by a dataset-level validator.**
   - `EventCatalogueValidator : ICommitValidator` uses the store's existing
     contract (ADR 0058), not a change to the commit path. It is in
     `Varve.Protocol`, and the server binds it to every dataset it opens,
     the CLI's embedded datasets included.
   - It runs only when the effective delta touches a graph under
     `varve:events-catalogue/`.
   - It compares the declarations of the touched catalogue graphs before
     (the proposed state with the delta undone) and after.
5. **What it refuses.**
   - **`event-declaration-invalid`** (`422`) covers each of these:
     - a term missing, repeated or of the wrong kind;
     - a blank node;
     - a graph under the catalogue namespace that is no catalogue;
     - a `SELECT` that does not parse or is not of the rendering's form;
     - a field repeated;
     - a class declared twice in one graph;
     - **a short name taken**, which names nothing but the declaration being
       written (ADR 0144);
     - **a declaration removed**: a type is retired with
       `varve:eventRetired true`, never deleted, so a short name never
       returns with an old version, and the check needs no history;
     - a version lowered.
   - **`event-schema-incompatible`** (`422`) applies when the version is
     unchanged and the new schema would accept an instance the old one
     refuses. That covers:
     - a field removed;
     - a field's JSON type or format changed;
     - a required field added, or an optional one made required;
     - a required field made optional (an instance without it would fail the
       old revision);
     - `fieldAs` or `fieldKeepLanguage` changed.

     Its member `fields` names each field and the kind of change.
   - **What keeps the version:**
     - an optional field added;
     - the query's text, the subject, the class or a description changed;
     - retirement.

     An incompatible change is accepted when the version goes up. Versions
     may skip, and never decrease.
6. **A validator's refusal reaches HTTP as its own problem type.**
   - A rejection's report whose first term is the IRI of a catalogued problem
     type that the catalogue marks as a validator's, followed by an
     `rdf:JSON` literal of the members, is answered as that type.
   - Any other report is answered as `rejected`, as before (ADR 0119).

## Alternatives considered

- **Bump the version on every schema change.** That is simpler, but it
  contradicts the brief's "compatible keeps the version", and a consumer pinned
  to v1 would never see a new optional field.
- **Store the schema in the catalogue graph.** It would duplicate the
  declaration, and the two could disagree. The golden files give the computed
  schema the stability a stored one would have.
- **Check compatibility in a projection, after commit.** The breaking change
  would already be in the log, and an event renders with it.
- **Allow deletion, with tombstones in a system graph.** That is a second
  record of the same fact, and the check would need history. Retirement keeps
  the declaration where it was.
- **Name the other graph in `name-taken`.** It is helpful to an admin, and it
  is a leak to everyone else.

## Consequences

- A consumer can pin `dataschema`, and the bytes at that URL never change.
- A producer cannot ship a breaking change without saying so in the
  declaration.
- Every commit runs one more dataset validator. It costs a pass over the
  delta's graphs, and nothing more unless a catalogue graph is touched.
- The problem catalogue gains `event-declaration-invalid` and
  `event-schema-incompatible`. Its writer gains the validator path of point 6.

## Checks

- **Checked against the accepted ADRs** (0001–0133). It touches:
  - **0058** (a dataset-level validator, its intended use);
  - **0017** (the overlay it reads);
  - **0119** (two problem types, the validator path);
  - **0143** and **0144** (what it checks).

  No conflict.
- **Layer ownership.**
  - `Varve.Events` (4): the generator and the comparison.
  - `Varve.Protocol` (5): the validator adapter, the endpoints and the problem
    path.
  - `Varve.Server` (6): the binding.
- **Analyzer rule.** None new.
- **Open questions owned.** None.

## Amendment, 2026-10-23 — revision identifiers after the move

Filed by milestone Management plane, part 2, of #105, unaccepted until the
maintainer accepts it (ADR 0066). The ruling is ADR
[0167](0167-event-declarations-move-to-the-management-plane.md)'s
`RevisionsAfterTheMove`.

- **Point 2's r is a dataset position until the move.** After a dataset's
  declarations move to the management plane, a schema that changes has no
  dataset position to be revisioned at. Such a revision is
  `…/versions/{n}/revisions/m{p}`, p the management position of the change.
- **Every URL already served stays immutable.** A revision minted before the
  move keeps its bare integer, and a type whose generated bytes did not change
  across the move keeps its revision (`EventCatalogueHistory.ContinueFrom`).
  The two spaces cannot collide: an `m` segment is never a number.
- **`SchemaRevision.Revision`** is a `SchemaRevisionId`, which is either
  kind (breaking before 1.0).
- **Point 3's golden files** run unchanged. A new case, `moved`, crosses the
  move and holds an `m{p}` revision.
- **Point 4's check** runs in the new home through the same entry point,
  `EventDeclarationCheck`, on every commit to the system dataset that changes
  a dataset's moved declarations.

## Amendment, 2026-10-25 — what the commit-time check runs

Filed by SHACL, part 2b (milestone 8, #13), unaccepted until the maintainer
accepts it (ADR 0066).

Point 4's check is unchanged in what it refuses and how it answers. What it
runs changes: the declaration shapes of ADR 0157 decide what they state, and
the code decides the rest and the version history (ADR
[0175](0175-shapes-validate-varves-declarations.md)).
`event-declaration-invalid` and `event-schema-incompatible` keep their wire
shape.

**The ledger.** The rulings are ADR 0175's `ShapesAreTheRuntimeValidator` and
`WireShapesKept`.
