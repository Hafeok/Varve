# 0237 — Rows on every surface

## Status

**Filed unaccepted by milestone 9, part 1 (#202, under #14), 2026-10-30**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the plan in #14 (2026-10-30), question 5 and decision 4.
Per-result filtering of the report is #204. **Amends ADRs
[0174](0174-the-validation-projection.md)** (point 9) and
**[0173](0173-the-validation-report-on-the-commit.md)** (the attachment's
delivery), by dated amendments in those files.

Records decision set
[`rows-on-every-surface`](../decisions/rows-on-every-surface.md).

## Context

ADR 0230 decides which quads a caller sees. Every surface that answers with
quads, or with something drawn from them, must agree with it. That is
ADR 0107 point 7's second property and ADR 0190 point 8's, extended to rows.

## Decision

1. **The query, `GRAPH ?g`, `FROM`/`FROM NAMED` and property paths** are
   evaluated over ADR 0235's composition. A path crosses only quads the
   caller sees, so it never reaches through a hidden row.
2. **Graph Store `GET`** answers the quads of the graph the caller sees. A
   readable graph whose quads are all hidden is an empty graph, not a `404`:
   a reader of the graph was entitled to know it exists. Its tag is the
   graph's version, as before. The version is a position, and says nothing
   about rows.
3. **The service description** lists the readable graphs as before.
4. **The change feed and the diff** deliver the delta of the caller's
   **view**, not the commit's delta filtered.
   - **The view** V_p is the set of quads the caller sees at p.
   - **The commit's delivered delta** is (V_p \ V_{p−1}, V_{p−1} \ V_p).
   - **It is computed from** the commit's own delta, judged by
     about_p for additions and about_{p−1} for retractions, together with the
     quads of the nodes whose subject set changed at p (ADR 0233 point 3).
     Those quads are found in the rows of those nodes and in the quads that
     name them as objects, in both states.
   - **A commit whose delivered delta is empty is skipped**, and the next
     delivered commit carries its true position (spec §8).
   - **`Settings` and `Erasure` commits** go to admin only, as before.
   - **The property**: folding a caller's feed from position 0 equals its
     view at every position.
   - **The diff (P₁, P₂)** is (V_{P₂} \ V_{P₁}, V_{P₁} \ V_{P₂}).
5. **`/commits/{p}`** answers the commit's own delta, each quad judged as in
   point 4: additions by about_p, retractions by about_{p−1}. Its metadata
   is unchanged.
6. **The validation report graph is withheld whole** from a caller whose
   readable subject set is not `all` for a binding whose Γ_B meets the
   graphs a shapes binding validates. ADR 0174 point 9 is amended:
   - such a caller sees no such graph;
   - the Graph Store answers `404`;
   - ADR 0173's attachment is not delivered to it on the commits resource,
     the feed or the diff.

   The reasons:
   - `sh:conforms` speaks for every row;
   - a result about a visible focus can depend on a hidden row, as
     `sh:class` on a value node does.

   Filtering per result, for row-local shapes, is #204.
7. **Inference** is ADR 0238's.
8. **As-of reads.**
   - The classification is the one at the read's position (ADR 0233 point 5).
   - The grants and their subject sets are those at the management position
     the read is served under (ADR 0165), as for graph scopes.
   - The rule forms (`subjectClass`, `subjectLink`) are evaluated over the
     data at the read's position.
9. **The Web UI** (#96) is a client of these routes and inherits every point
   above. `explain` (#171) follows ADR 0238.
10. **The access route** (ADR 0231 point 4) is admin-only, so it sees every
    row.
11. **A tier-1 follower** (ADR 0164) keeps its own classification projection
    from its log. Classification is derived, so it computes the same
    assignment at the same position. The gate runs a row-restricted
    caller's queries, Graph Store `GET`, feed and diff on the writer and on
    a tier-1 follower, at the follower's reported position, and compares.

## The property

ADR 0230 point 7, over every surface above, with:
- the feed's fold;
- the diff over generated pairs of positions;
- as-of reads at generated positions;
- the follower.

## Alternatives considered

- **The feed as the commit's delta filtered by about-set.** Quads that
  change visibility without changing (`alice a ex:Person` retracted) would
  never reach a consumer that keeps the caller's view. The fold would
  differ from the query.
- **Report results withheld one by one now.** That is #204, for row-local
  shapes, with its own argument for `sh:conforms`.
- **`404` for a readable graph whose quads are all hidden.** The caller reads
  the graph, so its existence was never secret to it, and the answer would
  vary with rows it cannot see.

## Consequences

- **A feed consumer under a row restriction** receives quads its view gains
  by reclassification, as additions, and quads it loses, as retractions,
  even when no quad of the dataset changed.
- **The feed's cost** for a row-restricted caller includes the rows of the
  nodes reclassified at each position.

## Checks

- **Checked against the accepted ADRs** (0001–0190) and specification 1.8.
  Touches:
  - **0095**: reads;
  - **0097** and **0107** point 5: the feed;
  - **0164**: the follower;
  - **0165**: positions;
  - **0173** and **0174**: amended;
  - spec §8: true positions.

  No conflict.
- **Layer ownership.** `Varve.Protocol` (5): the surfaces. The server (6):
  the composition.
- **Analyzer rule.** None.
- **Open questions owned.** Per-result report filtering, #204.
