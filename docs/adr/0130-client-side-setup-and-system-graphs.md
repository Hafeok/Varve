# 0130 — Client-side setup: a declared dataset, migrations recorded as data, and the first system graph

## Status

**Accepted — filed unaccepted by milestone Client and integration of #93,
2026-10-13** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Client and integration plan:
- a migration's record takes its position from the commit that asserted it;
- a baseline holding a blank node is refused before anything is sent;
- the setup file's YAML is a specified subset, refused outside it with a line
  and a column.

Then, on the first part's review:
- **baselines add and never replace.** A Graph Store `PUT` on every apply, and
  on every retry, would delete what the application wrote to the graph
  since, so a baseline is a `POST`, and retractions happen only through
  recorded migrations;
- **settings are desired state, last writer wins.** A setup overwrites a
  manual change to a setting it declares, and leaves one it does not declare
  alone.

## Context

An application that uses Varve needs its dataset to exist with known settings
and a known vocabulary before its first request. Then, over its life, it needs
the occasional change that is not idempotent by nature: a retraction, a
rename, a split. Relational applications reach for migration frameworks
because their schema is DDL and DDL is not idempotent.

Varve's setup is data, and data writes are idempotent by construction. I2 says
re-asserting what exists is an empty effective delta, and no commit. A Graph
Store `POST` of the same triples twice makes one commit, then none. So most of a
setup can be declared and re-applied on every start. Only the non-idempotent
remainder needs to remember whether it has run, and the natural place to
remember that is the dataset itself, where it is versioned, time-travellable
and visible in the commits list like every other fact.

Two details stop "just `POST` it" from being idempotent.

1. **Blank nodes.** Labels in a request are fresh nodes (ADR 0098), so a
   baseline with `[ … ]` in it adds new nodes every time, one commit per
   start, for ever.
2. **A migration's record.** A migration's update and the fact that it ran must
   be one commit. Otherwise a crash between them runs it twice. One commit
   cannot contain its own position, so the record cannot either.

## Decision

1. **`DatasetSetup`** in `Varve.Protocol.Client` declares the following.
   - **The dataset's name and storage** (`File` or `Memory`).
   - **Its settings**, as a JSON object of the keys the settings resource
     takes (ADR 0118) that the setup means to hold; the keys it omits are
     not its concern. **Settings here are interim:** the Management plane's
     ADR, point 10 ([#105](https://github.com/Hafeok/Varve/issues/105)),
     moves settings, like other declarations, to `/manage/`, and
     `DatasetSetup` keeps data-shaped work only: seed data, data migrations
     and the setup graph.
   - **Baseline sources**, ordered. Each is a name, a target graph (an IRI or
     the default graph), a media type Varve reads, and the content as bytes,
     a stream factory, or an embedded resource. Each is **added** to its
     graph with the Graph Store Protocol's `POST`, never put: a baseline
     never retracts.
   - **Migrations**, ordered and named. Each is an id unique within the setup
     and one SPARQL Update request.
2. **`client.ApplyAsync(setup)`**, on a client addressing that dataset, does
   in order:
   1. **The dataset.** A read (an `ASK` query, which needs `read` alone). When
      it answers `404`, `PUT /datasets/{name}` with the storage (idempotent,
      ADR 0118). So a setup run by a caller without server admin works against
      a dataset that exists.
   2. **The settings, as desired state.** `GET …/settings`, and each
      declared key compared with the dataset's as a JSON value. When one
      differs, the declared keys alone are merged in (`PATCH`, RFC 7396)
      under `If-Match` with the `ETag` just read. Last writer wins: a manual
      change to a declared key is overwritten on the next apply, and a key
      the setup does not declare is never touched. A `409` or `412`, another
      writer first, is read again and the declared values applied again.
   3. **The baselines.** Each in order, added by the Graph Store Protocol's
      `POST`: one commit the first time, none after, by I2. Nothing a baseline
      did not write is ever retracted, so what the application wrote to the
      same graph survives every apply and every retry. A triple a baseline no
      longer holds stays until a migration retracts it, and that retraction
      is recorded like any other.
   4. **The migrations.** The ones not yet recorded, each as **one request**
      (below).

   It answers a `SetupResult` naming what committed and the final position.
   On a refusal it throws `SetupException`, carrying the step, the status and
   the problem body.
3. **A baseline with a blank node is refused before any request.**
   - The client reads each source with the reader for its media type and
     throws `SetupException` naming the source and the first blank node's
     line.
   - The message points to skolem IRIs (RDF 1.1 Concepts §3.5): mint
     `https://example.org/.well-known/genid/…` IRIs, or IRIs of the
     application's own, in place of `[ … ]` and `_:b`.
   - This is why the client references `Varve.RdfXml` and `Varve.JsonLd` as
     well as `Varve.Turtle`.
4. **A migration and its record are one commit.**
   - **The graph.** Applied migrations are recorded in the graph
     **`https://w3id.org/varve/ns#setup`** (`varve:setup`).
   - **The record.** Each is a node
     `https://w3id.org/varve/ns#setup/migration/{id}` (the id
     percent-encoded) with `rdf:type varve:Migration` and
     `varve:migrationId "{id}"`.
   - **The request.** A migration's request is its update, `;`, and an
     `INSERT DATA` of that record into `varve:setup`, sent under
     `If-Match: "<p>"`, where `p` is the position at which the records were
     read.
   - **The outcome.** Either both land in one commit (ADR 0057) or neither
     does. A failure leaves the head and the record as they were.
   - **Concurrent appliers.** A `409` or `412` means another writer moved the
     dataset first. The client reads the records again and continues from
     what is recorded, so two instances starting together apply each
     migration once. A baseline write that meets one is sent again, since it
     is idempotent.
   - **The record carries no position.** The migration's position is the
     commit that asserted its record, found by the commits list filtered to
     `varve:setup` or by an as-of query.
5. **`varve:setup` is the first system graph.**
   - Graph names under `https://w3id.org/varve/ns#` are reserved for system
     graphs that Varve's own components write. They are **ordinary data**:
     - stored in the log;
     - covered by grants like any graph (ADR 0107), so an applier needs read
       and write on `varve:setup` beside its targets;
     - visible to SPARQL;
     - time-travellable.
   - The server gives them no special treatment in this milestone.
   - `docs/vocabulary.md`'s scope widens from the service description to
     every term Varve writes: `varve:setup`, `varve:Migration` and
     `varve:migrationId` are listed there.
6. **Grants are not setup.** An application cannot grant itself permissions.
   Grants are the server's configuration (ADR 0107), and the guide says so.
   A setup that needs a permission it lacks fails with the `403` it was given.
7. **The setup file.** `DatasetSetupFile.Read(path)` reads a declared setup
   from a YAML file (ADR 0131's `varve setup apply`).
   - **The format is a strict subset, specified on its documentation page:**
     - mappings of plain keys;
     - sequences of mappings;
     - plain, single-quoted and double-quoted scalars;
     - `|` block scalars;
     - whole-line comments.
   - **Anything outside the subset is refused with its line and column**: an
     anchor, a tag, a flow collection, a tab in the indentation, an unknown
     key, a key given twice.
   - **Files** are named relative to the setup file. The media type comes
     from the extension, or from `format`.

## Alternatives considered

- **A migration framework with a version table.** It is the relational answer
  to non-idempotent DDL, and Varve's setup is mostly idempotent. The remainder
  is recorded the way everything in Varve is recorded.
- **The record in a second commit, carrying the first one's position.** Not
  atomic: a crash between the two runs the migration again.
- **The server records migrations.** It would need a setup endpoint and a
  migration model. The client can do it with the protocol as it is, and the
  record is plain data either way.
- **Skolemising blank nodes in the client.** Deterministic skolem IRIs would
  have to be derived from content (RDFC-1.0), and every edit to the baseline
  would mint new IRIs for unchanged nodes. Refusing, with the fix named, keeps
  identity in the author's hands.
- **YamlDotNet.** It is admitted for `tools/repo-standard` only. A shipped
  package needs AOT-safe parsing, and the subset is small: the release
  descriptor reader already shows the shape (`eng/lib/Releases.cs`).

## Consequences

- An application's setup is declarative and safe to run on every start. A
  second run makes no commit.
- Migrations are visible in the history, as of any position.
- The client gains Graph Store writes (`PUT`, `POST`, `DELETE`) and the
  dataset `PUT`. Commit path (#91) adds their `If-Match`.

## Checks

- **Checked against the accepted ADRs** (0001–0129). Touches **I2** (the
  idempotence the design rests on), **0057** (one request, one commit),
  **0094** (`If-Match`), **0098** (why blank nodes are refused), **0106** and
  **0118** (the dataset and settings resources), **0107** (grants on
  `varve:setup`), **0092** (the vocabulary's namespace). No conflict.
- **Layer ownership.** `Varve.Protocol.Client`, layer 5.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-15 — the event catalogue graphs, and the first derived grant

Filed by milestone Client and integration, part 2, of #93, unaccepted until
the maintainer accepts it (ADR 0066).

- **More system graphs.** The graphs named `varve:events-catalogue/{enc(G)}`
  (ADR 0144) are system graphs on point 5's terms: ordinary data, in the log,
  visible to SPARQL, time-travellable, written by any write path.
- **Point 5's "no special treatment" no longer holds for them.** A catalogue
  graph is governed by its source graph (ADR 0107's seam, amended the same
  day): a caller reads it exactly when it reads G and writes it exactly when it
  writes G, and a grant naming it has no effect. `varve:setup` keeps point 5
  as written.
- **They are checked.** A commit that touches one runs ADR 0145's validator.

**The ledger.** The ruling of this block is `CatalogueGraphsGovernedBySource`,
unaccepted until the maintainer accepts it.
