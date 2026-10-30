# 0233 — The classification projection

## Status

**Filed unaccepted by milestone 9, part 1 (#202, under #14), 2026-10-30**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
on the plan in #14 (2026-10-30), question 3. Its shape follows ADR
[0174](0174-the-validation-projection.md) on purpose.

Records decision set
[`the-classification-projection`](../decisions/the-classification-projection.md).

## Context

ADR 0230 point 6 makes classification a function of the state and the
binding. That makes it a projection (spec §7), and lets it work with erasure
mode off. Every read by a row-restricted caller needs it at the read's
position, and so does every write by one.

## Decision

1. **One projection per classifier binding.** `ClassificationProjection` in
   `Varve.Classification` is an `IProjection` (ADR 0016), fed the dataset's
   closed commits in order.
2. **Rows.** A row is a node handle with its interned subject-set id and a
   validity interval `[asserted, retracted)`, in ADR 0138's style. An
   unclassified node has no row.
3. **A data commit** applies ADR 0232 point 5. Each affected node whose set
   changed has its open row closed at the commit, and a new row opened when
   its set is not empty. **The nodes whose set changed at p are kept**, so
   the feed (ADR 0237) can name the quads whose visibility changed.
4. **Generations.** The following start a generation: a commit that touches
   the shapes graph; the first commit after a shapes graph that did not
   load; a change of the binding in force (ADR 0230 point 6). A generation
   is a classification from scratch at that position, replacing every open
   row, and its position is published only once it is applied, so a reader
   never sees a mixed classification.
5. **`At(p)`** answers an `IRowClassification` at position p (ADR 0231).
   - **Above the floor**, it serves the rows valid at p.
   - **Below the floor**, it classifies from scratch over `AsOfAsync(p)`:
     correct, at a full classification's cost.
   - **The floor** is where a cold start resets the projection, at the head.
     That is ADR 0174 point 7, because replaying history as-of would cost
     the square of it.
6. **Storage: the local cache only.**
   - The state is one blob in the host's `IDerivedStore`, under
     `classification/{enc(binding)}/state-{position}`, with its position.
   - A snapshot identifies its binding (IRI, shapes graph and graph set)
     under a format version, and is read only when every one matches and its
     position is at or below the head. **It is verified in full on read**:
     a length, and a SHA-256 over the whole blob.
   - **A blob that does not verify is a miss.** It is ignored and the
     projection is rebuilt, never served wrong. That is #146's rule. When
     ADR 0213's checked envelope lands (#180), the blob moves to it.
   - A memory or object-storage dataset keeps the state in memory.
7. **Readiness and waiting.**
   - Each binding's lag (head minus position) is reported on readiness
     under its dataset's line as `classification`, and **never makes the
     dataset unready**.
   - **A read by a row-restricted caller** at a position the projection has
     not reached waits once. The server's wait is two seconds. After it,
     the read is `503 classification-behind`, with `binding`, `position`,
     `projectedPosition` and `Retry-After: 1`.
   - **An unrestricted caller never reads the projection, and never waits.**
8. **Writes** read the projection at the pinned position for the
   before-state, and the binding's `IClassifier` over the staging view for
   the after-state (ADR 0236).

## Properties

- **Incremental equals from scratch** at every position, over generated
  histories and generated annotated shapes, including link chains, cycles,
  retracted links, retracted types and shapes changes mid-history.
- **As-of** reads equal from scratch above and below the floor.
- **The cache-damage gate**: a blob with any byte changed, truncated or
  extended is a miss, never a wrong row.

## Alternatives considered

- **Classification stored in the log.** It would be written by every
  commit, and could not change with a new classifier. Erasure mode records
  key ids, and only for private occurrences (#203).
- **A synchronous projection**, as the default one is. Every commit would pay
  classification, including on datasets whose readers are all unrestricted.
  The wait is paid only by the reads that need it.
- **A dataset unready while classification lags.** The same ruling as ADR
  0174's: only the reads that need it are refused.

## Consequences

- **A cold start classifies the head once per binding.** The benchmark
  reports it at REC M.
- **Reads below the floor** by row-restricted callers cost a full
  classification each.
- **`docs/operator/row-level-access.md`** describes the projection, its lag
  and its cost.

## Checks

- **Checked against the accepted ADRs** (0001–0190). Touches:
  - **0016**: an ordinary projection;
  - **0138**: validity intervals;
  - **0174**: the same shape;
  - **0119**: a new problem type, by amendment.

  No conflict.
- **Layer ownership.** `Varve.Classification` (5); the server (6) feeds it.
- **Analyzer rule.** None.
- **Open questions owned.** Sharing the state as extents, with ADR 0174's.
