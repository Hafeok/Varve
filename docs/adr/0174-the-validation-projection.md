# 0174 — The validation projection and its reserved graph

## Status

**Accepted — filed unaccepted by SHACL, part 2b (milestone 8, #13),
2026-10-23** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 2 plan (#13, 2026-10-20), with
three rulings:
- **a projection's lag is reported per binding on readiness, and never makes
  the dataset unready or "behind".** Only reads of the report graph are
  `503 validation-behind`;
- **reading the report graph needs read on every graph the binding
  validates**, by amendment to 0107. Writing it is `graph-not-writable`;
- **every instance rebuilds the projection on a cold start**, since it is not
  shared. Sharing it as extents is a later decision.

Amends **0107** (the report graph's read rule), **0119** (`validation-behind`,
and `graph-not-writable` as a validator type), **0130** point 5 (a computed
system graph) and **0155** (the additive members), each by a dated amendment.

**Amended 2026-10-27** (ADR 0068), by #146 under [ADR 0213](0213-cached-derived-data-is-verified-before-use.md): the projection's snapshot is written in the checked envelope; see the end. Filed unaccepted.

## Context

ADR 0169 gates a commit on the focus nodes its delta affects, and ADR 0173
attaches a digest of that slice to the commit, with the projection named as
the reference that holds the whole report. ADR 0171's comparison says
which (shape, focus node) pairs a delta affects, and that every other pair
keeps its results. A report at every position is therefore a function of the
log, maintained commit by commit, and the brief's "the log is the source of
truth and every index is a projection of it" says where it belongs: in a
projection (spec §7, ADR 0016).

## Decision

1. **One projection per binding.** `ValidationProjection` in
   `Varve.Shacl.Store` (layer 5) is an `IProjection` over one
   `ShapesBinding`. It is fed the dataset's closed commits in order, by
   `CatchUpAsync` or a subscription, and reads each position as of
   `AsOfAsync`.
2. **Rows.** A row is one (shape, focus node) pair, where the shape is the
   one that targets the node, not a nested shape a result names as its
   source. It holds that pair's results with a validity interval
   `[asserted, retracted)`, in ADR 0138's style. A pair without results has
   no row.
3. **A data commit replaces the rows of the pairs it affects.**
   `IncrementalValidation.Compare` gives every affected pair with its results
   after the delta (`IncrementalComparison.Pairs`). Each affected pair's open
   row is closed at the commit, and a new one opened when it has results.
   Every other row stays open.
4. **Generations.** A commit that changes the shapes graph starts a
   generation, and so does the first commit after a shapes graph that did
   not load or a validation that failed. A generation is a full validation by
   pair at that position (`ShaclValidator.ValidateByPair`), which replaces
   every open row. A comparison that fails is replaced by a generation too.
   - **Published whole.** The projection's position moves to a commit only
     once the commit is applied, generation included, so a reader never sees
     a mixed report. The incremental cost of a shapes change, re-evaluating
     only the shapes that changed, is not taken here: a generation is the
     full validation, and the property below is the guarantee either way.
   - **A failed generation** is served as its problems. The report graph is
     then empty, and the binding's description says why.
5. **The report at p** is the composition of the rows valid at p under the
   generation's header (`ValidationReport.Compose`). **It equals
   `BindingValidation.Validate` over `AsOfAsync(p)`, byte for byte in
   canonical N-Quads.** `BindingValidation` is the binding's full validation
   as of one state: the shapes graph as of the state, against the union of
   the bound graphs without the shapes graph and the `varve:` graphs. The
   gate and REC's standalone check use the same function.
6. **ADR 0173's slice.** The projection's rows at p, restricted to a commit's
   affected pairs, give the commit's attachment's digest. A property holds it.
7. **Storage: the local cache only.**
   - `SaveAsync` writes the state as one blob, under
     `validation/{enc(binding)}/state-{position}` in an `IDerivedStore` the
     host supplies. Its position is in the same blob as its rows, so the two
     are atomic, as `IProjection` requires. Older states are deleted.
     `LoadAsync` reads the newest.
   - **A snapshot identifies its binding**: the IRI, the shapes graph, the
     graphs, the prefixes, the default graph and the subclass flag, under a
     format version. It is read only when every one matches and its position
     is at or below the head; otherwise it is ignored, and the projection is
     rebuilt from the log. A release that changes SHACL results (ADR 0173
     point 4) bumps the format.
   - **The server** passes a file dataset's own `derived/`, which is on the
     machine. A memory or object-storage dataset has no local derived store
     the server can give, so its projection is kept in memory. It is never
     written to an object store.
   - **A cold start begins at the head.** A projection with no saved state,
     on a dataset with history, is reset to a view of the head
     (`IProjection.ResetAsync`): one generation there, which is its floor,
     and it follows the log from then on. Replaying the history commit by
     commit would read each position as of, and an as-of read costs its
     distance from a checkpoint or its extent set's size (ADR 0138), so that
     replay grows with the square of the history. **Below the floor**
     nothing is held, and a read of the report there is the binding's full
     validation as of that position, computed when it is read: correct, and
     at a full validation's cost.
   - **A projection that keeps up** reads each commit at the head, through
     the index (`Dataset.Pin`), and reads it as of only when it is behind.
8. **The reserved graph.** The report at a position is readable as
   `https://w3id.org/varve/ns#validation/{enc(binding IRI)}`, `enc`
   percent-encoding every byte outside RFC 3986's unreserved set, as the
   catalogue graphs do.
   - **The seam.** `Varve.Protocol` cannot reference `Varve.Shacl.Store`,
     since both are at layer 5. It defines `IValidationReports`, which the
     host implements over its projections. Each report graph names its
     binding, its graph and the graphs it validates, gives its report at a
     position it has reached, and gives it after one bounded wait.
   - **Composed at a pinned position.** A read's view is scoped over the
     view's staging view (ADR 0058), and a report is added as an overlay
     whose terms are staged, so the dictionary is never touched. It is read
     the first time the read names the report graph or matches every named
     graph, and then once. A read that never needs a report never reads one,
     and never waits.
   - **`validation-behind`.** A report not reached after the wait is
     `503 validation-behind`, with `binding`, `position` and
     `projectedPosition`, and `Retry-After: 1`. The server's wait is two
     seconds. A query that streams has its cut after the first byte as a
     `Varve-Error` trailer, as every read limit does (ADR 0095).
   - **A report graph has no graph version in the log.** The Graph Store
     tags it with the position it was read at.
9. **Who reads it** (ADR 0107, amended): a caller who reads every graph the
   binding validates. That means every graph, the default graph when the
   binding names it, each named graph, and each prefix under a prefix of the
   caller's own. Any other caller sees no such graph.
10. **Nobody writes it** (ADR 0130 point 5, amended): `ReportGraphGuard`, a
    dataset validator in `Varve.Protocol`, refuses a commit with a quad in
    any graph under `https://w3id.org/varve/ns#validation/` as
    `graph-not-writable`. That holds on every write path, whoever writes it,
    an administrator included. The server puts it on every dataset.
11. **Readiness** reports each binding's lag, head minus projection position,
    under its dataset's line as `validation`. The lag never makes the dataset
    unready or "behind".

## Alternatives considered

- **A row per result.** The plan's first wording. Keying by targeted pair
  makes a commit's change a replacement of the rows ADR 0171 names, and
  makes ADR 0173's slice a selection of rows. A row per result would need a
  multiset difference per commit to say the same.
- **Generations built in the background, beside the old one.** The plan's
  wording. A generation is applied within the commit's `ApplyAsync`, and the
  position is published after it. That gives the same guarantee — reads
  above the old position wait or are `validation-behind`, reads below are
  served — without a second set of rows.
- **Report graphs stored in the log.** They would be written by every
  commit, twice the report, and could not be rebuilt under a new engine.
- **A dataset made unready by a lagging projection.** The maintainer's
  ruling: only the reads that need the report are refused.
- **The state on the object store**, shared by the fleet. A later decision,
  as extents.
- **A local derived store for object-storage datasets**, a directory under
  the cache. It would need a public file-backed derived store in
  `Varve.Store`. Not taken in this milestone: those datasets rebuild on start.

## Consequences

- **Every instance rebuilds every projection on a cold start**: one full
  validation at the head per binding, the rebuild time the benchmark reports
  at REC M. A file dataset's warm restart reads its saved state instead.
- **Reads below a projection's floor** cost a full validation each. They are
  the history before the instance first started the projection, read as of.
- **The report at every position is held in memory**, as rows whose
  intervals cover the whole log. A long history with results that change
  often grows it. Compaction below a horizon is the archive's (milestone
  10).
- **A read that matches every named graph** (`GRAPH ?g`) reads every report
  the caller may read, and is `validation-behind` while one is behind.
- `docs/vocabulary.md` lists the report graphs, and `docs/operator/shapes.md`
  describes the projection, its lag and its cost.

## Checks

- **Checked against the accepted ADRs** (0001–0173). Touches **0016** and
  spec §7 (an ordinary projection), **0017** (the guard is a dataset
  validator), **0058** (the staging view's provisional handles), **0095**
  (the trailer after the first byte), **0107**, **0119**, **0130** and
  **0155** (amended), **0138** (validity intervals), **0169** to **0173**.
  No conflict.
- **Layer ownership.** `Varve.Shacl` (3) gives results per pair, composes
  and serialises reports. `Varve.Shacl.Store` (5) holds the projection.
  `Varve.Protocol` (5) holds the seam, the composition and the guard. The
  server (6) composes them.
- **Analyzer rule.** None. `GraphScope.Prefixes` and the composition's
  per-read path cite this ADR's decisions.
- **Open questions owned.** Sharing the projection as extents, owned by the
  object storage track. A local derived store for object-storage datasets,
  owned by milestone 8's follow-up.

## Amendment, 2026-10-27 — the snapshot is a checked envelope

Filed by #146, unaccepted until the maintainer accepts it (ADR 0066); ruled
in [ADR 0213](0213-cached-derived-data-is-verified-before-use.md) point 6.

- **The projection's snapshot is written through `CheckedBlob`**, kind 9,
  in 64 KiB blocks each checked. It is bound to the dataset, and to the
  position and header hash it was built at.
- **What counts as a miss:** a snapshot that does not verify, one bound to
  another dataset or another commit, or one whose decoding raises any
  exception at all. Each is a miss, and the projection rebuilds from the
  log, as on a cold start.
- **A `VVP1` snapshot is a miss** the first time this build opens it.
